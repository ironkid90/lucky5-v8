/*
 * cabinet-ai9-button-images.js
 * ═════════════════════════════════════════════════════════════════════════════
 * AI9 PARITY — Button image management
 *
 * PURPOSE:
 *   Manage button PNG asset rendering for AI9 parity.
 *   - Initialize button images on page load (idle state).
 *   - Update button images when pressed/released.
 *   - Set CSS --btn-image variable so cabinet-ai9-parity.css can render them.
 *
 * BUTTON MAPPING:
 *   Hold buttons (5):  hold_off.png (idle) → hold_on.png (pressed)
 *   BIG:              big.png (idle) → big_on.png (pressed)
 *   SMALL:            small.png (idle) → small_on.png (pressed)
 *   CANCEL HOLD:      cancel_hold.png (idle) → cancel_hold_on.png (pressed)
 *   DEAL DRAW:        deal_draw.png (idle) → deal_draw_on.png (pressed)
 *   BET:              bet.png (idle) → bet_on.png (pressed)
 *   TAKE HALF:        take_half.png (idle) → take_half_on.png (pressed)
 *   TAKE SCORE:       take_score.png (idle) → take_score_on.png (pressed)
 *   MENU:             menu.png (always same, no pressed state)
 *
 * LOAD ORDER:
 *   This file should load AFTER cabinet-orchestrator-vnext.js so the
 *   button elements exist in the DOM.
 *
 * ═════════════════════════════════════════════════════════════════════════════
 */

window.CabinetAI9ButtonImages = (function () {
    'use strict';

    // Button asset file mapping: { idle: 'name.png', pressed: 'name_on.png' }
    const BUTTON_ASSETS = {
        'cab-hold': { idle: 'hold_off.png', pressed: 'hold_on.png' },
        'cab-big': { idle: 'big.png', pressed: 'big_on.png' },
        'cab-small': { idle: 'small.png', pressed: 'small_on.png' },
        'cab-cancel': { idle: 'cancel_hold.png', pressed: 'cancel_hold_on.png' },
        'cab-deal': { idle: 'deal_draw.png', pressed: 'deal_draw_on.png' },
        'cab-bet': { idle: 'bet.png', pressed: 'bet_on.png' },
        'cab-takehalf': { idle: 'take_half.png', pressed: 'take_half_on.png' },
        'cab-takescore': { idle: 'take_score.png', pressed: 'take_score_on.png' },
        'cab-menu': { idle: 'menu.png', pressed: 'menu.png' }  // menu has no pressed state
    };

    const ASSET_BASE = '/assets/images/';

    /**
     * Set the --btn-image CSS variable on a button element.
     * @param {HTMLElement} btn - The button element
     * @param {string} imageName - The PNG filename (e.g., 'hold_off.png')
     */
    function _setButtonImage(btn, imageName) {
        if (!btn) return;
        const url = `url('${ASSET_BASE}${imageName}')`;
        btn.style.setProperty('--btn-image', url);
    }

    /**
     * Get the asset class for a button (e.g., 'cab-hold' from classList).
     * @param {HTMLElement} btn
     * @returns {string|null} The asset class name or null
     */
    function _getButtonAssetClass(btn) {
        for (const cls of btn.classList) {
            if (BUTTON_ASSETS[cls]) {
                return cls;
            }
        }
        return null;
    }

    /**
     * Update a button's image based on its pressed state.
     * @param {HTMLElement} btn - The button element
     */
    function _updateButtonImage(btn) {
        const assetClass = _getButtonAssetClass(btn);
        if (!assetClass) return;

        const assets = BUTTON_ASSETS[assetClass];
        if (!assets) return;

        // Check if button is pressed (has 'is-pressed' or 'active' class)
        const isPressed = btn.classList.contains('is-pressed') || btn.classList.contains('active');
        const imageName = isPressed ? assets.pressed : assets.idle;

        _setButtonImage(btn, imageName);
    }

    /**
     * Initialize all button images on page load.
     */
    function _initializeButtonImages() {
        // Query all buttons
        const buttons = document.querySelectorAll('.cab-btn');
        buttons.forEach(btn => {
            _updateButtonImage(btn);
        });
    }

    /**
     * Install a MutationObserver to watch for button state changes.
     * When a button's class changes (e.g., 'is-pressed' added/removed),
     * update its image.
     */
    function _installButtonObserver() {
        const buttons = document.querySelectorAll('.cab-btn');
        const observer = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                if (mutation.type === 'attributes' && mutation.attributeName === 'class') {
                    _updateButtonImage(mutation.target);
                }
            });
        });

        buttons.forEach(btn => {
            observer.observe(btn, { attributes: true, attributeFilter: ['class'] });
        });
    }

    /**
     * Fallback: patch button press/release handlers if MutationObserver
     * doesn't catch state changes (e.g., if state is set via JS without
     * modifying classList).
     */
    function _installButtonStatePatches() {
        // If the cabinet uses a custom button press handler, patch it here.
        // For now, rely on MutationObserver + CSS class changes.
    }

    /**
     * Public API
     */
    return {
        init: function () {
            // Wait for DOM to be ready
            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', () => {
                    _initializeButtonImages();
                    _installButtonObserver();
                    _installButtonStatePatches();
                });
            } else {
                _initializeButtonImages();
                _installButtonObserver();
                _installButtonStatePatches();
            }
        },

        // Allow manual update if needed
        updateButton: _updateButtonImage,
        updateAllButtons: _initializeButtonImages
    };
})();

// Auto-initialize on load
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        CabinetAI9ButtonImages.init();
    });
} else {
    CabinetAI9ButtonImages.init();
}
