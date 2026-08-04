namespace Lucky5.Domain.Entities;

public sealed class Machine
{
    public int Id { get; init; }
    public int GameId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string MachineSerial { get; init; } = string.Empty;
    public string MachineSerie { get; init; } = string.Empty;
    public string VariantState { get; set; } = "{}"; // JSON payload for variant-specific state (e.g., Kent)
    public bool IsOpen { get; set; } = true;
    public decimal MinBet { get; init; } = 1;
    public decimal MaxBet { get; init; } = 10;
    public decimal FirstRechargeCredit { get; init; } = 200_000m;
    public decimal SecondRechargeCredit { get; init; } = 500_000m;
    public decimal FirstRechargeBonus { get; init; } = 20_000m;
    public decimal SecondRechargeBonus { get; init; } = 75_000m;

    /// <summary>
    /// Bet increment for the Lebanese bet ramp counter. Credits tick up in these steps.
    /// Default: 100 credits per tick.
    /// </summary>
    public decimal BetIncrement { get; init; } = 100m;

    /// <summary>
    /// Tier-based machine close threshold. When machine credits reach this value,
    /// the machine freezes and the player must cash out.
    /// Tier 1 (2500-5000): 11M | Tier 2 (5000-10000): 22M | Tier 3 (10000-20000): 44M
    /// </summary>
    public decimal CloseThreshold => MinBet switch
    {
        >= 10000 => 44_000_000m,
        >= 5000 => 22_000_000m,
        _ => 11_000_000m
    };
}
