namespace Lucky5.Domain.Game.CleanRoom;

using System.Globalization;
using Lucky5.Domain.Entities;

public enum RoundState
{
	Bet = 0,
	Deal5 = 1,
	Hold = 2,
	Draw = 3,
	Evaluate = 4,
	DoubleUp = 5
}

public enum RoundPhase
{
	Dealt = 0,
	Drawn = 1
}

public enum RoundActionKind
{
	ToggleHold = 0,
	SetHoldMask = 1,
	Draw = 2
}

public enum BigSmallGuess
{
	Big = 0,
	Small = 1
}

public enum HandCategory
{
	None = -1,
	HighCard = 0,
	OnePair = 1,
	TwoPair = 2,
	ThreeOfAKind = 3,
	Straight = 4,
	Flush = 5,
	FullHouse = 6,
	FourOfAKind = 7,
	StraightFlush = 8,
	RoyalFlush = 9,
	FiveOfAKind = 10
}

public enum Lucky5DoubleUpOutcome
{
	Win = 0,
	Lose = 1,
	SafeFail = 2,
	MachineClosed = 3
}

public sealed record RoundAction(RoundActionKind Kind, int? CardIndex = null, bool[]? HoldMask = null);

public readonly record struct CleanRoomCard(int Rank, char Suit)
{
	private const string ValidSuits = "CDHS";

	public string Code => $"{GetDisplayRank(Rank)}{Suit}";

	public static CleanRoomCard FromCode(string code)
	{
		if (string.IsNullOrWhiteSpace(code))
		{
			throw new ArgumentException("Card code is required.", nameof(code));
		}

		var normalized = code.Trim().ToUpperInvariant().Replace("10", "T", StringComparison.Ordinal);
		if (normalized.Length != 2)
		{
			throw new ArgumentException($"Invalid card code: {code}", nameof(code));
		}

		var suit = normalized[1];
		if (!ValidSuits.Contains(suit, StringComparison.Ordinal))
		{
			throw new ArgumentException($"Unsupported suit in card code: {code}", nameof(code));
		}

		return new CleanRoomCard(ParseRank(normalized[0]), suit);
	}

	public PokerCard ToLegacyPokerCard() => new(GetLegacyRank(Rank), Suit.ToString());

	public static int ParseRank(char rank) => rank switch
	{
		'2' => 2,
		'3' => 3,
		'4' => 4,
		'5' => 5,
		'6' => 6,
		'7' => 7,
		'8' => 8,
		'9' => 9,
		'T' => 10,
		'J' => 11,
		'Q' => 12,
		'K' => 13,
		'A' => 14,
		_ => throw new ArgumentOutOfRangeException(nameof(rank), rank, "Unsupported rank character.")
	};

	public static string GetDisplayRank(int rank) => rank switch
	{
		>= 2 and <= 9 => rank.ToString(CultureInfo.InvariantCulture),
		10 => "T",
		11 => "J",
		12 => "Q",
		13 => "K",
		14 => "A",
		_ => throw new ArgumentOutOfRangeException(nameof(rank), rank, "Unsupported rank value.")
	};

	public static string GetLegacyRank(int rank) => rank == 10 ? "10" : GetDisplayRank(rank);
}

public sealed record FiveCardDrawState(
	ulong SeedToken,
	CleanRoomCard[] Deck,
	CleanRoomCard[] Hand,
	int DrawIndex,
	bool[] Held,
	RoundPhase Phase,
	RoundState State)
{
	public static FiveCardDrawState Create(ulong seedToken, CleanRoomCard[] deck, CleanRoomCard[] hand)
		=> new(seedToken, deck, hand, 5, [false, false, false, false, false], RoundPhase.Dealt, RoundState.Hold);
}

public sealed record HandEvaluation(
	HandCategory Category,
	string DisplayName,
	int[] Tiebreak,
	int? PairRank = null);

public sealed record PaytableProfile(
	string Name,
	IReadOnlyDictionary<HandCategory, int> Payouts,
	int MinimumPairRankForPayout = 11,
	int MaxCoinBet = 5,
	int RoyalFlushMaxCoinPayout = 4000)
{
	public static PaytableProfile Lebanese { get; } = new(
		"Lebanese",
		new Dictionary<HandCategory, int>
		{
			[HandCategory.RoyalFlush] = 1000,
			[HandCategory.StraightFlush] = 300,
			[HandCategory.FourOfAKind] = 120,
			[HandCategory.FullHouse] = 20,
			[HandCategory.Flush] = 14,
			[HandCategory.Straight] = 10,
			[HandCategory.ThreeOfAKind] = 6,
			[HandCategory.TwoPair] = 4
		},
		int.MaxValue,
		MaxCoinBet: 5,
		RoyalFlushMaxCoinPayout: 5_000_000);

	public static PaytableProfile JacksOrBetter { get; } = new(
		"Jacks or Better",
		new Dictionary<HandCategory, int>
		{
			[HandCategory.RoyalFlush] = 250,
			[HandCategory.StraightFlush] = 50,
			[HandCategory.FourOfAKind] = 25,
			[HandCategory.FullHouse] = 9,
			[HandCategory.Flush] = 6,
			[HandCategory.Straight] = 4,
			[HandCategory.ThreeOfAKind] = 3,
			[HandCategory.TwoPair] = 2,
			[HandCategory.OnePair] = 1
		});

	public static PaytableProfile TwoPairMinimum { get; } = new(
		"Two Pair Minimum",
		new Dictionary<HandCategory, int>
		{
			[HandCategory.RoyalFlush] = 250,
			[HandCategory.StraightFlush] = 50,
			[HandCategory.FourOfAKind] = 25,
			[HandCategory.FullHouse] = 9,
			[HandCategory.Flush] = 6,
			[HandCategory.Straight] = 4,
			[HandCategory.ThreeOfAKind] = 3,
			[HandCategory.TwoPair] = 2
		},
		int.MaxValue);

	public int ResolvePayout(HandEvaluation evaluation, int bet)
	{
		if (bet <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(bet), bet, "Bet must be positive.");
		}

		if (evaluation.Category == HandCategory.OnePair)
		{
			if (evaluation.PairRank is null ||
				evaluation.PairRank < MinimumPairRankForPayout ||
				!Payouts.TryGetValue(HandCategory.OnePair, out var pairMultiplier))
			{
				return 0;
			}

			return pairMultiplier * bet;
		}

		if (!Payouts.TryGetValue(evaluation.Category, out var multiplier))
		{
			return 0;
		}

		if (evaluation.Category == HandCategory.RoyalFlush && bet == MaxCoinBet)
		{
			return RoyalFlushMaxCoinPayout;
		}

		return multiplier * bet;
	}
}

public sealed record Lucky5DoubleUpOptions(
	int MaxSwitchesPerRound = 2,
	int FirstLuckyMultiplier = 4,
	int RepeatLuckyMultiplier = 2,
	int MaxCreditLimit = 40_000_000,
	bool AceCountsHiOrLo = true);

public sealed record Lucky5DoubleUpSession(
	ulong SeedRoot,
	ulong RoundSeedToken,
	CleanRoomCard[] Deck,
	int DealerIndex,
	CleanRoomCard DealerCard,
	int CurrentAmount,
	int MachineCreditBaseline,
	int CurrentRoundIndex,
	int SwitchCountInRound,
	int LuckyHitCount,
	bool IsNoLoseActive,
	Lucky5DoubleUpOptions Options,
	bool IsTerminal = false,
	Lucky5DoubleUpOutcome? TerminalOutcome = null,
	int CashoutCredits = 0,
	int SwapActivePosition = -1,
	int[]? PlayedDealerIndexes = null,
	CleanRoomCard[]? CurrentBoardCards = null,
	bool CurrentBoardComplete = false,
	HandCategory? BoardHandRank = null,
	int LastBoardBonusAmount = 0,
	int BoardBonusTotal = 0,
	int LastResolvedBoardSlotIndex = 1,
	int BetAmount = 0) : IDoubleUpSession;

public sealed record Lucky5DoubleUpResolution(
	BigSmallGuess Guess,
	CleanRoomCard DealerCard,
	CleanRoomCard ChallengerCard,
	Lucky5DoubleUpOutcome Outcome,
	int PreviousAmount,
	int NextAmount,
	int CashoutCredits,
	Lucky5DoubleUpSession Session);

public sealed record PresentationNoisePlan(
	int SuspenseMs,
	int RevealMs,
	int FlipFrames,
	int PulseFrames,
	int DecoySwaps);

/// <summary>
/// Externalized engine configuration for RTP rebalancing.
/// All tuning parameters in one place — see plans/Upgrade 3/LUCKY5_80_RTP_TECHNICAL_IMPLEMENTATION_UPDATE.md.
/// v8 (2026-04-19) calibration pass: rules unchanged, only controller knobs retuned so composite RTP
/// converges to ~80% under optimal player strategy. Key changes vs v7:
///   - TargetDoubleUpRtp raised 0.0950 -> 0.1200 (reflects Ace-auto-win reality under optimal BIG/SMALL play).
///   - DoubleUpRtpHardCap raised 0.110 -> 0.130 in sync so the leak clamp engages on sustained overshoot
///     rather than every round.
///   - Warmup opening scales lowered (1.65/1.70/1.75 -> 1.55/1.58/1.60) to trim fresh-session over-pay.
///   - DefaultPayoutScale lowered 1.75 -> 1.15, MinPayoutScale lowered 1.18 -> 0.72 so the correction
///     loop has enough downward headroom after Ace, jackpot, and always-on double-up overlays are modeled faithfully.
///   - CrisisScaleBoost trimmed 0.07 -> 0.05 to avoid pity-driven overshoot during long loss streaks.
///   - Double-up pressure can remove bounded key cards and sequence high-pressure/high-exposure chains,
///     with a small deterministic release rate for suspense and close calls.
/// </summary>
public sealed record EngineConfig(
	// === Payout Scale ===
	decimal TargetRtp = 0.80m,
	decimal TargetDoubleUpRtp = 0.1200m,
	decimal MinimumObservedBaseRtp = 0.3800m,
	decimal DefaultPayoutScale = 1.15m,
	decimal MinPayoutScale = 0.72m,
	decimal MaxPayoutScale = 2.05m,
	int WarmupRounds = 60,
	int ConvergenceHorizon = 320,
	decimal CorrectionGain = 1.00m,
	decimal MaxCorrection = 0.28m,
	decimal DeadZone = 0.0125m,
	int RtpSmoothingWindow = 280,
	int RtpMinSamplesForControl = 30,
	decimal MaxDriftClamp = 0.150m,
	decimal JitterAmplitude = 0.020m,
	decimal SmallTierFactor = 1.00m,
	decimal MediumTierFactor = 1.04m,
	decimal BigTierFactor = 1.08m,
	decimal WarmupOpeningSmallScale = 1.15m,
	decimal WarmupOpeningMediumScale = 1.18m,
	decimal WarmupOpeningBigScale = 1.20m,

	// === Envelope & Orbit Clamp ===
	decimal EnvelopeScaleClamp = 0.18m,
	decimal RollingMeanScaleAlpha = 0.05m,
	decimal HouseEdgeBufferCap = 0.06m,
	decimal JackpotRtpSoftCap = 0.030m,
	decimal JackpotLeakDamp = 0.40m,
	decimal DoubleUpRtpHardCap = 0.130m,
	decimal PityBoostCap = 0.14m,

	// === Double-Up Deck Pressure ===
	int DoubleUpPressureMinRounds = 12,
	decimal DoubleUpPressureSoftDrift = 0.020m,
	int DoubleUpPressureMaxKeyRemovals = 29,
	int DoubleUpPressureRecoveryDroughtRounds = 28,
	int DoubleUpMinDeckSize = 23,
	decimal DoubleUpCloseCallPressureStart = 0.70m,
	decimal DoubleUpSequencePressureStart = 0.72m,
	decimal DoubleUpSequenceCreditStart = 0.60m,
	decimal DoubleUpHighExposureSequencePressureStart = 0.22m,
	decimal DoubleUpSuspenseReleaseChance = 0.12m,

	// === Deck Alteration Bounds ===
	int MaxColdRemovals = 1,
	int MaxHotAdditions = 2,
	bool NeverRemoveFiveOfSpades = true,
	int MinDeckSize = 51,

	// === Streaks & Pity ===
	int StreakSoftThreshold = 4,
	int StreakHardThreshold = 8,
	int CrisisThreshold = 12,
	decimal CrisisScaleBoost = 0.05m,
	int MediumWinDroughtThreshold = 15,
	int CooldownLength = 2,

	// === Soft Caps ===
	decimal SoftCapWarning = 28_000_000m,
	decimal SoftCapHard = 35_000_000m,
	decimal CloseThreshold = 40_000_000m,

	// === Jackpots (Fixed Increment Mode) ===
	// Jackpots increase by fixed increments each round (not % of bet).
	// This gives predictable, non-chaotic growth — like a real mechanical counter ticking up.
	//
	// 4 OF A KIND: Two separate jackpots (A and B), one per side of the cabinet bottom.
	//   Only the active slot (marked with red * on the cabinet) increases each round.
	//   They alternate randomly each round. Max: 99,999. Start: 20,000.
	//
	// STRAIGHT FLUSH: Single jackpot. Max: 10,000,000. Start: 850,000.
	//   Higher cap because SF is rarer and has more DU potential.
	//
	// FULL HOUSE: Single jackpot, rank-armed. Max scales with rank:
	//   Ace (rank 14) = 20,000,000 cap. Lower ranks = proportionally lower cap.
	//   Start: 90,000 regardless of rank.
	//
	// KENT (Sequential Straight): A straight where all 5 cards are in sequential order
	//   (e.g., 6-7-8-9-10). Max: 5,000,000. Start: 500,000.
	//   This is a separate jackpot from the base STRAIGHT payout. A player can win
	//   both the base straight payout AND the Kent jackpot on the same hand.
	//   Must hit this jackpot 3 times total to fully "clear" the Kent pool.
	//
	// ROYAL FLUSH: No jackpot. Pays base paytable only (1000x bet).
	//   The base payout is already massive — no jackpot needed.
	//
	// Contribution increments are fixed amounts per round (not percentages):
	//   4OAK: +500 per round (active slot only)
	//   SF:   +800 per round
	//   FH:   +300 per round
	//   Kent: +200 per round
	decimal JackpotFourOfAKindCap = 99_999m,
	decimal JackpotFullHouseRank14Cap = 20_000_000m,
	decimal JackpotStraightFlushCap = 10_000_000m,
	decimal JackpotKentCap = 5_000_000m,
	int JackpotFourOfAKindContribution = 500,
	int JackpotFullHouseContribution = 300,
	int JackpotStraightFlushContribution = 800,
	int JackpotKentContribution = 200,
	decimal JackpotFourOfAKindStart = 20_000m,
	decimal JackpotFullHouseStart = 90_000m,
	decimal JackpotStraightFlushStart = 850_000m,
	decimal JackpotKentStart = 500_000m
)
{
	public static EngineConfig Default { get; } = new();

	// Computed properties for convenience
	public decimal TargetJackpotRtp => 0.0325m;
	public decimal TargetScaledBaseRtp => TargetRtp - TargetJackpotRtp - TargetDoubleUpRtp;
	public decimal BoundedHouseEdgeBuffer => Math.Min(1m - TargetRtp, HouseEdgeBufferCap);

	/// <summary>
	/// Full House jackpot cap scales with the armed rank.
	/// Ace (rank 14) = full cap. Lower ranks = proportionally lower.
	/// </summary>
	public decimal GetFullHouseCapForRank(int rank)
	{
		var normalizedRank = Math.Clamp(rank, 2, 14);
		return Math.Round(JackpotFullHouseRank14Cap * normalizedRank / 14m, 0);
	}

	/// <summary>
	/// Kent jackpot cap is fixed — does not scale with rank.
	/// The Kent jackpot pays when the player gets a sequential straight
	/// (5 cards in order, e.g., 6-7-8-9-10). Must be hit 3 times to clear the pool.
	/// </summary>
	public decimal GetKentCap() => JackpotKentCap;
}
