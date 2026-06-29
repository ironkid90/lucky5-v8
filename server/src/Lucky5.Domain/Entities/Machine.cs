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
}
