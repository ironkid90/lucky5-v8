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
        if (amount <= 0)
            throw new InvalidOperationException("Amount must be positive");
        agent.CreditPool += amount;
        return Task.FromResult(ToDto(agent));
    }

    public async Task AssignUserToAgentAsync(Guid userId, int agentId, CancellationToken cancellationToken)
    {
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
