namespace Lucky5.Tests;

using System.Text.RegularExpressions;
using Lucky5.Application.Contracts;
using Lucky5.Application.Requests;
using Lucky5.Domain.Entities;
using Lucky5.Infrastructure.Data.Repositories;
using Lucky5.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

public static class AuthSecurityRegressionTests
{
    public static async Task RunAsync(List<string> failures)
    {
        await SignupShouldHashPasswordsAndIssueExpiringOtpAsync(failures);
        await LoginShouldMigrateLegacyPlaintextPasswordsAsync(failures);
        await VerifyOtpShouldRejectExpiredCodesAsync(failures);
        await ResendOtpShouldRefreshThePendingChallengeAsync(failures);
        await WebCabinetShouldUseIssuedOtpAndRecoverToCredentialLoginAsync(failures);
    }

    private static async Task SignupShouldHashPasswordsAndIssueExpiringOtpAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var service = CreateAuthService(store);
        var request = new SignupRequest(
            "security-signup",
            "s3cr3t-password",
            "+96170000001",
            "security-signup@lucky5.local");
        var beforeSignup = DateTime.UtcNow;

        var (_, challenge) = await service.SignupAsync(request, CancellationToken.None);

        store.Users.Values.Single(user => user.Username == request.Username).PasswordHash
            .ShouldNotBePlaintext(
                failures,
                "Signup should not persist the raw password for new members.",
                request.Password);

        var signedUpUser = store.Users.Values.Single(user => user.Username == request.Username);

        Assert(
            failures,
            "Signup should persist a BCrypt password hash that still verifies the user's password.",
            BCrypt.Net.BCrypt.Verify(request.Password, signedUpUser.PasswordHash));
        Assert(
            failures,
            "Signup should issue a six-digit OTP challenge instead of reusing a fixed code.",
            Regex.IsMatch(challenge.OtpCode, @"^\d{6}$", RegexOptions.CultureInvariant));
        Assert(
            failures,
            "Signup should store the same OTP challenge that it returns to the caller.",
            string.Equals(signedUpUser.PendingOtp, challenge.OtpCode, StringComparison.Ordinal)
                && signedUpUser.PendingOtpExpiresUtc == challenge.ExpiresAtUtc);
        Assert(
            failures,
            "Signup OTP expiry should remain close to the intended 10-minute window.",
            challenge.ExpiresAtUtc >= beforeSignup.AddMinutes(9)
                && challenge.ExpiresAtUtc <= DateTime.UtcNow.AddMinutes(11));
    }

    private static async Task LoginShouldMigrateLegacyPlaintextPasswordsAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var service = CreateAuthService(store);
        var userId = Guid.NewGuid();
        var legacyPassword = "legacy-password";
        SeedUser(store, userId, "legacy-player", legacyPassword, isOtpVerified: true);

        _ = await service.LoginAsync(new LoginRequest("legacy-player", legacyPassword), CancellationToken.None);

        var migratedUser = store.Users[userId];
        migratedUser.PasswordHash.ShouldNotBePlaintext(
            failures,
            "Legacy plaintext passwords should be replaced with a BCrypt hash after a successful login.",
            legacyPassword);
        Assert(
            failures,
            "Legacy plaintext password migration should preserve successful login verification.",
            BCrypt.Net.BCrypt.Verify(legacyPassword, migratedUser.PasswordHash));
    }

    private static async Task VerifyOtpShouldRejectExpiredCodesAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var service = CreateAuthService(store);
        var request = new SignupRequest(
            "expired-otp-player",
            "otp-password",
            "+96170000002",
            "expired-otp-player@lucky5.local");

        var (_, challenge) = await service.SignupAsync(request, CancellationToken.None);
        var user = store.Users.Values.Single(candidate => candidate.Username == request.Username);
        user.PendingOtpExpiresUtc = DateTime.UtcNow.AddMinutes(-1);

        var verified = await service.VerifyOtpAsync(new VerifyOtpRequest(request.Username, challenge.OtpCode), CancellationToken.None);

        Assert(
            failures,
            "Expired OTP challenges should fail verification.",
            !verified);
        Assert(
            failures,
            "Expired OTP verification should clear the stale pending challenge.",
            user.PendingOtp is null && user.PendingOtpExpiresUtc is null && !user.IsOtpVerified);
    }

    private static async Task ResendOtpShouldRefreshThePendingChallengeAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var service = CreateAuthService(store);
        var request = new SignupRequest(
            "resend-otp-player",
            "resend-password",
            "+96170000003",
            "resend-otp-player@lucky5.local");

        var (_, firstChallenge) = await service.SignupAsync(request, CancellationToken.None);
        var user = store.Users.Values.Single(candidate => candidate.Username == request.Username);
        user.PendingOtpExpiresUtc = DateTime.UtcNow.AddMinutes(1);

        var resentChallenge = await service.ResendOtpAsync(new ResendOtpRequest(request.Username), CancellationToken.None);

        Assert(
            failures,
            "Resend OTP should return a fresh active challenge for an existing user.",
            resentChallenge is not null
                && Regex.IsMatch(resentChallenge.OtpCode, @"^\d{6}$", RegexOptions.CultureInvariant)
                && resentChallenge.ExpiresAtUtc > firstChallenge.ExpiresAtUtc.AddMinutes(-8));
        Assert(
            failures,
            "Resend OTP should update the stored pending challenge details.",
            resentChallenge is not null
                && string.Equals(user.PendingOtp, resentChallenge.OtpCode, StringComparison.Ordinal)
                && user.PendingOtpExpiresUtc == resentChallenge.ExpiresAtUtc);
    }

    private static async Task WebCabinetShouldUseIssuedOtpAndRecoverToCredentialLoginAsync(List<string> failures)
    {
        var cabinetIndex = await File.ReadAllTextAsync(ResolveRepoFilePath("server", "src", "Lucky5.Api", "wwwroot", "index.html"));
        var cabinetScript = await File.ReadAllTextAsync(ResolveRepoFilePath("server", "src", "Lucky5.Api", "wwwroot", "js", "game.js"));
        var readme = await File.ReadAllTextAsync(ResolveRepoFilePath("README.md"));

        Assert(
            failures,
            "The web cabinet should expose username/password login and signup flows instead of requiring a preloaded token.",
            cabinetIndex.Contains("id=\"auth-username\"", StringComparison.Ordinal)
                && cabinetIndex.Contains("id=\"auth-password\"", StringComparison.Ordinal)
                && cabinetIndex.Contains("id=\"auth-submit\"", StringComparison.Ordinal)
                && cabinetScript.Contains("/api/Auth/login", StringComparison.Ordinal)
                && cabinetScript.Contains("/api/Auth/signup", StringComparison.Ordinal)
                && cabinetScript.Contains("/api/Auth/verify-otp", StringComparison.Ordinal));
        Assert(
            failures,
            "The web cabinet should recover from invalid saved sessions by clearing the token and returning to the auth screen.",
            cabinetScript.Contains("clearToken();", StringComparison.Ordinal)
                && cabinetScript.Contains("authScreen.style.display = '';", StringComparison.Ordinal));
        Assert(
            failures,
            "README should document the browser-first startup path and shared local credentials.",
            readme.Contains(".\\dev.ps1", StringComparison.Ordinal)
                && readme.Contains("http://localhost:5051", StringComparison.Ordinal)
                && readme.Contains("admin123", StringComparison.Ordinal)
                && readme.Contains("tester", StringComparison.Ordinal));
    }

    private static AuthService CreateAuthService(InMemoryDataStore store)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JWT:SIGNING_KEY"] = "test-signing-key" })
            .Build();

        var dataStoreAdapter = new InMemoryDataStoreAdapter(store);
        var tokenRevocationStore = new PersistentTokenRevocationStore(dataStoreAdapter);
        var tokenService = new SimpleTokenService(configuration, tokenRevocationStore);
        return new AuthService(store, tokenService);
    }

    private static void SeedUser(InMemoryDataStore store, Guid userId, string username, string passwordHash, bool isOtpVerified)
    {
        var user = new User
        {
            Id = userId,
            Username = username,
            PasswordHash = passwordHash,
            PhoneNumber = $"+961{Math.Abs(username.GetHashCode()):0000000}",
            Role = "player",
            IsOtpVerified = isOtpVerified
        };

        store.Profiles[userId] = user;
        store.Users[userId] = user;
        store.MemberProfiles[userId] = new MemberProfile
        {
            UserId = userId,
            Username = username,
            DisplayName = username,
            Email = $"{username}@lucky5.local",
            PhoneNumber = user.PhoneNumber,
            WalletBalance = 500_000m,
            LastSeenUtc = DateTime.UtcNow
        };
    }

    private static string ResolveRepoFilePath(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file '{Path.Combine(segments)}' from base directory '{AppContext.BaseDirectory}'");
    }

    private static void Assert(List<string> failures, string message, bool condition)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }
}

file static class AuthSecurityRegressionTestExtensions
{
    public static void ShouldNotBePlaintext(this string actual, List<string> failures, string message, string expectedPlaintext)
    {
        if (string.Equals(actual, expectedPlaintext, StringComparison.Ordinal))
        {
            failures.Add(message);
        }
    }
}
