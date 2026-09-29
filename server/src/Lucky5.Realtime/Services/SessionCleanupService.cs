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

    // Sessions with residue (credits or a reserved stake) but no active round and no
    // update for this long are treated as abandoned — the crash/kill-without-disconnect
    // case the hub's grace timer can never see. The threshold is deliberately longer
    // than the hub's 5-minute reconnect grace period so a player in a normal reconnect
    // window is never swept out from under their own session.
    private static readonly TimeSpan StaleSessionThreshold = TimeSpan.FromMinutes(15);

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
                await CleanupStaleSessionsAsync(stoppingToken);
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
                    // Keep the round in ActiveRounds so the next cleanup tick
                    // retries the settlement instead of dropping it silently.
                    _logger.LogWarning(ex, "Failed to settle stale round {RoundId} for user {UserId}; will retry next sweep",
                        roundId, round.UserId);
                    continue;
                }
            }

            _store.ActiveRounds.TryRemove(roundId, out _);
        }
    }

    /// <summary>
    /// Safety net for the crash/kill-without-disconnect case: ages SESSIONS by
    /// LastUpdatedUtc (not rounds) and force-settles any that hold residue — machine
    /// credits or a reserved stake — with no active round. The hub's grace timer only
    /// ever arms if OnDisconnectedAsync ran; when the process never saw a disconnect
    /// (server restart, network kill, app crash), the session would otherwise sit
    /// occupied with credits forever. Settlement funnels through the hub's single
    /// ForceReleaseMachineCoreAsync path (reserved stake released, pending round
    /// resolved, credits cashed out to the wallet, seat released, lobby notified).
    /// </summary>
    private async Task CleanupStaleSessionsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // Machines with any live active round are settled by CleanupStaleRoundsAsync /
        // the round's own cashout path — never sweep a session out from under one.
        var machinesWithActiveRound = new HashSet<int>(_store.ActiveRounds.Values.Select(r => r.MachineId));

        var staleSessions = _store.MachineSessions.Values
            .Where(s => now - s.LastUpdatedUtc > StaleSessionThreshold)
            .Where(s => s.MachineCredits > 0m || s.ReservedStake > 0m)
            .Where(s => !machinesWithActiveRound.Contains(s.MachineId))
            .ToList();

        if (staleSessions.Count == 0)
            return;

        _logger.LogWarning("Force-settling {Count} abandoned machine sessions", staleSessions.Count);

        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<CarrePokerGameHub>>();
        var registry = scope.ServiceProvider.GetRequiredService<ConnectionRegistry>();

        foreach (var session in staleSessions)
        {
            try
            {
                var result = await CarrePokerGameHub.ForceReleaseMachineCoreAsync(
                    session.MachineId,
                    _scopeFactory,
                    hubContext,
                    registry,
                    CarrePokerGameHub.ForceReleaseReason.StaleSessionSweep,
                    expectedUserId: session.UserId);
                _logger.LogInformation(
                    "Stale session sweep settled machine {MachineId} (user {UserId}): occupied={WasOccupied} settled={Settled}",
                    session.MachineId, session.UserId, result.WasOccupied, result.Settled);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Stale session sweep failed for machine {MachineId} (user {UserId}); will retry next sweep",
                    session.MachineId, session.UserId);
            }
        }
    }
}
