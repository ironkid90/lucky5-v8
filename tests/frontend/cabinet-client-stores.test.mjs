import assert from 'node:assert/strict';
import { test } from 'node:test';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';

function loadClientStores() {
    const win = { console, sessionStorage: {} };
    const sandbox = { window: win, console, sessionStorage: win.sessionStorage };
    vm.runInNewContext(
        readFileSync('server/src/Lucky5.Api/wwwroot/js/cabinet-client-stores.js', 'utf8'),
        sandbox
    );
    return win.CabinetClientStores;
}

function jsonEqual(a, b) {
    return JSON.stringify(a) === JSON.stringify(b);
}

test('authStore initializes with idle status and empty user', () => {
    const Stores = loadClientStores();
    const state = Stores.auth.getState();
    assert.equal(state.status, 'idle');
    assert.equal(state.user.username, '');
    assert.equal(state.user.role, 'player');
    assert.equal(state.token, null);
    assert.equal(state.balance, 0);
    assert.equal(state.walletBalance, 0);
});

test('initAuthFromStorage reads from sessionStorage', () => {
    const win = { console, sessionStorage: {
        getItem: (key) => {
            if (key === 'lucky5_token') return 'tok-123';
            if (key === 'lucky5_username') return 'player1';
            if (key === 'lucky5_role') return 'admin';
            return null;
        }
    }};
    const sandbox = { window: win, console, sessionStorage: win.sessionStorage };
    vm.runInNewContext(
        readFileSync('server/src/Lucky5.Api/wwwroot/js/cabinet-client-stores.js', 'utf8'),
        sandbox
    );
    const Stores = win.CabinetClientStores;
    const state = Stores.initAuthFromStorage();
    assert.equal(state.user.username, 'player1');
    assert.equal(state.user.role, 'admin');
    assert.equal(state.token, 'tok-123');
    assert.equal(state.status, 'authenticated');
});

test('initAuthFromStorage returns idle when no token', () => {
    const win = { console, sessionStorage: { getItem: () => null } };
    const sandbox = { window: win, console, sessionStorage: win.sessionStorage };
    vm.runInNewContext(
        readFileSync('server/src/Lucky5.Api/wwwroot/js/cabinet-client-stores.js', 'utf8'),
        sandbox
    );
    const Stores = win.CabinetClientStores;
    const state = Stores.initAuthFromStorage();
    assert.equal(state.status, 'idle');
    assert.equal(state.token, null);
});

test('authStore setters update state', () => {
    const Stores = loadClientStores();
    Stores.setAuthUser({ username: 'alice', role: 'player' });
    Stores.setAuthToken('tok-456');
    Stores.setAuthStatus('authenticated');
    Stores.setAuthBalance(50000);
    Stores.setAuthWalletBalance(25000);

    const state = Stores.auth.getState();
    assert.equal(state.user.username, 'alice');
    assert.equal(state.token, 'tok-456');
    assert.equal(state.status, 'authenticated');
    assert.equal(state.balance, 50000);
    assert.equal(state.walletBalance, 25000);
});

test('authStore reset clears to initial state', () => {
    const Stores = loadClientStores();
    Stores.setAuthUser({ username: 'bob', role: 'admin' });
    Stores.setAuthBalance(99999);
    Stores.resetAuth();

    const state = Stores.auth.getState();
    assert.equal(state.user.username, '');
    assert.equal(state.user.role, 'player');
    assert.equal(state.balance, 0);
    assert.equal(state.status, 'idle');
});

test('gameStore initializes with idle phase', () => {
    const Stores = loadClientStores();
    const state = Stores.game.getState();
    assert.equal(state.phase, 'idle');
    assert.equal(state.machineId, 0);
    assert.ok(jsonEqual(state.cards, []));
    assert.ok(jsonEqual(state.holds, []));
    assert.equal(state.bet, 0);
    assert.equal(state.winMeter, 0);
});

test('gameStore setters update individual fields', () => {
    const Stores = loadClientStores();
    Stores.setGameMachineId(7);
    Stores.setGamePhase('hold');
    Stores.setGameCards([{ code: 'AS', rank: 14, suit: 'S' }]);
    Stores.setGameHolds([0, 2]);
    Stores.setGameBet(5000);
    Stores.setGameWinMeter(10000);
    Stores.setGameVersion(3, 5);

    const state = Stores.game.getState();
    assert.equal(state.machineId, 7);
    assert.equal(state.phase, 'hold');
    assert.ok(state.cards.length === 1 && state.cards[0].code === 'AS');
    assert.ok(jsonEqual(state.holds, [0, 2]));
    assert.equal(state.bet, 5000);
    assert.equal(state.winMeter, 10000);
    assert.equal(state.stateVersion, 3);
    assert.equal(state.sequenceNumber, 5);
});

test('gameStore setGamePhase defaults to idle', () => {
    const Stores = loadClientStores();
    Stores.setGamePhase('');
    assert.equal(Stores.game.getState().phase, 'idle');
});

test('gameStore doubleUpState merge updates fields', () => {
    const Stores = loadClientStores();
    Stores.setGameDoubleUpState({ dealerCard: { code: 'KH', rank: 13, suit: 'H' } });
    Stores.setGameDoubleUpState({ switchesRemaining: 2 });

    const state = Stores.game.getState();
    assert.ok(state.doubleUpState.dealerCard.code === 'KH');
    assert.equal(state.doubleUpState.switchesRemaining, 2);
    assert.equal(state.doubleUpState.luckyMultiplier, 1);
});

test('gameStore reset clears state', () => {
    const Stores = loadClientStores();
    Stores.setGameMachineId(7);
    Stores.setGamePhase('hold');
    Stores.setGameBet(5000);
    Stores.resetGame(false);

    const state = Stores.game.getState();
    assert.equal(state.machineId, 0);
    assert.equal(state.phase, 'idle');
    assert.equal(state.bet, 0);
});

test('gameStore reset keeps machineId when requested', () => {
    const Stores = loadClientStores();
    Stores.setGameMachineId(9);
    Stores.setGamePhase('hold');
    Stores.resetGame(true);

    const state = Stores.game.getState();
    assert.equal(state.machineId, 9);
    assert.equal(state.phase, 'idle');
});

test('gameStore subscribe notifies on state changes', () => {
    const Stores = loadClientStores();
    let calls = 0;
    const unsub = Stores.game.subscribe(() => { calls++; });

    Stores.setGamePhase('hold');
    assert.equal(calls, 1);

    Stores.setGameBet(1000);
    assert.equal(calls, 2);

    unsub();
    Stores.setGamePhase('idle');
    assert.equal(calls, 2);
});

test('gameStore subscribe receives snapshot and prev', () => {
    const Stores = loadClientStores();
    const snapshots = [];
    Stores.game.subscribe((snapshot, prev) => {
        snapshots.push({ prevPhase: prev.phase, nextPhase: snapshot.phase });
    });

    Stores.setGamePhase('hold');
    assert.deepEqual(snapshots[0], { prevPhase: 'idle', nextPhase: 'hold' });
});

test('initGameFromGlobals reads window values', () => {
    const w = {
        machineId: 11,
        gameState: 'doubleup',
        cards: [{ code: 'AH', rank: 14, suit: 'H' }],
        holdIndexes: [1, 3],
        currentBet: 8000,
        winAmount: 20000,
        duDealerCard: { code: '10S', rank: 10, suit: 'S' },
        duCardTrail: [{ code: '5D', rank: 5, suit: 'D' }],
        duSwitchesRemaining: 1,
        duIsNoLoseActive: true,
        duLuckyMultiplier: 2,
        duSessionStarted: true,
        clientStateVersion: 7,
        clientSequenceNumber: 12
    };
    const sandbox = { window: w, console };
    vm.runInNewContext(
        readFileSync('server/src/Lucky5.Api/wwwroot/js/cabinet-client-stores.js', 'utf8'),
        sandbox
    );
    const Stores = w.CabinetClientStores;
    const state = Stores.initGameFromGlobals();
    assert.equal(state.machineId, 11);
    assert.equal(state.phase, 'doubleup');
    assert.ok(state.cards.length === 1 && state.cards[0].code === 'AH');
    assert.ok(jsonEqual(state.holds, [1, 3]));
    assert.equal(state.bet, 8000);
    assert.equal(state.winMeter, 20000);
    assert.equal(state.doubleUpState.dealerCard.code, '10S');
    assert.equal(state.doubleUpState.trail.length, 1);
    assert.equal(state.doubleUpState.switchesRemaining, 1);
    assert.equal(state.doubleUpState.isNoLoseActive, true);
    assert.equal(state.doubleUpState.luckyMultiplier, 2);
    assert.equal(state.doubleUpState.started, true);
    assert.equal(state.stateVersion, 7);
    assert.equal(state.sequenceNumber, 12);
});

test('syncGameFromSnapshot applies partial updates', () => {
    const Stores = loadClientStores();
    Stores.setGameMachineId(1);
    Stores.setGamePhase('idle');
    Stores.setGameBet(0);

    Stores.syncGameFromSnapshot({
        machineId: 3,
        phase: 'hold',
        bet: 4000,
        cards: [{ code: 'KS', rank: 13, suit: 'S' }],
        holds: [0],
        stateVersion: 2,
        sequenceNumber: 4
    });

    const state = Stores.game.getState();
    assert.equal(state.machineId, 3);
    assert.equal(state.phase, 'hold');
    assert.equal(state.bet, 4000);
    assert.ok(state.cards.length === 1 && state.cards[0].code === 'KS');
    assert.ok(jsonEqual(state.holds, [0]));
    assert.equal(state.stateVersion, 2);
    assert.equal(state.sequenceNumber, 4);
});

test('syncGameFromSnapshot ignores null snapshot', () => {
    const Stores = loadClientStores();
    Stores.setGamePhase('hold');
    Stores.syncGameFromSnapshot(null);
    assert.equal(Stores.game.getState().phase, 'hold');
});

test('syncGameFromSnapshot merges doubleUpState', () => {
    const Stores = loadClientStores();
    Stores.setGamePhase('doubleup');
    Stores.setGameDoubleUpState({ dealerCard: null, switchesRemaining: 0 });

    Stores.syncGameFromSnapshot({
        doubleUpState: { dealerCard: { code: 'QD', rank: 12, suit: 'D' }, switchesRemaining: 1 }
    });

    const state = Stores.game.getState();
    assert.equal(state.phase, 'doubleup');
    assert.ok(state.doubleUpState.dealerCard.code === 'QD');
    assert.equal(state.doubleUpState.switchesRemaining, 1);
});

test('syncGameFromSnapshot preserves existing doubleUpState when not provided', () => {
    const Stores = loadClientStores();
    Stores.setGamePhase('hold');
    Stores.setGameDoubleUpState({ switchesRemaining: 3 });

    Stores.syncGameFromSnapshot({ phase: 'hold', bet: 1000 });

    assert.equal(Stores.game.getState().doubleUpState.switchesRemaining, 3);
});

test('getVersion increments on every setState', () => {
    const Stores = loadClientStores();
    assert.equal(Stores.game.getVersion(), 0);
    Stores.setGamePhase('hold');
    assert.equal(Stores.game.getVersion(), 1);
    Stores.setGameBet(5000);
    assert.equal(Stores.game.getVersion(), 2);
    Stores.game.reset();
    assert.equal(Stores.game.getVersion(), 3);
});

test('authStore setState ignores extra arguments', () => {
    const Stores = loadClientStores();
    Stores.auth.setState({ token: 'x' }, true, 'extra');
    assert.equal(Stores.auth.getState().token, 'x');
    assert.equal(Stores.auth.getState().status, 'idle');
});
