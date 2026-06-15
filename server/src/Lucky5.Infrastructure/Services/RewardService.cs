namespace Lucky5.Infrastructure.Services;

using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Interfaces;
using Lucky5.Domain.Entities;

public sealed class RewardService(IDataStore store) : IRewardService
{
    private static readonly DailyBonusConfig Config = DailyBonusConfig.Default;

    public async Task<BonusStatusDto> GetBonusStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await store.GetProfileAsync(userId)
            ?? throw new KeyNotFoundException("Profile not found");

        var isEligible = IsEligible(profile);
        var nextEligible = profile.BonusDate?.AddHours(Config.CooldownHours);

        return new BonusStatusDto(isEligible, profile.BonusDate, nextEligible, profile.BonusRechargeCount);
    }

    public async Task<BonusClaimResultDto> ClaimDailyBonusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await store.GetProfileAsync(userId)
            ?? throw new KeyNotFoundException("Profile not found");

        if (!IsEligible(profile))
        {
            var nextEligible = profile.BonusDate?.AddHours(Config.CooldownHours) ?? DateTime.UtcNow;
            throw new InvalidOperationException($"Daily bonus not available until {nextEligible:u}");
        }

        var amount = ComputeRewardAmount(userId);

        profile.Credit += amount;
        profile.BonusDate = DateTime.UtcNow;
        profile.BonusRechargeCount++;
        await store.UpdateProfileAsync(profile);

        await store.AddWalletLedgerEntryAsync(new WalletLedgerEntry
        {
            UserId = userId,
            Amount = amount,
            BalanceAfter = profile.Credit,
            TransactionType = "BonusClaim",
            ReferenceId = $"daily-bonus:{profile.BonusRechargeCount}",
            CreatedUtc = DateTime.UtcNow
        });

        return new BonusClaimResultDto(amount, profile.Credit, DateTime.UtcNow, profile.BonusRechargeCount);
    }

    private static bool IsEligible(MemberProfile profile)
    {
        if (profile.BonusDate is null) return true;
        return DateTime.UtcNow - profile.BonusDate.Value >= TimeSpan.FromHours(Config.CooldownHours);
    }

    private static decimal ComputeRewardAmount(Guid userId)
    {
        var hash = (uint)(userId.GetHashCode() ^ DateTime.UtcNow.DayOfYear);
        var range = Config.MaxAmount - Config.MinAmount;
        var fraction = (hash % 1000) / 1000m;
        return Math.Round(Config.MinAmount + range * fraction, 0);
    }
}
