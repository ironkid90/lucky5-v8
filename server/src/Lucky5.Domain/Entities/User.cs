namespace Lucky5.Domain.Entities;

public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public bool IsOtpVerified { get; set; }
    public string? PendingOtp { get; set; }
    public DateTime? PendingOtpExpiresUtc { get; set; }
    public string Role { get; set; } = "player";
    public int? AgentId { get; set; }
    public string GeneratedID { get; set; } = Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
}
