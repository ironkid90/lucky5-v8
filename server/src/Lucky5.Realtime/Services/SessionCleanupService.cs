namespace Lucky5.Realtime.Services;

using Lucky5.Domain.Entities;
using Lucky5.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Background service that periodically cleans up stuck game sessions.
/// Clears active rounds older than a threshold (default: 10 minutes) and
/// force-settles machine sessions for disconnected players.
/// This prevents the "stuck DU" issue where a player disconnects mid-double-up
/// and the machine remains locked indefinitely.
/// </summary>
public sealed class SessionCleanupService : BackgroundService
{
    private readonly InMemoryDataStore _store;
    private readonly ILogger<SessionCleanupService> _logger;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StaleRoundThreshold = TimeSpan.FromMinutes(10);

    public SessionCleanupService(InMemoryDataStore store, ILogger<SessionCleanupService> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before first cleanup to let the server fully start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CleanupStaleRounds();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during session cleanup");
            }

            await Task.Delay(CleanupInterval, stoppingToken);
        }
    }

    private void CleanupStaleRounds()
    {
        var now = DateTime.UtcNow;
        var staleRounds = _store.ActiveRounds
            .Where(kvp => now - kvp.Value.CreatedUtc > StaleRoundThreshold)
            .ToList();

        if (staleRounds.Count == 0)
            return;

        _logger.LogWarning("Cleaning up {Count} stale active rounds", staleRounds.Count);

        foreach (var (roundId, round) in staleRounds)
        {
            _store.ActiveRounds.TryRemove(roundId, out _);

            // If the round was in DU or had pending winnings, settle to wallet
            if (round.WinAmount > 0m && !round.IsPayoutSettled)
            {
                var session = _store.MachineSessions.Values
                    .FirstOrDefault(s => s.UserId == round.UserId && s.MachineId == round.MachineId);

                if (session is not null && _store.MemberProfiles.TryGetValue(round.UserId, out var profile))
                {
                    lock (_store.LedgerSync)
                    {
                        profile.WalletBalance += round.WinAmount;
                    }

                    _store.Ledger.Add(new WalletLedgerEntry
                    {
                        UserId = round.UserId,
                        Amount = round.WinAmount,
                        Type = "StaleRoundSettle",
                        Reference = $"round:{roundId}:stale_cleanup",
                        BalanceAfter = profile.WalletBalance,
                        CreatedUtc = DateTime.UtcNow
                    });

                    _logger.LogInformation("Settled {Amount} from stale round {RoundId} to user {UserId}",
                        round.WinAmount, roundId, round.UserId);
                }

                // Mark as settled so it doesn't get processed again
                round.IsPayoutSettled = true;
            }
        }
    }
}
