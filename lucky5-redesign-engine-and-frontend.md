# Lucky5 v8 Redesign — Modular Execution Plan

## STATUS (2026-07-28)
- ✅ Module A: Kent/Serie + Jackpot Overhaul — DONE
- ✅ Module B: Machine Close Threshold — DONE
- ✅ Module C: Paytable + Bugs — DONE
- ✅ Module D: Stake/Bet/LastHand — DONE
- ✅ Module E: RTP Engine — DONE (tuned, see RTP Engine section below)
- ⬜ Module F: Anim/Layout — NOT STARTED (frontend visual parity)
- ⬜ Module G: CSS/Styling — NOT STARTED (paytable colors, card style, CRT glow)
- ⬜ Module H: Sound — NOT STARTED (15+ audio files replacing press.mp3)
- ✅ Module I: Simulation — DONE (RtpSimulationTests.cs, 100K rounds)
- ✅ Module J: Dead Code — DONE

### RTP Engine Status
The CleanRoom engine is tuned for 80% composite RTP. Key design decisions:
- DU is intentionally generous (2 switches, ace auto-win, 5♠ no-lose) — this is the "lam3a" soul
- Deck pressure creates close calls (duplicate middle ranks, 15% anomaly inversion)
- Controller auto-adjusts base game scale to compensate for generous DU
- See `mem.md` "RTP Engine — Current State" section for full parameter reference
- See `server/tests/Lucky5.Tests/RtpSimulationTests.cs` for Monte Carlo simulation

### Remaining Work (Modules F, G, H)
These are frontend/visual/audio modules — ready for delegation.
- Module F needs ai9 reference frames in `mediaframes/ai9/` and `mediaframes/ai999/`
- Module G needs the CSS override chain from mem.md
- Module H needs 15+ audio files (currently only `press.mp3` exists)

## Overview
This plan is structured as **10 independent modules** that can be delegated to separate agents. Each module is self-contained with its own context, files, dependencies, and acceptance criteria. Modules can be worked on in parallel where dependencies allow.

## Dependency Graph
```
A (Kent/Jackpots) ─────────────────────────────┐
B (Machine Close) ── depends on A (jackpot vals)┤
C (Paytable + Bugs) ─── no deps ───────────────┤
D (Stake/Bet/LastHand) ── depends on B (close) ┤
E (RTP Engine) ──────── no deps ────────────────┤──→ I (Simulation)
F (Anim/Layout) ──────── no deps ───────────────┤
G (CSS/Styling) ──────── no deps ───────────────┤
H (Sound) ────────────── depends on F (timing) ─┤
J (Dead Code) ────────── no deps ───────────────┘
```

**Parallelizable groups:**
- Group 1 (no deps): C, E, F, G, J — can all run simultaneously
- Group 2 (after A): B, D — sequential
- Group 3 (after F): H
- Group 4 (after all): I (simulation validation)

---

## Module A: Kent/Serie + Jackpot Overhaul

**Scope:** Wire up the dead Kent/Serie system and overhaul all jackpot baseline/cap values.
**Agent type:** Backend .NET developer
**Estimated effort:** Medium
**Dependencies:** None (can start immediately)

### Context
The `FiveCardDrawEngine.IsSequentialBoard()` method exists in the CleanRoom engine but is NEVER called from GameService. The Kent jackpot pool accumulates +200/round but can never pay out. All jackpot start values and caps need updating.

### Kent Evaluation Rule
Kent is evaluated **once per round** on the **final hand only** (the 5 cards after draw/replacement). If the post-draw hand forms a sequential straight in positional slots (ascending OR descending left-to-right), it counts as a Kent. The initial deal is NOT evaluated for Kent — only the final hand matters. One round = one Kent check = at most one increment.

### Implementation Steps
1. In `GameService.DrawAsync`, after hand evaluation:
   - Call `FiveCardDrawEngine.IsSequentialBoard(state.Hand)` on the final hand only
   - If true: set `round.IsKent = true`, increment `ledger.KentStreak` by 1
   - If `KentStreak == 3`: pay Kent jackpot, reset streak to 0, reset pool to start value
2. Add Kent to `ApplyJackpotContributions`: +200 per round (currently missing)
3. Add Kent to `SnapshotJackpots` DTO
4. Add `IsKent`, `KentStreak` fields to `GameRound` entity (if not present)
5. Implement `ledger.Serie` counter: increments every round, resets on machine close/cash-out
6. Update `EngineConfig` jackpot values:

| Jackpot | Old Start | New Start | Old Cap | New Cap |
|---------|-----------|-----------|---------|---------|
| 4 of a Kind (A & B) | 20,000 | **200,000** | 99,999 | **999,999** |
| Full House (ranked) | 90,000 | **1,000,000** | 20,000,000 | **9,999,999** |
| Straight Flush | 850,000 | **1,000,000** | 10,000,000 | **9,999,999** |
| Kent | 500,000 | **500,000** | 5,000,000 | **5,000,000** |

### Files to Modify
- `server/src/Lucky5.Domain/Game/CleanRoom/CoreModels.cs` — EngineConfig jackpot values, Kent fields
- `server/src/Lucky5.Infrastructure/Services/GameService.cs` — DrawAsync jackpot section, ApplyJackpotContributions, SnapshotJackpots
- `server/src/Lucky5.Application/Dtos/` — SnapshotJackpots DTO, DrawResultDto Kent fields

### Acceptance Criteria
- [ ] `IsSequentialBoard()` called on final hand only in DrawAsync
- [ ] Kent streak increments by 1 per round (single evaluation)
- [ ] Kent jackpot pays on 3rd Kent, resets streak and pool
- [ ] All 4 jackpot start values and caps updated in EngineConfig
- [ ] Kent contribution (+200/round) added to ApplyJackpotContributions
- [ ] Kent data included in SnapshotJackpots DTO
- [ ] Serie counter increments per round, resets on close/cashout
- [ ] `dotnet build server/Lucky5.sln` passes with 0 errors

---

## Module B: Machine Close Threshold — Tier-Based + Manual Cashout

**Scope:** Make close threshold tier-dependent and change cashout from auto to manual.
**Agent type:** Backend .NET developer
**Estimated effort:** Medium
**Dependencies:** Module A (for updated jackpot values — affects close threshold sizing)

### Context
The current close threshold is a single fixed value (24M from `EngineConfig.CloseThreshold`). It needs to scale with machine tier. The close/cashout behavior needs to change from automatic to manual.

### Close Thresholds Per Tier
| Machine Tier | Bet Range | Close Threshold |
|---|---|---|
| Tier 1 (smallest) | 2,500 – 5,000 | 11,000,000 |
| Tier 2 | 5,000 – 10,000 | 22,000,000 |
| Tier 3 (largest) | 10,000 – 20,000 | 44,000,000 |

### Close Trigger Logic
- Check TWO independent conditions — machine freezes if **either** passes the threshold:
  1. `session.MachineCredits >= closeThreshold`
  2. `currentDoubleUpTotalScore >= closeThreshold`
- As long as credits ≤ (threshold - 1) AND DU total score ≤ (threshold - 1) → machine stays open
- Example on Tier 1 (threshold 11M): player with 10,999,999 credits and DU score of 3,000,000 → still open. Player with 11,000,000 credits (even with no DU) → frozen. Player with 5,000,000 credits but DU score of 11,000,000 → frozen.
- On freeze: machine enters CLOSED state, DU auto-settles at current amount

### Manual Cashout (No Auto-Transfer)
- Machine closes → credits do NOT auto-transfer to wallet
- Player sees "MACHINE CLOSED" and must manually press TAKE SCORE or CASH OUT
- Credits remain on machine display until player explicitly cashes out
- Cashout button transfers `session.MachineCredits` → `profile.WalletBalance`
- This preserves the "counting your winnings" feeling

### Files to Modify
- `server/src/Lucky5.Domain/Game/CleanRoom/CoreModels.cs` — `EngineConfig.CloseThreshold` (make tier-dependent or add per-tier config)
- `server/src/Lucky5.Infrastructure/Services/GameService.cs` — close trigger logic in DealAsync/DrawAsync/DoubleUp, cashout behavior
- `server/src/Lucky5.Api/wwwroot/js/game.js` — MACHINE CLOSED overlay, manual cashout UI state

### Acceptance Criteria
- [ ] Close threshold is tier-dependent (11M / 22M / 44M)
- [ ] Close checks BOTH credits AND DU total score independently — either ≥ threshold triggers freeze
- [ ] Machine does NOT auto-cashout on close — player must manually cash out
- [ ] "MACHINE CLOSED" overlay shown, TAKE SCORE / CASH OUT buttons active
- [ ] Credits remain visible on machine until player cashes out
- [ ] `dotnet build server/Lucky5.sln` passes

---

## Module C: Paytable Fix + Bug Fixes (Quick Wins)

**Scope:** Fix the client-facing paytable mismatch and the swap_double_up_card bug.
**Agent type:** Backend .NET developer
**Estimated effort:** Small (quick fixes)
**Dependencies:** None

### Fix 1: Paytable Mismatch
Two different paytables exist:
- `GameService.Rules` (client-facing): RF=1000, SF=75, 4K=15, FH=12, Fl=10, St=8, 3K=3, 2P=2
- `PaytableProfile.Lebanese` (actual payouts): RF=1000, SF=300, 4K=120, FH=20, Fl=14, St=10, 3K=6, 2P=4

**Fix:** Unify `GameService.Rules` dictionary to match `PaytableProfile.Lebanese` multipliers exactly.

### Fix 2: swap_double_up_card Bug
In `ExecuteCabinetCommandAsync` (~line 1352), the `swap_double_up_card` case calls `SwitchDealerAsync` instead of `SwapDoubleUpCardAsync`. The `swap_position` payload is never read.

**Fix:** Map to `SwapDoubleUpCardAsync(userId, roundId, swapPosition, cancellationToken)` and extract `swap_position` from payload.

### Files to Modify
- `server/src/Lucky5.Infrastructure/Services/GameService.cs` — `Rules` dictionary (lines 37-47), `ExecuteCabinetCommandAsync` (~line 1352)

### Acceptance Criteria
- [ ] `Rules` dictionary matches `PaytableProfile.Lebanese` exactly
- [ ] `swap_double_up_card` calls `SwapDoubleUpCardAsync` with correct payload
- [ ] `dotnet build server/Lucky5.sln` passes

---

## Module D: Stake Tiers + Bet Ramp + Last-Hand Behavior

**Scope:** Implement three machine tiers, the Lebanese bet counter ramp, and last-hand behavior for insufficient credits.
**Agent type:** Full-stack (.NET backend + vanilla JS frontend)
**Estimated effort:** Large
**Dependencies:** Module B (close threshold must be tier-aware)

### Stake Tiers
Define three machine tiers:
- Tier 1: MinBet=2500, MaxBet=5000, CloseThreshold=11,000,000
- Tier 2: MinBet=5000, MaxBet=10000, CloseThreshold=22,000,000
- Tier 3: MinBet=10000, MaxBet=20000, CloseThreshold=44,000,000

BET button cycles through bet levels (min → max → min) with 100-credit intervals.

### Bet Ramp (Lebanese Cabinet Core Mechanic)
In real Lebanese cabinets, pressing BET starts a visual counter that ramps from 0 to the bet amount in 100-credit increments, simultaneously draining credits from the CREDIT display.

**Backend:**
- The ramp is presentation-only — credit deduction happens at ramp completion
- Server validates `session.MachineCredits >= betAmount` before allowing ramp to complete
- If credits run out mid-ramp, counter stops at whatever was drained — that becomes the actual bet

**Frontend:**
- BET press → `startBetRamp()`: counter ticks 0 → target, 100 per tick, ~50ms per tick
- CREDIT counts DOWN simultaneously (mechanical drain feel)
- Paytable values update every tick: `multiplier × currentBetAmount`
- After ramp completes: brief settle (~200ms), DEAL DRAW activates, BET deactivates
- Sound: rapid credit-tick synchronized with counter

### Last-Hand Behavior
If `session.MachineCredits < machine.MinBet` but `> 0`:
- Allow deal with `betAmount = session.MachineCredits`
- Paytable scales: `multiplier × actualBet`
- Mark as "last hand" — after round settles, if credits = 0 and no win → auto cash-out
- Remove the `MachineCredits < betAmount * 2m` hard gate for last-hand scenarios

### Files to Modify
- `server/src/Lucky5.Infrastructure/Services/GameService.cs` — DealAsync validation, bet ramp endpoint, last-hand logic
- `server/src/Lucky5.Domain/` — Machine configuration (tier definitions)
- `server/src/Lucky5.Api/wwwroot/js/game.js` — startBetRamp(), handleBetRampTick(), commitBetToServer(), last-hand UI
- `server/src/Lucky5.Api/wwwroot/js/cabinet-stage-vnext.js` — bet counter DOM rendering
- `server/src/Lucky5.Api/wwwroot/js/game-config.js` — ramp timing (tickMs: 50, stepSize: 100)

### Acceptance Criteria
- [ ] Three machine tiers defined with correct min/max bet and close thresholds
- [ ] BET button cycles through bet levels with 100-credit intervals
- [ ] Bet ramp animation: counter ticks 0 → target, credit drains simultaneously
- [ ] Paytable updates in real-time during ramp
- [ ] If credits < min bet: last-hand mode with scaled paytable
- [ ] After last hand settles with 0 credits: auto cash-out
- [ ] `dotnet build server/Lucky5.sln` passes

---

## Module E: RTP Engine Fairness Overhaul

**Scope:** Eliminate all detectable scripted patterns in the RTP controller while maintaining 80% composite RTP convergence.
**Agent type:** Backend .NET developer with math/probability understanding
**Estimated effort:** Large
**Dependencies:** None (can start immediately, operates on MachinePolicy.cs only)

### The Problem
The current RTP engine has 6 detectable patterns:
1. Every session starts with 15-20% above equilibrium (warmup burst)
2. 4→8→12 consecutive loss tiers create predictable relief waves
3. Hard thresholds at 12M/18M/24M create visible cliffs
4. Double-up deck rebuilds after each winning step (progressive difficulty ratchet)
5. Deck pressure is entirely deterministic given state
6. Cooldown after wins is nearly constant at 2 rounds

### Fix 2.1: Remove Warmup Generosity Burst
- Remove `WarmupOpeningSmallScale`/`MediumScale`/`BigScale` boosts
- Keep `RtpMinSamplesForControl = 30` (controller inactive, not boosted)
- During warmup, use `DefaultPayoutScale` (1.15) — baseline, not generous
- Remove the `0.08 × (1 - progress)` warmup bias in `ResolveLivePayoutScale`

### Fix 2.2: Continuous Noisy Pity Function
Replace discrete tiers with sigmoid:
```
pityBoost = PityBoostCap × sigmoid((consecutiveLosses - 6) / 3)
pityBoost += rng.NextDouble(-0.02, 0.02)  // jitter
```
- **Remove dual-channel relief**: pity affects base-game scale ONLY, NOT double-up deck
- Replace `StreakSoftThreshold`/`HardThreshold`/`CrisisThreshold` with continuous function

### Fix 2.3: Continuous Soft Cap Pressure
Replace discrete thresholds with sigmoid:
```
capPressure = sigmoid((credits - 15M) / 3M)
```
- Mode shift to Hot when `capPressure > 0.5` (~15M), not at hard 18M cliff
- Close threshold remains hard (structural), but approach is continuous

### Fix 2.4: Eliminate Per-Step DU Deck Rebuild
- Build DU deck **once** when double-up session starts
- Do NOT rebuild between steps within a single DU chain
- Deck pressure applies to initial construction only
- Long chains become genuinely possible AND genuinely risky

### Fix 2.5: Add Genuine Randomness to Deck Pressure
- 8% chance per DU session: pressure inverted (hard→easy, easy→hard)
- Randomize removal budget: `ceil(pressure × rng.Next(15, 29))` instead of fixed `ceil(pressure × 29)`
- Vary jitter per round: `rng.NextDouble(0.01, 0.04)` instead of fixed 0.02

### Fix 2.6: Randomize Cooldown Duration
Replace fixed `CooldownLength = 2` with weighted random:
- 1 round: 20%, 2 rounds: 35%, 3 rounds: 25%, 4 rounds: 15%, 5 rounds: 5%

### Files to Modify
- `server/src/Lucky5.Domain/Game/CleanRoom/MachinePolicy.cs` — ALL of: ResolvePayoutScale, ResolveLivePayoutScale, ComputePityBoost, BuildDoubleUpPlayDeck, cooldown assignment, cap pressure

### Acceptance Criteria
- [ ] No warmup generosity burst (first 30 rounds use baseline scale)
- [ ] Pity is continuous sigmoid, not discrete tiers
- [ ] Pity affects base-game scale ONLY, not DU deck
- [ ] Soft cap pressure is continuous sigmoid, not discrete thresholds
- [ ] DU deck built once per session, not per step
- [ ] Deck pressure has randomized elements (anomaly, removal budget, jitter)
- [ ] Cooldown duration is weighted random 1-5
- [ ] `dotnet build server/Lucky5.sln` passes
- [ ] Engine still converges to ~80% RTP (verify with existing simulation)

---

## Module F: Visual Parity — Animation + Layout

**Scope:** Match ai9 reference animation speeds, title screen, bet ramp animation, and layout details.
**Agent type:** Frontend (vanilla JS, CSS, DOM manipulation)
**Estimated effort:** Large
**Dependencies:** None (can start immediately)

### Reference Sources
- ai9 video frames: `mediafiles/ai999_frames/` (2885 frames) and `mediafiles/frames_ai9/` (192 frames)
- ai9 analysis: `mediafiles/video_ScreenRecording2026-06-2714.54_analysis_20260627_121259.md`
- ai9 Flutter source: `ai9/ai9-download/install/main.dart.js` (4.5MB compiled)
- v8 current frames: `mediafiles/frames_v8/` (192 frames) for comparison

### Fix 3.1: Deal/Draw Animation Speed (CRITICAL FEEL GAP)
- ai9: 50-80ms stagger, total deal ~300-400ms, feels "snappy, mechanical"
- v8: 183ms stagger, total deal ~900ms
- **Reduce `staggerFrames` from 11 to 3-5** (50-83ms @ 60Hz)
- Keep thump effect but faster — settle within stagger window
- Draw: unheld cards vanish instantly (already correct), replacements at same reduced rate

### Fix 3.2: Title Screen — "LUCKY 5 POKER" + Ace of Diamonds
- Add "POKER" subtitle below "LUCKY 5"
- Show single Ace of Diamonds in center during initial title display
- After idle delay (~2.5s): title fades, FH rank card appears in middle slot (slot 2)
- Other slots remain pure black (#000000)
- Background when no cards: pure black, not dark gray

### Fix 3.3: Bet Ramp Animation (Frontend)
- BET press → counter ticks 0 → target in 100-credit steps (~50ms/tick)
- CREDIT counts DOWN simultaneously
- Paytable values update every tick: `multiplier × currentBet`
- If credits < target: counter stops at credits remaining
- After ramp: settle (~200ms), DEAL DRAW activates

### Fix 3.4: Background Pure Black + Watermark When Cards Absent
- Card slot backgrounds: pure black (#000000) when empty
- Card removal: instant snap to black, no fade, no lingering
- **Watermark during gameplay:** Large "LUCKY 5 POKER" text in cyan (#00FFFF) at low opacity spans nearly full width behind card area, with neon phosphor glow bleeding into black background. Always present behind cards during play.
- Card edges: no distinct border — the white card face simply ends against the black background (pixel boundary, not a drawn stroke)

### Fix 3.8: Win Display and Credit Drain
- Win amount: flashing yellow text near credit display
- TAKE SCORE drain: win counts down, credit counts up, ~1-1.5s for typical wins
- Paytable winning row: solid white background + black text (inverted)
- After drain: highlight clears
- **HOLD indicator on winning cards:** Small white "HOLD" text appears beneath each card that is part of the winning combination

### Fix 3.9: DU Shuffle Speed
- ai9: 16-33ms per card change, creates blur effect
- v8: 130ms — way too slow
- **Reduce `shuffleFrameMs` from 130 to 30-50**
- Blur should be prominent — faces cycling almost unreadable
- Stop is instantaneous on BIG/SMALL press

### Fix 3.11: DU Mode Labels + Instruction Text
- Update rule panel: "ACE ALWAYS WIN" (not "ACE COUNTS HI OR LO")
- Update: "5 NEVER LOSE WHEN BUYING" (not "5 ♠ NEVER LOSE WHEN BUYING")
- Keep SERIE and KENT /3 visible during DU mode
- DU label: "DOUBLE UP" in cyan
- **Contextual instruction text:** "PRESS HOLDS TO KEEP CARD" appears below cards during hold phase in white pixel font

### Fix 3.12: Control Deck — Woodgrain Texture + Button States
- **Background:** Textured gradient from dark brown to orange/amber, simulating polished woodgrain/laminate arcade cabinet panel
- **Button 3D bevel:** All buttons have lighter top/left edges + darker bottom/right shadows (raised appearance)
- **Button depress:** On click, bevel inverts (dark top/left, light bottom/right) creating a "pressed in" effect
- **Button layout (3 rows, matching ai9 exactly):**
  - Row 1: 5 × HOLD (Bright Yellow), aligned directly beneath the 5 card positions
  - Row 2: BIG (Orange) | SMALL (Orange) | CANCEL HOLD (White/Light Gray) | DEAL DRAW (Bright Red) | BET (Bright Green)
  - Row 3: TAKE HALF (Bright Red) | MENU (black circular hamburger icon — NOT a 3D button) | TAKE SCORE (Orange)
- **Button text:** Black, centered, sans-serif pixel font

### Fix 3.13: Jackpot Info Block + Credit/Stake Display
- **Jackpot block** (lower-left, small pixel font):
  - `SERIE` (Red text)
  - `KENT /3 - N` (Red + White text, N = current streak count)
  - Jackpot values: cyan, red, and white mixed — format: `{value} {max} x {cap}`
  - `S/N: {serial}` (Cyan text)
- **Credit/Stake display** (top-right):
  - `CREDIT`: Label in Cyan, value in Green — right-aligned
  - `STAKE`: Label in Cyan, value in Yellow/Orange (#FFB800) — right-aligned
  - Both use blocky monospaced pixel font

### Files to Modify
- `server/src/Lucky5.Api/wwwroot/js/game-config.js` — `staggerFrames`, `dealBaseFrames`, `dealDurationFrames`, `drawStaggerFrames`, `shuffleFrameMs`, ramp timing
- `server/src/Lucky5.Api/wwwroot/js/cabinet-stage-vnext.js` — idle state rendering, card removal, bet counter, DU shuffle speed, watermark rendering
- `server/src/Lucky5.Api/wwwroot/js/game.js` — DU mode text, bet ramp functions, win display, HOLD indicators, instruction text, button depress animation
- `server/src/Lucky5.Api/wwwroot/index.html` — title screen markup, button panel structure, jackpot block structure
- `server/src/Lucky5.Api/wwwroot/css/game.css` — control deck background, button 3D bevel, button layout

### Acceptance Criteria
- [ ] Deal animation: 50-80ms stagger, total ~300-400ms
- [ ] Title screen shows "LUCKY 5 POKER" with 3D drop-shadow + single Ace of Diamonds center
- [ ] Bet ramp: counter ticks 0→target, credit drains, paytable updates
- [ ] Card backgrounds: pure black when empty; "LUCKY 5 POKER" cyan watermark visible behind cards
- [ ] Cards: pure white, NO border (pixel-edge), correct pip/rank layout
- [ ] Win display: flashing yellow, drain ~1-1.5s, white "HOLD" text under winning cards
- [ ] DU shuffle: 30-50ms, blur effect, instant stop
- [ ] DU labels match ai9: "ACE ALWAYS WIN", "5 NEVER LOSE WHEN BUYING"
- [ ] Instruction text: "PRESS HOLDS TO KEEP CARD" during hold phase
- [ ] Control deck: woodgrain gradient, 3D bevel buttons with invert-on-press
- [ ] HOLD buttons aligned directly under 5 card positions
- [ ] MENU button: black circle hamburger icon, not 3D button
- [ ] Jackpot block: SERIE, KENT /3, value format, S/N displayed
- [ ] Credit: Cyan label + Green value; Stake: Cyan label + Yellow value

---

## Module G: Visual Parity — CSS + Styling

**Scope:** Match ai9 reference paytable colors, card style, CRT effects, and banner styling.
**Agent type:** Frontend (CSS, visual design)
**Estimated effort:** Medium
**Dependencies:** None (can start immediately)

### Fix 3.5: Paytable Row Colors (match ai9 exactly)
- ROYAL FLUSH: Yellow/Orange (#FFB800)
- STRAIGHT FLUSH: Red (#FF0000)
- 4 OF A KIND: Green (#00FF00)
- FULL HOUSE: White (#FFFFFF) — **always** enclosed in a solid white rectangular outline box (permanent, not just when active). When active: box fills solid white, text inverts to black.
- FLUSH: Cyan/Light Blue (#00FFFF)
- STRAIGHT: Yellow (#FFFF00)
- 3 OF A KIND: Orange (#FFA500)
- 2 PAIR: Cyan/Light Blue (#00FFFF)
- Payout values: right-aligned, same color as hand name text
- All text uses classic 8-bit/16-bit monospaced pixel font, ALL CAPS

### Fix 3.6: Card Style — Pure White, No Border (Pixel-Edge)
- Background: pure white (#FFFFFF), not ivory
- **No visible border** — the white card face ends at a pixel boundary against the black background. No drawn stroke, no gold inset, no box-shadow.
- Remove all existing gold inner border (#d4af37) and box-shadow inset styling
- Keep pixel font for ranks/suits
- Suit colors: Hearts/Diamonds = bright red (#FF0000), Spades/Clubs = solid black (#000000)
- Rank in top-left and bottom-right (rotated 180°), miniature suit pip next to each rank
- Center pip: single large blocky pixelated suit character dominating the card center
- Face cards: simplified retro pixel-art (blocky color zones, no intricate detail)

### Fix 3.7: CRT Effect — Phosphor Glow + Pixel Grid + Subtle Scanlines
- Three-layer CRT simulation:
  1. **Pixel grid:** Coarse square-pixel grid overlay across entire digital display (top ~70%), creating visible individual RGB-like subpixels
  2. **Phosphor bloom/glow:** text-shadow with spread on cyan, green, yellow, white — glow bleeds slightly into black background
  3. **Scanlines:** Very subtle horizontal lines — secondary to pixel grid, not the dominant effect
- CRT black level: very dark slightly-illuminated navy-gray (powered-on CRT idle glow), NOT pure #000000
- Subtle screen curvature at edges and chromatic aberration on high-contrast text edges

### Fix 3.10: "4 OF A KIND WINS BONUS" Banner
- Split into two spans: "4 OF A KIND" (yellow, larger) + "WINS BONUS" (white, smaller)
- Always visible below jackpot info block

### Files to Modify
- `server/src/Lucky5.Api/wwwroot/css/game.css` — card styling, backgrounds
- `server/src/Lucky5.Api/wwwroot/css/cabinet-v8-quality.css` — card face CSS, button styles
- `server/src/Lucky5.Api/wwwroot/css/cabinet-ai9-parity.css` — paytable colors, CRT effects
- `server/src/Lucky5.Api/wwwroot/index.html` — banner markup

### Acceptance Criteria
- [ ] Paytable colors match ai9 exactly (8 rows)
- [ ] FH row: ALWAYS has white outline box; fills solid white + black text when active
- [ ] Cards: pure white, NO border (pixel-edge against black background)
- [ ] CRT: three-layer effect — pixel grid (dominant) + phosphor glow + subtle scanlines
- [ ] CRT black level: very dark navy-gray, NOT pure black
- [ ] Screen curvature + chromatic aberration on text edges
- [ ] Banner: "4 OF A KIND" (yellow) + "WINS BONUS" (white)

---

## Module H: Sound Design

**Scope:** Replace single `press.mp3` with 15+ distinct audio files for all game events.
**Agent type:** Frontend (audio integration)
**Estimated effort:** Medium
**Dependencies:** Module F (animation timing affects audio sync)

### Audio Files Needed
| File | Event | Notes |
|------|-------|-------|
| `deal.mp3` | Card landing | Per card, ~50ms to match faster stagger |
| `hold.mp3` | HOLD button | Mechanical click feel |
| `draw.mp3` | Card flip/replacement | Distinct from deal |
| `win-small.mp3` | 2P/3K win | Gentle chime |
| `win-medium.mp3` | Straight/Flush/FH | 3-tone ascending |
| `win-big.mp3` | 4K/SF win | Fanfare |
| `win-jackpot.mp3` | Jackpot win | Extended fanfare + counter tick |
| `du-shuffle.mp3` | DU reel spin | Rapid clicking, matches 30-50ms cycle |
| `du-reveal.mp3` | DU card snap | Brief sustain |
| `du-win.mp3` | DU correct guess | Ascending tone |
| `du-lose.mp3` | DU wrong guess | Descending buzz |
| `lucky5.mp3` | 5♠ SafeFail | Ethereal chime |
| `machine-close.mp3` | Machine freeze | Alarm/bell + sustained counter tick |
| `bet-ramp.mp3` | Bet counter | Rapid credit drain ticking |
| `credit-tick.mp3` | Credit digit | Individual tick |

### Files to Modify
- `server/src/Lucky5.Api/wwwroot/assets/audio/` — new audio files
- `server/src/Lucky5.Api/wwwroot/js/game-config.js` — audio event mappings
- `server/src/Lucky5.Api/wwwroot/js/cabinet-audio-vnext.js` — audio playback engine

### Acceptance Criteria
- [ ] Each event has a distinct audio file (not all press.mp3)
- [ ] Deal sounds staggered with card landings at new faster rate
- [ ] DU shuffle audio matches 30-50ms cycle speed
- [ ] Bet ramp audio synchronized with 50ms counter ticks

---

## Module I: Simulation Harness Improvements

**Scope:** Add player-experience metrics to validate that the engine feels fair, not just that aggregate RTP is correct.
**Agent type:** Backend .NET developer with statistics knowledge
**Estimated effort:** Medium
**Dependencies:** Module E (RTP engine changes must be in place first)

### New Metrics to Add
1. **Autocorrelation test**: lag-1 autocorrelation of win/loss sequence. Target: |r| < 0.05
2. **Streak distribution**: P50/P95/P99 consecutive loss lengths. Target: P95 < 15
3. **Session outcome distribution**: histogram of per-session returns (should be bell-shaped, not bimodal)
4. **Pattern detection**: runs test for 5 most common 3-round patterns (LLL, LLW, LWL, WLL, WLW). Each within 5% of expected frequency
5. **Warmup detection**: first-30-round RTP vs last-30-round RTP across 1000 sessions. Should not differ by > 5%

### Files to Modify
- `server/src/Lucky5.Simulation/Program.cs`

### Acceptance Criteria
- [ ] All 5 new metrics implemented and reported
- [ ] Simulation runs cleanly with all Module E changes
- [ ] RTP still converges to ~80% ± 2%
- [ ] Autocorrelation |r| < 0.05
- [ ] P95 consecutive loss < 15

---

## Module J: Dead Code Cleanup

**Scope:** Remove dead functions, methods, and legacy shims across backend and frontend.
**Agent type:** Full-stack (safe deletions)
**Estimated effort:** Small
**Dependencies:** None (safe to do anytime)

### Items to Remove
- `animateReverseDrain()` in `game.js` (~60 lines, dead function)
- `BuildCabinetEnabledButtons()` in `GameService.cs` (dead method, never called)
- Legacy `DoubleUpAsync()` facade in `GameService.cs` (superseded by individual methods)
- Inline-styled spectator count overlay in `game.js` → move to CSS class
- `du-board-canvas.js` placeholder card rendering → use actual DOM card templates
- Legacy fallback `renderCards()` in `game.js` (if `enableCabinetStage: true` is permanent)

### Files to Modify
- `server/src/Lucky5.Api/wwwroot/js/game.js`
- `server/src/Lucky5.Api/wwwroot/js/cabinet-stage-vnext.js`
- `server/src/Lucky5.Infrastructure/Services/GameService.cs`

### Acceptance Criteria
- [ ] All identified dead code removed
- [ ] No functional regressions (all game flows still work)
- [ ] `dotnet build server/Lucky5.sln` passes

---

## Key Invariants (All Modules Must Preserve)
- 80% composite RTP target
- 5-minute disconnect grace period
- Wallet balance NEVER shown during gameplay
- Cards always revealed one at a time
- Lebanese paytable multipliers (only fix client-facing mismatch)
- SplitMix64 deterministic RNG (cryptographically sound)
- Retro cabinet aesthetic (match ai9 quality, not modern casino UI)

## Critical Files Conflict Map
| File | Modules |
|------|---------|
| `GameService.cs` | A, B, C, D |
| `CoreModels.cs` | A, B, E |
| `MachinePolicy.cs` | E |
| `game.js` | B, D, F, J |
| `game-config.js` | D, F, H |
| `cabinet-stage-vnext.js` | D, F, G, J |
| `*.css` | G |
| `index.html` | F, G |
| `Simulation/Program.cs` | I |

**Conflict note:** Modules A, B, C, D all touch `GameService.cs`. They should be done sequentially (A → B → C → D) or by the same agent to avoid merge conflicts.
