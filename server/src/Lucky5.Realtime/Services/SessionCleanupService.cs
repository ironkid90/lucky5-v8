namespace Lucky5.Realtime.Services;

using Lucky5.Domain.Entities;
using Lucky5.Application.Contracts;
using Lucky5.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    // IGameService is scoped; this hosted service is a singleton, so it must
    // create a scope per settlement instead of injecting IGameService directly
    // (doing so crashes the host at startup under scope validation).
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

            // If the round had pending winnings or an active DU session,
            // use CashOutAsync to properly settle DU credits and zero out
            // the session (prevents double-settlement if player reconnects).
            if (round.WinAmount > 0m && !round.IsPayoutSettled)
            {
                _ = SettleRoundAsync(round.UserId, round.MachineId, roundId);
            }
        }
    }

    private async Task SettleRoundAsync(Guid userId, int machineId, Guid roundId)
    {
        try
        {
            // Own scope: the settlement outlives the cleanup tick.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();
            await gameService.CashOutAsync(userId, machineId, CancellationToken.None, bypassRules: true);
            _logger.LogInformation("Settled stale round {RoundId} to user {UserId} via CashOut", roundId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to settle stale round {RoundId} for user {UserId}", roundId, userId);
        }
    }
}
