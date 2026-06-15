namespace Lucky5.Api.Models;

using System.Security.Claims;
using Lucky5.Application.Contracts;

public static class HttpContextExtensions
{
    public static Guid RequireUserId(this HttpContext context)
    {
        var claim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (claim is null || !Guid.TryParse(claim, out var userId))
        {
            throw new UnauthorizedAccessException("Missing authentication context");
        }

        return userId;
    }

    public static Guid RequireAdminRole(this HttpContext context)
    {
        var userId = context.RequireUserId();
        var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
        if (!string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Admin role required");
        }

        return userId;
    }

    public static CabinetDeviceAuthContext RequireCabinetDevice(this HttpContext context)
    {
        if (context.Items.TryGetValue("cabinet_device", out var value) && value is CabinetDeviceAuthContext device)
        {
            return device;
        }

        throw new UnauthorizedAccessException("Cabinet device authentication required");
    }
}
