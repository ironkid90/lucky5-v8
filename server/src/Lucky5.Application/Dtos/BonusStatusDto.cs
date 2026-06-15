namespace Lucky5.Application.Dtos;

public sealed record BonusStatusDto(
    bool IsEligible,
    DateTime? LastClaimUtc,
    DateTime? NextEligibleUtc,
    int BonusRechargeCount);

public sealed record BonusClaimResultDto(
    decimal AmountAwarded,
    decimal NewCreditBalance,
    DateTime ClaimedAtUtc,
    int BonusRechargeCount);
