using System.ComponentModel.DataAnnotations;

namespace Lucky5.Application.Contracts;

using Lucky5.Application.Dtos;

public interface IAgentService
{
    Task<IReadOnlyList<AgentDto>> GetAgentsAsync(CancellationToken cancellationToken);
    Task<AgentDto> CreateAgentAsync(CreateAgentRequest request, CancellationToken cancellationToken);
    Task<AgentDto> LoadCreditAsync(int agentId, decimal amount, CancellationToken cancellationToken);
    Task AssignUserToAgentAsync(Guid userId, int agentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminUserDto>> GetUsersByAgentAsync(int agentId, CancellationToken cancellationToken);
}

public sealed record CreateAgentRequest(
    [Required]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Agent Name must be between 3 and 50 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9\s._\-()]+$", ErrorMessage = "Agent Name must contain alphanumeric characters, spaces, or simple dashes/dots/underscores/parens.")]
    string Name,

    [Required]
    [StringLength(10, MinimumLength = 2, ErrorMessage = "Agent Code must be between 2 and 10 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9]+$", ErrorMessage = "Agent Code must contain alphanumeric characters only.")]
    string Code,

    [Required]
    [StringLength(20, MinimumLength = 5, ErrorMessage = "Phone Number must be between 5 and 20 characters.")]
    [RegularExpression(@"^\+?[0-9\s\-()]{5,20}$", ErrorMessage = "Phone Number has an invalid format.")]
    string PhoneNumber);
