namespace Lucky5.Domain.Entities;

public sealed class CabinetDeviceSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DeviceId { get; init; }
    public string AccessTokenHash { get; init; } = string.Empty;
    public DateTime IssuedUtc { get; init; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; init; }
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedUtc { get; set; }
    public string FirmwareVersion { get; init; } = string.Empty;
    public string ClientVersion { get; init; } = string.Empty;

    public bool IsActive(DateTime utcNow) => RevokedUtc is null && ExpiresUtc > utcNow;
}