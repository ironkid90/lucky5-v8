'use strict';

window.CabinetState = (function () {
    const DEFAULT_LAYOUT = Object.freeze({
        width: 720,
        height: 1280,
        zones: Object.freeze({
            paytable:   { left: 8,   top: 8,   width: 350, height: 250 },
            counters:   { left: 505, top: 8,   width: 190, height: 185 },
            cards:      { left: 8,   top: 238, width: 705, height: 270 },
            machine:    { left: 8,   top: 505, width: 705, height: 120 },
            controls:   { left: 0,   top: 640, width: 720, height: 640 }
        })
    });

    const store = {
        machine: {
            screen: 'auth',
            machineId: 0,
            roundId: null,
            gameState: 'idle',
            balance: 0,
            walletBalance: 0,
            currentBet: 5000,
            winAmount: 0,
            cards: [],
            holdIndexes: [],
            jackpots: null,
            currentHandRank: null,
            roundDoubleUpAvailable: false,
            takeHalfUsedThisRound: false,
            duDealerCard: null,
            duCardTrail: [],
            duSwitchesRemaining: 0,
            duIsNoLoseActive: false,
            duLuckyMultiplier: 1,
            machineCanCashOut: false,
            machineSessionClosed: false,
            machineCashOutThreshold: 0,
            message: '',
            messageType: ''
        },
        presentation: {
            fps: 60,
            frame: 0,
            locked: false,
            planDepth: 0,
            lastAction: 'BOOT',
            lastTransition: null,
            lucky5Active: false,
            viewportScale: 1,
            lastUpdatedAt: Date.now()
        },
        layout: DEFAULT_LAYOUT
    };

    const listeners = new Set();

    function _safeNumber(value, fallback) {
        const num = Number(value);
        return Number.isFinite(num) ? num : fallback;
    }

    function _uniqueSortedInts(values) {
        return Array.from(
            new Set(
                (values || [])
                    .map((v) => parseInt(v, 10))
                    .filter((n) => !isNaN(n) && Number.isFinite(n))
            )
        ).sort((a, b) => a - b);
    }

    function _cloneState() {
        return {
            machine: {
                ...store.machine,
                cards: Array.isArray(store.machine.cards) ? store.machine.cards.map((c) => c ? { ...c } : c) : [],
                holdIndexes: [...store.machine.holdIndexes],
                jackpots: store.machine.jackpots ? { ...store.machine.jackpots } : null,
                duDealerCard: store.machine.duDealerCard ? { ...store.machine.duDealerCard } : null,
                duCardTrail: Array.isArray(store.machine.duCardTrail)
                    ? store.machine.duCardTrail.map((entry) => entry ? { ...entry, card: entry.card ? { ...entry.card } : entry.card } : entry)
                    : []
            },
            presentation: { ...store.presentation },
            layout: JSON.parse(JSON.stringify(store.layout))
        };
    }

    function _emit() {
        const snapshot = get();
        listeners.forEach((listener) => {
            try {
                listener(snapshot);
            } catch (_) {
                // ignore observer failures
            }
        });
    }

    function get() {
        return _cloneState();
    }

    function subscribe(listener) {
        if (typeof listener !== 'function') return function noop() {};
        listeners.add(listener);
        return function unsubscribe() { listeners.delete(listener); };
    }

    function updateMachine(patch) {
        store.machine = {
            ...store.machine,
            ...patch,
            holdIndexes: patch && patch.holdIndexes ? _uniqueSortedInts(patch.holdIndexes) : store.machine.holdIndexes,
            cards: patch && Array.isArray(patch.cards) ? patch.cards.map((c) => c ? { ...c } : c) : store.machine.cards,
            duCardTrail: patch && Array.isArray(patch.duCardTrail)
                ? patch.duCardTrail.map((entry) => entry ? { ...entry, card: entry.card ? { ...entry.card } : entry.card } : entry)
                : store.machine.duCardTrail,
            lastUpdatedAt: Date.now()
        };
        store.presentation.lastUpdatedAt = Date.now();
        _emit();
        return get();
    }

    function updatePresentation(patch) {
        store.presentation = {
            ...store.presentation,
            ...patch,
            frame: _safeNumber(patch && patch.frame, store.presentation.frame),
            planDepth: _safeNumber(patch && patch.planDepth, store.presentation.planDepth),
            lastUpdatedAt: Date.now()
        };
        _emit();
        return get();
    }

    function updateLayout(nextLayout) {
        const cabinet = nextLayout || {};
        const zones = cabinet.zones || {};
        store.layout = {
            width: _safeNumber(cabinet.width, DEFAULT_LAYOUT.width),
            height: _safeNumber(cabinet.height, DEFAULT_LAYOUT.height),
            zones: {
                ...DEFAULT_LAYOUT.zones,
                ...zones
            }
        };
        _emit();
        return get();
    }

    function syncFromRuntime(overrides) {
        const screenEl = document.getElementById('game-screen');
        const walletEl = document.getElementById('wallet-screen');
        const adminEl = document.getElementById('admin-screen');
        const lobbyEl = document.getElementById('lobby-screen');

        const screenName = screenEl?.classList.contains('active') ? 'game'
            : walletEl?.classList.contains('active') ? 'wallet'
            : adminEl?.classList.contains('active') ? 'admin'
            : lobbyEl?.classList.contains('active') ? 'lobby'
            : 'auth';

        if (typeof debugLog === 'function') {
            debugLog('syncFromRuntime:screen', { 
                screenName, 
                gameActive: screenEl?.classList.contains('active'),
                gameDisplay: screenEl ? window.getComputedStyle(screenEl).display : 'null'
            });
        }

        const runtime = {
            screen: screenName,
            machineId: typeof machineId !== 'undefined' ? _safeNumber(machineId, 0) : 0,
            roundId: typeof roundId !== 'undefined' ? roundId : null,
            gameState: typeof gameState !== 'undefined' ? gameState : 'idle',
            balance: typeof balance !== 'undefined' ? _safeNumber(balance, 0) : 0,
            walletBalance: typeof walletBalance !== 'undefined' ? _safeNumber(walletBalance, 0) : 0,
            currentBet: typeof currentBet !== 'undefined' ? _safeNumber(currentBet, 5000) : 5000,
            winAmount: typeof winAmount !== 'undefined' ? _safeNumber(winAmount, 0) : 0,
            cards: typeof cards !== 'undefined' && Array.isArray(cards) ? cards : [],
            holdIndexes: typeof holdIndexes !== 'undefined' && holdIndexes ? Array.from(holdIndexes) : [],
            jackpots: typeof jackpots !== 'undefined' ? jackpots : null,
            currentHandRank: typeof currentHandRank !== 'undefined' ? currentHandRank : null,
            roundDoubleUpAvailable: typeof roundDoubleUpAvailable !== 'undefined' ? Boolean(roundDoubleUpAvailable) : false,
            takeHalfUsedThisRound: typeof takeHalfUsedThisRound !== 'undefined' ? Boolean(takeHalfUsedThisRound) : false,
            duDealerCard: typeof duDealerCard !== 'undefined' ? duDealerCard : null,
            duCardTrail: typeof duCardTrail !== 'undefined' && Array.isArray(duCardTrail) ? duCardTrail : [],
            duSwitchesRemaining: typeof duSwitchesRemaining !== 'undefined' ? _safeNumber(duSwitchesRemaining, 0) : 0,
            duIsNoLoseActive: typeof duIsNoLoseActive !== 'undefined' ? Boolean(duIsNoLoseActive) : false,
            duLuckyMultiplier: typeof duLuckyMultiplier !== 'undefined' ? _safeNumber(duLuckyMultiplier, 1) : 1,
            machineCanCashOut: typeof machineCanCashOut !== 'undefined' ? Boolean(machineCanCashOut) : false,
            machineSessionClosed: typeof machineSessionClosed !== 'undefined' ? Boolean(machineSessionClosed) : false,
            machineCashOutThreshold: typeof machineCashOutThreshold !== 'undefined' ? _safeNumber(machineCashOutThreshold, 0) : 0,
            message: document.getElementById('game-message')?.textContent || '',
            messageType: document.getElementById('game-message')?.className || ''
        };
        return updateMachine({ ...runtime, ...(overrides || {}) });
    }

    function selectors(snapshot) {
        const state = snapshot || get();
        const machine = state.machine;
        const locked = Boolean(state.presentation.locked);
        const machineClosed = machine.machineSessionClosed || machine.balance >= (window.GAME_CONFIG?.rules?.machineCreditLimit || 40000000);

        return {
            machineClosed,
            canBet: !locked && !machineClosed && (machine.gameState === 'idle' || machine.gameState === 'doubleup'),
            canDeal: !locked && !machineClosed && (machine.gameState === 'idle' || machine.gameState === 'hold'),
            // Only allow holds during 'hold' phase. The jackpot-rank-adjust via hold[0] during
            // idle is handled entirely by game.js's own setButtonStates; vnext does not override it.
            canHold: (index) => !locked && machine.gameState === 'hold',
            canGuess: !locked && (machine.gameState === 'doubleup' || (machine.gameState === 'win' && machine.roundDoubleUpAvailable && machine.winAmount > 0)),
            canSwitch: !locked && machine.gameState === 'doubleup' && machine.duSwitchesRemaining > 0,
            canTakeScore: !locked && (machine.gameState === 'win' || machine.gameState === 'doubleup'),
            canTakeHalf: !locked && (machine.gameState === 'win' || machine.gameState === 'doubleup') && !machine.takeHalfUsedThisRound,
            canCashOut: !locked && machine.machineCanCashOut,
            shouldShowIdleOverlay: machine.gameState === 'idle' && machine.holdIndexes.length === 0 && !machine.duDealerCard
        };
    }

    return {
        DEFAULT_LAYOUT,
        get,
        subscribe,
        updateMachine,
        updatePresentation,
        updateLayout,
        syncFromRuntime,
        selectors
    };
})();
