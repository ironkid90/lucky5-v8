namespace Lucky5.Domain.Entities;

public sealed class CabinetDevice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int MachineId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string DeviceSecretHash { get; set; } = string.Empty;
    public string SecretFingerprint { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public Guid CreatedByAdminId { get; init; }
    public DateTime? LastAuthenticatedUtc { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public string LastFirmwareVersion { get; set; } = string.Empty;
    public string LastClientVersion { get; set; } = string.Empty;
    public bool IsRevoked { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public Guid? RevokedByAdminId { get; set; }
    public string RevocationReason { get; set; } = string.Empty;
}