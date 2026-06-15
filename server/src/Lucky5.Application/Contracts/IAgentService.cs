namespace Lucky5.Application.Contracts;

using Lucky5.Application.Dtos;

public interface IAgentService
{
    Task<IReadOnlyList<AgentDto>> GetAgentsAsync(CancellationToken cancellationToken);
    Task<AgentDto> CreateAgentAsync(CreateAgentRequest request, CancellationToken cancellationToken);
    Task<AgentDto> LoadCreditAsync(int agentId, decimal amount, CancellationToken cancellationToken);
    Task AssignUserToAgentAsync(Guid userId, int agentId, CancellationToken cancellationToken);
}

public sealed record CreateAgentRequest(string Name, string Code, string PhoneNumber);
