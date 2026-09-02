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
            await FanOutAsync(machineId, payload);
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
            await FanOutAsync(round.MachineId, payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Machine state broadcast failed for round {RoundId}", roundId);
        }
    }

    private async Task FanOutAsync(int machineId, object payload)
    {
        // The machine group (the seated player's own connections) receives the
        // full payload. Spectators receive a sanitized copy: cards, DU state and
        // machine credits are cabinet-visible (a bystander sees them on a
        // physical machine), but wallet balance, credit balance, lifetime
        // cash-in, and session identity are private to the player.
        var spectatorPayload = SanitizeForSpectators(payload);

        // CancellationToken.None is intentional: the broadcast is fire-and-forget
        // from the controller and must outlive the originating request — aborting
        // fan-out when the acting player's HTTP request completes would drop
        // spectator updates on every quick disconnect.
        await hub.Clients
            .Group(CarrePokerGameHub.MachineGroupNamePublic(machineId))
            .SendAsync(MachineStateUpdatedEvent, payload, CancellationToken.None);
        await hub.Clients
            .Group(CarrePokerGameHub.SpectatorGroupNamePublic(machineId))
            .SendAsync(MachineStateUpdatedEvent, spectatorPayload, CancellationToken.None);
    }

    private static readonly string[] PrivateCreditFields = ["wallet_balance", "credit_balance", "total_cash_in"];
    private static readonly string[] PrivateSessionFields = ["authenticated_user_id", "session_id"];

    private static object SanitizeForSpectators(object payload)
    {
        if (payload is not IDictionary<string, object> dict)
        {
            return payload;
        }

        var copy = new Dictionary<string, object>(dict);
        RedactJsonElementFields(copy, "credits", PrivateCreditFields);
        RedactJsonElementFields(copy, "session", PrivateSessionFields);
        return copy;
    }

    private static void RedactJsonElementFields(Dictionary<string, object> dict, string sectionKey, string[] fields)
    {
        if (!dict.TryGetValue(sectionKey, out var sectionObj)
            || sectionObj is not JsonElement section
            || section.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var sanitized = new Dictionary<string, object>();
        foreach (var property in section.EnumerateObject())
        {
            if (fields.Any(f => property.NameEquals(f)))
            {
                sanitized[property.Name] = "0";
                continue;
            }
            sanitized[property.Name] = property.Value;
        }
        dict[sectionKey] = sanitized;
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
