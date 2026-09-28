/*
 * cabinet-hq-toggle.js
 * ═════════════════════════════════════════════════════════════════════════════
 * HQ ("crisper retro") asset layer controller.
 *
 * PURPOSE:
 *   Single source of truth for whether the cabinet renders the regenerated
 *   high-resolution asset set (/assets_hq/) or the original AI9 bitmaps
 *   (/assets/images/). Sets `body.hq-on` which css/cabinet-v8-hq.css keys off,
 *   and exposes window.Lucky5HQ for the asset-path resolvers in game.js,
 *   cabinet-stage-vnext.js and cabinet-ai9-button-images.js.
 *
 * RESOLUTION ORDER (highest wins):
 *   1. URL query           ?hq=1 / ?hq=0
 *   2. localStorage        lucky5-hq = "1" | "0"
 *   3. default             ON
 *
 * LOAD ORDER: load BEFORE cabinet-stage-vnext.js and game.js so card and
 *   button paths resolve correctly on first paint.
 * ═════════════════════════════════════════════════════════════════════════════
 */
(function () {
    'use strict';

    var STORAGE_KEY = 'lucky5-hq';
    var HQ_ROOT = '/assets_hq/';
    var STD_ROOT = '/assets/images/';

    function _resolveOn() {
        try {
            var q = new URLSearchParams(window.location.search).get('hq');
            if (q === '1' || q === 'true') return true;
            if (q === '0' || q === 'false') return false;
        } catch (e) { /* ignore */ }
        try {
            var s = window.localStorage.getItem(STORAGE_KEY);
            if (s === '1') return true;
            if (s === '0') return false;
        } catch (e) { /* ignore */ }
        return true; // default: HQ on
    }

    function _apply(on) {
        if (document.body) {
            document.body.classList.toggle('hq-on', on);
        }
        // Re-point any already-rendered card images (cheap; runs on toggle).
        var imgs = document.querySelectorAll('.card-face img, .card-back-pattern, .card-front');
        imgs.forEach(function (img) {
            var src = img.getAttribute('src') || '';
            var m = src.match(/\/(?:assets_hq|assets\/images)\/(cards\/.+\.png)$/);
            if (m) {
                img.setAttribute('src', (on ? HQ_ROOT : STD_ROOT) + m[1]);
            }
        });
        // Re-point button --btn-image variables.
        if (window.CabinetAI9ButtonImages &&
            typeof window.CabinetAI9ButtonImages.refreshAssets === 'function') {
            window.CabinetAI9ButtonImages.refreshAssets();
        }
    }

    var _on = _resolveOn();

    window.Lucky5HQ = {
        isOn: function () { return _on; },
        /** Map a relative asset path ("cards/AS.png") to the active root. */
        cardAssetUrl: function (rel) { return (_on ? HQ_ROOT : STD_ROOT) + rel; },
        assetUrl: function (rel) { return (_on ? HQ_ROOT : STD_ROOT) + rel; },
        set: function (on, persist) {
            _on = !!on;
            if (persist !== false) {
                try { window.localStorage.setItem(STORAGE_KEY, _on ? '1' : '0'); } catch (e) { }
            }
            _apply(_on);
        },
        toggle: function () { window.Lucky5HQ.set(!_on); }
    };

    // Apply as early as possible so first paint uses the right root.
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { _apply(_on); });
    } else {
        _apply(_on);
    }
})();
