namespace Lucky5.Api.Middleware;

using System.Diagnostics;
using System.Text.Json;

/// <summary>
/// Structured audit trail middleware that logs every API call with
/// actor, action, path, status, and duration — feeding into the
/// Langfuse observability pipeline already wired in Program.cs.
/// </summary>
public sealed class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;

    private static readonly HashSet<string> SkipPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/config/rules",
        "/health/live",
        "/health/ready",
        "/swagger",
        "/favicon.ico"
    };

    public AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var path = context.Request.Path.Value ?? "/";
        var method = context.Request.Method;
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string? actor = null;

        // Extract actor from JWT claims if available
        var nameClaim = context.User?.FindFirst("unique_name")?.Value
                        ?? context.User?.FindFirst("sub")?.Value
                        ?? context.User?.Identity?.Name;
        actor = nameClaim ?? "anonymous";

        // Extract machine / round context from route if available
        var routeValues = context.Request.RouteValues;
        string? machineId = routeValues.TryGetValue("machineId", out var mid) ? mid?.ToString() : null;

        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            var skip = SkipPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (!skip)
            {
                _logger.LogInformation(
                    "AUDIT {Actor} {Method} {Path} {StatusCode} {DurationMs}ms machine={MachineId}",
                    actor, method, path, context.Response.StatusCode, sw.ElapsedMilliseconds, machineId ?? "n/a");
            }
        }
    }
}