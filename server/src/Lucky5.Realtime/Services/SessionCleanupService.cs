namespace Lucky5.Realtime.Services;

using Lucky5.Domain.Entities;
using Lucky5.Application.Contracts;
using Lucky5.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Background service that periodically cleans up stuck game sessions.
/// Clears active rounds older than a threshold (default: 10 minutes) and
/// force-settles machine sessions for disconnected players.
/// This prevents the "stuck DU" issue where a player disconnects mid-double-up
/// and the machine remains locked indefinitely.
/// Uses IGameService.CashOutAsync to ensure DU credits are properly settled
/// and machine credits are zeroed out (preventing double-settlement on reconnect).
/// Creates a service scope per cleanup iteration to safely resolve the scoped IGameService.
/// </summary>
public sealed class SessionCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InMemoryDataStore _store;
    private readonly ILogger<SessionCleanupService> _logger;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StaleRoundThreshold = TimeSpan.FromMinutes(10);

    public SessionCleanupService(IServiceScopeFactory scopeFactory, InMemoryDataStore store, ILogger<SessionCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
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
                await CleanupStaleRoundsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during session cleanup");
            }

            await Task.Delay(CleanupInterval, stoppingToken);
        }
    }

    private async Task CleanupStaleRoundsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var staleRounds = _store.ActiveRounds
            .Where(kvp => now - kvp.Value.CreatedUtc > StaleRoundThreshold)
            .ToList();

        if (staleRounds.Count == 0)
            return;

        _logger.LogWarning("Cleaning up {Count} stale active rounds", staleRounds.Count);

        // Create a scope to safely resolve the scoped IGameService
        using var scope = _scopeFactory.CreateScope();
        var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();

        foreach (var (roundId, round) in staleRounds)
        {
            // If the round had pending winnings or an active DU session,
            // use CashOutAsync to properly settle DU credits and zero out
            // the session (prevents double-settlement if player reconnects).
            if (round.WinAmount > 0m && !round.IsPayoutSettled)
            {
                try
                {
                    await gameService.CashOutAsync(round.UserId, round.MachineId, cancellationToken, bypassRules: true);
                    _logger.LogInformation("Settled stale round {RoundId} to user {UserId} via CashOut",
                        roundId, round.UserId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to settle stale round {RoundId} for user {UserId}",
                        roundId, round.UserId);
                }
            }

            _store.ActiveRounds.TryRemove(roundId, out _);
        }
    }
}
