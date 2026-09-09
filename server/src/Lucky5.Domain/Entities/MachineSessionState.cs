namespace Lucky5.Domain.Entities;

public sealed class MachineSessionState
{
    public Guid SessionId { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public int MachineId { get; init; }
    public decimal MachineCredits { get; set; }
    public decimal ReservedStake { get; set; }
    public Guid? ReservationId { get; set; }
    public DateTime? ReservationExpiresUtc { get; set; }
    public string? ReservationStatus { get; set; }
    public Dictionary<Guid, string> ReservationDealResults { get; set; } = new();
    public Dictionary<Guid, decimal> ReservationRequests { get; set; } = new();

    public decimal TotalCashIn { get; set; }
    public bool IsMachineClosed { get; set; }
    public int CounterplayScore { get; set; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}
