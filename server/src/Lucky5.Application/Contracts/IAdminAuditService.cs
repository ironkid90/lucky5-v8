namespace Lucky5.Application.Contracts;

using Lucky5.Application.Dtos;

public interface IAdminAuditService
{
    Task<AdminAuditEntryDto> AppendAsync(AdminAuditWriteDto write, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminAuditEntryDto>> ListRecentAsync(int take, CancellationToken cancellationToken);
}

public sealed record AdminAuditWriteDto(
    Guid ActorUserId,
    string ActorRole,
    string Action,
    string TargetType,
    string TargetId,
    int? MachineId = null,
    Guid? CabinetDeviceId = null,
    string Outcome = "succeeded",
    string Reason = "",
    IReadOnlyDictionary<string, string>? Metadata = null);