namespace Lucky5.Infrastructure.Services;

using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Interfaces;
using Lucky5.Domain.Entities;
using System.Collections.Concurrent;

public sealed class AgentService(IDataStore store, InMemoryDataStore inMemoryStore) : IAgentService
{
    private static int _nextId = 1;
    private static readonly ConcurrentDictionary<int, Agent> _agents = new();

    public async Task<IReadOnlyList<AgentDto>> GetAgentsAsync(CancellationToken cancellationToken)
    {
        // Load from persistent store; fall back to in-memory cache
        var persistentAgents = await store.GetAgentsAsync();
        foreach (var agent in persistentAgents)
        {
            _agents[agent.Id] = agent;
        }

        IReadOnlyList<AgentDto> list = _agents.Values
            .OrderBy(a => a.Id)
            .Select(ToDto)
            .ToArray();
        return list;
    }

    public async Task<AgentDto> CreateAgentAsync(CreateAgentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length < 3 || request.Name.Length > 50 || !System.Text.RegularExpressions.Regex.IsMatch(request.Name, @"^[a-zA-Z0-9\s._\-()]+$"))
            throw new ArgumentException("Agent Name must be 3-50 characters and contain alphanumeric characters, spaces, or simple dashes/dots/underscores/parens.");

        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length < 2 || request.Code.Length > 10 || !System.Text.RegularExpressions.Regex.IsMatch(request.Code, @"^[a-zA-Z0-9]+$"))
            throw new ArgumentException("Agent Code must be 2-10 alphanumeric characters.");

        if (string.IsNullOrWhiteSpace(request.PhoneNumber) || !System.Text.RegularExpressions.Regex.IsMatch(request.PhoneNumber, @"^\+?[0-9\s\-()]{5,20}$"))
            throw new ArgumentException("Phone Number has an invalid format (must be 5 to 20 digits with optional +/spaces/dashes/parens).");

        // Check for duplicate code in persistent store
        var existingAgent = await store.GetAgentByCodeAsync(request.Code);
        if (existingAgent is not null)
            throw new InvalidOperationException($"Agent code '{request.Code}' already exists");

        if (_agents.Values.Any(a => a.Code.Equals(request.Code, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Agent code '{request.Code}' already exists");

        var id = Interlocked.Increment(ref _nextId);
        var agent = new Agent
        {
            Id = id,
            Name = request.Name,
            Code = request.Code,
            PhoneNumber = request.PhoneNumber,
            CreditPool = 0,
            IsActive = true
        };

        await store.CreateAgentAsync(agent);
        _agents[id] = agent;

        // Automatically create a linked User entity with role="agent" so agent can log in
        var username = request.Code.ToLowerInvariant();
        if (!inMemoryStore.Users.Values.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
        {
            var user = new User
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Agent@12345"),
                PhoneNumber = request.PhoneNumber,
                Email = $"{username}@agent.lucky5.local",
                FullName = request.Name,
                Role = "agent",
                AgentId = agent.Id,
                IsOtpVerified = true
            };
            inMemoryStore.Users[user.Id] = user;
            inMemoryStore.Profiles[user.Id] = user;
            inMemoryStore.MemberProfiles[user.Id] = new MemberProfile
            {
                UserId = user.Id,
                Username = user.Username,
                DisplayName = user.Username,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                WalletBalance = 0m,
                Credit = 0m,
                TotalWins = 0,
                AgentId = agent.Id,
                GeneratedID = user.GeneratedID,
                LastSeenUtc = DateTime.UtcNow
            };
        }

        return ToDto(agent);
    }

    public async Task<AgentDto> LoadCreditAsync(int agentId, decimal amount, CancellationToken cancellationToken)
    {
        var agent = await store.GetAgentByIdAsync(agentId)
            ?? throw new KeyNotFoundException($"Agent {agentId} not found");

        if (amount <= 0 || amount > 10000000m)
            throw new ArgumentException("Load credit amount must be between 0.01 and 10,000,000.");

        agent.CreditPool += amount;
        await store.UpdateAgentAsync(agent);
        _agents[agentId] = agent;
        return ToDto(agent);
    }

    public async Task AssignUserToAgentAsync(Guid userId, int agentId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID cannot be an empty GUID.");

        var profile = await store.GetProfileAsync(userId)
            ?? throw new KeyNotFoundException("User profile not found");

        var agent = await store.GetAgentByIdAsync(agentId)
            ?? throw new KeyNotFoundException($"Agent {agentId} not found");

        profile.AgentId = agentId;
        await store.UpdateProfileAsync(profile);
    }

    public Task<IReadOnlyList<AdminUserDto>> GetUsersByAgentAsync(int agentId, CancellationToken cancellationToken)
    {
        var users = inMemoryStore.Users.Values
            .Where(u => u.AgentId == agentId)
            .Select(u =>
            {
                var profile = inMemoryStore.MemberProfiles.TryGetValue(u.Id, out var p) ? p : null;
                var agent = u.AgentId.HasValue ? _agents.GetValueOrDefault(u.AgentId.Value) : null;
                return new AdminUserDto(
                    u.Id,
                    u.Username,
                    profile?.DisplayName ?? u.Username,
                    u.PhoneNumber,
                    profile?.WalletBalance ?? 0m,
                    u.Role,
                    u.CreatedUtc,
                    profile?.LastSeenUtc ?? u.CreatedUtc,
                    u.Email,
                    u.FullName,
                    u.AgentId,
                    agent?.Name
                );
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<AdminUserDto>>(users);
    }

    private static AgentDto ToDto(Agent a) =>
        new(a.Id, a.Name, a.Code, a.PhoneNumber, a.IsActive, a.CreditPool, a.CreatedUtc);
}