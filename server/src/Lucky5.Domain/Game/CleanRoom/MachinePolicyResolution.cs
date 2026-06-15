namespace Lucky5.Domain.Game.CleanRoom;

public enum PolicyEnvelopeMode
{
    Recovery = 0,
    Neutral = 1,
    Pressure = 2,
    Cooldown = 3
}

public sealed record MachinePolicyTelemetry(
    bool IsWarmupActive,
    bool IsPityActive,
    bool IsCrisisActive,
    decimal BaseScale,
    decimal WarmupBias,
    decimal PityBoost,
    decimal JackpotLeakAdjustment,
    decimal DoubleUpLeakAdjustment,
    decimal EffectiveScale,
    PolicyEnvelopeMode EnvelopeMode,
    int RoundCount,
    int ConsecutiveLosses,
    int RoundsSinceMediumWin,
    decimal ObservedRtp,
    decimal TargetRtp);

/// <summary>
/// Unified policy result returned by MachinePolicy.ResolvePolicy.
///
/// Integration notes for the live repo:
/// 1. Replace deal-time calls to ResolvePayoutScale(...) with ResolvePolicy(...).
/// 2. Keep double-up availability visible, but feed policy telemetry into session DTOs.
/// 3. Preserve deterministic pre-shuffle behavior; all guardrails operate on distributions and future scaling only.
/// </summary>
public sealed record MachinePolicyResolution(
    decimal EffectiveScale,
    PolicyDistributionMode DistributionMode,
    PolicyEnvelopeMode EnvelopeMode,
    MachinePolicyTelemetry Telemetry)
{
    public decimal ForTier(PayoutTier tier, EngineConfig? config = null)
    {
        var cfg = config ?? EngineConfig.Default;
        return tier switch
        {
            PayoutTier.Small => decimal.Round(EffectiveScale * cfg.SmallTierFactor, 4),
            PayoutTier.Medium => decimal.Round(EffectiveScale * cfg.MediumTierFactor, 4),
            PayoutTier.Big => decimal.Round(EffectiveScale * cfg.BigTierFactor, 4),
            _ => EffectiveScale
        };
    }
}
