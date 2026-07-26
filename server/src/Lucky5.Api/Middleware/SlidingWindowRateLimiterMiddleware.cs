using Microsoft.Extensions.Caching.Memory;

namespace Lucky5.Api.Middleware;

/// <summary>
/// Simple sliding-window in-memory rate limiter that does not depend on
/// the ASP.NET RateLimiter middleware (which lacks a stable .NET 10 build).
/// </summary>
public sealed class SlidingWindowRateLimiterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;

    private static readonly (string path, int maxPerWindow, int windowSeconds)[] DefaultConfigs =
    [
        ("/api/auth/login", 5, 60),
        ("/api/auth/signup", 5, 60),
        ("/api/game/deal", 30, 60),
        ("/api/game/draw", 30, 60),
        ("/api/admin", 60, 60),
    ];

    public SlidingWindowRateLimiterMiddleware(RequestDelegate next, IMemoryCache cache)
    {
        _next = next;
        _cache = cache;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        foreach (var (prefix, maxPerWindow, windowSeconds) in DefaultConfigs)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var key = $"rl:{context.Connection.RemoteIpAddress}:{prefix}";
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var windowStart = now - windowSeconds;

            var timestamps = _cache.GetOrCreate(key, _ => new List<long>())!;

            // Remove entries outside the window
            timestamps.RemoveAll(t => t < windowStart);

            if (timestamps.Count >= maxPerWindow)
            {
                context.Response.StatusCode = 429;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    "{\"success\":false,\"message\":\"Too many requests. Please wait.\"}");
                return;
            }

            timestamps.Add(now);
            break;
        }

        await _next(context);
    }
}