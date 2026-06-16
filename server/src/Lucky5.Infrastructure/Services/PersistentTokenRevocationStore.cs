namespace Lucky5.Infrastructure.Services;

using System.Security.Cryptography;
using System.Text;
using Lucky5.Application.Contracts;
using Lucky5.Application.Interfaces;
using Lucky5.Domain.Entities;

public sealed class PersistentTokenRevocationStore(IDataStore store) : ITokenRevocationStore
{
    private static readonly TimeSpan TokenExpiryBuffer = TimeSpan.FromHours(48); // Extra buffer beyond max token lifetime

    public async Task<bool> IsRevokedAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        // Use SHA256 hash of token as the key for storage (never store raw tokens)
        var tokenHash = HashToken(token);
        var entry = await store.GetTokenRevocationAsync(tokenHash, cancellationToken);
        
        if (entry is null)
            return false;

        // Check if the revocation entry itself has expired (shouldn't happen if we clean up)
        if (entry.ExpiresUtc < DateTime.UtcNow)
        {
            // Clean up stale entry
            await store.DeleteTokenRevocationAsync(tokenHash, cancellationToken);
            return false;
        }

        return true;
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        var tokenHash = HashToken(token);
        
        // Extract expiry from token if possible to set revocation expiry
        var expiresUtc = ExtractExpiryFromToken(token) ?? DateTime.UtcNow.Add(TokenExpiryBuffer);
        
        var entry = new TokenRevocationEntry
        {
            TokenHash = tokenHash,
            RevokedUtc = DateTime.UtcNow,
            ExpiresUtc = expiresUtc
        };

        await store.SaveTokenRevocationAsync(entry, cancellationToken);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await store.RevokeAllUserTokensAsync(userId, cancellationToken);
    }

    public async Task CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        await store.CleanupExpiredTokenRevocationsAsync(cancellationToken);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static DateTime? ExtractExpiryFromToken(string token)
    {
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split('.', 4);
            if (parts.Length >= 2 && long.TryParse(parts[1], out var expires))
            {
                return DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime;
            }
        }
        catch
        {
            // Ignore parsing errors
        }
        return null;
    }
}