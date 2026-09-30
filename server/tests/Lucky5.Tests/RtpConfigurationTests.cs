namespace Lucky5.Tests;

using Lucky5.Domain.Entities;
using Lucky5.Domain.Game;
using Lucky5.Domain.Game.CleanRoom;

/// <summary>
/// Focused tests for RTP configuration: schema validation, default behaviour,
/// configured RTP values, and dynamic per-session RTP overrides.
/// These tests verify the payout controller's response to different TargetRtp
/// settings and confirm that existing default calculations remain unaffected.
/// </summary>
public static class RtpConfigurationTests
{
    private static readonly ulong Seed = DeterministicSeed.FromString("rtp-config-regression");

    public static Task RunAsync(List<string> failures)
    {
        var defaultConfig = EngineConfig.Default;

        // ──────────────────────────────────────────────────────────────
        // 1. Schema / Invariant Validation
        // ──────────────────────────────────────────────────────────────

        Assert(failures,
            "EngineConfig.TargetRtp must be a positive fraction (0 < x < 1)",
            defaultConfig.TargetRtp > 0m && defaultConfig.TargetRtp < 1m);

        Assert(failures,
            "EngineConfig.TargetRtp must default to the approved 80% baseline",
            defaultConfig.TargetRtp == 0.80m);

        Assert(failures,
            "EngineConfig must expose a positive MinimumObservedBaseRtp",
            defaultConfig.MinimumObservedBaseRtp > 0m);

        Assert(failures,
            "EngineConfig payout-scale bounds must be ordered (Min <= Default <= Max)",
            defaultConfig.MinPayoutScale <= defaultConfig.DefaultPayoutScale
                && defaultConfig.DefaultPayoutScale <= defaultConfig.MaxPayoutScale);

        Assert(failures,
            "EngineConfig TargetScaledBaseRtp must be positive and less than TargetRtp",
            defaultConfig.TargetScaledBaseRtp > 0m
                && defaultConfig.TargetScaledBaseRtp < defaultConfig.TargetRtp);

        Assert(failures,
            "EngineConfig TargetJackpotRtp must be a small positive fraction",
            defaultConfig.TargetJackpotRtp > 0m && defaultConfig.TargetJackpotRtp < 0.10m);

        Assert(failures,
            "EngineConfig TargetDoubleUpRtp must be a positive fraction",
            defaultConfig.TargetDoubleUpRtp > 0m && defaultConfig.TargetDoubleUpRtp < 1m);

        // ──────────────────────────────────────────────────────────────
        // 2. Default RTP Behaviour
        // ──────────────────────────────────────────────────────────────

        var defaultPolicyState = new MachinePolicyState
        {
            TargetRtp = defaultConfig.TargetRtp,
            CreditsIn = 1_000_000m,
            CreditsOut = 800_000m,      // exactly 80% observed
            BaseCreditsOut = 320_000m,  // 32% base
            JackpotCreditsOut = 35_000m,
            DoubleUpCreditsOut = 90_000m,
            RoundCount = defaultConfig.ConvergenceHorizon
        };

        var defaultScale = MachinePolicy.ResolvePayoutScale(defaultPolicyState, Seed);
        Assert(failures,
            "Default RTP state should produce payout scales inside the configured band",
            defaultScale.SmallScale >= defaultConfig.MinPayoutScale
                && defaultScale.SmallScale <= defaultConfig.MaxPayoutScale
                && defaultScale.MediumScale >= defaultConfig.MinPayoutScale
                && defaultScale.MediumScale <= defaultConfig.MaxPayoutScale
                && defaultScale.BigScale >= defaultConfig.MinPayoutScale
                && defaultScale.BigScale <= defaultConfig.MaxPayoutScale);

        var defaultPolicy = MachinePolicy.ResolvePolicy(defaultPolicyState, Seed);
        Assert(failures,
            "Default RTP policy resolution should report the configured target",
            defaultPolicy.Telemetry.TargetRtp == defaultConfig.TargetRtp);

        // ──────────────────────────────────────────────────────────────
        // 3. Configured RTP Values (Custom EngineConfig)
        // ──────────────────────────────────────────────────────────────

        var customConfig = defaultConfig with { TargetRtp = 0.75m };
        var customPolicyState = new MachinePolicyState
        {
            TargetRtp = customConfig.TargetRtp,
            CreditsIn = 1_000_000m,
            CreditsOut = 750_000m,      // exactly 75% observed
            BaseCreditsOut = 320_000m,
            JackpotCreditsOut = 35_000m,
            DoubleUpCreditsOut = 90_000m,
            RoundCount = customConfig.ConvergenceHorizon
        };

        var customScale = MachinePolicy.ResolvePayoutScale(customPolicyState, Seed, customConfig);
        Assert(failures,
            "Custom RTP (75%) should produce scales inside the configured band",
            customScale.SmallScale >= customConfig.MinPayoutScale
                && customScale.SmallScale <= customConfig.MaxPayoutScale);

        var customPolicy = MachinePolicy.ResolvePolicy(customPolicyState, Seed, customConfig);
        Assert(failures,
            "Custom RTP policy resolution should report the custom target",
            customPolicy.Telemetry.TargetRtp == customConfig.TargetRtp);

        // ──────────────────────────────────────────────────────────────
        // 4. Dynamic Per-Session RTP Override
        // ──────────────────────────────────────────────────────────────
        // The engine supports per-session RTP via MachinePolicyState.TargetRtp.
        // A session running at a higher target should see a higher base scale
        // than a session at the default target, all else being equal.

        var highRtpState = new MachinePolicyState
        {
            TargetRtp = 0.90m,          // per-session override
            CreditsIn = 1_000_000m,
            CreditsOut = 800_000m,      // observed 80% (below 90% target)
            BaseCreditsOut = 320_000m,
            JackpotCreditsOut = 35_000m,
            DoubleUpCreditsOut = 90_000m,
            RoundCount = defaultConfig.ConvergenceHorizon
        };

        var lowRtpState = new MachinePolicyState
        {
            TargetRtp = 0.70m,          // per-session override
            CreditsIn = 1_000_000m,
            CreditsOut = 800_000m,      // observed 80% (above 70% target)
            BaseCreditsOut = 320_000m,
            JackpotCreditsOut = 35_000m,
            DoubleUpCreditsOut = 90_000m,
            RoundCount = defaultConfig.ConvergenceHorizon
        };

        var highRtpScale = MachinePolicy.ResolvePayoutScale(highRtpState, Seed, defaultConfig);
        var lowRtpScale = MachinePolicy.ResolvePayoutScale(lowRtpState, Seed, defaultConfig);

        Assert(failures,
            "Higher per-session RTP target should yield a higher (or equal) small-tier scale than a lower target",
            highRtpScale.SmallScale >= lowRtpScale.SmallScale);

        Assert(failures,
            "Higher per-session RTP target should yield a higher (or equal) big-tier scale than a lower target",
            highRtpScale.BigScale >= lowRtpScale.BigScale);

        // Verify the drift direction: observed above target should push scale down
        var observedAboveTarget = new MachinePolicyState
        {
            TargetRtp = 0.70m,
            CreditsIn = 1_000_000m,
            CreditsOut = 900_000m,      // observed 90% (well above 70% target)
            BaseCreditsOut = 320_000m,
            JackpotCreditsOut = 35_000m,
            DoubleUpCreditsOut = 90_000m,
            RoundCount = defaultConfig.ConvergenceHorizon
        };
        var observedBelowTarget = new MachinePolicyState
        {
            TargetRtp = 0.90m,
            CreditsIn = 1_000_000m,
            CreditsOut = 700_000m,      // observed 70% (well below 90% target)
            BaseCreditsOut = 320_000m,
            JackpotCreditsOut = 35_000m,
            DoubleUpCreditsOut = 90_000m,
            RoundCount = defaultConfig.ConvergenceHorizon
        };

        var scaleAbove = MachinePolicy.ResolvePayoutScale(observedAboveTarget, Seed, defaultConfig);
        var scaleBelow = MachinePolicy.ResolvePayoutScale(observedBelowTarget, Seed, defaultConfig);

        Assert(failures,
            "Observed RTP above target should produce a lower small-tier scale than observed below target",
            scaleAbove.SmallScale <= scaleBelow.SmallScale);

        // ──────────────────────────────────────────────────────────────
        // 5. Representative Payout Calculations
        // ──────────────────────────────────────────────────────────────

        var evaluation = FiveCardDrawEngine.EvaluateHand(
            FiveCardDrawEngine.ParseCards(["KH", "KD", "KC", "JH", "JS"]));
        var basePayout = FiveCardDrawEngine.ResolvePayout(evaluation, 100, PaytableProfile.Lebanese);

        var payoutAtDefault = (int)Math.Round(basePayout * defaultScale.SmallScale, MidpointRounding.AwayFromZero);
        var payoutAtCustom = (int)Math.Round(basePayout * customScale.SmallScale, MidpointRounding.AwayFromZero);
        var payoutAtHigh = (int)Math.Round(basePayout * highRtpScale.SmallScale, MidpointRounding.AwayFromZero);
        var payoutAtLow = (int)Math.Round(basePayout * lowRtpScale.SmallScale, MidpointRounding.AwayFromZero);

        Assert(failures,
            "Representative payout at default RTP should be positive",
            payoutAtDefault > 0);

        Assert(failures,
            "Representative payout at custom 75% RTP should be positive",
            payoutAtCustom > 0);

        Assert(failures,
            "Representative payout at 90% session override should be >= payout at 70% session override",
            payoutAtHigh >= payoutAtLow);

        // ──────────────────────────────────────────────────────────────
        // 6. Backward Compatibility / Regression Guards
        // ──────────────────────────────────────────────────────────────

        // Existing tests already assert:
        //   - defaultConfig.TargetRtp == 0.80m
        //   - new MachinePolicyState().TargetRtp == defaultConfig.TargetRtp
        //   - warmup scales open at approved values
        //   - equilibrium scales stay ordered and inside the band
        // These assertions are re-verified here to ensure no regression.

        Assert(failures,
            "Regression: default TargetRtp remains 0.80m",
            defaultConfig.TargetRtp == 0.80m);

        Assert(failures,
            "Regression: MachinePolicyState inherits default TargetRtp",
            new MachinePolicyState().TargetRtp == defaultConfig.TargetRtp);

        Assert(failures,
            "Regression: MachineLedgerState inherits default TargetRtp",
            new MachineLedgerState { MachineId = 1 }.TargetRtp == defaultConfig.TargetRtp);

        var warmupState = new MachinePolicyState
        {
            TargetRtp = defaultConfig.TargetRtp,
            RoundCount = 1
        };
        var warmupScale = MachinePolicy.ResolvePayoutScale(warmupState, Seed, defaultConfig);
        Assert(failures,
            "Regression: warmup scales open at approved values",
            warmupScale.SmallScale == defaultConfig.WarmupOpeningSmallScale
                && warmupScale.MediumScale == defaultConfig.WarmupOpeningMediumScale
                && warmupScale.BigScale == defaultConfig.WarmupOpeningBigScale);

        return Task.CompletedTask;
    }

    private static void Assert(List<string> failures, string message, bool condition)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }
}
