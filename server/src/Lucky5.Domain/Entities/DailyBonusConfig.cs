namespace Lucky5.Domain.Entities;

public sealed record DailyBonusConfig(
    int CooldownHours = 24,
    decimal MinAmount = 1_000m,
    decimal MaxAmount = 100_000m,
    int MaxDailyRecharges = 1)
{
    public static DailyBonusConfig Default { get; } = new();
}
