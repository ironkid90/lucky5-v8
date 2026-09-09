namespace Lucky5.Tests;

using System.Globalization;
using System.Text.Json;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Lucky5.Domain.Entities;
using Lucky5.Infrastructure.Data.Repositories;
using Lucky5.Infrastructure.Services;

/// <summary>Exercises the public command envelope against the real service and in-memory repository.</summary>
public static class StakeReservationTests
{
    public static async Task RunAsync(List<string> failures)
    {
        (string Name, Func<Task> Run)[] tests =
        [
            (nameof(ReserveReplaceCancelAsync), ReserveReplaceCancelAsync),
            (nameof(DuplicateConflictAndStaleCommandsAsync), DuplicateConflictAndStaleCommandsAsync),
            (nameof(DealConsumesExactlyOnceAsync), DealConsumesExactlyOnceAsync),
            (nameof(ConcurrentDealCommandsAsync), ConcurrentDealCommandsAsync),
            (nameof(LastHandAsync), LastHandAsync),
            (nameof(WrongStaleAndMismatchedReservationAsync), WrongStaleAndMismatchedReservationAsync),
            (nameof(LegacyDealGuardAsync), LegacyDealGuardAsync),
            (nameof(ExpiryOnSessionReadAsync), ExpiryOnSessionReadAsync),
            (nameof(ExpiryOnSnapshotReadAsync), ExpiryOnSnapshotReadAsync),
            (nameof(ExpiredDealAsync), ExpiredDealAsync),
            (nameof(InvalidReserveAsync), InvalidReserveAsync),
            (nameof(LeaveReleasesAsync), LeaveReleasesAsync),
            (nameof(CashOutReleasesAsync), CashOutReleasesAsync),
            (nameof(ActiveRoundGuardsAsync), ActiveRoundGuardsAsync),
            (nameof(CancelledCommandAsync), CancelledCommandAsync)
        ];
        foreach (var test in tests)
        {
            try { await test.Run(); }
            catch (Exception ex) { failures.Add($"StakeReservationTests.{test.Name}: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private static async Task ReserveReplaceCancelAsync()
    {
        var f = await Fixture.CreateAsync();
        var wallet = f.Wallet;
        var ledgerCount = f.Store.WalletLedger.Count;
        await f.ReserveAsync(f.MinBet);
        var id = f.Session.ReservationId;
        var expires = f.Session.ReservationExpiresUtc;
        Check(id.HasValue && expires > DateTime.UtcNow, "BET must create a live reservation identity and expiry.");
        await f.CheckBalancesAsync(f.Funding, f.MinBet);
        Check(f.Wallet == wallet && f.Store.WalletLedger.Count == ledgerCount && f.Store.ActiveRounds.IsEmpty,
            "Reserving must not debit the wallet, append a Bet, or create a round.");
        await f.ReserveAsync(f.MinBet);
        Check(f.Session.ReservationId == id, "Same-amount BET must retain the reservation ID.");
        await f.ReserveAsync(f.MaxBet);
        Check(f.Session.ReservationId.HasValue && f.Session.ReservationId != id, "Changing stake must replace the reservation ID.");
        await f.CheckBalancesAsync(f.Funding, f.MaxBet);
        await f.ReserveAsync(0m);
        Check(f.Session.ReservationId is null && f.Session.ReservationExpiresUtc is null, "Zero BET must clear reservation metadata.");
        await f.CheckBalancesAsync(f.Funding, 0m);
        await f.ReserveAsync(0m);
        await f.CheckBalancesAsync(f.Funding, 0m);
        Check(f.Wallet == wallet && f.BetCount == 0, "Cancellation must neither refund unspent credits nor charge a bet.");
    }

    private static async Task DuplicateConflictAndStaleCommandsAsync()
    {
        var f = await Fixture.CreateAsync();
        var command = await f.CommandAsync("bet_change", Bet(f.MinBet));
        var accepted = await f.SubmitAsync(command);
        Accepted(accepted);
        var id = f.Session.ReservationId;
        var duplicate = await f.SubmitAsync(command);
        Check(duplicate.Accepted && duplicate.Status == "duplicate" && duplicate.StateVersion == accepted.StateVersion,
            "Identical reserve command must return cached success without a new state version.");
        var conflict = await f.SubmitAsync(command with { CommandId = Guid.NewGuid(), Payload = Bet(f.MaxBet), ExpectedStateVersion = accepted.StateVersion });
        Check(!conflict.Accepted && conflict.Error?.Code == "IDEMPOTENCY_CONFLICT", "Different content with the same key must conflict.");
        var stale = await f.SubmitAsync(command with { CommandId = Guid.NewGuid(), IdempotencyKey = Guid.NewGuid().ToString(), Payload = Bet(f.MaxBet) });
        Check(!stale.Accepted && stale.Error?.Code == "STALE_STATE" && stale.Snapshot is not null, "Stale BET must reject with recovery snapshot.");
        Check(f.Session.ReservationId == id, "Duplicate/conflicting/stale commands must preserve the existing reservation.");
        await f.CheckBalancesAsync(f.Funding, f.MinBet);
    }

    private static async Task DealConsumesExactlyOnceAsync()
    {
        var f = await Fixture.CreateAsync();
        await f.ReserveAsync(f.MinBet);
        var id = f.Session.ReservationId;
        var command = await f.CommandAsync("deal", Bet(f.MinBet, id));
        var dealt = await f.SubmitAsync(command);
        Accepted(dealt);
        var roundId = dealt.Snapshot?.Hand.RoundId;
        Check(roundId.HasValue && f.Store.ActiveRounds.Count == 1 && f.BetCount == 1, "DEAL must create exactly one round and Bet ledger entry.");
        Check(f.Store.WalletLedger.Single(row => row.UserId == f.UserId && row.TransactionType == "Bet").ReferenceId == roundId.Value.ToString("N"),
            "Bet ledger must reference the created round.");
        await f.CheckBalancesAsync(f.Funding - f.MinBet, 0m);
        var replay = await f.SubmitAsync(command);
        Check(replay.Accepted && replay.Status == "duplicate" && replay.Snapshot?.Hand.RoundId == roundId, "Identical DEAL retry must return the original round.");
        Check(f.BetCount == 1 && f.Store.ActiveRounds.Count == 1, "DEAL replay must not consume another bet.");
        await f.CheckBalancesAsync(f.Funding - f.MinBet, 0m);
    }

    private static async Task ConcurrentDealCommandsAsync()
    {
        var f = await Fixture.CreateAsync();
        await f.ReserveAsync(f.MinBet);
        var first = await f.CommandAsync("deal", Bet(f.MinBet, f.Session.ReservationId));
        var second = first with { CommandId = Guid.NewGuid(), IdempotencyKey = Guid.NewGuid().ToString() };
        // Separate scoped services share the repository, as concurrent HTTP requests do.
        var other = f.NewService();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = Task.Run(async () => { await start.Task; return await f.SubmitAsync(first); });
        var b = Task.Run(async () => { await start.Task; return await other.SubmitCabinetCommandAsync(f.UserId, second, CancellationToken.None); });
        start.SetResult();
        var results = await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(15));
        Check(results.Count(result => result.Accepted) == 1, "Distinct concurrent DEAL commands at the same version must have exactly one winner.");
        Check(f.Store.ActiveRounds.Count == 1 && f.BetCount == 1, "Concurrent DEAL must persist exactly one round and one debit.");
        await f.CheckBalancesAsync(f.Funding - f.MinBet, 0m);
    }

    private static async Task LastHandAsync()
    {
        var f = await Fixture.CreateAsync(lastHand: true);
        await f.ReserveAsync(f.MinBet);
        await f.CheckBalancesAsync(f.MinBet, f.MinBet);
        var recovered = await f.NewService().GetMachineSessionAsync(f.UserId, f.MachineId, CancellationToken.None);
        Check(recovered.TotalCashIn == f.MinBet, "A fully reserved last hand must not normalize away cash-in history.");
        Accepted(await f.SendAsync("deal", Bet(f.MinBet, f.Session.ReservationId)));
        Check(f.Session.MachineCredits == 0m && f.Session.ReservedStake == 0m && f.BetCount == 1, "Last available hand must deal successfully with exactly one debit.");
    }

    private static async Task WrongStaleAndMismatchedReservationAsync()
    {
        var f = await Fixture.CreateAsync();
        await f.ReserveAsync(f.MinBet);
        var staleId = f.Session.ReservationId;
        await f.ReserveAsync(f.MaxBet);
        var currentId = f.Session.ReservationId;
        foreach (var payload in new[] { Bet(f.MaxBet, Guid.NewGuid()), Bet(f.MaxBet, staleId), Bet(f.MinBet, currentId) })
        {
            Check(!(await f.SendAsync("deal", payload)).Accepted, "Wrong, replaced, or amount-mismatched reservation must reject.");
            Check(f.Session.MachineCredits == f.Funding && f.BetCount == 0 && f.Store.ActiveRounds.IsEmpty, "Rejected DEAL must not debit or create a round.");
        }
        await f.ReserveAsync(0m);
        Check(!(await f.SendAsync("deal", Bet(f.MaxBet, currentId))).Accepted, "Cancelled reservation ID must not fall back to a legacy deal.");
    }

    private static async Task LegacyDealGuardAsync()
    {
        var f = await Fixture.CreateAsync();
        await f.ReserveAsync(f.MinBet);
        var rejected = false;
        try { await f.Service.DealAsync(f.UserId, new DealRequest(f.MachineId, f.MinBet), CancellationToken.None); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && f.BetCount == 0, "Legacy ID-less DEAL must reject while a reservation is active.");
        await f.ReserveAsync(0m);
        var deal = await f.Service.DealAsync(f.UserId, new DealRequest(f.MachineId, f.MinBet), CancellationToken.None);
        Check(deal.MachineCreditsAfterBet == f.Funding - f.MinBet && f.BetCount == 1, "Legacy ID-less DEAL remains supported when no reservation exists.");
    }

    private static Task ExpiryOnSessionReadAsync() => ExpiryOnReadAsync(snapshot: false);
    private static Task ExpiryOnSnapshotReadAsync() => ExpiryOnReadAsync(snapshot: true);
    private static async Task ExpiryOnReadAsync(bool snapshot)
    {
        var f = await Fixture.CreateAsync();
        await f.ReserveAsync(f.MinBet);
        f.Session.ReservationExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
        if (snapshot) await f.Service.GetCabinetSnapshotAsync(f.UserId, f.MachineId, CancellationToken.None);
        else await f.Service.GetMachineSessionAsync(f.UserId, f.MachineId, CancellationToken.None);
        Check(f.Session.ReservedStake == 0m && f.Session.ReservationId is null && f.Session.ReservationExpiresUtc is null && f.Session.ReservationStatus == "expired",
            "Read must persist expiry, not just hide it in a DTO.");
        var recovered = await f.NewService().GetMachineSessionAsync(f.UserId, f.MachineId, CancellationToken.None);
        Check(recovered.ReservedStake == 0m && recovered.ReservationId is null, "New service must recover the persisted expired state.");
        await f.CheckBalancesAsync(f.Funding, 0m);
        Check(f.BetCount == 0, "Expiry must not charge a bet.");
    }

    private static async Task ExpiredDealAsync()
    {
        var f = await Fixture.CreateAsync();
        await f.ReserveAsync(f.MinBet);
        var command = await f.CommandAsync("deal", Bet(f.MinBet, f.Session.ReservationId));
        f.Session.ReservationExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
        Check(!(await f.SubmitAsync(command)).Accepted, "Expired explicit reservation must not be consumed or fall back to legacy DEAL.");
        Check(f.BetCount == 0 && f.Store.ActiveRounds.IsEmpty && f.Session.MachineCredits == f.Funding, "Expired DEAL must leave gross credits untouched.");
        await f.CheckBalancesAsync(f.Funding, 0m);
    }

    private static async Task InvalidReserveAsync()
    {
        var f = await Fixture.CreateAsync();
        await f.ReserveAsync(f.MinBet);
        var id = f.Session.ReservationId;
        foreach (var amount in new object?[] { -1m, f.MinBet - 1m, f.MaxBet + 1m, "not-money", null })
        {
            Check(!(await f.SendAsync("bet_change", new Dictionary<string, object?> { ["bet_amount"] = amount })).Accepted, $"Invalid stake {amount ?? "null"} must reject.");
            Check(f.Session.ReservationId == id && f.Session.ReservedStake == f.MinBet && f.Session.MachineCredits == f.Funding, "Invalid replacement must preserve the prior hold and credits.");
        }
        Check(!(await f.SendAsync("bet_change", new Dictionary<string, object?>())).Accepted, "Missing bet_amount must reject.");
        var poor = await Fixture.CreateAsync(lastHand: true);
        Check(!(await poor.SendAsync("bet_change", Bet(poor.MaxBet))).Accepted, "In-range but unaffordable stake must reject.");
        Check(poor.Session.ReservedStake == 0m && poor.Session.MachineCredits == poor.MinBet, "Insufficient-credit reserve must not mutate funds.");
    }

    private static async Task LeaveReleasesAsync()
    {
        var f = await Fixture.CreateAsync();
        await f.ReserveAsync(f.MinBet);
        var id = f.Session.ReservationId;
        Accepted(await f.SendAsync("leave_machine", new Dictionary<string, object?>()));
        await f.CheckBalancesAsync(f.Funding, 0m);
        Check(f.Session.ReservationId is null && f.BetCount == 0, "Leave must release without charging or refunding the hold.");
        Check(!(await f.SendAsync("deal", Bet(f.MinBet, id))).Accepted, "ID released by leave must be unusable.");
    }

    private static async Task CashOutReleasesAsync()
    {
        var f = await Fixture.CreateAsync();
        // Model already-earned winnings at the existing authoritative 2x cash-out threshold.
        f.Session.MachineCredits = f.Funding * 2m;
        await f.ReserveAsync(f.MinBet);
        var wallet = f.Wallet;
        var command = await f.CommandAsync("cash_out", new Dictionary<string, object?>());
        Accepted(await f.SubmitAsync(command));
        Check(f.Wallet == wallet + f.Funding * 2m, "Cash-out must return gross credits, including the undebited hold, exactly once.");
        Check(f.Session.ReservedStake == 0m && f.Session.ReservationId is null && f.Session.MachineCredits == 0m, "Cash-out must drain credits and reservation metadata.");
        var replay = await f.SubmitAsync(command);
        Check(replay.Accepted && replay.Status == "duplicate" && f.Wallet == wallet + f.Funding * 2m && f.BetCount == 0, "Cash-out replay must not refund the stake again.");
    }

    private static async Task ActiveRoundGuardsAsync()
    {
        foreach (var completed in new[] { false, true })
        {
            var f = await Fixture.CreateAsync();
            var dealt = await f.Service.DealAsync(f.UserId, new DealRequest(f.MachineId, f.MinBet), CancellationToken.None);
            var round = f.Store.ActiveRounds[dealt.RoundId];
            // Also exercise completed-but-unsettled winnings, which are still recoverable work.
            round.IsCompleted = completed;
            round.IsPayoutSettled = false;
            if (completed) { round.WinAmount = f.MinBet * 2m; round.OriginalWinAmount = round.WinAmount; }
            var credits = f.Session.MachineCredits;
            Check(!(await f.SendAsync("bet_change", Bet(f.MinBet))).Accepted, "Active/unsettled round must block a new reservation.");
            Check(!(await f.SendAsync("deal", Bet(f.MinBet))).Accepted, "Active/unsettled round must block a second DEAL.");
            Check(f.Session.ReservedStake == 0m && f.Session.MachineCredits == credits && f.Store.ActiveRounds.Count == 1 && f.BetCount == 1,
                "Active-round guards must leave existing round and balances intact.");
        }
    }

    private static async Task CancelledCommandAsync()
    {
        var f = await Fixture.CreateAsync();
        await f.ReserveAsync(f.MinBet);
        var id = f.Session.ReservationId;
        var command = await f.CommandAsync("deal", Bet(f.MinBet, id));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = false;
        try { await f.Service.SubmitCabinetCommandAsync(f.UserId, command, cts.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && f.BetCount == 0 && f.Store.ActiveRounds.IsEmpty && f.Session.ReservationId == id,
            "Pre-cancelled DEAL must not consume its reservation.");
        await f.CheckBalancesAsync(f.Funding, f.MinBet);
    }

    private static Dictionary<string, object?> Bet(decimal amount, Guid? reservationId = null)
    {
        var payload = new Dictionary<string, object?> { ["bet_amount"] = amount.ToString(CultureInfo.InvariantCulture) };
        if (reservationId.HasValue) payload["reservation_id"] = reservationId.Value.ToString();
        return payload;
    }

    private static void Accepted(CabinetCommandResultDto result)
        => Check(result.Accepted, $"Command rejected: {result.Status} / {result.Error?.Code} / {result.Error?.Message}");
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture
    {
        public InMemoryDataStore Store { get; } = new();
        public Guid UserId { get; } = Guid.NewGuid();
        public int MachineId { get; private set; }
        public decimal Funding { get; private set; }
        public GameService Service { get; private set; } = null!;
        public MachineSessionState Session => Store.MachineSessions.Values.Single(s => s.UserId == UserId && s.MachineId == MachineId);
        public decimal MinBet => Store.Machines[MachineId].MinBet;
        public decimal MaxBet => Store.Machines[MachineId].MaxBet;
        public decimal Wallet => Store.MemberProfiles[UserId].WalletBalance;
        public int BetCount => Store.WalletLedger.Count(row => row.UserId == UserId && row.TransactionType == "Bet");
        public GameService NewService() => new(new InMemoryDataStoreAdapter(Store), new FixedEntropy(), new NullCache(), new SpectatorTracker());

        public static async Task<Fixture> CreateAsync(bool lastHand = false)
        {
            var f = new Fixture();
            f.MachineId = f.Store.Machines.Values.OrderBy(m => m.Id).First(m => m.IsOpen).Id;
            var user = new User { Id = f.UserId, Username = $"reservation-{f.UserId:N}", PhoneNumber = "+96170000000", PasswordHash = "test-hash", IsOtpVerified = true, Role = "Player" };
            f.Store.Users[f.UserId] = user;
            f.Store.Profiles[f.UserId] = user;
            f.Store.MemberProfiles[f.UserId] = new MemberProfile { UserId = f.UserId, Username = user.Username, DisplayName = user.Username, Email = "reservation@lucky5.local", PhoneNumber = user.PhoneNumber, WalletBalance = 1_000_000m, LastSeenUtc = DateTime.UtcNow };
            f.Service = f.NewService();
            f.Funding = lastHand ? f.MinBet : 200_000m;
            await f.Service.CashInAsync(f.UserId, f.MachineId, f.Funding, CancellationToken.None);
            return f;
        }

        public async Task<CabinetCommandDto> CommandAsync(string type, IReadOnlyDictionary<string, object?> payload)
        {
            var cursor = await Service.GetCabinetStateCursorAsync(UserId, MachineId, CancellationToken.None);
            return new CabinetCommandDto("cabinet_command", "cabinet.v1", Guid.NewGuid(), type, Session.SessionId, MachineId,
                cursor.StateVersion, Guid.NewGuid().ToString(), 1, DateTime.UtcNow, payload, DateTime.UtcNow);
        }
        public Task<CabinetCommandResultDto> SubmitAsync(CabinetCommandDto command)
            => Service.SubmitCabinetCommandAsync(UserId, command, CancellationToken.None);
        public async Task<CabinetCommandResultDto> SendAsync(string type, IReadOnlyDictionary<string, object?> payload)
            => await SubmitAsync(await CommandAsync(type, payload));
        public async Task ReserveAsync(decimal amount) => Accepted(await SendAsync("bet_change", Bet(amount)));

        public async Task CheckBalancesAsync(decimal gross, decimal reserved)
        {
            var dto = await Service.GetMachineSessionAsync(UserId, MachineId, CancellationToken.None);
            var snapshot = await Service.GetCabinetSnapshotAsync(UserId, MachineId, CancellationToken.None);
            Check(Session.MachineCredits == gross && Session.ReservedStake == reserved && dto.MachineCredits == gross && dto.ReservedStake == reserved,
                $"Expected gross={gross}, reserved={reserved}; stored gross={Session.MachineCredits}, reserved={Session.ReservedStake}.");
            // Serialize real DTOs to verify the wire contract as well as service behavior.
            var sessionJson = JsonSerializer.SerializeToElement(dto);
            Check(sessionJson.GetProperty("AvailableCredits").GetDecimal() == gross - reserved, "Session DTO available credits must subtract the hold.");
            Check(sessionJson.GetProperty("ReservationStatus").ValueKind is JsonValueKind.String or JsonValueKind.Null, "Session DTO must expose reservation status.");
            var credits = JsonSerializer.SerializeToElement(snapshot.Credits);
            Check(credits.GetProperty("machine_credits").GetString() == gross.ToString("0.##", CultureInfo.InvariantCulture)
                && decimal.Parse(credits.GetProperty("reserved_stake").GetString()!, CultureInfo.InvariantCulture) == reserved
                && decimal.Parse(credits.GetProperty("available_credits").GetString()!, CultureInfo.InvariantCulture) == gross - reserved,
                "Cabinet credits must expose invariant string-decimal gross/reserved/available values.");
            Check(credits.TryGetProperty("reservation_id", out var id) && (reserved == 0m ? id.ValueKind == JsonValueKind.Null : id.GetGuid() == Session.ReservationId), "Cabinet reservation ID must match persisted state.");
            Check(credits.TryGetProperty("reservation_expires_utc", out var expiry) && (reserved == 0m ? expiry.ValueKind == JsonValueKind.Null : expiry.GetDateTime() == Session.ReservationExpiresUtc), "Cabinet expiry must match persisted state.");
            Check(credits.TryGetProperty("reservation_status", out _), "Cabinet credits must expose reservation_status.");
            if (reserved > 0m) Check(snapshot.Session.IsArmed, "A reserved session must be armed, including the last available hand.");
        }
    }

    private sealed class FixedEntropy : IEntropyGenerator
    {
        public ulong CreateSeed(Guid userId, int machineId, decimal betAmount, MachineLedgerState ledger) => 0xBEEFUL;
    }
    private sealed class NullCache : IMachineStateCache
    {
        public Task<ActiveRoundStateDto?> GetActiveRoundAsync(Guid userId, int machineId) => Task.FromResult<ActiveRoundStateDto?>(null);
        public void SetActiveRound(Guid userId, int machineId, ActiveRoundStateDto? dto) { }
        public void InvalidateActiveRound(Guid userId, int machineId) { }
        public Task<MachineSessionDto?> GetMachineSessionAsync(Guid userId, int machineId) => Task.FromResult<MachineSessionDto?>(null);
        public void SetMachineSession(Guid userId, int machineId, MachineSessionDto dto) { }
        public void InvalidateMachineSession(Guid userId, int machineId) { }
    }
}
