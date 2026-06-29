namespace Lucky5.Tests;

using Lucky5.Domain.Entities;
using Lucky5.Domain.Game;
using Lucky5.Domain.Game.CleanRoom;

public static class CleanRoomEngineTests
{
	public static Task RunAsync(List<string> failures)
	{
		var seed = DeterministicSeed.FromString("cedar-chip-foundation");

		Assert(
			failures,
			"Shuffle should replay deterministically for identical seeds",
			Codes(FiveCardDrawEngine.ShuffleDeck(seed, "shuffle-a")) == Codes(FiveCardDrawEngine.ShuffleDeck(seed, "shuffle-a")));

		Assert(
			failures,
			"Different shuffle streams should diverge",
			Codes(FiveCardDrawEngine.ShuffleDeck(seed, "shuffle-a")) != Codes(FiveCardDrawEngine.ShuffleDeck(seed, "shuffle-b")));

		var initial = FiveCardDrawEngine.DealFiveCardDraw(seed, "opening-hand");
		var heldState = FiveCardDrawEngine.Reduce(initial, new RoundAction(RoundActionKind.SetHoldMask, HoldMask: [true, false, true, false, false]));
		var drawnState = FiveCardDrawEngine.Reduce(heldState, new RoundAction(RoundActionKind.Draw));

		var longRunningLedger = new MachineLedgerState
		{
			MachineId = 1,
			RoundCount = 42,
			CapitalIn = 420_000m,
			CapitalOut = 41_969_664m,
			BaseCapitalOut = 71_649m,
			JackpotCapitalOut = 0m,
			DoubleUpCapitalOut = 41_898_015m,
			NetSinceLastClose = 0m
		};
		var entropyCreated = false;
		try
		{
			entropyCreated = RoundNoiseRng.CreateEntropySeed(
				Guid.Parse("10000000-0000-0000-0000-000000000001"),
				1,
				5_000m,
				longRunningLedger) != 0UL;
		}
		catch (OverflowException)
		{
			entropyCreated = false;
		}

		Assert(
			failures,
			"Entropy seed should support long-running machine ledgers without Int32 overflow",
			entropyCreated);

		Assert(failures, "Held card 0 should survive draw", drawnState.Hand[0].Equals(initial.Hand[0]));
		Assert(failures, "Held card 2 should survive draw", drawnState.Hand[2].Equals(initial.Hand[2]));
		Assert(failures, "Discarded card 1 should be replaced", !drawnState.Hand[1].Equals(initial.Hand[1]));
		Assert(failures, "Discarded card 3 should be replaced", !drawnState.Hand[3].Equals(initial.Hand[3]));
		Assert(failures, "Discarded card 4 should be replaced", !drawnState.Hand[4].Equals(initial.Hand[4]));

		var royal = FiveCardDrawEngine.EvaluateHand(FiveCardDrawEngine.ParseCards(["TH", "JH", "QH", "KH", "AH"]));
		Assert(failures, "Royal flush should evaluate correctly", royal.Category == HandCategory.RoyalFlush);

		var wheel = FiveCardDrawEngine.EvaluateHand(FiveCardDrawEngine.ParseCards(["AS", "2D", "3C", "4H", "5S"]));
		Assert(failures, "Wheel straight should evaluate as straight", wheel.Category == HandCategory.Straight);
		Assert(failures, "Wheel straight high card should be 5", wheel.Tiebreak.SequenceEqual([5]));

		var highPair = FiveCardDrawEngine.EvaluateHand(FiveCardDrawEngine.ParseCards(["QH", "QS", "7C", "4D", "2S"]));
		Assert(
			failures,
			"Jacks-or-Better should pay qualifying high pairs",
			FiveCardDrawEngine.ResolvePayout(highPair, 5, PaytableProfile.JacksOrBetter) == 5);
		Assert(
			failures,
			"Two-pair-minimum should reject lone high pairs",
			FiveCardDrawEngine.ResolvePayout(highPair, 5, PaytableProfile.TwoPairMinimum) == 0);

		var aceSafetySession = Lucky5DoubleUpEngine.CreateSessionFromDeck(
			seedRoot: seed,
			deck: FiveCardDrawEngine.ParseCards(["9H", "AS", "4C", "2D"]),
			openingAmount: 20);
		var aceSafetyResolution = Lucky5DoubleUpEngine.ResolveGuess(aceSafetySession, BigSmallGuess.Small);
		Assert(failures, "Ace challenger should auto-win even on the wrong side when ace safety is enabled", aceSafetyResolution.Outcome == Lucky5DoubleUpOutcome.Win);
		Assert(failures, "Ace auto-win should double the amount", aceSafetyResolution.NextAmount == 40);

		var openingDealerLuckySession = Lucky5DoubleUpEngine.CreateSessionFromDeck(
			seedRoot: seed,
			deck: FiveCardDrawEngine.ParseCards(["5S", "KC", "2D", "9H"]),
			openingAmount: 10);
		Assert(failures, "Opening dealer 5S should not activate no-lose mode", !openingDealerLuckySession.IsNoLoseActive);
		Assert(failures, "Opening dealer 5S should not change the amount", openingDealerLuckySession.CurrentAmount == 10);

		var revealedLuckyResultSession = Lucky5DoubleUpEngine.CreateSessionFromDeck(
			seedRoot: seed,
			deck: FiveCardDrawEngine.ParseCards(["4H", "5S", "KC", "2D"]),
			openingAmount: 10);
		var revealedLuckyResult = Lucky5DoubleUpEngine.ResolveGuess(revealedLuckyResultSession, BigSmallGuess.Big);
		Assert(failures, "A revealed 5S result card should still resolve as a normal win", revealedLuckyResult.Outcome == Lucky5DoubleUpOutcome.Win);
		Assert(failures, "A revealed 5S result card should not arm no-lose mode", !revealedLuckyResult.Session.IsNoLoseActive);
		Assert(failures, "A revealed 5S result card should only double the amount", revealedLuckyResult.NextAmount == 20);

		var luckySwitchSession = Lucky5DoubleUpEngine.CreateSessionFromDeck(
			seedRoot: seed,
			deck: FiveCardDrawEngine.ParseCards(["9H", "5S", "KC", "2D"]),
			openingAmount: 10);
		var switchedLuckySession = Lucky5DoubleUpEngine.SwitchDealer(luckySwitchSession);
		Assert(failures, "First 5S switch should activate no-lose mode", switchedLuckySession.IsNoLoseActive);
		Assert(failures, "First 5S switch should apply 4x multiplier", switchedLuckySession.CurrentAmount == 40);

		var safeFailSession = Lucky5DoubleUpEngine.CreateSessionFromDeck(
			seedRoot: seed,
			deck: FiveCardDrawEngine.ParseCards(["9H", "5S", "9C", "2D"]),
			openingAmount: 10);
		var afterLuckySwitch = Lucky5DoubleUpEngine.SwitchDealer(safeFailSession);
		var safeFailResolution = Lucky5DoubleUpEngine.ResolveGuess(afterLuckySwitch, BigSmallGuess.Small);
		Assert(failures, "Wrong guess under no-lose mode should safe-fail", safeFailResolution.Outcome == Lucky5DoubleUpOutcome.SafeFail);
		Assert(failures, "Safe fail should bank the protected winnings", safeFailResolution.CashoutCredits == 40);
		Assert(failures, "Double-up switch should not append to the played high/low trail", afterLuckySwitch.PlayedDealerIndexes is { Length: 0 });
		Assert(failures, "Double-up resolution should append only the dealer card that was actually played", safeFailResolution.Session.PlayedDealerIndexes is { Length: 1 } safeTrail && safeTrail[0] == 1);

		// Chained no-lose: win after Lucky5 should keep protection, then lose → SafeFail
		// Deck: 9H(start) → 5S(switch,Lucky5) → KH(guess Big,win:K>5) → QD(guess Big,lose:Q<K)
		var chainedNoLoseSession = Lucky5DoubleUpEngine.CreateSessionFromDeck(
			seedRoot: seed,
			deck: FiveCardDrawEngine.ParseCards(["9H", "5S", "KH", "QD"]),
			openingAmount: 10);
		var chainedSwitch = Lucky5DoubleUpEngine.SwitchDealer(chainedNoLoseSession);
		Assert(failures, "Chained: switch onto 5S should activate no-lose", chainedSwitch.IsNoLoseActive);
		Assert(failures, "Chained: 5S switch should 4x the amount", chainedSwitch.CurrentAmount == 40);
		var chainedWin = Lucky5DoubleUpEngine.ResolveGuess(chainedSwitch, BigSmallGuess.Big);
		Assert(failures, "Chained: winning guess after Lucky5 should still be a Win", chainedWin.Outcome == Lucky5DoubleUpOutcome.Win);
		Assert(failures, "Chained: no-lose should persist through wins", chainedWin.Session.IsNoLoseActive);
		Assert(failures, "Chained: amount should double on win", chainedWin.NextAmount == 80);
		Assert(failures, "Chained: played high/low trail should preserve the switched dealer as the first played card", chainedWin.Session.PlayedDealerIndexes is { Length: 1 } chainedTrail && chainedTrail[0] == 1);
		var chainedLoss = Lucky5DoubleUpEngine.ResolveGuess(chainedWin.Session, BigSmallGuess.Big);
		Assert(failures, "Chained: losing after wins with no-lose active should SafeFail", chainedLoss.Outcome == Lucky5DoubleUpOutcome.SafeFail);
		Assert(failures, "Chained: SafeFail should bank the pre-loss amount", chainedLoss.CashoutCredits == 80);
		Assert(failures, "Chained: every high/low hit should append one played dealer card left-to-right", chainedLoss.Session.PlayedDealerIndexes is { Length: 2 } lossTrail && lossTrail[0] == 1 && lossTrail[1] == 2);

		var repeatedLuckySession = Lucky5DoubleUpEngine.CreateSessionFromDeck(
			seedRoot: seed,
			deck: FiveCardDrawEngine.ParseCards(["9H", "5S", "5S", "KD"]),
			openingAmount: 10);
		var firstLuckySwitch = Lucky5DoubleUpEngine.SwitchDealer(repeatedLuckySession);
		var secondLuckySwitch = Lucky5DoubleUpEngine.SwitchDealer(firstLuckySwitch);
		Assert(failures, "Repeated 5S in the same streak should apply the repeat multiplier", secondLuckySwitch.CurrentAmount == 80);

		var machineCloseSession = Lucky5DoubleUpEngine.CreateSessionFromDeck(
			seedRoot: seed,
			deck: FiveCardDrawEngine.ParseCards(["9H", "AS", "4C", "2D"]),
			openingAmount: 20,
			machineCreditBaseline: 70,
			options: new Lucky5DoubleUpOptions(MaxCreditLimit: 100));
		var machineCloseResolution = Lucky5DoubleUpEngine.ResolveGuess(machineCloseSession, BigSmallGuess.Big);
		Assert(failures, "Credit ceiling should close the machine immediately after a winning double-up", machineCloseResolution.Outcome == Lucky5DoubleUpOutcome.MachineClosed);
		Assert(failures, "Machine close should cash out the post-win amount", machineCloseResolution.CashoutCredits == 40);

		var preCapLuckySwitchSession = Lucky5DoubleUpEngine.CreateSessionFromDeck(
			seedRoot: seed,
			deck: FiveCardDrawEngine.ParseCards(["9H", "5S", "KC", "2D"]),
			openingAmount: 10,
			machineCreditBaseline: 40,
			options: new Lucky5DoubleUpOptions(MaxCreditLimit: 45));
		var preCapLuckySwitch = Lucky5DoubleUpEngine.SwitchDealer(preCapLuckySwitchSession);
		Assert(failures, "Switching onto 5S near the credit ceiling should not auto-close before a winning guess", !preCapLuckySwitch.IsTerminal);
		Assert(failures, "Switching onto 5S should still apply the Lucky 5 multiplier before close evaluation", preCapLuckySwitch.CurrentAmount == 40);

		var postSwitchMachineClose = Lucky5DoubleUpEngine.ResolveGuess(preCapLuckySwitch, BigSmallGuess.Big);
		Assert(failures, "Machine close should happen only after the next winning resolution", postSwitchMachineClose.Outcome == Lucky5DoubleUpOutcome.MachineClosed);
		Assert(failures, "Machine close after a Lucky 5 switch should cash out the real post-win amount", postSwitchMachineClose.CashoutCredits == 80);

		var defaultConfig = EngineConfig.Default;
		Assert(failures, "Approved RTP target should default to the current tuned baseline", defaultConfig.TargetRtp == 0.80m);
		Assert(failures, "Machine policy state should inherit the approved RTP target by default", new MachinePolicyState().TargetRtp == defaultConfig.TargetRtp);
		Assert(failures, "Approved close threshold should default to 40,000,000", defaultConfig.CloseThreshold == 40_000_000m);
		Assert(failures, "Approved payout-scale defaults should match the v8 tuned architecture", defaultConfig.DefaultPayoutScale == 1.15m && defaultConfig.MinPayoutScale == 0.72m && defaultConfig.MaxPayoutScale == 2.05m);

		var defaultCloseSession = Lucky5DoubleUpEngine.CreateSessionFromDeck(
			seedRoot: seed,
			deck: FiveCardDrawEngine.ParseCards(["9H", "AS", "4C", "2D"]),
			openingAmount: 1_000_000,
			machineCreditBaseline: 39_500_000);
		var defaultCloseResolution = Lucky5DoubleUpEngine.ResolveGuess(defaultCloseSession, BigSmallGuess.Big);
		Assert(failures, "Default double-up options should use the approved 40M close threshold", defaultCloseResolution.Outcome == Lucky5DoubleUpOutcome.MachineClosed);

		var exactBoundaryCloseSession = Lucky5DoubleUpEngine.CreateSessionFromDeck(
			seedRoot: seed,
			deck: FiveCardDrawEngine.ParseCards(["9H", "AS", "4C", "2D"]),
			openingAmount: 20,
			machineCreditBaseline: 39_999_980);
		var exactBoundaryCloseResolution = Lucky5DoubleUpEngine.ResolveGuess(exactBoundaryCloseSession, BigSmallGuess.Big);
		Assert(failures, "Crossing exactly onto 40,000,000 should still trigger machine close", exactBoundaryCloseResolution.Outcome == Lucky5DoubleUpOutcome.MachineClosed);

		var unlimitedChainSession = Lucky5DoubleUpEngine.CreateSessionFromDeck(
			seedRoot: seed,
			deck: FiveCardDrawEngine.ParseCards(["9H", "AS", "2D", "AH", "3C", "AD"]),
			openingAmount: 10);
		var unlimitedChainWin1 = Lucky5DoubleUpEngine.ResolveGuess(unlimitedChainSession, BigSmallGuess.Big);
		var unlimitedChainWin2 = Lucky5DoubleUpEngine.ResolveGuess(unlimitedChainWin1.Session, BigSmallGuess.Small);
		var unlimitedChainWin3 = Lucky5DoubleUpEngine.ResolveGuess(unlimitedChainWin2.Session, BigSmallGuess.Big);
		Assert(failures, "Unlimited chaining should allow multiple consecutive winning guesses", unlimitedChainWin1.Outcome == Lucky5DoubleUpOutcome.Win && unlimitedChainWin2.Outcome == Lucky5DoubleUpOutcome.Win && unlimitedChainWin3.Outcome == Lucky5DoubleUpOutcome.Win);
		Assert(failures, "Unlimited chaining should continue doubling the amount across repeated wins", unlimitedChainWin3.NextAmount == 80 && !unlimitedChainWin3.Session.IsTerminal);

		var boardBonusSession = Lucky5DoubleUpEngine.CreateSessionFromDeck(
			seedRoot: seed,
			deck: FiveCardDrawEngine.ParseCards(["2H", "KS", "3D", "KD", "2C"]),
			openingAmount: 10,
			boardBetAmount: 5_000);
		var boardBonusWin1 = Lucky5DoubleUpEngine.ResolveGuess(boardBonusSession, BigSmallGuess.Big);
		var boardBonusWin2 = Lucky5DoubleUpEngine.ResolveGuess(boardBonusWin1.Session, BigSmallGuess.Small);
		var boardBonusWin3 = Lucky5DoubleUpEngine.ResolveGuess(boardBonusWin2.Session, BigSmallGuess.Big);
		var boardBonusWin4 = Lucky5DoubleUpEngine.ResolveGuess(boardBonusWin3.Session, BigSmallGuess.Small);
		Assert(failures, "A completed 5-card double-up board should evaluate and expose the hand rank when it forms a paying hand",
			boardBonusWin4.Session.BoardHandRank == HandCategory.TwoPair);
		Assert(failures, "A completed 5-card double-up board should add the base paytable bonus to the doubled amount",
			boardBonusWin4.Session.LastBoardBonusAmount == 10_000 && boardBonusWin4.NextAmount == 10_160);
		Assert(failures, "Double-up should carry the completed board slot index and cumulative board bonus for the UI contract",
			boardBonusWin4.Session.LastResolvedBoardSlotIndex == 5 && boardBonusWin4.Session.BoardBonusTotal == 10_000);

		var continuedBoardWin = Lucky5DoubleUpEngine.ResolveGuess(boardBonusWin4.Session, BigSmallGuess.Big);
		Assert(failures, "Continuing after a completed double-up board should start a fresh board from the carried dealer card",
			continuedBoardWin.Session.CurrentBoardCards is { Length: 2 } continuedBoard
			&& continuedBoard[0].Code == boardBonusWin4.Session.DealerCard.Code
			&& !continuedBoardWin.Session.CurrentBoardComplete
			&& continuedBoardWin.Session.LastResolvedBoardSlotIndex == 2);

		var warmupScale = MachinePolicy.ResolvePayoutScale(
			new MachinePolicyState
			{
				TargetRtp = defaultConfig.TargetRtp,
				RoundCount = 1
			},
			seed);
		Assert(failures, "Warmup payout scales should open at the approved generous values", warmupScale.SmallScale == defaultConfig.WarmupOpeningSmallScale && warmupScale.MediumScale == defaultConfig.WarmupOpeningMediumScale && warmupScale.BigScale == defaultConfig.WarmupOpeningBigScale);

		var equilibriumScale = MachinePolicy.ResolvePayoutScale(
			new MachinePolicyState
			{
				CreditsIn = 1_000_000m,
				CreditsOut = 850_000m,
				BaseCreditsOut = 386_825m,
				JackpotCreditsOut = 35_000m,
				DoubleUpCreditsOut = 90_000m,
				TargetRtp = defaultConfig.TargetRtp,
				RoundCount = defaultConfig.ConvergenceHorizon
			},
			seed);
		Assert(
			failures,
			"Equilibrium payout scales should stay ordered and inside the configured payout-scale band",
			equilibriumScale.SmallScale >= defaultConfig.MinPayoutScale
				&& equilibriumScale.SmallScale <= defaultConfig.MaxPayoutScale
				&& equilibriumScale.MediumScale >= defaultConfig.MinPayoutScale
				&& equilibriumScale.MediumScale <= defaultConfig.MaxPayoutScale
				&& equilibriumScale.BigScale >= defaultConfig.MinPayoutScale
				&& equilibriumScale.BigScale <= defaultConfig.MaxPayoutScale
				&& equilibriumScale.SmallScale <= equilibriumScale.MediumScale
				&& equilibriumScale.MediumScale <= equilibriumScale.BigScale);

		var zeroJackpotBasePolicy = MachinePolicy.ResolvePolicy(
			new MachinePolicyState
			{
				CreditsIn = 1_000_000m,
				CreditsOut = 380_000m,
				BaseCreditsOut = 380_000m,
				TargetRtp = defaultConfig.TargetRtp,
				RoundCount = defaultConfig.ConvergenceHorizon
			},
			seed);
		var expectedBaseScale = defaultConfig.TargetScaledBaseRtp / defaultConfig.MinimumObservedBaseRtp;
		Assert(
			failures,
			"Base payout scale should reserve the configured jackpot RTP budget even before a fresh machine has seen jackpot wins.",
			Math.Abs(zeroJackpotBasePolicy.Telemetry.BaseScale - expectedBaseScale) < 0.0001m);

		var hotDoubleUpBasePolicy = MachinePolicy.ResolvePolicy(
			new MachinePolicyState
			{
				CreditsIn = 1_000_000m,
				CreditsOut = 530_000m,
				BaseCreditsOut = 380_000m,
				DoubleUpCreditsOut = 150_000m,
				TargetRtp = defaultConfig.TargetRtp,
				RoundCount = defaultConfig.ConvergenceHorizon
			},
			seed);
		var expectedHotDoubleUpBaseScale = (defaultConfig.TargetRtp - defaultConfig.TargetJackpotRtp - 0.1500m) / defaultConfig.MinimumObservedBaseRtp;
		Assert(
			failures,
			"Base payout scale should reserve observed double-up RTP when that layer runs above its target.",
			Math.Abs(hotDoubleUpBasePolicy.Telemetry.BaseScale - expectedHotDoubleUpBaseScale) < 0.0001m);

		var earlyOutlierState = new MachinePolicyState
		{
			CreditsIn = 200_000m,
			CreditsOut = 5_000m,
			BaseCreditsOut = 2_000m,
			JackpotCreditsOut = 1_000m,
			DoubleUpCreditsOut = 2_000m,
			TargetRtp = defaultConfig.TargetRtp,
			RoundCount = 6
		};
		var rawDrift = earlyOutlierState.Drift;
		var smoothedDrift = earlyOutlierState.ComputeSmoothedDrift(defaultConfig);
		Assert(failures, "Adaptive RTP smoothing should damp early outlier drift versus raw drift", Math.Abs(smoothedDrift) < Math.Abs(rawDrift));

		var firstBuyInOutlierState = new MachinePolicyState
		{
			CreditsIn = 200_000m,
			CreditsOut = 40_000_000m,
			BaseCreditsOut = 40_000_000m,
			RoundCount = 1
		};
		Assert(
			failures,
			"Adaptive RTP smoothing should not fully trust a one-round sample just because the first buy-in has many credits.",
			firstBuyInOutlierState.ComputeSmoothedObservedRtp(defaultConfig) == defaultConfig.TargetRtp);

		var overTargetOfferState = new MachinePolicyState
		{
			CreditsIn = 1_000m,
			CreditsOut = 950m,
			TargetRtp = defaultConfig.TargetRtp,
			RoundCount = defaultConfig.ConvergenceHorizon
		};
		Assert(
			failures,
			"Double-up is a fixed cabinet rule and should remain available even while the machine is hot.",
			Enumerable.Range(0, 200).All(index => MachinePolicy.ShouldOfferDoubleUp(overTargetOfferState, DeterministicSeed.Derive(seed, "du-always-on-test", index))));

		var highPressureDoubleUpState = new MachinePolicyState
		{
			CreditsIn = 1_000_000m,
			CreditsOut = 920_000m,
			BaseCreditsOut = 600_000m,
			DoubleUpCreditsOut = 220_000m,
			TargetRtp = defaultConfig.TargetRtp,
			RoundCount = defaultConfig.ConvergenceHorizon,
			NetSinceLastClose = defaultConfig.SoftCapHard + 1m
		};
		var highPressureDeck = MachinePolicy.BuildDoubleUpDeck(
			FiveCardDrawEngine.BuildStandardDeck(),
			seed,
			roundsSinceLucky5Hit: 4,
			netSinceLastClose: highPressureDoubleUpState.NetSinceLastClose,
			roundPolicyMode: PolicyDistributionMode.Cold,
			state: highPressureDoubleUpState,
			openingAmount: 250_000,
			machineCreditBaseline: 33_000_000);
		Assert(
			failures,
			"High double-up pressure should reduce auto-win key cards instead of hiding the double-up feature.",
			highPressureDeck.Count(card => card.Rank == 14) < 4
				&& !highPressureDeck.Any(card => card.Rank == 5 && card.Suit == 'S')
				&& highPressureDeck.Length >= defaultConfig.DoubleUpMinDeckSize);
		Assert(
			failures,
			"Double-up pressure deck should still be a no-duplicate card set after bounded removals.",
			highPressureDeck.Distinct().Count() == highPressureDeck.Length);

		var highPressurePlayDeck = MachinePolicy.BuildDoubleUpPlayDeck(
			FiveCardDrawEngine.BuildStandardDeck(),
			DeterministicSeed.Derive(seed, "du-play-pressure"),
			roundsSinceLucky5Hit: 4,
			netSinceLastClose: highPressureDoubleUpState.NetSinceLastClose,
			roundPolicyMode: PolicyDistributionMode.Cold,
			state: highPressureDoubleUpState,
			openingAmount: 250_000,
			machineCreditBaseline: 33_000_000);
		var firstTwelvePairWins = Enumerable.Range(0, Math.Min(12, highPressurePlayDeck.Length - 1))
			.Count(index => IsOptimalHiLoWin(highPressurePlayDeck[index], highPressurePlayDeck[index + 1]));
		Assert(
			failures,
			"High double-up pressure play deck should sequence trap-heavy adjacent pairs without changing cards.",
			firstTwelvePairWins <= 3
				&& highPressurePlayDeck.Distinct().Count() == highPressurePlayDeck.Length);

		var recoveryDoubleUpState = new MachinePolicyState
		{
			CreditsIn = 1_000_000m,
			CreditsOut = 650_000m,
			BaseCreditsOut = 550_000m,
			DoubleUpCreditsOut = 60_000m,
			TargetRtp = defaultConfig.TargetRtp,
			RoundCount = defaultConfig.ConvergenceHorizon,
			ConsecutiveLosses = defaultConfig.StreakHardThreshold,
			RoundsSinceMediumWin = defaultConfig.MediumWinDroughtThreshold
		};
		var recoveryDeck = MachinePolicy.BuildDoubleUpDeck(
			FiveCardDrawEngine.BuildStandardDeck(),
			DeterministicSeed.Derive(seed, "du-recovery"),
			roundsSinceLucky5Hit: defaultConfig.DoubleUpPressureRecoveryDroughtRounds + 8,
			netSinceLastClose: 0m,
			PolicyDistributionMode.Hot,
			recoveryDoubleUpState,
			openingAmount: 50_000,
			machineCreditBaseline: 0);
		Assert(
			failures,
			"Recovery double-up pressure should preserve Lucky 5 and all ace auto-win cards during long droughts.",
			recoveryDeck.Count(card => card.Rank == 14) == 4
				&& recoveryDeck.Any(card => card.Rank == 5 && card.Suit == 'S'));

		var noiseA = PresentationNoiseGenerator.Build(seed, 4);
		var noiseB = PresentationNoiseGenerator.Build(seed, 4);
		var noiseC = PresentationNoiseGenerator.Build(seed, 5);
		Assert(failures, "Presentation noise should replay deterministically", noiseA == noiseB);
		Assert(failures, "Presentation noise should vary by round index", noiseA != noiseC);

		Assert(failures, "Bonanza reference should preserve next-card BIG/SMALL double-up style", CabinetReferences.BonanzaGoldenPoker.SupportsBonanzaBigSmall);
		Assert(failures, "Bonanza reference should preserve 10-credit max bet", CabinetReferences.BonanzaGoldenPoker.OperatorSettings?.MaxBetCredits == 10);
		Assert(failures, "Bonanza reference should preserve 5,000-credit auto-collect threshold", CabinetReferences.BonanzaGoldenPoker.OperatorSettings?.AutoCollectThreshold == 5000);
		Assert(failures, "Bonus Poker reference should flag four-of-a-kind jackpot lineage", CabinetReferences.BonusPoker.GetJackpotFeature(HandCategory.FourOfAKind) is not null);
		Assert(failures, "Bonus Poker reference should flag straight-flush jackpot lineage", CabinetReferences.BonusPoker.GetJackpotFeature(HandCategory.StraightFlush) is not null);

		var fhKingsTrips = FiveCardDrawEngine.EvaluateHand(FiveCardDrawEngine.ParseCards(["KH", "KD", "KC", "JH", "JS"]));
		Assert(failures, "Full House K-K-K-J-J should have Tiebreak[0] == 13 (Kings trips)", fhKingsTrips.Category == HandCategory.FullHouse && fhKingsTrips.Tiebreak[0] == 13);

		var fhJacksTrips = FiveCardDrawEngine.EvaluateHand(FiveCardDrawEngine.ParseCards(["JH", "JD", "JC", "KH", "KS"]));
		Assert(failures, "Full House J-J-J-K-K should have Tiebreak[0] == 11 (Jacks trips)", fhJacksTrips.Category == HandCategory.FullHouse && fhJacksTrips.Tiebreak[0] == 11);

		const int jackpotFullHouseRankKings = 13;
		Assert(failures, "Full House K-K-K-J-J should trigger jackpot when JackpotFullHouseRank is Kings",
			fhKingsTrips.Tiebreak[0] == jackpotFullHouseRankKings);
		Assert(failures, "Full House J-J-J-K-K should NOT trigger jackpot when JackpotFullHouseRank is Kings",
			fhJacksTrips.Tiebreak[0] != jackpotFullHouseRankKings);

		var advisedQuads = FiveCardDrawEngine.ComputeAdvisedHolds(FiveCardDrawEngine.ParseCards(["KH", "KD", "KC", "KS", "2H"]));
		Assert(failures, "Advised holds for quads should hold the four matching cards", advisedQuads.SequenceEqual([0, 1, 2, 3]));

		var advisedPair = FiveCardDrawEngine.ComputeAdvisedHolds(FiveCardDrawEngine.ParseCards(["AH", "AD", "3C", "7S", "9H"]));
		Assert(failures, "Advised holds for a pair should hold the pair", advisedPair.SequenceEqual([0, 1]));

		var advisedNothing = FiveCardDrawEngine.ComputeAdvisedHolds(FiveCardDrawEngine.ParseCards(["2H", "4D", "6C", "8S", "KH"]));
		Assert(failures, "Advised holds for no pattern should return empty", advisedNothing.Length == 0);

		return Task.CompletedTask;
	}

	private static void Assert(List<string> failures, string message, bool condition)
	{
		if (!condition)
		{
			failures.Add(message);
		}
	}

	private static bool IsOptimalHiLoWin(CleanRoomCard dealer, CleanRoomCard challenger)
	{
		if (challenger.Rank == 14 || dealer.Rank == 14)
		{
			return true;
		}

		return dealer.Rank <= 8
			? challenger.Rank > dealer.Rank
			: challenger.Rank < dealer.Rank;
	}

	private static string Codes(IEnumerable<CleanRoomCard> cards)
		=> string.Join(",", cards.Select(card => card.Code));
}
