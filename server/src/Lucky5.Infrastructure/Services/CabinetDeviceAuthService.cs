namespace Lucky5.Infrastructure.Services;

using System.Security.Cryptography;
using System.Text;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Lucky5.Domain.Entities;

public sealed class CabinetDeviceAuthService(InMemoryDataStore store, IAdminAuditService auditService) : ICabinetDeviceAuthService
{
    private const int SecretBytes = 32;
    private const int TokenBytes = 32;
    private static readonly Guid SystemAuditActorId = Guid.Parse("00000000-0000-0000-0000-000000000009");
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(12);

    public async Task<CabinetDeviceProvisioningDto> ProvisionDeviceAsync(Guid adminId, ProvisionCabinetDeviceRequest request, CancellationToken cancellationToken)
    {
        if (adminId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Admin role required");
        }

        if (!store.Machines.TryGetValue(request.MachineId, out var machine))
        {
            throw new KeyNotFoundException("Machine not found");
        }

        var displayName = Normalize(request.DisplayName, $"Cabinet {machine.Id}");
        var serialNumber = Normalize(request.SerialNumber, machine.MachineSerial);
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            throw new InvalidOperationException("Serial number is required");
        }

        if (store.CabinetDevices.Values.Any(device => device.SerialNumber.Equals(serialNumber, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Cabinet device serial number already exists");
        }

        var secret = GenerateOpaqueValue("l5cabsec", SecretBytes);
        var device = new CabinetDevice
        {
            MachineId = request.MachineId,
            DisplayName = displayName,
            SerialNumber = serialNumber,
            DeviceSecretHash = HashValue(secret),
            SecretFingerprint = Fingerprint(secret),
            CreatedUtc = DateTime.UtcNow,
            CreatedByAdminId = adminId
        };

        store.CabinetDevices[device.Id] = device;

        await auditService.AppendAsync(new AdminAuditWriteDto(
            adminId,
            "admin",
            "cabinet_device.provision",
            "cabinet_device",
            device.Id.ToString("N"),
            MachineId: device.MachineId,
            CabinetDeviceId: device.Id,
            Metadata: new Dictionary<string, string>
            {
                ["display_name"] = device.DisplayName,
                ["serial_number"] = device.SerialNumber,
                ["secret_fingerprint"] = device.SecretFingerprint
            }), cancellationToken);

        return new CabinetDeviceProvisioningDto(ToDto(device), secret);
    }

    public async Task<CabinetDeviceAuthResultDto> AuthenticateAsync(CabinetDeviceAuthRequest request, CancellationToken cancellationToken)
    {
        if (!store.CabinetDevices.TryGetValue(request.DeviceId, out var device))
        {
            await WriteAuthFailureAuditAsync(request.DeviceId, null, "unknown_device", cancellationToken);
            throw new UnauthorizedAccessException("Invalid cabinet device credentials");
        }

        if (device.IsRevoked)
        {
            await WriteAuthFailureAuditAsync(request.DeviceId, device.MachineId, "revoked_device", cancellationToken);
            throw new UnauthorizedAccessException("Cabinet device revoked");
        }

        if (!FixedTimeEquals(device.DeviceSecretHash, HashValue(request.DeviceSecret)))
        {
            await WriteAuthFailureAuditAsync(request.DeviceId, device.MachineId, "invalid_secret", cancellationToken);
            throw new UnauthorizedAccessException("Invalid cabinet device credentials");
        }

        var now = DateTime.UtcNow;
        var accessToken = GenerateOpaqueValue("l5cab", TokenBytes);
        var session = new CabinetDeviceSession
        {
            DeviceId = device.Id,
            AccessTokenHash = HashValue(accessToken),
            IssuedUtc = now,
            ExpiresUtc = now.Add(AccessTokenLifetime),
            LastSeenUtc = now,
            FirmwareVersion = Normalize(request.FirmwareVersion, string.Empty),
            ClientVersion = Normalize(request.ClientVersion, string.Empty)
        };

        store.CabinetDeviceSessions[session.Id] = session;
        device.LastAuthenticatedUtc = now;
        device.LastSeenUtc = now;
        device.LastFirmwareVersion = session.FirmwareVersion;
        device.LastClientVersion = session.ClientVersion;

        return new CabinetDeviceAuthResultDto(accessToken, session.ExpiresUtc, ToDto(device));
    }

    public Task<CabinetDeviceAuthContext?> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Task.FromResult<CabinetDeviceAuthContext?>(null);
        }

        var tokenHash = HashValue(accessToken);
        var now = DateTime.UtcNow;
        var session = store.CabinetDeviceSessions.Values.FirstOrDefault(candidate => FixedTimeEquals(candidate.AccessTokenHash, tokenHash));
        if (session is null || !session.IsActive(now))
        {
            return Task.FromResult<CabinetDeviceAuthContext?>(null);
        }

        if (!store.CabinetDevices.TryGetValue(session.DeviceId, out var device) || device.IsRevoked)
        {
            return Task.FromResult<CabinetDeviceAuthContext?>(null);
        }

        session.LastSeenUtc = now;
        device.LastSeenUtc = now;

        return Task.FromResult<CabinetDeviceAuthContext?>(new CabinetDeviceAuthContext(device.Id, device.MachineId, device.DisplayName, device.SerialNumber));
    }

    public async Task<CabinetDeviceDto> RevokeDeviceAsync(Guid adminId, Guid deviceId, RevokeCabinetDeviceRequest request, CancellationToken cancellationToken)
    {
        if (adminId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Admin role required");
        }

        if (!store.CabinetDevices.TryGetValue(deviceId, out var device))
        {
            throw new KeyNotFoundException("Cabinet device not found");
        }

        var now = DateTime.UtcNow;
        var reason = Normalize(request.Reason, "revoked by admin");
        device.IsRevoked = true;
        device.RevokedUtc ??= now;
        device.RevokedByAdminId ??= adminId;
        device.RevocationReason = reason;

        foreach (var session in store.CabinetDeviceSessions.Values.Where(session => session.DeviceId == deviceId && session.RevokedUtc is null))
        {
            session.RevokedUtc = now;
        }

        await auditService.AppendAsync(new AdminAuditWriteDto(
            adminId,
            "admin",
            "cabinet_device.revoke",
            "cabinet_device",
            device.Id.ToString("N"),
            MachineId: device.MachineId,
            CabinetDeviceId: device.Id,
            Reason: reason,
            Metadata: new Dictionary<string, string>
            {
                ["display_name"] = device.DisplayName,
                ["serial_number"] = device.SerialNumber
            }), cancellationToken);

        return ToDto(device);
    }

    public Task<IReadOnlyList<CabinetDeviceDto>> ListDevicesAsync(CancellationToken cancellationToken)
    {
        var devices = store.CabinetDevices.Values
            .OrderBy(device => device.MachineId)
            .ThenBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto)
            .ToArray();

        return Task.FromResult<IReadOnlyList<CabinetDeviceDto>>(devices);
    }

    public Task<CabinetDeviceDto> GetDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        if (!store.CabinetDevices.TryGetValue(deviceId, out var device))
        {
            throw new KeyNotFoundException("Cabinet device not found");
        }

        return Task.FromResult(ToDto(device));
    }

    private CabinetDeviceDto ToDto(CabinetDevice device)
    {
        var machineName = store.Machines.TryGetValue(device.MachineId, out var machine) ? machine.Name : string.Empty;
        var now = DateTime.UtcNow;
        var activeSessionCount = store.CabinetDeviceSessions.Values.Count(session => session.DeviceId == device.Id && session.IsActive(now));
        return new CabinetDeviceDto(
            device.Id,
            device.MachineId,
            machineName,
            device.DisplayName,
            device.SerialNumber,
            device.SecretFingerprint,
            device.CreatedUtc,
            device.CreatedByAdminId,
            device.LastAuthenticatedUtc,
            device.LastSeenUtc,
            device.LastFirmwareVersion,
            device.LastClientVersion,
            device.IsRevoked,
            device.RevokedUtc,
            device.RevokedByAdminId,
            device.RevocationReason,
            activeSessionCount);
    }

    private async Task WriteAuthFailureAuditAsync(Guid deviceId, int? machineId, string reason, CancellationToken cancellationToken)
    {
        await auditService.AppendAsync(new AdminAuditWriteDto(
            SystemAuditActorId,
            "cabinet_device",
            "cabinet_device.auth_failed",
            "cabinet_device",
            deviceId == Guid.Empty ? "unknown" : deviceId.ToString("N"),
            MachineId: machineId,
            CabinetDeviceId: deviceId == Guid.Empty ? null : deviceId,
            Outcome: "denied",
            Reason: reason), cancellationToken);
    }

    private static string GenerateOpaqueValue(string prefix, int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return $"{prefix}_{Convert.ToBase64String(bytes).Replace("+", "-", StringComparison.Ordinal).Replace("/", "_", StringComparison.Ordinal).TrimEnd('=')}";
    }

    private static string HashValue(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes);
    }

    private static string Fingerprint(string value)
        => HashValue(value)[..12];

    private static bool FixedTimeEquals(string leftHex, string rightHex)
    {
        var left = Encoding.UTF8.GetBytes(leftHex);
        var right = Encoding.UTF8.GetBytes(rightHex);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim();
    }
}