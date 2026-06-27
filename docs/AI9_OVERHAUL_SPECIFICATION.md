# AI9 Cabinet Overhaul Specification

This document details the comprehensive overhaul required to bring the Lucky5 v8 web-native cabinet into exact visual, timing, and gameplay parity with the AI9 reference target. The goal is to eliminate any modernized web-app aesthetics and enforce the crude, high-contrast, mechanical feel of the original Lebanese arcade cabinet.

## 1. Visual Discrepancies and Structural Changes

Based on an exhaustive analysis of the AI9 reference recording against current v8 screenshots and CSS assets, the current implementation suffers from several key deviations that must be corrected.

### 1.1 Global Layout and Proportions
The AI9 cabinet is rigidly split into two distinct zones: the upper CRT display field (occupying roughly 65% of the vertical space) and a lower physical control deck (occupying the remaining 35%). The current v8 layout features too much empty black space and fails to anchor the bottom of the screen with a dominant, warm-toned woodgrain deck. The control deck must be expanded vertically, and the buttons must be significantly enlarged to match the reference proportions.

### 1.2 Control Deck and Button Styling
The current buttons resemble isolated web widgets rather than physical arcade hardware. The overhaul requires rebuilding the buttons to feature:
- **Geometry:** Tall, rounded rectangles (approx. 1.3:1 height-to-width ratio) set within recessed, darker socket frames.
- **Texture:** A warm, reddish-brown woodgrain background for the entire deck.
- **Beveling:** Heavy 3D arcade bevels with a top/left highlight and bottom/right shadow.
- **Color Palette:**
  - `HOLD` (Top row, x5): Bright yellow/amber gradient.
  - `BIG` / `SMALL`: Orange gradient.
  - `CANCEL HOLD`: Cream/off-white.
  - `DEAL DRAW` / `TAKE HALF`: Bright red.
  - `BET`: Bright green.
  - `TAKE SCORE`: Orange/amber.
  - `MENU`: A smaller, dark circular button with three white horizontal lines.
- **Typography:** Narrow, black, sans-serif pixel lettering centered on each button.

### 1.3 Card Rendering
The current v8 DOM-based cards are too polished, featuring visible gold inner borders and modernized vector pips. The AI9 reference cards are crude and minimalist. The overhaul must strip away ornamental borders, leaving flat white rectangles with a single, massive pixelated center pip and simple corner ranks. Red suits must be pure `#FF0000` and black suits pure `#000000`.

### 1.4 Paytable and HUD Typography
The AI9 paytable is a dense, dot-matrix list anchored in the top-left corner. The overhaul must enforce exact color mapping:
- ROYAL FLUSH: Gold/Yellow
- STRAIGHT FLUSH: Red
- 4 OF A KIND: Green
- FULL HOUSE: White text on a solid white background block (when selected)
- FLUSH: Gold/Yellow
- STRAIGHT: Cyan
- 3 OF A KIND: Gold/Yellow
- 2 PAIR: Cyan

The Full House highlight must transition from an outlined row to a hard, filled white selection block. Additionally, the `CREDIT` (green) and `STAKE` (gold) blocks in the top-right must be tightly stacked and right-aligned using a strict pixel font.

### 1.5 Background Watermark and CRT Effects
The current implementation utilizes a soft, blurred glow for the background watermark. The AI9 reference uses chunky, cyan square-pixel block letters (`LUCKY 5` over `POKER`) with a distinct 3D drop-shadow extrusion. The overhaul must replace the soft CSS text-shadow blur with a grid-based, extruded pixel font presentation. Furthermore, the overall cabinet should retain a dark, slightly illuminated gray/black background (`~#0a0a0a`) to simulate CRT phosphor idle glow, rather than pure hex black.

## 2. Timing and Gameplay Adjustments

The pacing of the AI9 cabinet is mechanical, snappy, and instantaneous. The current v8 implementation likely employs theatrical CSS transitions or delayed slide-in animations that violate this feel.

### 2.1 Deal and Draw Stagger
Cards do not slide from off-screen. They must appear in place via an instant reveal. The stagger between each card appearing (left-to-right) is extremely fast, measured at approximately 50-80 milliseconds in the reference video. The entire 5-card deal should conclude within 300-400 milliseconds.

### 2.2 Double-Up Reel Animation
During the gamble phase, the player's card undergoes a rapid slot-reel shuffle. The card face must cycle through random ranks and suits at roughly 1 frame per update (16-33ms), creating a blur effect that stops instantaneously upon player input.

### 2.3 Credit Drain
When `TAKE SCORE` is pressed, the transfer of won credits to the main meter is not instantaneous. It must execute as a rapid numeric scroll, moving thousands of credits over a 1 to 1.5-second window, accompanied by the appropriate audio hook.

## 3. Implementation Plan

The implementation phase will target the following files in the `server/src/Lucky5.Api/wwwroot/` directory:
1. **CSS Layout (`cabinet-layout-vnext.css` / `cabinet-v8-quality.css`):** Redefine the global grid to enforce the 65/35 CRT-to-deck ratio. Rebuild the button classes (`.apk-btn`, `.apk-hold-btn`) with the specified 3D bevels, inset sockets, and exact color gradients.
2. **Card DOM Rendering (`game.js` / `cabinet-v8-effects.js`):** Strip out gold borders and complex CSS gradients from the card classes. Implement the crude, flat-white pixel aesthetic. Ensure legacy image-based fallbacks are fully removed.
3. **Paytable and HUD:** Update the paytable row colors and implement the hard white background block for the Full House selection state. Replace the soft watermark glow with the chunky, extruded `LUCKY 5 POKER` pixel art.
4. **Animation Timing (`cabinet-stage-vnext.js` / `cabinet-transition-vnext.js`):** Remove any CSS `transform: translate` slide-in effects for cards. Enforce the 50-80ms left-to-right reveal stagger for deals and draws.

By executing these precise modifications, the v8 web-native client will achieve the authoritative, retro-arcade parity demanded by the AI9 reference target.
