using System.ComponentModel.DataAnnotations;

namespace Lucky5.Application.Requests;

public sealed record CreateOfferRequest(
    [Required]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Title must be between 1 and 100 characters.")]
    string Title,

    [Required]
    [StringLength(500, ErrorMessage = "Description must not exceed 500 characters.")]
    string Description,

    [Required]
    [Range(0, 100000000.00, ErrorMessage = "Bonus amount must be between 0 and 100,000,000.")]
    decimal BonusAmount);

public sealed record UpdateOfferRequest(
    [Required]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Title must be between 1 and 100 characters.")]
    string Title,

    [Required]
    [StringLength(500, ErrorMessage = "Description must not exceed 500 characters.")]
    string Description,

    [Required]
    [Range(0, 100000000.00, ErrorMessage = "Bonus amount must be between 0 and 100,000,000.")]
    decimal BonusAmount);

public sealed record UpsertTermsRequest(
    [Required]
    [StringLength(20, MinimumLength = 1, ErrorMessage = "Version must be between 1 and 20 characters.")]
    string Version,

    [Required]
    string BodyMarkdown);

public sealed record UpsertAppSettingRequest(
    [Required]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Key must be between 1 and 100 characters.")]
    string Key,

    [Required]
    [StringLength(2000, ErrorMessage = "Value must not exceed 2000 characters.")]
    string Value);
