using Lucky5.Api.Models;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Lucky5.Domain.Game.CleanRoom;
using Lucky5.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Lucky5.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GameController(IGameService gameService) : ControllerBase
{
    private Guid UserId => HttpContext.RequireUserId();

    [HttpGet("lobby")]
    public async Task<ActionResult<ApiResponse<PlayerLobbyDto>>> GetLobby(CancellationToken cancellationToken)
    {
        var lobby = await gameService.GetLobbyAsync(UserId, cancellationToken);
        return Ok(ApiResponse<PlayerLobbyDto>.Ok(lobby, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("machines")]
    [HttpGet("games/machines")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MachineListingDto>>>> GetMachines(CancellationToken cancellationToken)
    {
        var machines = await gameService.GetMachinesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<MachineListingDto>>.Ok(machines, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("rules")]
    [HttpGet("defaultRules")]
    public async Task<ActionResult<ApiResponse<DefaultRulesDto>>> GetRules(CancellationToken cancellationToken)
    {
        var rules = await gameService.GetDefaultRulesAsync(cancellationToken);
        return Ok(ApiResponse<DefaultRulesDto>.Ok(rules, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("machine/{machineId}/session")]
    public async Task<ActionResult<ApiResponse<MachineSessionDto>>> GetMachineSession(int machineId, CancellationToken cancellationToken)
    {
        var session = await gameService.GetMachineSessionAsync(UserId, machineId, cancellationToken);
        return Ok(ApiResponse<MachineSessionDto>.Ok(session, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("machine/{machineId}/cash-in")]
    public async Task<ActionResult<ApiResponse<MachineSessionDto>>> CashIn(int machineId, [FromBody] MachineCashRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await gameService.CashInAsync(UserId, machineId, request.Amount, cancellationToken);
            return Ok(ApiResponse<MachineSessionDto>.Ok(session, traceId: HttpContext.TraceIdentifier));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<MachineSessionDto>(false, ex.Message, null, [], HttpContext.TraceIdentifier));
        }
    }

    [HttpPost("machine/{machineId}/cash-out")]
    public async Task<ActionResult<ApiResponse<MachineSessionDto>>> CashOut(int machineId, [FromQuery] bool isExit = false, [FromQuery] bool bypassRules = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await gameService.CashOutAsync(UserId, machineId, cancellationToken, bypassRules: isExit || bypassRules);
            return Ok(ApiResponse<MachineSessionDto>.Ok(session, traceId: HttpContext.TraceIdentifier));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<MachineSessionDto>(false, ex.Message, null, [], HttpContext.TraceIdentifier));
        }
    }

    [HttpGet("active-round/{machineId}")]
    [HttpGet("machine/{machineId}/active-round")]
    public async Task<ActionResult<ApiResponse<ActiveRoundStateDto?>>> GetActiveRound(int machineId, CancellationToken cancellationToken)
    {
        var result = await gameService.GetActiveRoundAsync(UserId, machineId, cancellationToken);
        return Ok(new ApiResponse<ActiveRoundStateDto?>(true, "OK", result, [], HttpContext.TraceIdentifier));
    }

    [HttpGet("machine/{machineId}/cabinet-snapshot")]
    public async Task<ActionResult<ApiResponse<CabinetSnapshotDto>>> GetCabinetSnapshot(int machineId, CancellationToken cancellationToken)
    {
        var result = await gameService.GetCabinetSnapshotAsync(UserId, machineId, cancellationToken);
        return Ok(ApiResponse<CabinetSnapshotDto>.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("cabinet/command")]
    public async Task<ActionResult<ApiResponse<CabinetCommandResultDto>>> SubmitCabinetCommand([FromBody] CabinetCommandDto command, CancellationToken cancellationToken)
    {
        var result = await gameService.SubmitCabinetCommandAsync(UserId, command, cancellationToken);
        var response = ApiResponse<CabinetCommandResultDto>.Ok(result, traceId: HttpContext.TraceIdentifier);

        return result.Status switch
        {
            "stale_state" => Conflict(response),
            "invalid" or "rejected" or "requires_snapshot" => BadRequest(response),
            _ => Ok(response)
        };
    }

    [HttpPost("machine/{machineId}/cabinet-replay")]
    public async Task<ActionResult<ApiResponse<CabinetReplayDto>>> GetCabinetReplay(int machineId, [FromBody] CabinetReconnectRequest request, CancellationToken cancellationToken)
    {
        var result = await gameService.GetCabinetReplayAsync(UserId, machineId, request.LastStateVersion, request.LastSequenceNumber, cancellationToken);
        return Ok(ApiResponse<CabinetReplayDto>.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("deal")]
    [HttpPost("cards/deal")]
    public async Task<ActionResult<ApiResponse<DealResultDto>>> Deal([FromBody] DealRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.DealAsync(UserId, request, cancellationToken);
            return Ok(ApiResponse<DealResultDto>.Ok(result, traceId: HttpContext.TraceIdentifier));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<DealResultDto>(false, ex.Message, null, [], HttpContext.TraceIdentifier));
        }
    }

    [HttpPost("draw")]
    [HttpPost("cards/draw")]
    public async Task<ActionResult<ApiResponse<DrawResultDto>>> Draw([FromBody] DrawRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.DrawAsync(UserId, request, cancellationToken);
            return Ok(ApiResponse<DrawResultDto>.Ok(result, traceId: HttpContext.TraceIdentifier));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<DrawResultDto>(false, ex.Message, null, [], HttpContext.TraceIdentifier));
        }
    }

    [HttpPost("double-up/start")]
    public async Task<ActionResult<ApiResponse<DoubleUpResultDto>>> StartDoubleUp([FromBody] StartDoubleUpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.StartDoubleUpAsync(UserId, request.RoundId, cancellationToken);
            return Ok(ApiResponse<DoubleUpResultDto>.Ok(result, traceId: HttpContext.TraceIdentifier));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<DoubleUpResultDto>(false, ex.Message, null, [], HttpContext.TraceIdentifier));
        }
    }

    [HttpPost("double-up/guess")]
    public async Task<ActionResult<ApiResponse<DoubleUpResultDto>>> GuessDoubleUp([FromBody] DoubleUpGuessRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.GuessDoubleUpAsync(UserId, request.RoundId, request.Guess, cancellationToken);
            return Ok(ApiResponse<DoubleUpResultDto>.Ok(result, traceId: HttpContext.TraceIdentifier));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<DoubleUpResultDto>(false, ex.Message, null, [], HttpContext.TraceIdentifier));
        }
    }

    [HttpPost("double-up/half")]
    public async Task<ActionResult<ApiResponse<DoubleUpResultDto>>> TakeHalfDoubleUp([FromBody] CashoutDoubleUpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.TakeHalfAsync(UserId, request.RoundId, cancellationToken);
            return Ok(ApiResponse<DoubleUpResultDto>.Ok(result, traceId: HttpContext.TraceIdentifier));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<DoubleUpResultDto>(false, ex.Message, null, [], HttpContext.TraceIdentifier));
        }
    }

    [HttpPost("double-up/swap")]
    public async Task<ActionResult<ApiResponse<DoubleUpResultDto>>> SwapDoubleUpCard([FromBody] CashoutDoubleUpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.SwapDoubleUpCardAsync(UserId, request.RoundId, 0, cancellationToken);
            return Ok(ApiResponse<DoubleUpResultDto>.Ok(result, traceId: HttpContext.TraceIdentifier));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<DoubleUpResultDto>(false, ex.Message, null, [], HttpContext.TraceIdentifier));
        }
    }

    [HttpPost("double-up/take")]
    public async Task<ActionResult<ApiResponse<DoubleUpResultDto>>> TakeScoreDoubleUp([FromBody] CashoutDoubleUpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.CashoutDoubleUpAsync(UserId, request.RoundId, cancellationToken);
            return Ok(ApiResponse<DoubleUpResultDto>.Ok(result, traceId: HttpContext.TraceIdentifier));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<DoubleUpResultDto>(false, ex.Message, null, [], HttpContext.TraceIdentifier));
        }
    }

    [HttpPost("double-up/collect")]
    public async Task<ActionResult<ApiResponse<DoubleUpResultDto>>> CollectDoubleUp([FromBody] CashoutDoubleUpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.CashoutDoubleUpAsync(UserId, request.RoundId, cancellationToken);
            return Ok(ApiResponse<DoubleUpResultDto>.Ok(result, traceId: HttpContext.TraceIdentifier));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<DoubleUpResultDto>(false, ex.Message, null, [], HttpContext.TraceIdentifier));
        }
    }
}
