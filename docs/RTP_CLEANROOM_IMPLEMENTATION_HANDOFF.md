# Lucky5 CleanRoom RTP / Gameplay Calibration Handoff

This is the continuation handoff for agents working on the Lucky5 v8 CleanRoom RTP/gameplay tuning effort. It compresses the recent implementation and reasoning journey so future agents can continue without replaying the full conversation.

## User goal

User approved implementation work until all targets are met and the game is polished:

- Keep long-term RTP around **80%**.
- Preserve Lucky5 / Lebanese cabinet rules and retro cabinet feel.
- Make gameplay more interesting, GoldenPoker/bucket-style inspired: close calls, hidden signals, long/short session interest.
- Keep all features and jackpots possible.
- Ensure the **40,000,000 credit machine close** is achievable by Double-Up, even from a small Two Pair win if the chain runs long enough.
- Harden via simulation, calibration sweeps, exploit/counterplay tests, and certification runs.

## Non-negotiables / rules preserved

- Authoritative game logic remains in `server/src/Lucky5.Domain/Game/CleanRoom/`.
- Web client must remain presentation-only for server-issued game states.
- Do not modernize UI; preserve retro AI9 / Lebanese cabinet feel.
- Vanilla client only; no React/Vue/Godot pivot.
- Card rendering remains DOM-based; do not load `/assets/images/cards/`.
- Gameplay rules preserved:
  - Lebanese paytable.
  - 5♠ in Double-Up remains permanent no-lose / lucky behavior.
  - Ace behavior remains special: Ace auto-win in Double-Up; Ace 2× base multiplier was restored in simulation because production `GameService` already does it.
  - Kent jackpot uses the documented streak behavior.

## Current important files

- `server/src/Lucky5.Domain/Game/CleanRoom/CoreModels.cs`
  - `EngineConfig` contains RTP/controller knobs.
  - Current observed important knobs:
    - `TargetRtp = 0.80m`
    - `TargetDoubleUpRtp = 0.1200m`
    - `MinPayoutScale = 0.35m`
    - `MaxPayoutScale = 2.05m`
    - `DrawAnteMultiplier = 1.0m` default, CLI-overridable in simulator.
    - `SoftCapWarning = 28_000_000m`
    - `SoftCapHard = 35_000_000m`
    - `CloseThreshold = 40_000_000m`
- `server/src/Lucky5.Domain/Game/CleanRoom/MachinePolicy.cs`
  - `MachinePolicy.Cfg` was changed from hardcoded `EngineConfig.Default` to a settable backing property so simulator/config sweeps can actually override engine policy.
  - Key functions: `ResolvePolicy`, `ResolvePayoutScale`, `BuildDoubleUpPlayDeck`, `ComputeDoubleUpDeckPressure`.
- `server/src/Lucky5.Simulation/Program.cs`
  - Simulator now has CLI args:
    - `--rounds N`
    - `--certification` = 500,000 rounds
    - `--behavior balanced|conservative|aggressive|counterplay` (exact parser names should be verified)
    - `--variance-report`
    - `--draw-ante M`
    - `--min-scale S`
  - Stake model now includes deal ante + draw ante: `Bet + Bet * cfg.DrawAnteMultiplier`.
  - Current calibration work uses `--draw-ante 1.5` often.
  - Double-Up chain currently has a **per-step deck refresh** attempt inside `PlayDoubleUpChain`.

## Major reasoning journey and findings

### 1. Initial RTP problem

Early simulation showed total RTP could be near 80%, but channel allocation was wrong:

- Base RTP too low.
- Double-Up RTP far too high.
- Jackpot RTP variable depending on run length and pool maturity.

Representative observed shape before tuning: total in band, but channels like `Base ~50%`, `DU ~26-29%`, jackpot low/swingy.

Conclusion: total RTP alone is not enough; need channel analysis and player-behavior analysis.

### 2. CLI overrides initially looked broken

`--min-scale` and related sweep knobs appeared to have no effect. Root cause found:

- `MachinePolicy` read `EngineConfig.Default` through a hardcoded static accessor.
- Simulator built an overridden `cfg`, but `MachinePolicy` did not consume it.

Fix made:

- `MachinePolicy.Cfg` now has a backing field/setter.
- Simulator assigns `MachinePolicy.Cfg = cfg` after CLI overrides.

Important: future agents should verify this still holds after any refactor.

### 3. Draw ante became a key calibration lever

User explicitly allowed considering machines where draw costs more than the initial deal stake, e.g. stake 5K then draw 10K.

Simulator now models:

```text
deal ante = Bet
draw ante = Bet * DrawAnteMultiplier
total stake = Bet + draw ante
```

Current best working value from recent runs was `--draw-ante 1.5`, not necessarily final. This shifts RTP without changing paytable rules.

### 4. Double-Up was the dominant overshoot

Double-Up is very generous because:

- Ace auto-win.
- 5♠ no-lose / lucky behavior.
- Good players guess optimally on Big/Small.
- Take-half/cashout behavior can preserve gains.

Naive or too-aggressive Balanced behavior produced DU around 24-29% when target was 12%.

The simulator now models more realistic Balanced behavior:

- Balanced DU acceptance was tuned down from aggressive values to around `0.15 + driftAdj` in `ShouldEnterDoubleUp`.
- Balanced cashout became more conservative/aggressive depending on current amount; current code has `currentAmount >= Math.Max(openingAmount * 2, 400_000)` or a `0.30 + cashDriftAdj` roll.
- Mistake rates were introduced in guess selection (middle cards and edge cards), but exact current values should be re-read before changing.

### 5. Per-step DU deck refresh was implemented as the bucket-style lever

User emphasized that DU is the core, and each DU step should be able to have its own tuned deck. Work added an attempt to refresh/rebuild the DU deck after each winning step, using updated projected exposure / live net-since-close.

Current code location:

- `server/src/Lucky5.Simulation/Program.cs`
- `PlayDoubleUpChain`, around the per-step deck pressure / refresh block.

Observed effect:

- More pressure samples per chain.
- More dynamic DU behavior.
- Helped balance Base/DU with player heuristic changes.

Caution:

- Re-read the code carefully. The current implementation rebuilds a session after `ResolveGuess`, then the `switch` case may assign `session = resolution.Session` on win. That may overwrite part of the freshly rebuilt session. The code built and simulated, but this is a likely correctness/polish area. If the goal is to wire the live game, design a cleaner domain-level DU step/deck refresh API rather than cloning records inside the simulator.

### 6. Best observed Balanced calibration so far

A strong 20K Balanced run with `--draw-ante 1.5` produced:

```text
20,000 rounds | RTP 80.33% | Base 65.51% | Jackpot 2.05% | DU 12.77%
Accept 14.04%
```

This was a major breakthrough because:

- Total RTP was in the 78-82% band.
- Base was within ~1pp of target.
- DU was within ~1pp of target.
- Jackpot was low in short run but expected to mature upward over longer runs.

### 7. Variance / certification observations

A variance report was run with `--draw-ante 1.5` and showed:

```text
Balanced 10k sample band | min 78.10% | median 80.61% | max 83.04%
Balanced 100k           | RTP 80.46% | Base 60.21% | Jackpot 5.59% | DU 14.66%
Aggressive close 200k   | RTP 81.40% | Base 31.76% | Jackpot 7.13% | DU 42.51%
Counterplay sabotage    | RTP 11.62% | Base 74.39% | Jackpot 4.79% | DU -67.56%
```

Interpretation:

- Balanced is stable near target.
- Aggressive close behavior intentionally pushes DU much higher; it gets close-suspense events and is useful as a fun-factor stress test, not a house-average baseline.
- Counterplay sabotage getting crushed means obvious exploit strategies are not working under the simulator assumptions.

A 500K certification run was observed:

```text
500,000 rounds | RTP 81.30% | Base 60.61% | Jackpot 6.02% | DU 14.66%
Jackpots 414 | Largest jackpot 10,000,000 | Largest bank event 20,000,000 | Max credits 21,239,776
Jackpot mix: FH 376 | SF 26 | Kent 12
RTP windows: 1k 79.65% | 5k 79.46% | 50k 81.11%
RESULT: PASS within [78%, 82%]
```

Interpretation:

- Long-term total RTP target is passing.
- Jackpot/DU channels still drift high long-term compared with nominal channel targets.
- All key jackpot classes observed except 4OAK in that sample; 4OAK jackpot cap is small (99,999) so it may be hidden by reporting or rare due to active-slot mechanics. Recheck if "all jackpots possible" needs formal proof.
- Max credits only reached ~21M in Balanced 500K, so 40M close was **not** demonstrated in Balanced certification.

## Current unresolved gap: 40M machine close engagement

This is the main remaining gameplay/polish gap.

User explicitly cares that the 40M close is achievable by Double-Up even from the smallest Two Pair. Mathematically, a Two Pair payout can close if the player chains enough DU wins, but current Balanced certification did not produce a 40M close.

Known observations:

- Balanced 500K max credits observed: ~21.2M.
- 40M closes observed: 0 in Balanced runs.
- Aggressive close 200K reached `Max credits 39,813,880`, with multiple close-suspense events but still 0 actual closes. This is very close and should be the next tuning target.

Likely next work:

1. Make a dedicated close-reachability simulation mode:
   - Start from a controlled Two Pair / small payout.
   - Force or heavily prefer entering DU.
   - Verify that 11-ish successful doubles can hit close and that `MachineClosed` resolution triggers.
   - This should be a deterministic/proof test, separate from average Balanced RTP.
2. Tune close-call script around 28M-40M:
   - `DoubleUpSequenceCreditStart`
   - `DoubleUpSequencePressureStart`
   - `DoubleUpHighExposureSequencePressureStart`
   - `DoubleUpSuspenseReleaseChance`
   - `SoftCapWarning` / `SoftCapHard` if needed.
3. Preserve average RTP while increasing rare close drama:
   - This should be rare and compensated by pressure after close or by post-close reset, not by raising everyday RTP.
4. Consider a high-zone DU release mechanic:
   - When player is already near 28M-39M, allow deterministic suspense/release paths sometimes instead of only tightening pressure.
   - Must remain hidden, subtle, deterministic, and long-term 80% safe.

## Important caution: current active todo list may be stale

The preserved todo list after context compression said:

- t2 Confirm clean build + tests pass in progress.
- t3 baseline 10K Balanced smoke pending.
- t4 behavior-aware heuristics pending.
- t5 TargetRtp sweep pending.
- t10 Wire DoubleUpNoisePlan pending.
- t11 certification pending.

But conversation after that already did some of these partially:

- Build was observed clean.
- `server/tests/Lucky5.Tests` passed in a previous turn.
- Balanced 20K, variance report, and certification were observed.
- Behavior-aware heuristics and per-step DU refresh were implemented in simulator.

Future agent should not trust the todo list blindly. Re-run targeted verification and then rewrite todos based on actual current files/results.

## Verification commands used / recommended

From repo root:

```bash
dotnet build server/Lucky5.sln --nologo
cd server && dotnet run --project tests/Lucky5.Tests/Lucky5.Tests.csproj --no-build
```

Simulation examples from `server/`:

```bash
dotnet run --project src/Lucky5.Simulation/Lucky5.Simulation.csproj --no-build -- --rounds 20000 --draw-ante 1.5

dotnet run --project src/Lucky5.Simulation/Lucky5.Simulation.csproj --no-build -- --rounds 20000 --draw-ante 1.5 --variance-report

dotnet run --project src/Lucky5.Simulation/Lucky5.Simulation.csproj --no-build -- --certification --draw-ante 1.5
```

If `--no-build` produces stale behavior, build first.

## Known tooling / repo state notes

- Project `AGENTS.md` mandates vexp first. In this handoff-writing turn, `mcp_vexp_run_pipeline` failed because the vexp daemon pipe was unavailable. Future agents should run `vexp setup` or otherwise restore the vexp daemon before deeper code exploration.
- A previous `git status` attempt during the RTP work reported `.git/index: index file smaller than expected`; a later status showed only `.comp/session-memory.json` modified. Treat git state as suspicious and verify before committing.
- Do not store this full handoff in Hermes persistent memory. The workspace doc is the durable task-progress source; global memory should only point to this file.

## Recommended next concrete step

Start with the 40M close gap, because average RTP is already near target but the user explicitly wants close drama.

Suggested next plan:

1. Re-run build/tests to confirm current code after context compression.
2. Run a 20K Balanced smoke with `--draw-ante 1.5` and save current channel breakdown.
3. Add a deterministic close-reachability simulation/proof mode:
   - Force a small Two Pair opening amount.
   - Run Double-Up under controlled favorable seeds until reaching close.
   - Assert `Lucky5DoubleUpOutcome.MachineClosed` can occur.
4. If proof passes but organic frequency is too low, tune rare high-zone release / suspense knobs.
5. Re-run:
   - Balanced 20K smoke.
   - Balanced 100K.
   - Aggressive close 200K.
   - Counterplay 200K.
   - 500K certification.
6. Only declare complete when:
   - Total RTP remains within 78-82% on certification.
   - Balanced short windows are interesting.
   - Aggressive close mode produces actual 40M closes sometimes.
   - Counterplay remains non-exploitable.
   - All jackpot types are proven reachable/firing.
