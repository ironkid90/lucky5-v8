namespace Lucky5.Infrastructure.Services;

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Lucky5.Domain.Entities;
using Lucky5.Domain.Game.CleanRoom;
using Lucky5.Application.Interfaces;

public sealed class GameService(IDataStore store, IEntropyGenerator entropyGenerator, IMachineStateCache stateCache, ISpectatorTracker spectatorTracker) : IGameService
{
	private const decimal CashInUnit = 200_000m;
	private const decimal MaxSessionCashIn = 1_000_000m;
	private const string CabinetSchemaVersion = "cabinet.v1";
	private const string CabinetVariantId = "lucky5.classic";
	private const string CabinetVariantSchemaVersion = "variant.v1";
	private const string CabinetPaytableHash = "sha256:cbe816c3eaa3d13cf0a55850ffb27140b856a35015a8fd44d41bd507babdb196";
	private const int CabinetReplayMaxEvents = 128;
	private static readonly EngineConfig EngineCfg = EngineConfig.Default;
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> CabinetCommandLocks = new(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentDictionary<Guid, DoubleUpStartLock> DoubleUpStartLocks = new();
	// Per-(user, machine) mutation gate. All credit/round-mutating entry points
	// (cash-in/out, deal, draw, double-up lifecycle, reset) serialize on this gate
	// so concurrent requests — double-clicks, network retries, multi-tab play, hub
	// and REST paths racing — can never interleave read-modify-write sequences on
	// the shared session/round/ledger entities. Without this, two concurrent
	// TakeHalf or cashout calls both pass their guard checks and double-pay.
	// Gates are reference-counted and evicted at zero so a long-running process
	// does not accumulate one semaphore per historical user-machine pair.
	private static readonly ConcurrentDictionary<string, SessionMutationGate> SessionMutationGates = new(StringComparer.Ordinal);

	private sealed class SessionMutationGate
	{
		public SemaphoreSlim Gate { get; } = new(1, 1);
		public object SyncRoot { get; } = new();
		public int ReferenceCount { get; set; }
	}

	private static string SessionGateKey(Guid userId, int machineId) => $"{userId:N}:{machineId}";

	private static async Task<T> WithSessionGateAsync<T>(Guid userId, int machineId, CancellationToken cancellationToken, Func<Task<T>> action)
	{
		var key = SessionGateKey(userId, machineId);
		SessionMutationGate gate;
		while (true)
		{
			gate = SessionMutationGates.GetOrAdd(key, static _ => new SessionMutationGate());
			lock (gate.SyncRoot)
			{
				if (SessionMutationGates.TryGetValue(key, out var current) && ReferenceEquals(current, gate))
				{
					gate.ReferenceCount++;
					break;
				}
				// Lost a removal race — the fetched gate is being disposed; retry
				// to pick up (or create) the live one.
			}
		}

		try
		{
			await gate.Gate.WaitAsync(cancellationToken);
			try
			{
				return await action();
			}
			finally
			{
				gate.Gate.Release();
			}
		}
		finally
		{
			var dispose = false;
			lock (gate.SyncRoot)
			{
				gate.ReferenceCount--;
				if (gate.ReferenceCount == 0)
				{
					SessionMutationGates.TryRemove(key, out _);
					dispose = true;
				}
			}
			if (dispose)
			{
				gate.Gate.Dispose();
			}
		}
	}
	private static readonly JsonSerializerOptions CabinetJsonOptions = new(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true
	};

	private sealed class DoubleUpStartLock
	{
		public SemaphoreSlim Gate { get; } = new(1, 1);
		public object SyncRoot { get; } = new();
		public int ReferenceCount { get; set; }
	}

	private static readonly IReadOnlyList<OfferDto> DefaultOffers =
	[
		new(1, "Welcome Bonus", "First deposit bonus", 10),
		new(2, "Weekend Cashback", "5% cashback on losses", 5)
	];

	private static readonly Dictionary<string, decimal> Rules = new(StringComparer.OrdinalIgnoreCase)
	{
		["RoyalFlush"] = 1000,
		["StraightFlush"] = 300,
		["FourOfAKind"] = 120,
		["FullHouse"] = 20,
		["Flush"] = 14,
		["Straight"] = 10,
		["ThreeOfAKind"] = 6,
		["TwoPair"] = 4
	};

	public GameService(IDataStore store, IEntropyGenerator entropyGenerator)
		: this(store, entropyGenerator, new InMemoryMachineStateCache(new MachineCacheTtlOptions()), new SpectatorTracker())
	{
	}

	public Task<IReadOnlyList<string>> GetGamesAsync(CancellationToken cancellationToken)
		=> Task.FromResult<IReadOnlyList<string>>(["Lucky5", "VideoPoker"]);

	public async Task<IReadOnlyList<MachineListingDto>> GetMachinesAsync(CancellationToken cancellationToken)
		=> (await store.GetMachinesAsync())
			.Select(x => new MachineListingDto(x.Id, x.Name, x.IsOpen, x.MinBet, x.MaxBet, x.BetIncrement))
			.ToArray();

	public async Task<IReadOnlyList<MachineListingDto>> GetLobbyMachinesAsync(Guid userId, CancellationToken cancellationToken)
	{
		var machines = (await store.GetMachinesAsync())
			.OrderBy(machine => machine.Id)
			.ToArray();
		var allSessions = await store.GetAllMachineSessionsAsync();
		var result = new List<MachineListingDto>(machines.Length);

		foreach (var machine in machines)
		{
			var activeOccupantSession = allSessions.FirstOrDefault(s => s.MachineId == machine.Id && !s.IsMachineClosed && s.MachineCredits > 0m);
			bool isOccupied = activeOccupantSession != null;
			string? occupiedByUsername = isOccupied ? (await store.GetUserByIdAsync(activeOccupantSession!.UserId))?.Username : null;
			DateTime? reservedUntil = isOccupied ? activeOccupantSession!.LastUpdatedUtc.AddMinutes(5) : null;
			int idleSec = 0;
			if (reservedUntil.HasValue)
			{
				var remaining = (reservedUntil.Value - DateTime.UtcNow).TotalSeconds;
				idleSec = remaining > 0 ? (int)remaining : 0;
			}
			int spectatorCount = spectatorTracker.GetSpectatorCount(machine.Id);

			result.Add(new MachineListingDto(
				machine.Id,
				machine.Name,
				machine.IsOpen,
				machine.MinBet,
				machine.MaxBet,
				machine.BetIncrement,
				isOccupied,
				occupiedByUsername,
				reservedUntil,
				idleSec,
				spectatorCount));
		}

		return result;
	}

	public async Task<PlayerLobbyDto> GetLobbyAsync(Guid userId, CancellationToken cancellationToken)
	{
		var profile = await RequireProfileAsync(userId);
		var machines = (await store.GetMachinesAsync())
			.OrderBy(machine => machine.Id)
			.ToArray();
		var lobbyMachines = new List<PlayerLobbyMachineDto>(machines.Length);

		foreach (var machine in machines)
		{
			var ledger = await RequireMachineLedgerAsync(machine.Id);
			var session = await store.GetMachineSessionAsync(userId, machine.Id);
			var sessionDto = session is null
				? null
				: await ToMachineSessionDtoAsync(userId, session, profile.WalletBalance);
			var activeRound = await GetActiveRoundAsync(userId, machine.Id, cancellationToken);

			var allSessions = await store.GetAllMachineSessionsAsync();
			var activeOccupantSession = allSessions.FirstOrDefault(s => s.MachineId == machine.Id && !s.IsMachineClosed && s.MachineCredits > 0m);
			bool isOccupied = activeOccupantSession != null;
			string? occupiedByUsername = isOccupied ? (await store.GetUserByIdAsync(activeOccupantSession!.UserId))?.Username : null;
			DateTime? reservedUntil = isOccupied ? activeOccupantSession!.LastUpdatedUtc.AddMinutes(5) : null;
			int idleSec = 0;
			if (reservedUntil.HasValue)
			{
				var remaining = (reservedUntil.Value - DateTime.UtcNow).TotalSeconds;
				idleSec = remaining > 0 ? (int)remaining : 0;
			}
			int spectatorCount = spectatorTracker.GetSpectatorCount(machine.Id);

			lobbyMachines.Add(new PlayerLobbyMachineDto(
				machine.Id,
				machine.Name,
				machine.IsOpen,
				machine.MinBet,
				machine.MaxBet,
				SnapshotJackpots(ledger),
				ledger.ObservedRtp,
				ledger.LastDistributionMode.ToString(),
				ledger.RoundCount,
				sessionDto,
				activeRound,
				isOccupied,
				occupiedByUsername,
				reservedUntil,
				idleSec,
				spectatorCount));
		}

		return new PlayerLobbyDto(profile.UserId, profile.Username, profile.WalletBalance, profile.Credit, lobbyMachines);
	}

	public Task<DefaultRulesDto> GetDefaultRulesAsync(CancellationToken cancellationToken)
		=> Task.FromResult(new DefaultRulesDto(new Dictionary<string, decimal>(Rules)));

	public async Task<IReadOnlyList<OfferDto>> GetOffersAsync(CancellationToken cancellationToken)
	{
		var offers = await store.GetOffersAsync();
		if (offers.Count == 0)
		{
			return DefaultOffers;
		}

		return offers
			.Select(x => new OfferDto(x.Id, x.Title, x.Description, x.BonusAmount))
			.ToArray();
	}

	public async Task<MachineSessionDto> GetMachineSessionAsync(Guid userId, int machineId, CancellationToken cancellationToken)
	{
		var cachedSession = await stateCache.GetMachineSessionAsync(userId, machineId);
		if (cachedSession is not null && !IsStaleZeroCreditSession(cachedSession))
			return cachedSession;

		if (cachedSession is not null)
		{
			InvalidateCaches(userId, machineId);
		}

		var profile = await RequireProfileAsync(userId);
		await RequireMachineAsync(machineId);
		var session = await RequireMachineSessionAsync(userId, machineId, createIfMissing: true);

		var dto = await ToMachineSessionDtoAsync(userId, session, profile.WalletBalance);
		stateCache.SetMachineSession(userId, machineId, dto);
		return dto;
	}

	public Task<MachineSessionDto> CashInAsync(Guid userId, int machineId, decimal amount, CancellationToken cancellationToken)
		=> WithSessionGateAsync(userId, machineId, cancellationToken,
			() => CashInCoreAsync(userId, machineId, amount, cancellationToken));

	private async Task<MachineSessionDto> CashInCoreAsync(Guid userId, int machineId, decimal amount, CancellationToken cancellationToken)
	{
		var profile = await RequireProfileAsync(userId);
		var machine = await RequireMachineAsync(machineId);
		var session = await RequireMachineSessionAsync(userId, machineId, createIfMissing: true);

		if (amount <= 0)
			throw new InvalidOperationException("Cash in amount must be positive");
		if (session.IsMachineClosed)
			throw new InvalidOperationException("Machine is closed - cash out to wallet before continuing");
		var totalAvailable = profile.WalletBalance + profile.Credit;
		if (totalAvailable <= 0)
			throw new InvalidOperationException("Insufficient wallet balance");
		// Allow any positive amount up to the player's total available balance.
		// If the player has less than the standard increment, deposit whatever they have.
		if (amount > totalAvailable)
			throw new InvalidOperationException("Insufficient wallet balance");
		// Only enforce session cap if it would not prevent the player from depositing
		// their entire remaining balance (last-deposit allowance).
		if (session.TotalCashIn + amount > MaxSessionCashIn && amount < totalAvailable)
			throw new InvalidOperationException("Maximum session cash-in is 1,000,000");

		var fromCredit = Math.Min(profile.Credit, amount);
		var fromBalance = amount - fromCredit;
		profile.Credit -= fromCredit;
		profile.WalletBalance -= fromBalance;
		session.MachineCredits += amount;
		session.TotalCashIn += amount;
		session.LastUpdatedUtc = DateTime.UtcNow;
		session.IsMachineClosed = session.MachineCredits >= machine.CloseThreshold;

		await store.UpdateMachineSessionAsync(session);

		await store.AddWalletLedgerEntryAsync(new WalletLedgerEntry
		{
			UserId = userId,
			Amount = -amount,
			TransactionType = "MachineCashIn",
			ReferenceId = $"machine:{machineId}:cashin",
			BalanceAfter = profile.WalletBalance,
			CreatedUtc = DateTime.UtcNow
		});

		await store.UpdateProfileAsync(profile);

		stateCache.InvalidateActiveRound(userId, machineId);
		stateCache.InvalidateMachineSession(userId, machineId);
		return await ToMachineSessionDtoAsync(userId, session, profile.WalletBalance);
	}

	public Task<MachineSessionDto> CashOutAsync(Guid userId, int machineId, CancellationToken cancellationToken, bool bypassRules = false)
		=> WithSessionGateAsync(userId, machineId, cancellationToken,
			() => CashOutCoreAsync(userId, machineId, cancellationToken, bypassRules));

	private async Task<MachineSessionDto> CashOutCoreAsync(Guid userId, int machineId, CancellationToken cancellationToken, bool bypassRules)
	{
		var profile = await RequireProfileAsync(userId);
		var session = await RequireMachineSessionAsync(userId, machineId, createIfMissing: false);

		if (session.MachineCredits <= 0m)
		{
			return await ToMachineSessionDtoAsync(userId, session, profile.WalletBalance);
		}

		var latestRound = await store.GetLatestRoundAsync(userId, machineId);
		if (latestRound is not null)
		{
			if (!latestRound.IsCompleted)
			{
				try
				{
					// Gate already held by CashOutCoreAsync — call the core draw
					// directly to avoid re-acquiring the per-session gate.
					_ = await DrawCoreAsync(userId, new DrawRequest(latestRound.RoundId, []), CancellationToken.None);
					latestRound = await store.GetRoundAsync(latestRound.RoundId);
				}
				catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
				{
					// Draw cannot proceed (e.g., machine closed, credits exhausted, round not found).
					// Mark round as completed with zero payout so cashout can proceed.
					latestRound.IsCompleted = true;
					latestRound.WinAmount = 0;
					await store.SaveRoundAsync(latestRound);
				}
			}

			if (latestRound is not null && !latestRound.IsPayoutSettled && latestRound.WinAmount > 0m)
			{
				var settleCredits = latestRound.DoubleUpSession?.CurrentAmount ?? (int)latestRound.WinAmount;
				await FinalizeDoubleUpAsync(latestRound, session, settleCredits);
				session = await RequireMachineSessionAsync(userId, machineId, createIfMissing: false);
			}
		}

		if (!bypassRules)
		{
			if (!CanCashOut(session))
				throw new InvalidOperationException("Cash out is only available when the machine is closed or credits reach the 2x session threshold");

			if (profile.MinimumOut > 0m && session.MachineCredits < profile.MinimumOut)
				throw new InvalidOperationException($"Minimum cash-out threshold is {profile.MinimumOut:N0} credits");
		}

		var amount = session.MachineCredits;
		profile.WalletBalance += amount;
		session.MachineCredits = 0m;
		session.TotalCashIn = 0m;
		session.IsMachineClosed = false;
		session.LastUpdatedUtc = DateTime.UtcNow;

		await store.UpdateMachineSessionAsync(session);
		await store.UpdateProfileAsync(profile);

		await store.AddWalletLedgerEntryAsync(new WalletLedgerEntry
		{
			UserId = userId,
			Amount = amount,
			TransactionType = "MachineCashOut",
			ReferenceId = $"machine:{machineId}:cashout",
			BalanceAfter = profile.WalletBalance,
			CreatedUtc = DateTime.UtcNow
		});

		stateCache.InvalidateActiveRound(userId, machineId);
		stateCache.InvalidateMachineSession(userId, machineId);
		return await ToMachineSessionDtoAsync(userId, session, profile.WalletBalance);
	}

	public Task<DealResultDto> DealAsync(Guid userId, DealRequest request, CancellationToken cancellationToken)
		=> WithSessionGateAsync(userId, request.MachineId, cancellationToken,
			() => DealCoreAsync(userId, request, cancellationToken));

	private async Task<DealResultDto> DealCoreAsync(Guid userId, DealRequest request, CancellationToken cancellationToken)
	{
		var machine = await RequireMachineAsync(request.MachineId);
		var session = await RequireMachineSessionAsync(userId, request.MachineId, createIfMissing: true);

		if (session.IsMachineClosed)
			throw new InvalidOperationException("Machine is closed - cash out to wallet before continuing");

		// Last-hand behavior: if credits < min bet but > 0, allow play with remaining credits.
		// Paytable scales to the actual bet amount. This prevents orphaned credits on the machine.
		bool isLastHand = session.MachineCredits > 0 && session.MachineCredits < machine.MinBet;
		var effectiveMinBet = isLastHand ? 1m : machine.MinBet;

		if (request.BetAmount <= 0 || request.BetAmount < effectiveMinBet || request.BetAmount > machine.MaxBet)
			throw new InvalidOperationException("Bet amount is outside machine limits");
		if (session.MachineCredits < request.BetAmount && !isLastHand)
			throw new InvalidOperationException("Insufficient machine credits for deal - cash in from wallet first");

		// For last-hand: clamp bet to available credits
		if (isLastHand)
		{
			request = request with { BetAmount = Math.Min(request.BetAmount, session.MachineCredits) };
		}

		ulong seed;
		int active4kSlot;
		PolicyDistributionMode policyMode;
		MachinePolicyState policyState;

		var ledger = await RequireMachineLedgerAsync(machine.Id);

		seed = entropyGenerator.CreateSeed(userId, machine.Id, request.BetAmount, ledger);
		policyState = BuildMachinePolicyState(ledger);

		var policyResolution = MachinePolicy.ResolvePolicy(policyState, seed);
		policyMode = policyResolution.DistributionMode;
		if (session.CounterplayScore >= 3 && policyMode == PolicyDistributionMode.Cold)
		{
			policyMode = PolicyDistributionMode.Neutral;
		}

		ledger.CapitalIn += request.BetAmount;
		ledger.RoundCount++;
		ledger.RoundsSinceMediumWin++;
		ledger.RoundsSinceLucky5Hit++;
		if (ledger.CooldownRoundsRemaining > 0) ledger.CooldownRoundsRemaining--;
		ledger.LastRoundUtc = DateTime.UtcNow;
		ledger.LastDistributionMode = policyMode switch
		{
			PolicyDistributionMode.Cold => DistributionMode.Cold,
			PolicyDistributionMode.Hot => DistributionMode.Hot,
			_ => DistributionMode.Neutral
		};
		active4kSlot = (ledger.RoundCount % 2 == 0) ? (int)(seed % 2) : 1 - (int)(seed % 2);
		ledger.ActiveFourOfAKindSlot = active4kSlot;
		// Only the currently-starred Four-of-a-Kind jackpot accrues this round.
		// The non-starred side is frozen — matching the UI rule that the red *
		// marks the sole jackpot able to both grow and pay this round.
		ApplyJackpotContributions(ledger, EngineCfg, active4kSlot);
		ledger.NetSinceLastClose = Math.Max(ledger.CapitalIn - ledger.CapitalOut, 0m);

		await store.UpdateMachineLedgerAsync(ledger);

		var engine = CabinetVariantFactory.GetEngine(machine.GameId);
		var standardDeck = engine.BuildDeck().ToArray();
		var alteredDeck = MachinePolicy.AlterDeck(standardDeck, policyMode, seed, policyState.ConsecutiveLosses);
		var shuffledDeck = FiveCardDrawEngine.ShuffleDeck(seed, "hand", alteredDeck);
		var hand = shuffledDeck.Take(5).ToArray();
		var drawState = FiveCardDrawState.Create(seed, shuffledDeck.ToArray(), hand);

		session.MachineCredits -= request.BetAmount;
		session.LastUpdatedUtc = DateTime.UtcNow;
		await store.UpdateMachineSessionAsync(session);

		var cards = hand.Select(c => c.ToLegacyPokerCard()).ToList();

		var round = new GameRound
		{
			UserId = userId,
			MachineId = request.MachineId,
			BetAmount = request.BetAmount,
			InitialCards = cards,
			FinalCards = cards,
			PolicyMode = policyMode,
			RoundEntropySeed = seed,
			CleanRoomState = drawState,
			ActiveFourOfAKindSlotAtDeal = active4kSlot
		};

		await store.SaveRoundAsync(round);

		var cursor = await store.AdvanceCabinetStateCursorAsync(userId, request.MachineId);

		var profile = await RequireProfileAsync(userId);
		await store.AddWalletLedgerEntryAsync(new WalletLedgerEntry
		{
			UserId = userId,
			Amount = -request.BetAmount,
			TransactionType = "Bet",
			ReferenceId = round.RoundId.ToString("N"),
			BalanceAfter = profile.WalletBalance, // Technically wallet doesn't change here, just reference
			CreatedUtc = DateTime.UtcNow
		});

		var jackpots = SnapshotJackpots(ledger);
		var advisedHolds = FiveCardDrawEngine.ComputeAdvisedHolds(hand);

		stateCache.InvalidateActiveRound(userId, request.MachineId);
		stateCache.InvalidateMachineSession(userId, request.MachineId);
		return new DealResultDto(round.RoundId, cards.Select(ToDto).ToArray(), request.BetAmount, session.MachineCredits, jackpots, advisedHolds,
			AceCard: false,
			AceMultiplier: 0,
			StateVersion: cursor.StateVersion,
			SequenceNumber: cursor.SequenceNumber);
	}

	public async Task<DrawResultDto> DrawAsync(Guid userId, DrawRequest request, CancellationToken cancellationToken)
	{
		// Discover the machine for the gate key, then re-load and re-validate the
		// round inside the gate (concurrent state may have changed in between).
		var probe = await store.GetRoundAsync(request.RoundId);
		if (probe == null || probe.UserId != userId)
			throw new KeyNotFoundException("Round not found");
		return await WithSessionGateAsync(userId, probe.MachineId, cancellationToken,
			() => DrawCoreAsync(userId, request, cancellationToken));
	}

	private async Task<DrawResultDto> DrawCoreAsync(Guid userId, DrawRequest request, CancellationToken cancellationToken)
	{
		var round = await store.GetRoundAsync(request.RoundId);
		if (round == null || round.UserId != userId)
			throw new KeyNotFoundException("Round not found");
		if (round.IsCompleted)
			throw new InvalidOperationException("Round already completed");
		if (round.CleanRoomState is null)
			throw new InvalidOperationException("Clean-room state not initialized");
		if (round.CleanRoomState.Phase != RoundPhase.Dealt)
			throw new InvalidOperationException("Draw already completed for this round");

		var session = await RequireMachineSessionAsync(userId, round.MachineId, createIfMissing: false);
		if (session.IsMachineClosed)
			throw new InvalidOperationException("Machine is closed - cash out to wallet before continuing");

		// Last-hand: draw ante is deducted from remaining credits (may be 0 if all consumed by deal)
		var drawAnte = Math.Min(round.BetAmount, session.MachineCredits);
		if (drawAnte > 0)
		{
			session.MachineCredits -= drawAnte;
		}
		session.LastUpdatedUtc = DateTime.UtcNow;

		var ledger = await RequireMachineLedgerAsync(round.MachineId);
		ledger.CapitalIn += drawAnte;
		// Draw-phase jackpot contribution honors the deal-time starred slot.
		// This keeps "only the starred 4OAK grows this round" consistent across
		// both the deal bet and the draw bet even if the ledger's live active
		// slot has since rotated to a new value for the next deal.
		ApplyJackpotContributions(ledger, EngineCfg, round.ActiveFourOfAKindSlotAtDeal);
		ledger.NetSinceLastClose = Math.Max(ledger.CapitalIn - ledger.CapitalOut, 0m);

		await store.UpdateMachineLedgerAsync(ledger);

		var profile = await RequireProfileAsync(userId);
		await store.AddWalletLedgerEntryAsync(new WalletLedgerEntry
		{
			UserId = userId,
			Amount = -drawAnte,
			BalanceAfter = session.MachineCredits, // Represents machine credits context here
			TransactionType = "DrawBet",
			ReferenceId = round.RoundId.ToString("N"),
			CreatedUtc = DateTime.UtcNow
		});

		var holdMask = new bool[5];
		foreach (var idx in request.HoldIndexes)
			if (idx >= 0 && idx < 5)
				holdMask[idx] = true;

		UpdateCounterplay(session, AssessCounterplay(round.CleanRoomState.Hand, request.HoldIndexes));

		var machine = await RequireMachineAsync(round.MachineId);
		var engine = CabinetVariantFactory.GetEngine(machine.GameId);

		var state = FiveCardDrawEngine.Reduce(round.CleanRoomState, new RoundAction(RoundActionKind.SetHoldMask, HoldMask: holdMask));
		state = FiveCardDrawEngine.Reduce(state, new RoundAction(RoundActionKind.Draw));

		var evaluation = engine.EvaluateHand(state.Hand);
		var basePayout = FiveCardDrawEngine.ResolvePayout(evaluation, (int)round.BetAmount,
			machine.GameId == 2 ? PaytableProfile.WildWitch : PaytableProfile.Lebanese);

		var specialRules = EngineCfg.ResolvedSpecialRules;
		var aceCard = state.Hand.FirstOrDefault(c => c.Rank == 14);
		var aceMultiplier = basePayout > 0 && aceCard.Rank == 14
			? Math.Max(1, specialRules.BaseGameAceWinMultiplier)
			: 1;
		if (basePayout > 0 && aceCard.Rank == 14 && aceMultiplier > 1)
		{
			basePayout *= aceMultiplier;
			round.AceCard = aceCard.ToLegacyPokerCard();
			round.AceMultiplier = aceMultiplier;
			round.AceMultiplierFired = true;
		}
		else
		{
			round.AceCard = null;
			round.AceMultiplier = 1;
			round.AceMultiplierFired = false;
		}

		decimal payoutScale;

		var scaleState = BuildMachinePolicyState(ledger);
		var policyResolution = MachinePolicy.ResolvePolicy(scaleState, round.RoundEntropySeed);
		payoutScale = policyResolution.ForTier(MachinePolicy.ClassifyHand(evaluation.Category));
		ledger.LastPayoutScale = payoutScale;

		var payout = basePayout > 0 ? (int)Math.Round(basePayout * payoutScale, MidpointRounding.AwayFromZero) : 0;
		var handRankName = MapHandCategory(evaluation);
		var finalCards = state.Hand.Select(c => c.ToLegacyPokerCard()).ToList();

		round.FinalCards = finalCards;
		round.HandRank = handRankName;
		round.WinAmount = payout;
		round.IsCompleted = true;
		round.CleanRoomState = state;
		round.DrawAttempts++;

		decimal jackpotWon = 0;
		if (payout > 0)
		{
			ledger.CapitalOut += payout;
			ledger.BaseCapitalOut += basePayout;
			ledger.ConsecutiveLosses = 0;
			ledger.LastWinChannel = WinChannel.BaseGame;
			if (MachinePolicy.ClassifyHand(evaluation.Category) >= PayoutTier.Medium)
				ledger.RoundsSinceMediumWin = 0;
			ledger.CooldownRoundsRemaining = MachinePolicy.ComputeCooldownLength(evaluation.Category, round.RoundEntropySeed);

			if (evaluation.Category == HandCategory.FullHouse
				&& evaluation.Tiebreak[0] == ledger.JackpotFullHouseRank
				&& ledger.JackpotFullHouse > payout)
			{
				jackpotWon = ledger.JackpotFullHouse;
				ledger.JackpotFullHouse = EngineCfg.JackpotFullHouseStart;
				// Rotate Full House rank: 2-14, wrap around
				ledger.JackpotFullHouseRank = ledger.JackpotFullHouseRank >= 14 ? 2 : ledger.JackpotFullHouseRank + 1;
			}
			else if (evaluation.Category == HandCategory.FourOfAKind && round.ActiveFourOfAKindSlotAtDeal == 0 && ledger.JackpotFourOfAKindA > payout)
			{
				jackpotWon = ledger.JackpotFourOfAKindA;
				ledger.JackpotFourOfAKindA = EngineCfg.JackpotFourOfAKindStart;
			}
			else if (evaluation.Category == HandCategory.FourOfAKind && round.ActiveFourOfAKindSlotAtDeal == 1 && ledger.JackpotFourOfAKindB > payout)
			{
				jackpotWon = ledger.JackpotFourOfAKindB;
				ledger.JackpotFourOfAKindB = EngineCfg.JackpotFourOfAKindStart;
			}
			else if (evaluation.Category == HandCategory.StraightFlush && ledger.JackpotStraightFlush > payout)
			{
				jackpotWon = ledger.JackpotStraightFlush;
				ledger.JackpotStraightFlush = EngineCfg.JackpotStraightFlushStart;
			}
			// Note: Royal Flush pays base paytable only — no jackpot. The 1000x is already massive.


			if (jackpotWon > 0)
			{
				var netJackpot = jackpotWon - payout;
				ledger.CapitalOut += netJackpot;
				ledger.JackpotCapitalOut += netJackpot;
				ledger.LastWinChannel = WinChannel.Jackpot;
			}
			ledger.NetSinceLastClose = Math.Max(ledger.CapitalIn - ledger.CapitalOut, 0m);
		}
		else
		{
			ledger.ConsecutiveLosses++;
			ledger.LastWinChannel = WinChannel.None;
			ledger.NetSinceLastClose = Math.Max(ledger.CapitalIn - ledger.CapitalOut, 0m);
		}

		if (jackpotWon > 0)
		{
			payout = (int)jackpotWon;
		}

		// KENT evaluation: sequential straight in positional slots (ascending OR descending).
		// Evaluated on BOTH initial dealt cards AND final cards after draw.
		// Binary per round — if either qualifies, it's a Kent. Never double-count.
		var initialCleanCards = round.InitialCards.Select(ConvertToCleanRoomCard).ToArray();
		var finalCleanCards = state.Hand;
		bool isKent = FiveCardDrawEngine.IsSequentialBoard(initialCleanCards) || FiveCardDrawEngine.IsSequentialBoard(finalCleanCards);
		if (isKent)
		{
			round.IsKent = true;
			ledger.KentStreak++;
			if (ledger.KentStreak >= 3)
			{
				// Kent jackpot pays!
				jackpotWon = ledger.JackpotKent;
				ledger.JackpotKent = EngineCfg.JackpotKentStart;
				ledger.KentStreak = 0;
				ledger.CapitalOut += jackpotWon;
				ledger.JackpotCapitalOut += jackpotWon;
				payout = (int)jackpotWon;
			}
		}
		round.KentStreak = ledger.KentStreak;

		// SERIE: increments every round, visible counter on cabinet
		ledger.MachineSerie = (int.Parse(string.IsNullOrEmpty(ledger.MachineSerie) ? "0" : ledger.MachineSerie) + 1).ToString();

		session.IsMachineClosed = session.MachineCredits >= machine.CloseThreshold;

		round.WinAmount = payout;
		round.OriginalWinAmount = payout;
		round.JackpotWinAmount = jackpotWon;

		bool doubleUpAvailable = payout > 0;
		round.DoubleUpOffered = doubleUpAvailable;

		await store.UpdateMachineLedgerAsync(ledger);
		await store.UpdateMachineSessionAsync(session);
		await store.SaveRoundAsync(round);

		var cursor = await store.AdvanceCabinetStateCursorAsync(userId, round.MachineId);

		var jackpots = SnapshotJackpots(ledger);

		stateCache.InvalidateActiveRound(userId, round.MachineId);
		stateCache.InvalidateMachineSession(userId, round.MachineId);
		return new DrawResultDto(round.RoundId, finalCards.Select(ToDto).ToArray(), handRankName, payout, session.MachineCredits, jackpotWon, jackpots, doubleUpAvailable, cursor.StateVersion, cursor.SequenceNumber);
	}

	public async Task<DoubleUpResultDto> StartDoubleUpAsync(Guid userId, Guid roundId, CancellationToken cancellationToken)
	{
		// Round lock FIRST: concurrent starts for the same round must block before
		// reading the round (regression-tested serialization contract). The session
		// gate is taken inside — lock order is always round lock → session gate.
		var startLock = await AcquireDoubleUpStartLockAsync(roundId, cancellationToken);

		try
		{
			var probe = await store.GetRoundAsync(roundId);
			if (probe == null || probe.UserId != userId)
				throw new KeyNotFoundException("Round not found");
			return await WithSessionGateAsync(userId, probe.MachineId, cancellationToken,
				() => StartDoubleUpCoreAsync(userId, roundId, cancellationToken));
		}
		finally
		{
			startLock.Gate.Release();
			ReleaseDoubleUpStartLockReference(roundId, startLock);
		}
	}

	private static async Task<DoubleUpStartLock> AcquireDoubleUpStartLockAsync(Guid roundId, CancellationToken cancellationToken)
	{
		while (true)
		{
			var startLock = DoubleUpStartLocks.GetOrAdd(roundId, _ => new DoubleUpStartLock());
			if (!TryRetainDoubleUpStartLock(roundId, startLock))
			{
				continue;
			}

			try
			{
				await startLock.Gate.WaitAsync(cancellationToken);
				return startLock;
			}
			catch
			{
				ReleaseDoubleUpStartLockReference(roundId, startLock);
				throw;
			}
		}
	}

	private static bool TryRetainDoubleUpStartLock(Guid roundId, DoubleUpStartLock startLock)
	{
		lock (startLock.SyncRoot)
		{
			if (!DoubleUpStartLocks.TryGetValue(roundId, out var currentLock)
				|| !ReferenceEquals(currentLock, startLock))
			{
				return false;
			}

			startLock.ReferenceCount++;
			return true;
		}
	}

	private static void ReleaseDoubleUpStartLockReference(Guid roundId, DoubleUpStartLock startLock)
	{
		var disposeLock = false;
		lock (startLock.SyncRoot)
		{
			startLock.ReferenceCount--;
			if (startLock.ReferenceCount == 0)
			{
				DoubleUpStartLocks.TryRemove(roundId, out _);
				disposeLock = true;
			}
		}

		if (disposeLock)
		{
			startLock.Gate.Dispose();
		}
	}

	private async Task<DoubleUpResultDto> StartDoubleUpCoreAsync(Guid userId, Guid roundId, CancellationToken cancellationToken)
	{
		var round = await store.GetRoundAsync(roundId);
		if (round == null || round.UserId != userId)
			throw new KeyNotFoundException("Round not found");
		if (round.IsPayoutSettled)
			throw new InvalidOperationException("Payout already settled");
		if (!round.IsCompleted || round.WinAmount <= 0)
			throw new InvalidOperationException("No win to double up");
		round.DoubleUpOffered = true;

		if (round.DoubleUpSession is { IsTerminal: false } existingSession)
		{
			var existingSessionBank = await RequireMachineSessionAsync(userId, round.MachineId, createIfMissing: false);
			var existingNoise = GenerateNoise(round.RoundEntropySeed, existingSession.CurrentRoundIndex);
			return new DoubleUpResultDto(roundId, "Started", existingSession.CurrentAmount, existingSessionBank.MachineCredits,
				DealerCard: ToCleanRoomDto(existingSession.DealerCard),
				SwitchesRemaining: existingSession.Options.MaxSwitchesPerRound - existingSession.SwitchCountInRound,
				IsNoLoseActive: existingSession.IsNoLoseActive,
				CurrentRoundIndex: existingSession.CurrentRoundIndex,
				Noise: existingNoise,
				CardTrail: BuildCardTrail(existingSession),
				BoardHandRank: existingSession.BoardHandRank?.ToString(),
				BoardBonusAmount: existingSession.LastBoardBonusAmount,
				SlotIndex: existingSession.LastResolvedBoardSlotIndex,
				IsLucky5Active: existingSession.IsNoLoseActive,
				CurrentBonusAmount: existingSession.BoardBonusTotal,
				AceCard: round.AceCard != null,
				AceMultiplier: round.AceMultiplier,
				AceMultiplierFired: round.AceMultiplierFired);
		}

		var machine = await RequireMachineAsync(round.MachineId);
		var sessionBank = await RequireMachineSessionAsync(userId, round.MachineId, createIfMissing: false);
		if (sessionBank.IsMachineClosed || sessionBank.MachineCredits >= machine.CloseThreshold)
			throw new InvalidOperationException("Machine closed - take score and cash out to wallet");
		var machineCreditBaseline = (int)Math.Min(sessionBank.MachineCredits, int.MaxValue);

		// WinAmount already includes the Ace multiplier from DrawAsync.
		var startingAmount = (int)round.WinAmount;
		var engine = CabinetVariantFactory.GetEngine(machine.GameId);

		Lucky5DoubleUpSession session;
		if (machine.GameId == 1)
		{
			var ledger = await RequireMachineLedgerAsync(round.MachineId);
			var playDeck = MachinePolicy.BuildDoubleUpPlayDeck(
				FiveCardDrawEngine.BuildStandardDeck(),
				round.RoundEntropySeed,
				ledger.RoundsSinceLucky5Hit,
				ledger.NetSinceLastClose,
				round.PolicyMode,
				BuildMachinePolicyState(ledger),
				startingAmount,
				machineCreditBaseline);

			var specialRules = EngineCfg.ResolvedSpecialRules;
			session = Lucky5DoubleUpEngine.CreateSessionFromDeck(
				round.RoundEntropySeed,
				playDeck,
				startingAmount,
				machineCreditBaseline,
				new Lucky5DoubleUpOptions(
					FirstLuckyMultiplier: specialRules.LuckyFiveFirstSwitchMultiplier,
					RepeatLuckyMultiplier: specialRules.LuckyFiveRepeatSwitchMultiplier,
					MaxCreditLimit: Decimal.ToInt32(machine.CloseThreshold),
					AceCountsHiOrLo: specialRules.AceAutoWinsDoubleUp,
					LuckyFiveArmsNoLose: specialRules.LuckyFiveSwitchArmsNoLose),
				Decimal.ToInt32(round.BetAmount));
		}
		else
		{
			session = (Lucky5DoubleUpSession)engine.StartDoubleUp(startingAmount, round.RoundEntropySeed, machineCreditBaseline, Decimal.ToInt32(round.BetAmount));
		}

		round.DoubleUpSession = session;
		round.EnteredDoubleUp = true;

		await store.SaveRoundAsync(round);
		var cursor = await store.AdvanceCabinetStateCursorAsync(userId, round.MachineId);

		var noise = GenerateNoise(round.RoundEntropySeed, 0);
		InvalidateCaches(userId, round.MachineId);
		return new DoubleUpResultDto(roundId, "Started", session.CurrentAmount, sessionBank.MachineCredits,
			DealerCard: ToCleanRoomDto(session.DealerCard),
			SwitchesRemaining: session.Options.MaxSwitchesPerRound - session.SwitchCountInRound,
			IsNoLoseActive: session.IsNoLoseActive,
			CurrentRoundIndex: session.CurrentRoundIndex,
			Noise: noise,
			CardTrail: BuildCardTrail(session),
			BoardHandRank: session.BoardHandRank?.ToString(),
			BoardBonusAmount: session.LastBoardBonusAmount,
			SlotIndex: session.LastResolvedBoardSlotIndex,
			IsLucky5Active: session.IsNoLoseActive,
			CurrentBonusAmount: session.BoardBonusTotal,
			AceCard: round.AceCard != null,
			AceMultiplier: round.AceMultiplier,
			AceMultiplierFired: round.AceMultiplierFired,
			StateVersion: cursor.StateVersion,
			SequenceNumber: cursor.SequenceNumber);
	}

	public async Task<DoubleUpResultDto> SwitchDealerAsync(Guid userId, Guid roundId, CancellationToken cancellationToken)
	{
		var probe = await store.GetRoundAsync(roundId);
		if (probe == null || probe.UserId != userId)
			throw new KeyNotFoundException("Round not found");
		return await WithSessionGateAsync(userId, probe.MachineId, cancellationToken,
			() => SwitchDealerCoreAsync(userId, roundId, cancellationToken));
	}

	private async Task<DoubleUpResultDto> SwitchDealerCoreAsync(Guid userId, Guid roundId, CancellationToken cancellationToken)
	{
		var round = await store.GetRoundAsync(roundId);
		if (round == null || round.UserId != userId)
			throw new KeyNotFoundException("Round not found");
		if (round.DoubleUpSession is null)
			throw new InvalidOperationException("Double-up session not started");

		var session = Lucky5DoubleUpEngine.SwitchDealer(round.DoubleUpSession);
		round.DoubleUpSession = session;
		var isLucky = session.DealerCard.Rank == 5 && session.DealerCard.Suit == 'S';
		var luckyMult = 0;

		var ledger = await RequireMachineLedgerAsync(round.MachineId);

		if (isLucky)
		{
			luckyMult = session.LuckyHitCount == 1 ? session.Options.FirstLuckyMultiplier : session.Options.RepeatLuckyMultiplier;
			ledger.RoundsSinceLucky5Hit = 0;
			await store.UpdateMachineLedgerAsync(ledger);
		}

		await store.SaveRoundAsync(round);

		var sessionBank = await RequireMachineSessionAsync(userId, round.MachineId, createIfMissing: false);
		var noise = GenerateNoise(round.RoundEntropySeed, session.CurrentRoundIndex);

		if (session.IsTerminal && session.TerminalOutcome == Lucky5DoubleUpOutcome.MachineClosed)
		{
			await FinalizeDoubleUpAsync(round, sessionBank, session.CashoutCredits);
			var closedCursor = await store.AdvanceCabinetStateCursorAsync(userId, round.MachineId);
			InvalidateCaches(userId, round.MachineId);
			return new DoubleUpResultDto(roundId, "MachineClosed", session.CashoutCredits, sessionBank.MachineCredits,
				DealerCard: ToCleanRoomDto(session.DealerCard),
				SwitchesRemaining: 0,
				IsNoLoseActive: session.IsNoLoseActive,
				LuckyMultiplier: luckyMult,
				CurrentRoundIndex: session.CurrentRoundIndex,
				Noise: noise,
				CardTrail: BuildCardTrail(session),
				BoardHandRank: session.BoardHandRank?.ToString(),
				BoardBonusAmount: session.LastBoardBonusAmount,
				SlotIndex: session.LastResolvedBoardSlotIndex,
				IsLucky5Active: session.IsNoLoseActive,
				CurrentBonusAmount: session.BoardBonusTotal,
				StateVersion: closedCursor.StateVersion,
				SequenceNumber: closedCursor.SequenceNumber);
		}

		var cursor = await store.AdvanceCabinetStateCursorAsync(userId, round.MachineId);
		InvalidateCaches(userId, round.MachineId);
		return new DoubleUpResultDto(roundId, isLucky ? "Lucky5" : "Switched", session.CurrentAmount, sessionBank.MachineCredits,
			DealerCard: ToCleanRoomDto(session.DealerCard),
			SwitchesRemaining: session.Options.MaxSwitchesPerRound - session.SwitchCountInRound,
			IsNoLoseActive: session.IsNoLoseActive,
			LuckyMultiplier: luckyMult,
			CurrentRoundIndex: session.CurrentRoundIndex,
			Noise: noise,
			CardTrail: BuildCardTrail(session),
			BoardHandRank: session.BoardHandRank?.ToString(),
			BoardBonusAmount: session.LastBoardBonusAmount,
			SlotIndex: session.LastResolvedBoardSlotIndex,
			IsLucky5Active: session.IsNoLoseActive,
			CurrentBonusAmount: session.BoardBonusTotal,
			StateVersion: cursor.StateVersion,
			SequenceNumber: cursor.SequenceNumber);
	}

	public async Task<DoubleUpResultDto> SwapDoubleUpCardAsync(Guid userId, Guid roundId, int swapPosition, CancellationToken cancellationToken)
	{
		var probe = await store.GetRoundAsync(roundId);
		if (probe == null || probe.UserId != userId)
			throw new KeyNotFoundException("Round not found");
		return await WithSessionGateAsync(userId, probe.MachineId, cancellationToken,
			() => SwapDoubleUpCardCoreAsync(userId, roundId, swapPosition, cancellationToken));
	}

	private async Task<DoubleUpResultDto> SwapDoubleUpCardCoreAsync(Guid userId, Guid roundId, int swapPosition, CancellationToken cancellationToken)
	{
		var round = await store.GetRoundAsync(roundId);
		if (round == null || round.UserId != userId)
			throw new KeyNotFoundException("Round not found");
		if (round.DoubleUpSession is null)
			throw new InvalidOperationException("Double-up session not started");

		var session = Lucky5DoubleUpEngine.SwapChallenger(round.DoubleUpSession, swapPosition);
		round.DoubleUpSession = session;
		await store.SaveRoundAsync(round);
		var cursor = await store.AdvanceCabinetStateCursorAsync(userId, round.MachineId);

		var sessionBank = await RequireMachineSessionAsync(userId, round.MachineId, createIfMissing: false);
		var noise = GenerateNoise(round.RoundEntropySeed, session.CurrentRoundIndex);

		return new DoubleUpResultDto(
			roundId,
			"SwapCard",
			session.CurrentAmount,
			sessionBank.MachineCredits,
			DealerCard: ToCleanRoomDto(session.DealerCard),
			ChallengerCard: ToCleanRoomDto(session.Deck[swapPosition]),
			SwitchesRemaining: session.Options.MaxSwitchesPerRound - session.SwitchCountInRound,
			IsNoLoseActive: session.IsNoLoseActive,
			CurrentRoundIndex: session.CurrentRoundIndex,
			Noise: noise,
			CardTrail: BuildCardTrail(session),
			BoardHandRank: session.BoardHandRank?.ToString(),
			BoardBonusAmount: session.LastBoardBonusAmount,
			SlotIndex: session.LastResolvedBoardSlotIndex,
			IsLucky5Active: session.IsNoLoseActive,
			CurrentBonusAmount: session.BoardBonusTotal,
			SwapActivePosition: session.SwapActivePosition,
			StateVersion: cursor.StateVersion,
			SequenceNumber: cursor.SequenceNumber);
	}

	public async Task<DoubleUpResultDto> GuessDoubleUpAsync(Guid userId, Guid roundId, string guess, CancellationToken cancellationToken)
	{
		var probe = await store.GetRoundAsync(roundId);
		if (probe == null || probe.UserId != userId)
			throw new KeyNotFoundException("Round not found");
		return await WithSessionGateAsync(userId, probe.MachineId, cancellationToken,
			() => GuessDoubleUpCoreAsync(userId, roundId, guess, cancellationToken));
	}

	private async Task<DoubleUpResultDto> GuessDoubleUpCoreAsync(Guid userId, Guid roundId, string guess, CancellationToken cancellationToken)
	{
		var round = await store.GetRoundAsync(roundId);
		if (round == null || round.UserId != userId)
			throw new KeyNotFoundException("Round not found");
		if (round.IsPayoutSettled)
			throw new InvalidOperationException("Payout already settled");
		if (round.DoubleUpSession is null)
			throw new InvalidOperationException("Double-up session not started. Call StartDoubleUp first.");

		var parsedGuess = guess.Equals("big", StringComparison.OrdinalIgnoreCase) ? BigSmallGuess.Big : BigSmallGuess.Small;
		var resolution = Lucky5DoubleUpEngine.ResolveGuess(round!.DoubleUpSession!, parsedGuess);
		round.DoubleUpSession = resolution.Session;
		var sessionBank = await RequireMachineSessionAsync(userId, round.MachineId, createIfMissing: false);
		var noise = GenerateNoise(round.RoundEntropySeed, resolution.Session.CurrentRoundIndex);

		DoubleUpResultDto guessResult;
		CabinetStateCursor cursor;
		switch (resolution.Outcome)
		{
			case Lucky5DoubleUpOutcome.Win:
				await store.SaveRoundAsync(round);
				cursor = await store.AdvanceCabinetStateCursorAsync(userId, round.MachineId);
				guessResult = new DoubleUpResultDto(
					roundId,
					"Win",
					resolution.NextAmount,
					sessionBank.MachineCredits,
					DealerCard: ToCleanRoomDto(resolution.Session.DealerCard),
					ChallengerCard: ToCleanRoomDto(resolution.ChallengerCard),
					SwitchesRemaining: resolution.Session.Options.MaxSwitchesPerRound - resolution.Session.SwitchCountInRound,
					IsNoLoseActive: resolution.Session.IsNoLoseActive,
					CurrentRoundIndex: resolution.Session.CurrentRoundIndex,
					Noise: noise,
					CardTrail: BuildCardTrail(resolution.Session),
					BoardHandRank: resolution.Session.BoardHandRank?.ToString(),
					BoardBonusAmount: resolution.Session.LastBoardBonusAmount,
					SlotIndex: resolution.Session.LastResolvedBoardSlotIndex,
					IsLucky5Active: resolution.Session.IsNoLoseActive,
					CurrentBonusAmount: resolution.Session.BoardBonusTotal,
					StateVersion: cursor.StateVersion,
					SequenceNumber: cursor.SequenceNumber);
				break;

			case Lucky5DoubleUpOutcome.SafeFail:
				await FinalizeDoubleUpAsync(round, sessionBank, resolution.CashoutCredits);
				cursor = await store.AdvanceCabinetStateCursorAsync(userId, round.MachineId);
				guessResult = new DoubleUpResultDto(
					roundId,
					"SafeFail",
					resolution.CashoutCredits,
					sessionBank.MachineCredits,
					DealerCard: ToCleanRoomDto(resolution.DealerCard),
					ChallengerCard: ToCleanRoomDto(resolution.ChallengerCard),
					SwitchesRemaining: 0,
					IsNoLoseActive: false,
					CurrentRoundIndex: resolution.Session.CurrentRoundIndex,
					Noise: noise,
					CardTrail: BuildCardTrail(resolution.Session),
					BoardHandRank: resolution.Session.BoardHandRank?.ToString(),
					BoardBonusAmount: resolution.Session.LastBoardBonusAmount,
					SlotIndex: resolution.Session.LastResolvedBoardSlotIndex,
					IsLucky5Active: false,
					CurrentBonusAmount: resolution.Session.BoardBonusTotal,
					StateVersion: cursor.StateVersion,
					SequenceNumber: cursor.SequenceNumber);
				break;

			case Lucky5DoubleUpOutcome.MachineClosed:
				await FinalizeDoubleUpAsync(round, sessionBank, resolution.CashoutCredits);
				cursor = await store.AdvanceCabinetStateCursorAsync(userId, round.MachineId);
				guessResult = new DoubleUpResultDto(
					roundId,
					"MachineClosed",
					resolution.CashoutCredits,
					sessionBank.MachineCredits,
					DealerCard: ToCleanRoomDto(resolution.DealerCard),
					ChallengerCard: ToCleanRoomDto(resolution.ChallengerCard),
					SwitchesRemaining: 0,
					CurrentRoundIndex: resolution.Session.CurrentRoundIndex,
					Noise: noise,
					CardTrail: BuildCardTrail(resolution.Session),
					BoardHandRank: resolution.Session.BoardHandRank?.ToString(),
					BoardBonusAmount: resolution.Session.LastBoardBonusAmount,
					SlotIndex: resolution.Session.LastResolvedBoardSlotIndex,
					IsLucky5Active: false,
					CurrentBonusAmount: resolution.Session.BoardBonusTotal,
					StateVersion: cursor.StateVersion,
					SequenceNumber: cursor.SequenceNumber);
				break;

			default:
				await FinalizeDoubleUpAsync(round, sessionBank, 0);
				round.WinAmount = 0;
				await store.SaveRoundAsync(round);
				cursor = await store.AdvanceCabinetStateCursorAsync(userId, round.MachineId);
				guessResult = new DoubleUpResultDto(
					roundId,
					"Lose",
					0,
					sessionBank.MachineCredits,
					DealerCard: ToCleanRoomDto(resolution.DealerCard),
					ChallengerCard: ToCleanRoomDto(resolution.ChallengerCard),
					SwitchesRemaining: 0,
					CurrentRoundIndex: resolution.Session.CurrentRoundIndex,
					Noise: noise,
					CardTrail: BuildCardTrail(resolution.Session),
					BoardHandRank: resolution.Session.BoardHandRank?.ToString(),
					BoardBonusAmount: resolution.Session.LastBoardBonusAmount,
					SlotIndex: resolution.Session.LastResolvedBoardSlotIndex,
					IsLucky5Active: false,
					CurrentBonusAmount: resolution.Session.BoardBonusTotal,
					StateVersion: cursor.StateVersion,
					SequenceNumber: cursor.SequenceNumber);
				break;
		}
		InvalidateCaches(userId, round.MachineId);
		return guessResult;
	}

	public async Task<DoubleUpResultDto> CashoutDoubleUpAsync(Guid userId, Guid roundId, CancellationToken cancellationToken)
	{
		var probe = await store.GetRoundAsync(roundId);
		if (probe == null || probe.UserId != userId)
			throw new KeyNotFoundException("Round not found");
		return await WithSessionGateAsync(userId, probe.MachineId, cancellationToken,
			() => CashoutDoubleUpCoreAsync(userId, roundId, cancellationToken));
	}

	private async Task<DoubleUpResultDto> CashoutDoubleUpCoreAsync(Guid userId, Guid roundId, CancellationToken cancellationToken)
	{
		var round = await store.GetRoundAsync(roundId);
		if (round == null || round.UserId != userId)
			throw new KeyNotFoundException("Round not found");
		var session = await RequireMachineSessionAsync(userId, round.MachineId, createIfMissing: false);
		var cashoutAmount = round.DoubleUpSession != null ? round.DoubleUpSession.CurrentAmount : (int)round.WinAmount;
		if (round.IsPayoutSettled)
		{
			var earlyStatus = session.IsMachineClosed ? "MachineClosed" : "Cashout";
			var earlyCursor = await store.GetOrInitializeCabinetStateCursorAsync(userId, round.MachineId);
			return new DoubleUpResultDto(roundId, earlyStatus, 0, session.MachineCredits,
				StateVersion: earlyCursor.StateVersion,
				SequenceNumber: earlyCursor.SequenceNumber);
		}

		CabinetStateCursor cursor;
		if (round.DoubleUpSession != null && !round.DoubleUpSession.IsTerminal)
		{
			await FinalizeDoubleUpAsync(round, session, cashoutAmount);
			cursor = await store.AdvanceCabinetStateCursorAsync(userId, round.MachineId);
		}
		else if (round.DoubleUpSession == null)
		{
			session.MachineCredits += cashoutAmount;
			session.LastUpdatedUtc = DateTime.UtcNow;
			round.SettledAmount += cashoutAmount;
			round.IsPayoutSettled = true;

			var ledger = await RequireMachineLedgerAsync(round.MachineId);
			var delta = round.SettledAmount - round.OriginalWinAmount;
			if (delta != 0) ledger.CapitalOut += delta;
			ledger.LastWinChannel = round.JackpotWinAmount > 0 ? WinChannel.Jackpot : WinChannel.BaseGame;
			ledger.NetSinceLastClose = Math.Max(ledger.CapitalIn - ledger.CapitalOut, 0m);

			await store.UpdateMachineLedgerAsync(ledger);

			var machine = await RequireMachineAsync(round.MachineId);
			session.IsMachineClosed = session.MachineCredits >= machine.CloseThreshold;
			await store.UpdateMachineSessionAsync(session);

			var profile = await RequireProfileAsync(userId);
			await store.AddWalletLedgerEntryAsync(new WalletLedgerEntry
			{
				UserId = userId,
				Amount = cashoutAmount,
				BalanceAfter = session.MachineCredits, // represents machine context here
				TransactionType = "Cashout",
				ReferenceId = round.RoundId.ToString("N"),
				CreatedUtc = DateTime.UtcNow
			});

			await store.SaveRoundAsync(round);
			cursor = await store.AdvanceCabinetStateCursorAsync(userId, round.MachineId);
		}
		else
		{
			cursor = await store.GetOrInitializeCabinetStateCursorAsync(userId, round.MachineId);
		}
		var status = session.IsMachineClosed ? "MachineClosed" : "Cashout";
		InvalidateCaches(userId, round.MachineId);
		return new DoubleUpResultDto(roundId, status, cashoutAmount, session.MachineCredits,
			StateVersion: cursor.StateVersion,
			SequenceNumber: cursor.SequenceNumber);
	}

	public async Task<DoubleUpResultDto> TakeHalfAsync(Guid userId, Guid roundId, CancellationToken cancellationToken)
	{
		var probe = await store.GetRoundAsync(roundId);
		if (probe == null || probe.UserId != userId)
			throw new KeyNotFoundException("Round not found");
		return await WithSessionGateAsync(userId, probe.MachineId, cancellationToken,
			() => TakeHalfCoreAsync(userId, roundId, cancellationToken));
	}

	private async Task<DoubleUpResultDto> TakeHalfCoreAsync(Guid userId, Guid roundId, CancellationToken cancellationToken)
	{
		var round = await store.GetRoundAsync(roundId);
		if (round == null || round.UserId != userId)
			throw new KeyNotFoundException("Round not found");
		if (round.IsPayoutSettled)
			throw new InvalidOperationException("Payout already settled");
		if (round.TakeHalfUsed)
			throw new InvalidOperationException("Take-half already used this round");

		var machine = await RequireMachineAsync(round.MachineId);
		var session = await RequireMachineSessionAsync(userId, round.MachineId, createIfMissing: false);
		var currentAmount = round.DoubleUpSession != null ? round.DoubleUpSession.CurrentAmount : (int)round.WinAmount;
		if (currentAmount <= 1) throw new InvalidOperationException("Amount too small to split");

		var half = currentAmount / 2;
		var remaining = currentAmount - half;

		// Add half to machine credits immediately
		session.MachineCredits += half;
		session.LastUpdatedUtc = DateTime.UtcNow;
		session.IsMachineClosed = session.MachineCredits >= machine.CloseThreshold;

		// Update round state
		round.TakeHalfUsed = true;
		round.SettledAmount += half;

		// Update ledger
		var ledger = await RequireMachineLedgerAsync(round.MachineId);
		var delta = half;
		if (delta != 0) ledger.CapitalOut += delta;
		ledger.NetSinceLastClose = Math.Max(ledger.CapitalIn - ledger.CapitalOut, 0m);

		await store.UpdateMachineLedgerAsync(ledger);
		await store.UpdateMachineSessionAsync(session);

		var profile = await RequireProfileAsync(userId);

		// Record ledger entry
		await store.AddWalletLedgerEntryAsync(new WalletLedgerEntry
		{
			UserId = userId,
			Amount = half,
			BalanceAfter = session.MachineCredits, // context of machine credits
			TransactionType = "TakeHalf",
			ReferenceId = round.RoundId.ToString("N"),
			CreatedUtc = DateTime.UtcNow
		});

		// Update double-up session if active
		if (round.DoubleUpSession != null)
		{
			round.DoubleUpSession = round.DoubleUpSession with { CurrentAmount = remaining };
		}

		await store.SaveRoundAsync(round);
		var cursor = await store.AdvanceCabinetStateCursorAsync(userId, round.MachineId);

		var noise = GenerateNoise(round.RoundEntropySeed, 0);
		var switchesRemaining = round.DoubleUpSession is null
			? 0
			: round.DoubleUpSession.Options.MaxSwitchesPerRound - round.DoubleUpSession.SwitchCountInRound;
		InvalidateCaches(userId, round.MachineId);
		return new DoubleUpResultDto(roundId, "TookHalf", remaining, session.MachineCredits,
			DealerCard: round.DoubleUpSession != null ? ToCleanRoomDto(round.DoubleUpSession.DealerCard) : null,
			SwitchesRemaining: switchesRemaining,
			IsNoLoseActive: round.DoubleUpSession?.IsNoLoseActive ?? false,
			CurrentRoundIndex: round.DoubleUpSession?.CurrentRoundIndex ?? 0,
			Noise: noise,
			CardTrail: round.DoubleUpSession is null ? null : BuildCardTrail(round.DoubleUpSession),
			BoardHandRank: round.DoubleUpSession?.BoardHandRank?.ToString(),
			BoardBonusAmount: round.DoubleUpSession?.LastBoardBonusAmount ?? 0,
			SlotIndex: round.DoubleUpSession?.LastResolvedBoardSlotIndex ?? 0,
			CurrentBonusAmount: round.DoubleUpSession?.BoardBonusTotal,
			StateVersion: cursor.StateVersion,
			SequenceNumber: cursor.SequenceNumber);
	}

	public async Task<JackpotInfoDto> ChangeJackpotRankAsync(int machineId, int rank, CancellationToken cancellationToken)
	{
		if (rank < 2 || rank > 14) throw new ArgumentException("Rank must be between 2 and 14");

		var ledger = await RequireMachineLedgerAsync(machineId);
		ledger.JackpotFullHouseRank = rank;
		await store.UpdateMachineLedgerAsync(ledger);

		return SnapshotJackpots(ledger);
	}

	public async Task<JackpotInfoDto> ChangeCabinetJackpotRankAsync(Guid userId, int machineId, int rank, CancellationToken cancellationToken)
	{
		var session = await RequireMachineSessionAsync(userId, machineId, createIfMissing: false);
		if (session.IsMachineClosed || session.MachineCredits <= 0m)
			throw new InvalidOperationException("Full House rank can only be changed from an active funded session");

		var activeRound = await GetActiveRoundAsync(userId, machineId, cancellationToken);
		if (activeRound is not null)
			throw new InvalidOperationException("Full House rank can only be changed before a round is dealt");

		var result = await ChangeJackpotRankAsync(machineId, rank, cancellationToken);
		InvalidateCaches(userId, machineId);
		return result;
	}

	public async Task<CabinetSnapshotDto> GetCabinetSnapshotAsync(Guid userId, int machineId, CancellationToken cancellationToken)
	{
		var machine = await RequireMachineAsync(machineId);
		var profile = await RequireProfileAsync(userId);
		var session = await RequireMachineSessionAsync(userId, machineId, createIfMissing: true);
		var ledger = await RequireMachineLedgerAsync(machineId);
		var activeRound = await GetActiveRoundAsync(userId, machineId, cancellationToken);
		var cursor = await store.GetOrInitializeCabinetStateCursorAsync(userId, machineId);
		var serverTimeUtc = DateTime.UtcNow;

		return BuildCabinetSnapshot(userId, machine, profile, session, ledger, activeRound, cursor, serverTimeUtc, requiresFullSnapshot: false, recoveryReason: string.Empty);
	}

	public async Task<CabinetReplayDto> GetCabinetReplayAsync(Guid userId, int machineId, long lastStateVersion, long lastSequenceNumber, CancellationToken cancellationToken)
	{
		if (lastStateVersion < 0 || lastSequenceNumber < 0)
		{
			var snapshot = await BuildRecoverySnapshotAsync(userId, machineId, "invalid_reconnect_cursor", cancellationToken);
			return new CabinetReplayDto(
				ReplayAvailable: false,
				RequiresFullSnapshot: true,
				FromSequenceNumber: Math.Max(0, lastSequenceNumber),
				ToSequenceNumber: snapshot.SequenceNumber,
				StateVersion: snapshot.StateVersion,
				SequenceNumber: snapshot.SequenceNumber,
				Events: [],
				Snapshot: snapshot,
				Error: new CabinetCommandErrorDto("INVALID_RECONNECT_CURSOR", "Reconnect cursors must be zero or greater.", false));
		}

		await RequireMachineAsync(machineId);
		var cursor = await store.GetOrInitializeCabinetStateCursorAsync(userId, machineId);
		if (lastSequenceNumber == cursor.SequenceNumber && lastStateVersion == cursor.StateVersion)
		{
			return new CabinetReplayDto(true, false, lastSequenceNumber, cursor.SequenceNumber, cursor.StateVersion, cursor.SequenceNumber, []);
		}

		if (lastSequenceNumber > cursor.SequenceNumber || lastStateVersion > cursor.StateVersion)
		{
			var snapshot = await BuildRecoverySnapshotAsync(userId, machineId, "client_cursor_ahead_of_server", cancellationToken);
			return new CabinetReplayDto(
				ReplayAvailable: false,
				RequiresFullSnapshot: true,
				FromSequenceNumber: lastSequenceNumber,
				ToSequenceNumber: cursor.SequenceNumber,
				StateVersion: cursor.StateVersion,
				SequenceNumber: cursor.SequenceNumber,
				Events: [],
				Snapshot: snapshot,
				Error: new CabinetCommandErrorDto("REPLAY_GAP", "Client reconnect cursor is ahead of the authoritative server cursor. Apply the returned snapshot.", false));
		}

		var records = await store.GetCabinetEventRecordsAfterAsync(userId, machineId, lastSequenceNumber, CabinetReplayMaxEvents);
		var ordered = records.OrderBy(record => record.SequenceNumber).ToArray();
		if (!IsContiguousReplay(ordered, lastSequenceNumber, cursor.SequenceNumber))
		{
			var snapshot = await BuildRecoverySnapshotAsync(userId, machineId, "replay_gap", cancellationToken);
			return new CabinetReplayDto(
				ReplayAvailable: false,
				RequiresFullSnapshot: true,
				FromSequenceNumber: lastSequenceNumber,
				ToSequenceNumber: cursor.SequenceNumber,
				StateVersion: cursor.StateVersion,
				SequenceNumber: cursor.SequenceNumber,
				Events: [],
				Snapshot: snapshot,
				Error: new CabinetCommandErrorDto("REPLAY_GAP", "A contiguous replay range is not available. Apply the returned snapshot before enabling cabinet commands.", false));
		}

		var events = ordered.Select(ToCabinetEventDto).ToArray();
		return new CabinetReplayDto(true, false, lastSequenceNumber, cursor.SequenceNumber, cursor.StateVersion, cursor.SequenceNumber, events);
	}

	private async Task<CabinetSnapshotDto> BuildRecoverySnapshotAsync(Guid userId, int machineId, string reason, CancellationToken cancellationToken)
	{
		var machine = await RequireMachineAsync(machineId);
		var profile = await RequireProfileAsync(userId);
		var session = await RequireMachineSessionAsync(userId, machineId, createIfMissing: true);
		var ledger = await RequireMachineLedgerAsync(machineId);
		var activeRound = await GetActiveRoundAsync(userId, machineId, cancellationToken);
		var cursor = await store.GetOrInitializeCabinetStateCursorAsync(userId, machineId);
		return BuildCabinetSnapshot(userId, machine, profile, session, ledger, activeRound, cursor, DateTime.UtcNow, requiresFullSnapshot: true, recoveryReason: reason);
	}

	private static bool IsContiguousReplay(IReadOnlyList<CabinetEventRecord> records, long lastSequenceNumber, long currentSequenceNumber)
	{
		if (currentSequenceNumber == lastSequenceNumber)
		{
			return true;
		}

		if (records.Count == 0 || records[0].SequenceNumber != lastSequenceNumber + 1)
		{
			return false;
		}

		var expected = lastSequenceNumber + 1;
		foreach (var record in records)
		{
			if (record.SequenceNumber != expected)
			{
				return false;
			}

			expected++;
		}

		return records[^1].SequenceNumber == currentSequenceNumber;
	}

	private static CabinetEventDto ToCabinetEventDto(CabinetEventRecord record)
	{
		var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(record.PayloadJson, CabinetJsonOptions)
			?? new Dictionary<string, object?>();

		return new CabinetEventDto(
			MessageType: "cabinet_event",
			SchemaVersion: CabinetSchemaVersion,
			EventId: record.EventId,
			EventType: record.EventType,
			StateVersion: record.StateVersion,
			Payload: payload,
			SequenceNumber: record.SequenceNumber,
			ServerTimeUtc: record.CreatedUtc);
	}

	public async Task<CabinetCommandResultDto> SubmitCabinetCommandAsync(Guid userId, CabinetCommandDto command, CancellationToken cancellationToken)
	{
		if (command is null)
			throw new ArgumentNullException(nameof(command));

		var lockKey = BuildCabinetCommandLockKey(userId, command.MachineId);
		var commandLock = CabinetCommandLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
		await commandLock.WaitAsync(cancellationToken);

		try
		{
			if (!TryValidateCabinetCommand(command, out var validationError))
			{
				return await BuildCabinetCommandRejectionAsync(userId, command, "invalid", validationError!, includeSnapshot: false, cancellationToken);
			}

			var requestHash = ComputeCabinetCommandHash(command);
			var existing = await store.GetCabinetCommandRecordAsync(userId, command.CommandId, command.IdempotencyKey);
			if (existing is not null)
			{
				if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
				{
					return await BuildCabinetCommandRejectionAsync(
						userId,
						command,
						"rejected",
						new CabinetCommandErrorDto("IDEMPOTENCY_CONFLICT", "The command id or idempotency key was already used with different command content.", false),
						includeSnapshot: true,
						cancellationToken);
				}

				var cached = JsonSerializer.Deserialize<CabinetCommandResultDto>(existing.ResultJson, CabinetJsonOptions);
				if (cached is null)
				{
					return await BuildCabinetCommandRejectionAsync(
						userId,
						command,
						"requires_snapshot",
						new CabinetCommandErrorDto("COMMAND_REPLAY_UNAVAILABLE", "The cached command result could not be replayed; apply the returned snapshot before retrying with a new command.", false),
						includeSnapshot: true,
						cancellationToken);
				}

				return cached with
				{
					Status = "duplicate",
					ServerTimeUtc = DateTime.UtcNow
				};
			}

			var currentCursor = await store.GetOrInitializeCabinetStateCursorAsync(userId, command.MachineId);
			if (RequiresExpectedStateVersion(command.CommandType) && command.ExpectedStateVersion != currentCursor.StateVersion)
			{
				var stale = await BuildCabinetCommandRejectionAsync(
					userId,
					command,
					"stale_state",
					new CabinetCommandErrorDto("STALE_STATE", "Command expected a stale cabinet state version. Apply the returned snapshot and issue a new command.", false),
					includeSnapshot: true,
					cancellationToken);

				await SaveCabinetCommandRecordAsync(userId, command, requestHash, stale);
				return stale;
			}

			var sessionMismatch = await HasCabinetSessionMismatchAsync(userId, command);
			if (sessionMismatch)
			{
				var rejected = await BuildCabinetCommandRejectionAsync(
					userId,
					command,
					"requires_snapshot",
					new CabinetCommandErrorDto("SESSION_MISMATCH", "Command session does not match the authoritative machine session. Apply the returned snapshot before issuing a new command.", false),
					includeSnapshot: true,
					cancellationToken);

				await SaveCabinetCommandRecordAsync(userId, command, requestHash, rejected);
				return rejected;
			}

			try
			{
				var previousCursor = await store.GetOrInitializeCabinetStateCursorAsync(userId, command.MachineId);
				var previousVersion = previousCursor.StateVersion;
				await ExecuteCabinetCommandAsync(userId, command, cancellationToken);
				var mutatesCabinetState = MutatesCabinetState(command.CommandType);
				CabinetStateCursor cursor;
				if (mutatesCabinetState)
				{
					cursor = await store.GetOrInitializeCabinetStateCursorAsync(userId, command.MachineId);
					if (cursor.StateVersion == previousVersion)
					{
						cursor = await store.AdvanceCabinetStateCursorAsync(userId, command.MachineId);
					}
				}
				else
				{
					cursor = await store.GetOrInitializeCabinetStateCursorAsync(userId, command.MachineId);
				}

				var snapshot = await GetCabinetSnapshotAsync(userId, command.MachineId, cancellationToken);
				var cabinetEvent = mutatesCabinetState ? await SaveCabinetEventAsync(userId, command, snapshot) : null;
				var accepted = new CabinetCommandResultDto(
					MessageType: "cabinet_command_result",
					SchemaVersion: CabinetSchemaVersion,
					CommandId: command.CommandId,
					IdempotencyKey: command.IdempotencyKey,
					Accepted: true,
					Status: "accepted",
					StateVersion: cursor.StateVersion,
					SequenceNumber: cursor.SequenceNumber,
					ServerTimeUtc: DateTime.UtcNow,
					Snapshot: snapshot,
					Event: cabinetEvent,
					Error: null);

				await SaveCabinetCommandRecordAsync(userId, command, requestHash, accepted);
				return accepted;
			}
			catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
			{
				var rejected = await BuildCabinetCommandRejectionAsync(
					userId,
					command,
					"rejected",
					new CabinetCommandErrorDto("COMMAND_REJECTED", ex.Message, false),
					includeSnapshot: true,
					cancellationToken);

				await SaveCabinetCommandRecordAsync(userId, command, requestHash, rejected);
				return rejected;
			}
		}
		finally
		{
			commandLock.Release();
		}
	}

	private static bool TryValidateCabinetCommand(CabinetCommandDto command, out CabinetCommandErrorDto? error)
	{
		error = null;

		if (!string.IsNullOrWhiteSpace(command.MessageType)
			&& !string.Equals(command.MessageType, "cabinet_command", StringComparison.OrdinalIgnoreCase))
		{
			error = new CabinetCommandErrorDto("INVALID_MESSAGE_TYPE", "Cabinet commands must use message_type 'cabinet_command'.", false);
			return false;
		}

		if (string.IsNullOrWhiteSpace(command.SchemaVersion) || !IsSupportedCabinetSchemaVersion(command.SchemaVersion))
		{
			error = new CabinetCommandErrorDto("INVALID_SCHEMA_VERSION", "Cabinet command schema_version must be 'cabinet.v1'.", false);
			return false;
		}

		if (command.CommandId == Guid.Empty)
		{
			error = new CabinetCommandErrorDto("INVALID_COMMAND_ID", "Cabinet command_id must be a non-empty UUID.", false);
			return false;
		}

		if (command.MachineId <= 0)
		{
			error = new CabinetCommandErrorDto("INVALID_MACHINE", "Cabinet command machine_id must be greater than zero.", false);
			return false;
		}

		if (string.IsNullOrWhiteSpace(command.IdempotencyKey)
			|| command.IdempotencyKey.Length < 8
			|| command.IdempotencyKey.Length > 128)
		{
			error = new CabinetCommandErrorDto("INVALID_IDEMPOTENCY_KEY", "Cabinet command idempotency_key must be 8 to 128 characters.", false);
			return false;
		}

		if (command.ExpectedStateVersion < 0)
		{
			error = new CabinetCommandErrorDto("INVALID_STATE_VERSION", "Cabinet command expected_state_version must be zero or greater.", false);
			return false;
		}

		if (command.ClientSequenceNumber < 0)
		{
			error = new CabinetCommandErrorDto("INVALID_CLIENT_SEQUENCE", "Cabinet command client_sequence_number must be zero or greater.", false);
			return false;
		}

		if (string.IsNullOrWhiteSpace(command.CommandType) || !IsKnownCabinetCommandType(command.CommandType))
		{
			error = new CabinetCommandErrorDto("INVALID_COMMAND_TYPE", $"Unsupported cabinet command_type '{command.CommandType}'.", false);
			return false;
		}

		if (command.Payload is null)
		{
			error = new CabinetCommandErrorDto("INVALID_PAYLOAD", "Cabinet command payload is required.", false);
			return false;
		}

		return true;
	}

	private async Task<bool> HasCabinetSessionMismatchAsync(Guid userId, CabinetCommandDto command)
	{
		if (command.SessionId is null)
		{
			return false;
		}

		var session = await store.GetMachineSessionAsync(userId, command.MachineId);
		return session is not null && session.SessionId != command.SessionId.Value;
	}

	private async Task ExecuteCabinetCommandAsync(Guid userId, CabinetCommandDto command, CancellationToken cancellationToken)
	{
		var type = NormalizeCabinetCommandType(command.CommandType);
		switch (type)
		{
			case "cash_in":
				await CashInAsync(userId, command.MachineId, GetRequiredDecimalPayload(command.Payload, "amount"), cancellationToken);
				return;

			case "cash_out":
				await CashOutAsync(userId, command.MachineId, cancellationToken);
				return;

			case "deal":
				await DealAsync(userId, new DealRequest(command.MachineId, GetRequiredDecimalPayload(command.Payload, "bet_amount")), cancellationToken);
				return;

			case "draw":
				await DrawAsync(userId, new DrawRequest(GetRequiredGuidPayload(command.Payload, "round_id"), GetRequiredIntArrayPayload(command.Payload, "hold_indexes")), cancellationToken);
				return;

			case "double_up_start":
				await StartDoubleUpAsync(userId, GetRequiredGuidPayload(command.Payload, "round_id"), cancellationToken);
				return;

			case "double_up_guess":
				await GuessDoubleUpAsync(userId, GetRequiredGuidPayload(command.Payload, "round_id"), GetRequiredStringPayload(command.Payload, "guess"), cancellationToken);
				return;

			case "double_up_switch":
				await SwitchDealerAsync(userId, GetRequiredGuidPayload(command.Payload, "round_id"), cancellationToken);
				return;

			case "swap_double_up_card":
				// Legacy alias: this IS the dealer-switch action used by older clients.
				await SwitchDealerAsync(userId, GetRequiredGuidPayload(command.Payload, "round_id"), cancellationToken);
				return;
			case "swap_challenger_card":
				await SwapDoubleUpCardAsync(userId, GetRequiredGuidPayload(command.Payload, "round_id"), GetRequiredIntPayload(command.Payload, "swap_position"), cancellationToken);
				return;

			case "take_half":
				await TakeHalfAsync(userId, GetRequiredGuidPayload(command.Payload, "round_id"), cancellationToken);
				return;

			case "take_score":
				await CashoutDoubleUpAsync(userId, GetRequiredGuidPayload(command.Payload, "round_id"), cancellationToken);
				return;

			case "jackpot_rank_change":
				await ChangeCabinetJackpotRankAsync(userId, command.MachineId, GetRequiredIntPayload(command.Payload, "rank"), cancellationToken);
				return;

			case "reset_machine":
				await ResetMachineAsync(userId, command.MachineId, cancellationToken);
				return;

			case "join_machine":
				await GetMachineSessionAsync(userId, command.MachineId, cancellationToken);
				return;

			case "hold":
			case "clear_holds":
			case "bet_change":
				await RequireMachineAsync(command.MachineId);
				return;

			case "leave_machine":
			case "heartbeat":
			case "reconnect_sync":
				await RequireMachineAsync(command.MachineId);
				return;

			default:
				throw new InvalidOperationException($"Cabinet command '{command.CommandType}' is not yet backed by an authoritative server action.");
		}
	}

	private async Task<CabinetCommandResultDto> BuildCabinetCommandRejectionAsync(
		Guid userId,
		CabinetCommandDto command,
		string status,
		CabinetCommandErrorDto error,
		bool includeSnapshot,
		CancellationToken cancellationToken)
	{
		var cursor = await GetCabinetCursorOrDefaultAsync(userId, command.MachineId);
		CabinetSnapshotDto? snapshot = null;
		if (includeSnapshot && command.MachineId > 0)
		{
			try
			{
				snapshot = await GetCabinetSnapshotAsync(userId, command.MachineId, cancellationToken);
				cursor = new CabinetStateCursor
				{
					UserId = userId,
					MachineId = command.MachineId,
					StateVersion = snapshot.StateVersion,
					SequenceNumber = snapshot.SequenceNumber
				};
			}
			catch
			{
				snapshot = null;
			}
		}

		return new CabinetCommandResultDto(
			MessageType: "cabinet_command_result",
			SchemaVersion: CabinetSchemaVersion,
			CommandId: command.CommandId,
			IdempotencyKey: command.IdempotencyKey ?? string.Empty,
			Accepted: false,
			Status: status,
			StateVersion: cursor.StateVersion,
			SequenceNumber: cursor.SequenceNumber,
			ServerTimeUtc: DateTime.UtcNow,
			Snapshot: snapshot,
			Event: null,
			Error: error);
	}

	private async Task SaveCabinetCommandRecordAsync(Guid userId, CabinetCommandDto command, string requestHash, CabinetCommandResultDto result)
	{
		await store.SaveCabinetCommandRecordAsync(new CabinetCommandRecord
		{
			UserId = userId,
			CommandId = command.CommandId,
			IdempotencyKey = command.IdempotencyKey,
			RequestHash = requestHash,
			CommandType = NormalizeCabinetCommandType(command.CommandType),
			MachineId = command.MachineId,
			SessionId = command.SessionId,
			ExpectedStateVersion = command.ExpectedStateVersion,
			Accepted = result.Accepted,
			Status = result.Status,
			StateVersion = result.StateVersion,
			SequenceNumber = result.SequenceNumber,
			ResultJson = JsonSerializer.Serialize(result, CabinetJsonOptions),
			CreatedUtc = DateTime.UtcNow,
			CompletedUtc = DateTime.UtcNow
		});
	}

	private async Task<CabinetEventDto> SaveCabinetEventAsync(Guid userId, CabinetCommandDto command, CabinetSnapshotDto snapshot)
	{
		var payload = BuildCabinetEventPayload(command.CommandType, snapshot);
		var payloadJson = JsonSerializer.Serialize(payload, CabinetJsonOptions);
		var record = new CabinetEventRecord
		{
			UserId = userId,
			MachineId = command.MachineId,
			EventType = ResolveCabinetEventType(command.CommandType),
			StateVersion = snapshot.StateVersion,
			SequenceNumber = snapshot.SequenceNumber,
			PayloadJson = payloadJson,
			CreatedUtc = snapshot.ServerTimeUtc
		};

		await store.SaveCabinetEventRecordAsync(record);
		return ToCabinetEventDto(record);
	}

	private async Task<CabinetStateCursor> GetCabinetCursorOrDefaultAsync(Guid userId, int machineId)
	{
		if (machineId <= 0)
		{
			return new CabinetStateCursor { UserId = userId, MachineId = machineId };
		}

		return await store.GetOrInitializeCabinetStateCursorAsync(userId, machineId);
	}

	private static string ComputeCabinetCommandHash(CabinetCommandDto command)
	{
		var canonical = new SortedDictionary<string, object?>(StringComparer.Ordinal)
		{
			["schema_version"] = NormalizeCabinetSchemaVersion(command.SchemaVersion),
			["command_id"] = command.CommandId.ToString("D"),
			["command_type"] = NormalizeCabinetCommandType(command.CommandType),
			["session_id"] = command.SessionId?.ToString("D"),
			["machine_id"] = command.MachineId,
			["expected_state_version"] = command.ExpectedStateVersion,
			["idempotency_key"] = command.IdempotencyKey,
			["client_sequence_number"] = command.ClientSequenceNumber,
			["payload"] = NormalizePayload(command.Payload)
		};

		var json = JsonSerializer.Serialize(canonical, CabinetJsonOptions);
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
		return Convert.ToHexString(bytes);
	}

	private static IReadOnlyDictionary<string, object?> NormalizePayload(IReadOnlyDictionary<string, object?> payload)
	{
		var normalized = new SortedDictionary<string, object?>(StringComparer.Ordinal);
		foreach (var (key, value) in payload)
		{
			normalized[key] = NormalizePayloadValue(value);
		}

		return normalized;
	}

	private static object? NormalizePayloadValue(object? value)
	{
		if (value is null)
		{
			return null;
		}

		if (value is JsonElement element)
		{
			return NormalizeJsonElement(element);
		}

		if (value is decimal dec)
		{
			return dec.ToString(CultureInfo.InvariantCulture);
		}

		if (value is double dbl)
		{
			return dbl.ToString("R", CultureInfo.InvariantCulture);
		}

		if (value is float flt)
		{
			return flt.ToString("R", CultureInfo.InvariantCulture);
		}

		if (value is int or long or short or byte or bool or string or Guid)
		{
			return value is Guid guid ? guid.ToString("D") : value;
		}

		if (value is IEnumerable<int> ints)
		{
			return ints.ToArray();
		}

		if (value is IEnumerable<object?> objects && value is not string)
		{
			return objects.Select(NormalizePayloadValue).ToArray();
		}

		return value.ToString();
	}

	private static object? NormalizeJsonElement(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.Object => element.EnumerateObject()
				.OrderBy(property => property.Name, StringComparer.Ordinal)
				.ToDictionary(property => property.Name, property => NormalizeJsonElement(property.Value), StringComparer.Ordinal),
			JsonValueKind.Array => element.EnumerateArray().Select(NormalizeJsonElement).ToArray(),
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number => element.TryGetDecimal(out var dec) ? dec.ToString(CultureInfo.InvariantCulture) : element.GetRawText(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Null => null,
			_ => element.GetRawText()
		};
	}

	private static decimal GetRequiredDecimalPayload(IReadOnlyDictionary<string, object?> payload, string key)
	{
		var value = GetRequiredPayloadValue(payload, key);
		return value switch
		{
			decimal dec => dec,
			int i => i,
			long l => l,
			double dbl => Convert.ToDecimal(dbl, CultureInfo.InvariantCulture),
			float flt => Convert.ToDecimal(flt, CultureInfo.InvariantCulture),
			string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
			JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var parsed) => parsed,
			JsonElement element when element.ValueKind == JsonValueKind.String && decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => throw new InvalidOperationException($"Cabinet command payload '{key}' must be a decimal value.")
		};
	}

	private static int GetRequiredIntPayload(IReadOnlyDictionary<string, object?> payload, string key)
	{
		var value = GetRequiredPayloadValue(payload, key);
		return value switch
		{
			int i => i,
			long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
			decimal dec => Decimal.ToInt32(dec),
			string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
			JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var parsed) => parsed,
			JsonElement element when element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => throw new InvalidOperationException($"Cabinet command payload '{key}' must be an integer value.")
		};
	}

	private static Guid GetRequiredGuidPayload(IReadOnlyDictionary<string, object?> payload, string key)
	{
		var value = GetRequiredPayloadValue(payload, key);
		return value switch
		{
			Guid guid => guid,
			string s when Guid.TryParse(s, out var parsed) => parsed,
			JsonElement element when element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var parsed) => parsed,
			_ => throw new InvalidOperationException($"Cabinet command payload '{key}' must be a UUID value.")
		};
	}

	private static string GetRequiredStringPayload(IReadOnlyDictionary<string, object?> payload, string key)
	{
		var value = GetRequiredPayloadValue(payload, key);
		return value switch
		{
			string s when !string.IsNullOrWhiteSpace(s) => s,
			JsonElement element when element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString()) => element.GetString()!,
			_ => throw new InvalidOperationException($"Cabinet command payload '{key}' must be a non-empty string value.")
		};
	}

	private static int[] GetRequiredIntArrayPayload(IReadOnlyDictionary<string, object?> payload, string key)
	{
		var value = GetRequiredPayloadValue(payload, key);
		return value switch
		{
			int[] ints => ints,
			IEnumerable<int> ints => ints.ToArray(),
			JsonElement element when element.ValueKind == JsonValueKind.Array => element.EnumerateArray().Select(ReadJsonInt).ToArray(),
			IEnumerable<object?> objects => objects.Select(ToPayloadInt).ToArray(),
			_ => throw new InvalidOperationException($"Cabinet command payload '{key}' must be an integer array.")
		};
	}

	private static object GetRequiredPayloadValue(IReadOnlyDictionary<string, object?> payload, string key)
	{
		if (!payload.TryGetValue(key, out var value) || value is null)
		{
			throw new InvalidOperationException($"Cabinet command payload is missing required field '{key}'.");
		}

		return value;
	}

	private static int ReadJsonInt(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var parsed))
		{
			return parsed;
		}

		throw new InvalidOperationException("Cabinet command integer array contains a non-integer value.");
	}

	private static int ToPayloadInt(object? value)
	{
		return value switch
		{
			int i => i,
			long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
			JsonElement element => ReadJsonInt(element),
			_ => throw new InvalidOperationException("Cabinet command integer array contains a non-integer value.")
		};
	}

	private static bool IsSupportedCabinetSchemaVersion(string schemaVersion)
		=> string.Equals(NormalizeCabinetSchemaVersion(schemaVersion), CabinetSchemaVersion, StringComparison.Ordinal);

	private static string NormalizeCabinetSchemaVersion(string schemaVersion)
	{
		if (string.IsNullOrWhiteSpace(schemaVersion))
		{
			return string.Empty;
		}

		return string.Equals(schemaVersion, "v1", StringComparison.OrdinalIgnoreCase)
			? CabinetSchemaVersion
			: schemaVersion.Trim().ToLowerInvariant();
	}

	private static string NormalizeCabinetCommandType(string commandType)
		=> commandType.Trim().ToLowerInvariant();

	private static bool RequiresExpectedStateVersion(string commandType)
		=> NormalizeCabinetCommandType(commandType) is not ("heartbeat" or "reconnect_sync");

	private static bool MutatesCabinetState(string commandType)
		=> NormalizeCabinetCommandType(commandType) is not ("leave_machine" or "heartbeat" or "reconnect_sync" or "hold" or "clear_holds" or "bet_change");

	private static bool IsKnownCabinetCommandType(string commandType)
		=> NormalizeCabinetCommandType(commandType) is "cash_in"
			or "cash_out"
			or "deal"
			or "draw"
			or "hold"
			or "clear_holds"
			or "double_up_start"
			or "double_up_guess"
			or "double_up_switch"
			or "swap_double_up_card"
			or "take_half"
			or "take_score"
			or "bet_change"
			or "jackpot_rank_change"
			or "reset_machine"
			or "join_machine"
			or "leave_machine"
			or "heartbeat"
			or "reconnect_sync";

	private static string BuildCabinetCommandLockKey(Guid userId, int machineId)
		=> $"{userId:N}:{machineId}";

	public async Task<ActiveRoundStateDto?> GetActiveRoundAsync(Guid userId, int machineId, CancellationToken cancellationToken)
	{
		var cached = await stateCache.GetActiveRoundAsync(userId, machineId);
		if (cached is not null)
			return cached;

		var round = await store.GetLatestRoundAsync(userId, machineId);

		if (round is null || !IsRoundRecoverable(round))
			return null;

		var state = round.CleanRoomState;
		if (state is null)
			return null;

		// Determine phase
		var duSession = round.DoubleUpSession;
		string phase;
		if (duSession is not null && !duSession.IsTerminal)
			phase = "DoubleUp";
		else if (duSession is not null && duSession.IsTerminal)
			phase = "DoubleUpEnded";
		else if (state.Phase == RoundPhase.Dealt)
			phase = "Dealt";
		else
			phase = "Drawn";

		var cards = round.InitialCards.Select(ToDto).ToArray();
		var resultCards = state.Hand.Select(ToCleanRoomDto).ToArray();

		// Held indexes (only meaningful during Dealt phase)
		var heldIndexes = phase == "Dealt"
			? state.Held
				.Select((held, idx) => held ? idx : -1)
				.Where(idx => idx >= 0)
				.ToArray()
			: Array.Empty<int>();

		// Double-up snapshot
		DoubleUpStateDto? duDto = null;
		if (duSession is not null)
		{
			var switchesRemaining = duSession.IsTerminal
				? 0
				: duSession.Options.MaxSwitchesPerRound - duSession.SwitchCountInRound;
			var multiplier = !duSession.IsNoLoseActive
				? 1
				: duSession.LuckyHitCount <= 1
					? duSession.Options.FirstLuckyMultiplier
					: duSession.Options.RepeatLuckyMultiplier;
			duDto = new DoubleUpStateDto(
				DealerCard: ToCleanRoomDto(duSession.DealerCard),
				CurrentAmount: duSession.CurrentAmount,
				SwitchesRemaining: switchesRemaining,
				IsNoLoseActive: duSession.IsNoLoseActive,
				LuckyMultiplier: multiplier,
				CurrentRoundIndex: duSession.CurrentRoundIndex,
				CardTrail: BuildCardTrail(duSession),
				IsLucky5Active: duSession.IsNoLoseActive,
				BoardHandRank: duSession.BoardHandRank?.ToString(),
				BoardBonusAmount: duSession.LastBoardBonusAmount,
				CurrentBonusAmount: duSession.BoardBonusTotal,
				SlotIndex: duSession.LastResolvedBoardSlotIndex);
		}

		var dto = new ActiveRoundStateDto(
			RoundId: round.RoundId,
			MachineId: machineId,
			BetAmount: round.BetAmount,
			Phase: phase,
			HandRank: round.HandRank,
			Cards: cards,
			ResultCards: resultCards,
			HeldIndexes: heldIndexes,
			PendingWinAmount: round.WinAmount,
			DoubleUpAvailable: round.WinAmount > 0 && !round.IsPayoutSettled,
			TakeHalfUsed: round.TakeHalfUsed,
			DoubleUpSession: duDto);

		stateCache.SetActiveRound(userId, machineId, dto);
		return dto;
	}

	public async Task<object> GetMachineStateAsync(int machineId, CancellationToken cancellationToken, Guid? userId = null)
	{
		var ledger = await RequireMachineLedgerAsync(machineId);
		// Using some simplistic counts since we don't have direct access to all active rounds/sessions easily
		// in EF without a specific query. These properties are mainly for admin debugging.
		var activeRounds = 0; // Would require a specific repository method if really needed
		var activeSessions = 0; // Same here
		long stateVersion = 0;
		long sequenceNumber = 0;
		if (userId.HasValue)
		{
			var cursor = await store.GetOrInitializeCabinetStateCursorAsync(userId.Value, machineId);
			stateVersion = cursor.StateVersion;
			sequenceNumber = cursor.SequenceNumber;
		}
		var result = new Dictionary<string, object>
		{
			["machineId"] = machineId,
			["activeRounds"] = activeRounds,
			["activeSessions"] = activeSessions,
			["observedRtp"] = ledger.ObservedRtp,
			["targetRtp"] = ledger.TargetRtp,
			["baseRtp"] = ledger.CapitalIn > 0 ? Math.Round(ledger.BaseCapitalOut / ledger.CapitalIn, 4) : 0m,
			["phase"] = ledger.LastDistributionMode.ToString(),
			["lastPayoutScale"] = ledger.LastPayoutScale,
			["consecutiveLosses"] = ledger.ConsecutiveLosses,
			["roundsSinceMediumWin"] = ledger.RoundsSinceMediumWin,
			["cooldownRemaining"] = ledger.CooldownRoundsRemaining,
			["netSinceLastClose"] = ledger.NetSinceLastClose,
			["roundsSinceLucky5Hit"] = ledger.RoundsSinceLucky5Hit,
			["jackpots"] = new
			{
				fullHouse = ledger.JackpotFullHouse,
				fullHouseRank = ledger.JackpotFullHouseRank,
				fourOfAKindA = ledger.JackpotFourOfAKindA,
				fourOfAKindB = ledger.JackpotFourOfAKindB,
				activeFourOfAKindSlot = ledger.ActiveFourOfAKindSlot,
				straightFlush = ledger.JackpotStraightFlush,
				machineSerial = ledger.MachineSerial,
				machineSerie = ledger.MachineSerie,
				machineKent = ledger.MachineKent
			},
			["timestampUtc"] = DateTime.UtcNow,
			["state_version"] = stateVersion,
			["sequence_number"] = sequenceNumber
		};
		return result;
	}

	public async Task<(long StateVersion, long SequenceNumber)> GetCabinetStateCursorAsync(Guid userId, int machineId, CancellationToken cancellationToken)
	{
		var cursor = await store.GetOrInitializeCabinetStateCursorAsync(userId, machineId);
		return (cursor.StateVersion, cursor.SequenceNumber);
	}

	public Task<object> ResetMachineAsync(Guid userId, int machineId, CancellationToken cancellationToken)
		=> WithSessionGateAsync(userId, machineId, cancellationToken,
			() => ResetMachineCoreAsync(userId, machineId, cancellationToken));

	private async Task<object> ResetMachineCoreAsync(Guid userId, int machineId, CancellationToken cancellationToken)
	{
		var profile = await RequireProfileAsync(userId);
		await RequireMachineAsync(machineId);

		if (await HasRecoverableRoundAsync(userId, machineId))
			throw new InvalidOperationException("Cannot reset machine while an active round still exists");

		var session = await store.GetMachineSessionAsync(userId, machineId);
		if (session is null)
		{
			InvalidateCaches(userId, machineId);
			return new { success = true, message = "Machine session reset", walletBalance = profile.WalletBalance };
		}

		if (session.MachineCredits > 0m)
		{
			throw new InvalidOperationException("Cash out machine credits before resetting the machine");
		}

		await store.DeleteMachineSessionAsync(session.SessionId);
		InvalidateCaches(userId, machineId);

		return new { success = true, message = "Machine session reset", walletBalance = profile.WalletBalance };
	}

	private async Task FinalizeDoubleUpAsync(GameRound round, MachineSessionState session, int cashoutCredits)
	{
		if (round.IsPayoutSettled)
		{
			return;
		}

		var machine = await RequireMachineAsync(round.MachineId);
		session.MachineCredits += cashoutCredits;
		session.LastUpdatedUtc = DateTime.UtcNow;
		session.IsMachineClosed = session.MachineCredits >= machine.CloseThreshold;
		round.IsPayoutSettled = true;
		round.SettledAmount += cashoutCredits;
		var ledgerDelta = round.SettledAmount - round.OriginalWinAmount;

		var ledger = await RequireMachineLedgerAsync(round.MachineId);
		if (ledgerDelta != 0)
		{
			ledger.CapitalOut += ledgerDelta;
			ledger.DoubleUpCapitalOut += ledgerDelta;
		}
		if (cashoutCredits <= 0)
		{
			ledger.LastWinChannel = WinChannel.None;
		}
		else if (cashoutCredits > round.OriginalWinAmount)
		{
			ledger.LastWinChannel = WinChannel.DoubleUp;
		}
		else if (round.JackpotWinAmount > 0)
		{
			ledger.LastWinChannel = WinChannel.Jackpot;
		}
		else
		{
			ledger.LastWinChannel = WinChannel.BaseGame;
		}
		ledger.NetSinceLastClose = Math.Max(ledger.CapitalIn - ledger.CapitalOut, 0m);

		await store.UpdateMachineLedgerAsync(ledger);
		await store.UpdateMachineSessionAsync(session);
		await store.SaveRoundAsync(round);

		if (cashoutCredits > 0)
		{
			var profile = await RequireProfileAsync(round.UserId);
			profile.TotalWins++;
			await store.UpdateProfileAsync(profile);
		}

		await store.AddWalletLedgerEntryAsync(new WalletLedgerEntry
		{
			UserId = round.UserId,
			Amount = cashoutCredits,
			BalanceAfter = session.MachineCredits,
			TransactionType = cashoutCredits > 0 ? "DoubleUpCashout" : "DoubleUpLoss",
			ReferenceId = round.RoundId.ToString("N"),
			CreatedUtc = DateTime.UtcNow
		});
	}

	private static PresentationNoiseDto GenerateNoise(ulong seed, int roundIndex)
	{
		var plan = PresentationNoiseGenerator.Build(seed, roundIndex);
		return new PresentationNoiseDto(plan.SuspenseMs, plan.RevealMs, plan.FlipFrames, plan.PulseFrames);
	}

	private static JackpotInfoDto SnapshotJackpots(MachineLedgerState ledger) =>
		new(
			ledger.JackpotFullHouse,
			ledger.JackpotFullHouseRank,
			ledger.JackpotFourOfAKindA,
			ledger.JackpotFourOfAKindB,
			ledger.ActiveFourOfAKindSlot,
			ledger.JackpotStraightFlush,
			ledger.MachineSerial,
			ledger.MachineSerie,
			ledger.MachineKent,
			ledger.JackpotKent,
			ledger.KentStreak);

	private static CabinetSnapshotDto BuildCabinetSnapshot(
		Guid userId,
		Machine machine,
		MemberProfile profile,
		MachineSessionState session,
		MachineLedgerState ledger,
		ActiveRoundStateDto? activeRound,
		CabinetStateCursor cursor,
		DateTime serverTimeUtc,
		bool requiresFullSnapshot,
		string recoveryReason)
	{
		var gameState = BuildCabinetGameState(activeRound, session);
		var heldIndexes = activeRound?.HeldIndexes ?? [];
		var advisedHolds = BuildCabinetAdvisedHolds(gameState, activeRound);
		var heldSet = heldIndexes.ToHashSet();
		var handCards = (activeRound?.Cards ?? [])
			.Select((card, index) => ToCabinetCard(card, faceUp: true, held: heldSet.Contains(index)))
			.ToArray();
		var resultCards = (activeRound?.ResultCards ?? activeRound?.Cards ?? [])
			.Select((card, index) => ToCabinetCard(card, faceUp: true, held: heldSet.Contains(index)))
			.ToArray();
		var pendingWin = activeRound?.PendingWinAmount ?? 0m;
		var roundBet = activeRound?.BetAmount ?? 0m;
		var jackpot = SnapshotJackpots(ledger);
		var message = BuildCabinetMessage(gameState, pendingWin, session);
		var doubleUpSession = activeRound?.DoubleUpSession;
		var bonusPresentation = BuildCabinetBonusPresentation(gameState, activeRound, doubleUpSession, pendingWin);

		return new CabinetSnapshotDto(
			SchemaVersion: CabinetSchemaVersion,
			StateVersion: cursor.StateVersion,
			SequenceNumber: cursor.SequenceNumber,
			ServerTimeUtc: serverTimeUtc,
			Session: new CabinetSessionStateDto(
				SessionId: session.SessionId,
				AuthenticatedUserId: userId.ToString("D"),
				MachineId: machine.Id,
				IsMachineClosed: session.IsMachineClosed,
				CanCashOut: CanCashOut(session) && activeRound is null,
				IsArmed: activeRound is null && session.MachineCredits > 0m && roundBet > 0m,
				Visibility: "foreground",
				StartedAtUtc: session.CreatedUtc,
				LastSeenUtc: serverTimeUtc),
			Machine: new CabinetMachineStateDto(
				MachineId: machine.Id,
				Name: machine.Name,
				IsOpen: machine.IsOpen,
				MinBet: ToDecimalString(machine.MinBet),
				MaxBet: ToDecimalString(machine.MaxBet),
				MachineSerial: FirstNonEmpty(jackpot.MachineSerial, machine.MachineSerial),
				MachineSerie: FirstNonEmpty(jackpot.MachineSerie, machine.MachineSerie),
				MachineKent: FirstNonEmpty(jackpot.MachineKent, machine.VariantState.Contains("MachineKent") ? System.Text.Json.JsonDocument.Parse(machine.VariantState).RootElement.GetProperty("MachineKent").GetString() ?? string.Empty : string.Empty),
				FirstRechargeCredit: ToDecimalString(machine.FirstRechargeCredit),
				SecondRechargeCredit: ToDecimalString(machine.SecondRechargeCredit),
				FirstRechargeBonus: ToDecimalString(machine.FirstRechargeBonus),
				SecondRechargeBonus: ToDecimalString(machine.SecondRechargeBonus),
				Paytable: new Dictionary<string, decimal>(Rules)),
			Variant: new CabinetVariantRefDto(
				VariantId: CabinetVariantId,
				VariantSchemaVersion: CabinetVariantSchemaVersion,
				PaytableHash: CabinetPaytableHash,
				DisplayName: "Lucky5 Classic",
				CabinetSkinId: "lebanese_retro_v1",
				PresentationProfileId: "retro_cabinet_v1"),
			GameState: gameState,
			Credits: new CabinetCreditsDto(
				MachineCredits: ToDecimalString(session.MachineCredits),
				WalletBalance: ToDecimalString(profile.WalletBalance),
				CreditBalance: ToDecimalString(profile.Credit),
				Stake: ToDecimalString(roundBet > 0m ? roundBet : machine.MinBet),
				TotalCashIn: ToDecimalString(session.TotalCashIn),
				CashOutThreshold: ToDecimalString(session.TotalCashIn * 2m),
				PendingWinAmount: ToDecimalString(pendingWin)),
			Hand: new CabinetHandDto(
				Cards: handCards,
				ResultCards: resultCards,
				HeldIndexes: heldIndexes,
				RoundId: activeRound?.RoundId,
				AdvisedHolds: advisedHolds),
			Evaluation: new CabinetEvaluationDto(
				HandRank: NormalizeCabinetHandRank(activeRound?.HandRank),
				WinAmount: ToDecimalString(pendingWin),
				JackpotWon: "0",
				DoubleUpAvailable: activeRound?.DoubleUpAvailable ?? false,
				Message: message),
			DoubleUp: new CabinetDoubleUpDto(
				Active: gameState == "double_up",
				CurrentAmount: ToDecimalString(doubleUpSession?.CurrentAmount ?? pendingWin),
				SwitchesRemaining: doubleUpSession?.SwitchesRemaining ?? 0,
				IsNoLoseActive: doubleUpSession?.IsNoLoseActive ?? false,
				IsLucky5Active: doubleUpSession?.IsLucky5Active ?? false,
				CurrentRoundIndex: doubleUpSession?.CurrentRoundIndex ?? 0,
				Status: BuildCabinetDoubleUpStatus(activeRound),
				RoundId: gameState == "double_up" ? activeRound?.RoundId : null,
				DealerCard: doubleUpSession?.DealerCard is null ? null : ToCabinetCard(doubleUpSession.DealerCard, faceUp: true, held: false),
				ChallengerCard: null,
				CardTrail: doubleUpSession?.CardTrail?.Select(card => ToCabinetCard(card, faceUp: true, held: false)).ToArray(),
				LuckyMultiplier: Math.Max(1, doubleUpSession?.LuckyMultiplier ?? 1),
				BoardHandRank: doubleUpSession?.BoardHandRank,
				BoardBonusAmount: ToDecimalString(doubleUpSession?.BoardBonusAmount ?? 0m),
				CurrentBonusAmount: ToDecimalString(doubleUpSession?.CurrentBonusAmount ?? 0m),
				SlotIndex: doubleUpSession?.SlotIndex ?? 0),
			Jackpot: new CabinetJackpotDto(
				FullHouse: ToDecimalString(jackpot.FullHouse),
				FullHouseRank: jackpot.FullHouseRank,
				FourOfAKindA: ToDecimalString(jackpot.FourOfAKindA),
				FourOfAKindB: ToDecimalString(jackpot.FourOfAKindB),
				ActiveFourOfAKindSlot: jackpot.ActiveFourOfAKindSlot == 0 ? "A" : "B",
				StraightFlush: ToDecimalString(jackpot.StraightFlush)),
			Buttons: BuildCabinetButtons(gameState, handCards.Length, session, activeRound, requiresFullSnapshot),
			Presentation: new CabinetPresentationStateDto(
				LayoutProfile: "portrait_720x1280",
				SkinId: "lebanese_retro_v1",
				Message: message,
				MessageTone: BuildCabinetMessageTone(gameState, pendingWin, requiresFullSnapshot),
				PacingProfile: "classic_arcade",
				Effects: BuildCabinetEffects(gameState, pendingWin, requiresFullSnapshot),
				Bonus: bonusPresentation),
			Recovery: new CabinetRecoveryStateDto(
				Connected: true,
				CommandsAllowed: !requiresFullSnapshot,
				RequiresFullSnapshot: requiresFullSnapshot,
				LastAppliedStateVersion: cursor.StateVersion,
				LastAppliedSequenceNumber: cursor.SequenceNumber,
				Reason: recoveryReason));
	}

	private static CabinetCardDto ToCabinetCard(PokerCardDto card, bool faceUp, bool held)
	{
		var rank = NormalizeCardRank(card.Rank, card.Code);
		var suit = NormalizeCardSuit(card.Suit, card.Code);
		var code = $"{rank}{suit}";
		return new CabinetCardDto(code, rank, suit, faceUp, held, $"cards/{code}");
	}

	private static CabinetBonusPresentationDto BuildCabinetBonusPresentation(
		string gameState,
		ActiveRoundStateDto? activeRound,
		DoubleUpStateDto? doubleUpSession,
		decimal pendingWin)
	{
		if (gameState == "double_up" && doubleUpSession?.IsLucky5Active == true)
		{
			return new CabinetBonusPresentationDto(
				Active: true,
				Kind: "lucky5",
				Card: TryBuildCabinetCardFromCode("5S"),
				Amount: ToDecimalString(doubleUpSession.CurrentAmount),
				FreeGameCount: 0,
				Message: "5 NEVER LOSE");
		}

		var handRank = NormalizeCabinetHandRank(activeRound?.HandRank);
		if (handRank == "FourOfAKind")
		{
			return new CabinetBonusPresentationDto(
				Active: true,
				Kind: "bonus_card",
				Card: FindRepeatedRankCabinetCard(activeRound?.ResultCards, 4),
				Amount: ToDecimalString(pendingWin),
				FreeGameCount: 0,
				Message: "4 OF A KIND BONUS");
		}

		return new CabinetBonusPresentationDto(
			Active: false,
			Kind: "free_games",
			Card: null,
			Amount: "0",
			FreeGameCount: 0,
			Message: "FREE GAMES BONUS");
	}

	private static CabinetCardDto? FindRepeatedRankCabinetCard(IReadOnlyList<PokerCardDto>? cards, int minimumCount)
	{
		if (cards is null || cards.Count == 0)
		{
			return null;
		}

		var repeated = cards
			.Select(card => new { Card = card, Rank = NormalizeCardRank(card.Rank, card.Code) })
			.GroupBy(item => item.Rank, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault(group => group.Count() >= minimumCount);

		return repeated is null
			? null
			: ToCabinetCard(repeated.First().Card, faceUp: true, held: false);
	}

	private static CabinetCardDto? TryBuildCabinetCardFromCode(string code)
	{
		try
		{
			return ToCabinetCard(ToCleanRoomDto(CleanRoomCard.FromCode(code)), faceUp: true, held: false);
		}
		catch
		{
			return null;
		}
	}

	private static string BuildCabinetGameState(ActiveRoundStateDto? activeRound, MachineSessionState session)
		=> activeRound?.Phase switch
		{
			"Dealt" => "hold",
			"Drawn" when activeRound.PendingWinAmount > 0m => "win",
			"Drawn" => "drawn",
			"DoubleUp" => "double_up",
			_ when session.IsMachineClosed => "closed",
			_ => "idle"
		};

	private static IReadOnlyList<int>? BuildCabinetAdvisedHolds(string gameState, ActiveRoundStateDto? activeRound)
	{
		if (gameState != "hold" || activeRound is null || activeRound.Cards.Count != 5)
		{
			return null;
		}

		try
		{
			var cards = activeRound.Cards.Select(ToCleanRoomCard).ToArray();
			return FiveCardDrawEngine.ComputeAdvisedHolds(cards);
		}
		catch
		{
			return [];
		}
	}

	private static CleanRoomCard ToCleanRoomCard(PokerCardDto card)
	{
		var code = !string.IsNullOrWhiteSpace(card.Code)
			? card.Code!
			: $"{NormalizeCardRank(card.Rank, card.Code)}{NormalizeCardSuit(card.Suit, card.Code)}";
		return CleanRoomCard.FromCode(code);
	}

	private static string BuildCabinetDoubleUpStatus(ActiveRoundStateDto? activeRound)
		=> activeRound?.Phase == "DoubleUp" ? "started" : "none";

	private static string NormalizeCabinetHandRank(string? handRank)
	{
		if (string.IsNullOrWhiteSpace(handRank))
		{
			return "None";
		}

		return handRank.Trim().Replace(" ", string.Empty, StringComparison.Ordinal) switch
		{
			"Nothing" or "NoWin" or "HighCard" or "OnePair" => "None",
			"TwoPair" => "TwoPair",
			"ThreeOfAKind" => "ThreeOfAKind",
			"Straight" => "Straight",
			"Flush" => "Flush",
			"FullHouse" => "FullHouse",
			"FourOfAKind" => "FourOfAKind",
			"StraightFlush" => "StraightFlush",
			"RoyalFlush" => "RoyalFlush",
			_ => "None"
		};
	}

	private static IReadOnlyList<CabinetButtonStateDto> BuildCabinetButtons(string gameState, int cardCount, MachineSessionState session, ActiveRoundStateDto? activeRound, bool recoveryRequired)
	{
		var enabled = BuildCabinetEnabledButtonSet(gameState, cardCount, session, activeRound, recoveryRequired);
		string[] buttonIds =
		[
			"menu", "bet", "deal_draw", "cancel_hold", "hold_0", "hold_1", "hold_2", "hold_3", "hold_4",
			"big", "small", "double_up_switch", "take_half", "take_score", "cash_in", "cash_out", "reset_machine", "back_to_lobby", "logout"
		];

		return buttonIds
			.Select(id => new CabinetButtonStateDto(id, enabled.Contains(id), true, false, enabled.Contains(id) ? string.Empty : BuildButtonDisabledReason(id, gameState, recoveryRequired)))
			.ToArray();
	}

	private static HashSet<string> BuildCabinetEnabledButtonSet(string gameState, int cardCount, MachineSessionState session, ActiveRoundStateDto? activeRound, bool recoveryRequired)
	{
		var buttons = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "menu", "back_to_lobby", "logout" };
		if (recoveryRequired)
		{
			return buttons;
		}

		if (gameState == "hold")
		{
			for (var index = 0; index < Math.Min(5, cardCount); index++)
			{
				buttons.Add($"hold_{index}");
			}

			buttons.Add("cancel_hold");
			buttons.Add("deal_draw");
		}
		else if (gameState is "win" or "double_up")
		{
			var doubleUpControlsAvailable = gameState == "double_up" || (activeRound?.PendingWinAmount ?? 0m) > 0m;
			if (doubleUpControlsAvailable)
			{
				buttons.Add("big");
				buttons.Add("small");
				if (CanSwitchDoubleUpDealer(gameState, activeRound))
				{
					buttons.Add("double_up_switch");
					buttons.Add("bet");
				}
			}

			buttons.Add("take_score");
			if (doubleUpControlsAvailable && (activeRound?.PendingWinAmount ?? 0m) > 1m && activeRound?.TakeHalfUsed != true)
			{
				buttons.Add("take_half");
			}
		}
		else if (gameState == "idle")
		{
			buttons.Add("bet");
			buttons.Add("cash_in");
			if (!session.IsMachineClosed && session.MachineCredits > 0m)
			{
				buttons.Add("deal_draw");
				// The Full House jackpot rank can only be adjusted via HOLD[0] 
				// AFTER a bet has been placed (armed state). In the backend snapshot,
				// we enable it if the session has credits, but the frontend
				// provides the final 'armed' gate.
				buttons.Add("hold_0");
			}
		}
		else if (gameState == "closed")
		{
			buttons.Add("take_score");
		}

		if (CanCashOut(session) && activeRound is null)
		{
			buttons.Add("cash_out");
			buttons.Add("take_score");
		}

		return buttons;
	}

	private static bool CanSwitchDoubleUpDealer(string gameState, ActiveRoundStateDto? activeRound)
		=> gameState == "double_up" && (activeRound?.DoubleUpSession?.SwitchesRemaining ?? 0) > 0;

	private static string BuildButtonDisabledReason(string buttonId, string gameState, bool recoveryRequired)
	{
		if (recoveryRequired && buttonId is not ("menu" or "back_to_lobby" or "logout"))
		{
			return "recovery_required";
		}

		return $"disabled_in_{gameState}";
	}

	private static string BuildCabinetMessageTone(string gameState, decimal pendingWin, bool recoveryRequired)
	{
		if (recoveryRequired)
		{
			return "recovery";
		}

		if (pendingWin > 0m || gameState is "win" or "double_up")
		{
			return "win";
		}

		return gameState == "closed" ? "warning" : "normal";
	}

	private static IReadOnlyList<string> BuildCabinetEffects(string gameState, decimal pendingWin, bool recoveryRequired)
	{
		var effects = new List<string>();
		if (recoveryRequired) effects.Add("recovery_overlay");
		if (pendingWin > 0m) effects.Add("win_lamps");
		if (gameState == "closed") effects.Add("machine_closed");
		return effects;
	}

	private static IReadOnlyDictionary<string, object?> BuildCabinetEventPayload(string commandType, CabinetSnapshotDto snapshot)
		=> new Dictionary<string, object?>
		{
			["command_type"] = NormalizeCabinetCommandType(commandType),
			["state_version"] = snapshot.StateVersion,
			["sequence_number"] = snapshot.SequenceNumber,
			["snapshot"] = snapshot
		};

	private static string ResolveCabinetEventType(string commandType)
		=> NormalizeCabinetCommandType(commandType) switch
		{
			"cash_in" or "cash_out" => "credits_updated",
			"deal" or "draw" or "take_score" => "round_updated",
			"double_up_start" or "double_up_guess" or "double_up_switch" or "swap_double_up_card" or "take_half" => "double_up_updated",
			"jackpot_rank_change" => "jackpot_updated",
			"join_machine" or "leave_machine" => "session_visibility_changed",
			"heartbeat" => "heartbeat_ack",
			"reconnect_sync" => "recovery_required",
			_ => "state_changed"
		};

	private static string ToDecimalString(decimal value)
		=> value.ToString("0.################", CultureInfo.InvariantCulture);

	private static string FirstNonEmpty(string primary, string fallback)
		=> string.IsNullOrWhiteSpace(primary) ? fallback : primary;

	private static string NormalizeCardRank(string rank, string? code)
	{
		var candidate = string.IsNullOrWhiteSpace(rank) && !string.IsNullOrWhiteSpace(code)
			? code![..^1]
			: rank;
		candidate = candidate.Trim().ToUpperInvariant();
		return candidate == "T" ? "10" : candidate;
	}

	private static string NormalizeCardSuit(string suit, string? code)
	{
		if (!string.IsNullOrWhiteSpace(code))
		{
			var fromCode = char.ToUpperInvariant(code![^1]);
			if ("HDCS".Contains(fromCode, StringComparison.Ordinal))
			{
				return fromCode.ToString();
			}
		}

		return suit.Trim().ToUpperInvariant() switch
		{
			"HEARTS" or "H" => "H",
			"DIAMONDS" or "D" => "D",
			"CLUBS" or "C" => "C",
			"SPADES" or "S" => "S",
			_ => "S"
		};
	}

	/// <summary>
	/// Applies per-round jackpot contributions. Only the currently-starred
	/// Four-of-a-Kind jackpot (slot 0 -> A, slot 1 -> B) accrues this round;
	/// the other side is frozen. Full House and Straight Flush jackpots
	/// always accrue because they have no A/B split.
	/// </summary>
	private static void ApplyJackpotContributions(MachineLedgerState ledger, EngineConfig cfg, int activeFourOfAKindSlot)
	{
		// 4 OF A KIND: Only the active slot (marked with red * on the cabinet) increases.
		// The two slots (A and B) alternate randomly each round.
		// Fixed increment of 500 per round, capped at 99,999.
		if (activeFourOfAKindSlot == 0)
		{
			ledger.JackpotFourOfAKindA = Math.Min(ledger.JackpotFourOfAKindA + cfg.JackpotFourOfAKindContribution, cfg.JackpotFourOfAKindCap);
		}
		else
		{
			ledger.JackpotFourOfAKindB = Math.Min(ledger.JackpotFourOfAKindB + cfg.JackpotFourOfAKindContribution, cfg.JackpotFourOfAKindCap);
		}

		// FULL HOUSE: Fixed increment of 300 per round.
		// Cap scales with the armed rank (Aces = 20M, Deuces = ~1.4M).
		var fhCap = cfg.GetFullHouseCapForRank(ledger.JackpotFullHouseRank);
		ledger.JackpotFullHouse = Math.Min(ledger.JackpotFullHouse + cfg.JackpotFullHouseContribution, fhCap);

		// STRAIGHT FLUSH: Fixed increment of 800 per round. Capped at 9,999,999.
		ledger.JackpotStraightFlush = Math.Min(ledger.JackpotStraightFlush + cfg.JackpotStraightFlushContribution, cfg.JackpotStraightFlushCap);

		// KENT: Fixed increment of 200 per round. Capped at 5,000,000.
		ledger.JackpotKent = Math.Min(ledger.JackpotKent + cfg.JackpotKentContribution, cfg.JackpotKentCap);
	}

	private static IReadOnlyList<PokerCardDto> BuildCardTrail(Lucky5DoubleUpSession session)
	{
		if (session.CurrentBoardCards is { Length: > 0 })
		{
			return session.CurrentBoardCards.Select(ToCleanRoomDto).ToArray();
		}
		return [ToCleanRoomDto(session.DealerCard)];
	}

	private static CleanRoomCard ConvertToCleanRoomCard(PokerCard c)
	{
		var rankInt = c.Rank switch
		{
			"A" => 14, "K" => 13, "Q" => 12, "J" => 11, "10" => 10,
			"9" => 9, "8" => 8, "7" => 7, "6" => 6, "5" => 5,
			"4" => 4, "3" => 3, "2" => 2,
			_ => throw new ArgumentException($"Unknown rank: {c.Rank}")
		};
		return new CleanRoomCard(rankInt, c.Suit[0]);
	}

	private static PokerCardDto ToDto(PokerCard c)
	{
		var cardId = ComputeCardId(c.Rank, c.Suit);
		var title = $"{c.Rank[0]}{c.Suit[0]}";
		return new PokerCardDto(cardId, title, c.Suit, c.Rank, c.Code);
	}

	private static PokerCardDto ToCleanRoomDto(CleanRoomCard c)
	{
		var rank = CleanRoomCard.GetLegacyRank(c.Rank);
		var suit = c.Suit.ToString();
		var cardId = ComputeCardId(rank, suit);
		var title = $"{rank[0]}{suit[0]}";
		return new PokerCardDto(cardId, title, suit, rank, $"{rank}{suit}");
	}

	private static int ComputeCardId(string rank, string suit)
	{
		// Map rank to 0-12 (2=0, ..., A=12)
		var rankIndex = rank switch
		{
			"2" => 0,
			"3" => 1,
			"4" => 2,
			"5" => 3,
			"6" => 4,
			"7" => 5,
			"8" => 6,
			"9" => 7,
			"10" => 8,
			"J" => 9,
			"Q" => 10,
			"K" => 11,
			"A" => 12,
			_ => 0
		};

		// Map both canonical names and CleanRoom short suits to base offsets.
		var suitOffset = suit switch
		{
			"H" or "Hearts" => 0,
			"D" or "Diamonds" => 13,
			"C" or "Clubs" => 26,
			"S" or "Spades" => 39,
			_ => 0
		};

		return suitOffset + rankIndex + 1; // CardId is 1-based
	}

	private void InvalidateCaches(Guid userId, int machineId)
	{
		stateCache.InvalidateActiveRound(userId, machineId);
		stateCache.InvalidateMachineSession(userId, machineId);
	}

	private static string MapHandCategory(HandEvaluation eval) => eval.Category switch
	{
		HandCategory.RoyalFlush => "RoyalFlush",
		HandCategory.StraightFlush => "StraightFlush",
		HandCategory.FourOfAKind => "FourOfAKind",
		HandCategory.FullHouse => "FullHouse",
		HandCategory.Flush => "Flush",
		HandCategory.Straight => "Straight",
		HandCategory.ThreeOfAKind => "ThreeOfAKind",
		HandCategory.TwoPair => "TwoPair",
		_ => "Nothing"
	};

	private static string BuildCabinetMessage(string gameState, decimal pendingWin, MachineSessionState session)
	{
		return gameState switch
		{
			"idle" when session.MachineCredits <= 0m => "INSERT COIN",
			"idle" => "PRESS DEAL",
			"dealing" => "SELECT HOLDS",
			"drawn" when pendingWin > 0m => "WIN BONUS",
			"drawn" => "NO WIN",
			"double_up" => "BIG OR SMALL",
			"closed" => "TAKE SCORE",
			_ => "READY"
		};
	}

	private async Task<Machine> RequireMachineAsync(int machineId)
	{
		var machine = await store.GetMachineAsync(machineId);
		if (machine is null || !machine.IsOpen)
			throw new KeyNotFoundException("Machine not found or closed");
		return machine;
	}

	private async Task<MachineLedgerState> RequireMachineLedgerAsync(int machineId)
	{
		return await store.GetOrInitializeMachineLedgerAsync(machineId);
	}

	private async Task<MemberProfile> RequireProfileAsync(Guid userId)
	{
		var profile = await store.GetProfileAsync(userId);
		if (profile is null) throw new KeyNotFoundException("Profile not found");
		return profile;
	}

	private async Task<MachineSessionState> RequireMachineSessionAsync(Guid userId, int machineId, bool createIfMissing)
	{
		var machine = await store.GetMachineAsync(machineId);
		var closeThreshold = machine?.CloseThreshold ?? EngineCfg.CloseThreshold;
		var session = await store.GetMachineSessionAsync(userId, machineId);
		if (session != null)
		{
			if (NormalizeMachineSession(session, closeThreshold))
			{
				await store.UpdateMachineSessionAsync(session);
				InvalidateCaches(userId, machineId);
			}

			return session;
		}

		if (!createIfMissing) throw new KeyNotFoundException("Machine session not found");

		session = new MachineSessionState { UserId = userId, MachineId = machineId };
		await store.CreateMachineSessionAsync(session);
		return session;
	}

	private static bool NormalizeMachineSession(MachineSessionState session, decimal closeThreshold)
	{
		var changed = false;

		if (session.MachineCredits <= 0m)
		{
			if (session.MachineCredits != 0m)
			{
				session.MachineCredits = 0m;
				changed = true;
			}

			if (session.TotalCashIn != 0m)
			{
				session.TotalCashIn = 0m;
				changed = true;
			}

			if (session.IsMachineClosed)
			{
				session.IsMachineClosed = false;
				changed = true;
			}
		}
		else if (!session.IsMachineClosed && session.MachineCredits >= closeThreshold)
		{
			session.IsMachineClosed = true;
			changed = true;
		}

		if (changed)
		{
			session.LastUpdatedUtc = DateTime.UtcNow;
		}

		return changed;
	}



	private static int AssessCounterplay(CleanRoomCard[] hand, int[] holdIndexes)
	{
		var advised = FiveCardDrawEngine.ComputeAdvisedHolds(hand);
		var advisedSet = advised.ToHashSet();
		var actualSet = holdIndexes.Where(i => i >= 0 && i < 5).ToHashSet();
		var evaluation = FiveCardDrawEngine.EvaluateHand(hand);
		var delta = advisedSet.Except(actualSet).Count() + actualSet.Except(advisedSet).Count();

		if (evaluation.Category is HandCategory.FourOfAKind or HandCategory.FullHouse or HandCategory.Flush or HandCategory.Straight)
		{
			if (delta > 0) return 3;
		}
		if (evaluation.Category == HandCategory.ThreeOfAKind && actualSet.Count < 3)
		{
			return 2;
		}
		if (delta >= 4) return 2;
		if (delta >= 2) return 1;
		return -1;
	}

	private static void UpdateCounterplay(MachineSessionState session, int delta)
	{
		session.CounterplayScore = Math.Clamp(session.CounterplayScore + delta, 0, 10);
		session.LastUpdatedUtc = DateTime.UtcNow;
	}

	private async Task<MachineSessionDto> ToMachineSessionDtoAsync(Guid userId, MachineSessionState session, decimal walletBalance)
	{
		var canCashOut = !await HasRecoverableRoundAsync(userId, session.MachineId) && CanCashOut(session);

		// Attach transparency telemetry if available
		MachineTransparencyDto? transparency = null;
		try
		{
			var ledger = await RequireMachineLedgerAsync(session.MachineId);
			var policyState = BuildMachinePolicyState(ledger);
			var policyResolution = MachinePolicy.ResolvePolicy(policyState, 0UL); // Use zero seed for deterministic telemetry

			transparency = new MachineTransparencyDto(
				IsWarmupActive: policyResolution.Telemetry.IsWarmupActive,
				IsPityActive: policyResolution.Telemetry.IsPityActive,
				IsCrisisActive: policyResolution.Telemetry.IsCrisisActive,
				BaseScale: policyResolution.Telemetry.BaseScale,
				WarmupBias: policyResolution.Telemetry.WarmupBias,
				PityBoost: policyResolution.Telemetry.PityBoost,
				JackpotLeakAdjustment: policyResolution.Telemetry.JackpotLeakAdjustment,
				DoubleUpLeakAdjustment: policyResolution.Telemetry.DoubleUpLeakAdjustment,
				EffectiveScale: policyResolution.Telemetry.EffectiveScale,
				EnvelopeMode: policyResolution.Telemetry.EnvelopeMode.ToString(),
				RoundCount: policyResolution.Telemetry.RoundCount,
				ConsecutiveLosses: policyResolution.Telemetry.ConsecutiveLosses,
				RoundsSinceMediumWin: policyResolution.Telemetry.RoundsSinceMediumWin,
				ObservedRtp: policyResolution.Telemetry.ObservedRtp,
				TargetRtp: policyResolution.Telemetry.TargetRtp);
		}
		catch
		{
			// If we can't get transparency data, continue without it
		}

		return ToMachineSessionDto(session, walletBalance, canCashOut, transparency);
	}

	private async Task<bool> HasRecoverableRoundAsync(Guid userId, int machineId)
	{
		var round = await store.GetLatestRoundAsync(userId, machineId);
		return IsRoundRecoverable(round);
	}

	private static bool IsRoundRecoverable(GameRound? round)
	{
		if (round is null)
		{
			return false;
		}

		return !round.IsCompleted || (!round.IsPayoutSettled && round.WinAmount > 0m);
	}

	private static bool CanCashOut(MachineSessionState session)
	{
		if (session.IsMachineClosed)
		{
			return true;
		}

		return session.TotalCashIn > 0m && session.MachineCredits >= session.TotalCashIn * 2m;
	}

	private static bool IsStaleZeroCreditSession(MachineSessionDto session)
		=> session.MachineCredits <= 0m
			&& (session.IsMachineClosed || session.TotalCashIn > 0m || session.CanCashOut || session.CashOutThreshold > 0m);

	private static MachineSessionDto ToMachineSessionDto(MachineSessionState session, decimal walletBalance, bool canCashOut, MachineTransparencyDto? transparency = null)
		=> new(session.SessionId, session.MachineId, session.MachineCredits, session.TotalCashIn, session.TotalCashIn * 2m, canCashOut, session.IsMachineClosed, walletBalance, transparency);

	private static MachinePolicyState BuildMachinePolicyState(MachineLedgerState ledger)
	{
		return new MachinePolicyState
		{
			CreditsIn = ledger.CapitalIn,
			CreditsOut = ledger.CapitalOut,
			BaseCreditsOut = ledger.BaseCapitalOut,
			JackpotCreditsOut = ledger.JackpotCapitalOut,
			DoubleUpCreditsOut = ledger.DoubleUpCapitalOut,
			TargetRtp = ledger.TargetRtp,
			RoundCount = ledger.RoundCount,
			ConsecutiveLosses = ledger.ConsecutiveLosses,
			RoundsSinceMediumWin = ledger.RoundsSinceMediumWin,
			CooldownRoundsRemaining = ledger.CooldownRoundsRemaining,
			NetSinceLastClose = ledger.NetSinceLastClose,
			RoundsSinceLucky5Hit = ledger.RoundsSinceLucky5Hit
		};
	}
}
