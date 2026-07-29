# Fix Three Critical Gameplay Issues

**Date**: 2026-07-29  
**Task**: Fix deal/draw stagger timing, add global input lock, fix jackpot drain animation

## Summary

Fixed three critical gameplay issues in the Lucky5 v8 cabinet frontend:

### Issue 1: Deal/Draw Stagger Timing
- Changed `staggerFrames` from 11 to 8 (~133ms per card, down from ~183ms)
- Updated all dependent frame values: `dealBaseFrames: 4`, `dealDurationFrames: 8`, `drawStaggerFrames: 8`, `drawDurationFrames: 8`, `drawRevealStartFrames: 4`
- Total 5-card deal now ~530ms (down from ~900ms) — snappy but readable arcade pace

### Issue 2: Global Input Lock During Animations
- Added `_actionLock` and `jackpotDrainActive` module-scope guard flags in `game.js`
- Added guard checks to `doDeal()`, `startDoubleUpFlow()`, and `doDoubleUp()` entry points
- Added `_actionLock = false` release in all `doDoubleUp()` exit paths (Win, SafeFail, MachineClosed, Loss, Catch)
- Added lock/release in `proceedToDoubleUp` — sets `_actionLock = true` before calling `startDoubleUpFlow()`
- Added lock/release in `exitDoubleUp()` — releases both `_actionLock` and `jackpotDrainActive`, re-enables all buttons
- All button clicks now blocked during any active animation (deal, draw, DU, jackpot drain)

### Issue 3: Jackpot Drain Animation Fixed
- Added console logging in `animateJackpotFill` to verify which counter element is found
- Added visual freeze effect: `#card-area.frozen` class dims and disables pointer events during jackpot drain
- Added "JACKPOT COLLECTED!" message shown for 1.5s after drain completes before entering DU
- Added `jackpotDrainActive` flag set true during jackpot animation, checked by all button handlers
- Explicitly disables all `.cab-btn` buttons during jackpot drain via `pointerEvents: 'none'`
- After jackpot drain, DU session uses normal paytable (existing `highlightPaytableDU` call with correct `handName`)

### Files Modified
- `server/src/Lucky5.Api/wwwroot/js/game-config.js` — timing values (v13)
- `server/src/Lucky5.Api/wwwroot/js/game.js` — input lock guards, jackpot drain fixes (v35)
- `server/src/Lucky5.Api/wwwroot/js/cabinet-orchestrator-vnext.js` — version bump (v3)
- `server/src/Lucky5.Api/wwwroot/index.html` — version string bumps
- `server/src/Lucky5.Api/wwwroot/css/game.css` — `.frozen` CSS class for jackpot drain visual

### Verification
- `dotnet build server/Lucky5.sln` — **0 errors**, 1 pre-existing CS7022 warning (test entry point)
