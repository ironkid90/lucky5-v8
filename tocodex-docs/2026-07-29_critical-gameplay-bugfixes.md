# Critical Gameplay Bugfixes — 2026-07-29

## Summary
Fixed three critical gameplay issues: stagger timing too fast, double-click input locking missing, and jackpot drain animation broken.

## Changes Made

### 1. Deal/Draw Stagger Timing
- **File**: [`game-config.js`](server/src/Lucky5.Api/wwwroot/js/game-config.js:65)
- `staggerFrames: 8` (~133ms/card) — was 11 (~183ms)
- `dealBaseFrames: 4`, `dealDurationFrames: 8`, `drawStaggerFrames: 8`, `drawDurationFrames: 8`, `drawRevealStartFrames: 4`
- Total 5-card deal ~530ms — snappy arcade pace, readable

### 2. Global Input Lock
- **File**: [`game.js`](server/src/Lucky5.Api/wwwroot/js/game.js:135)
- Added `_actionLock` and `jackpotDrainActive` guard flags at module scope
- Guard checks on `doDeal()`, `startDoubleUpFlow()`, `doDoubleUp()` entry points
- Lock set before `startDoubleUpFlow()` in `proceedToDoubleUp`, released in all DU exit paths
- Prevents double-clicking BIG/SMALL from triggering two DU sessions

### 3. Jackpot Drain Animation
- **File**: [`game.js`](server/src/Lucky5.Api/wwwroot/js/game.js:2143)
- `jackpotDrainActive = true` freezes all input during drain
- Visual freeze: `#card-area.frozen` CSS class (dimmed + pointer-events disabled)
- All `.cab-btn` buttons explicitly disabled via `pointerEvents: 'none'`
- "JACKPOT COLLECTED!" message shown for 1.5s after drain before DU entry
- After jackpot drain, DU uses normal paytable for the hand rank

### 4. CSS Freeze Overlay
- **File**: [`game.css`](server/src/Lucky5.Api/wwwroot/css/game.css:647)
- `#card-area.frozen` class: pointer-events none, opacity dim

### 5. Version Bumps
- `game-config.js?v=13`, `game.js?v=35`, `cabinet-orchestrator-vnext.js?v=3`

## Verification
- Build: `dotnet build server/Lucky5.sln` — 0 errors
- Stagger: ~133ms per card, total ~530ms for 5 cards
- Input lock: all buttons blocked during deal/draw/DU/jackpot-drain
- Jackpot drain: freeze → drain → 1.5s collect message → DU with normal paytable
