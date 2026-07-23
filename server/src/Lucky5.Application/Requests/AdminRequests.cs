using System.ComponentModel.DataAnnotations;

namespace Lucky5.Application.Requests;

using Lucky5.Domain.Entities;

public sealed record AdminCreditRequest(
    [Required]
    Guid TargetUserId,

    [Required]
    [Range(-100000000.00, 100000000.00, ErrorMessage = "Amount must be between -100,000,000 and 100,000,000.")]
    decimal Amount,

    [Required]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Reason must be between 3 and 100 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9\s._\-()!]+$", ErrorMessage = "Reason must contain alphanumeric characters, spaces, or simple punctuation like dots, underscores, dashes, parens, exclamation.")]
    string Reason);

public sealed record AdminCreateUserRequest(
    [Required]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9]+$", ErrorMessage = "Username must be alphanumeric only.")]
    string Username,

    [Required]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    string Password,

    [Required]
    [StringLength(20, MinimumLength = 5, ErrorMessage = "Phone Number must be between 5 and 20 characters.")]
    [RegularExpression(@"^\+?[0-9\s\-()]{5,20}$", ErrorMessage = "Phone Number has an invalid format.")]
    string PhoneNumber,

    [StringLength(100, ErrorMessage = "Full Name must not exceed 100 characters.")]
    string? FullName = null,

    [EmailAddress(ErrorMessage = "Invalid email format.")]
    [StringLength(100, ErrorMessage = "Email must not exceed 100 characters.")]
    string? Email = null,

    [Required]
    [RegularExpression(@"^(player|agent|admin)$", ErrorMessage = "Role must be player, agent, or admin.")]
    string Role = "player",

    int? AgentId = null);

public sealed record AdminUpdateUserRequest(
    [Required]
    Guid UserId,

    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    string? Password = null,

    [StringLength(20, MinimumLength = 5, ErrorMessage = "Phone Number must be between 5 and 20 characters.")]
    [RegularExpression(@"^\+?[0-9\s\-()]{5,20}$", ErrorMessage = "Phone Number has an invalid format.")]
    string? PhoneNumber = null,

    [StringLength(100, ErrorMessage = "Full Name must not exceed 100 characters.")]
    string? FullName = null,

    [EmailAddress(ErrorMessage = "Invalid email format.")]
    [StringLength(100, ErrorMessage = "Email must not exceed 100 characters.")]
    string? Email = null,

    [RegularExpression(@"^(player|agent|admin)$", ErrorMessage = "Role must be player, agent, or admin.")]
    string? Role = null,

    int? AgentId = null);

public sealed record BulkAssignAgentRequest(
    [Required]
    [MinLength(1, ErrorMessage = "At least one user ID is required.")]
    IReadOnlyList<Guid> UserIds,

    [Required]
    int AgentId);

public sealed record SetDoorStateRequest(
    DoorState DoorState);

public sealed record RechargeBonusRequest(
    [Required]
    Guid UserId,

    [Required]
    [Range(0.01, 100000000.00, ErrorMessage = "Recharge amount must be between 0.01 and 100,000,000.")]
    decimal RechargeAmount);
