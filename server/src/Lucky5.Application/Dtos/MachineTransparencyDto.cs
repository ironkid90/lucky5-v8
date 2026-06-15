namespace Lucky5.Application.Dtos;

public sealed record MachineTransparencyDto(
    bool IsWarmupActive,
    bool IsPityActive,
    bool IsCrisisActive,
    decimal BaseScale,
    decimal WarmupBias,
    decimal PityBoost,
    decimal JackpotLeakAdjustment,
    decimal DoubleUpLeakAdjustment,
    decimal EffectiveScale,
    string EnvelopeMode,
    int RoundCount,
    int ConsecutiveLosses,
    int RoundsSinceMediumWin,
    decimal ObservedRtp,
    decimal TargetRtp);
