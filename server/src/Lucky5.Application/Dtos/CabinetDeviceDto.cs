namespace Lucky5.Application.Dtos;

public sealed record CabinetDeviceDto(
    Guid DeviceId,
    int MachineId,
    string MachineName,
    string DisplayName,
    string SerialNumber,
    string SecretFingerprint,
    DateTime CreatedUtc,
    Guid CreatedByAdminId,
    DateTime? LastAuthenticatedUtc,
    DateTime? LastSeenUtc,
    string LastFirmwareVersion,
    string LastClientVersion,
    bool IsRevoked,
    DateTime? RevokedUtc,
    Guid? RevokedByAdminId,
    string RevocationReason,
    int ActiveSessionCount);

public sealed record CabinetDeviceProvisioningDto(
    CabinetDeviceDto Device,
    string DeviceSecret);

public sealed record CabinetDeviceAuthResultDto(
    string AccessToken,
    DateTime ExpiresAtUtc,
    CabinetDeviceDto Device);