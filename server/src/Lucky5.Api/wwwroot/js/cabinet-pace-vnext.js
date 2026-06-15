/**
 * cabinet-pace-vnext.js
 * OWNER: Codex Agent 2
 * PURPOSE: Count-up animations, win collection pacing, Lucky5 flash, jackpot fill.
 * LOADS AFTER: cabinet-stage-vnext.js
 * DO NOT EDIT: game.css, game.js, index.html, any .cs backend files
 *
 * Expose API on window.CabinetPace — game.js calls these after state transitions.
 * No game logic here — purely presentation timing and animation.
 */

'use strict';

window.CabinetPace = (function () {

    const _fmt  = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });
    let _activeAnimations = new WeakMap();
    let _timers = new Set();

    function _resolveConfig(overrides) {
        const _cfg = (typeof GAME_CONFIG !== 'undefined') ? GAME_CONFIG.timing : null;
        const next = {
            countUpMinMs: _cfg ? _cfg.countUpMinMs : 1500,
            countUpMaxMs: _cfg ? _cfg.countUpMaxMs : 5000,
            jackpotFillMinMs: _cfg ? _cfg.jackpotFillMinMs : 10000,
            jackpotFillMaxMs: _cfg ? _cfg.jackpotFillMaxMs : 15000,
            lucky5ActiveMs: _cfg ? _cfg.lucky5ActiveScreenMs : 1400,
            collectDelayMs: 180
        };
        if (overrides && typeof overrides === 'object') {
            Object.assign(next, overrides);
        }
        return next;
    }

    let _config = _resolveConfig();

    function configure(overrides) {
        _config = _resolveConfig(overrides);
        return getConfig();
    }

    function getConfig() {
        return {
            countUpMinMs: _config.countUpMinMs,
            countUpMaxMs: _config.countUpMaxMs,
            jackpotFillMinMs: _config.jackpotFillMinMs,
            jackpotFillMaxMs: _config.jackpotFillMaxMs,
            lucky5ActiveMs: _config.lucky5ActiveMs,
            collectDelayMs: _config.collectDelayMs
        };
    }

    function _safeNumber(value) {
        const n = Number(value);
        return Number.isFinite(n) ? n : 0;
    }

    function _clamp01(value) {
        return Math.max(0, Math.min(1, Number(value) || 0));
    }

    function _setTimer(fn, ms) {
        const id = setTimeout(() => {
            _timers.delete(id);
            fn();
        }, ms);
        _timers.add(id);
        return id;
    }

    function _cancelElementAnimation(element) {
        if (!element) return;
        const active = _activeAnimations.get(element);
        if (active && active.rafId) cancelAnimationFrame(active.rafId);
        _activeAnimations.delete(element);
    }

    /* ── countUp ─────────────────────────────────────────────────────────── */
    /**
     * Animate a numeric element from startValue to endValue over durationMs.
     * Uses requestAnimationFrame. Sets element.textContent.
     * @param {HTMLElement} element
     * @param {number} startValue
     * @param {number} endValue
     * @param {number} durationMs
     * @param {function} [onComplete]
     */
    function countUp(element, startValue, endValue, durationMs, onComplete) {
        if (!element) { if (onComplete) onComplete(); return; }
        _cancelElementAnimation(element);

        const startNum = _safeNumber(startValue);
        const endNum = _safeNumber(endValue);
        const duration = Math.max(60, _safeNumber(durationMs));
        if (startNum === endNum) {
            element.textContent = _fmt.format(endNum);
            if (onComplete) onComplete();
            return;
        }

        const start = performance.now();
        const range = endNum - startNum;

        function step(now) {
            const currentAnim = _activeAnimations.get(element);
            if (!currentAnim) return;
            const elapsed = now - start;
            const progress = Math.min(elapsed / duration, 1);
            // ease-out cubic
            const ease = 1 - Math.pow(1 - progress, 3);
            const current = Math.round(startNum + range * ease);
            element.textContent = _fmt.format(current);
            if (progress < 1) {
                currentAnim.rafId = requestAnimationFrame(step);
            } else {
                element.textContent = _fmt.format(endNum);
                _activeAnimations.delete(element);
                if (onComplete) onComplete();
            }
        }

        const animState = { rafId: requestAnimationFrame(step) };
        _activeAnimations.set(element, animState);
    }

    /* ── collectWin ──────────────────────────────────────────────────────── */
    /**
     * Show win amount instantly, then count credits up by winAmount.
     * Duration scales with amount: 1500ms (small) to 5000ms (large).
     * @param {number} winAmount
     * @param {number} currentCredits   — credits BEFORE the win is added
     * @param {number} newCredits       — credits AFTER collection
     * @param {function} [onComplete]
     */
    function collectWin(winAmount, currentCredits, newCreditsOrCallback, onComplete) {
        const amount = _safeNumber(winAmount);
        const fromCredits = _safeNumber(currentCredits);
        let toCredits = fromCredits + amount;
        let done = onComplete;
        if (typeof newCreditsOrCallback === 'number') {
            toCredits = _safeNumber(newCreditsOrCallback);
        } else if (typeof newCreditsOrCallback === 'function') {
            done = newCreditsOrCallback;
        }

        // Show win amount immediately
        const winEl = document.getElementById('win-amount-value');
        if (winEl) winEl.textContent = _fmt.format(amount);

        // Scale duration from config min/max based on amount tiers
        const minMs = Math.max(200, _safeNumber(_config.countUpMinMs));
        const maxMs = Math.max(minMs, _safeNumber(_config.countUpMaxMs));
        const duration = Math.round(
            minMs + (maxMs - minMs) * _clamp01(amount / 5_000_000)
        );

        // Brief pause then count credits up
        _setTimer(() => {
            const credEl = document.querySelector('#credits span');
            if (credEl) {
                countUp(credEl, fromCredits, toCredits, duration, done);
            } else {
                if (done) done();
            }
        }, Math.max(0, _safeNumber(_config.collectDelayMs)));
    }

    /* ── fillJackpot ─────────────────────────────────────────────────────── */
    /**
     * Count a jackpot meter from fromValue to toValue over 10-15 seconds.
     * @param {HTMLElement} jpCvalElement   — the .jp-cval span inside a .jp-counter
     * @param {number} fromValue
     * @param {number} toValue
     * @param {function} [onComplete]
     */
    function fillJackpot(jpCvalElement, fromValue, toValue, onComplete) {
        const fromNum = _safeNumber(fromValue);
        const toNum = _safeNumber(toValue);
        const delta = Math.abs(toNum - fromNum);
        const minMs = Math.max(500, _safeNumber(_config.jackpotFillMinMs));
        const maxMs = Math.max(minMs, _safeNumber(_config.jackpotFillMaxMs));
        // Scale jackpot fill from config min/max based on delta.
        const duration = Math.round(
            minMs + (maxMs - minMs) * _clamp01(delta / 5_000_000)
        );
        countUp(jpCvalElement, fromNum, toNum, duration, onComplete);
    }

    /* ── flashLucky5 ─────────────────────────────────────────────────────── */
    /**
     * Brief white/gold screen flash + banner flicker. Non-blocking.
     */
    function flashLucky5() {
        const flashEl = document.getElementById('lucky5-flash');
        if (flashEl) {
            flashEl.classList.remove('active');
            // force reflow
            void flashEl.offsetWidth;
            flashEl.classList.add('active');
            _setTimer(() => flashEl.classList.remove('active'), Math.max(80, _safeNumber(_config.lucky5ActiveMs)));
        }

        const banner = document.getElementById('lucky5-banner');
        if (banner) {
            banner.classList.add('banner-flicker');
            _setTimer(() => banner.classList.remove('banner-flicker'), 700);
        }

        // Also trigger stage glow/banner
        if (window.CabinetStage && window.CabinetStage.showLucky5Active) {
            window.CabinetStage.showLucky5Active();
        }
    }

    /* ── animateJackpotCounters ──────────────────────────────────────────── */
    /**
     * Incrementally update the live jackpot counter displays from SignalR/API data.
     * Short count-up (300ms) for live trickle updates.
     * @param {{ fourOfAKindA, fourOfAKindB, straightFlush, fullHouse }} jackpots
     * @param {{ fourOfAKindA, fourOfAKindB, straightFlush, fullHouse }} prevJackpots
     */
    function animateJackpotCounters(jackpots, prevJackpots) {
        if (!jackpots) return;
        const DURATION = 300;

        const targets = [
            { el: document.querySelector('#jp-counter-a .jp-cval'),      from: prevJackpots?.fourOfAKindA || 0, to: jackpots.fourOfAKindA },
            { el: document.querySelector('#jp-counter-b .jp-cval'),      from: prevJackpots?.fourOfAKindB || 0, to: jackpots.fourOfAKindB },
            { el: document.querySelector('#jp-counter-center .jp-cval'), from: prevJackpots?.straightFlush || 0, to: jackpots.straightFlush },
            { el: document.querySelector('#jp-counter-fh .jp-cval'),     from: prevJackpots?.fullHouse || 0,    to: jackpots.fullHouse },
        ];

        targets.forEach(({ el, from, to }) => {
            if (!el || from === to) return;
            countUp(el, Number(from), Number(to), DURATION);
        });
    }

    function stopAll() {
        _timers.forEach((id) => clearTimeout(id));
        _timers.clear();
        _activeAnimations = new WeakMap();
    }

    /* ── public API ──────────────────────────────────────────────────────── */

    return {
        configure,
        getConfig,
        countUp,
        collectWin,
        fillJackpot,
        flashLucky5,
        animateJackpotCounters,
        stopAll,
    };

}());
