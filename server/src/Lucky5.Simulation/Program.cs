using Lucky5.Domain.Entities;
using Lucky5.Domain.Game.CleanRoom;

// Parse command line arguments
var rounds = 10_000;
var minRtp = 0.78m;
var maxRtp = 0.82m;
var isCertificationRun = false;
var behavior = PlayerBehavior.Balanced;
var varianceReport = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--rounds":
            if (i + 1 < args.Length && int.TryParse(args[i + 1], out var r))
                rounds = r;
            break;
        case "--min-rtp":
            if (i + 1 < args.Length && decimal.TryParse(args[i + 1], out var min))
                minRtp = min;
            break;
        case "--max-rtp":
            if (i + 1 < args.Length && decimal.TryParse(args[i + 1], out var max))
                maxRtp = max;
            break;
        case "--certification":
            isCertificationRun = true;
            rounds = 500_000;
            break;
        case "--behavior":
            if (i + 1 < args.Length && TryParseBehavior(args[i + 1], out var parsedBehavior))
                behavior = parsedBehavior;
            break;
        case "--variance-report":
            varianceReport = true;
            break;
    }
}

const int Bet = 5_000;

var cfg = EngineConfig.Default;
var paytable = PaytableProfile.Lebanese;

Console.WriteLine("=== Lucky5 RTP Monte Carlo Simulation ===");
Console.WriteLine($"Bet: {Bet:N0} | Paytable: {paytable.Name}");
Console.WriteLine("Stake model: deal stake + draw stake per completed hand, matching GameService.DealAsync/DrawAsync");
Console.WriteLine($"Controller nominal reserve: {cfg.TargetRtp:P2} = Base {cfg.TargetScaledBaseRtp:P2} + Jackpot {cfg.TargetJackpotRtp:P2} + Double-Up pressure target {cfg.TargetDoubleUpRtp:P2}");
Console.WriteLine($"Machine close threshold: {cfg.CloseThreshold:N0} | Double-up always-on | DU pressure removals: {cfg.DoubleUpPressureMaxKeyRemovals} | Min DU deck: {cfg.DoubleUpMinDeckSize}");
Console.WriteLine($"Run type: {(isCertificationRun ? "Certification" : "CI Gate")} | Rounds: {rounds:N0} | RTP range: [{minRtp:P2}, {maxRtp:P2}]");
Console.WriteLine($"Behavior: {DescribeBehavior(behavior)}");
Console.WriteLine();

// Run the main simulation with enhanced telemetry
var mainResult = RunSimulation(rounds, behavior, 0, true);

Console.WriteLine("--- Main Simulation Results ---");
PrintEnhancedSummary($"{rounds:N0} rounds", mainResult);

if (varianceReport)
{
    PrintVarianceReport();
}

// Determine pass/fail based on RTP bounds
var passed = mainResult.TotalRtp >= minRtp && mainResult.TotalRtp <= maxRtp;
Console.WriteLine();
Console.WriteLine($"=== RESULT: {(passed ? "PASS" : "FAIL")} ===");
Console.WriteLine($"Final RTP: {mainResult.TotalRtp:P2} (target range: [{minRtp:P2}, {maxRtp:P2}])");

// Exit with appropriate code
Environment.Exit(passed ? 0 : 1);

SimulationResult RunSimulation(int rounds, PlayerBehavior behavior, int sampleIndex, bool enhancedTelemetry = false)
{
    var ledger = new MachineLedgerState { MachineId = 1, TargetRtp = cfg.TargetRtp };
    var session = new SessionState();
    session.StartNewSession();

    var result = new SimulationResult(behavior, rounds)
    {
        SessionsStarted = 1
    };

    for (var roundIndex = 0; roundIndex < rounds; roundIndex++)
    {
        if (session.PendingReset || session.MachineCredits < Bet * 2m)
        {
            session.StartNewSession();
            result.SessionsStarted++;
        }

        session.BeginRound();

        var seed = DeterministicSeed.FromString($"rtp-{DescribeBehavior(behavior)}-{rounds}-{sampleIndex}-{roundIndex}");
        var policyState = BuildPolicyState(ledger);
        var policyResolution = MachinePolicy.ResolvePolicy(policyState, seed);
        var policyMode = policyResolution.DistributionMode;
        if (session.CounterplayScore >= 3 && policyMode == PolicyDistributionMode.Cold)
        {
            policyMode = PolicyDistributionMode.Neutral;
            result.CounterplayColdOverrides++;
        }

        result.RecordPolicyMode(policyMode);
        if (behavior == PlayerBehavior.CounterplaySabotage && policyMode == PolicyDistributionMode.Hot)
        {
            result.CounterplayHotRounds++;
            if (!result.CounterplayWasHotLastRound)
            {
                result.CounterplayHotTransitions++;
            }

            if (result.CounterplaySabotageRounds > 0)
            {
                result.CounterplayHotAfterSabotageRounds++;
            }

            result.CounterplayWasHotLastRound = true;
        }
        else
        {
            result.CounterplayWasHotLastRound = false;
        }
        
        // Track enhanced telemetry
        if (enhancedTelemetry)
        {
            if (policyResolution.Telemetry.IsWarmupActive) result.WarmupActivations++;
            if (policyResolution.Telemetry.IsPityActive) result.PityActivations++;
            if (policyResolution.Telemetry.IsCrisisActive) result.CrisisActivations++;
            result.JackpotLeakAdjustments += policyResolution.Telemetry.JackpotLeakAdjustment;
            result.DoubleUpLeakAdjustments += policyResolution.Telemetry.DoubleUpLeakAdjustment;
        }

        ledger.CapitalIn += Bet;
        ledger.RoundCount++;
        ledger.RoundsSinceMediumWin++;
        ledger.RoundsSinceLucky5Hit++;
        if (ledger.CooldownRoundsRemaining > 0)
        {
            ledger.CooldownRoundsRemaining--;
        }

        ledger.LastDistributionMode = policyMode switch
        {
            PolicyDistributionMode.Cold => DistributionMode.Cold,
            PolicyDistributionMode.Hot => DistributionMode.Hot,
            _ => DistributionMode.Neutral
        };
        ledger.ActiveFourOfAKindSlot = (ledger.RoundCount % 2 == 0) ? (int)(seed % 2) : 1 - (int)(seed % 2);
        ApplyJackpotContributions(ledger);
        ledger.NetSinceLastClose = Math.Max(ledger.CapitalIn - ledger.CapitalOut, 0m);
        session.MachineCredits -= Bet;

        ledger.CapitalIn += Bet;
        ApplyJackpotContributions(ledger);
        ledger.NetSinceLastClose = Math.Max(ledger.CapitalIn - ledger.CapitalOut, 0m);
        session.MachineCredits -= Bet;

        result.TotalIn += Bet * 2m;

        var standardDeck = FiveCardDrawEngine.BuildStandardDeck();
        var alteredDeck = MachinePolicy.AlterDeck(standardDeck, policyMode, seed, ledger.ConsecutiveLosses);
        var shuffledDeck = FiveCardDrawEngine.ShuffleDeck(seed, "hand", alteredDeck);
        var hand = shuffledDeck.Take(5).ToArray();
        var drawState = FiveCardDrawState.Create(seed, shuffledDeck, hand);
        var sabotagePhase = IsCounterplaySabotagePhase(behavior, ledger, session);
        var holdMask = ComputeBehaviorHolds(hand, behavior, seed, roundIndex, sabotagePhase, result);
        UpdateCounterplay(session, AssessCounterplay(hand, HoldIndexesFromMask(holdMask)), result);
        drawState = FiveCardDrawEngine.Reduce(drawState, new RoundAction(RoundActionKind.SetHoldMask, HoldMask: holdMask));
        drawState = FiveCardDrawEngine.Reduce(drawState, new RoundAction(RoundActionKind.Draw));

        var evaluation = FiveCardDrawEngine.EvaluateHand(drawState.Hand);
        var rawBasePayout = FiveCardDrawEngine.ResolvePayout(evaluation, Bet, paytable);
        var basePayout = rawBasePayout;
        if (basePayout > 0 && drawState.Hand.Any(card => card.Rank == 14))
        {
            basePayout *= 2;
            result.AceMultiplierHands++;
            result.AceMultiplierUnscaledCredits += basePayout - rawBasePayout;
        }
        var scaleState = BuildPolicyState(ledger);
        var scaleResolution = MachinePolicy.ResolvePolicy(scaleState, seed);
        var payoutScale = scaleResolution.ForTier(MachinePolicy.ClassifyHand(evaluation.Category));
        ledger.LastPayoutScale = payoutScale;
        result.PayoutScaleSum += payoutScale;
        result.PayoutScaleSamples++;

        var scaledBasePayout = basePayout > 0
            ? (int)Math.Round(basePayout * payoutScale, MidpointRounding.AwayFromZero)
            : 0;

        var payout = scaledBasePayout;
        decimal jackpotOverlay = 0m;
        if (scaledBasePayout > 0)
        {
            ledger.CapitalOut += scaledBasePayout;
            ledger.BaseCapitalOut += basePayout;
            ledger.ConsecutiveLosses = 0;
            ledger.LastWinChannel = WinChannel.BaseGame;
            if (MachinePolicy.ClassifyHand(evaluation.Category) >= PayoutTier.Medium)
            {
                ledger.RoundsSinceMediumWin = 0;
            }

            ledger.CooldownRoundsRemaining = MachinePolicy.ComputeCooldownLength(evaluation.Category, seed);

            var jackpotWon = ResolveJackpot(ref ledger, evaluation, scaledBasePayout);
            if (jackpotWon.TotalPayout > 0)
            {
                jackpotOverlay = jackpotWon.TotalPayout - scaledBasePayout;
                ledger.CapitalOut += jackpotOverlay;
                ledger.JackpotCapitalOut += jackpotOverlay;
                ledger.LastWinChannel = WinChannel.Jackpot;
                payout = (int)jackpotWon.TotalPayout;
                result.RecordJackpot(jackpotWon.Kind, jackpotWon.TotalPayout);
            }
        }
        else
        {
            ledger.ConsecutiveLosses++;
            ledger.LastWinChannel = WinChannel.None;
        }

        ledger.NetSinceLastClose = Math.Max(ledger.CapitalIn - ledger.CapitalOut, 0m);

        result.ScaledBaseOut += scaledBasePayout;
        result.JackpotOverlayOut += jackpotOverlay;
        
        // Track RTP windows for enhanced telemetry
        if (enhancedTelemetry && (roundIndex + 1) % 1000 == 0)
        {
            var currentRtp = ledger.CapitalIn <= 0m ? 0m : decimal.Round((ledger.CapitalOut) / ledger.CapitalIn, 4);
            result.RtpWindows.Add(currentRtp);
        }

        if (payout > 0)
        {
            result.DirectPayingSpins++;
            if (MachinePolicy.ClassifyHand(evaluation.Category) >= PayoutTier.Medium)
            {
                result.MediumOrBetterSpins++;
            }

            result.PreDoubleUpWinCredits += payout;
            result.EligibleWinningRounds++;

            var offerState = BuildPolicyState(ledger);
            var offered = MachinePolicy.ShouldOfferDoubleUp(offerState, seed);
            if (offered)
            {
                result.OfferedWinningRounds++;
            }

            if (offered && ShouldEnterDoubleUp(behavior, seed, payout, session.MachineCredits, sabotagePhase))
            {
                result.EnteredDoubleUpChains++;
                result.EnteredTriggerCredits += payout;
                var doubleUpOpeningAmount = payout;

                var chainResult = PlayDoubleUpChain(seed, policyMode, behavior, session, payout, doubleUpOpeningAmount, offerState, sabotagePhase, result);
                result.DoubleUpOverlayOut += chainResult.Delta;
                ledger.CapitalOut += chainResult.Delta;
                ledger.DoubleUpCapitalOut += chainResult.Delta;
                ledger.LastWinChannel = chainResult.Delta > 0m ? WinChannel.DoubleUp : ledger.LastWinChannel;

                if (chainResult.ContinuedAfterTakeHalf && session.MachineCredits >= 50_000_000m)
                {
                    result.Over50MViaTakeHalfContinuation++;
                }
            }
            else
            {
                BankCredits(session, payout, result, jackpotOverlay > 0m ? BankEventChannel.Jackpot : BankEventChannel.BaseGame);
            }
        }

        if (!session.PendingReset && ShouldCashOutSession(behavior, session))
        {
            session.PendingReset = true;
        }
    }

    result.FinalObservedRtp = ledger.ObservedRtp;
    result.FinalBaseRtp = ledger.CapitalIn <= 0m ? 0m : decimal.Round(result.ScaledBaseOut / ledger.CapitalIn, 4);
    result.FinalJackpotRtp = ledger.CapitalIn <= 0m ? 0m : decimal.Round(result.JackpotOverlayOut / ledger.CapitalIn, 4);
    result.FinalDoubleUpRtp = ledger.CapitalIn <= 0m ? 0m : decimal.Round(result.DoubleUpOverlayOut / ledger.CapitalIn, 4);
    return result;
}

static MachinePolicyState BuildPolicyState(MachineLedgerState ledger) => new()
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

JackpotResolution ResolveJackpot(ref MachineLedgerState ledger, HandEvaluation evaluation, int scaledBasePayout)
{
    if (evaluation.Category == HandCategory.FullHouse
        && evaluation.Tiebreak[0] == ledger.JackpotFullHouseRank
        && ledger.JackpotFullHouse > scaledBasePayout)
    {
        var jackpot = ledger.JackpotFullHouse;
        ledger.JackpotFullHouse = cfg.JackpotFullHouseStart;
        ledger.JackpotFullHouseRank = ledger.JackpotFullHouseRank >= 14 ? 2 : ledger.JackpotFullHouseRank + 1;
        return new JackpotResolution(jackpot, JackpotHitKind.FullHouse);
    }

    if (evaluation.Category == HandCategory.FourOfAKind && ledger.ActiveFourOfAKindSlot == 0 && ledger.JackpotFourOfAKindA > scaledBasePayout)
    {
        var jackpot = ledger.JackpotFourOfAKindA;
        ledger.JackpotFourOfAKindA = cfg.JackpotFourOfAKindStart;
        return new JackpotResolution(jackpot, JackpotHitKind.FourOfAKindA);
    }

    if (evaluation.Category == HandCategory.FourOfAKind && ledger.ActiveFourOfAKindSlot == 1 && ledger.JackpotFourOfAKindB > scaledBasePayout)
    {
        var jackpot = ledger.JackpotFourOfAKindB;
        ledger.JackpotFourOfAKindB = cfg.JackpotFourOfAKindStart;
        return new JackpotResolution(jackpot, JackpotHitKind.FourOfAKindB);
    }

    if (evaluation.Category == HandCategory.StraightFlush && ledger.JackpotStraightFlush > scaledBasePayout)
    {
        var jackpot = ledger.JackpotStraightFlush;
        ledger.JackpotStraightFlush = cfg.JackpotStraightFlushStart;
        return new JackpotResolution(jackpot, JackpotHitKind.StraightFlush);
    }

    if (evaluation.Category == HandCategory.FiveOfAKind && ledger.JackpotKent > scaledBasePayout)
    {
        var jackpot = ledger.JackpotKent;
        ledger.JackpotKent = cfg.JackpotKentStart;
        return new JackpotResolution(jackpot, JackpotHitKind.Kent);
    }

    return new JackpotResolution(0m, JackpotHitKind.None);
}

DoubleUpChainResult PlayDoubleUpChain(
    ulong roundSeed,
    PolicyDistributionMode policyMode,
    PlayerBehavior behavior,
    SessionState bank,
    int originalWinAmount,
    int openingAmount,
    MachinePolicyState policyState,
    bool sabotagePhase,
    SimulationResult result)
{
    var machineCreditBaseline = Decimal.ToInt32(Math.Min(bank.MachineCredits, int.MaxValue));
    var pressure = MachinePolicy.ComputeDoubleUpDeckPressure(
        policyState,
        policyState.RoundsSinceLucky5Hit,
        policyState.NetSinceLastClose,
        policyMode,
        openingAmount,
        machineCreditBaseline);
    var projectedWin = bank.MachineCredits + (openingAmount * 2m);
    var projectedChainExposure = bank.MachineCredits + Math.Max(openingAmount * 16m, openingAmount * 2m);
    var isProjectedCloseCall = projectedWin >= cfg.CloseThreshold * cfg.DoubleUpSequenceCreditStart
        || projectedChainExposure >= cfg.SoftCapWarning;
    var sequencePressureStart = projectedChainExposure >= cfg.SoftCapWarning
        ? Math.Min(cfg.DoubleUpSequencePressureStart, cfg.DoubleUpHighExposureSequencePressureStart)
        : cfg.DoubleUpSequencePressureStart;
    var shouldReleaseLowExposure = pressure >= cfg.DoubleUpSequencePressureStart
        && !isProjectedCloseCall
        && RollUnsalted(roundSeed, "double-up-suspense-release", cfg.DoubleUpSuspenseReleaseChance);
    result.DoubleUpPressureSamples++;
    result.DoubleUpPressureSum += pressure;
    result.MaxDoubleUpPressure = Math.Max(result.MaxDoubleUpPressure, pressure);
    if (projectedChainExposure >= cfg.SoftCapWarning)
    {
        result.DoubleUpHighExposureChains++;
    }

    if (pressure >= cfg.DoubleUpSequencePressureStart)
    {
        result.DoubleUpNormalPressureChains++;
    }

    if (pressure >= sequencePressureStart && (isProjectedCloseCall || !shouldReleaseLowExposure))
    {
        result.DoubleUpSequenceEligibleChains++;
    }

    var duDeck = MachinePolicy.BuildDoubleUpPlayDeck(
        FiveCardDrawEngine.BuildStandardDeck(),
        roundSeed,
        policyState.RoundsSinceLucky5Hit,
        policyState.NetSinceLastClose,
        policyMode,
        policyState,
        openingAmount,
        machineCreditBaseline);

    var session = Lucky5DoubleUpEngine.CreateSessionFromDeck(
        roundSeed,
        duDeck,
        openingAmount,
        machineCreditBaseline: machineCreditBaseline,
        options: new Lucky5DoubleUpOptions(MaxCreditLimit: Decimal.ToInt32(cfg.CloseThreshold)));

    var settledCredits = 0;
    var continuedAfterTakeHalf = false;
    var takeHalfUsed = false;

    for (var step = 0; step < 16; step++)
    {
        if (!takeHalfUsed && ShouldTakeHalf(behavior, roundSeed, step, openingAmount, bank.MachineCredits, session.CurrentAmount))
        {
            var half = session.CurrentAmount / 2;
            var remaining = session.CurrentAmount - half;
            settledCredits += half;
            result.TakeHalfEvents++;
            BankCredits(bank, half, result, BankEventChannel.DoubleUpTakeHalf);
            session = session with { CurrentAmount = remaining };
            takeHalfUsed = true;
        }

        if (ShouldCashoutDoubleUp(behavior, roundSeed, step, openingAmount, bank.PendingReset, takeHalfUsed, bank.MachineCredits, session.CurrentAmount))
        {
            settledCredits += session.CurrentAmount;
            result.DoubleUpCashoutSettlements++;
            BankCredits(bank, session.CurrentAmount, result, BankEventChannel.DoubleUp);
            return new DoubleUpChainResult(settledCredits - originalWinAmount, takeHalfUsed && continuedAfterTakeHalf);
        }

        while (session.SwitchCountInRound < session.Options.MaxSwitchesPerRound
            && ShouldSwitchDealer(behavior, roundSeed, step, session, sabotagePhase))
        {
            session = Lucky5DoubleUpEngine.SwitchDealer(session);
            result.DoubleUpDealerSwitches++;
            if (session.DealerCard.Rank == 5 && session.DealerCard.Suit == 'S')
            {
                result.LuckySwitchHits++;
            }

            if (takeHalfUsed)
            {
                continuedAfterTakeHalf = true;
            }

            if (!ShouldSwitchDealer(behavior, roundSeed, step + session.SwitchCountInRound, session, sabotagePhase))
            {
                break;
            }
        }

        var guess = ChooseGuess(session, behavior, roundSeed, step, sabotagePhase, result);
        var resolution = Lucky5DoubleUpEngine.ResolveGuess(session, guess);
        if (takeHalfUsed)
        {
            continuedAfterTakeHalf = true;
        }

        switch (resolution.Outcome)
        {
            case Lucky5DoubleUpOutcome.Win:
                result.DoubleUpResolutionWins++;
                session = resolution.Session;
                continue;

            case Lucky5DoubleUpOutcome.MachineClosed:
                result.DoubleUpResolutionWins++;
                result.DoubleUpMachineClosedResolutions++;
                settledCredits += resolution.CashoutCredits;
                BankCredits(bank, resolution.CashoutCredits, result, BankEventChannel.DoubleUp);
                return new DoubleUpChainResult(settledCredits - originalWinAmount, takeHalfUsed && continuedAfterTakeHalf);

            case Lucky5DoubleUpOutcome.SafeFail:
                result.DoubleUpResolutionLosses++;
                result.DoubleUpSafeFails++;
                settledCredits += resolution.CashoutCredits;
                BankCredits(bank, resolution.CashoutCredits, result, BankEventChannel.DoubleUp);
                return new DoubleUpChainResult(settledCredits - originalWinAmount, takeHalfUsed && continuedAfterTakeHalf);

            default:
                result.DoubleUpResolutionLosses++;
                return new DoubleUpChainResult(settledCredits - originalWinAmount, takeHalfUsed && continuedAfterTakeHalf);
        }
    }

    settledCredits += session.CurrentAmount;
    result.DoubleUpCashoutSettlements++;
    BankCredits(bank, session.CurrentAmount, result, BankEventChannel.DoubleUp);
    return new DoubleUpChainResult(settledCredits - originalWinAmount, takeHalfUsed && continuedAfterTakeHalf);
}

void BankCredits(SessionState bank, int amount, SimulationResult result, BankEventChannel channel)
{
    if (amount <= 0)
    {
        return;
    }

    var before = bank.MachineCredits;
    bank.MachineCredits += amount;
    result.MaxMachineCredits = Math.Max(result.MaxMachineCredits, bank.MachineCredits);
    result.LargestBankedCreditEvent = Math.Max(result.LargestBankedCreditEvent, amount);
    if (before < cfg.SoftCapWarning && bank.MachineCredits >= cfg.SoftCapWarning)
    {
        result.SoftCapWarningTouches++;
        result.RecordSoftCapTouch(channel);
    }

    if (before < cfg.SoftCapHard && bank.MachineCredits >= cfg.SoftCapHard)
    {
        result.HardCapCloseCalls++;
        result.RecordHardCapTouch(channel);
    }

    if (before < cfg.CloseThreshold * 0.95m && bank.MachineCredits >= cfg.CloseThreshold * 0.95m)
    {
        result.CriticalCloseCalls++;
        result.RecordCriticalCloseCall(channel);
    }

    if (!bank.PendingReset && before < cfg.CloseThreshold && bank.MachineCredits >= cfg.CloseThreshold)
    {
        bank.PendingReset = true;
        result.MachineCloses40M++;
        result.RecordMachineClose(channel);
    }
}

static bool ShouldEnterDoubleUp(PlayerBehavior behavior, ulong seed, int payout, decimal machineCredits, bool sabotagePhase)
{
    return behavior switch
    {
        PlayerBehavior.ConservativeCollectFirst => false,
        PlayerBehavior.Balanced => machineCredits + payout < EngineConfig.Default.CloseThreshold
            && Roll(seed, "accept-balanced", payout, 0.78m),
        PlayerBehavior.AggressiveCabinetClosing => machineCredits + payout < 50_000_000m || payout < 2_000_000,
        PlayerBehavior.CounterplaySabotage => sabotagePhase
            || machineCredits + payout < 50_000_000m
            || payout < 2_000_000,
        _ => false
    };
}

static bool ShouldTakeHalf(PlayerBehavior behavior, ulong seed, int step, int openingAmount, decimal machineCredits, int currentAmount)
{
    return behavior switch
    {
        PlayerBehavior.Balanced => currentAmount >= Math.Max(openingAmount * 4, 500_000)
            && machineCredits + currentAmount < EngineConfig.Default.CloseThreshold
            && Roll(seed, "take-half-balanced", step, 0.35m),
        PlayerBehavior.AggressiveCabinetClosing => currentAmount >= Math.Max(openingAmount * 8, 1_000_000)
            && machineCredits + currentAmount >= EngineConfig.Default.CloseThreshold * 0.65m
            && Roll(seed, "take-half-aggressive", step, 0.60m),
        PlayerBehavior.CounterplaySabotage => currentAmount >= Math.Max(openingAmount * 10, 1_500_000)
            && machineCredits + currentAmount >= EngineConfig.Default.CloseThreshold * 0.70m
            && Roll(seed, "take-half-counterplay", step, 0.45m),
        _ => false
    };
}

static bool ShouldCashoutDoubleUp(
    PlayerBehavior behavior,
    ulong seed,
    int step,
    int openingAmount,
    bool machineAlreadyClosed,
    bool takeHalfUsed,
    decimal machineCredits,
    int currentAmount)
{
    if (machineAlreadyClosed && behavior != PlayerBehavior.AggressiveCabinetClosing)
    {
        return true;
    }

    return behavior switch
    {
        PlayerBehavior.Balanced => step > 0 && (
            takeHalfUsed
            || currentAmount >= Math.Max(openingAmount * 2, 250_000)
            || Roll(seed, "cashout-balanced", step, 0.70m)),
        PlayerBehavior.AggressiveCabinetClosing => (machineCredits + currentAmount >= EngineConfig.Default.CloseThreshold && step > 0)
            || currentAmount >= Math.Max(openingAmount * 32, 8_000_000)
            || step >= 7,
        PlayerBehavior.CounterplaySabotage => (machineCredits + currentAmount >= EngineConfig.Default.CloseThreshold && step > 0)
            || currentAmount >= Math.Max(openingAmount * 40, 10_000_000)
            || step >= 8,
        _ => true
    };
}

static bool ShouldSwitchDealer(PlayerBehavior behavior, ulong seed, int step, Lucky5DoubleUpSession session, bool sabotagePhase)
{
    if (sabotagePhase)
    {
        return false;
    }

    var dealerRank = session.DealerCard.Rank;
    return behavior switch
    {
        PlayerBehavior.Balanced => session.SwitchCountInRound == 0
            && dealerRank is 7 or 8
            && Roll(seed, "switch-balanced", step, 0.25m),
        PlayerBehavior.AggressiveCabinetClosing => dealerRank is >= 6 and <= 9
            && Roll(seed, "switch-aggressive", step + session.SwitchCountInRound, session.SwitchCountInRound == 0 ? 0.60m : 0.35m),
        PlayerBehavior.CounterplaySabotage => dealerRank is >= 6 and <= 9
            && Roll(seed, "switch-counterplay", step + session.SwitchCountInRound, session.SwitchCountInRound == 0 ? 0.65m : 0.40m),
        _ => false
    };
}

static bool ShouldCashOutSession(PlayerBehavior behavior, SessionState session)
{
    return behavior switch
    {
        PlayerBehavior.ConservativeCollectFirst => session.MachineCredits >= session.SessionCashIn * 2m,
        PlayerBehavior.Balanced => session.MachineCredits >= Math.Max(session.SessionCashIn * 2.5m, 2_000_000m)
            && session.MachineCredits < EngineConfig.Default.CloseThreshold * 0.85m,
        _ => false
    };
}

static BigSmallGuess ChooseGuess(
    Lucky5DoubleUpSession session,
    PlayerBehavior behavior,
    ulong seed,
    int step,
    bool sabotagePhase,
    SimulationResult result)
{
    var optimal = session.DealerCard.Rank <= 8 ? BigSmallGuess.Big : BigSmallGuess.Small;
    if (behavior != PlayerBehavior.CounterplaySabotage || !sabotagePhase)
    {
        return optimal;
    }

    BigSmallGuess guess;
    if (session.DealerCard.Rank is >= 7 and <= 9 && Roll(seed, "counterplay-random-middle", step, 0.35m))
    {
        guess = Roll(seed, "counterplay-random-side", step, 0.50m)
            ? BigSmallGuess.Big
            : BigSmallGuess.Small;
    }
    else
    {
        guess = optimal == BigSmallGuess.Big ? BigSmallGuess.Small : BigSmallGuess.Big;
    }

    if (guess != optimal)
    {
        result.CounterplayWrongWayDoubleUpGuesses++;
        if (session.DealerCard.Rank <= 3 && guess == BigSmallGuess.Small)
        {
            result.CounterplaySmallOnLowDealerGuesses++;
        }
    }

    return guess;
}

static bool Roll(ulong seed, string stream, int salt, decimal threshold)
{
    var rng = new SplitMix64Rng(DeterministicSeed.Derive(seed, stream, salt));
    return (decimal)rng.NextUnit() < threshold;
}

static bool RollUnsalted(ulong seed, string stream, decimal threshold)
{
    var rng = new SplitMix64Rng(DeterministicSeed.Derive(seed, stream));
    return (decimal)rng.NextUnit() < threshold;
}

bool[] ComputeOptimalHolds(CleanRoomCard[] hand)
{
    var advisedIndexes = FiveCardDrawEngine.ComputeAdvisedHolds(hand);
    if (behavior != PlayerBehavior.CounterplaySabotage || !sabotagePhase)
    {
        return MaskFromIndexes(advisedIndexes);
    }

    result.CounterplaySabotageRounds++;
    var initialEvaluation = FiveCardDrawEngine.EvaluateHand(hand);
    if (FiveCardDrawEngine.ResolvePayout(initialEvaluation, Bet, paytable) > 0)
    {
        result.CounterplayBrokenPayingHands++;
    }

    if (advisedIndexes.Length == 0)
    {
        var trashMask = new bool[5];
        if (Roll(seed, "counterplay-trash-hold", roundIndex, 0.65m))
        {
            var trashIndex = Enumerable.Range(0, hand.Length)
                .OrderBy(index => hand[index].Rank == 14 ? 20 : hand[index].Rank)
                .ThenBy(index => index)
                .First();
            trashMask[trashIndex] = true;
            result.CounterplayTrashHolds++;
        }

        return trashMask;
    }

    result.CounterplayBrokenAdvisedHoldRounds++;
    var advisedSet = advisedIndexes.ToHashSet();
    var breakMask = new bool[5];
    var offAdviceIndex = Enumerable.Range(0, hand.Length)
        .Where(index => !advisedSet.Contains(index))
        .OrderBy(index => hand[index].Rank == 14 ? 20 : hand[index].Rank)
        .ThenBy(index => index)
        .Cast<int?>()
        .FirstOrDefault();

    if (offAdviceIndex.HasValue && Roll(seed, "counterplay-off-advice-hold", roundIndex, 0.45m))
    {
        breakMask[offAdviceIndex.Value] = true;
        result.CounterplayTrashHolds++;
    }

    return breakMask;
}

static int AssessCounterplay(CleanRoomCard[] hand, int[] holdIndexes)
{
    var advised = FiveCardDrawEngine.ComputeAdvisedHolds(hand);
    var advisedSet = advised.ToHashSet();
    var actualSet = holdIndexes.Where(index => index >= 0 && index < 5).ToHashSet();
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

static void UpdateCounterplay(SessionState session, int delta, SimulationResult result)
{
    var before = session.CounterplayScore;
    session.CounterplayScore = Math.Clamp(session.CounterplayScore + delta, 0, 10);
    result.MaxCounterplayScore = Math.Max(result.MaxCounterplayScore, session.CounterplayScore);
    if (session.CounterplayScore > before)
    {
        result.CounterplayScoreIncreases++;
    }
}

static int[] HoldIndexesFromMask(bool[] holdMask)
    => holdMask.Select((held, index) => (held, index))
        .Where(item => item.held)
        .Select(item => item.index)
        .ToArray();

static bool[] MaskFromIndexes(int[] indexes)
{
    var mask = new bool[5];
    foreach (var index in indexes)
    {
        if (index >= 0 && index < mask.Length)
        {
            mask[index] = true;
        }
    }

    return mask;
}

static bool IsCounterplaySabotagePhase(PlayerBehavior behavior, MachineLedgerState ledger, SessionState session)
{
    if (behavior != PlayerBehavior.CounterplaySabotage)
    {
        return false;
    }

    if (session.MachineCredits >= EngineConfig.Default.CloseThreshold * 0.70m)
    {
        return false;
    }

    if (ledger.ConsecutiveLosses >= EngineConfig.Default.StreakHardThreshold + 5)
    {
        return false;
    }

    if (ledger.RoundsSinceMediumWin >= EngineConfig.Default.MediumWinDroughtThreshold)
    {
        return false;
    }

    return session.CounterplayScore < 7 || ledger.ConsecutiveLosses < EngineConfig.Default.StreakHardThreshold;
}

void ApplyJackpotContributions(MachineLedgerState ledger)
{
    // Only the currently-starred Four-of-a-Kind jackpot accrues this round,
    // mirroring GameService.ApplyJackpotContributions so the simulation
    // reflects production jackpot dynamics faithfully.
    if (ledger.ActiveFourOfAKindSlot == 0)
    {
        ledger.JackpotFourOfAKindA = Math.Min(ledger.JackpotFourOfAKindA + cfg.JackpotFourOfAKindContribution, cfg.JackpotFourOfAKindCap);
    }
    else
    {
        ledger.JackpotFourOfAKindB = Math.Min(ledger.JackpotFourOfAKindB + cfg.JackpotFourOfAKindContribution, cfg.JackpotFourOfAKindCap);
    }
    ledger.JackpotFullHouse = Math.Min(ledger.JackpotFullHouse + cfg.JackpotFullHouseContribution, cfg.JackpotFullHouseCap);
    ledger.JackpotStraightFlush = Math.Min(ledger.JackpotStraightFlush + cfg.JackpotStraightFlushContribution, cfg.JackpotStraightFlushCap);
    ledger.JackpotKent = Math.Min(ledger.JackpotKent + cfg.JackpotKentContribution, cfg.JackpotKentCap);
}

static void PrintEnhancedSummary(string label, SimulationResult result)
{
    Console.WriteLine($"{label,-32} | RTP {result.TotalRtp:P2} | Base {result.BaseRtp:P2} | Jackpot {result.JackpotRtp:P2} | DU {result.DoubleUpRtp:P2}");
    Console.WriteLine($"  Paying spins {result.DirectPayingSpinFrequency:P2} | Medium+ {result.MediumOrBetterFrequency:P2} | DU offer/win {result.OfferRateOnWinningRounds:P2} | Accept {result.AcceptRate:P2}");
    Console.WriteLine($"  Entered DU gain {result.RealizedIncrementalGainPerEnteredChain:P2} of trigger win | Avg scale {result.AveragePayoutScale:F3} | 40M closes {result.MachineCloses40M:N0} | 50M take-half+continue {result.Over50MViaTakeHalfContinuation:N0}");
    Console.WriteLine($"  Jackpots {result.JackpotHits:N0} | Largest jackpot {result.LargestJackpot:N0} | Largest bank event {result.LargestBankedCreditEvent:N0} | Max credits {result.MaxMachineCredits:N0}");
    Console.WriteLine($"  Jackpot mix: FH {result.FullHouseJackpots:N0} | 4K-A {result.FourOfAKindAJackpots:N0} | 4K-B {result.FourOfAKindBJackpots:N0} | SF {result.StraightFlushJackpots:N0} | Kent {result.KentJackpots:N0}");
    Console.WriteLine($"  DU outcomes: win {result.DoubleUpResolutionWins:N0} | lose {result.DoubleUpResolutionLosses:N0} | safe {result.DoubleUpSafeFails:N0} | close {result.DoubleUpMachineClosedResolutions:N0} | switches {result.DoubleUpDealerSwitches:N0} | take-half {result.TakeHalfEvents:N0}");
    Console.WriteLine($"  Ace effects: base hands {result.AceMultiplierHands:N0} | unscaled lift {result.AceMultiplierUnscaledCredits:N0} | DU opening lift {result.AceDoubleUpOpeningBoostCredits:N0}");
    Console.WriteLine($"  Close suspense: >=28M {result.SoftCapWarningTouches:N0} | >=35M {result.HardCapCloseCalls:N0} | >=38M {result.CriticalCloseCalls:N0}");
    Console.WriteLine($"  DU pressure: seq {result.DoubleUpSequenceEligibleChains:N0}/{result.DoubleUpPressureSamples:N0} | high exposure {result.DoubleUpHighExposureChains:N0} | normal pressure {result.DoubleUpNormalPressureChains:N0} | avg {result.AverageDoubleUpPressure:F3} | max {result.MaxDoubleUpPressure:F3}");
    
    // Enhanced telemetry
    Console.WriteLine($"  Warmup activations: {result.WarmupActivations:N0} | Pity activations: {result.PityActivations:N0} | Crisis activations: {result.CrisisActivations:N0}");
    Console.WriteLine($"  Jackpot leak adjustments: {result.JackpotLeakAdjustments:F4} | Double-up leak adjustments: {result.DoubleUpLeakAdjustments:F4}");
    
    // RTP windows (1k, 5k, 50k if available)
    if (result.RtpWindows.Count > 0)
    {
        var window1k = result.RtpWindows.Count >= 1 ? result.RtpWindows[0] : 0m;
        var window5k = result.RtpWindows.Count >= 5 ? result.RtpWindows[4] : 0m;
        var window50k = result.RtpWindows.Count >= 50 ? result.RtpWindows[49] : 0m;
        Console.WriteLine($"  RTP windows: 1k {window1k:P2} | 5k {window5k:P2} | 50k {window50k:P2}");
    }
}

void PrintVarianceReport()
{
    Console.WriteLine();
    Console.WriteLine("--- Variance / Suspense Report ---");

    var samples10k = Enumerable.Range(0, 9)
        .Select(sample => RunSimulation(10_000, PlayerBehavior.Balanced, sample))
        .OrderBy(result => result.TotalRtp)
        .ToArray();
    var median10k = samples10k[samples10k.Length / 2];
    var min10k = samples10k.First();
    var max10k = samples10k.Last();
    Console.WriteLine($"Balanced 10k sample band       | min {min10k.TotalRtp:P2} | median {median10k.TotalRtp:P2} | max {max10k.TotalRtp:P2}");

    var balanced100k = RunSimulation(100_000, PlayerBehavior.Balanced, 0, true);
    PrintEnhancedSummary("Balanced 100k", balanced100k);

    var aggressive200k = RunSimulation(200_000, PlayerBehavior.AggressiveCabinetClosing, 0, true);
    PrintEnhancedSummary("Aggressive close 200k", aggressive200k);

    var counterplay200k = RunSimulation(200_000, PlayerBehavior.CounterplaySabotage, 0, true);
    PrintEnhancedSummary("Counterplay sabotage 200k", counterplay200k);
}

static string DescribeBehavior(PlayerBehavior behavior) => behavior switch
{
    PlayerBehavior.ConservativeCollectFirst => "Conservative collect-first",
    PlayerBehavior.Balanced => "Balanced",
    PlayerBehavior.AggressiveCabinetClosing => "Aggressive cabinet-closing",
    PlayerBehavior.CounterplaySabotage => "Counterplay sabotage / hot-state hunt",
    _ => behavior.ToString()
};

static bool TryParseBehavior(string value, out PlayerBehavior behavior)
{
    behavior = value.Trim().ToLowerInvariant() switch
    {
        "conservative" or "collect" or "collect-first" => PlayerBehavior.ConservativeCollectFirst,
        "balanced" => PlayerBehavior.Balanced,
        "aggressive" or "close" or "cabinet-closing" => PlayerBehavior.AggressiveCabinetClosing,
        "counterplay" or "sabotage" or "intentional-lose" or "glitch-hunt" or "exploit" => PlayerBehavior.CounterplaySabotage,
        _ => PlayerBehavior.Balanced
    };

    return behavior != PlayerBehavior.Balanced
        || value.Trim().Equals("balanced", StringComparison.OrdinalIgnoreCase);
}

enum PlayerBehavior
{
    ConservativeCollectFirst = 0,
    Balanced = 1,
    AggressiveCabinetClosing = 2,
    CounterplaySabotage = 3
}

enum BankEventChannel
{
    BaseGame = 0,
    Jackpot = 1,
    DoubleUp = 2,
    DoubleUpTakeHalf = 3
}

enum JackpotHitKind
{
    None = 0,
    FullHouse = 1,
    FourOfAKindA = 2,
    FourOfAKindB = 3,
    StraightFlush = 4,
    Kent = 5
}

sealed class SessionState
{
    private const decimal DefaultStartingSessionCredits = 1_000_000m;

    public decimal MachineCredits { get; set; }
    public decimal SessionCashIn { get; set; }
    public bool PendingReset { get; set; }
    public int CounterplayScore { get; set; }

    public void StartNewSession()
    {
        MachineCredits = DefaultStartingSessionCredits;
        SessionCashIn = DefaultStartingSessionCredits;
        PendingReset = false;
        CounterplayScore = 0;
    }

    public void BeginRound()
    {
        // No-op placeholder for symmetry with the live flow.
    }
}

sealed class SimulationResult(PlayerBehavior behavior, int rounds)
{
    public PlayerBehavior Behavior { get; } = behavior;
    public int Rounds { get; } = rounds;
    public decimal TotalIn { get; set; }
    public decimal ScaledBaseOut { get; set; }
    public decimal JackpotOverlayOut { get; set; }
    public decimal DoubleUpOverlayOut { get; set; }
    public decimal PreDoubleUpWinCredits { get; set; }
    public decimal EnteredTriggerCredits { get; set; }
    public decimal PayoutScaleSum { get; set; }
    public int PayoutScaleSamples { get; set; }
    public int DirectPayingSpins { get; set; }
    public int MediumOrBetterSpins { get; set; }
    public int EligibleWinningRounds { get; set; }
    public int OfferedWinningRounds { get; set; }
    public int EnteredDoubleUpChains { get; set; }
    public int DoubleUpResolutionWins { get; set; }
    public int DoubleUpResolutionLosses { get; set; }
    public int LuckySwitchHits { get; set; }
    public int JackpotHits { get; set; }
    public int MachineCloses40M { get; set; }
    public int Over50MViaTakeHalfContinuation { get; set; }
    public int SessionsStarted { get; set; }
    public int SoftCapWarningTouches { get; set; }
    public int HardCapCloseCalls { get; set; }
    public int CriticalCloseCalls { get; set; }
    public int DoubleUpPressureSamples { get; set; }
    public int DoubleUpSequenceEligibleChains { get; set; }
    public int DoubleUpHighExposureChains { get; set; }
    public int DoubleUpNormalPressureChains { get; set; }
    public decimal MaxMachineCredits { get; set; }
    public decimal LargestJackpot { get; set; }
    public decimal LargestBankedCreditEvent { get; set; }
    public decimal DoubleUpPressureSum { get; set; }
    public decimal MaxDoubleUpPressure { get; set; }
    public decimal FinalObservedRtp { get; set; }
    public decimal FinalBaseRtp { get; set; }
    public decimal FinalJackpotRtp { get; set; }
    public decimal FinalDoubleUpRtp { get; set; }
    public int AceMultiplierHands { get; set; }
    public decimal AceMultiplierUnscaledCredits { get; set; }
    public decimal AceDoubleUpOpeningBoostCredits { get; set; }
    public int FullHouseJackpots { get; set; }
    public int FourOfAKindAJackpots { get; set; }
    public int FourOfAKindBJackpots { get; set; }
    public int StraightFlushJackpots { get; set; }
    public int KentJackpots { get; set; }
    public int DoubleUpSafeFails { get; set; }
    public int DoubleUpMachineClosedResolutions { get; set; }
    public int DoubleUpCashoutSettlements { get; set; }
    public int DoubleUpDealerSwitches { get; set; }
    public int TakeHalfEvents { get; set; }
    public int PolicyColdRounds { get; set; }
    public int PolicyNeutralRounds { get; set; }
    public int PolicyHotRounds { get; set; }
    public int MachineClosesFromBaseGame { get; set; }
    public int MachineClosesFromJackpot { get; set; }
    public int MachineClosesFromDoubleUp { get; set; }
    public int MachineClosesFromTakeHalf { get; set; }
    public int SoftCapTouchesFromBaseGame { get; set; }
    public int SoftCapTouchesFromJackpot { get; set; }
    public int SoftCapTouchesFromDoubleUp { get; set; }
    public int SoftCapTouchesFromTakeHalf { get; set; }
    public int HardCapTouchesFromBaseGame { get; set; }
    public int HardCapTouchesFromJackpot { get; set; }
    public int HardCapTouchesFromDoubleUp { get; set; }
    public int HardCapTouchesFromTakeHalf { get; set; }
    public int CriticalTouchesFromBaseGame { get; set; }
    public int CriticalTouchesFromJackpot { get; set; }
    public int CriticalTouchesFromDoubleUp { get; set; }
    public int CriticalTouchesFromTakeHalf { get; set; }
    public int CounterplaySabotageRounds { get; set; }
    public int CounterplayBrokenAdvisedHoldRounds { get; set; }
    public int CounterplayBrokenPayingHands { get; set; }
    public int CounterplayTrashHolds { get; set; }
    public int CounterplayWrongWayDoubleUpGuesses { get; set; }
    public int CounterplaySmallOnLowDealerGuesses { get; set; }
    public int CounterplayColdOverrides { get; set; }
    public int CounterplayHotRounds { get; set; }
    public int CounterplayHotTransitions { get; set; }
    public int CounterplayHotAfterSabotageRounds { get; set; }
    public int CounterplayScoreIncreases { get; set; }
    public int MaxCounterplayScore { get; set; }
    public bool CounterplayWasHotLastRound { get; set; }
    
    // Enhanced telemetry fields
    public int WarmupActivations { get; set; }
    public int PityActivations { get; set; }
    public int CrisisActivations { get; set; }
    public decimal JackpotLeakAdjustments { get; set; }
    public decimal DoubleUpLeakAdjustments { get; set; }
    public List<decimal> RtpWindows { get; set; } = new();

    public void RecordPolicyMode(PolicyDistributionMode mode)
    {
        switch (mode)
        {
            case PolicyDistributionMode.Cold:
                PolicyColdRounds++;
                break;
            case PolicyDistributionMode.Hot:
                PolicyHotRounds++;
                break;
            default:
                PolicyNeutralRounds++;
                break;
        }
    }

    public void RecordJackpot(JackpotHitKind kind, decimal amount)
    {
        JackpotHits++;
        LargestJackpot = Math.Max(LargestJackpot, amount);
        switch (kind)
        {
            case JackpotHitKind.FullHouse:
                FullHouseJackpots++;
                break;
            case JackpotHitKind.FourOfAKindA:
                FourOfAKindAJackpots++;
                break;
            case JackpotHitKind.FourOfAKindB:
                FourOfAKindBJackpots++;
                break;
            case JackpotHitKind.StraightFlush:
                StraightFlushJackpots++;
                break;
            case JackpotHitKind.Kent:
                KentJackpots++;
                break;
        }
    }

    public void RecordSoftCapTouch(BankEventChannel channel) => IncrementByChannel(
        channel,
        () => SoftCapTouchesFromBaseGame++,
        () => SoftCapTouchesFromJackpot++,
        () => SoftCapTouchesFromDoubleUp++,
        () => SoftCapTouchesFromTakeHalf++);

    public void RecordHardCapTouch(BankEventChannel channel) => IncrementByChannel(
        channel,
        () => HardCapTouchesFromBaseGame++,
        () => HardCapTouchesFromJackpot++,
        () => HardCapTouchesFromDoubleUp++,
        () => HardCapTouchesFromTakeHalf++);

    public void RecordCriticalCloseCall(BankEventChannel channel) => IncrementByChannel(
        channel,
        () => CriticalTouchesFromBaseGame++,
        () => CriticalTouchesFromJackpot++,
        () => CriticalTouchesFromDoubleUp++,
        () => CriticalTouchesFromTakeHalf++);

    public void RecordMachineClose(BankEventChannel channel) => IncrementByChannel(
        channel,
        () => MachineClosesFromBaseGame++,
        () => MachineClosesFromJackpot++,
        () => MachineClosesFromDoubleUp++,
        () => MachineClosesFromTakeHalf++);

    private static void IncrementByChannel(
        BankEventChannel channel,
        Action baseGame,
        Action jackpot,
        Action doubleUp,
        Action takeHalf)
    {
        switch (channel)
        {
            case BankEventChannel.Jackpot:
                jackpot();
                break;
            case BankEventChannel.DoubleUp:
                doubleUp();
                break;
            case BankEventChannel.DoubleUpTakeHalf:
                takeHalf();
                break;
            default:
                baseGame();
                break;
        }
    }

    public decimal TotalRtp => TotalIn <= 0m ? 0m : decimal.Round((ScaledBaseOut + JackpotOverlayOut + DoubleUpOverlayOut) / TotalIn, 4);
    public decimal BaseRtp => TotalIn <= 0m ? 0m : decimal.Round(ScaledBaseOut / TotalIn, 4);
    public decimal JackpotRtp => TotalIn <= 0m ? 0m : decimal.Round(JackpotOverlayOut / TotalIn, 4);
    public decimal DoubleUpRtp => TotalIn <= 0m ? 0m : decimal.Round(DoubleUpOverlayOut / TotalIn, 4);
    public decimal DirectPayingSpinFrequency => Rounds <= 0 ? 0m : decimal.Round((decimal)DirectPayingSpins / Rounds, 4);
    public decimal MediumOrBetterFrequency => Rounds <= 0 ? 0m : decimal.Round((decimal)MediumOrBetterSpins / Rounds, 4);
    public decimal OfferRateOnWinningRounds => EligibleWinningRounds <= 0 ? 0m : decimal.Round((decimal)OfferedWinningRounds / EligibleWinningRounds, 4);
    public decimal AcceptRate => OfferedWinningRounds <= 0 ? 0m : decimal.Round((decimal)EnteredDoubleUpChains / OfferedWinningRounds, 4);
    public decimal RealizedIncrementalGainPerEnteredChain => EnteredTriggerCredits <= 0m ? 0m : decimal.Round(DoubleUpOverlayOut / EnteredTriggerCredits, 4);
    public decimal AveragePayoutScale => PayoutScaleSamples <= 0 ? 0m : decimal.Round(PayoutScaleSum / PayoutScaleSamples, 4);
    public decimal AverageDoubleUpPressure => DoubleUpPressureSamples <= 0 ? 0m : decimal.Round(DoubleUpPressureSum / DoubleUpPressureSamples, 4);
}

readonly record struct JackpotResolution(decimal TotalPayout, JackpotHitKind Kind);

readonly record struct DoubleUpChainResult(decimal Delta, bool ContinuedAfterTakeHalf);
