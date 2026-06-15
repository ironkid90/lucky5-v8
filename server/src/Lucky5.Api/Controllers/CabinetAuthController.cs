namespace Lucky5.Api.Controllers;

using Lucky5.Api.Models;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public sealed class CabinetAuthController(ICabinetDeviceAuthService cabinetDeviceAuthService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<CabinetDeviceAuthResultDto>>> Login([FromBody] CabinetDeviceAuthRequest request, CancellationToken cancellationToken)
    {
        var result = await cabinetDeviceAuthService.AuthenticateAsync(request, cancellationToken);
        return Ok(ApiResponse<CabinetDeviceAuthResultDto>.Ok(result, "Cabinet device authenticated", HttpContext.TraceIdentifier));
    }

    [HttpGet("me")]
    public ActionResult<ApiResponse<CabinetDeviceAuthContext>> Me()
    {
        var context = HttpContext.RequireCabinetDevice();
        return Ok(ApiResponse<CabinetDeviceAuthContext>.Ok(context, traceId: HttpContext.TraceIdentifier));
    }
}