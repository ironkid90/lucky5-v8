namespace Lucky5.Domain.Entities;

public sealed class AdminAuditRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public long SequenceNumber { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public Guid ActorUserId { get; init; }
    public string ActorRole { get; init; } = "admin";
    public string Action { get; init; } = string.Empty;
    public string TargetType { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public int? MachineId { get; init; }
    public Guid? CabinetDeviceId { get; init; }
    public string Outcome { get; init; } = "succeeded";
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}