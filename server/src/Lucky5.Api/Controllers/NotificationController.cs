using Lucky5.Api.Models;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Lucky5.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationController(INotificationService notificationService) : ControllerBase
{
    public sealed record RegisterDeviceRequest(string Token, string Platform = "android");

    [HttpPost("register-device")]
    public async Task<ActionResult<ApiResponse<object>>> RegisterDevice(
        [FromBody] RegisterDeviceRequest request, CancellationToken cancellationToken)
    {
        var userId = HttpContext.RequireUserId();
        await notificationService.RegisterDeviceAsync(userId, request.Token, request.Platform, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { registered = true }, traceId: HttpContext.TraceIdentifier));
    }
}
