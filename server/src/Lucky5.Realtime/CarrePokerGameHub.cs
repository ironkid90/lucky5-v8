namespace Lucky5.Realtime;

using System.Security.Claims;
using System.Collections.Concurrent;
using Lucky5.Application.Contracts;
using Lucky5.Application.Requests;
using Lucky5.Realtime.Services;
using Microsoft.AspNetCore.SignalR;

public sealed class CarrePokerGameHub(IGameService gameService, ConnectionRegistry registry) : Hub
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
    private const string ErrorEvent = "Error";
    private const string CurrentMachineContextKey = "machine-id";

    // Seat-occupancy lock: tracks which machine is occupied by which connection
    private static readonly ConcurrentDictionary<int, string> MachineOccupancy = new();

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
            // Emit UserStatusChanged for lobby presence
            _ = Clients.All.SendAsync(UserStatusChangedEvent, new { userId = GetMemberId(userId), state = "Reconnecting" }, Context.ConnectionAborted);
        }

        if (TryGetCurrentMachineId(out var machineId))
        {
            // Release seat-occupancy lock on disconnect
            MachineOccupancy.TryRemove(machineId, out _);

            _ = Clients.All.SendAsync(MachineStatusChangedEvent, new { machineId, isOccupied = false, playerId = (int?)null, gameId = 0 }, Context.ConnectionAborted);
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

        // Seat-occupancy lock: check if machine is already occupied
        if (MachineOccupancy.TryGetValue(machineId, out var occupyingConnectionId) &&
            occupyingConnectionId != Context.ConnectionId)
        {
            await EmitErrorAsync("MACHINE_OCCUPIED", "Machine is already occupied by another player.");
            throw new HubException("Machine is already occupied by another player.");
        }

        // Release previous machine lock if switching machines
        if (TryGetCurrentMachineId(out var previousMachineId) && previousMachineId != machineId)
        {
            MachineOccupancy.TryRemove(previousMachineId, out _);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(previousMachineId));
        }

        // Acquire lock on new machine
        MachineOccupancy.TryAdd(machineId, Context.ConnectionId);
        Context.Items[CurrentMachineContextKey] = machineId;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(machineId));

        // Emit MachineStatusChanged for lobby presence
        if (TryGetUserId(out var userId))
        {
            await Clients.All.SendAsync(MachineStatusChangedEvent,
                new { machineId, isOccupied = true, playerId = GetMemberId(userId), gameId = 0 },
                Context.ConnectionAborted);
        }

        await BroadcastMachineStateAsync(machineId, Clients.Caller, Context.ConnectionAborted);
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

        // Emit MachineStatusChanged for lobby presence
        await Clients.All.SendAsync(MachineStatusChangedEvent,
            new { machineId, isOccupied = false, playerId = (int?)null, gameId = 0 },
            Context.ConnectionAborted);

        if (TryGetCurrentMachineId(out var currentMachineId) && currentMachineId == machineId)
        {
            Context.Items.Remove(CurrentMachineContextKey);
        }
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
        await Clients.Group(GroupName(machineId)).SendAsync(BetPlacedEvent,
            new { machineId, memberId = GetMemberId(userId), stake = betAmount },
            Context.ConnectionAborted);

        var result = await gameService.DealAsync(
            userId,
            new DealRequest(machineId, betAmount),
            Context.ConnectionAborted);

        await Clients.Caller.SendAsync(CardsDealtEvent, result, Context.ConnectionAborted);
        await BroadcastMachineStateAsync(machineId, Clients.Group(GroupName(machineId)), Context.ConnectionAborted);
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
            await Clients.Group(GroupName(machineId)).SendAsync(HoldCardUpdatedEvent,
                new { machineId, memberId = GetMemberId(userId), holds },
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
            await BroadcastMachineStateAsync(machineId, Clients.Group(GroupName(machineId)), Context.ConnectionAborted);
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
        // Emit DoubleUpWin (v2) instead of RewardStatus (v1)
        await Clients.Caller.SendAsync(DoubleUpWinEvent, result, Context.ConnectionAborted);
        await Clients.Caller.SendAsync("DoubleUpCard", new { roundId, guess }, Context.ConnectionAborted);
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

    public async Task ReconnectSync(int machineId, long lastStateVersion = 0, long lastSequenceNumber = 0)
    {
        Context.Items[CurrentMachineContextKey] = machineId;
        registry.Touch(Context.ConnectionId);

        if (TryGetUserId(out var userId))
        {
            var replay = await gameService.GetCabinetReplayAsync(userId, machineId, lastStateVersion, lastSequenceNumber, Context.ConnectionAborted);
            await Clients.Caller.SendAsync(CabinetReplayEvent, replay, Context.ConnectionAborted);
            if (replay.Snapshot is not null)
            {
                await Clients.Caller.SendAsync(CabinetSnapshotEvent, replay.Snapshot, Context.ConnectionAborted);
            }
        }

        await BroadcastMachineStateAsync(machineId, Clients.Caller, Context.ConnectionAborted);
    }

    private async Task BroadcastMachineStateAsync(int machineId, IClientProxy target, CancellationToken cancellationToken)
    {
        var state = await gameService.GetMachineStateAsync(machineId, cancellationToken);
        await target.SendAsync(MachineStateUpdatedEvent, state, cancellationToken);
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
