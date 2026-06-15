namespace Lucky5.Application.Dtos;

public sealed record AdminAuditEntryDto(
    Guid Id,
    long SequenceNumber,
    DateTime CreatedUtc,
    Guid ActorUserId,
    string ActorRole,
    string Action,
    string TargetType,
    string TargetId,
    int? MachineId,
    Guid? CabinetDeviceId,
    string Outcome,
    string Reason,
    IReadOnlyDictionary<string, string> Metadata);