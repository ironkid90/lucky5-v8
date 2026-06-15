namespace Lucky5.Application.Contracts;

using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;

public interface ICabinetDeviceAuthService
{
    Task<CabinetDeviceProvisioningDto> ProvisionDeviceAsync(Guid adminId, ProvisionCabinetDeviceRequest request, CancellationToken cancellationToken);
    Task<CabinetDeviceAuthResultDto> AuthenticateAsync(CabinetDeviceAuthRequest request, CancellationToken cancellationToken);
    Task<CabinetDeviceAuthContext?> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken);
    Task<CabinetDeviceDto> RevokeDeviceAsync(Guid adminId, Guid deviceId, RevokeCabinetDeviceRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<CabinetDeviceDto>> ListDevicesAsync(CancellationToken cancellationToken);
    Task<CabinetDeviceDto> GetDeviceAsync(Guid deviceId, CancellationToken cancellationToken);
}

public sealed record CabinetDeviceAuthContext(
    Guid DeviceId,
    int MachineId,
    string DisplayName,
    string SerialNumber);