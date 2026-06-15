'use strict';

window.CabinetTransition = (function () {
    const fps = () => Number(window.GAME_CONFIG?.cabinet?.fps || 60);
    const msPerFrame = () => 1000 / fps();

    const queue = [];
    let active = false;
    let currentTimer = null;
    let virtualClockMs = 0;

    function framesToMs(frames) {
        return Math.max(0, Math.round(Number(frames || 0) * msPerFrame()));
    }

    // Use a simple boolean lock rather than a depth counter.
    // A depth counter goes negative when flush() is called mid-queue or when
    // _setLocked(false) is called more times than _setLocked(true).
    let _locked = false;

    function _setLocked(locked) {
        if (!window.CabinetState) return;
        const next = Boolean(locked);
        if (_locked === next) return; // no change — skip the updatePresentation noise
        _locked = next;
        CabinetState.updatePresentation({ locked: _locked, planDepth: _locked ? 1 : 0 });
    }

    function _next() {
        if (queue.length === 0) {
            active = false;
            _setLocked(false);
            return;
        }

        active = true;
        // Keep locked=true for the entire duration of the queue without toggling per-step.
        if (!_locked) _setLocked(true);

        const step = queue.shift();
        const durationMs = framesToMs(step.frames || 0);
        CabinetState.updatePresentation({
            lastTransition: step.name || null,
            lastAction: step.actionType || step.name || 'STEP'
        });

        try {
            if (typeof step.run === 'function') {
                step.run();
            }
        } catch (_) {
            // keep planner alive even if a visual step fails
        }

        currentTimer = setTimeout(() => {
            currentTimer = null;
            CabinetState.updatePresentation({
                frame: CabinetState.get().presentation.frame + Math.max(1, Number(step.frames || 0))
            });
            _next();
        }, durationMs);
    }

    function enqueue(steps) {
        const list = Array.isArray(steps) ? steps.filter(Boolean) : [steps].filter(Boolean);
        if (list.length === 0) return;
        queue.push(...list);
        if (!active && !currentTimer) {
            _next();
        }
    }

    function flush() {
        if (currentTimer) {
            clearTimeout(currentTimer);
            currentTimer = null;
        }
        queue.length = 0;
        active = false;
        // Hard-reset without going through _setLocked to avoid planDepth arithmetic
        _locked = false;
        if (window.CabinetState) {
            CabinetState.updatePresentation({ locked: false, planDepth: 0 });
        }
    }

    function advanceTime(ms) {
        const frames = Math.max(1, Math.round(Number(ms || 0) / msPerFrame()));
        virtualClockMs += Number(ms || 0);
        CabinetState.updatePresentation({
            frame: CabinetState.get().presentation.frame + frames
        });
        return {
            advancedMs: Number(ms || 0),
            advancedFrames: frames,
            virtualClockMs
        };
    }

    function dispatch(action) {
        if (!action || !action.type) return;
        CabinetState.updatePresentation({ lastAction: action.type });

        switch (action.type) {
            case 'RENDER_DEAL':
                // game.js / CabinetStage own the deal DOM directly.
                // This step is lock window + audio only.
                enqueue([
                    {
                        name: 'deal-cards',
                        actionType: action.type,
                        frames: Math.max(1, Math.ceil((Number(action.cardCount || 5) * Number(action.staggerFrames || 6)) + Number(action.settleFrames || 12))),
                        run: function () {
                            if (window.CabinetAudio) CabinetAudio.queue('deal');
                        }
                    }
                ]);
                break;
            case 'RENDER_DRAW':
                // game.js calls CabinetStage.drawCards directly; this step provides only
                // the lock window and the audio cue to avoid double-animation.
                enqueue([
                    {
                        name: 'draw-cards',
                        actionType: action.type,
                        frames: Math.max(1, Number(action.frames || 18)),
                        run: function () {
                            if (window.CabinetAudio) CabinetAudio.queue('draw');
                        }
                    }
                ]);
                break;
            case 'RENDER_DOUBLEUP':
                // game.js / CabinetStage own the double-up DOM directly.
                // This step is lock window + audio only.
                enqueue([
                    {
                        name: 'doubleup-stage',
                        actionType: action.type,
                        frames: Math.max(1, Number(action.frames || 10)),
                        run: function () {
                            if (window.CabinetAudio) CabinetAudio.queue('doubleup');
                        }
                    }
                ]);
                break;
            case 'COLLECT_WIN':
                enqueue([
                    {
                        name: 'collect-win',
                        actionType: action.type,
                        frames: Math.max(1, Number(action.frames || 24)),
                        run: function () {
                            if (window.CabinetAudio) CabinetAudio.queue('collect', { priority: 'low' });
                            if (window.CabinetPace?.collectWin) {
                                CabinetPace.collectWin(action.amount || 0, action.fromCredits || 0, action.toCredits || 0, action.onComplete);
                            } else if (typeof action.onComplete === 'function') {
                                action.onComplete();
                            }
                        }
                    }
                ]);
                break;
            case 'FILL_JACKPOT':
                enqueue([
                    {
                        name: 'jackpot-fill',
                        actionType: action.type,
                        frames: Math.max(1, Number(action.frames || 90)),
                        run: function () {
                            if (window.CabinetPace?.fillJackpot) {
                                CabinetPace.fillJackpot(action.element, action.fromValue || 0, action.toValue || 0, action.onComplete);
                            } else if (typeof action.onComplete === 'function') {
                                action.onComplete();
                            }
                        }
                    }
                ]);
                break;
            case 'FLASH_LUCKY5':
                enqueue([
                    {
                        name: 'lucky5-flash',
                        actionType: action.type,
                        frames: Math.max(1, Number(action.frames || 12)),
                        run: function () {
                            if (window.CabinetAudio) CabinetAudio.queue('lucky5', { priority: 'high' });
                            if (window.CabinetPace?.flashLucky5) {
                                CabinetPace.flashLucky5();
                            }
                        }
                    }
                ]);
                break;
            case 'MACHINE_CLOSED':
                enqueue([
                    {
                        name: 'machine-closed',
                        actionType: action.type,
                        frames: Math.max(1, Number(action.frames || 18)),
                        run: function () {
                            if (window.CabinetAudio) CabinetAudio.queue('machineClose', { priority: 'high' });
                            if (typeof action.onRun === 'function') action.onRun();
                        }
                    }
                ]);
                break;
            default:
                break;
        }
    }

    return {
        enqueue,
        dispatch,
        framesToMs,
        flush,
        advanceTime
    };
})();
