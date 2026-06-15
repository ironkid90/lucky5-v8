namespace Lucky5.Infrastructure.Services;

using System.Security.Cryptography;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Lucky5.Domain.Entities;

public sealed class AuthService(InMemoryDataStore store, ITokenService tokenService) : IAuthService
{
    public Task<(AuthTokens Tokens, MemberProfileDto Profile)> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = store.Users.Values.FirstOrDefault(x => x.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase));
        var passwordMatches = user is not null && VerifyPassword(user, request.Password);
        if (user is null || !passwordMatches)
        {
            throw new InvalidOperationException("Invalid credentials");
        }

        if (!user.IsOtpVerified)
        {
            throw new InvalidOperationException("OTP not verified");
        }

        var access = tokenService.IssueToken(user.Id, TimeSpan.FromHours(8), user.Role);
        var refresh = tokenService.IssueToken(user.Id, TimeSpan.FromDays(30), user.Role);
        var memberProfile = store.MemberProfiles[user.Id];
        memberProfile.LastSeenUtc = DateTime.UtcNow;

        return Task.FromResult((new AuthTokens(access, refresh, DateTime.UtcNow.AddHours(8)), ToDto(memberProfile)));
    }

    public Task<(MemberProfileDto Profile, PendingOtpChallengeDto Challenge)> SignupAsync(SignupRequest request, CancellationToken cancellationToken)
    {
        if (store.Users.Values.Any(x => x.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Username already exists");
        }

        var challenge = CreateOtpChallenge();
        var user = new User
        {
            Username = request.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            PhoneNumber = request.PhoneNumber,
            Email = request.Email ?? string.Empty,
            FullName = request.FullName ?? request.Username,
            DateOfBirth = request.DateOfBirth,
            IsOtpVerified = false,
            PendingOtp = challenge.OtpCode,
            PendingOtpExpiresUtc = challenge.ExpiresAtUtc,
            Role = "player",
            AgentId = request.AgentId
        };

        store.Users[user.Id] = user;
        store.Profiles[user.Id] = user;
        store.MemberProfiles[user.Id] = new MemberProfile
        {
            UserId = user.Id,
            Username = user.Username,
            DisplayName = user.Username,
            FullName = user.FullName,
            Email = user.Email.Length > 0 ? user.Email : $"{user.Username}@lucky5.local",
            PhoneNumber = user.PhoneNumber,
            DateOfBirth = user.DateOfBirth,
            WalletBalance = 200000m,
            Credit = 0m,
            TotalWins = 0,
            AgentId = user.AgentId,
            GeneratedID = user.GeneratedID,
            MinimumOut = 0m,
            BonusDate = null,
            BonusRechargeCount = 0,
            LastSeenUtc = DateTime.UtcNow
        };

        return Task.FromResult((ToDto(store.MemberProfiles[user.Id]), challenge));
    }

    public Task<bool> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken cancellationToken)
    {
        var user = store.Users.Values.FirstOrDefault(x => x.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase));
        if (user is null || user.PendingOtpExpiresUtc is null || user.PendingOtp != request.OtpCode)
        {
            return Task.FromResult(false);
        }
        if (user.PendingOtpExpiresUtc <= DateTime.UtcNow)
        {
            user.PendingOtp = null;
            user.PendingOtpExpiresUtc = null;
            return Task.FromResult(false);
        }

        user.IsOtpVerified = true;
        user.PendingOtp = null;
        user.PendingOtpExpiresUtc = null;
        return Task.FromResult(true);
    }

    public Task<PendingOtpChallengeDto?> ResendOtpAsync(ResendOtpRequest request, CancellationToken cancellationToken)
    {
        var user = store.Users.Values.FirstOrDefault(x => x.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            return Task.FromResult<PendingOtpChallengeDto?>(null);
        }

        var challenge = CreateOtpChallenge();
        user.PendingOtp = challenge.OtpCode;
        user.PendingOtpExpiresUtc = challenge.ExpiresAtUtc;
        return Task.FromResult<PendingOtpChallengeDto?>(challenge);
    }

    public Task<MemberProfileDto> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!store.MemberProfiles.TryGetValue(userId, out var profile))
        {
            throw new KeyNotFoundException("User not found");
        }

        return Task.FromResult(ToDto(profile));
    }

    public Task<IReadOnlyList<WalletLedgerEntryDto>> GetMemberHistoryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = store.Ledger
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new WalletLedgerEntryDto(x.Id, x.Amount, x.BalanceAfter, x.Type, x.Reference, x.CreatedUtc))
            .ToArray();

        return Task.FromResult<IReadOnlyList<WalletLedgerEntryDto>>(rows);
    }

    public Task<WalletLedgerEntryDto> TransferBalanceAsync(Guid userId, TransferRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(AdjustBalance(userId, request.Amount, "TransferBalance", request.Reference));
    }

    public Task<WalletLedgerEntryDto> MoveWinToBalanceAsync(Guid userId, TransferRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(AdjustBalance(userId, request.Amount, "MoveWinToBalance", request.Reference));
    }

    public Task<WalletLedgerEntryDto> UpdateCreditAsync(Guid userId, TransferRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(AdjustCredit(userId, request.Amount, "UpdateCredit", request.Reference));
    }

    public Task<WalletLedgerEntryDto> RechargeBonusAsync(Guid userId, decimal rechargeAmount, CancellationToken cancellationToken)
    {
        if (!store.MemberProfiles.TryGetValue(userId, out var profile))
        {
            throw new KeyNotFoundException("Profile not found");
        }

        // Tiered bonus structure based on recharge amount
        decimal bonusPercentage = rechargeAmount switch
        {
            >= 10_000_000 => 0.20m, // 20% bonus for 10M+
            >= 5_000_000 => 0.15m,  // 15% bonus for 5M+
            >= 1_000_000 => 0.10m,  // 10% bonus for 1M+
            >= 500_000 => 0.05m,    // 5% bonus for 500K+
            _ => 0m                 // No bonus for smaller amounts
        };

        var bonusAmount = rechargeAmount * bonusPercentage;
        var totalAmount = rechargeAmount + bonusAmount;

        profile.WalletBalance += totalAmount;
        profile.BonusRechargeCount++;
        profile.BonusDate = DateTime.UtcNow;

        var row = new WalletLedgerEntry
        {
            UserId = userId,
            Amount = totalAmount,
            Type = "RechargeBonus",
            Reference = $"Recharge: {rechargeAmount:N0}, Bonus: {bonusAmount:N0}",
            BalanceAfter = profile.WalletBalance,
            CreatedUtc = DateTime.UtcNow
        };

        store.Ledger.Add(row);
        return Task.FromResult(new WalletLedgerEntryDto(row.Id, row.Amount, row.BalanceAfter, row.Type, row.Reference, row.CreatedUtc));
    }

    public Task LogoutAsync(string accessToken, CancellationToken cancellationToken)
    {
        tokenService.Revoke(accessToken);
        return Task.CompletedTask;
    }

    public Task<AuthTokens> RefreshTokenAsync(TokenRefreshRequest request, CancellationToken cancellationToken)
    {
        if (!tokenService.TryValidate(request.RefreshToken, out var userId, out var role))
        {
            throw new InvalidOperationException("Invalid or expired refresh token");
        }

        if (!store.Users.TryGetValue(userId, out var user))
        {
            throw new InvalidOperationException("User not found");
        }

        tokenService.Revoke(request.RefreshToken);

        var access = tokenService.IssueToken(userId, TimeSpan.FromHours(8), role);
        var refresh = tokenService.IssueToken(userId, TimeSpan.FromDays(30), role);
        return Task.FromResult(new AuthTokens(access, refresh, DateTime.UtcNow.AddHours(8)));
    }

    private static PendingOtpChallengeDto CreateOtpChallenge()
        => new(GenerateOtpCode(), DateTime.UtcNow.AddMinutes(10));

    private static string GenerateOtpCode()
        => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static bool VerifyPassword(User user, string password)
    {
        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return false;
        }

        if (LooksLikeBcryptHash(user.PasswordHash))
        {
            return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        }

        if (!string.Equals(user.PasswordHash, password, StringComparison.Ordinal))
        {
            return false;
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        return true;
    }

    private static bool LooksLikeBcryptHash(string value)
        => value.StartsWith("$2a$", StringComparison.Ordinal)
            || value.StartsWith("$2b$", StringComparison.Ordinal)
            || value.StartsWith("$2y$", StringComparison.Ordinal);

    private WalletLedgerEntryDto AdjustBalance(Guid userId, decimal amount, string type, string reference)
    {
        if (!store.MemberProfiles.TryGetValue(userId, out var profile))
        {
            throw new KeyNotFoundException("Profile not found");
        }

        profile.WalletBalance += amount;
        var row = new WalletLedgerEntry
        {
            UserId = userId,
            Amount = amount,
            Type = type,
            Reference = reference,
            BalanceAfter = profile.WalletBalance,
            CreatedUtc = DateTime.UtcNow
        };

        store.Ledger.Add(row);
        return new WalletLedgerEntryDto(row.Id, row.Amount, row.BalanceAfter, row.Type, row.Reference, row.CreatedUtc);
    }

    private WalletLedgerEntryDto AdjustCredit(Guid userId, decimal amount, string type, string reference)
    {
        if (!store.MemberProfiles.TryGetValue(userId, out var profile))
        {
            throw new KeyNotFoundException("Profile not found");
        }

        profile.Credit += amount;
        var row = new WalletLedgerEntry
        {
            UserId = userId,
            Amount = amount,
            Type = type,
            Reference = reference,
            BalanceAfter = profile.Credit,
            CreatedUtc = DateTime.UtcNow
        };

        store.Ledger.Add(row);
        return new WalletLedgerEntryDto(row.Id, row.Amount, row.BalanceAfter, row.Type, row.Reference, row.CreatedUtc);
    }

    private MemberProfileDto ToDto(MemberProfile profile)
    {
        var role = "player";
        if (store.Users.TryGetValue(profile.UserId, out var user))
        {
            role = user.Role;
        }
        return new MemberProfileDto(
            profile.UserId, profile.Username, profile.DisplayName, profile.FullName,
            profile.Email, profile.PhoneNumber, profile.DateOfBirth,
            profile.WalletBalance, profile.Credit, profile.TotalWins,
            profile.AgentId, profile.GeneratedID, profile.MinimumOut,
            profile.BonusDate, profile.BonusRechargeCount,
            profile.LastSeenUtc, role);
    }
}
