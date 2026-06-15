namespace Lucky5.Domain.Entities;

public sealed class CabinetStateCursor
{
    public Guid UserId { get; init; }
    public int MachineId { get; init; }
    public long StateVersion { get; set; }
    public long SequenceNumber { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}