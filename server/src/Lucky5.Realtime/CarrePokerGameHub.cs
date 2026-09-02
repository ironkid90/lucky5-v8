namespace Lucky5.Realtime;

using System.Security.Claims;
using System.Collections.Concurrent;
using System.Threading;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Lucky5.Realtime.Services;
using Microsoft.AspNetCore.SignalR;

public sealed class CarrePokerGameHub(IGameService gameService, ConnectionRegistry registry, ISpectatorTracker spectatorTracker) : Hub
{
    // Legacy v1 events (deprecated, kept for backward compatibility during migration)
    private const string MachineStateUpdatedEvent = "MachineStateUpdated";
    private const string CardRevealedEvent = "CardRevealed";
    private const string WalletUpdatedEvent = "WalletUpdated";

    // v2 live protocol events
    private const string CardsDealtEvent = "CardsDealt";
    private const string DoubleUpWinEvent = "DoubleUpWin";
    private const string SwapDoubleUpCardEvent = "SwapDoubleUpCard";
    private const string BetPlacedEvent = "BetPlaced";
    private const string HoldCardUpdatedEvent = "HoldCardUpdated";
    private const string MachineStatusChangedEvent = "MachineStatusChanged";
    private const string UserStatusChangedEvent = "UserStatusChanged";
    private const string CabinetReplayEvent = "CabinetReplay";
    private const string CabinetSnapshotEvent = "CabinetSnapshot";
    private const string LobbyMachinesUpdatedEvent = "LobbyMachinesUpdated";
    private const string ErrorEvent = "Error";
    private const string CurrentMachineContextKey = "machine-id";

    // Seat-occupancy lock: tracks which machine is occupied by which connection
    private static readonly ConcurrentDictionary<int, string> MachineOccupancy = new();

    // Session pause grace period: when a player disconnects, their machine seat is
    // held for this duration so they can reconnect and continue where they left off.
    // This simulates the behavior of a physical arcade cabinet — if you walk away,
    // the machine doesn't instantly reset. Other players see the machine as "busy".
    private static readonly TimeSpan SessionPauseGracePeriod = TimeSpan.FromMinutes(5);

    // Pending disconnect timers: machineId → (userId, timer).
    // When a player disconnects, we start a timer. If they don't reconnect within
    // the grace period, the timer fires and releases the machine lock.
    private static readonly ConcurrentDictionary<int, (Guid UserId, Timer Timer)> PendingDisconnects = new();

    private static string SpectatorGroupName(int machineId) => $"machine:spectate:{machineId}";

    // Public group-name accessors so non-hub senders (REST controllers via
    // IMachineStateNotifier) target the exact same groups.
    public static string MachineGroupNamePublic(int machineId) => GroupName(machineId);
    public static string SpectatorGroupNamePublic(int machineId) => SpectatorGroupName(machineId);

    public override Task OnConnectedAsync()
    {
        if (TryGetUserId(out var userId))
        {
            registry.Add(Context.ConnectionId, userId);
            // Emit UserStatusChanged for lobby presence
            _ = Clients.All.SendAsync(UserStatusChangedEvent, new { userId = GetMemberId(userId), state = "Active" }, Context.ConnectionAborted);
        }

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetUserId(out var userId))
        {
            registry.Remove(Context.ConnectionId);
            // Emit UserStatusChanged for lobby presence — "Reconnecting" state
            _ = Clients.All.SendAsync(UserStatusChangedEvent, new { userId = GetMemberId(userId), state = "Reconnecting" }, Context.ConnectionAborted);
        }

        if (TryGetCurrentMachineId(out var machineId))
        {
            // Race guard: the player may have ALREADY reconnected on a new
            // connection and reclaimed the seat before this disconnect callback
            // ran (server-side disconnect detection via keep-alive timeout is
            // slower than the client's automatic reconnect). If a live
            // connection of the same user now holds the seat, do not arm the
            // auto-cashout timer — the session is continuing just fine.
            var seatReclaimedBySameUser =
                MachineOccupancy.TryGetValue(machineId, out var currentOccupant) &&
                currentOccupant != Context.ConnectionId &&
                registry.TryGetUserId(currentOccupant, out var occupantUserId) &&
                TryGetUserId(out var disconnectedUserId) &&
                occupantUserId == disconnectedUserId;

            if (!seatReclaimedBySameUser)
            {
            // Don't immediately release the machine lock. Instead, start a grace-period
            // timer. If the same player reconnects within the window, they resume their
            // session (DU, win settlement, etc.). If the timer expires, the machine
            // is released and the player's credits are auto-cashed out to their wallet.
            var timer = new Timer(async _timerState =>
            {
                // Grace period expired — auto-cashout and release the machine lock.
                PendingDisconnects.TryRemove(machineId, out _);

                // Final liveness check: if the seat is now held by a live
                // connection (player reconnected but the reclaim raced past the
                // cancel), skip the auto-cashout instead of yanking the credits
                // out from under an active session.
                if (MachineOccupancy.TryGetValue(machineId, out var occupantAtFire) &&
                    occupantAtFire != Context.ConnectionId &&
                    registry.TryGetUserId(occupantAtFire, out var occupantStillLive))
                {
                    return;
                }

                MachineOccupancy.TryRemove(machineId, out _);

                // Auto-cashout: return remaining machine credits to player's wallet
                try
                {
                    await gameService.CashOutAsync(userId, machineId, CancellationToken.None, bypassRules: true);
                }
                catch (Exception ex)
                {
                    // Log but don't throw — we still need to release the machine
                    Console.WriteLine($"[AutoCashout] Failed for user {userId} on machine {machineId}: {ex.Message}");
                }

                _ = Clients.All.SendAsync(MachineStatusChangedEvent,
                    new { machineId, isOccupied = false, playerId = (int?)null, gameId = 0 },
                    CancellationToken.None);
                _ = Clients.All.SendAsync(UserStatusChangedEvent,
                    new { userId = GetMemberId(userId), state = "Idle" },
                    CancellationToken.None);
                _ = BroadcastLobbyMachinesUpdatedAsync(CancellationToken.None);
            }, null, SessionPauseGracePeriod, Timeout.InfiniteTimeSpan);

            PendingDisconnects[machineId] = (userId, timer);

            // Machine still appears occupied to other players during the grace period.
            // The lobby shows "Reconnecting" state.
            }
        }

        if (Context.Items.TryGetValue("spectate-machine-id", out var specObj) && specObj is int specMachineId)
        {
            spectatorTracker.RemoveSpectator(specMachineId, Context.ConnectionId);
            var count = spectatorTracker.GetSpectatorCount(specMachineId);
            _ = Clients.All.SendAsync("SpectatorsChanged", new { machineId = specMachineId, count }, CancellationToken.None);
            _ = BroadcastLobbyMachinesUpdatedAsync(CancellationToken.None);
        }

        Context.Items.Remove(CurrentMachineContextKey);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task JoinMachine(int machineId)
    {
        if (machineId <= 0)
        {
            await EmitErrorAsync("INVALID_MACHINE", "Machine id must be positive.");
            throw new HubException("Machine id must be positive.");
        }

        var hasUserId = TryGetUserId(out var userId);
        var isReclaimingPendingSeat = false;

        // If this machine has a pending disconnect for the same user, cancel
        // the timer — the player is back.
        if (hasUserId &&
            PendingDisconnects.TryGetValue(machineId, out var pending) &&
            pending.UserId == userId &&
            PendingDisconnects.TryRemove(machineId, out var removedPending))
        {
            removedPending.Timer.Dispose();
            isReclaimingPendingSeat = true;
        }

        // Seat-occupancy lock: check if machine is already occupied
        if (MachineOccupancy.TryGetValue(machineId, out var occupyingConnectionId) &&
            occupyingConnectionId != Context.ConnectionId)
        {
            if (isReclaimingPendingSeat)
            {
                MachineOccupancy[machineId] = Context.ConnectionId;
            }
            else
            {
                await EmitErrorAsync("MACHINE_OCCUPIED", "Machine is already occupied by another player.");
                throw new HubException("Machine is already occupied by another player.");
            }
        }
        else
        {
            // Acquire lock on new machine when not currently occupied.
            MachineOccupancy[machineId] = Context.ConnectionId;
        }

        // Release previous machine lock if switching machines
        if (TryGetCurrentMachineId(out var previousMachineId) && previousMachineId != machineId)
        {
            MachineOccupancy.TryRemove(previousMachineId, out _);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(previousMachineId));
        }

        Context.Items[CurrentMachineContextKey] = machineId;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(machineId));

        // Emit MachineStatusChanged for lobby presence
        if (hasUserId)
        {
            await Clients.All.SendAsync(MachineStatusChangedEvent,
                new { machineId, isOccupied = true, playerId = GetMemberId(userId), gameId = 0 },
                Context.ConnectionAborted);
            _ = BroadcastLobbyMachinesUpdatedAsync(Context.ConnectionAborted);
        }

        await BroadcastMachineStateAsync(machineId, Clients.Caller, Context.ConnectionAborted, userId);
    }

    public async Task LeaveMachine(int machineId)
    {
        if (machineId <= 0)
        {
            return;
        }

        // Release seat-occupancy lock
        MachineOccupancy.TryRemove(machineId, out _);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(machineId));

        if (TryGetUserId(out var userId))
        {
            try
            {
                await gameService.CashOutAsync(userId, machineId, Context.ConnectionAborted, bypassRules: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LeaveMachine] Auto-cashout error for user {userId} on machine {machineId}: {ex.Message}");
            }
        }

        // Emit MachineStatusChanged for lobby presence
        await Clients.All.SendAsync(MachineStatusChangedEvent,
            new { machineId, isOccupied = false, playerId = (int?)null, gameId = 0 },
            Context.ConnectionAborted);
        _ = BroadcastLobbyMachinesUpdatedAsync(Context.ConnectionAborted);

        if (TryGetCurrentMachineId(out var currentMachineId) && currentMachineId == machineId)
        {
            Context.Items.Remove(CurrentMachineContextKey);
        }
    }

    public async Task JoinMachineAsSpectator(int machineId)
    {
        if (machineId <= 0) return;

        spectatorTracker.AddSpectator(machineId, Context.ConnectionId);

        Context.Items["spectate-machine-id"] = machineId;
        await Groups.AddToGroupAsync(Context.ConnectionId, SpectatorGroupName(machineId));
        
        var count = spectatorTracker.GetSpectatorCount(machineId);
        await Clients.All.SendAsync("SpectatorsChanged", new { machineId, count }, Context.ConnectionAborted);
        _ = BroadcastLobbyMachinesUpdatedAsync(Context.ConnectionAborted);
        await BroadcastMachineStateAsync(machineId, Clients.Caller, Context.ConnectionAborted, TryGetUserId(out var spectUserId) ? spectUserId : (Guid?)null);
    }

    public async Task LeaveMachineAsSpectator(int machineId)
    {
        if (machineId <= 0) return;

        spectatorTracker.RemoveSpectator(machineId, Context.ConnectionId);

        Context.Items.Remove("spectate-machine-id");
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, SpectatorGroupName(machineId));

        var count = spectatorTracker.GetSpectatorCount(machineId);
        await Clients.All.SendAsync("SpectatorsChanged", new { machineId, count }, Context.ConnectionAborted);
        _ = BroadcastLobbyMachinesUpdatedAsync(Context.ConnectionAborted);
    }

    public async Task Deal(int machineId, decimal betAmount)
    {
        if (!TryGetUserId(out var userId))
        {
            await EmitErrorAsync("UNAUTHORIZED", "Unauthorized");
            throw new HubException("Unauthorized");
        }

        if (machineId <= 0)
        {
            await EmitErrorAsync("INVALID_MACHINE", "Machine id must be positive.");
            throw new HubException("Machine id must be positive.");
        }

        if (betAmount <= 0)
        {
            await EmitErrorAsync("INVALID_BET", "Bet amount must be positive.");
            throw new HubException("Bet amount must be positive.");
        }

        Context.Items[CurrentMachineContextKey] = machineId;

        // Emit BetPlaced for presentation sync
        var cursor = await gameService.GetCabinetStateCursorAsync(userId, machineId, Context.ConnectionAborted);
        await Clients.Groups(GroupName(machineId), SpectatorGroupName(machineId)).SendAsync(BetPlacedEvent,
            new { machineId, memberId = GetMemberId(userId), stake = betAmount, stateVersion = cursor.StateVersion, sequenceNumber = cursor.SequenceNumber },
            Context.ConnectionAborted);

        var result = await gameService.DealAsync(
            userId,
            new DealRequest(machineId, betAmount),
            Context.ConnectionAborted);

        await Clients.Caller.SendAsync(CardsDealtEvent, result, Context.ConnectionAborted);
        await BroadcastMachineStateAsync(machineId, Clients.Groups(GroupName(machineId), SpectatorGroupName(machineId)), Context.ConnectionAborted, userId);
    }

    public async Task Draw(Guid roundId, int[] holdIndexes)
    {
        if (!TryGetUserId(out var userId))
        {
            await EmitErrorAsync("UNAUTHORIZED", "Unauthorized");
            throw new HubException("Unauthorized");
        }

        var normalizedHoldIndexes = (holdIndexes ?? [])
            .Where(index => index >= 0 && index < 5)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        // Emit HoldCardUpdated for presentation sync
        if (TryGetCurrentMachineId(out var machineId))
        {
            var holds = new bool[5];
            foreach (var index in normalizedHoldIndexes)
            {
                if (index >= 0 && index < 5)
                {
                    holds[index] = true;
                }
            }
            var cursor = await gameService.GetCabinetStateCursorAsync(userId, machineId, Context.ConnectionAborted);
            await Clients.Groups(GroupName(machineId), SpectatorGroupName(machineId)).SendAsync(HoldCardUpdatedEvent,
                new { machineId, memberId = GetMemberId(userId), holds, stateVersion = cursor.StateVersion, sequenceNumber = cursor.SequenceNumber },
                Context.ConnectionAborted);
        }

        var result = await gameService.DrawAsync(
            userId,
            new DrawRequest(roundId, normalizedHoldIndexes),
            Context.ConnectionAborted);

        await Clients.Caller.SendAsync(CardRevealedEvent, result, Context.ConnectionAborted);
        await Clients.Caller.SendAsync(
            WalletUpdatedEvent,
            new
            {
                result.RoundId,
                result.WalletBalanceAfterRound
            },
            Context.ConnectionAborted);

        if (TryGetCurrentMachineId(out machineId))
        {
            await BroadcastMachineStateAsync(machineId, Clients.Groups(GroupName(machineId), SpectatorGroupName(machineId)), Context.ConnectionAborted, userId);
        }
    }

    public async Task DoubleUp(Guid roundId, string guess)
    {
        if (!TryGetUserId(out var userId))
        {
            await EmitErrorAsync("UNAUTHORIZED", "Unauthorized");
            throw new HubException("Unauthorized");
        }

        var result = await gameService.GuessDoubleUpAsync(userId, roundId, guess, Context.ConnectionAborted);
        var duMachineId = TryGetCurrentMachineId(out var mId) ? mId : 0;
        var cursor = duMachineId > 0
            ? await gameService.GetCabinetStateCursorAsync(userId, duMachineId, Context.ConnectionAborted)
            : (StateVersion: 0L, SequenceNumber: 0L);
        // Emit DoubleUpWin (v2) instead of RewardStatus (v1)
        await Clients.Caller.SendAsync(DoubleUpWinEvent, result with { StateVersion = cursor.StateVersion, SequenceNumber = cursor.SequenceNumber }, Context.ConnectionAborted);
        await Clients.Caller.SendAsync("DoubleUpCard", new { roundId, guess }, Context.ConnectionAborted);

        // Broadcast machine state to group and spectators so all watchers see the DU result
        if (duMachineId > 0)
        {
            await BroadcastMachineStateAsync(duMachineId, Clients.Groups(GroupName(duMachineId), SpectatorGroupName(duMachineId)), Context.ConnectionAborted, userId);
        }
    }

    public async Task StartDoubleUp(Guid roundId)
    {
        if (!TryGetUserId(out var userId))
        {
            await EmitErrorAsync("UNAUTHORIZED", "Unauthorized");
            throw new HubException("Unauthorized");
        }

        var result = await gameService.StartDoubleUpAsync(userId, roundId, Context.ConnectionAborted);
        var duMachineId = TryGetCurrentMachineId(out var mId) ? mId : 0;
        var cursor = duMachineId > 0
            ? await gameService.GetCabinetStateCursorAsync(userId, duMachineId, Context.ConnectionAborted)
            : (StateVersion: 0L, SequenceNumber: 0L);
        await Clients.Caller.SendAsync(DoubleUpWinEvent, result with { StateVersion = cursor.StateVersion, SequenceNumber = cursor.SequenceNumber }, Context.ConnectionAborted);

        // Broadcast machine state to group and spectators
        if (duMachineId > 0)
        {
            await BroadcastMachineStateAsync(duMachineId, Clients.Groups(GroupName(duMachineId), SpectatorGroupName(duMachineId)), Context.ConnectionAborted, userId);
        }
    }

    public async Task SwitchDoubleUpDealer(Guid roundId)
    {
        if (!TryGetUserId(out var userId))
        {
            await EmitErrorAsync("UNAUTHORIZED", "Unauthorized");
            throw new HubException("Unauthorized");
        }

        var result = await gameService.SwitchDealerAsync(userId, roundId, Context.ConnectionAborted);
        var duMachineId = TryGetCurrentMachineId(out var mId) ? mId : 0;
        var cursor = duMachineId > 0
            ? await gameService.GetCabinetStateCursorAsync(userId, duMachineId, Context.ConnectionAborted)
            : (StateVersion: 0L, SequenceNumber: 0L);
        await Clients.Caller.SendAsync(DoubleUpWinEvent, result with { StateVersion = cursor.StateVersion, SequenceNumber = cursor.SequenceNumber }, Context.ConnectionAborted);

        // Broadcast machine state to group and spectators
        if (duMachineId > 0)
        {
            await BroadcastMachineStateAsync(duMachineId, Clients.Groups(GroupName(duMachineId), SpectatorGroupName(duMachineId)), Context.ConnectionAborted, userId);
        }
    }

    public async Task SwapDoubleUpCard(Guid roundId, int swapPosition)
    {
        if (!TryGetUserId(out var userId))
        {
            await EmitErrorAsync("UNAUTHORIZED", "Unauthorized");
            throw new HubException("Unauthorized");
        }

        var result = await gameService.SwapDoubleUpCardAsync(userId, roundId, swapPosition, Context.ConnectionAborted);
        var duMachineId = TryGetCurrentMachineId(out var mId) ? mId : 0;
        var cursor = duMachineId > 0
            ? await gameService.GetCabinetStateCursorAsync(userId, duMachineId, Context.ConnectionAborted)
            : (StateVersion: 0L, SequenceNumber: 0L);
        await Clients.Caller.SendAsync(SwapDoubleUpCardEvent, result with { StateVersion = cursor.StateVersion, SequenceNumber = cursor.SequenceNumber }, Context.ConnectionAborted);
    }

    public async Task CashoutDoubleUp(Guid roundId)
    {
        if (!TryGetUserId(out var userId))
        {
            await EmitErrorAsync("UNAUTHORIZED", "Unauthorized");
            throw new HubException("Unauthorized");
        }

        var result = await gameService.CashoutDoubleUpAsync(userId, roundId, Context.ConnectionAborted);
        var duMachineId = TryGetCurrentMachineId(out var mId) ? mId : 0;
        var cursor = duMachineId > 0
            ? await gameService.GetCabinetStateCursorAsync(userId, duMachineId, Context.ConnectionAborted)
            : (StateVersion: 0L, SequenceNumber: 0L);
        await Clients.Caller.SendAsync(DoubleUpWinEvent, result with { StateVersion = cursor.StateVersion, SequenceNumber = cursor.SequenceNumber }, Context.ConnectionAborted);

        // Broadcast machine state to group and spectators — cashout changes credits
        if (duMachineId > 0)
        {
            await BroadcastMachineStateAsync(duMachineId, Clients.Groups(GroupName(duMachineId), SpectatorGroupName(duMachineId)), Context.ConnectionAborted, userId);
        }
    }

    public async Task TakeHalfDoubleUp(Guid roundId)
    {
        if (!TryGetUserId(out var userId))
        {
            await EmitErrorAsync("UNAUTHORIZED", "Unauthorized");
            throw new HubException("Unauthorized");
        }

        var result = await gameService.TakeHalfAsync(userId, roundId, Context.ConnectionAborted);
        await Clients.Caller.SendAsync(DoubleUpWinEvent, result, Context.ConnectionAborted);
    }

    public Task Heartbeat()
    {
        registry.Touch(Context.ConnectionId);
        return Task.CompletedTask;
    }

    public async Task GetAvailableMachines(int gameId)
    {
        var machines = await gameService.GetMachinesAsync(Context.ConnectionAborted);
        await Clients.Caller.SendAsync("AvailableMachines", machines, Context.ConnectionAborted);
    }

    public async Task GetLobbySnapshot(CancellationToken cancellationToken = default)
    {
        await BroadcastLobbyMachinesUpdatedAsync(cancellationToken);
    }

    public async Task ReconnectSync(int machineId, long lastStateVersion = 0, long lastSequenceNumber = 0)
    {
        Context.Items[CurrentMachineContextKey] = machineId;
        registry.Touch(Context.ConnectionId);

        if (TryGetUserId(out var userId))
        {
            // The player is back — cancel the pending-disconnect auto-cashout
            // timer. Without this, a player who recovers via ReconnectSync (the
            // primary client reconnect path) still gets cashed out and loses
            // their seat when the grace-period timer fires mid-session.
            var reclaimedPendingSeat = false;
            if (PendingDisconnects.TryGetValue(machineId, out var pending) &&
                pending.UserId == userId &&
                PendingDisconnects.TryRemove(machineId, out var removedPending))
            {
                removedPending.Timer.Dispose();
                reclaimedPendingSeat = true;
            }

            // Reclaim the seat for this connection. SignalR reconnects with a NEW
            // connection id and group memberships do not survive reconnects, so
            // both the occupancy map and the group must be refreshed — otherwise
            // the seat points at a dead connection (machine looks occupied forever,
            // or freed by the timer while the player is still playing).
            var seatReclaimed = false;
            if (MachineOccupancy.TryGetValue(machineId, out var occupyingConnectionId))
            {
                if (occupyingConnectionId == Context.ConnectionId)
                {
                    seatReclaimed = true;
                }
                else if (reclaimedPendingSeat
                    || !registry.TryGetUserId(occupyingConnectionId, out var occupyingUserId)
                    || occupyingUserId == userId)
                {
                    // Our own pending seat, a dead occupant connection, or another
                    // connection of the SAME user (covers the race where this
                    // reconnect is processed before the old connection's
                    // OnDisconnectedAsync). A live seat owned by a different user
                    // is never stolen here.
                    MachineOccupancy[machineId] = Context.ConnectionId;
                    seatReclaimed = true;
                }
            }
            else
            {
                MachineOccupancy[machineId] = Context.ConnectionId;
                seatReclaimed = true;
            }

            if (seatReclaimed)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(machineId));
            }

            var replay = await gameService.GetCabinetReplayAsync(userId, machineId, lastStateVersion, lastSequenceNumber, Context.ConnectionAborted);
            await Clients.Caller.SendAsync(CabinetReplayEvent, replay, Context.ConnectionAborted);
            if (replay.Snapshot is not null)
            {
                await Clients.Caller.SendAsync(CabinetSnapshotEvent, replay.Snapshot, Context.ConnectionAborted);
            }

            await BroadcastMachineStateAsync(machineId, Clients.Caller, Context.ConnectionAborted, userId);
        }
        else
        {
            await BroadcastMachineStateAsync(machineId, Clients.Caller, Context.ConnectionAborted);
        }
    }

    private async Task BroadcastMachineStateAsync(int machineId, IClientProxy target, CancellationToken cancellationToken, Guid? userId = null)
    {
        var state = await gameService.GetMachineStateAsync(machineId, cancellationToken, userId);
        await target.SendAsync(MachineStateUpdatedEvent, state, cancellationToken);
    }

    private async Task BroadcastLobbyMachinesUpdatedAsync(CancellationToken cancellationToken)
    {
        var lobbyMachines = await gameService.GetLobbyMachinesAsync(Guid.Empty, cancellationToken);

        var result = new List<LobbyMachineInfo>();
        foreach (var machine in lobbyMachines)
        {
            int? occupantUserId = null;
            var isOccupied = MachineOccupancy.ContainsKey(machine.Id);
            if (isOccupied && MachineOccupancy.TryGetValue(machine.Id, out var connectionId)
                && registry.TryGetUserId(connectionId, out var occUserId))
            {
                occupantUserId = GetMemberId(occUserId);
            }
            result.Add(new LobbyMachineInfo(machine.Id, isOccupied, occupantUserId, machine.SpectatorCount, machine.OccupiedByUsername, machine.IdleSecondsRemaining, machine.ReservedUntilUtc));
        }

        await Clients.All.SendAsync(LobbyMachinesUpdatedEvent, result, cancellationToken);
    }

    private Task EmitErrorAsync(string code, string message)
        => Clients.Caller.SendAsync(ErrorEvent, new { code, message }, Context.ConnectionAborted);

    private bool TryGetCurrentMachineId(out int machineId)
    {
        machineId = 0;

        if (!Context.Items.TryGetValue(CurrentMachineContextKey, out var value) || value is null)
        {
            return false;
        }

        return value switch
        {
            int intValue => (machineId = intValue) > 0,
            long longValue => (machineId = checked((int)longValue)) > 0,
            string stringValue when int.TryParse(stringValue, out var parsed) => (machineId = parsed) > 0,
            _ => false
        };
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var value = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return value is not null && Guid.TryParse(value, out userId);
    }

    private static int GetMemberId(Guid userId)
    {
        // TODO: In production, this should map Guid to the actual integer memberId from the database
        // For now, use a simple hash to generate a stable integer
        return Math.Abs(userId.GetHashCode() % 1000000);
    }

    private static string GroupName(int machineId) => $"machine:{machineId}";
}
