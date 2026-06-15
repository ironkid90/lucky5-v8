namespace Lucky5.Application.Contracts;

using Lucky5.Application.Dtos;

public interface IRewardService
{
    Task<BonusStatusDto> GetBonusStatusAsync(Guid userId, CancellationToken cancellationToken);
    Task<BonusClaimResultDto> ClaimDailyBonusAsync(Guid userId, CancellationToken cancellationToken);
}
