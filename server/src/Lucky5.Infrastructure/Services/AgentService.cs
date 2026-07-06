namespace Lucky5.Infrastructure.Services;

using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Interfaces;
using Lucky5.Domain.Entities;
using System.Collections.Concurrent;

public sealed class AgentService(IDataStore store) : IAgentService
{
    private static int _nextId = 1;
    private static readonly ConcurrentDictionary<int, Agent> _agents = new();

    public Task<IReadOnlyList<AgentDto>> GetAgentsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AgentDto> list = _agents.Values
            .OrderBy(a => a.Id)
            .Select(ToDto)
            .ToArray();
        return Task.FromResult(list);
    }

    public Task<AgentDto> CreateAgentAsync(CreateAgentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length < 3 || request.Name.Length > 50 || !System.Text.RegularExpressions.Regex.IsMatch(request.Name, @"^[a-zA-Z0-9\s._\-()]+$"))
            throw new ArgumentException("Agent Name must be 3-50 characters and contain alphanumeric characters, spaces, or simple dashes/dots/underscores/parens.");

        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length < 2 || request.Code.Length > 10 || !System.Text.RegularExpressions.Regex.IsMatch(request.Code, @"^[a-zA-Z0-9]+$"))
            throw new ArgumentException("Agent Code must be 2-10 alphanumeric characters.");

        if (string.IsNullOrWhiteSpace(request.PhoneNumber) || !System.Text.RegularExpressions.Regex.IsMatch(request.PhoneNumber, @"^\+?[0-9\s\-()]{5,20}$"))
            throw new ArgumentException("Phone Number has an invalid format (must be 5 to 20 digits with optional +/spaces/dashes/parens).");

        if (_agents.Values.Any(a => a.Code.Equals(request.Code, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Agent code '{request.Code}' already exists");

        var id = Interlocked.Increment(ref _nextId);
        var agent = new Agent
        {
            Id = id,
            Name = request.Name,
            Code = request.Code,
            PhoneNumber = request.PhoneNumber
        };
        _agents[id] = agent;
        return Task.FromResult(ToDto(agent));
    }

    public Task<AgentDto> LoadCreditAsync(int agentId, decimal amount, CancellationToken cancellationToken)
    {
        if (!_agents.TryGetValue(agentId, out var agent))
            throw new KeyNotFoundException($"Agent {agentId} not found");
        if (amount <= 0 || amount > 10000000m)
            throw new ArgumentException("Load credit amount must be between 0.01 and 10,000,000.");
        agent.CreditPool += amount;
        return Task.FromResult(ToDto(agent));
    }

    public async Task AssignUserToAgentAsync(Guid userId, int agentId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID cannot be an empty GUID.");

        var profile = await store.GetProfileAsync(userId)
            ?? throw new KeyNotFoundException("User profile not found");
        if (!_agents.ContainsKey(agentId))
            throw new KeyNotFoundException($"Agent {agentId} not found");

        profile.AgentId = agentId;
        await store.UpdateProfileAsync(profile);
    }

    private static AgentDto ToDto(Agent a) =>
        new(a.Id, a.Name, a.Code, a.PhoneNumber, a.IsActive, a.CreditPool, a.CreatedUtc);
}
