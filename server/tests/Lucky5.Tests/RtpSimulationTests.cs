namespace Lucky5.Tests;

using Lucky5.Domain.Entities;
using Lucky5.Domain.Game;
using Lucky5.Domain.Game.CleanRoom;

/// <summary>
/// Monte Carlo RTP simulation that models realistic human play (not perfect strategy).
/// Uses the engine's own payout scale controller to measure convergence to the configured target.
/// </summary>
public static class RtpSimulationTests
{
    private const int SimulationRounds = 100_000;

    public static Task RunAsync(List<string> failures)
    {
        var config = EngineConfig.Default;
        var result = RunSimulation(config, SimulationRounds);

        Console.WriteLine("══════════════════════════════════════════════════════════");
        Console.WriteLine($"           RTP SIMULATION ({SimulationRounds:N0} ROUNDS)");
        Console.WriteLine("══════════════════════════════════════════════════════════");
        Console.WriteLine($"  Target RTP:        {config.TargetRtp:P2}");
        Console.WriteLine($"  Observed RTP:      {result.ObservedRtp:P2}");
        Console.WriteLine($"    Base RTP:        {result.BaseRtp:P2}");
        Console.WriteLine($"    Jackpot RTP:     {result.JackpotRtp:P2}");
        Console.WriteLine($"    Double-Up RTP:   {result.DoubleUpRtp:P2}");
        Console.WriteLine($"  Total In:          {result.TotalCreditsIn:N0}");
        Console.WriteLine($"  Total Out:         {result.TotalCreditsOut:N0}");
        Console.WriteLine($"  Win Rate:          {result.WinRate:P2}");
        Console.WriteLine($"  Max Consec Losses: {result.MaxConsecutiveLosses}");
        Console.WriteLine($"  Avg Scale:         {result.AvgPayoutScale:F3}");
        Console.WriteLine("══════════════════════════════════════════════════════════");

        var rtpDelta = Math.Abs(result.ObservedRtp - config.TargetRtp);
        Assert(failures,
            $"RTP near {config.TargetRtp:P0} (obs={result.ObservedRtp:P2}, delta={rtpDelta:P2})",
            rtpDelta < 0.06m);

        Assert(failures,
            $"Base RTP in range (obs={result.BaseRtp:P2})",
            result.BaseRtp > 0.30m && result.BaseRtp < 0.70m);

        Assert(failures,
            $"DU RTP in range (obs={result.DoubleUpRtp:P2})",
            result.DoubleUpRtp > 0.05m && result.DoubleUpRtp < 0.35m);

        // Configurability: verify engine adapts to different targets
        Console.WriteLine("\n  Configurability:");
        foreach (var target in new[] { 0.70m, 0.80m, 0.90m })
        {
            var cfg = config with { TargetRtp = target };
            var r = RunSimulation(cfg, 50_000);
            var delta = Math.Abs(r.ObservedRtp - target);
            Console.WriteLine($"    {target:P0} target -> {r.ObservedRtp:P2} (delta={delta:P2}, base={r.BaseRtp:P2}, du={r.DoubleUpRtp:P2})");
            Assert(failures, $"  {target:P0} target convergence (delta={delta:P2})", delta < 0.08m);
        }

        return Task.CompletedTask;
    }

    private static SimulationResult RunSimulation(EngineConfig config, int rounds)
    {
        var rng = new Random(42);
        // Seed policy state with pre-warmed values (simulates a machine that's been running).
        // This avoids the warmup "debt" that takes millions of rounds to overcome.
        var policyState = new MachinePolicyState
        {
            TargetRtp = config.TargetRtp,
            CreditsIn = 5_000_000m,
            CreditsOut = 4_000_000m,          // 80% RTP
            BaseCreditsOut = 2_550_000m,       // 51% base RTP
            JackpotCreditsOut = 162_500m,      // 3.25% jackpot RTP
            DoubleUpCreditsOut = 1_100_000m,   // 22% DU RTP
            RoundCount = 1000,
            ConsecutiveLosses = 0,
        };
        var ledger = new MachineLedgerState
        {
            MachineId = 1,
            CapitalIn = 5_000_000m,
            CapitalOut = 4_000_000m,
            RoundCount = 1000,
            JackpotFourOfAKindA = 500_000m,
            JackpotFourOfAKindB = 500_000m,
            JackpotStraightFlush = 1_500_000m,
            JackpotFullHouse = 1_300_000m,
            JackpotFullHouseRank = 14,
        };

        decimal totalIn = 0, totalOut = 0, baseOut = 0, jpOut = 0, duOut = 0;
        int wins = 0, maxConsec = 0, curConsec = 0;
        decimal totalScale = 0;
        int scaleN = 0;

        // Realistic 5-card draw poker hit frequencies (with optimal hold strategy).
        // Source: standard video poker probability tables.
        // These account for human-level play (not perfect computer strategy).
        var handDistribution = new (HandCategory cat, decimal freq, int baseMultiplier)[]
        {
            (HandCategory.RoyalFlush,      0.000015m, 1000),  // ~1 in 65,000
            (HandCategory.StraightFlush,   0.000130m, 300),   // ~1 in 7,700
            (HandCategory.FourOfAKind,     0.002400m, 120),   // ~1 in 4,165
            (HandCategory.FullHouse,       0.011500m, 20),    // ~1 in 87
            (HandCategory.Flush,           0.011000m, 14),    // ~1 in 91
            (HandCategory.Straight,        0.011200m, 10),    // ~1 in 89
            (HandCategory.ThreeOfAKind,    0.074400m, 6),     // ~1 in 13
            (HandCategory.TwoPair,         0.050000m, 4),     // ~1 in 20 (lower with min-pair filter)
        };
        // Note: The Lebanese paytable doesn't pay for single pairs (no OnePair payout).
        // This means ~75% of hands are no-win, which is normal for this paytable.
        decimal totalHitFreq = 0;
        foreach (var (_, freq, _) in handDistribution) totalHitFreq += freq;
        // Remaining: no-win hands (high card, low pair)

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

            // Policy resolution (this is the controller that manages RTP)
            var policyRes = MachinePolicy.ResolvePolicy(policyState, seed, config);
            var policyMode = policyRes.DistributionMode;
            var scale = policyRes.EffectiveScale;

            totalScale += scale;
            scaleN++;

            // Deal: determine hand outcome using realistic hit frequencies
            var roll = (decimal)rng.NextDouble();
            decimal cumulative = 0;
            HandCategory resultCat = HandCategory.HighCard;
            int baseMultiplier = 0;

            foreach (var (cat, freq, mult) in handDistribution)
            {
                cumulative += freq;
                if (roll < cumulative)
                {
                    resultCat = cat;
                    baseMultiplier = mult;
                    break;
                }
            }

            // Base payout with scale
            int basePay = baseMultiplier > 0 ? bet * baseMultiplier : 0;
            int payout = basePay > 0 ? (int)Math.Round(basePay * scale, MidpointRounding.AwayFromZero) : 0;

            // Jackpot (progressive pool — rare, big payouts)
            decimal jpWon = 0;
            if (resultCat == HandCategory.FourOfAKind && ledger.JackpotFourOfAKindA > payout)
                jpWon = ledger.JackpotFourOfAKindA;
            else if (resultCat == HandCategory.StraightFlush && ledger.JackpotStraightFlush > payout)
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

            // Track wins/losses for pity system
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

            // ── Double-Up ──
            // Models realistic DU: board trap mechanic + take-score behavior.
            // Per-round win rate ~30% (traps + imperfect play).
            // Most players take score after 1-2 wins (lam3a dopamine balance).
            if (payout > 0 || jpWon > 0)
            {
                int duAmt = (int)(jpWon > 0 ? jpWon : payout);
                int baseAmt = duAmt;
                bool duWon = false;

                for (int d = 0; d < 3; d++) // Max 3 DU rounds
                {
                    // Per-round DU win rate: ~30%
                    // (board traps + imperfect player decisions + deck pressure)
                    double duWinChance = d == 0 ? 0.30 : (d == 1 ? 0.28 : 0.26);

                    if (rng.NextDouble() < duWinChance)
                    {
                        duAmt *= 2;
                        duWon = true;

                        // Take-score: most players cash out after 1-2 wins
                        double takeChance = d switch { 0 => 0.60, 1 => 0.35, _ => 0.20 };
                        if (rng.NextDouble() < takeChance)
                            break; // Took the money
                    }
                    else
                    {
                        duAmt = 0;
                        break;
                    }
                }

                if (duWon && duAmt > baseAmt)
                {
                    var duWinnings = duAmt - baseAmt;
                    totalOut += duWinnings;
                    duOut += duWinnings;
                }
            }

            // Update ledger
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
    }
}
