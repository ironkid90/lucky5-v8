namespace Lucky5.Domain.Entities;

public sealed class MachineSessionState
{
    public Guid SessionId { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public int MachineId { get; init; }
    public decimal MachineCredits { get; set; }
    public decimal TotalCashIn { get; set; }
    public bool IsMachineClosed { get; set; }
    public int CounterplayScore { get; set; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}
