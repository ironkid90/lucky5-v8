/* ============================================================
   LUCKY 5 v8 — Cabinet realism effects runtime (MECHANICAL ONLY)
   ------------------------------------------------------------
   Self-contained, idempotent, DOM-safe.
   Provides:
     - Cabinet marquee injection
     - Coin hopper injection
     - Button press haptic (.is-pressed class)
     - Card-area win/lose classes
     - Idle attract-mode toggle after configurable delay
     - Body.v8-ready flag after asset loader completes
   No Hollywood: no coin showers, no camera shake, no strobe.
   ============================================================ */
(function () {
    'use strict';

    if (window.__lucky5V8EffectsLoaded) return;
    window.__lucky5V8EffectsLoaded = true;

    const IDLE_ATTRACT_MS = (function () {
        try {
            const t = window.GAME_CONFIG && window.GAME_CONFIG.timing;
            const v = t && Number(t.idleAttractModeMs);
            return (v && v > 0) ? v : 12_000;
        } catch (e) { return 12_000; }
    })();
    const MARQUEE_TEXT =
        'LUCKY 5  \u2022  LEBANESE LEGEND  \u2022  80% RTP  \u2022  FIVE OF SPADES NEVER LOSES  \u2022  ';

    // ---------- DOM bootstrap ------------------------------------

    function onReady(fn) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', fn, { once: true });
        } else {
            fn();
        }
    }

    function ensureMarquee() {
        const container = document.getElementById('game-container');
        if (!container || document.getElementById('cabinet-marquee')) return;
        const marquee = document.createElement('div');
        marquee.id = 'cabinet-marquee';
        marquee.innerHTML =
            '<div class="marquee-track">' +
            '<span>' + MARQUEE_TEXT + '</span>' +
            '<span>' + MARQUEE_TEXT + '</span>' +
            '</div>';
        container.appendChild(marquee);
    }

    function ensureHopper() {
        const container = document.getElementById('game-container');
        if (!container || document.getElementById('cabinet-hopper')) return;
        const hopper = document.createElement('div');
        hopper.id = 'cabinet-hopper';
        container.appendChild(hopper);
    }

    // ---------- Button haptic feedback ---------------------------

    function wireButtonHaptic() {
        document.body.addEventListener('pointerdown', function (e) {
            const btn = e.target.closest('.cab-btn, .menu-panel-btn, .lobby-btn, .auth-btn');
            if (!btn) return;
            btn.classList.add('is-pressed');
        });

        const release = function (e) {
            const btn = e.target && e.target.closest
                ? e.target.closest('.cab-btn, .menu-panel-btn, .lobby-btn, .auth-btn')
                : null;
            if (btn) btn.classList.remove('is-pressed');
        };

        document.body.addEventListener('pointerup', release);
        document.body.addEventListener('pointercancel', release);
        document.body.addEventListener('pointerleave', release, true);
    }

    // ---------- Win tier detection (subtle, no Hollywood) --------

    function classifyWin(amount) {
        if (!amount || amount <= 0) return null;
        if (amount >= 5_000_000) return 'v8-win-jackpot';
        if (amount >= 500_000) return 'v8-win-big';
        if (amount >= 20_000) return 'v8-win-medium';
        return 'v8-win-small';
    }

    function parseWinAmount(text) {
        if (!text) return 0;
        const cleaned = String(text).replace(/[^\d-]/g, '');
        const n = parseInt(cleaned, 10);
        return isFinite(n) ? n : 0;
    }

    function clearWinClasses() {
        document.body.classList.remove(
            'v8-win-small', 'v8-win-medium', 'v8-win-big', 'v8-win-jackpot'
        );
    }

    function applyWinTier(tier) {
        if (!tier) return;
        clearWinClasses();
        document.body.classList.add(tier);
        const clearMs = tier === 'v8-win-jackpot' ? 3000
            : tier === 'v8-win-big' ? 1800
                : tier === 'v8-win-medium' ? 1200
                    : 700;
        setTimeout(clearWinClasses, clearMs);
    }

    function observeWinDisplay() {
        const winValue = document.getElementById('win-amount-value');
        if (!winValue) return;

        let lastText = '';
        const observer = new MutationObserver(function () {
            const now = winValue.textContent || '';
            if (now === lastText) return;
            lastText = now;
            const amount = parseWinAmount(now);
            if (amount <= 0) return;
            applyWinTier(classifyWin(amount));
        });
        observer.observe(winValue, {
            childList: true,
            characterData: true,
            subtree: true
        });
    }

    // ---------- Idle attract mode --------------------------------

    let idleTimer = 0;
    let attractActive = false;
    let attractPaytableTimer = 0;
    let attractPaytableIndex = 0;

    function resetIdleTimer() {
        if (attractActive) exitAttract();
        clearTimeout(idleTimer);
        idleTimer = setTimeout(enterAttract, IDLE_ATTRACT_MS);
    }

    function enterAttract() {
        const gameScreen = document.getElementById('game-screen');
        if (!gameScreen || gameScreen.style.display === 'none') return;
        const idleOverlay = document.getElementById('idle-overlay');
        if (!idleOverlay) return;
        const overlayVisible = getComputedStyle(idleOverlay).display !== 'none';
        if (!overlayVisible) return;

        attractActive = true;
        document.body.classList.add('v8-attract');
        cyclePaytableHighlight();
    }

    function exitAttract() {
        attractActive = false;
        document.body.classList.remove('v8-attract');
        clearTimeout(attractPaytableTimer);
        document.querySelectorAll('.pay-row[data-attract-active="1"]').forEach(function (el) {
            el.removeAttribute('data-attract-active');
        });
    }

    function cyclePaytableHighlight() {
        if (!attractActive) return;
        const rows = Array.from(document.querySelectorAll('.pay-row'));
        if (rows.length === 0) return;
        rows.forEach(function (el) { el.removeAttribute('data-attract-active'); });
        const row = rows[attractPaytableIndex % rows.length];
        if (row) row.setAttribute('data-attract-active', '1');
        attractPaytableIndex++;
        attractPaytableTimer = setTimeout(cyclePaytableHighlight, 1600);
    }

    function wireIdleAttract() {
        ['pointerdown', 'keydown', 'touchstart', 'wheel'].forEach(function (ev) {
            document.addEventListener(ev, resetIdleTimer, { passive: true });
        });
        resetIdleTimer();
    }

    // ---------- Ready flag ---------------------------------------

    function markReady() {
        document.body.classList.add('v8-ready');
    }

    function wireReadyFlag() {
        const loader = document.getElementById('asset-loader');
        if (!loader) {
            markReady();
            return;
        }
        if (loader.classList.contains('done')) {
            markReady();
            return;
        }
        const obs = new MutationObserver(function () {
            if (loader.classList.contains('done')) {
                markReady();
                obs.disconnect();
            }
        });
        obs.observe(loader, { attributes: true, attributeFilter: ['class'] });
        setTimeout(markReady, 6000);
    }

    // ---------- Card-face load-error guard -----------------------

    const FACE_ERROR_CLASS = 'v8-face-error';

    function cardBackSrc() {
        const cfg = window.GAME_CONFIG && window.GAME_CONFIG.assets;
        return (cfg && cfg.cardBack) || '/assets/images/cards/bside.png';
    }

    function flagCardSlotError(img) {
        const slot = img.closest ? img.closest('.card-slot, .du-card-slot') : null;
        if (!slot) return;
        slot.classList.add(FACE_ERROR_CLASS);
    }

    function clearCardSlotError(img) {
        const slot = img.closest ? img.closest('.card-slot, .du-card-slot') : null;
        if (!slot) return;
        if (slot.classList.contains(FACE_ERROR_CLASS)) {
            slot.classList.remove(FACE_ERROR_CLASS);
        }
    }

    function isCardBackSrc(src) {
        if (!src) return true;
        const back = cardBackSrc();
        return src.indexOf(back) >= 0 || src.toLowerCase().indexOf('bside') >= 0;
    }

    function wireCardFaceGuard() {
        const area = document.getElementById('card-area');
        if (!area) return;

        area.addEventListener('error', function (e) {
            const target = e.target;
            if (!target || target.tagName !== 'IMG') return;
            flagCardSlotError(target);
        }, true);

        area.addEventListener('load', function (e) {
            const target = e.target;
            if (!target || target.tagName !== 'IMG') return;
            if (!isCardBackSrc(target.getAttribute('src'))) {
                clearCardSlotError(target);
            }
        }, true);
    }

    // ---------- Boot ---------------------------------------------

    onReady(function () {
        ensureMarquee();
        ensureHopper();
        wireButtonHaptic();
        wireReadyFlag();
        observeWinDisplay();
        wireIdleAttract();
        wireCardFaceGuard();
    });

    // Expose minimal debug hook
    window.Lucky5V8 = {
        testWin: function (tier) { applyWinTier(tier || 'v8-win-big'); },
        enterAttract: enterAttract,
        exitAttract: exitAttract
    };
})();
