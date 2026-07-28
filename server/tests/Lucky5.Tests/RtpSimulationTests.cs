namespace Lucky5.Tests;

using Lucky5.Domain.Entities;
using Lucky5.Domain.Game;
using Lucky5.Domain.Game.CleanRoom;

public static class RtpSimulationTests
{
    private const int SimulationRounds = 50_000;
    private const int DuRoundsPerSession = 3;

    public static Task RunAsync(List<string> failures)
    {
        var config = EngineConfig.Default;
        var result = RunSimulation(config, SimulationRounds);

        Console.WriteLine("══════════════════════════════════════════════════════════");
        Console.WriteLine("           RTP SIMULATION RESULTS (50K ROUNDS)");
        Console.WriteLine("══════════════════════════════════════════════════════════");
        Console.WriteLine($"  Target RTP:        {config.TargetRtp:P2}");
        Console.WriteLine($"  Observed RTP:      {result.ObservedRtp:P2}");
        Console.WriteLine($"  Base RTP:          {result.BaseRtp:P2}");
        Console.WriteLine($"  Jackpot RTP:       {result.JackpotRtp:P2}");
        Console.WriteLine($"  Double-Up RTP:     {result.DoubleUpRtp:P2}");
        Console.WriteLine($"  Total Credits In:  {result.TotalCreditsIn:N0}");
        Console.WriteLine($"  Total Credits Out: {result.TotalCreditsOut:N0}");
        Console.WriteLine($"  Win Rate:          {result.WinRate:P2}");
        Console.WriteLine($"  Max Consec Losses: {result.MaxConsecutiveLosses}");
        Console.WriteLine($"  Avg Payout Scale:  {result.AvgPayoutScale:F3}");
        Console.WriteLine($"  DU Sessions:       {result.DoubleUpSessions}");
        Console.WriteLine("══════════════════════════════════════════════════════════");

        var rtpDelta = Math.Abs(result.ObservedRtp - config.TargetRtp);
        Assert(failures,
            $"RTP near target (obs={result.ObservedRtp:P2}, tgt={config.TargetRtp:P2}, delta={rtpDelta:P2})",
            rtpDelta < 0.06m);

        Assert(failures,
            $"Base RTP reasonable (obs={result.BaseRtp:P2})",
            result.BaseRtp > 0.35m && result.BaseRtp < 0.80m);

        // Configurability test
        var config70 = config with { TargetRtp = 0.70m };
        var result70 = RunSimulation(config70, 20_000);
        Console.WriteLine($"\n  70% target -> observed {result70.ObservedRtp:P2}");

        var config90 = config with { TargetRtp = 0.90m };
        var result90 = RunSimulation(config90, 20_000);
        Console.WriteLine($"  90% target -> observed {result90.ObservedRtp:P2}");

        return Task.CompletedTask;
    }

    private static SimulationResult RunSimulation(EngineConfig config, int rounds)
    {
        var paytable = PaytableProfile.Lebanese;
        var rng = new Random(42);
        var policyState = new MachinePolicyState { TargetRtp = config.TargetRtp };
        var ledger = new MachineLedgerState { MachineId = 1 };

        decimal totalIn = 0, totalOut = 0, baseOut = 0, jpOut = 0, duOut = 0;
        int wins = 0, maxConsec = 0, curConsec = 0;
        decimal totalScale = 0;
        int scaleN = 0, duSessions = 0;

        for (int i = 0; i < rounds; i++)
        {
            int bet = 5000;
            totalIn += bet;

            // Sync policy state
            policyState.CreditsIn = totalIn;
            policyState.CreditsOut = totalOut;
            policyState.BaseCreditsOut = baseOut;
            policyState.JackpotCreditsOut = jpOut;
            policyState.DoubleUpCreditsOut = duOut;
            policyState.RoundCount = i + 1;
            policyState.ConsecutiveLosses = curConsec;
            policyState.NetSinceLastClose = Math.Max(ledger.CapitalIn - ledger.CapitalOut, 0m);

            ulong seed = MakeSeed(rng);

            // Policy
            var policyRes = MachinePolicy.ResolvePolicy(policyState, seed);
            var policyMode = policyRes.DistributionMode;

            // Deal
            var stdDeck = FiveCardDrawEngine.BuildStandardDeck();
            var altDeck = MachinePolicy.AlterDeck(stdDeck, policyMode, seed, curConsec);
            var deck = FiveCardDrawEngine.ShuffleDeck(seed, "hand", altDeck);
            var hand = deck.Take(5).ToArray();

            // Hold (advised)
            var holds = FiveCardDrawEngine.ComputeAdvisedHolds(hand);
            var mask = new bool[5];
            foreach (var h in holds) if (h >= 0 && h < 5) mask[h] = true;

            // Draw
            var ds = FiveCardDrawState.Create(seed, deck, hand);
            var st = FiveCardDrawEngine.Reduce(ds, new RoundAction(RoundActionKind.SetHoldMask, HoldMask: mask));
            st = FiveCardDrawEngine.Reduce(st, new RoundAction(RoundActionKind.Draw));

            // Evaluate
            var ev = FiveCardDrawEngine.EvaluateHand(st.Hand);
            var basePay = FiveCardDrawEngine.ResolvePayout(ev, bet, paytable);

            var tier = MachinePolicy.ClassifyHand(ev.Category);
            var scale = policyRes.ForTier(tier);
            totalScale += scale;
            scaleN++;

            var payout = basePay > 0 ? (int)Math.Round(basePay * scale, MidpointRounding.AwayFromZero) : 0;

            // Jackpot
            decimal jpWon = 0;
            if (ev.Category == HandCategory.FourOfAKind && ledger.JackpotFourOfAKindA > payout)
                jpWon = ledger.JackpotFourOfAKindA;
            else if (ev.Category == HandCategory.StraightFlush && ledger.JackpotStraightFlush > payout)
                jpWon = ledger.JackpotStraightFlush;

            if (jpWon > 0)
            {
                totalOut += jpWon;
                jpOut += jpWon - payout;
                baseOut += payout;
                ledger.JackpotFourOfAKindA = config.JackpotFourOfAKindStart;
                ledger.JackpotStraightFlush = config.JackpotStraightFlushStart;
            }
            else
            {
                totalOut += payout;
                baseOut += payout;
            }

            if (payout > 0 || jpWon > 0)
            {
                wins++;
                curConsec = 0;
            }
            else
            {
                curConsec++;
                if (curConsec > maxConsec) maxConsec = curConsec;
            }

            // Double-up
            if (payout > 0)
            {
                duSessions++;
                int duAmt = (int)(jpWon > 0 ? jpWon : payout);
                int baseAmt = duAmt;

                for (int d = 0; d < DuRoundsPerSession; d++)
                {
                    ulong duSeed = MakeSeed(rng);
                    var duDeck = MachinePolicy.BuildDoubleUpDeck(
                        stdDeck, duSeed, policyState.RoundsSinceLucky5Hit,
                        policyState.NetSinceLastClose, policyMode,
                        policyState, duAmt, (int)ledger.CapitalIn, config);

                    var dealerCard = duDeck[(int)(duSeed % (ulong)duDeck.Length)];

                    // Ace auto-win (both dealer and challenger positions)
                    if (dealerCard.Rank == 14)
                    {
                        duAmt *= 2;
                        continue;
                    }

                    // Check for 5 of Spades auto-win
                    if (dealerCard.Rank == 5 && dealerCard.Suit == 'S')
                    {
                        duAmt *= 2;
                        continue;
                    }

                    // Realistic BIG/SMALL win probability by dealer rank
                    // Player always picks optimal side. Full deck probabilities:
                    // Rank 2: BIG wins ~71%, 3: ~67%, 4: ~62%, 5: ~58%, 6: ~53%
                    // Rank 7: near coin-flip (~47% for either side)
                    // Rank 8: SMALL wins ~53%, 9: ~58%, 10: ~62%, J: ~67%, Q: ~71%, K: ~75%
                    double winChance = dealerCard.Rank switch
                    {
                        2 => 0.71,
                        3 => 0.67,
                        4 => 0.62,
                        5 => 0.58,
                        6 => 0.53,
                        7 => 0.47,
                        8 => 0.53,
                        9 => 0.58,
                        10 => 0.62,
                        11 => 0.67,
                        12 => 0.71,
                        13 => 0.75,
                        _ => 0.50
                    };

                    if (rng.NextDouble() < winChance)
                        duAmt *= 2;
                    else
                    { duAmt = 0; break; }
                }

                if (duAmt > 0)
                {
                    var duWinnings = duAmt - baseAmt;
                    totalOut += duWinnings;
                    duOut += duWinnings;
                }
            }

            // Ledger
            ledger.CapitalIn += bet;
            ledger.CapitalOut = totalOut;
            ledger.RoundCount = i + 1;
            ledger.ConsecutiveLosses = curConsec;
            ledger.JackpotFourOfAKindA += config.JackpotFourOfAKindContribution;
            ledger.JackpotStraightFlush += config.JackpotStraightFlushContribution;
        }

        return new SimulationResult
        {
            TotalCreditsIn = totalIn,
            TotalCreditsOut = totalOut,
            BaseCreditsOut = baseOut,
            JackpotCreditsOut = jpOut,
            DoubleUpCreditsOut = duOut,
            ObservedRtp = totalIn > 0 ? decimal.Round(totalOut / totalIn, 4) : 0m,
            BaseRtp = totalIn > 0 ? decimal.Round(baseOut / totalIn, 4) : 0m,
            JackpotRtp = totalIn > 0 ? decimal.Round(jpOut / totalIn, 4) : 0m,
            DoubleUpRtp = totalIn > 0 ? decimal.Round(duOut / totalIn, 4) : 0m,
            WinRate = (decimal)wins / rounds,
            MaxConsecutiveLosses = maxConsec,
            AvgPayoutScale = scaleN > 0 ? totalScale / scaleN : 1m,
            DoubleUpSessions = duSessions
        };
    }

    private static ulong MakeSeed(Random rng)
        => ((ulong)(uint)rng.Next() << 32) | (uint)rng.Next();

    private static void Assert(List<string> f, string name, bool ok)
    {
        if (!ok) f.Add(name);
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
        public int MaxConsecutiveLosses { get; set; }
        public decimal AvgPayoutScale { get; set; }
        public int DoubleUpSessions { get; set; }
    }
}
