namespace Lucky5.Domain.Entities;

public sealed class CabinetCommandRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public Guid CommandId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public string RequestHash { get; init; } = string.Empty;
    public string CommandType { get; init; } = string.Empty;
    public int MachineId { get; init; }
    public Guid? SessionId { get; init; }
    public long ExpectedStateVersion { get; init; }
    public bool Accepted { get; init; }
    public string Status { get; init; } = string.Empty;
    public long StateVersion { get; init; }
    public long SequenceNumber { get; init; }
    public string ResultJson { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime CompletedUtc { get; init; } = DateTime.UtcNow;
}