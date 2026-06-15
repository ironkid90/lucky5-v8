using Lucky5.Api.Models;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Lucky5.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RewardController(IRewardService rewardService) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<BonusStatusDto>>> GetBonusStatus(CancellationToken cancellationToken)
    {
        var userId = HttpContext.RequireUserId();
        var status = await rewardService.GetBonusStatusAsync(userId, cancellationToken);
        return Ok(ApiResponse<BonusStatusDto>.Ok(status, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("claim")]
    public async Task<ActionResult<ApiResponse<BonusClaimResultDto>>> ClaimDailyBonus(CancellationToken cancellationToken)
    {
        var userId = HttpContext.RequireUserId();
        var result = await rewardService.ClaimDailyBonusAsync(userId, cancellationToken);
        return Ok(ApiResponse<BonusClaimResultDto>.Ok(result, "Bonus claimed!", HttpContext.TraceIdentifier));
    }
}
