'use strict';

window.CabinetOrchestrator = (function () {
    const originals = {};
    let installed = false;

    function _fmt(value) {
        return Math.floor(Number(value || 0)).toLocaleString();
    }

    function _safeText(el, text) {
        if (el) el.textContent = text;
    }

    function _renderImmediateCards(cardData, heldIndexes) {
        const area = document.getElementById('card-area');
        if (!area) return;

        if (window.CabinetStage?.initCardSlots) {
            CabinetStage.initCardSlots();
        } else {
            area.innerHTML = '';
        }

        (cardData || []).forEach((card, index) => {
            const slot = area.querySelector(`.card-slot[data-slot="${index}"]`);
            const img = slot?.querySelector('.card-face img');
            if (img) {
                img.src = card && card.code ? `/assets/images/cards/${card.code}.png` : (window.GAME_CONFIG?.assets?.cardBack || '/assets/images/cards/bside.png');
            }
            const held = Array.isArray(heldIndexes) ? heldIndexes.includes(index) : false;
            if (window.CabinetStage?.setHold) {
                CabinetStage.setHold(index, held);
            }
        });
    }

    function _syncAndSelect() {
        const snapshot = CabinetState.syncFromRuntime();
        return {
            snapshot,
            rules: CabinetState.selectors(snapshot)
        };
    }

    function _applyButtonStatesFromSelectors(snapshot) {
        // Accept a pre-computed snapshot to avoid re-entrancy from syncFromRuntime
        // triggering another updateMachine -> _emit -> subscriber -> here cycle.
        const state = snapshot || CabinetState.get();
        const rules = CabinetState.selectors(state);
        const holdBtns = document.querySelectorAll('.cab-hold');
        const betBtn = document.getElementById('btn-bet');
        const dealBtn = document.getElementById('btn-deal');
        const cancelBtn = document.getElementById('btn-cancel');
        const bigBtn = document.getElementById('btn-big');
        const smallBtn = document.getElementById('btn-small');
        const takeScoreBtn = document.getElementById('btn-take-score');
        const takeHalfBtn = document.getElementById('btn-take-half');

        if (betBtn) betBtn.disabled = !rules.canBet;
        if (dealBtn) dealBtn.disabled = !rules.canDeal;
        if (cancelBtn) cancelBtn.disabled = !(state.machine.gameState === 'hold') || state.presentation.locked;
        if (bigBtn) bigBtn.disabled = !rules.canGuess;
        if (smallBtn) smallBtn.disabled = !rules.canGuess;
        if (takeScoreBtn) takeScoreBtn.disabled = !rules.canTakeScore;
        if (takeHalfBtn) takeHalfBtn.disabled = !rules.canTakeHalf;
        // Use data-index attribute so DOM order doesn't have to match slot index
        holdBtns.forEach((btn) => {
            const idx = parseInt(btn.dataset.index, 10);
            btn.disabled = !rules.canHold(Number.isFinite(idx) ? idx : 0);
        });
    }

    function _patch(name, replacement) {
        if (typeof window[name] === 'function' || typeof globalThis[name] === 'function') {
            const current = globalThis[name];
            originals[name] = current;
            globalThis[name] = replacement(current);
        }
    }

    function _installGeometry() {
        const layout = window.GAME_CONFIG?.cabinet?.layout || CabinetState.DEFAULT_LAYOUT;
        CabinetState.updateLayout(layout);

        // Layout is fluid via cabinet-layout-vnext.css min() sizing — no CSS transform scaling.
        // Track viewport scale in state for debug / render_game_to_text output only.
        function resize() {
            const width = layout.width;
            const height = layout.height;
            const scale = Math.min(window.innerWidth / width, window.innerHeight / height);
            CabinetState.updatePresentation({ viewportScale: scale });
        }

        resize();
        window.addEventListener('resize', resize);
    }

    function _installDebugHooks() {
        window.render_game_to_text = function renderGameToText() {
            const snapshot = CabinetState.get();
            return JSON.stringify({
                coordinateSystem: 'origin top-left; +x right; +y down; base layout 720x1280',
                machine: snapshot.machine,
                presentation: snapshot.presentation,
                selectors: CabinetState.selectors(snapshot)
            });
        };

        window.advanceTime = function advanceTime(ms) {
            return CabinetTransition.advanceTime(ms);
        };
    }

    function _installInputGuards() {
        // Use click capture (not pointerdown) so game.js onclick handlers are blocked.
        // pointerdown+stopPropagation does not cancel a subsequent click event.
        document.addEventListener('click', function (event) {
            const btn = event.target.closest('.cab-btn, .menu-panel-btn, .menu-panel-close');
            if (!btn) return;
            if (CabinetState.get().presentation.locked && !btn.closest('#menu-panel')) {
                event.preventDefault();
                event.stopImmediatePropagation();
                if (window.CabinetAudio) CabinetAudio.queue('invalid', { priority: 'high' });
            }
        }, true);
    }

    function install() {
        if (installed) return;
        installed = true;

        if (!window.CabinetState || !window.CabinetTransition) return;

        CabinetState.syncFromRuntime();
        CabinetAudio?.preload?.();
        _installGeometry();
        _installDebugHooks();
        _installInputGuards();

        _patch('playPress', function () {
            return function patchedPlayPress() {
                if (window.CabinetAudio) {
                    CabinetAudio.queue('press');
                    return;
                }
                if (typeof originals.playPress === 'function') {
                    originals.playPress.call(this);
                }
            };
        });

        _patch('showMessage', function (legacy) {
            return function patchedShowMessage(text, type) {
                const result = legacy.call(this, text, type);
                CabinetState.updateMachine({ message: text || '', messageType: type || '' });
                return result;
            };
        });

        _patch('updateCredits', function (legacy) {
            return function patchedUpdateCredits() {
                const result = legacy.call(this);
                CabinetState.syncFromRuntime();
                return result;
            };
        });

        _patch('updateStakeDisplay', function (legacy) {
            return function patchedUpdateStakeDisplay() {
                const result = legacy.call(this);
                CabinetState.syncFromRuntime();
                return result;
            };
        });

        _patch('updateWinIndicator', function (legacy) {
            return function patchedUpdateWinIndicator(amount) {
                const result = legacy.call(this, amount);
                CabinetState.updateMachine({ winAmount: Number(amount || 0) });
                return result;
            };
        });

        _patch('setButtonStates', function (legacy) {
            return function patchedSetButtonStates() {
                const result = typeof legacy === 'function'
                    ? legacy.call(this)
                    : undefined;
                // syncFromRuntime here so selectors see the live gameState value,
                // not a store snapshot that may lag behind the caller's state mutation.
                const freshSnapshot = CabinetState.syncFromRuntime();
                _applyButtonStatesFromSelectors(freshSnapshot);
                return result;
            };
        });

        _patch('renderDealStage', function (legacy) {
            return function patchedRenderDealStage(cardData) {
                const result = legacy.call(this, cardData);
                CabinetState.updateMachine({
                    cards: Array.isArray(cardData) ? cardData : [],
                    holdIndexes: []
                });
                return result;
            };
        });

        _patch('renderDrawStage', function (legacy) {
            return function patchedRenderDrawStage(cardData, heldIndexes) {
                const result = legacy.call(this, cardData, heldIndexes);
                const heldSnapshot = Array.isArray(heldIndexes)
                    ? heldIndexes
                    : Array.from(heldIndexes || []);
                CabinetState.updateMachine({
                    cards: Array.isArray(cardData) ? cardData : [],
                    holdIndexes: heldSnapshot
                });
                return result;
            };
        });

        _patch('renderDoubleUpCards', function (legacy) {
            return function patchedRenderDoubleUpCards(dealerCard, showShuffle, challengerCard) {
                const result = legacy.call(this, dealerCard, showShuffle, challengerCard);
                const trail = (typeof duCardTrail !== 'undefined' && Array.isArray(duCardTrail)) ? duCardTrail : [];
                CabinetState.updateMachine({
                    duDealerCard: dealerCard || null,
                    duCardTrail: trail
                });
                return result;
            };
        });

        _patch('triggerLucky5Flash', function (legacy) {
            return function patchedTriggerLucky5Flash() {
                CabinetState.updatePresentation({ lucky5Active: true });
                CabinetTransition.dispatch({ type: 'FLASH_LUCKY5', frames: 12 });
                if (typeof legacy === 'function') {
                    try { legacy.call(this); } catch (_) {}
                }
            };
        });

        _patch('animateJackpotFill', function (legacy) {
            return function patchedAnimateJackpotFill(amount, startBalance, handName) {
                // Guard active4kSlot — it is a game.js global that may not be set yet
                const slot4k = (typeof active4kSlot !== 'undefined') ? active4kSlot : 0;
                const counterSelector = handName === 'FullHouse'
                    ? '#jp-counter-fh .jp-cval'
                    : handName === 'StraightFlush'
                        ? '#jp-counter-center .jp-cval'
                        : slot4k === 1
                            ? '#jp-counter-b .jp-cval'
                            : '#jp-counter-a .jp-cval';
                const element = document.querySelector(counterSelector);

                // Delegate entirely to legacy (which calls CabinetPace.fillJackpot internally
                // via the patched animateJackpotFill path in cabinet-pace-vnext.js).
                // Only sync state after completion to avoid double-count.
                if (typeof legacy === 'function') {
                    return legacy.call(this, amount, startBalance, handName).then(() => {
                        CabinetState.syncFromRuntime();
                    }).catch(() => {
                        CabinetState.syncFromRuntime();
                    });
                }

                // Fallback: dispatch directly if legacy is unavailable
                return new Promise((resolve) => {
                    CabinetTransition.dispatch({
                        type: 'FILL_JACKPOT',
                        element,
                        fromValue: Number(amount || 0),
                        toValue: Number(window.GAME_CONFIG?.rules?.jackpotReset?.[handName] || 0),
                        frames: Math.max(60, Math.round((Number(window.GAME_CONFIG?.timing?.jackpotFillMinMs || 10000) / 1000) * 60)),
                        onComplete: resolve
                    });
                });
            };
        });

        _patch('animateDrainToCredits', function (legacy) {
            return function patchedAnimateDrainToCredits(amount, startBalance) {
                // Delegate to legacy which calls CabinetPace.collectWin internally.
                // Do NOT also dispatch COLLECT_WIN — that would double-count the credit drain animation.
                if (typeof legacy === 'function') {
                    return legacy.call(this, amount, startBalance).then(() => {
                        CabinetState.syncFromRuntime();
                    }).catch(() => {
                        CabinetState.syncFromRuntime();
                    });
                }

                // Fallback: dispatch directly if legacy is unavailable
                return new Promise((resolve) => {
                    CabinetTransition.dispatch({
                        type: 'COLLECT_WIN',
                        amount: Number(amount || 0),
                        fromCredits: Number(startBalance || 0),
                        toCredits: Number(startBalance || 0) + Number(amount || 0),
                        frames: Math.max(24, Math.round((Number(window.GAME_CONFIG?.timing?.countUpMinMs || 3000) / 1000) * 60)),
                        onComplete: function () {
                            CabinetState.syncFromRuntime();
                            resolve();
                        }
                    });
                });
            };
        });

        _patch('fetchMachineSession', function (legacy) {
            return async function patchedFetchMachineSession() {
                const result = await legacy.call(this);
                CabinetState.syncFromRuntime({
                    machineCanCashOut: Boolean(result?.canCashOut),
                    machineSessionClosed: Boolean(result?.isMachineClosed),
                    machineCashOutThreshold: Number(result?.cashOutThreshold || 0)
                });
                return result;
            };
        });

        _patch('restoreRoundFromSnapshot', function (legacy) {
            return function patchedRestoreRoundFromSnapshot(snapshot) {
                const result = legacy.call(this, snapshot);
                // Sync runtime globals updated by legacy, then overlay DU trail from snapshot
                // because duCardTrail is rebuilt by legacy from snapshot.doubleUpSession
                // which may not yet be reflected in the global at the point syncFromRuntime runs.
                CabinetState.syncFromRuntime();
                if (snapshot && snapshot.phase === 'DoubleUp' && snapshot.doubleUpSession) {
                    const duSnap = snapshot.doubleUpSession;
                    const trailSeed = duSnap.dealerCard
                        ? [{ card: duSnap.dealerCard, label: 'DEALER' }]
                        : [];
                    CabinetState.updateMachine({
                        duCardTrail: trailSeed,
                        duSwitchesRemaining: Number(duSnap.switchesRemaining || 0),
                        duIsNoLoseActive: Boolean(duSnap.isNoLoseActive)
                    });
                }
                return result;
            };
        });

        _patch('showLobby', function (legacy) {
            return async function patchedShowLobby() {
                const result = await legacy.call(this);
                CabinetState.syncFromRuntime({ screen: 'lobby' });
                return result;
            };
        });

        _patch('showWallet', function (legacy) {
            return function patchedShowWallet() {
                const result = legacy.call(this);
                CabinetState.syncFromRuntime({ screen: 'wallet' });
                return result;
            };
        });

        _patch('showAdmin', function (legacy) {
            return function patchedShowAdmin() {
                const result = legacy.call(this);
                CabinetState.syncFromRuntime({ screen: 'admin' });
                return result;
            };
        });

        _patch('openGame', function (legacy) {
            return function patchedOpenGame(gameId, selectedMachineId, options) {
                const result = legacy.call(this, gameId, selectedMachineId, options);
                CabinetState.syncFromRuntime({ screen: 'game', machineId: selectedMachineId || machineId || 0 });
                return result;
            };
        });

        CabinetState.subscribe(function (snapshot) {
            // Pass the already-computed snapshot to avoid re-entrancy:
            // syncFromRuntime -> updateMachine -> _emit -> here -> syncFromRuntime again
            _applyButtonStatesFromSelectors(snapshot);

            const bonus = document.getElementById('bonus-text');
            if (bonus && snapshot.machine.message && snapshot.machine.gameState === 'doubleup') {
                bonus.dataset.dispatchState = snapshot.machine.gameState;
            }

            const creditsSpan = document.querySelector('#credits span');
            if (creditsSpan) _safeText(creditsSpan, _fmt(snapshot.machine.balance));
            const stakeSpan = document.querySelector('#stake-display span');
            if (stakeSpan) _safeText(stakeSpan, _fmt(snapshot.machine.currentBet));
        });
    }

    return {
        install,
        originals
    };
})();

// Unified feel and performance verification harness for Phase 7
window.CabinetVerification = (function () {
    const logs = [];
    const stats = {
        deals: [],
        draws: [],
        buttonTaps: {},
        errors: []
    };

    function log(type, message, details = {}) {
        const entry = {
            tick: window.CabinetClock ? window.CabinetClock.getTickCount() : 0,
            timestamp: performance.now(),
            type,
            message,
            details
        };
        logs.push(entry);
        console.log(`[CabinetVerification] [Tick ${entry.tick}] [${type}] ${message}`, details);
        if (type === 'ERROR') {
            stats.errors.push(entry);
        }
    }

    let observer = null;
    let slotStartTicks = {};
    let slotEndTicks = {};

    function startObserving() {
        const cardArea = document.getElementById('card-area');
        if (!cardArea) {
            window.CabinetClock.delayTicks(30, startObserving);
            return;
        }

        observer = new MutationObserver((mutations) => {
            const nowTick = window.CabinetClock.getTickCount();
            mutations.forEach((mutation) => {
                const target = mutation.target;
                const slotIndexAttr = target.getAttribute('data-slot');
                if (slotIndexAttr === null) return;
                const idx = parseInt(slotIndexAttr, 10);

                if (mutation.attributeName === 'style') {
                    const style = target.getAttribute('style') || '';
                    const isStarting = style.includes('translateY(-120%)') || style.includes('opacity: 0') || style.includes('opacity: 0;');
                    const isDone = (style.includes('translateY(0%)') || style.includes('translateY(0)') || !style.includes('translateY')) && style.includes('opacity: 1');

                    if (isStarting) {
                        if (idx === 0) {
                            slotStartTicks = {};
                            slotEndTicks = {};
                        }
                        if (!slotStartTicks[idx]) {
                            slotStartTicks[idx] = nowTick;
                            log('DEAL_SLOT_START', `Slot ${idx} started deal reveal.`);
                        }
                    }

                    if (isDone && slotStartTicks[idx] && !slotEndTicks[idx]) {
                        slotEndTicks[idx] = nowTick;
                        log('DEAL_SLOT_END', `Slot ${idx} completed deal reveal.`);

                        if (Object.keys(slotEndTicks).length === 5) {
                            verifyDealSequence();
                        }
                    }
                }
            });
        });

        observer.observe(cardArea, { attributes: true, subtree: true, attributeFilter: ['style'] });
        log('SYSTEM', 'DOM card-area observer installed.');
    }

    function verifyDealSequence() {
        for (let i = 0; i < 4; i++) {
            const startA = slotStartTicks[i];
            const startB = slotStartTicks[i + 1];
            if (startA && startB && startA > startB) {
                log('ERROR', `Deal sequence order regression! Slot ${i} started at tick ${startA} which is after Slot ${i+1} at tick ${startB}`);
            }
        }

        const cfg = window.CabinetStage?.getConfig?.();
        if (cfg) {
            const expectedTotalTicks = window.CabinetClock.msToTicks(cfg.dealBaseMs + 4 * cfg.dealStaggerMs + cfg.dealDurationMs);
            const totalDurationTicks = slotEndTicks[4] - slotStartTicks[0];

            log('METRIC', `Deal Duration: ${totalDurationTicks} ticks (Expected approx ${expectedTotalTicks} ticks).`);
            
            const tolerance = 6; 
            if (Math.abs(totalDurationTicks - expectedTotalTicks) > tolerance) {
                log('ERROR', `Deal duration mismatch! Took ${totalDurationTicks} ticks, expected ${expectedTotalTicks} ticks (tolerance +/- ${tolerance}).`);
            }
        }
    }

    function verifyButtonDebounce(buttonId) {
        const nowTick = window.CabinetClock.getTickCount();
        const lastPress = stats.buttonTaps[buttonId];
        stats.buttonTaps[buttonId] = nowTick;

        if (lastPress) {
            const diff = nowTick - lastPress;
            if (diff < 4) {
                log('ERROR', `Input Debounce regression! Button ${buttonId} pressed at tick ${nowTick}, only ${diff} ticks after previous press.`);
            } else {
                log('INPUT_OK', `Button ${buttonId} pressed debounced with ${diff} ticks separation.`);
            }
        } else {
            log('INPUT_OK', `Button ${buttonId} first press registered.`);
        }
    }

    function installInputVerifier() {
        if (!window.CabinetInput || !window.CabinetInput.trigger) {
            window.CabinetClock.delayTicks(10, installInputVerifier);
            return;
        }

        const originalTrigger = window.CabinetInput.trigger;
        window.CabinetInput.trigger = function (buttonId, actionFn) {
            verifyButtonDebounce(buttonId);
            return originalTrigger.call(this, buttonId, actionFn);
        };
        log('SYSTEM', 'CabinetInput verifier patches installed.');
    }

    function monitorStateTransitions() {
        if (!window.CabinetState) {
            window.CabinetClock.delayTicks(10, monitorStateTransitions);
            return;
        }

        let lastGameState = null;
        window.CabinetState.subscribe((snapshot) => {
            const state = snapshot.machine?.gameState || null;
            const locked = snapshot.presentation?.locked || false;
            if (state !== lastGameState) {
                log('STATE_TRANSITION', `Game state transitioned from ${lastGameState} to ${state}. Presentation locked: ${locked}`);
                
                if ((state === 'deal' || state === 'draw' || state === 'doubleup') && !locked) {
                    log('ERROR', `State transition assertion failed! State is ${state} but presentation is not locked!`);
                }
                
                lastGameState = state;
            }
        });
        log('SYSTEM', 'State transition verifier installed.');
    }

    function getReport() {
        return {
            totalLogs: logs.length,
            errors: stats.errors,
            buttonTaps: stats.buttonTaps,
            status: stats.errors.length === 0 ? 'PASS' : 'FAIL'
        };
    }

    return {
        init: function () {
            startObserving();
            installInputVerifier();
            monitorStateTransitions();
        },
        getReport,
        getLogs: () => logs
    };
})();

document.addEventListener('DOMContentLoaded', function () {
    if (window.CabinetOrchestrator) {
        window.CabinetOrchestrator.install();
    }
    if (window.CabinetVerification) {
        window.CabinetVerification.init();
    }
});
