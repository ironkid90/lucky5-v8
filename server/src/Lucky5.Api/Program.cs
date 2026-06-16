using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Infrastructure.Services;
using Lucky5.Realtime;
using Lucky5.Realtime.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

// NOTE: Rate limiting disabled for .NET 10 compatibility.
// Re-enable when Microsoft.AspNetCore.RateLimiter has a stable .NET 10/11 build.
// builder.Services.AddRateLimiter(...) ...
// app.UseRateLimiter();

var portValue = Environment.GetEnvironmentVariable("PORT")
    ?? Environment.GetEnvironmentVariable("WEBSITES_PORT")
    ?? "8080";

if (int.TryParse(portValue, out var port))
{
    builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));
}

if (!builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Production.json", optional: true, reloadOnChange: true);
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();
builder.Services.AddLucky5Realtime();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration["CORS:ALLOWED_ORIGINS"]
            ?? builder.Configuration["CORS__ALLOWED_ORIGINS"];

        if (!string.IsNullOrWhiteSpace(allowedOrigins))
        {
            var origins = allowedOrigins
                .Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (origins.Length > 0)
            {
                policy.WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
                return;
            }
        }

        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddLucky5Infrastructure(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck("live", () => new HealthCheckResult(HealthStatus.Healthy, "Application is running"));

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var (status, message) = ex switch
        {
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, ex.Message),
            KeyNotFoundException => (HttpStatusCode.NotFound, ex.Message),
            InvalidOperationException => (HttpStatusCode.BadRequest, ex.Message),
            _ => (HttpStatusCode.InternalServerError, "Unexpected server error")
        };

        app.Logger.LogError(
            ex,
            "Unhandled API request failure {Method} {Path} -> {StatusCode} ({TraceId})",
            context.Request.Method,
            context.Request.Path,
            (int)status,
            context.TraceIdentifier);

        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json";

        var payload = ApiResponse<object>.Fail(
            message,
            errors: status == HttpStatusCode.InternalServerError ? [] : [ex.Message],
            traceId: context.TraceIdentifier);

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
});

app.Use(async (context, next) =>
{
    var authHeader = context.Request.Headers.Authorization.ToString();
    var accessToken = string.Empty;
    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        accessToken = authHeader[7..].Trim();
    }
    else if (context.Request.Query.TryGetValue("access_token", out var queryToken))
    {
        accessToken = queryToken.ToString();
    }

    if (!string.IsNullOrWhiteSpace(accessToken))
    {
        var tokenService = context.RequestServices.GetRequiredService<ITokenService>();
        var result = await tokenService.ValidateTokenAsync(accessToken, context.RequestAborted);
        if (result.IsValid)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, result.UserId.ToString()),
                new Claim(ClaimTypes.Role, result.Role)
            };
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Lucky5Bearer"));
            context.Items["access_token"] = accessToken;
        }
        else
        {
            var cabinetAuthService = context.RequestServices.GetRequiredService<ICabinetDeviceAuthService>();
            var cabinetDevice = await cabinetAuthService.ValidateAccessTokenAsync(accessToken, context.RequestAborted);
            if (cabinetDevice is not null)
            {
                context.Items["access_token"] = accessToken;
                context.Items["cabinet_device"] = cabinetDevice;
            }
        }
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseCors();
// NOTE: Rate limiting disabled for .NET 10 compatibility - re-enable when stable
// app.UseRateLimiter();
app.MapControllers();
app.MapHub<CarrePokerGameHub>("/CarrePokerGameHub");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Name == "live"
});
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/simple", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/fallback", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapFallbackToFile("index.html");

app.Run();
