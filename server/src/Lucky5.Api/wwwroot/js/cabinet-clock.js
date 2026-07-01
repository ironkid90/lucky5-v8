/**
 * cabinet-clock.js
 * OWNER: Antigravity
 * PURPOSE: 60Hz deterministic cabinet tick clock and scanned hardware inputs.
 */
'use strict';

window.CabinetClock = (function () {
    let tickCount = 0;
    const queue = [];
    const handlers = new Set();
    let lastTime = null;
    let accumulatedMs = 0;
    const tickIntervalMs = 1000 / 60; // ~16.67ms
    let isRunning = false;
    let queueOrder = 0; // monotonic counter for stable ordering

    function start(options = {}) {
        // If already running, handle reset without creating duplicate RAF loop
        if (isRunning) {
            if (options.reset) {
                tickCount = 0;
                queue.length = 0;
                queueOrder = 0;
                lastTime = performance.now();
                accumulatedMs = 0;
            }
            return;
        }
        isRunning = true;
        if (options.reset) {
            tickCount = 0;
            queue.length = 0;
            queueOrder = 0;
        }
        lastTime = performance.now();
        accumulatedMs = 0;
        requestAnimationFrame(update);
    }

    function update(now) {
        if (!isRunning) return;
        let dt = now - lastTime;
        if (dt > 1000) dt = tickIntervalMs; // clamp giant lag spikes
        lastTime = now;
        accumulatedMs += dt;

        while (accumulatedMs >= tickIntervalMs) {
            accumulatedMs -= tickIntervalMs;
            tick();
        }
        requestAnimationFrame(update);
    }

    function tick() {
        tickCount++;
        
        // Execute handlers registered for every tick (e.g. meter drains, input scanner)
        for (const handler of handlers) {
            try {
                handler(tickCount);
            } catch (e) {
                console.error('[CabinetClock] Handler error:', e);
            }
        }

        // Execute queued callbacks
        // Extract ready items, sort by order, and execute in insertion order
        const ready = [];
        for (let i = queue.length - 1; i >= 0; i--) {
            const item = queue[i];
            if (tickCount >= item.targetTick) {
                queue.splice(i, 1);
                if (!item.cancelled) {
                    ready.push(item);
                }
            }
        }
        
        // Sort by insertion order for deterministic execution
        ready.sort((a, b) => a.order - b.order);
        
        for (const item of ready) {
            try {
                item.callback();
            } catch (e) {
                console.error('[CabinetClock] Callback error:', e);
            }
        }
    }

    function delayTicks(ticks, callback, options = {}) {
        const targetTick = tickCount + Math.max(1, Math.round(ticks));
        const order = queueOrder++;
        const namespace = options.namespace || null;
        queue.push({
            targetTick,
            callback,
            order,
            namespace,
            cancelled: false
        });
        return () => {
            // Find and mark as cancelled
            const item = queue.find(i => i.order === order);
            if (item) item.cancelled = true;
        };
    }

    function afterTicks(ticks, callback) {
        return delayTicks(ticks, callback);
    }

    function afterMs(ms, callback) {
        return delayTicks(msToTicks(ms), callback);
    }

    function sequence(steps, onComplete) {
        if (!Array.isArray(steps) || steps.length === 0) {
            if (onComplete) onComplete();
            return;
        }
        let offsetTicks = 0;
        for (const step of steps) {
            const delay = Math.max(0, Number(step.delayTicks) || 0);
            offsetTicks += delay;
            delayTicks(offsetTicks, () => {
                try {
                    if (typeof step.run === 'function') step.run();
                } catch (e) {
                    console.error('[CabinetClock] Sequence step error:', e);
                }
            });
        }
        delayTicks(offsetTicks + 1, () => {
            if (onComplete) onComplete();
        });
    }

    function msToTicks(ms) {
        return Math.max(1, Math.round(ms / tickIntervalMs));
    }

    function delayMs(ms, callback) {
        return delayTicks(msToTicks(ms), callback);
    }

    function registerHandler(fn) {
        handlers.add(fn);
    }

    function unregisterHandler(fn) {
        handlers.delete(fn);
    }

    function cancelNamespace(namespace) {
        if (!namespace) return;
        for (const item of queue) {
            if (item.namespace === namespace) {
                item.cancelled = true;
            }
        }
    }

    return {
        start,
        stop: () => { isRunning = false; },
        getTickCount: () => tickCount,
        msToTicks,
        delayTicks,
        delayMs,
        afterTicks,
        afterMs,
        sequence,
        cancelNamespace,
        registerHandler,
        unregisterHandler,
        getTickIntervalMs: () => tickIntervalMs
    };
})();

// Start it immediately
window.CabinetClock.start();

// Unified deterministic scanned hardware inputs module
window.CabinetInput = (function () {
    const debounceTicks = 4; // debounce for 4 ticks (approx 66ms)
    const lastPressTick = {}; // buttonId -> tickCount of last press
    const inputQueue = []; // queue of inputs to process on next tick

    // Precedence priority: lower number = higher priority
    const PREC_ORDER = {
        'btn-deal': 1,
        'cab-hold-0': 2,
        'cab-hold-1': 2,
        'cab-hold-2': 2,
        'cab-hold-3': 2,
        'cab-hold-4': 2,
        'btn-big': 3,
        'btn-small': 3,
        'btn-take-score': 4,
        'btn-take-half': 5,
        'btn-bet': 6,
        'btn-cancel': 7
    };

    function triggerInput(buttonId, actionFn) {
        const currentTick = window.CabinetClock.getTickCount();
        const hasLast = Object.prototype.hasOwnProperty.call(lastPressTick, buttonId);
        const lastTick = hasLast ? lastPressTick[buttonId] : -Infinity;
        
        // Debounce: ignore if pressed too recently
        if (currentTick - lastTick < debounceTicks) {
            return;
        }
        
        lastPressTick[buttonId] = currentTick;

        // Push to inputQueue for deterministic scan processing on next tick
        inputQueue.push({
            id: buttonId,
            action: actionFn,
            priority: PREC_ORDER[buttonId] || 99
        });
    }

    function processTickInputs() {
        if (inputQueue.length === 0) return;

        // Sort by precedence order (highest priority first)
        inputQueue.sort((a, b) => a.priority - b.priority);

        // Execute the single dominant input in this scan tick
        const dominantInput = inputQueue.shift();
        inputQueue.length = 0; // Throw away secondary/conflicting inputs scanned in the same frame

        if (dominantInput) {
            dominantInput.action();
        }
    }

    // Register with CabinetClock to act as the scan interrupt (NMI routine)
    window.CabinetClock.registerHandler(processTickInputs);

    return {
        trigger: triggerInput
    };
})();
