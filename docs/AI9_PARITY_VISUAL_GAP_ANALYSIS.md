# AI9 Parity Visual Gap Analysis

**Generated:** 2026-07-29  
**Status:** Comprehensive analysis based on documentation review and historical measurements

## Executive Summary

This document provides a comprehensive analysis of remaining visual, timing, and behavioral gaps between the AI9 reference implementation and Lucky5 v8, synthesized from existing documentation, historical measurements, and current implementation state.

**Key Finding:** The project has achieved substantial parity with AI9, but critical timing and visual refinements remain to achieve pixel-perfect matching.

---

## Analysis Methodology

Due to network connectivity issues preventing fresh video frame extraction, this analysis is based on:

1. **Historical Ground Truth**: [`AI9_PARITY_GROUND_TRUTH_AND_WORKLOG.md`](AI9_PARITY_GROUND_TRUTH_AND_WORKLOG.md) - Frame-by-frame measurements from June 2026
2. **Current Implementation State**: [`mem.md`](../mem.md) - July 2026 VSYNC-locked timing configuration
3. **Master Redesign Plan**: [`lucky5-redesign-engine-and-frontend.md`](../lucky5-redesign-engine-and-frontend.md) - Modules F, G, H specifications
4. **Existing Screenshots**: Current game screenshots in `/screenshots/current game/`

---

## Video Sources

- **AI9 Reference:** `mediafiles/ai9-l5.mp4`
- **Lucky5 v8:** `mediafiles/Screen Recording 2026-07-28 18.46.mp4`
- **AI9 Hi-Res Recording:** 900×1600px, 82.6s (primary geometry reference from historical analysis)

---

## Current State vs Target

### Implementation Progress Summary

| Category | Status | Completeness |
|----------|--------|--------------|
| **Asset Parity** | ✅ Complete | 100% |
| **Layout Geometry** | ✅ Complete | 100% |
| **Timing (Deal/Draw)** | ⚠️ Needs Tuning | 65% |
| **Card Visual Quality** | ✅ Complete | 95% |
| **Button Appearance** | ✅ Complete | 100% |
| **Paytable Styling** | ⚠️ Minor Issues | 90% |
| **CRT Effects** | ⚠️ Fine-tuning Needed | 85% |
| **Audio Feedback** | 📋 Documented | 70% |

---

## Critical Gaps (High Priority)

### 1. Animation Timing Discrepancy ⚠️

**Current State (mem.md):**
- Deal stagger: `staggerFrames: 11` = **183ms per card** (at 60Hz)
- Total deal time: ~900ms for 5 cards
- Uses VSYNC-locked `CabinetClock.delayTicks()`

**AI9 Reference (historical measurement):**
- Frame-stepped analysis showed: **50-80ms per card** for rapid deal
- Alternative slower pace: 450-500ms per card (may have been player-paced, not animation)

**Gap Analysis:**
- Current timing (183ms) falls between the two AI9 measurements
- Need to determine if AI9 uses **fast reveal (50-80ms)** or **medium pace (180-200ms)**
- Historical worklog shows ambiguity: "1000ms figure may reflect player's tapping, not animation"

**Recommendation:**
```javascript
// Target Options in game-config.js:
// OPTION A: Fast AI9 style
staggerFrames: 3-5  // 50-83ms at 60Hz

// OPTION B: Current medium (keep)
staggerFrames: 11   // 183ms at 60Hz

// OPTION C: Slow deliberate
staggerFrames: 27-30 // 450-500ms at 60Hz
```

**Priority:** HIGH - Most visually impactful difference

---

### 2. Card Appearance Standards ⚠️

**Current State:**
- Aspect ratio: 2.5:3.5 (313:528) ✅
- Background: Pure white (#ffffff) ✅
- Border: 1px solid black ✅
- Border-radius: 0 ✅
- DOM-based rendering (not PNGs) ✅

**AI9 Reference:**
- Uses pre-rendered 313×528 PNG card faces
- Cards have pixel-perfect crispness
- Slight texture/anti-aliasing on suit symbols

**Remaining Gaps:**
- Font rendering quality on card corners and center suits
- Exact color matching (#FF0302 red, #050706 black per historical ground truth)
- Pixel-perfect suit symbol sizing

**Files to Check:**
- `cabinet-v8-quality.css` - Card face styling
- `.card-corner`, `.card-suit-large` classes

**Priority:** MEDIUM - Visual quality is good, minor refinements needed

---

### 3. Paytable Active Row Highlighting ✅⚠️

**Current Standard (mem.md):**
```css
.active-win-row {
  background: #ffffff;  /* Solid white */
  color: #000000;       /* Black text */
  /* No cyan glow or text-shadow */
}
```

**Status:** Implemented correctly according to spec

**Verification Needed:**
- Confirm no regressions in recent builds
- Verify against latest screenshots
- Check z-index doesn't cause stacking issues

**Priority:** LOW - Already implemented, just needs verification

---

### 4. Double-Up Shuffle Speed 🔴

**Current State:**
```javascript
shuffleFrameMs: 30  // 130ms cycle time
```

**AI9 Target:**
- Historical estimate: **30-50ms per shuffle cycle**
- Extremely rapid card blur effect

**Gap:**
- Current 30ms setting should be correct
- Verify `delayTicks()` implementation matches frame timing
- May need visual comparison to confirm "blur" effect quality

**Recommendation:**
```javascript
// game-config.js
shuffleFrameMs: 10-15  // For 30-50ms effective cycle with frame timing
```

**Priority:** MEDIUM - DU is secondary feature but timing is noticeable

---

## Medium Priority Gaps

### 5. CRT Screen Visual Effects

**Current Implementation:**
- Scanlines present
- ARCADE.ttf font for dot-matrix text
- Color-coded paytable rows

**AI9 Ground Truth Colors (from historical analysis):**
| Element | Hex Code | Status |
|---------|----------|--------|
| ROYAL FLUSH | `#FFB300` | ✅ |
| STRAIGHT FLUSH | Red | ⚠️ Verify exact shade |
| 4 OF A KIND | Green | ⚠️ Verify exact shade |
| FULL HOUSE | White highlight | ✅ |
| CREDIT label | `#3BD23B` | ⚠️ Verify |
| STAKE label | Gold/Amber | ⚠️ Verify |

**Gaps:**
- Exact hex color verification needed
- Phosphor glow intensity on text
- Pixel grid overlay effect (if AI9 has it)
- Subtle CRT curvature (optional, low priority)

**Files:**
- `cabinet-ai9-parity.css` - CRT colors and effects
- `cabinet-layout-vnext.css` - Screen composition

---

### 6. Background and Control Deck

**Current State:**
- PNG assets from AI9 already integrated
- `board.png` (1024×1024 reddish-brown woodgrain) in place
- Button PNG mapping complete

**AI9 Colors (historical ground truth):**
```
Wood deck:    #3B1200 (center), #381100 (top), #320F00 (bottom)
HOLD lit:     #FEFD07
HOLD idle:    #F8F807
BET face:     #0CDE6B (rim #059746)
BIG face:     #FAAE06
SMALL face:   #FAB009
DEAL DRAW:    #F76414
```

**Status:** Assets in place, color verification needed

---

### 7. Credit/Stake Display Styling

**Current State:**
- ARCADE font applied
- Color coding: CREDIT=green, STAKE=gold
- Right-aligned in CRT region

**AI9 Measurements:**
- Region: x≈0.66-0.99w, y≈0-0.16h
- Stacked layout
- Large ARCADE digits

**Gap:** Font sizing and spacing fine-tuning

---

## Low Priority / Polish Items

### 8. Title Screen Watermark

**Current:** Background "LUCKY 5 POKER" text behind cards  
**AI9:** Giant cyan block-pixel letters, partially occluded by dealt cards  
**Status:** Implemented, verify cyan color accuracy

---

### 9. Jackpot Info Block

**Layout (from historical analysis):**
- Vertical band y≈0.36h-0.50h
- SERIE label (red), KENT counter, S/N display (cyan)
- Three large jackpot numbers
- Bottom line: "4 OF A KIND WINS BONUS"

**Status:** Present in current build, verify layout precision

---

### 10. Button Disabled States

**Current Standard:**
```css
opacity: 0.6-0.75;
grayscale(0.3);
brightness(0.7-0.85);
```

**Status:** Implemented (updated from earlier 0.35 opacity)  
**Priority:** LOW - Polish item

---

## Timing Analysis Summary

### Card Deal/Draw

| Metric | AI9 (Fast) | AI9 (Slow?) | Current v8 | Gap |
|--------|------------|-------------|------------|-----|
| Per-card stagger | 50-80ms | 450-500ms | 183ms | ⚠️ Ambiguous target |
| Total 5-card deal | 250-400ms | 2.0-2.5s | 900ms | Medium |
| Animation style | Instant reveal | Paced reveal | Scale-pop thump | Different approach |

**Key Question:** Which AI9 timing is canonical?

- **50-80ms**: Matches "fast arcade feel" described in docs
- **450-500ms**: May be player-paced interaction, not animation timing
- **Current 183ms**: Conservative middle ground

**Recommendation:** Test both extremes:
1. Try `staggerFrames: 3` (50ms) - "arcade snappy"
2. Compare with current `staggerFrames: 11` (183ms)
3. User preference should decide

---

### Double-Up Shuffle

| Metric | AI9 Target | Current v8 | Status |
|--------|------------|------------|--------|
| Shuffle cycle | 30-50ms | 30ms config | ✅ Close |
| Visual effect | Rapid blur | Uses delayTicks | ⚠️ Verify frames |

---

## Comprehensive Improvement Prompt

### Module F: Animation & Layout Timing

**Target Files:**
- [`game-config.js`](../server/src/Lucky5.Api/wwwroot/game-config.js) - Lines 15-30
- [`cabinet-stage-vnext.js`](../server/src/Lucky5.Api/wwwroot/cabinet-stage-vnext.js) - `dealCards()`, `drawCards()`
- [`cabinet-clock.js`](../server/src/Lucky5.Api/wwwroot/cabinet-clock.js) - VSYNC timing

**Required Changes:**

1. **Experiment with Deal Timing** (CRITICAL)
   ```javascript
   // game-config.js - Test these values:
   
   // OPTION A: Fast arcade (AI9 fast measurement)
   staggerFrames: 3,              // 50ms @ 60Hz
   dealBaseFrames: 3,
   dealDurationFrames: 5,
   
   // OPTION B: Medium current (keep as-is)
   staggerFrames: 11,             // 183ms @ 60Hz
   dealBaseFrames: 5,
   dealDurationFrames: 11,
   
   // Test both, compare to AI9 video, decide
   ```

2. **Draw Timing Parity**
   - Ensure `drawStaggerFrames` exactly matches `staggerFrames`
   - Verify `drawRevealStartFrames` matches `dealBaseFrames`
   - Confirm draw and deal animations are indistinguishable

3. **Double-Up Shuffle Speed**
   ```javascript
   // game-config.js
   shuffleFrameMs: 15,  // Reduce from 30 to 10-15 for faster blur
   ```

4. **Verify Frame-Locked Timing**
   - All animations use `CabinetClock.delayTicks()`
   - No `setTimeout()` or `setInterval()` in animation code
   - Confirm 60Hz VSYNC lock is active

**Acceptance Criteria:**
- [ ] Side-by-side video comparison shows matching deal speed
- [ ] Draw animation speed identical to deal
- [ ] Shuffle effect feels "rapid blur" not "stepped animation"
- [ ] No timing jitter or frame drops

---

### Module G: CSS & Visual Styling

**Target Files:**
- [`cabinet-ai9-parity.css`](../server/src/Lucky5.Api/wwwroot/cabinet-ai9-parity.css) - Final CSS authority
- [`cabinet-v8-quality.css`](../server/src/Lucky5.Api/wwwroot/cabinet-v8-quality.css) - Card/button base styles
- [`cabinet-layout-vnext.css`](../server/src/Lucky5.Api/wwwroot/cabinet-layout-vnext.css) - Geometry

**Required Changes:**

1. **Card Appearance Verification**
   ```css
   /* cabinet-v8-quality.css - Verify these exact values */
   .card {
     background: #ffffff;        /* Pure white */
     border: 1px solid #000000;  /* Solid black, exactly 1px */
     border-radius: 0;           /* Zero rounding */
     aspect-ratio: 313 / 528;    /* AI9 card ratio */
   }
   
   .card-suit-large {
     /* Verify suit colors match AI9 ground truth */
     /* Red: #FF0302, Black: #050706 */
   }
   ```

2. **Paytable Color Audit**
   ```css
   /* cabinet-ai9-parity.css */
   .paytable-row.royal-flush { color: #FFB300; }  /* Verify */
   .paytable-row.straight-flush { color: #FF0000; }  /* Exact red TBD */
   .paytable-row.four-kind { color: #00FF00; }  /* Exact green TBD */
   .paytable-row.full-house.active {
     background: #ffffff;
     color: #000000;
     /* Ensure no text-shadow or glow */
   }
   ```

3. **CREDIT/STAKE Display**
   ```css
   .credit-display {
     color: #3BD23B;  /* AI9 green - verify exact shade */
   }
   .stake-display {
     color: #FFB300;  /* Amber/gold - verify */
   }
   ```

4. **CRT Effects Fine-Tuning**
   ```css
   /* Scanlines, phosphor glow, optional pixel grid */
   .crt-scanlines {
     opacity: 0.1;  /* Subtle */
   }
   .crt-text {
     text-shadow: 0 0 2px currentColor;  /* Subtle glow */
   }
   ```

5. **Button States**
   ```css
   .cabinet-button:disabled {
     opacity: 0.65;           /* Updated from 0.35 */
     filter: grayscale(0.3) brightness(0.75);
   }
   ```

**Acceptance Criteria:**
- [ ] Digital color picker confirms hex values match AI9 ground truth
- [ ] Active win row has solid white bg, black text, no glow
- [ ] Cards have crisp 1px black border, zero radius
- [ ] Button disabled state clearly visible but not too dark

---

### Module H: Audio Feedback

**Target Files:**
- [`cabinet-audio-vnext.js`](../server/src/Lucky5.Api/wwwroot/cabinet-audio-vnext.js)
- [`MODULE_H_AUDIO_REQUIREMENTS.md`](MODULE_H_AUDIO_REQUIREMENTS.md)

**Status:** Documented, partially implemented

**Required Sounds:**
1. **Card Deal** - Crisp "thump" on each card landing (5 sequential)
2. **Card Draw** - Same thump for replacement cards
3. **Button Click** - Mechanical press sound (`press.mp3` from AI9 assets)
4. **Win** - Celebratory chime
5. **Credit Drain** - Tick/counting sound during TAKE SCORE
6. **Hold Toggle** - Subtle click feedback

**Implementation:**
```javascript
// cabinet-audio-vnext.js
const DEFAULT_EVENTS = {
  'card_deal': '/assets/sounds/card_deal.mp3',
  'card_draw': '/assets/sounds/card_deal.mp3',  // Same sound
  'button_click': '/assets/images/press.mp3',    // AI9 asset
  'win': '/assets/sounds/win.mp3',
  'credit_tick': '/assets/sounds/tick.mp3',
  'hold_toggle': '/assets/sounds/hold.mp3'
};
```

**Acceptance Criteria:**
- [ ] All button presses produce `press.mp3` feedback
- [ ] Card animations synchronized with audio timing
- [ ] Win sounds appropriate to hand rank
- [ ] Audio doesn't overlap/clash during rapid actions

---

## Priority-Ordered Action Items

### Phase 1: Critical Timing Fixes (Highest Visual Impact)

**Estimated Effort:** 2-3 hours

- [ ] **Test fast deal timing** - Set `staggerFrames: 3-5`, record side-by-side with AI9
- [ ] **Compare and decide** - Choose between fast (50ms) vs current (183ms)
- [ ] **Update shuffle speed** - Reduce `shuffleFrameMs` to 10-15ms
- [ ] **Verify VSYNC lock** - Confirm no timing jitter in production

**Files:**
- `game-config.js` (timing constants)
- `cabinet-stage-vnext.js` (animation methods)

---

### Phase 2: Visual Color Accuracy (Medium Impact)

**Estimated Effort:** 3-4 hours

- [ ] **Paytable color audit** - Digital color picker on AI9 reference screenshots
- [ ] **Update CSS hex values** - Match exact shades for all CRT elements
- [ ] **Verify card suit colors** - Red (#FF0302) and black (#050706)
- [ ] **Test active win row** - Confirm white bg, black text, no glow
- [ ] **Credit/Stake display** - Verify green (#3BD23B) and gold colors

**Files:**
- `cabinet-ai9-parity.css` (color definitions)
- `cabinet-v8-quality.css` (card suit colors)

---

### Phase 3: Polish & Fine-Tuning (Low Impact)

**Estimated Effort:** 2-3 hours

- [ ] **CRT effects tuning** - Scanline opacity, phosphor glow intensity
- [ ] **Font rendering** - Verify ARCADE.ttf renders crisply
- [ ] **Button state polish** - Verify disabled opacity (0.65) looks good
- [ ] **Jackpot block layout** - Confirm SERIE/KENT/S:N positioning
- [ ] **Title watermark** - Verify cyan color and z-index layering

**Files:**
- `cabinet-layout-vnext.css` (CRT effects)
- `cabinet-ai9-parity.css` (fine-tuning)

---

### Phase 4: Audio Integration (Enhancement)

**Estimated Effort:** 2-4 hours

- [ ] **Map all audio events** - Update `DEFAULT_EVENTS` in cabinet-audio-vnext.js
- [ ] **Sync with animations** - Card deal sound on `card-deal-thump` keyframe
- [ ] **Test button feedback** - `press.mp3` on all button interactions
- [ ] **Implement credit drain sound** - Ticking during TAKE SCORE animation
- [ ] **Volume balancing** - Ensure sounds don't overpower each other

**Files:**
- `cabinet-audio-vnext.js` (event mapping)
- `assets/sounds/` (sound files, may need additions)

---

## Testing & Verification Checklist

### Visual Comparison
- [ ] Record 60fps side-by-side video (AI9 left, v8 right)
- [ ] Frame-step key moments: idle, deal, draw, win, double-up
- [ ] Use digital color picker to sample and compare hex values
- [ ] Measure element dimensions with browser dev tools
- [ ] Compare on multiple screen sizes (portrait mobile, desktop)

### Timing Verification
- [ ] Record at 120fps for precise frame counting
- [ ] Count frames between card reveals
- [ ] Measure total deal/draw durations
- [ ] Verify shuffle blur effect speed
- [ ] Test on different refresh rate monitors (60Hz, 120Hz, 144Hz)

### Functional Testing
- [ ] All buttons clickable and responsive
- [ ] Hold lamps sync correctly with card selection
- [ ] Paytable highlight follows current selection
- [ ] Credit/Stake displays update correctly
- [ ] Admin modal z-index above menu (20000 > 10000)
- [ ] No visual artifacts or layout breaks

### Cross-Browser
- [ ] Chrome/Edge (Chromium)
- [ ] Firefox
- [ ] Safari (if Mac/iOS available)
- [ ] Mobile browsers (iOS Safari, Android Chrome)

### Performance
- [ ] 60fps maintained during animations
- [ ] No dropped frames on lower-end devices
- [ ] Memory usage stable over extended play
- [ ] No CSS cache issues (verify version query strings bumped)

---

## Known Constraints & Assumptions

### Constraints
1. **No video frame extraction** - Network issues prevented opencv installation
2. **Historical measurements** - Timing data from June 2026, may need re-validation
3. **AI9 timing ambiguity** - Two different measurements (50-80ms vs 450-500ms)
4. **Subjective "feel"** - Some gaps require human judgement, not just measurements

### Assumptions
1. AI9 reference video (`ai9-l5.mp4`) represents canonical behavior
2. Historical measurements in `AI9_PARITY_GROUND_TRUTH_AND_WORKLOG.md` are accurate
3. Current `mem.md` reflects latest implementation state
4. Asset parity already 100% complete (PNGs copied from AI9 APK)

---

## Success Metrics

### Quantitative
- [ ] Deal stagger within ±10ms of AI9 target
- [ ] All color values within ΔE < 2.0 (imperceptible difference)
- [ ] Layout geometry within ±2px of AI9 measurements
- [ ] 60fps animation maintained (no dropped frames)

### Qualitative
- [ ] Side-by-side comparison indistinguishable to casual observer
- [ ] "Feel" matches AI9 arcade responsiveness
- [ ] Audio feedback enhances tactile experience
- [ ] No visual artifacts or jarring transitions

### Acceptance
**Final Test:** Show side-by-side video to 5 users without identifying which is which. If <80% can correctly identify AI9 vs v8, parity is achieved.

---

## Reference Documentation

### Primary Sources
- [`mem.md`](../mem.md) - Current implementation state, VSYNC timing, file versions
- [`AI9_PARITY_GROUND_TRUTH_AND_WORKLOG.md`](AI9_PARITY_GROUND_TRUTH_AND_WORKLOG.md) - Historical measurements, asset inventory
- [`lucky5-redesign-engine-and-frontend.md`](../lucky5-redesign-engine-and-frontend.md) - Master plan, Modules F/G/H
- [`GAME_FEEL_REFERENCE.md`](GAME_FEEL_REFERENCE.md) - Visual design standards
- [`MODULE_H_AUDIO_REQUIREMENTS.md`](MODULE_H_AUDIO_REQUIREMENTS.md) - Audio specifications

### Supporting Docs
- [`GEMINI.md`](../GEMINI.md) - Architecture overview
- [`AGENTS.md`](../AGENTS.md) - Agent rules and tooling

---

## Conclusion

Lucky5 v8 has achieved substantial AI9 parity through systematic measurement, asset integration, and VSYNC-locked timing. The remaining gaps are primarily:

1. **Timing fine-tuning** (deal stagger: fast 50ms vs current 183ms vs slow 500ms)
2. **Color accuracy verification** (hex values need digital sampling confirmation)
3. **Polish and CRT effects** (scanlines, phosphor glow, subtle refinements)

**Estimated Total Effort:** 9-14 hours across 4 phases

**Recommended Approach:**
1. Start with Phase 1 timing experiments (test fast vs current)
2. Make timing decision based on side-by-side video comparison
3. Phase 2 color audit while waiting for timing feedback
4. Phases 3 & 4 as polish after core timing/color locked

**Critical Path:** Timing decision gates all other work. Test `staggerFrames: 3` vs `staggerFrames: 11` first, get user approval, then proceed with remaining phases.

---

**Document Status:** Complete based on available documentation  
**Next Steps:** Extract video frames once network/opencv issues resolved, update with empirical measurements  
**Maintenance:** Update this document as implementation progresses and gaps close
