namespace Lucky5.Api.Controllers;

using Lucky5.Api.Models;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(IAuthService authService, IHostEnvironment environment, IConfiguration configuration) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("auth-strict")]
    public async Task<ActionResult<ApiResponse<object>>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var (tokens, profile) = await authService.LoginAsync(request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { tokens, profile }, "Login successful", HttpContext.TraceIdentifier));
    }

    [HttpPost("signup")]
    [EnableRateLimiting("auth-strict")]
    public async Task<ActionResult<ApiResponse<object>>> Signup([FromBody] SignupRequest request, CancellationToken cancellationToken)
    {
        var (profile, challenge) = await authService.SignupAsync(request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(
            new
            {
                profile,
                otp = BuildOtpPayload(challenge)
            },
            "Signup successful, verify OTP",
            HttpContext.TraceIdentifier));
    }

    [HttpPost("verify-otp")]
    [EnableRateLimiting("auth-strict")]
    public async Task<ActionResult<ApiResponse<object>>> VerifyOtp([FromBody] VerifyOtpRequest request, CancellationToken cancellationToken)
    {
        var ok = await authService.VerifyOtpAsync(request, cancellationToken);
        if (!ok) return BadRequest(ApiResponse<object>.Fail("Invalid OTP", traceId: HttpContext.TraceIdentifier));
        return Ok(ApiResponse<object>.Ok(new { verified = true }, "OTP verified", HttpContext.TraceIdentifier));
    }

    [HttpPost("resend-otp")]
    [EnableRateLimiting("auth-strict")]
    public async Task<ActionResult<ApiResponse<object>>> ResendOtp([FromBody] ResendOtpRequest request, CancellationToken cancellationToken)
    {
        var challenge = await authService.ResendOtpAsync(request, cancellationToken);
        if (challenge is null) return NotFound(ApiResponse<object>.Fail("User not found", traceId: HttpContext.TraceIdentifier));
        return Ok(ApiResponse<object>.Ok(
            new
            {
                resent = true,
                otp = BuildOtpPayload(challenge)
            },
            "OTP resent",
            HttpContext.TraceIdentifier));
    }

    [HttpGet("GetUserById")]
    public async Task<ActionResult<ApiResponse<MemberProfileDto>>> GetUserById(CancellationToken cancellationToken)
    {
        var userId = HttpContext.RequireUserId();
        var profile = await authService.GetUserByIdAsync(userId, cancellationToken);
        return Ok(ApiResponse<MemberProfileDto>.Ok(profile, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("MemberHistory")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WalletLedgerEntryDto>>>> MemberHistory(CancellationToken cancellationToken)
    {
        var userId = HttpContext.RequireUserId();
        var history = await authService.GetMemberHistoryAsync(userId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<WalletLedgerEntryDto>>.Ok(history, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("TransferBalance")]
    public async Task<ActionResult<ApiResponse<WalletLedgerEntryDto>>> TransferBalance([FromBody] TransferRequest request, CancellationToken cancellationToken)
    {
        var userId = HttpContext.RequireAdminRole();
        var row = await authService.TransferBalanceAsync(userId, request, cancellationToken);
        return Ok(ApiResponse<WalletLedgerEntryDto>.Ok(row, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("MoveWinToBalance")]
    public async Task<ActionResult<ApiResponse<WalletLedgerEntryDto>>> MoveWinToBalance([FromBody] TransferRequest request, CancellationToken cancellationToken)
    {
        var userId = HttpContext.RequireUserId();
        var row = await authService.MoveWinToBalanceAsync(userId, request, cancellationToken);
        return Ok(ApiResponse<WalletLedgerEntryDto>.Ok(row, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("UpdateCredit")]
    public async Task<ActionResult<ApiResponse<WalletLedgerEntryDto>>> UpdateCredit([FromBody] TransferRequest request, CancellationToken cancellationToken)
    {
        var userId = HttpContext.RequireAdminRole();
        var row = await authService.UpdateCreditAsync(userId, request, cancellationToken);
        return Ok(ApiResponse<WalletLedgerEntryDto>.Ok(row, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("Deposit")]
    public async Task<ActionResult<ApiResponse<WalletLedgerEntryDto>>> Deposit([FromBody] TransferRequest request, CancellationToken cancellationToken)
    {
        var userId = HttpContext.RequireUserId();
        var row = await authService.UpdateCreditAsync(userId, request, cancellationToken);
        return Ok(ApiResponse<WalletLedgerEntryDto>.Ok(row, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("Withdraw")]
    public async Task<ActionResult<ApiResponse<WalletLedgerEntryDto>>> Withdraw([FromBody] TransferRequest request, CancellationToken cancellationToken)
    {
        var userId = HttpContext.RequireUserId();
        var row = await authService.UpdateCreditAsync(userId, request, cancellationToken);
        return Ok(ApiResponse<WalletLedgerEntryDto>.Ok(row, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("logout")]
    public async Task<ActionResult<ApiResponse<object>>> Logout(CancellationToken cancellationToken)
    {
        var token = HttpContext.Items["access_token"]?.ToString() ?? string.Empty;
        await authService.LogoutAsync(token, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { loggedOut = true }, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("refresh-token")]
    [EnableRateLimiting("auth-moderate")]
    public async Task<ActionResult<ApiResponse<object>>> RefreshToken([FromBody] TokenRefreshRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tokens = await authService.RefreshTokenAsync(request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { tokens }, "Token refreshed", HttpContext.TraceIdentifier));
        }
        catch (InvalidOperationException ex)
        {
            return Unauthorized(ApiResponse<object>.Fail(ex.Message, traceId: HttpContext.TraceIdentifier));
        }
    }

    private object BuildOtpPayload(PendingOtpChallengeDto challenge)
        => new
        {
            expiresAtUtc = challenge.ExpiresAtUtc,
            previewCode = ShouldExposeOtpPreview() ? challenge.OtpCode : null
        };

    private bool ShouldExposeOtpPreview()
    {
        if (environment.IsDevelopment())
        {
            return true;
        }

        var rawValue = configuration["Auth:ExposeOtpPreview"]
            ?? configuration["Auth__ExposeOtpPreview"]
            ?? Environment.GetEnvironmentVariable("LUCKY5_EXPOSE_OTP_PREVIEW");

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        return string.Equals(rawValue, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);
    }
}
