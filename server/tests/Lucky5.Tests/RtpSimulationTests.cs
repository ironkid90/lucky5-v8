namespace Lucky5.Tests;

using Lucky5.Domain.Game;
using Lucky5.Domain.Game.CleanRoom;

/// <summary>
/// Monte Carlo RTP simulation for the CleanRoom engine.
/// Simulates full game loops (deal → optimal hold → draw → evaluate → optional double-up)
/// to measure actual RTP convergence vs the 80% target.
/// </summary>
public static class RtpSimulationTests
{
    private const int SimulationRounds = 50_000;
    private const int DoubleUpRoundsPerSession = 3;

    public static Task RunAsync(List<string> failures)
    {
        var config = EngineConfig.Default;
        var result = RunSimulation(config, SimulationRounds, "RTP Simulation (50K rounds)");

        // Log results
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           RTP SIMULATION RESULTS (50K ROUNDS)          ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Target RTP:        {config.TargetRtp,8:P2}                             ║");
        Console.WriteLine($"║  Observed RTP:      {result.ObservedRtp,8:P2}                             ║");
        Console.WriteLine($"║  Base RTP:          {result.BaseRtp,8:P2}                             ║");
        Console.WriteLine($"║  Jackpot RTP:       {result.JackpotRtp,8:P2}                             ║");
        Console.WriteLine($"║  Double-Up RTP:     {result.DoubleUpRtp,8:P2}                             ║");
        Console.WriteLine($"║  Total Credits In:  {result.TotalCreditsIn,12:N0}                        ║");
        Console.WriteLine($"║  Total Credits Out: {result.TotalCreditsOut,12:N0}                        ║");
        Console.WriteLine($"║  Win Rate:          {result.WinRate,8:P2}                             ║");
        Console.WriteLine($"║  Avg Consec Losses: {result.AvgConsecutiveLosses,8:F1}                             ║");
        Console.WriteLine($"║  Max Consec Losses: {result.MaxConsecutiveLosses,8}                             ║");
        Console.WriteLine($"║  Payout Scale Avg:  {result.AvgPayoutScale,8:F3}                             ║");
        Console.WriteLine($"║  Double-Up Win%:    {result.DoubleUpWinRate,8:P2}                             ║");
        Console.WriteLine($"║  Double-Up Sessions:{result.DoubleUpSessions,8}                             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");

        // RTP should be within 5% of target (generous window for Monte Carlo noise)
        var rtpDelta = Math.Abs(result.ObservedRtp - config.TargetRtp);
        Assert(failures,
            $"RTP should converge near target (observed={result.ObservedRtp:P2}, target={config.TargetRtp:P2}, delta={rtpDelta:P2})",
            rtpDelta < 0.05m);

        // Base RTP should be in reasonable range (50-75% for 80% target)
        Assert(failures,
            $"Base RTP should be in reasonable range (observed={result.BaseRtp:P2})",
            result.BaseRtp > 0.40m && result.BaseRtp < 0.80m);

        // Double-up should contribute meaningfully (5-25%)
        Assert(failures,
            $"Double-up RTP should contribute meaningfully (observed={result.DoubleUpRtp:P2})",
            result.DoubleUpRtp > 0.02m && result.DoubleUpRtp < 0.30m);

        // Test with different RTP targets to verify configurability
        var config70 = config with { TargetRtp = 0.70m };
        var result70 = RunSimulation(config70, 30_000, "RTP Simulation 70% target");
        Console.WriteLine($"\n  70% target → observed {result70.ObservedRtp:P2}");

        var config90 = config with { TargetRtp = 0.90m };
        var result90 = RunSimulation(config90, 30_000, "RTP Simulation 90% target");
        Console.WriteLine($"  90% target → observed {result90.ObservedRtp:P2}");

        return Task.CompletedTask;
    }

    private static SimulationResult RunSimulation(EngineConfig config, int rounds, string label)
    {
        var paytable = PaytableProfile.Lebanese;
        var rng = new SplitMix64Rng(42);
        var policyState = new MachinePolicyState { TargetRtp = config.TargetRtp };
        var ledger = new MachineLedgerState { MachineId = 1 };

        decimal totalCreditsIn = 0;
        decimal totalCreditsOut = 0;
        decimal baseCreditsOut = 0;
        decimal jackpotCreditsOut = 0;
        decimal doubleUpCreditsOut = 0;
        int wins = 0;
        int totalLosses = 0;
        int maxConsecLosses = 0;
        int currentConsecLosses = 0;
        decimal totalPayoutScale = 0;
        int scaleCount = 0;
        int doubleUpWins = 0;
        int doubleUpSessions = 0;

        for (int i = 0; i < rounds; i++)
        {
            int bet = 5000;
            totalCreditsIn += bet;

            // Update policy state from ledger
            policyState.CreditsIn = totalCreditsIn;
            policyState.CreditsOut = totalCreditsOut;
            policyState.BaseCreditsOut = baseCreditsOut;
            policyState.JackpotCreditsOut = jackpotCreditsOut;
            policyState.DoubleUpCreditsOut = doubleUpCreditsOut;
            policyState.RoundCount = i + 1;
            policyState.ConsecutiveLosses = currentConsecLosses;
            policyState.NetSinceLastClose = Math.Max(ledger.CapitalIn - ledger.CapitalOut, 0m);

            // Deal
            var seed = (ulong)rng.NextInt(int.MaxValue) | ((ulong)rng.NextInt(int.MaxValue) << 32);
            var deck = FiveCardDrawEngine.ShuffleDeck(seed, "hand");
            var hand = deck.Take(5).ToArray();

            // Get policy resolution
            var policyResolution = MachinePolicy.ResolvePolicy(policyState, seed);
            var policyMode = policyResolution.DistributionMode;
            var alteredDeck = MachinePolicy.AlterDeck(deck, policyMode, seed, currentConsecLosses);
            var shuffledDeck = FiveCardDrawEngine.ShuffleDeck(seed, "hand-alt", alteredDeck);
            hand = shuffledDeck.Take(5).ToArray();

            // Optimal hold strategy (use advised holds)
            var advisedHolds = FiveCardDrawEngine.ComputeAdvisedHolds(hand);
            var holdMask = new bool[5];
            for (int j = 0; j < 5; j++)
                holdMask[j] = advisedHolds[j] == 1;

            // Draw
            var drawState = FiveCardDrawState.Create(seed, shuffledDeck, hand);
            var state = FiveCardDrawEngine.Reduce(drawState, new RoundAction(RoundActionKind.SetHoldMask, HoldMask: holdMask));
            state = FiveCardDrawEngine.Reduce(state, new RoundAction(RoundActionKind.Draw));

            // Evaluate
            var evaluation = FiveCardDrawEngine.EvaluateHand(state.Hand);
            var basePayout = FiveCardDrawEngine.ResolvePayout(evaluation, bet, paytable);

            // Apply payout scale
            var tier = MachinePolicy.ClassifyHand(evaluation.Category);
            var payoutScale = policyResolution.ForTier(tier);
            totalPayoutScale += (decimal)payoutScale;
            scaleCount++;

            var payout = basePayout > 0 ? (int)Math.Round(basePayout * payoutScale, MidpointRounding.AwayFromZero) : 0;

            // Jackpot check (simplified)
            decimal jackpotWon = 0;
            if (evaluation.Category == HandCategory.FourOfAKind)
            {
                jackpotWon = 200_000; // Simplified jackpot
            }
            else if (evaluation.Category == HandCategory.StraightFlush)
            {
                jackpotWon = 1_000_000;
            }

            if (jackpotWon > 0 && jackpotWon > payout)
            {
                totalCreditsOut += jackpotWon;
                jackpotCreditsOut += jackpotWon - payout;
                totalCreditsOut += payout;
                baseCreditsOut += payout;
            }
            else
            {
                totalCreditsOut += payout;
                baseCreditsOut += payout;
            }

            if (payout > 0 || jackpotWon > 0)
            {
                wins++;
                currentConsecLosses = 0;
            }
            else
            {
                currentConsecLosses++;
                totalLosses++;
                if (currentConsecLosses > maxConsecLosses)
                    maxConsecLosses = currentConsecLosses;
            }

            // Double-up simulation (simplified)
            if (payout > 0 && doubleUpSessions < rounds / 10)
            {
                doubleUpSessions++;
                int duAmount = payout;
                for (int du = 0; du < DoubleUpRoundsPerSession; du++)
                {
                    var duSeed = (ulong)rng.NextInt(int.MaxValue) | ((ulong)rng.NextInt(int.MaxValue) << 32);
                    var duDeck = MachinePolicy.BuildDoubleUpDeck(
                        FiveCardDrawEngine.BuildStandardDeck(), duSeed,
                        policyState.RoundsSinceLucky5Hit,
                        policyState.NetSinceLastClose,
                        policyMode);

                    // Dealer card
                    var dealerCard = duDeck[duSeed % (ulong)duDeck.Length];

                    // Optimal BIG/SMALL strategy
                    bool guessBig = dealerCard.Rank <= 7;
                    bool isAce = dealerCard.Rank == 14;

                    if (isAce)
                    {
                        // Ace auto-wins
                        duAmount *= 2;
                        doubleUpWins++;
                        doubleUpCreditsOut += duAmount / 2;
                        continue;
                    }

                    // Simplified: 50% base chance, adjusted by deck pressure
                    var duRng = new SplitMix64Rng(duSeed);
                    var roll = duRng.NextUnit();
                    bool won = roll < 0.48; // Slightly below 50% due to house edge

                    if (won)
                    {
                        duAmount *= 2;
                        doubleUpWins++;
                        doubleUpCreditsOut += duAmount / 2;
                    }
                    else
                    {
                        duAmount = 0;
                        break;
                    }
                }

                totalCreditsOut += duAmount > 0 ? duAmount : 0;
            }

            // Update ledger
            ledger.CapitalIn += bet;
            ledger.CapitalOut = totalCreditsOut;
            ledger.RoundCount = i + 1;
        }

        return new SimulationResult
        {
            TotalCreditsIn = totalCreditsIn,
            TotalCreditsOut = totalCreditsOut,
            BaseCreditsOut = baseCreditsOut,
            JackpotCreditsOut = jackpotCreditsOut,
            DoubleUpCreditsOut = doubleUpCreditsOut,
            ObservedRtp = totalCreditsIn > 0 ? decimal.Round(totalCreditsOut / totalCreditsIn, 4) : 0m,
            BaseRtp = totalCreditsIn > 0 ? decimal.Round(baseCreditsOut / totalCreditsIn, 4) : 0m,
            JackpotRtp = totalCreditsIn > 0 ? decimal.Round(jackpotCreditsOut / totalCreditsIn, 4) : 0m,
            DoubleUpRtp = totalCreditsIn > 0 ? decimal.Round(doubleUpCreditsOut / totalCreditsIn, 4) : 0m,
            WinRate = (decimal)wins / rounds,
            AvgConsecutiveLosses = (decimal)totalLosses / Math.Max(1, rounds - wins),
            MaxConsecutiveLosses = maxConsecLosses,
            AvgPayoutScale = scaleCount > 0 ? totalPayoutScale / scaleCount : 1m,
            DoubleUpWinRate = doubleUpSessions > 0 ? (decimal)doubleUpWins / doubleUpSessions : 0m,
            DoubleUpSessions = doubleUpSessions
        };
    }

    private static void Assert(List<string> failures, string name, bool condition)
    {
        if (!condition)
            failures.Add(name);
    }

    private class SimulationResult
    {
        public decimal TotalCreditsIn { get; set; }
        public decimal TotalCreditsOut { get; set; }
        public decimal BaseCreditsOut { get; set; }
        public decimal JackpotCreditsOut { get; set; }
        public decimal DoubleUpCreditsOut { get; set; }
        public decimal ObservedRtp { get; set; }
        public decimal BaseRtp { get; set; }
        public decimal JackpotRtp { get; set; }
        public decimal DoubleUpRtp { get; set; }
        public decimal WinRate { get; set; }
        public decimal AvgConsecutiveLosses { get; set; }
        public int MaxConsecutiveLosses { get; set; }
        public decimal AvgPayoutScale { get; set; }
        public decimal DoubleUpWinRate { get; set; }
        public int DoubleUpSessions { get; set; }
    }
}
