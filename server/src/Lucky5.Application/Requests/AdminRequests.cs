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

public sealed record SetDoorStateRequest(
    DoorState DoorState);

public sealed record RechargeBonusRequest(
    [Required]
    Guid UserId,

    [Required]
    [Range(0.01, 100000000.00, ErrorMessage = "Recharge amount must be between 0.01 and 100,000,000.")]
    decimal RechargeAmount);
