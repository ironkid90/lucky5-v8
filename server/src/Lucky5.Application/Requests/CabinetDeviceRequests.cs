namespace Lucky5.Application.Requests;

public sealed record ProvisionCabinetDeviceRequest(
    int MachineId,
    string DisplayName,
    string SerialNumber);

public sealed record CabinetDeviceAuthRequest(
    Guid DeviceId,
    string DeviceSecret,
    string FirmwareVersion = "",
    string ClientVersion = "");

public sealed record RevokeCabinetDeviceRequest(
    string Reason);