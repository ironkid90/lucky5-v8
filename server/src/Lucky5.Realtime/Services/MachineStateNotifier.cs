namespace Lucky5.Realtime.Services;

using System.Text.Json;
using Lucky5.Application.Contracts;
using Lucky5.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Broadcasts authoritative machine state to the machine group (active player)
/// and spectator group after REST-driven mutations. Hub-driven gameplay already
/// broadcasts from the hub; REST gameplay previously produced no realtime
/// updates at all, leaving spectators frozen on the join-time snapshot.
/// The payload merges the lightweight machine-state dictionary (jackpots,
/// cursor) with the full cabinet snapshot (game_state, hand, double_up, ...)
/// so watchers see live cards — the same shape the hub spectator path and the
/// client's snapshot-restore pipeline already understand.
/// </summary>
public sealed class MachineStateNotifier(
    IHubContext<CarrePokerGameHub> hub,
    IServiceScopeFactory scopeFactory,
    ILogger<MachineStateNotifier> logger) : IMachineStateNotifier
{
    private const string MachineStateUpdatedEvent = "MachineStateUpdated";
    // Web defaults: camelCase for plain properties; JsonPropertyName attributes
    // on CabinetSnapshotDto keep the snake_case contract fields intact.
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task MachineStateChangedAsync(int machineId, Guid? userId, CancellationToken cancellationToken = default)
    {
        if (machineId <= 0) return;

        try
        {
            // A dedicated scope: IGameService/IDataStore are scoped and the
            // caller's request scope may already be tearing down.
            await using var scope = scopeFactory.CreateAsyncScope();
            var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();
            var payload = await BuildPayloadAsync(gameService, machineId, userId);
            await hub.Clients
                .Groups(CarrePokerGameHub.MachineGroupNamePublic(machineId), CarrePokerGameHub.SpectatorGroupNamePublic(machineId))
                .SendAsync(MachineStateUpdatedEvent, payload, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Fan-out is best-effort; the REST response remains authoritative.
            logger.LogWarning(ex, "Machine state broadcast failed for machine {MachineId}", machineId);
        }
    }

    public async Task MachineStateChangedForRoundAsync(Guid roundId, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IDataStore>();
            var round = await store.GetRoundAsync(roundId);
            if (round is null) return;

            var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();
            var payload = await BuildPayloadAsync(gameService, round.MachineId, userId);
            await hub.Clients
                .Groups(CarrePokerGameHub.MachineGroupNamePublic(round.MachineId), CarrePokerGameHub.SpectatorGroupNamePublic(round.MachineId))
                .SendAsync(MachineStateUpdatedEvent, payload, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Machine state broadcast failed for round {RoundId}", roundId);
        }
    }

    private static async Task<object> BuildPayloadAsync(IGameService gameService, int machineId, Guid? userId)
    {
        var state = await gameService.GetMachineStateAsync(machineId, CancellationToken.None, userId);
        if (!userId.HasValue || state is not IDictionary<string, object> dict)
        {
            return state;
        }

        // Overlay the acting player's cabinet snapshot so the push carries the
        // full game state (cards, DU session, credits) — not just counters.
        // The "jackpot"/"Jackpot" key is skipped: the state dictionary's
        // "jackpots" entry is the shape the client's display path expects.
        // Snapshot build failures (e.g. session just closed) degrade the push
        // to the plain state dictionary instead of dropping it entirely.
        Lucky5.Application.Dtos.CabinetSnapshotDto? snapshot = null;
        try
        {
            snapshot = await gameService.GetCabinetSnapshotAsync(userId.Value, machineId, CancellationToken.None);
        }
        catch (Exception)
        {
            // fall through with the plain machine-state payload
        }

        if (snapshot is null)
        {
            return state;
        }

        var snapshotJson = JsonSerializer.SerializeToElement(snapshot, SnapshotJsonOptions);
        foreach (var property in snapshotJson.EnumerateObject())
        {
            if (property.NameEquals("jackpot") || property.NameEquals("Jackpot"))
            {
                continue;
            }

            dict[property.Name] = property.Value;
        }

        return dict;
    }
}
