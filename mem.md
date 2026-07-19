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

- **One global stagger**: `staggerFrames: 12` (200ms) drives ALL card reveals (deal, draw, DU)
- All card animation uses `CabinetClock.delayTicks()` — zero ms→frame conversion jitter
- Config: `game-config.js` → frame values (`staggerFrames`, `dealBaseFrames: 5`, `dealDurationFrames: 11`)
- Legacy ms aliases via computed getters for backward compat with game.js fallback paths
- Total deal: ~900ms (5 cards), animation: cards appear in-place with scale-pop "thump" effect (`.card-deal-thump` / `v8-card-thump-in` keyframe: scale 0.65→1.08→0.94→1.02→1.0). No off-screen slide or drop-from-above. Draw replacement: `.card-draw-thump` / `v8-card-draw-thump` keyframe (scale 0.55→1.10→0.92→1.03→1.0), only non-held cards animate. Old `deal-in`/`slide-in` classes (translateY(-100%) drop) removed.

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
| `game.js?v=30` | Core engine, state machine, DU logic, fallback render paths |
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
