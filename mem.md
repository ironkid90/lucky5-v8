# Lucky5 v8 Project Memory — Single Source of Truth

## Architecture

- **Primary client**: vanilla HTML/CSS/JS cabinet in `server/src/Lucky5.Api/wwwroot/`
- **Backend authority**: .NET 10 under `server/src/Lucky5.Domain/Game/CleanRoom/` (deterministic RNG, payouts, jackpots, credits, recovery, double-up)
- **Launch**: `./dev.ps1` → <http://localhost:5051>
- **Tests**: `dotnet run --project server/tests/Lucky5.Tests/Lucky5.Tests.csproj`
- **Persistence**: in-memory by default

## Visual Target

- AI9/ai9poker-style portrait (9:16) arcade cabinet parity
- Retro, tactile, asset-driven. Do NOT modernize into flat casino UI.
- CSS override chain: `cabinet-layout-vnext.css` → `game.css` → `cabinet-v8-*.css` → `cabinet-ai9-parity.css` (final authority)

## Card Design Standards

- 2.5:3.5 aspect ratio, pure white background, 1px solid black border, border-radius: 0
- DOM-based card faces (`.card-front .card-corner .card-suit-large`) — no PNG card assets
- `.du-card-slot` sizing: `flex: 0 0 16%`, card frames use `aspect-ratio: 313/528`
- Font sizing: `clamp()` in DU mode, `cqh` units for main hand (game-screen container query context)
- `.active-win-row`: solid white bg (#ffffff) + black text, no cyan glow or text-shadow

## VSYNC-Locked Timing (60Hz Cabinet Clock)

- **One global stagger**: `staggerFrames: 11` (or `12` in wildwitch) drives ALL card reveals (deal, draw/redeal, DU). Deal and redeal (draw) timings are strictly equal and identical (`staggerFrames`, `dealBaseFrames: 5`, `dealDurationFrames: 11`).
- All card animation uses `CabinetClock.delayTicks()` — zero ms→frame conversion jitter
- Config: `game-config.js` → frame values (`staggerFrames: 11`, `dealBaseFrames: 5`, `dealDurationFrames: 11`, `drawStaggerFrames: 11`, `drawRevealStartFrames: 5`)
- Legacy ms aliases via computed getters for backward compat with game.js fallback paths
- Total deal / full redeal: ~900ms (5 cards), animation: cards appear in-place with scale-pop "thump" effect (`.card-deal-thump` / `v8-card-thump-in` keyframe: scale 0.65→1.08→0.94→1.02→1.0). No off-screen slide or drop-from-above. Draw replacement: `.card-draw-thump` / `v8-card-draw-thump` keyframe (scale 0.55→1.10→0.92→1.03→1.0), only non-held cards animate. Old `deal-in`/`slide-in` classes (translateY(-100%) drop) removed.

## Button System

- PNG assets in `wwwroot/assets/images/` mapped by CSS class: hold, big, small, cancel_hold, deal_draw, bet, take_half, take_score, menu
- DEAL DRAW = red, BET = green, HOLD = yellow
- `::before` pseudo-elements show text labels as fallback
- Disabled state: opacity 0.6-0.75 (reduced from 0.35), grayscale(0.3) brightness(0.7-0.85)
- Menu panel: `position: fixed; z-index: 9999/10000`
- Admin modal (cash-in/cash-out): `position: fixed; z-index: 20000` — must be above menu

## Cabinet Stage (cabinet-stage-vnext.js)

- `dealCards()` / `drawCards()`: cards appear in-place with scale-pop "thump" (`.card-deal-thump` / `.card-draw-thump`)
- Pre-animation hidden states via CSS classes (`.card-pre-deal` / `.card-pre-draw`), not inline styles
- Deck-wide shadow burst on first card landing (`.card-area-thump`)
- `enterDoubleUp()` / `updateDoubleUpTrail()` / `exitDoubleUp()`: DU mode lifecycle
- All timing via `CabinetClock.delayTicks()` with frame counts from `_config`
- Shuffle: `shuffleFrameMs: 30` (rapid reel blur), uses `delayTicks(frameTicks)` for loop

## Key Files

| File | Role |
|------|------|
| `index.html?v=...` | Cabinet shell, CSS/JS load order, cache-bust versions |
| `game-config.js?v=9` | VSYNC timing, variant identity, paytable, rules, assets, audio |
| `game.js?v=32` | Core engine, state machine, DU logic, fallback render paths |
| `cabinet-stage-vnext.js?v=10` | Card choreography, hold lamps, DU viewport — all `delayTicks()` |
| `cabinet-clock.js?v=1` | 60Hz deterministic tick clock, `delayTicks()`, `CabinetInput` scanner |
| `cabinet-orchestrator-vnext.js?v=2` | State sync, button guards, input capture, deal verification |
| `cabinet-ai9-parity.css?v=4` | Final CSS authority: geometry, z-index, button PNGs, admin modal |
| `cabinet-v8-quality.css?v=9` | Card face CSS, button styles, DU card sizing, disabled states, thump keyframes |

## Cache Busting

- All CSS/JS have version query strings in index.html
- Bump on every change: `?v=N` → `?v=N+1`

## Known Pitfalls

- Stale browser CSS cache can blank paytables/buttons — versioned query strings fix this
- Admin modal was `position: absolute` (trapped in game-screen stacking context) → now `position: fixed; z-index: 20000`
- `.card-suit-large` overflows smaller DU frames without `overflow: hidden` + `clamp()` sizing
- `cqh` units require container query context on `#game-screen` (container-type: size)
- **Build OOM**: If `dotnet build` fails with `OutOfMemoryException` in `GenerateGlobalUsings`, kill the running `Lucky5.Api` process first (it locks the DLLs)

## RTP Engine — Current State (2026-07-28)

**Target:** 80% composite RTP, configurable via `EngineConfig.TargetRtp`.

**RTP Composition:**
- Base game: ~45-50% (controlled by payout scale, target ~42%)
- Double-up: ~35-40% (2 switches + ace auto-win + 5♠ no-lose, pressure-regulated)
- Jackpot: ~3.25% (progressive pools, accumulates slowly)
- **Total target:** 80% via controller auto-adjustment

**Key EngineConfig Values (CoreModels.cs):**
- `TargetRtp = 0.80` — overall RTP target
- `TargetDoubleUpRtp = 0.35` — accepts generous DU (2 switches + ace auto-win)
- `MinimumObservedBaseRtp = 1.50` — un-scaled Lebanese paytable EV (~1.48)
- `MinPayoutScale = 0.25` — floor for controller scaling
- `MaxCorrection = 0.28` — max controller adjustment per step
- `MaxDriftClamp = 0.20` — how much drift the controller can detect
- `DoubleUpRtpHardCap = 0.40` — DU leak clamp threshold
- `DoubleUpPressureMaxKeyRemovals = 35` — max cards removed under pressure
- `CloseThreshold = 40_000_000` — machine close threshold

**Double-Up Game Rules (preserved from original):**
- `MaxSwitchesPerRound = 2` — player sees 3 dealer cards (original + 2 switches)
- `AceCountsHiOrLo = true` — ace auto-wins on challenger position
- `LuckyFiveArmsNoLose = true` — 5♠ found via switch arms no-lose mode
- `FirstLuckyMultiplier = 4` — first 5♠ switch: 4× amount
- `RepeatLuckyMultiplier = 2` — subsequent 5♠ switches: 2× amount

**Deck Pressure System (MachinePolicy.cs) — "Lam3a Engine":**
- **Cold mode (machine running hot):** Removes aces, ADDS duplicate middle ranks (7,8,9,6,10) for close calls
- **Recovery mode (machine running cold):** Adds 5♠ (30% chance), high cards, trap cards
- **Anomaly:** 15% chance per DU session to invert pressure direction entirely
- **Effect:** DU feels tense (close calls, ties, "2 vs 3") not scripted. Every round feels like it could go either way.

**Controller Feedback Loop:**
1. Observed RTP drifts above target → Cold mode kicks in
2. Cold mode removes aces + adds middle ranks → DU win rate drops
3. Players get more close calls → take score earlier → DU contribution drops
4. Observed RTP converges toward target
5. 15% anomaly: sometimes the deck HELPS the player unexpectedly (surprise streaks), sometimes BURNS them

**Simulation Limitations:**
- The simulation uses approximate hand frequencies, not the real draw engine
- Jackpot RTP is inflated (~19% vs real game's ~3.25%) due to simplified progressive pool
- The real game's controller converges to 80% because its jackpot contribution is much lower
- Do NOT rely on simulation RTP numbers as ground truth — they verify the controller LOGIC works, not exact percentages
