namespace Lucky5.Tests;

using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Domain.Entities;
using Lucky5.Domain.Game.CleanRoom;
using Lucky5.Infrastructure.Data.Repositories;
using Lucky5.Infrastructure.Services;

public static class CabinetCommandIntegrityTests
{
    public static async Task RunAsync(List<string> failures)
    {
        await CashInCommandUsesAuthoritativeDualWalletAndIsIdempotentAsync(failures);
        await DuplicateCommandWithDifferentContentIsRejectedAsync(failures);
        await StaleExpectedStateVersionRejectsBeforeMutationAsync(failures);
        await DoubleUpSwitchCommandsConsumeNextDealerAsync(failures);
        await JackpotRankChangeCommandUpdatesIdleFundedSessionAsync(failures);
    }

    private static async Task CashInCommandUsesAuthoritativeDualWalletAndIsIdempotentAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var service = CreateService(store);
        var userId = Guid.Parse("22000000-0000-0000-0000-000000000001");
        SeedPlayer(store, userId, "cabinet-cashin", walletBalance: 100_000m, credit: 150_000m);
        var machineId = store.Machines.Values.First(machine => machine.IsOpen).Id;
        var session = await service.GetMachineSessionAsync(userId, machineId, CancellationToken.None);
        var snapshot = await service.GetCabinetSnapshotAsync(userId, machineId, CancellationToken.None);

        var command = BuildCommand(
            commandId: Guid.Parse("22000000-1000-0000-0000-000000000001"),
            type: "cash_in",
            machineId,
            session.SessionId,
            expectedStateVersion: snapshot.StateVersion,
            idempotencyKey: "cashin-command-0001",
            payload: new Dictionary<string, object?> { ["amount"] = "200000" });

        var accepted = await service.SubmitCabinetCommandAsync(userId, command, CancellationToken.None);
        var profileAfterFirst = store.MemberProfiles[userId];
        var sessionAfterFirst = GetSession(store, userId, machineId);
        var ledgerAfterFirst = store.Ledger.Where(row => row.UserId == userId).ToArray();

        Assert(
            failures,
            "Cabinet cash-in should be accepted and advance backend-owned state/sequence versions.",
            accepted.Accepted && accepted.Status == "accepted" && accepted.StateVersion == snapshot.StateVersion + 1 && accepted.SequenceNumber == snapshot.SequenceNumber + 1);
        Assert(
            failures,
            "Cabinet cash-in should consume Credit before WalletBalance under backend authority.",
            profileAfterFirst.Credit == 0m && profileAfterFirst.WalletBalance == 50_000m);
        Assert(
            failures,
            "Cabinet cash-in should credit the authoritative machine session exactly once.",
            sessionAfterFirst.MachineCredits == 200_000m && sessionAfterFirst.TotalCashIn == 200_000m);
        Assert(
            failures,
            "Cabinet cash-in should append one wallet ledger row with the wallet balance after debit.",
            ledgerAfterFirst.Length == 1
            && ledgerAfterFirst[0].Amount == -200_000m
            && ledgerAfterFirst[0].BalanceAfter == 50_000m
            && ledgerAfterFirst[0].Type == "MachineCashIn");

        var duplicate = await service.SubmitCabinetCommandAsync(userId, command, CancellationToken.None);
        var profileAfterDuplicate = store.MemberProfiles[userId];
        var sessionAfterDuplicate = GetSession(store, userId, machineId);
        var ledgerAfterDuplicate = store.Ledger.Where(row => row.UserId == userId).ToArray();

        Assert(
            failures,
            "Replaying the identical cabinet command should return an idempotent duplicate result, not execute the debit again.",
            duplicate.Accepted
            && duplicate.Status == "duplicate"
            && duplicate.StateVersion == accepted.StateVersion
            && duplicate.SequenceNumber == accepted.SequenceNumber);
        Assert(
            failures,
            "Duplicate cabinet cash-in should not debit wallet/credit a second time.",
            profileAfterDuplicate.Credit == 0m && profileAfterDuplicate.WalletBalance == 50_000m);
        Assert(
            failures,
            "Duplicate cabinet cash-in should not add machine credits or ledger entries a second time.",
            sessionAfterDuplicate.MachineCredits == 200_000m
            && sessionAfterDuplicate.TotalCashIn == 200_000m
            && ledgerAfterDuplicate.Length == 1);
    }

    private static async Task DuplicateCommandWithDifferentContentIsRejectedAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var service = CreateService(store);
        var userId = Guid.Parse("22000000-0000-0000-0000-000000000002");
        SeedPlayer(store, userId, "cabinet-conflict", walletBalance: 1_000_000m, credit: 0m);
        var machineId = store.Machines.Values.First(machine => machine.IsOpen).Id;
        var session = await service.GetMachineSessionAsync(userId, machineId, CancellationToken.None);
        var snapshot = await service.GetCabinetSnapshotAsync(userId, machineId, CancellationToken.None);

        var first = BuildCommand(
            commandId: Guid.Parse("22000000-1000-0000-0000-000000000002"),
            type: "cash_in",
            machineId,
            session.SessionId,
            expectedStateVersion: snapshot.StateVersion,
            idempotencyKey: "cashin-command-0002",
            payload: new Dictionary<string, object?> { ["amount"] = "200000" });

        var accepted = await service.SubmitCabinetCommandAsync(userId, first, CancellationToken.None);
        var walletAfterFirst = store.MemberProfiles[userId].WalletBalance;
        var ledgerCountAfterFirst = store.Ledger.Count(row => row.UserId == userId);

        var conflicting = first with
        {
            CommandId = Guid.Parse("22000000-1000-0000-0000-000000000003"),
            ExpectedStateVersion = accepted.StateVersion,
            Payload = new Dictionary<string, object?> { ["amount"] = "400000" }
        };

        var rejected = await service.SubmitCabinetCommandAsync(userId, conflicting, CancellationToken.None);

        Assert(
            failures,
            "Reusing a cabinet idempotency key with different command content should be rejected as a conflict.",
            !rejected.Accepted
            && rejected.Status == "rejected"
            && rejected.Error?.Code == "IDEMPOTENCY_CONFLICT"
            && rejected.Snapshot is not null);
        Assert(
            failures,
            "Conflicting duplicate cabinet command should not mutate wallet, machine credits, or ledger rows.",
            store.MemberProfiles[userId].WalletBalance == walletAfterFirst
            && GetSession(store, userId, machineId).MachineCredits == 200_000m
            && store.Ledger.Count(row => row.UserId == userId) == ledgerCountAfterFirst);
    }

    private static async Task StaleExpectedStateVersionRejectsBeforeMutationAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var service = CreateService(store);
        var userId = Guid.Parse("22000000-0000-0000-0000-000000000003");
        SeedPlayer(store, userId, "cabinet-stale", walletBalance: 1_000_000m, credit: 0m);
        var machineId = store.Machines.Values.First(machine => machine.IsOpen).Id;
        var session = await service.GetMachineSessionAsync(userId, machineId, CancellationToken.None);
        var snapshot = await service.GetCabinetSnapshotAsync(userId, machineId, CancellationToken.None);

        var first = BuildCommand(
            commandId: Guid.Parse("22000000-1000-0000-0000-000000000004"),
            type: "cash_in",
            machineId,
            session.SessionId,
            expectedStateVersion: snapshot.StateVersion,
            idempotencyKey: "cashin-command-0003",
            payload: new Dictionary<string, object?> { ["amount"] = "200000" });

        var accepted = await service.SubmitCabinetCommandAsync(userId, first, CancellationToken.None);
        var walletAfterFirst = store.MemberProfiles[userId].WalletBalance;
        var ledgerCountAfterFirst = store.Ledger.Count(row => row.UserId == userId);

        var stale = BuildCommand(
            commandId: Guid.Parse("22000000-1000-0000-0000-000000000005"),
            type: "cash_in",
            machineId,
            session.SessionId,
            expectedStateVersion: snapshot.StateVersion,
            idempotencyKey: "cashin-command-0004",
            payload: new Dictionary<string, object?> { ["amount"] = "200000" });

        var rejected = await service.SubmitCabinetCommandAsync(userId, stale, CancellationToken.None);

        Assert(
            failures,
            "Cabinet command with stale expected_state_version should be rejected with a recovery snapshot.",
            !rejected.Accepted
            && rejected.Status == "stale_state"
            && rejected.Error?.Code == "STALE_STATE"
            && rejected.Snapshot is not null
            && rejected.StateVersion == accepted.StateVersion);
        Assert(
            failures,
            "Stale cabinet command should not mutate wallet, session credits, or ledger rows before reconciliation.",
            store.MemberProfiles[userId].WalletBalance == walletAfterFirst
            && GetSession(store, userId, machineId).MachineCredits == 200_000m
            && store.Ledger.Count(row => row.UserId == userId) == ledgerCountAfterFirst);

        var duplicateStale = await service.SubmitCabinetCommandAsync(userId, stale, CancellationToken.None);
        Assert(
            failures,
            "Replaying the same stale command should return the cached duplicate rejection without mutation.",
            !duplicateStale.Accepted
            && duplicateStale.Status == "duplicate"
            && store.MemberProfiles[userId].WalletBalance == walletAfterFirst
            && store.Ledger.Count(row => row.UserId == userId) == ledgerCountAfterFirst);
    }

    private static async Task DoubleUpSwitchCommandsConsumeNextDealerAsync(List<string> failures)
    {
        await AssertDoubleUpDealerSwitchCommandAsync(
            failures,
            commandType: "double_up_switch",
            userId: Guid.Parse("22000000-0000-0000-0000-000000000004"),
            roundId: Guid.Parse("33000000-0000-0000-0000-000000000001"),
            commandId: Guid.Parse("22000000-1000-0000-0000-000000000006"),
            secondCommandId: Guid.Parse("22000000-1000-0000-0000-000000000009"),
            username: "cabinet-du-switch",
            idempotencyKey: "du-switch-command-0001",
            secondIdempotencyKey: "du-switch-command-0002",
            acceptedMessage: "double_up_switch cabinet command should be accepted as the BET dealer-switch action.",
            mutationMessage: "double_up_switch cabinet command should consume the next deterministic double-up dealer card.");

        await AssertDoubleUpDealerSwitchCommandAsync(
            failures,
            commandType: "swap_double_up_card",
            userId: Guid.Parse("22000000-0000-0000-0000-000000000006"),
            roundId: Guid.Parse("33000000-0000-0000-0000-000000000002"),
            commandId: Guid.Parse("22000000-1000-0000-0000-000000000008"),
            secondCommandId: Guid.Parse("22000000-1000-0000-0000-000000000010"),
            username: "cabinet-du-switch-legacy",
            idempotencyKey: "du-switch-legacy-command-0001",
            secondIdempotencyKey: "du-switch-legacy-command-0002",
            acceptedMessage: "Legacy swap_double_up_card cabinet command should be accepted as the dealer-switch action used by older clients.",
            mutationMessage: "Legacy swap_double_up_card command should consume the next deterministic double-up dealer card.");
    }

    private static async Task AssertDoubleUpDealerSwitchCommandAsync(
        List<string> failures,
        string commandType,
        Guid userId,
        Guid roundId,
        Guid commandId,
        Guid secondCommandId,
        string username,
        string idempotencyKey,
        string secondIdempotencyKey,
        string acceptedMessage,
        string mutationMessage)
    {
        var store = new InMemoryDataStore();
        var service = CreateService(store);
        SeedPlayer(store, userId, username, walletBalance: 1_000_000m, credit: 0m);
        var machineId = store.Machines.Values.First(machine => machine.IsOpen).Id;
        var session = await service.GetMachineSessionAsync(userId, machineId, CancellationToken.None);

        var hand = FiveCardDrawEngine.ParseCards(["AH", "AD", "8C", "8H", "2S"]).ToArray();
        var drawn = new FiveCardDrawState(
            SeedToken: 0xD055UL,
            Deck: hand,
            Hand: hand,
            DrawIndex: 5,
            Held: [false, false, false, false, false],
            Phase: RoundPhase.Drawn,
            State: RoundState.Evaluate);
        var duSession = Lucky5DoubleUpEngine.CreateSessionFromDeck(
            seedRoot: 0xD0B1EUL,
            deck: FiveCardDrawEngine.ParseCards(["9H", "AS", "4C", "2D"]),
            openingAmount: 10_000,
            machineCreditBaseline: 500_000);
        var round = new GameRound
        {
            RoundId = roundId,
            UserId = userId,
            MachineId = machineId,
            BetAmount = 5_000m,
            InitialCards = hand.Select(card => card.ToLegacyPokerCard()).ToList(),
            FinalCards = hand.Select(card => card.ToLegacyPokerCard()).ToList(),
            HandRank = "TwoPair",
            WinAmount = 10_000m,
            OriginalWinAmount = 10_000m,
            IsCompleted = true,
            IsPayoutSettled = false,
            DoubleUpOffered = true,
            DoubleUpSession = duSession,
            CleanRoomState = drawn
        };

        store.ActiveRounds[round.RoundId] = round;
        var snapshot = await service.GetCabinetSnapshotAsync(userId, machineId, CancellationToken.None);
        var payload = new Dictionary<string, object?> { ["round_id"] = round.RoundId };
        if (commandType == "swap_double_up_card")
        {
            payload["swap_position"] = 0;
        }

        var command = BuildCommand(
            commandId,
            type: commandType,
            machineId,
            session.SessionId,
            expectedStateVersion: snapshot.StateVersion,
            idempotencyKey,
            payload);

        var accepted = await service.SubmitCabinetCommandAsync(userId, command, CancellationToken.None);
        var saved = store.ActiveRounds[round.RoundId];

        Assert(
            failures,
            acceptedMessage,
            accepted.Accepted
            && accepted.Status == "accepted"
            && accepted.Error is null
            && accepted.StateVersion == snapshot.StateVersion + 1);
        Assert(
            failures,
            mutationMessage,
            saved.DoubleUpSession is not null
            && saved.DoubleUpSession.DealerIndex == 1
            && saved.DoubleUpSession.SwitchCountInRound == 1
            && saved.DoubleUpSession.DealerCard.Code == "AS");
        Assert(
            failures,
            "Cabinet snapshot should keep BET/dealer-switch enabled while switches remain.",
            accepted.Snapshot?.DoubleUp.SwitchesRemaining == 1
            && accepted.Snapshot.Buttons.Any(button => button.Id == "double_up_switch" && button.Enabled && button.Visible));

        var secondPayload = new Dictionary<string, object?> { ["round_id"] = round.RoundId };
        if (commandType == "swap_double_up_card")
        {
            secondPayload["swap_position"] = 0;
        }

        var secondCommand = BuildCommand(
            secondCommandId,
            type: commandType,
            machineId,
            session.SessionId,
            expectedStateVersion: accepted.StateVersion,
            secondIdempotencyKey,
            secondPayload);

        var secondAccepted = await service.SubmitCabinetCommandAsync(userId, secondCommand, CancellationToken.None);
        var secondSaved = store.ActiveRounds[round.RoundId];

        Assert(
            failures,
            "Cabinet dealer switch should accept the final remaining switch in the current double-up round.",
            secondAccepted.Accepted
            && secondAccepted.Status == "accepted"
            && secondAccepted.Error is null
            && secondSaved.DoubleUpSession is not null
            && secondSaved.DoubleUpSession.DealerIndex == 2
            && secondSaved.DoubleUpSession.SwitchCountInRound == 2
            && secondSaved.DoubleUpSession.DealerCard.Code == "4C");
        Assert(
            failures,
            "Cabinet snapshot should disable BET/dealer-switch after switches_remaining reaches zero.",
            secondAccepted.Snapshot?.DoubleUp.SwitchesRemaining == 0
            && secondAccepted.Snapshot.Buttons.Any(button => button.Id == "double_up_switch" && !button.Enabled && button.Visible));
    }

    private static async Task JackpotRankChangeCommandUpdatesIdleFundedSessionAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var service = CreateService(store);
        var userId = Guid.Parse("22000000-0000-0000-0000-000000000005");
        SeedPlayer(store, userId, "cabinet-fh-switch", walletBalance: 1_000_000m, credit: 0m);
        var machineId = store.Machines.Values.First(machine => machine.IsOpen).Id;

        await service.CashInAsync(userId, machineId, 200_000m, CancellationToken.None);
        var session = GetSession(store, userId, machineId);
        
        // Take a bet to 'arm' the machine for FH rank adjustment
        await service.SubmitCabinetCommandAsync(userId, BuildCommand(
            Guid.NewGuid(), "bet", machineId, session.SessionId, 0, "arm-bet", 
            new Dictionary<string, object?> { ["amount"] = 5000 }), CancellationToken.None);

        var snapshot = await service.GetCabinetSnapshotAsync(userId, machineId, CancellationToken.None);
        var command = BuildCommand(
            commandId: Guid.Parse("22000000-1000-0000-0000-000000000007"),
            type: "jackpot_rank_change",
            machineId,
            session.SessionId,
            expectedStateVersion: snapshot.StateVersion,
            idempotencyKey: "fh-rank-command-0001",
            payload: new Dictionary<string, object?> { ["rank"] = 13 });

        var accepted = await service.SubmitCabinetCommandAsync(userId, command, CancellationToken.None);
        var ledger = store.MachineLedgers[machineId];

        Assert(
            failures,
            "Idle armed cabinet snapshots should enable HOLD[0] as the player-facing Full House rank switch.",
            snapshot.GameState == "idle"
            && snapshot.Buttons.Any(button => button.Id == "hold_0" && button.Enabled && button.Visible));
        Assert(
            failures,
            "Cabinet jackpot_rank_change should update the authoritative Full House rank through the command envelope.",
            accepted.Accepted
            && accepted.Status == "accepted"
            && accepted.Event?.EventType == "jackpot_updated"
            && ledger.JackpotFullHouseRank == 13
            && accepted.Snapshot?.Jackpot.FullHouseRank == 13);
    }

    private static CabinetCommandDto BuildCommand(
        Guid commandId,
        string type,
        int machineId,
        Guid sessionId,
        long expectedStateVersion,
        string idempotencyKey,
        IReadOnlyDictionary<string, object?> payload)
        => new(
            MessageType: "cabinet_command",
            SchemaVersion: "cabinet.v1",
            CommandId: commandId,
            CommandType: type,
            SessionId: sessionId,
            MachineId: machineId,
            ExpectedStateVersion: expectedStateVersion,
            IdempotencyKey: idempotencyKey,
            ClientSequenceNumber: 1,
            SentAtUtc: DateTime.UtcNow,
            Payload: payload,
            Timestamp: DateTime.UtcNow);

    private static MachineSessionState GetSession(InMemoryDataStore store, Guid userId, int machineId)
        => store.MachineSessions.Values.First(session => session.UserId == userId && session.MachineId == machineId);

    private static GameService CreateService(InMemoryDataStore store, IEntropyGenerator? entropy = null, IMachineStateCache? cache = null)
        => new(new InMemoryDataStoreAdapter(store), entropy ?? new DefaultEntropyGenerator(), cache ?? new NullMachineStateCache());

    private static void SeedPlayer(InMemoryDataStore store, Guid userId, string username, decimal walletBalance, decimal credit)
    {
        var user = new User
        {
            Id = userId,
            Username = username,
            PhoneNumber = $"+961{Math.Abs(username.GetHashCode()):0000000}",
            PasswordHash = "test-hash",
            IsOtpVerified = true,
            Role = "Player"
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
            WalletBalance = walletBalance,
            Credit = credit,
            LastSeenUtc = DateTime.UtcNow
        };
    }

    private sealed class NullMachineStateCache : IMachineStateCache
    {
        public Task<ActiveRoundStateDto?> GetActiveRoundAsync(Guid userId, int machineId) => Task.FromResult<ActiveRoundStateDto?>(null);
        public void SetActiveRound(Guid userId, int machineId, ActiveRoundStateDto? dto) { }
        public void InvalidateActiveRound(Guid userId, int machineId) { }
        public Task<MachineSessionDto?> GetMachineSessionAsync(Guid userId, int machineId) => Task.FromResult<MachineSessionDto?>(null);
        public void SetMachineSession(Guid userId, int machineId, MachineSessionDto dto) { }
        public void InvalidateMachineSession(Guid userId, int machineId) { }
    }

    private static void Assert(List<string> failures, string message, bool condition)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }
}
