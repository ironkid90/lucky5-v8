# AI9 Parity Implementation Summary

**Date:** 2026-06-27  
**Status:** Feature-complete (primary targets implemented); ready for testing and refinement  
**Scope:** Make Lucky5 v8 match AI9 identically in cabinet look, graphics quality, animation timing, and gameplay.

---

## Executive Summary

The AI9 parity overhaul has been successfully implemented across three dimensions:

1. **Layout Geometry** — Cabinet proportions, zone positions, card/button grids corrected to match AI9 measurements.
2. **Visual Rendering** — CRT screen colors, paytable styling, credit/stake/jackpot displays, idle title overlay.
3. **Animation Timing** — Deal stagger updated from 180ms to 475ms (measured 450–500ms from frame analysis).

**Key Discovery:** The genuine AI9 PNG button and card assets were already present in the codebase. The overhaul required only CSS/JS changes, not asset replacement.

---

## Files Created / Modified

### New Files (Additive, Reversible)

| File | Purpose | Lines | Status |
|---|---|---|---|
| `css/cabinet-ai9-parity.css` | Layout geometry, CRT colors, button rendering | ~1,100 | ✅ Complete |
| `js/cabinet-ai9-button-images.js` | Button PNG asset management | ~180 | ✅ Complete |
| `docs/AI9_PARITY_GROUND_TRUTH_AND_WORKLOG.md` | Authoritative measurements & decisions | ~190 | ✅ Complete |

### Modified Files

| File | Changes | Status |
|---|---|---|
| `index.html` | Added CSS link (line 16) + JS link (line 363) | ✅ Complete |
| `js/game-config.js` | Updated `dealStaggerMs` 180→475, timing comment | ✅ Complete |

---

## Implementation Details

### 1. Layout Geometry (`cabinet-ai9-parity.css`)

**CRT Screen / Deck Split:**
- CRT screen: top 0–54.6% (measured from AI9 frame analysis: y=0..874px of 1600px)
- Wood control deck: bottom 45.4% (y=874..1600px)

**Card Grid (5 cards, edge-to-edge):**
- Centers: 0.099w, 0.299w, 0.499w, 0.699w, 0.899w (0.20w pitch)
- Width per card: 0.169w (≈121px in 720px cabinet)
- Aspect ratio: 313:528 (0.593, taller/narrower than standard poker)
- Vertical position: top 15.1%, height 20%

**Button Grids (3 rows in deck):**
- Row 1 (HOLD×5): top 2.4% of deck, 5-column grid, 0.169w pitch
- Row 2 (BIG/SMALL/CANCEL/DEAL/BET): top 8.7% of deck, 5-column grid
- Row 3 (TAKE HALF / MENU / TAKE SCORE): top 15.6% of deck, 3-column grid (1fr auto 1fr)

**Button Aspect Ratios:**
- Card buttons (HOLD, BIG, SMALL, CANCEL, DEAL, BET, TAKE HALF, TAKE SCORE): 1024:1536
- MENU button: 1:1 (circle)

**CRT Screen Zones:**
- Paytable: top-left, 55% width, 15% height
- Credit/Stake: top-right, 45% width, 15% height
- Card area: 15.1%–35.1% height
- Win amount: 35.5%–38.5% height
- Machine info (SERIE/KENT/S/N + jackpots): 35.8%–48.8% height
- Info bar: 48.8%–50.8% height
- Bonus bar: 50.8%–51.8% height

### 2. CRT Screen Colors (`cabinet-ai9-parity.css`)

**Paytable Rows (ARCADE dot-matrix font):**
| Hand | Color | Hex |
|---|---|---|
| ROYAL FLUSH | Gold | #ffaa00 |
| STRAIGHT FLUSH | Red | #ff3333 |
| 4 OF A KIND | Green | #33ff33 |
| FULL HOUSE | White bg, black text | Selection highlight |
| FLUSH | Gold | #ffaa00 |
| STRAIGHT | Cyan | #00dddd |
| 3 OF A KIND | Gold | #ffaa00 |
| 2 PAIR | Cyan | #00dddd |

**Credit/Stake Display:**
- CREDIT label + value: Green (#33ff33)
- STAKE label + value: Gold (#ffaa00)

**Machine Info Block:**
- SERIE / KENT: Red (#ff3333)
- S/N: Cyan (#00dddd)
- Jackpot counters: Left & right green (#33ff33), center (SF) red (#ff3333)

**Idle Overlay (LUCKY 5 POKER):**
- Color: Cyan (#00dddd)
- Text shadow: Glow effect (0 0 1cqh rgba(0, 221, 221, 0.8))
- Font: ARCADE (or Courier New fallback)
- Position: Behind cards, centered, static (no animation)

### 3. Button Rendering (`cabinet-ai9-parity.css` + `cabinet-ai9-button-images.js`)

**CSS Changes:**
- Re-enabled `background-image` (override v8-quality layer that disabled PNG rendering)
- Set `--btn-image` CSS variable to point to PNG assets
- Removed CSS gradients, box-shadows, text rendering (PNG is the visual)

**JavaScript (`cabinet-ai9-button-images.js`):**
- Initializes all button images on page load
- Maps button classes to asset pairs:
  - `cab-hold`: hold_off.png (idle) / hold_on.png (pressed)
  - `cab-big`: big.png / big_on.png
  - `cab-small`: small.png / small_on.png
  - `cab-cancel`: cancel_hold.png / cancel_hold_on.png
  - `cab-deal`: deal_draw.png / deal_draw_on.png
  - `cab-bet`: bet.png / bet_on.png
  - `cab-takehalf`: take_half.png / take_half_on.png
  - `cab-takescore`: take_score.png / take_score_on.png
  - `cab-menu`: menu.png (no pressed state)
- Installs MutationObserver to watch for button state changes (is-pressed class)
- Updates button image dynamically when pressed/released

### 4. Animation Timing (`js/game-config.js`)

**Deal Stagger:**
- **Old value:** 180ms
- **New value:** 475ms (midpoint of measured 450–500ms range)
- **Measurement method:** Frame-stepped AI9 recording #2 (900×1600, 30fps extraction), detected card reveals via white-pixel count in card area.
- **Measured inter-card gaps:** 100ms (cards 1-2, overlap), 500ms (cards 2-3), 433ms (cards 3-4), 966ms (cards 4-5, player pause)

**Draw Stagger & Drain Animation:**
- **Status:** TBD (requires frame-stepping draw and TAKE SCORE sequences)
- **Current v8 values:** drawStaggerMs=100, countUpMinMs=1000, countUpMaxMs=65000
- **Next steps:** Frame-step AI9 draw phase and TAKE SCORE collection to measure exact timing

---

## Testing & Verification Checklist

- [ ] **Visual Parity:** Compare live v8 cabinet against AI9 reference frames (ai9b/b6.png, b25.png)
  - [ ] CRT background is pure black
  - [ ] Paytable rows display correct colors (gold/red/green/cyan)
  - [ ] CREDIT/STAKE display is correct color and position
  - [ ] Idle overlay "LUCKY 5 POKER" is cyan, behind cards, centered
  - [ ] Card grid is 5 cards edge-to-edge, evenly spaced
  - [ ] Button grid is correctly inset relative to card grid
  - [ ] Wood deck background is visible (board.png)

- [ ] **Button Rendering:** Verify PNG assets display correctly
  - [ ] HOLD buttons show hold_off.png idle, hold_on.png when pressed
  - [ ] All action buttons (BIG, SMALL, etc.) display correct PNG
  - [ ] MENU button is a small circle
  - [ ] Button aspect ratios are correct (not stretched/squashed)

- [ ] **Animation Timing:** Verify deal stagger matches AI9
  - [ ] Cards appear left-to-right with ~475ms stagger
  - [ ] Draw stagger (if different from deal) matches AI9
  - [ ] Drain animation (TAKE SCORE) speed matches AI9

- [ ] **Font Rendering:** Verify ARCADE.ttf loads
  - [ ] Paytable and credit/stake text use dot-matrix font
  - [ ] Fallback to Courier New if ARCADE.ttf is missing

- [ ] **Responsive Scaling:** Verify cabinet scales correctly on different viewports
  - [ ] 9:16 portrait aspect ratio is maintained
  - [ ] All zones scale proportionally
  - [ ] Text remains readable at all sizes

---

## Known Limitations & Open Items

### Timing (TBD)

1. **Draw Stagger:** Current v8 value is 100ms. AI9 draw stagger is unknown; requires frame-stepping the draw sequence.
2. **Drain Animation:** Current v8 value is 1000–65000ms (scaled by amount). AI9 drain timing is unknown; requires frame-stepping TAKE SCORE collection.
3. **Double-Up Timing:** Not yet analyzed; AI9 DU sequences not captured in recordings.

### Fonts

- **ARCADE.ttf:** Expected at `/assets/fonts/ARCADE.ttf`. If missing, falls back to Courier New (acceptable but not pixel-perfect).
- **Verify:** Check if ARCADE.ttf exists in wwwroot/assets/fonts/. If not, either copy it or update the @font-face URL.

### Potential Issues

1. **Button State Tracking:** The MutationObserver watches for `is-pressed` class changes. If button state is set via a different mechanism (e.g., direct style manipulation), the observer may not catch it. **Mitigation:** Verify button state is set via classList in cabinet-orchestrator or game.js.

2. **CSS Specificity:** The parity CSS uses `!important` to override prior layers. If future changes add higher-specificity rules, they may not apply. **Mitigation:** Keep parity CSS as the final layer in index.html.

3. **Card Aspect Ratio:** AI9 cards are 313:528 (0.593), narrower than standard poker (0.714). If card images are stretched, they'll look distorted. **Mitigation:** Verify card PNGs are rendered at correct aspect ratio (CSS `aspect-ratio: 313 / 528`).

---

## Handoff Instructions for Next Agent

### If Continuing the Overhaul:

1. **Frame-Step Draw Sequence:** Extract frames from AI9 recording #2 around t=12–18s (draw phase). Detect when replaced cards flip out and new cards flip in. Measure inter-card gaps to lock `drawStaggerMs`.

2. **Frame-Step Drain Animation:** Extract frames from AI9 recording #1 around t=32s (TAKE SCORE collection). Measure how long the drain takes and how fast the credit counter ticks. Update `countUpMinMs`, `countUpMaxMs`, `creditTickMs`.

3. **Test Live Cabinet:** Deploy the changes to a running v8 cabinet and compare against AI9 reference frames. Screenshot key states (idle, dealt, draw, drain) and diff against ai9b/b6.png, b25.png.

4. **Verify Font:** Check if ARCADE.ttf exists in wwwroot/assets/fonts/. If missing, either add it or update the @font-face URL in cabinet-ai9-parity.css.

5. **Verify Button Images:** Manually test each button (HOLD, BIG, SMALL, etc.) to ensure PNG assets render correctly and state changes update the image.

### If Reverting:

- Comment out the CSS link in index.html (line 16)
- Comment out the JS link in index.html (line 363)
- Revert game-config.js dealStaggerMs to 180
- The cabinet will revert to v8 prior appearance (CSS gradients, 180ms deal stagger)

---

## References

- **Ground Truth Document:** `docs/AI9_PARITY_GROUND_TRUTH_AND_WORKLOG.md` (comprehensive measurements, decisions, and worklog)
- **AI9 Reference Frames:** `/home/ubuntu/ai9b/b6.png` (idle), `/home/ubuntu/ai9b/b25.png` (dealt hand)
- **AI9 Frame Extraction:** `/home/ubuntu/ai9_timing/` (2476 frames at 30fps from recording #2)
- **AI9 Assets:** `ai9/3 - Extracted/ai9poker.apk/assets/flutter_assets/` (authoritative button/card/font PNGs)

---

## Summary

The AI9 parity overhaul is **feature-complete** for the primary targets (geometry, colors, button rendering, deal timing). The implementation is **additive and reversible** — all changes live in new CSS/JS files or minimal edits to index.html and game-config.js. The next agent can verify the live cabinet output and complete the remaining timing measurements (draw stagger, drain animation) as needed.

**Estimated effort to complete:** 2–4 hours (frame-stepping, testing, refinement).
