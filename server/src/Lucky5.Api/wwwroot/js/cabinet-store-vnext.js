/*
 * cabinet-store-vnext.js
 * Lightweight state store with selectors and dispatch over CabinetState.
 * Phase D deliverable: selectors (S2), reducer dispatch (S3), optimistic updates (S4).
 */
(function attachCabinetStore(global) {
    'use strict';
    const State = global.CabinetState;
    if (!State) {
        console.warn('[CabinetStore] CabinetState not loaded; store layer unavailable.');
        return;
    }

    /* ── Selectors (pure functions from state to derived value) ── */
    const selectors = Object.freeze({
        isAuthorized(state) {
            return state.machine.screen !== 'auth';
        },
        isInRound(state) {
            const gs = state.machine.gameState;
            return gs === 'hold' || gs === 'drawing' || gs === 'doubleup' || gs === 'dealing';
        },
        canDeal(state) {
            return state.machine.gameState === 'idle'
                && state.machine.balance >= state.machine.currentBet;
        },
        canDraw(state) {
            return state.machine.gameState === 'hold';
        },
        canDoubleUp(state) {
            return state.machine.gameState === 'result'
                && state.machine.roundDoubleUpAvailable
                && state.machine.winAmount > 0;
        },
        heldCards(state) {
            return state.machine.cards
                .filter((_, i) => state.machine.holdIndexes.includes(i));
        },
        displayBalance(state) {
            return state.machine.balance + state.machine.walletBalance;
        },
        jackpotTotal(state) {
            const jp = state.machine.jackpots;
            if (!jp) return 0;
            return (jp.mini || 0) + (jp.midi || 0) + (jp.maxi || 0);
        },
        isJackpotArmed(state) {
            return Boolean(state.machine.jackpotRankArmed);
        },
        isPresentationLocked(state) {
            return state.presentation.locked;
        }
    });

    /* ── Dispatch (action objects drive state transitions) ── */
    const validActions = new Set([
        'SET_SCREEN', 'SET_GAME_STATE', 'SET_MESSAGE', 'SET_BALANCE',
        'SET_CARDS', 'SET_HOLDS', 'SET_JACKPOTS', 'SET_DOUBLE_UP_STATE',
        'SET_WIN_AMOUNT', 'SET_ROUND_ID', 'SET_PRESENTATION_LOCKED',
        'APPLY_SERVER_SNAPSHOT'
    ]);

    function dispatch(action) {
        if (!action || typeof action !== 'object' || !validActions.has(action.type)) {
            console.error('[CabinetStore] Invalid dispatch action:', action);
            return;
        }
        switch (action.type) {
            case 'SET_SCREEN':
                State.updateMachine({ screen: action.payload });
                break;
            case 'SET_GAME_STATE':
                State.updateMachine({ gameState: action.payload });
                if (action.payload === 'result' || action.payload === 'idle') {
                    State.updatePresentation({ locked: false });
                }
                break;
            case 'SET_MESSAGE':
                State.updateMachine({ message: action.payload || '', messageType: action.meta || '' });
                break;
            case 'SET_BALANCE':
                State.updateMachine({ balance: action.payload, walletBalance: action.meta || 0 });
                break;
            case 'SET_CARDS':
                State.updateMachine({ cards: action.payload });
                break;
            case 'SET_HOLDS':
                State.updateMachine({ holdIndexes: action.payload || [] });
                break;
            case 'SET_JACKPOTS':
                State.updateMachine({ jackpots: action.payload });
                break;
            case 'SET_DOUBLE_UP_STATE':
                State.updateMachine({
                    duDealerCard: action.payload.dealerCard || null,
                    duCardTrail: action.payload.trail || [],
                    duSwitchesRemaining: action.payload.switchesRemaining ?? 0,
                    duIsNoLoseActive: action.payload.isNoLose ?? false,
                    duLuckyMultiplier: action.payload.luckyMultiplier ?? 1
                });
                break;
            case 'SET_WIN_AMOUNT':
                State.updateMachine({ winAmount: action.payload });
                break;
            case 'SET_ROUND_ID':
                State.updateMachine({ roundId: action.payload });
                break;
            case 'SET_PRESENTATION_LOCKED': {
                const locked = Boolean(action.payload);
                State.updatePresentation({ locked });
                break;
            }
            case 'APPLY_SERVER_SNAPSHOT':
                if (action.payload && typeof action.payload === 'object') {
                    State.updateMachine(action.payload);
                }
                break;
            default:
                break;
        }
    }

    /* ── Optimistic update helper ── */
    function optimistic(action, rollbackAction, serverPromise) {
        dispatch(action);
        return serverPromise.catch((err) => {
            console.warn('[CabinetStore] Optimistic update failed, rolling back:', err);
            if (rollbackAction) dispatch(rollbackAction);
            throw err;
        });
    }

    function createSelector(computeFn) {
        if (typeof computeFn !== 'function') {
            throw new TypeError('[CabinetStore] createSelector requires a function');
        }
        let lastUpdatedAt = -1;
        let lastResult = undefined;
        return function memoizedSelector() {
            const state = State.get();
            // CabinetState's store.machine.lastUpdatedAt changes on every updateMachine call
            const ts = state.machine.lastUpdatedAt || state.presentation.lastUpdatedAt || Date.now();
            if (lastUpdatedAt === ts) return lastResult;
            lastUpdatedAt = ts;
            lastResult = computeFn(state);
            return lastResult;
        };
    }

    global.CabinetStore = Object.freeze({
        selectors,
        dispatch,
        optimistic,
        createSelector,
        getState: State.get.bind(State),
        subscribe: State.subscribe.bind(State)
    });
})(window);