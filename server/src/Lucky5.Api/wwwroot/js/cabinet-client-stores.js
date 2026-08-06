/*
 * cabinet-client-stores.js
 * Lightweight client-side state stores for auth and game state.
 * Provides subscribe/getState/setState pattern for reactive UI updates.
 * Mirrors global variables for backward compatibility during migration.
 */
(function attachClientStores(global) {
    'use strict';

    function _safeNumber(value, fallback) {
        const num = Number(value);
        return Number.isFinite(num) ? num : fallback;
    }

    function _clone(value) {
        if (value === null || typeof value !== 'object') return value;
        if (Array.isArray(value)) return value.map(_clone);
        const cloned = {};
        for (const key of Object.keys(value)) {
            cloned[key] = _clone(value[key]);
        }
        return cloned;
    }

    function createStore(initialState) {
        let state = _clone(initialState);
        const listeners = new Set();
        let version = 0;

        function notify(prev) {
            const snapshot = getState();
            listeners.forEach((listener) => {
                try { listener(snapshot, prev); } catch (_) {}
            });
        }

        function getState() {
            return _clone(state);
        }

        function subscribe(listener) {
            if (typeof listener !== 'function') return function noop() {};
            listeners.add(listener);
            return function unsubscribe() { listeners.delete(listener); };
        }

        function setState(partial) {
            const prev = getState();
            if (typeof partial === 'function') {
                state = { ...state, ...partial(state) };
            } else {
                state = { ...state, ...partial };
            }
            version++;
            notify(prev);
            return getState();
        }

        function getVersion() {
            return version;
        }

        function reset(next) {
            const prev = getState();
            state = _clone(next || initialState);
            version++;
            notify(prev);
            return getState();
        }

        return { getState, subscribe, setState, getVersion, reset };
    }

    const authStore = createStore({
        user: { username: '', role: 'player' },
        token: null,
        status: 'idle',
        balance: 0,
        walletBalance: 0,
        stateVersion: 0
    });

    function setAuthUser(user) {
        return authStore.setState({ user: _clone(user) || { username: '', role: 'player' } });
    }

    function setAuthToken(token) {
        return authStore.setState({ token });
    }

    function setAuthStatus(status) {
        return authStore.setState({ status: status || 'idle' });
    }

    function setAuthBalance(balance) {
        return authStore.setState({ balance: _safeNumber(balance, 0) });
    }

    function setAuthWalletBalance(walletBalance) {
        return authStore.setState({ walletBalance: _safeNumber(walletBalance, 0) });
    }

    function resetAuth() {
        return authStore.reset({
            user: { username: '', role: 'player' },
            token: null,
            status: 'idle',
            balance: 0,
            walletBalance: 0,
            stateVersion: 0
        });
    }

    function initAuthFromStorage() {
        const token = typeof sessionStorage !== 'undefined'
            ? sessionStorage.getItem('lucky5_token') || null
            : null;
        const username = typeof sessionStorage !== 'undefined'
            ? sessionStorage.getItem('lucky5_username') || ''
            : '';
        const role = typeof sessionStorage !== 'undefined'
            ? String(sessionStorage.getItem('lucky5_role') || 'player').trim().toLowerCase()
            : 'player';
        authStore.setState({
            user: { username, role },
            token,
            status: token ? 'authenticated' : 'idle',
            balance: 0,
            walletBalance: 0
        }, true);
        return authStore.getState();
    }

    const gameStore = createStore({
        machineId: 0,
        phase: 'idle',
        cards: [],
        holds: [],
        bet: 0,
        winMeter: 0,
        doubleUpState: {
            dealerCard: null,
            trail: [],
            switchesRemaining: 0,
            isNoLoseActive: false,
            luckyMultiplier: 1,
            started: false
        },
        stateVersion: 0,
        sequenceNumber: 0,
        pendingCommands: {},
        pendingTimestamp: 0
    });

    function setGameMachineId(machineId) {
        return gameStore.setState({ machineId: _safeNumber(machineId, 0) });
    }

    function setGamePhase(phase) {
        return gameStore.setState({ phase: phase || 'idle' });
    }

    function setGameCards(cards) {
        return gameStore.setState({ cards: Array.isArray(cards) ? cards.map(c => c ? _clone(c) : c) : [] });
    }

    function setGameHolds(holds) {
        return gameStore.setState({ holds: Array.isArray(holds) ? [...holds].sort((a, b) => a - b) : [] });
    }

    function setGameBet(bet) {
        return gameStore.setState({ bet: _safeNumber(bet, 0) });
    }

    function setGameWinMeter(winMeter) {
        return gameStore.setState({ winMeter: _safeNumber(winMeter, 0) });
    }

    function setGameDoubleUpState(partial) {
        const current = gameStore.getState().doubleUpState;
        const next = typeof partial === 'function' ? partial(current) : { ...current, ...partial };
        return gameStore.setState({ doubleUpState: next });
    }

    function setGameVersion(stateVersion, sequenceNumber) {
        return gameStore.setState({
            stateVersion: _safeNumber(stateVersion, 0),
            sequenceNumber: _safeNumber(sequenceNumber, 0)
        });
    }

    function _nextLocalSeq() {
        return (gameStore.getState().sequenceNumber || 0) + 1;
    }

    function _arraysEqual(a, b) {
        if (!Array.isArray(a) || !Array.isArray(b)) return false;
        if (a.length !== b.length) return false;
        for (let i = 0; i < a.length; i++) {
            if (a[i] !== b[i]) return false;
        }
        return true;
    }

    function holdCard(index) {
        if (typeof index !== 'number' || index < 0 || index > 4) return null;
        const state = gameStore.getState();
        if (state.phase !== 'hold') return null;

        const currentHolds = [...state.holds];
        const isHeld = currentHolds.includes(index);
        const newHolds = isHeld
            ? currentHolds.filter(i => i !== index)
            : [...currentHolds, index].sort((a, b) => a - b);

        if (_arraysEqual(currentHolds, newHolds)) return null;

        const localSeq = _nextLocalSeq();
        const timestamp = Date.now();
        const pending = { ...state.pendingCommands };
        pending[localSeq] = { type: 'hold', index, oldValue: currentHolds, value: newHolds, timestamp };

        gameStore.setState({ holds: newHolds, pendingCommands: pending, pendingTimestamp: timestamp });

        return { localSeq, newHolds, oldHolds: currentHolds };
    }

    function setBet(amount) {
        if (typeof amount !== 'number' || amount < 0) return null;
        const state = gameStore.getState();
        if (state.phase !== 'idle') return null;

        const currentBet = state.bet;
        if (currentBet === amount) return null;

        const localSeq = _nextLocalSeq();
        const timestamp = Date.now();
        const pending = { ...state.pendingCommands };
        pending[localSeq] = { type: 'bet', oldValue: currentBet, value: amount, timestamp };

        gameStore.setState({ bet: amount, pendingCommands: pending, pendingTimestamp: timestamp });

        return { localSeq, newBet: amount, oldBet: currentBet };
    }

    function rollbackPending(localSeq) {
        const state = gameStore.getState();
        const pending = state.pendingCommands[localSeq];
        if (!pending) return;

        const newPending = { ...state.pendingCommands };
        delete newPending[localSeq];

        if (pending.type === 'hold') {
            gameStore.setState({ holds: pending.oldValue, pendingCommands: newPending });
        } else if (pending.type === 'bet') {
            gameStore.setState({ bet: pending.oldValue, pendingCommands: newPending });
        }
    }

    function reconcileBet(stateVersion, sequenceNumber, serverBet) {
        const state = gameStore.getState();
        const pending = state.pendingCommands;
        const pendingBets = Object.entries(pending).filter(([, cmd]) => cmd.type === 'bet');

        if (pendingBets.length === 0) {
            if (state.bet !== serverBet) {
                gameStore.setState({ bet: serverBet, stateVersion, sequenceNumber });
            }
            return;
        }

        const matched = pendingBets.find(([, cmd]) => cmd.value === serverBet);
        if (matched) {
            const newPending = { ...pending };
            delete newPending[matched[0]];
            gameStore.setState({ bet: serverBet, stateVersion, sequenceNumber, pendingCommands: newPending });
        } else {
            gameStore.setState({ bet: serverBet, stateVersion, sequenceNumber, pendingCommands: {} });
        }
    }

    function reconcileHolds(stateVersion, sequenceNumber, serverHolds) {
        const state = gameStore.getState();
        const pending = state.pendingCommands;
        const pendingHolds = Object.entries(pending).filter(([, cmd]) => cmd.type === 'hold');

        if (pendingHolds.length === 0) {
            if (!_arraysEqual(state.holds, serverHolds)) {
                gameStore.setState({ holds: serverHolds, stateVersion, sequenceNumber });
            }
            return;
        }

        const matched = pendingHolds.find(([, cmd]) => _arraysEqual(cmd.value, serverHolds));
        if (matched) {
            const newPending = { ...pending };
            delete newPending[matched[0]];
            gameStore.setState({ holds: serverHolds, stateVersion, sequenceNumber, pendingCommands: newPending });
        } else {
            gameStore.setState({ holds: serverHolds, stateVersion, sequenceNumber, pendingCommands: {} });
        }
    }

    function getPendingCount() {
        return Object.keys(gameStore.getState().pendingCommands).length;
    }

    function clearPendingCommands() {
        gameStore.setState({ pendingCommands: {}, pendingTimestamp: 0 });
    }

    function resetGame(keepMachineId) {
        const current = gameStore.getState();
        return gameStore.reset({
            machineId: keepMachineId ? current.machineId : 0,
            phase: 'idle',
            cards: [],
            holds: [],
            bet: 0,
            winMeter: 0,
            doubleUpState: {
                dealerCard: null,
                trail: [],
                switchesRemaining: 0,
                isNoLoseActive: false,
                luckyMultiplier: 1,
                started: false
            },
            stateVersion: 0,
            sequenceNumber: 0,
            pendingCommands: {},
            pendingTimestamp: 0
        });
    }

    function initGameFromGlobals() {
        const w = typeof window !== 'undefined' ? window : {};
        gameStore.setState({
            machineId: _safeNumber(w.machineId, 0),
            phase: w.gameState || 'idle',
            cards: Array.isArray(w.cards) ? w.cards.map(c => c ? _clone(c) : c) : [],
            holds: w.holdIndexes ? Array.from(w.holdIndexes) : [],
            bet: _safeNumber(w.currentBet, 0),
            winMeter: _safeNumber(w.winAmount, 0),
            doubleUpState: {
                dealerCard: w.duDealerCard ? _clone(w.duDealerCard) : null,
                trail: Array.isArray(w.duCardTrail) ? w.duCardTrail.map(e => e ? _clone(e) : e) : [],
                switchesRemaining: _safeNumber(w.duSwitchesRemaining, 0),
                isNoLoseActive: Boolean(w.duIsNoLoseActive),
                luckyMultiplier: _safeNumber(w.duLuckyMultiplier, 1),
                started: Boolean(w.duSessionStarted)
            },
            stateVersion: _safeNumber(w.clientStateVersion, 0),
            sequenceNumber: _safeNumber(w.clientSequenceNumber, 0),
            pendingCommands: {},
            pendingTimestamp: 0
        }, true);
        return gameStore.getState();
    }

    function syncGameFromSnapshot(snapshot) {
        if (!snapshot) return;
        const updates = {};
        if (snapshot.machineId !== undefined) updates.machineId = _safeNumber(snapshot.machineId, 0);
        if (snapshot.phase) updates.phase = snapshot.phase;
        if (snapshot.cards) updates.cards = Array.isArray(snapshot.cards) ? snapshot.cards.map(c => c ? _clone(c) : c) : [];
        if (snapshot.holds) updates.holds = Array.isArray(snapshot.holds) ? [...snapshot.holds].sort((a, b) => a - b) : [];
        if (snapshot.bet !== undefined) updates.bet = _safeNumber(snapshot.bet, 0);
        if (snapshot.winMeter !== undefined) updates.winMeter = _safeNumber(snapshot.winMeter, 0);
        if (snapshot.doubleUpState) {
            const currentDu = gameStore.getState().doubleUpState;
            updates.doubleUpState = typeof snapshot.doubleUpState === 'function'
                ? snapshot.doubleUpState(currentDu)
                : { ...currentDu, ...snapshot.doubleUpState };
        }
        if (snapshot.stateVersion !== undefined || snapshot.sequenceNumber !== undefined) {
            updates.stateVersion = _safeNumber(snapshot.stateVersion, gameStore.getState().stateVersion);
            updates.sequenceNumber = _safeNumber(snapshot.sequenceNumber, gameStore.getState().sequenceNumber);
        }
        if (snapshot.pendingCommands !== undefined) {
            updates.pendingCommands = snapshot.pendingCommands;
        }
        if (snapshot.pendingTimestamp !== undefined) {
            updates.pendingTimestamp = snapshot.pendingTimestamp;
        }
        if (Object.keys(updates).length > 0) {
            gameStore.setState(updates);
        }
    }

    global.CabinetClientStores = Object.freeze({
        auth: authStore,
        game: gameStore,
        setAuthUser,
        setAuthToken,
        setAuthStatus,
        setAuthBalance,
        setAuthWalletBalance,
        resetAuth,
        initAuthFromStorage,
        setGameMachineId,
        setGamePhase,
        setGameCards,
        setGameHolds,
        setGameBet,
        setGameWinMeter,
        setGameDoubleUpState,
        setGameVersion,
        resetGame,
        initGameFromGlobals,
        syncGameFromSnapshot,
        holdCard,
        setBet,
        rollbackPending,
        reconcileBet,
        reconcileHolds,
        getPendingCount,
        clearPendingCommands
    });
})(window);
