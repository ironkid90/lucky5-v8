with open('src/Lucky5.Simulation/Program.cs', 'r') as f:
    s = f.read()

# === 1. ShouldEnterDoubleUp ===
old = '''static bool ShouldEnterDoubleUp(PlayerBehavior behavior, ulong seed, int payout, decimal machineCredits, bool sabotagePhase)
{
    return behavior switch
    {
        PlayerBehavior.ConservativeCollectFirst => false,
        // Balanced: realistic player — not always entering, especially when the win is small
        // or when they're already close to the cap. Target accept rate ≈ 55-60% on winning rounds.
        PlayerBehavior.Balanced => machineCredits + payout < EngineConfig.Default.CloseThreshold
            && Roll(seed, "accept-balanced", payout, 0.55m),
        PlayerBehavior.AggressiveCabinetClosing => machineCredits + payout < 50_000_000m || payout < 2_000_000,
        PlayerBehavior.CounterplaySabotage => sabotagePhase
            || machineCredits + payout < 50_000_000m
            || payout < 2_000_000,
        _ => false
    };
}'''
new = '''static bool ShouldEnterDoubleUp(PlayerBehavior behavior, ulong seed, int payout, decimal machineCredits, bool sabotagePhase, decimal drift)
{
    // Drift = ObservedRtp - TargetRtp. Positive = machine hot (player pulls back).
    // Negative = machine cold (player chases). Base accept ±15%.
    var driftAdj = Math.Clamp(-drift * 0.30m, -0.15m, 0.15m);
    return behavior switch
    {
        PlayerBehavior.ConservativeCollectFirst => false,
        PlayerBehavior.Balanced => machineCredits + payout < EngineConfig.Default.CloseThreshold
            && Roll(seed, "accept-balanced", payout, 0.45m + driftAdj),
        PlayerBehavior.AggressiveCabinetClosing => machineCredits + payout < 50_000_000m || payout < 2_000_000,
        PlayerBehavior.CounterplaySabotage => sabotagePhase
            || machineCredits + payout < 50_000_000m
            || payout < 2_000_000,
        _ => false
    };
}'''
assert old in s, "ShouldEnterDoubleUp not found"
s = s.replace(old, new, 1)

# === 2. ShouldTakeHalf ===
old = '''static bool ShouldTakeHalf(PlayerBehavior behavior, ulong seed, int step, int openingAmount, decimal machineCredits, int currentAmount)
{
    return behavior switch
    {
        // Balanced: take-half only when current amount is materially large (≥ 8× trigger).
        // Realistic player doesn't bank half on a 4× trigger — they keep going.
        PlayerBehavior.Balanced => currentAmount >= Math.Max(openingAmount * 8, 1_000_000)
            && machineCredits + currentAmount < EngineConfig.Default.CloseThreshold
            && Roll(seed, "take-half-balanced", step, 0.25m),
        PlayerBehavior.AggressiveCabinetClosing => currentAmount >= Math.Max(openingAmount * 8, 1_000_000)
            && machineCredits + currentAmount >= EngineConfig.Default.CloseThreshold * 0.65m
            && Roll(seed, "take-half-aggressive", step, 0.60m),
        PlayerBehavior.CounterplaySabotage => currentAmount >= Math.Max(openingAmount * 10, 1_500_000)
            && machineCredits + currentAmount >= EngineConfig.Default.CloseThreshold * 0.70m
            && Roll(seed, "take-half-counterplay", step, 0.45m),
        _ => false
    };
}'''
new = '''static bool ShouldTakeHalf(PlayerBehavior behavior, ulong seed, int step, int openingAmount, decimal machineCredits, int currentAmount, decimal drift)
{
    // Hot drift: take-half more often. Cold drift: gamble more.
    var driftAdj = Math.Clamp(drift * 0.20m, -0.10m, 0.10m);
    return behavior switch
    {
        PlayerBehavior.Balanced => currentAmount >= Math.Max(openingAmount * 6, 750_000)
            && machineCredits + currentAmount < EngineConfig.Default.CloseThreshold
            && Roll(seed, "take-half-balanced", step, 0.20m + driftAdj),
        PlayerBehavior.AggressiveCabinetClosing => currentAmount >= Math.Max(openingAmount * 8, 1_000_000)
            && machineCredits + currentAmount >= EngineConfig.Default.CloseThreshold * 0.65m
            && Roll(seed, "take-half-aggressive", step, 0.60m),
        PlayerBehavior.CounterplaySabotage => currentAmount >= Math.Max(openingAmount * 10, 1_500_000)
            && machineCredits + currentAmount >= EngineConfig.Default.CloseThreshold * 0.70m
            && Roll(seed, "take-half-counterplay", step, 0.45m),
        _ => false
    };
}'''
assert old in s, "ShouldTakeHalf not found"
s = s.replace(old, new, 1)

# === 3. ShouldCashoutDoubleUp signature ===
old = '''static bool ShouldCashoutDoubleUp(
    PlayerBehavior behavior,
    ulong seed,
    int step,
    int openingAmount,
    bool machineAlreadyClosed,
    bool takeHalfUsed,
    decimal machineCredits,
    int currentAmount)'''
new = '''static bool ShouldCashoutDoubleUp(
    PlayerBehavior behavior,
    ulong seed,
    int step,
    int openingAmount,
    bool machineAlreadyClosed,
    bool takeHalfUsed,
    decimal machineCredits,
    int currentAmount,
    decimal drift)'''
assert old in s, "ShouldCashoutDoubleUp sig not found"
s = s.replace(old, new, 1)

# Update Balanced branch inside
old = '''        // Balanced: realistic player — banks when they've at least tripled the trigger
        // or when impulse rolls high. Not on the bare 2× bounce.
        PlayerBehavior.Balanced => step > 0 && (
            takeHalfUsed
            || currentAmount >= Math.Max(openingAmount * 3, 500_000)
            || Roll(seed, "cashout-balanced", step, 0.55m)),'''
new = '''        // Hot drift: cashout more aggressively. Cold drift: keep going.
        var cashDriftAdj = Math.Clamp(drift * 0.25m, -0.15m, 0.15m);
        PlayerBehavior.Balanced => step > 0 && (
            takeHalfUsed
            || currentAmount >= Math.Max(openingAmount * 3, 500_000)
            || Roll(seed, "cashout-balanced", step, 0.40m + cashDriftAdj)),'''
assert old in s, "ShouldCashoutDoubleUp Balanced branch not found"
s = s.replace(old, new, 1)

# === 4. ShouldSwitchDealer signature ===
old = '''static bool ShouldSwitchDealer(PlayerBehavior behavior, ulong seed, int step, Lucky5DoubleUpSession session, bool sabotagePhase)'''
new = '''static bool ShouldSwitchDealer(PlayerBehavior behavior, ulong seed, int step, Lucky5DoubleUpSession session, bool sabotagePhase, decimal drift)'''
assert old in s, "ShouldSwitchDealer sig not found"
s = s.replace(old, new, 1)

# Update Balanced switch rate
old = '''        PlayerBehavior.Balanced => session.SwitchCountInRound == 0
            && dealerRank is 7 or 8
            && Roll(seed, "switch-balanced", step, 0.20m),'''
new = '''        // Cold drift: switch more. Hot drift: lock in current dealer.
        var switchDriftAdj = Math.Clamp(-drift * 0.15m, -0.10m, 0.10m);
        PlayerBehavior.Balanced => session.SwitchCountInRound == 0
            && dealerRank is 7 or 8
            && Roll(seed, "switch-balanced", step, 0.18m + switchDriftAdj),'''
assert old in s, "ShouldSwitchDealer Balanced branch not found"
s = s.replace(old, new, 1)

# === 5. Update call sites ===
old = 'ShouldEnterDoubleUp(behavior, seed, payout, session.MachineCredits, sabotagePhase)'
new = 'ShouldEnterDoubleUp(behavior, seed, payout, session.MachineCredits, sabotagePhase, ledger.ObservedRtp - ledger.TargetRtp)'
assert old in s
s = s.replace(old, new, 1)

old = 'ShouldTakeHalf(behavior, roundSeed, step, openingAmount, bank.MachineCredits, session.CurrentAmount)'
new = 'ShouldTakeHalf(behavior, roundSeed, step, openingAmount, bank.MachineCredits, session.CurrentAmount, ledger.ObservedRtp - ledger.TargetRtp)'
assert old in s
s = s.replace(old, new, 1)

old = 'ShouldCashoutDoubleUp(behavior, roundSeed, step, openingAmount, bank.PendingReset, takeHalfUsed, bank.MachineCredits, session.CurrentAmount)'
new = 'ShouldCashoutDoubleUp(behavior, roundSeed, step, openingAmount, bank.PendingReset, takeHalfUsed, bank.MachineCredits, session.CurrentAmount, ledger.ObservedRtp - ledger.TargetRtp)'
assert old in s
s = s.replace(old, new, 1)

# ShouldSwitchDealer has 2 call sites, both with trailing ))
old = 'ShouldSwitchDealer(behavior, roundSeed, step, session, sabotagePhase))'
new = 'ShouldSwitchDealer(behavior, roundSeed, step, session, sabotagePhase, ledger.ObservedRtp - ledger.TargetRtp))'
assert s.count(old) >= 1
s = s.replace(old, new)

old = 'ShouldSwitchDealer(behavior, roundSeed, step + session.SwitchCountInRound, session, sabotagePhase))'
new = 'ShouldSwitchDealer(behavior, roundSeed, step + session.SwitchCountInRound, session, sabotagePhase, ledger.ObservedRtp - ledger.TargetRtp))'
assert old in s
s = s.replace(old, new, 1)

with open('src/Lucky5.Simulation/Program.cs', 'w') as f:
    f.write(s)
print('ALL HEURISTICS ADAPTED TO DRIFT')