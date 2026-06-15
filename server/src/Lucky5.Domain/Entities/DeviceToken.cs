namespace Lucky5.Domain.Entities;

public sealed class DeviceToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public DateTime RegisteredUtc { get; init; } = DateTime.UtcNow;
}
