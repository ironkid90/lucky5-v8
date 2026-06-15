namespace Lucky5.Domain.Entities;

public sealed class CabinetEventRecord
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public int MachineId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public long StateVersion { get; init; }
    public long SequenceNumber { get; init; }
    public string PayloadJson { get; init; } = "{}";
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
}