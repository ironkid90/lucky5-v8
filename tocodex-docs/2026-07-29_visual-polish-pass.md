# Visual Polish Pass — Lucky5 v8 Cabinet

**Date**: 2026-07-29
**Summary**: Final visual polish pass addressing 7 cabinet items: card borders, button bevel, hold instruction text, watermark, CRT effects, bonus banner, and credit/stake colors.

## Findings & Changes

### 1. Card Borders — FIXED
- **Issue**: `.card-front` had `border: 1px solid #000` (cabinet-v8-quality.css:678) and `.card-slot` had `box-shadow: inset 0 0 0 1px rgba(255,255,255,0.08)` creating visible borders
- **Fix**: Changed `.card-front` to `border: none !important` and `.card-slot` to `box-shadow: none !important`
- **Files**: `cabinet-v8-quality.css`

### 2. Button 3D Bevel — VERIFIED OK
- Buttons use PNG assets (`hold_off.png`/`hold_on.png`, `big.png`/`big_on.png`, etc.) with bevel baked into the images
- CSS layers in `cabinet-ai9-parity.css` correctly suppress duplicate CSS styling
- Press state handled by swapping to `_on.png` variants
- No changes needed

### 3. "PRESS HOLDS TO KEEP CARD" — VERIFIED OK
- Already implemented at `game.js:2083` via `showMessage('PRESS HOLDS TO KEEP CARD')`
- Displays in `#game-message` within `#info-bar` during hold phase
- No changes needed

### 4. Watermark "LUCKY 5 POKER" — ADDED
- **Issue**: No always-visible watermark existed behind cards during gameplay
- **Fix**: Added `#watermark` element in `index.html` with CSS in `cabinet-ai9-parity.css`
- **Styling**: Cyan (#00FFFF) at 18% opacity, 4.5cqh font, 3D drop-shadow extrusion (3 layers), z-index:5 (behind cards at z-index:12)
- **Files**: `index.html`, `cabinet-ai9-parity.css`

### 5. CRT Effects — FIXED (Three Layers Combined)
- **Issue**: `cabinet-frame-vnext.css` overrode `game.css` `.crt-overlay` background, losing the pixel grid
- **Fix**: 
  - `game.css`: Combined pixel grid + scanlines into one background with 3 gradient layers; kept phosphor bloom `::after` and curvature `::before`
  - `cabinet-frame-vnext.css`: Removed conflicting `.crt-overlay` background override
- **Final layers**: (1) Pixel grid 2px squares, (2) Phosphor bloom vignette, (3) Subtle 1px scanlines at 3px pitch
- **Files**: `game.css`, `cabinet-frame-vnext.css`

### 6. "4 OF A KIND WINS BONUS" Banner — VERIFIED OK
- Markup: `<span class="bonus-4k">4 OF A KIND</span> <span class="bonus-wins">WINS BONUS</span>` (index.html:392)
- Styling: `.bonus-4k` = yellow (#ffaa00), `.bonus-wins` = white (#ffffff) (cabinet-ai9-parity.css:619-626)
- No changes needed

### 7. Credit/Stake Display Colors — FIXED
- **Issue**: Labels and values had wrong colors
- **Fix** in `cabinet-ai9-parity.css`:
  - `#credits` (label): `#00ffff` (cyan), `#credits span` (value): `#33ff33` (green)
  - `#stake-display` (label): `#00ffff` (cyan), `#stake-display span` (value): `#FFB800` (yellow/orange)

## Version Bumps
| File | Old | New |
|------|-----|-----|
| game.css | v=25 | v=26 |
| cabinet-v8-quality.css | v=12 | v=13 |
| cabinet-ai9-parity.css | v=11 | v=12 |
| cabinet-frame-vnext.css | v=2 | v=3 |

## Build Result
- `dotnet build server/Lucky5.sln` — **0 errors**, 9 pre-existing warnings (all unrelated)

## Acceptance Criteria
- [x] Cards: pure white, NO border (pixel-edge against black)
- [x] Buttons: 3D bevel via PNG assets (verified working)
- [x] "PRESS HOLDS TO KEEP CARD" text visible during hold phase (verified existing)
- [x] "LUCKY 5 POKER" cyan watermark behind cards (newly added)
- [x] CRT: pixel grid + phosphor glow + scanlines (three layers combined)
- [x] "4 OF A KIND WINS BONUS" banner visible (verified existing)
- [x] Credit: Cyan label + Green value; Stake: Cyan label + Yellow value
- [x] Build passes with 0 errors
- [x] All version strings bumped
