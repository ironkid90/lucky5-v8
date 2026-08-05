using Lucky5.Api.Models;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Lucky5.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LobbyController(IGameService gameService) : ControllerBase
{
    private Guid UserId => HttpContext.RequireUserId();

    [HttpGet("machines")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MachineListingDto>>>> GetMachines(CancellationToken cancellationToken)
    {
        var machines = await gameService.GetLobbyMachinesAsync(UserId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<MachineListingDto>>.Ok(machines, traceId: HttpContext.TraceIdentifier));
    }
}