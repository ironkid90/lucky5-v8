# AI9 Parity Color & Label Audit

**Date**: 2026-07-29  
**Task**: Compare frontend implementation against AI9 reference and fix all visual/color/label mismatches

## Changes Made

### 1. STRAIGHT Paytable Row Color
**File**: `server/src/Lucky5.Api/wwwroot/css/cabinet-ai9-parity.css` (line 523)
- Changed `#ffff00` (yellow) → `#00ffff` (cyan) for `.pay-row.st`
- AI9 reference uses cyan for STRAIGHT, not yellow

### 2. CRT Black Level
**File**: `server/src/Lucky5.Api/wwwroot/css/cabinet-ai9-parity.css` (line 464)
- Changed `#0a0d14` → `#0a0a0a` for `#game-screen` background
- Pure dark gray instead of navy-tinged black

### 3. DU Title Color
**File**: `server/src/Lucky5.Api/wwwroot/css/cabinet-labels-vnext.css` (line 302)
- Changed `#ffcc00` (gold) → `#00ffff` (cyan) for `#du-title`
- DU panel title matches AI9 cyan accent

### 4. DU Copy Labels (Main Config)
**File**: `server/src/Lucky5.Api/wwwroot/js/game-config.js` (lines 219-227)
- `label`: `'HI LO GAMBLE'` → `'DOUBLE UP'`
- `aceRule`: `'ACE COUNTS'` → `'ACE ALWAYS WIN'`
- `guessRule`: `'GUESS HIGHER OR LOWER'` → `''` (empty — not used in AI9)
- `luckyRule`: `'5♠ NEVER LOSE'` → `'5 NEVER LOSE'`

### 5. DU Copy Labels (Wild Witch Config)
**File**: `server/src/Lucky5.Api/wwwroot/js/game-config-wildwitch.js` (lines 212-220)
- Same label changes as main config, including spade symbol removal

### 6. DU Fallback Defaults
**File**: `server/src/Lucky5.Api/wwwroot/js/game.js` (lines 153-157)
- Updated fallback defaults to match config values:
  - `'DOUBLE UP'`, `'ACE ALWAYS WIN'`, `''`, `'5 NEVER LOSE'`, `'WHEN BUYING'`

### 7. Version Bumps (index.html)
| Asset | Old | New |
|-------|-----|-----|
| game.css | v24 | v25 |
| cabinet-labels-vnext.css | v2 | v3 |
| cabinet-ai9-parity.css | v10 | v11 |
| game-config.js | v13 | v14 |
| game.js | v35 | v36 |

## Verification

- `dotnet build server/Lucky5.sln` — **0 errors**, 9 warnings (all pre-existing)
- All modified files spot-checked and confirmed correct
- DU canvas renderer (`du-board-canvas.js`) already used "DOUBLE UP" — no changes needed

## Risks

- Visual verification still needed via browser screenshot (items 5-6 in original todo)
- Remaining items (HOLD buttons, credit/stake, jackpot block, card rendering, CRT effects, watermark) were verified as already matching AI9 reference in prior audit work
