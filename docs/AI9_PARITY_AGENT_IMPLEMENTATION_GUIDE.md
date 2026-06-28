# AI9 Parity Implementation Guide for Agents

## Context and Objective
Your objective is to upgrade the current Lucky5 v8 game cabinet to match the exact visual layout and behavior of the `ai9poker` reference cabinet. The current game suffers from overlapping button layouts, cut-off UI elements (like the "2 PAIR" row on the paytable), missing PNG button textures, and non-functioning buttons (specifically the MENU key).

This step-by-step guide is designed to provide you (an AI agent) with exact instructions on how to implement the required fixes and achieve full 1:1 visual parity.

---

## Step 1: Fix Cabinet Geometry and Overlaps
The current cabinet has an overlap between the card deck and the bottom control buttons. The paytable is too squished, cutting off the bottom rows.

**1. Modify Paytable and CRT Heights**
In the primary cabinet CSS (create or modify `cabinet-ai9-parity.css` and include it in `index.html`):
- Change `#paytable` and `#credit-stake-bar` to `height: 18% !important;` (was previously 15%).
- Adjust `#card-area` to `top: 18.2% !important;` and `height: 18% !important;`.
- Move `#win-amount-display` down to `top: 36.5% !important;`.
- Shift the `#machine-info-block` to `top: 39.8% !important;` and `height: 11.5% !important;`.

**2. Lock the Viewport Aspect Ratio**
To prevent button overlaps on resizing windows, strictly enforce a 9:16 aspect ratio.
- In CSS, apply `max-width: 56.25vh !important;` and `max-height: 177.78vw !important;` to `#cabinet-viewport`.

---

## Step 2: Fix Missing Button Textures (The Blank Squares Issue)
Currently, the buttons at the bottom of the screen appear as blank wooden squares. This is because modern CSS gradients are overriding the authentic PNG assets, or the PNGs aren't being mapped.

**1. Remove Legacy CSS Gradients**
Target all buttons `.cab-btn` and disable CSS decorations:
```css
.cab-btn {
    background-repeat: no-repeat !important;
    background-size: contain !important;
    background-position: center !important;
    background-color: transparent !important;
    border: none !important;
    box-shadow: none !important;
}
.cab-btn::before, .cab-btn::after {
    display: none !important;
    content: none !important;
}
```

**2. Map PNG Assets Directly via CSS**
Manually assign the correct `background-image` for every single button, including their active/pressed states.
- **Hold Buttons:** `hold_off.png` / `hold_on.png`
- **Big/Small:** `big.png` / `small.png`
- **Cancel Hold:** `cancel_hold.png` / `cancel_hold_on.png`
- **Deal Draw:** `deal_draw.png` / `deal_draw_on.png` (MUST be red).
- **Bet:** `bet.png` / `bet_on.png` (MUST be green).
- **Take Half:** `take_half.png` / `take_half_on.png`
- **Take Score:** `take_score.png` / `take_score_on.png`

Example for the Deal button:
```css
.cab-btn.cab-deal {
    background-image: url('/assets/images/deal_draw.png') !important;
    aspect-ratio: 1024 / 1536 !important;
}
.cab-btn.cab-deal:active, .cab-btn.cab-deal.active, .cab-btn.cab-deal.is-pressed {
    background-image: url('/assets/images/deal_draw_on.png') !important;
}
```

---

## Step 3: Fix Non-Functioning Menu Key and Buttons
The MENU button is missing from the layout, and some buttons fail to register clicks due to z-index overlaps or missing DOM elements.

**1. Insert the Menu Button in HTML**
Ensure `index.html` has the correct structure in the bottom control row:
```html
<div id="bottom-row" data-zone="bottom-row" class="bottom-row">
    <button id="btn-take-half" class="cab-btn cab-takehalf"></button>
    <button id="btn-menu" class="cab-btn cab-menu"></button>
    <button id="btn-take-score" class="cab-btn cab-takescore"></button>
</div>
```

**2. Map the Menu Button CSS**
The Menu button is a square.
```css
.cab-btn.cab-menu {
    background-image: url('/assets/images/menu.png') !important;
    width: 8% !important;
    aspect-ratio: 1 / 1 !important;
}
```

**3. Fix Pointer Events and Z-Index**
If the buttons still don't click, ensure that their parent container `#controls` does not have `pointer-events: none;` and that no transparent overlay is sitting on top of the button deck. Set `z-index: 10;` on `.cab-btn` if necessary.

**4. Bind the Menu Event Listener**
In `cabinet-shell-vnext.js` or `game.js`, verify the click handler binds to `#btn-menu`:
```javascript
const menuBtn = document.getElementById('btn-menu');
if (menuBtn) {
    menuBtn.addEventListener('click', () => {
        document.getElementById('menu-panel').classList.add('visible');
    });
}
```

---

## Step 4: Adjust Deal/Draw Timing Parity
The current card dealing animation is too slow compared to the arcade original.
- Locate `game-config.js` (or equivalent timing constants file).
- Adjust the `dealStaggerMs` to `350` (down from ~500 or 1000).
- Adjust `drawStaggerMs` to `100`.

---

## Final Verification
After performing the above steps, run the application (`./dev.ps1`) and verify:
1. All 8 paytable rows (up to 2 PAIR) are clearly visible and not clipped.
2. The control deck buttons correctly display `ai9poker` PNG textures instead of flat CSS squares.
3. The DEAL DRAW button is red and the BET button is green.
4. Clicking the square MENU button successfully triggers the menu overlay.
