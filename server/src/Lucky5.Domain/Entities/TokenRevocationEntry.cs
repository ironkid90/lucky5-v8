namespace Lucky5.Domain.Entities;

public sealed class TokenRevocationEntry
{
    public string TokenHash { get; set; } = string.Empty;
    public DateTime RevokedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; }
    public Guid? UserId { get; set; }
}