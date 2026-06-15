/* ============================================================
   LUCKY 5 v8 — Cabinet realism effects runtime
   ------------------------------------------------------------
   Self-contained, idempotent, DOM-safe. Attaches after existing
   cabinet scripts so it can observe the mature DOM and only
   augments (never removes) existing behavior.
   ------------------------------------------------------------
   Provides:
     - Cabinet marquee injection (#cabinet-marquee)
     - Coin hopper injection (#cabinet-hopper)
     - Button press haptic (.is-pressed class on .cab-btn)
     - Win celebration tier detection from #win-amount-value
     - Coin shower canvas on big/jackpot wins
     - Card-area win/lose classes driven from #paytable data
     - Idle attract-mode toggle after 30s
     - Body.v8-ready flag after asset loader completes
   ============================================================ */
(function () {
    'use strict';

    if (window.__lucky5V8EffectsLoaded) return;
    window.__lucky5V8EffectsLoaded = true;

    // Arcade-calibrated attract delay (2026-04-20). Reads from GAME_CONFIG
    // so variants can extend / shorten the attract-mode trigger centrally.
    // Falls back to 12 s if GAME_CONFIG hasn't loaded yet.
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

    function ensureCoinCanvas() {
        const container = document.getElementById('game-container');
        if (!container || document.getElementById('v8-coin-canvas')) return;
        const canvas = document.createElement('canvas');
        canvas.id = 'v8-coin-canvas';
        container.appendChild(canvas);
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

    // ---------- Win tier detection -------------------------------

    function classifyWin(amount, categoryEl) {
        if (!amount || amount <= 0) return null;
        const category = categoryEl ? (categoryEl.textContent || '').toUpperCase() : '';
        if (category.includes('ROYAL') || category.includes('JACKPOT')) return 'v8-win-jackpot';
        if (category.includes('STRAIGHT FLUSH') ||
            category.includes('4 OF A KIND') ||
            category.includes('FOUR OF A KIND')) return 'v8-win-big';
        if (category.includes('FULL HOUSE') ||
            category.includes('FLUSH') ||
            category.includes('STRAIGHT')) return 'v8-win-medium';
        if (amount >= 100_000) return 'v8-win-big';
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
        const clearMs = tier === 'v8-win-jackpot' ? 3200
            : tier === 'v8-win-big' ? 2200
                : tier === 'v8-win-medium' ? 1400
                    : 900;
        setTimeout(clearWinClasses, clearMs);

        if (tier === 'v8-win-big' || tier === 'v8-win-jackpot') {
            startCoinShower(tier === 'v8-win-jackpot' ? 2800 : 1600);
        }
    }

    function observeWinDisplay() {
        const winValue = document.getElementById('win-amount-value');
        const winTag = document.getElementById('win-slot-tag');
        if (!winValue) return;

        let lastText = '';
        const observer = new MutationObserver(function () {
            const now = winValue.textContent || '';
            if (now === lastText) return;
            lastText = now;
            const amount = parseWinAmount(now);
            if (amount <= 0) return;
            applyWinTier(classifyWin(amount, winTag));
        });
        observer.observe(winValue, {
            childList: true,
            characterData: true,
            subtree: true
        });
    }

    // ---------- Coin shower canvas -------------------------------

    const COIN_STATE = {
        animating: false,
        coins: [],
        rafId: 0,
        stopAt: 0
    };

    function sizeCanvas(canvas) {
        const parent = canvas.parentElement;
        if (!parent) return;
        const rect = parent.getBoundingClientRect();
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        canvas.width = Math.round(rect.width * dpr);
        canvas.height = Math.round(rect.height * dpr);
        canvas.style.width = rect.width + 'px';
        canvas.style.height = rect.height + 'px';
        const ctx = canvas.getContext('2d');
        if (ctx) ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    function spawnCoins(canvas, n) {
        const rect = canvas.getBoundingClientRect();
        for (let i = 0; i < n; i++) {
            COIN_STATE.coins.push({
                x: Math.random() * rect.width,
                y: -20 - Math.random() * 100,
                vx: (Math.random() - 0.5) * 1.6,
                vy: 1.5 + Math.random() * 2.8,
                size: 5 + Math.random() * 6,
                rot: Math.random() * Math.PI * 2,
                vrot: (Math.random() - 0.5) * 0.3,
                hue: 38 + Math.random() * 8
            });
        }
    }

    function drawCoin(ctx, c) {
        ctx.save();
        ctx.translate(c.x, c.y);
        ctx.rotate(c.rot);
        const grad = ctx.createRadialGradient(0, 0, 1, 0, 0, c.size);
        grad.addColorStop(0, 'hsl(' + c.hue + ', 95%, 78%)');
        grad.addColorStop(0.6, 'hsl(' + c.hue + ', 90%, 52%)');
        grad.addColorStop(1, 'hsl(' + (c.hue - 6) + ', 85%, 32%)');
        ctx.fillStyle = grad;
        ctx.beginPath();
        ctx.ellipse(0, 0, c.size * Math.abs(Math.cos(c.rot)), c.size, 0, 0, Math.PI * 2);
        ctx.fill();
        ctx.strokeStyle = 'rgba(120,70,10,0.45)';
        ctx.lineWidth = 1;
        ctx.stroke();
        ctx.restore();
    }

    function tickCoinShower(canvas) {
        const ctx = canvas.getContext('2d');
        if (!ctx) return;
        const rect = canvas.getBoundingClientRect();
        ctx.clearRect(0, 0, rect.width, rect.height);
        for (let i = COIN_STATE.coins.length - 1; i >= 0; i--) {
            const c = COIN_STATE.coins[i];
            c.x += c.vx;
            c.y += c.vy;
            c.vy += 0.12;
            c.rot += c.vrot;
            drawCoin(ctx, c);
            if (c.y - c.size > rect.height + 30) {
                COIN_STATE.coins.splice(i, 1);
            }
        }
        const now = performance.now();
        if (now < COIN_STATE.stopAt) {
            spawnCoins(canvas, 2);
        }
        if (COIN_STATE.coins.length > 0 || now < COIN_STATE.stopAt) {
            COIN_STATE.rafId = requestAnimationFrame(function () { tickCoinShower(canvas); });
        } else {
            COIN_STATE.animating = false;
            ctx.clearRect(0, 0, rect.width, rect.height);
        }
    }

    function startCoinShower(durationMs) {
        const canvas = document.getElementById('v8-coin-canvas');
        if (!canvas) return;
        sizeCanvas(canvas);
        if (!COIN_STATE.animating) {
            COIN_STATE.animating = true;
            COIN_STATE.stopAt = performance.now() + durationMs;
            spawnCoins(canvas, 24);
            tickCoinShower(canvas);
        } else {
            COIN_STATE.stopAt = Math.max(COIN_STATE.stopAt, performance.now() + durationMs);
            spawnCoins(canvas, 16);
        }
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
        // Do not attract while user is in double-up or mid-round UI states.
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

    // ---------- Ready / warm-up flag -----------------------------

    function markReady() {
        document.body.classList.add('v8-ready');
    }

    function wireReadyFlag() {
        // The existing loader toggles #asset-loader.done. Watch for it.
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
        // Fallback: force ready after 6 s so the cabinet is usable even if
        // the loader somehow stalls.
        setTimeout(markReady, 6000);
    }

    // ---------- Resize handling ----------------------------------

    function wireResize() {
        // rAF-debounced so a burst of resize / orientation events collapses
        // into a single relayout. Critical on mobile where orientation
        // change fires many pixel-ratio / viewport events in < 16 ms.
        let pending = 0;
        const run = function () {
            pending = 0;
            const canvas = document.getElementById('v8-coin-canvas');
            if (canvas) sizeCanvas(canvas);
        };
        const schedule = function () {
            if (pending) return;
            pending = requestAnimationFrame(run);
        };
        window.addEventListener('resize', schedule, { passive: true });
        window.addEventListener('orientationchange', schedule, { passive: true });
    }

    // ---------- Card-face load-error guard -----------------------
    //
    // If a card face PNG fails to load (404 on the deployed server, broken
    // CDN cache, or missing asset), the <img> element silently keeps the
    // previous src OR shows a transparent broken icon depending on browser.
    // Operators only see "cards look wrong" with no diagnostic signal.
    //
    // We attach delegated error/load listeners on the card-area so any
    // replacement <img> in .card-slot .card-face automatically reports
    // whether it successfully swapped to a face or failed. Failures mark
    // the slot with .v8-face-error which the v8 quality CSS renders as
    // a red MISSING ribbon — instantly visible in any deployment.

    const FACE_ERROR_CLASS = 'v8-face-error';

    function cardBackSrc() {
        const cfg = window.GAME_CONFIG && window.GAME_CONFIG.assets;
        return (cfg && cfg.cardBack) || '/assets/images/cards/bside.png';
    }

    function flagCardSlotError(img) {
        const slot = img.closest ? img.closest('.card-slot, .du-card-slot') : null;
        if (!slot) return;
        slot.classList.add(FACE_ERROR_CLASS);
        if (console && console.warn) {
            console.warn('[Lucky5 v8] card face failed to load', {
                src: img.getAttribute('src'),
                alt: img.getAttribute('alt'),
                slot: slot.dataset.slot || slot.dataset.duSlot || '?'
            });
        }
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

        // Delegated load + error events from all <img> descendants.
        area.addEventListener('error', function (e) {
            const target = e.target;
            if (!target || target.tagName !== 'IMG') return;
            flagCardSlotError(target);
        }, true);

        area.addEventListener('load', function (e) {
            const target = e.target;
            if (!target || target.tagName !== 'IMG') return;
            // Only clear the error flag once a real face loaded (not the back).
            if (!isCardBackSrc(target.getAttribute('src'))) {
                clearCardSlotError(target);
            }
        }, true);
    }

    // ---------- Boot ---------------------------------------------

    onReady(function () {
        ensureMarquee();
        ensureHopper();
        ensureCoinCanvas();
        wireButtonHaptic();
        wireReadyFlag();
        observeWinDisplay();
        wireIdleAttract();
        wireResize();
        wireCardFaceGuard();
    });

    // Expose minimal debug hook for manual QA
    window.Lucky5V8 = {
        testWin: function (tier) { applyWinTier(tier || 'v8-win-big'); },
        coinShower: function (ms) { startCoinShower(ms || 1800); },
        enterAttract: enterAttract,
        exitAttract: exitAttract
    };
})();
