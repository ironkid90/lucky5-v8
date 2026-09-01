/*
 * cabinet-client-stores.test.js
 * Unit tests for cabinet-client-stores.js
 * Run with: node cabinet-client-stores.test.js
 */

// Mock browser globals for Node.js environment
global.window = {
    sessionStorage: {
        _store: {},
        getItem(key) { return this._store[key] || null; },
        setItem(key, val) { this._store[key] = String(val); },
        removeItem(key) { delete this._store[key]; }
    }
};

// Load the store module
require('./cabinet-client-stores.js');

const { CabinetClientStores } = global.window;
const assert = {
    equal(actual, expected, msg = '') {
        if (actual !== expected) {
            throw new Error(`${msg} Expected ${expected}, got ${actual}`);
        }
    },
    ok(val, msg = '') {
        if (!val) throw new Error(`${msg} Expected truthy, got ${val}`);
    },
    deepEqual(actual, expected, msg = '') {
        if (JSON.stringify(actual) !== JSON.stringify(expected)) {
            throw new Error(`${msg} Expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
        }
    }
};

let passed = 0;
let failed = 0;

function test(name, fn) {
    try {
        fn();
        passed++;
        console.log(`  PASS: ${name}`);
    } catch (e) {
        failed++;
        console.log(`  FAIL: ${name} — ${e.message}`);
    }
}

function suite(name, fn) {
    console.log(`\n${name}`);
    fn();
}

// ── Auth Store Tests ──
suite('Auth Store', () => {
    test('initial state has correct defaults', () => {
        CabinetClientStores.resetAuth();
        const state = CabinetClientStores.auth.getState();
        assert.equal(state.user.username, '');
        assert.equal(state.user.role, 'player');
        assert.equal(state.token, null);
        assert.equal(state.status, 'idle');
        assert.equal(state.balance, 0);
        assert.equal(state.walletBalance, 0);
        assert.equal(state.stateVersion, 0);
    });

    test('setAuthToken updates token only', () => {
        CabinetClientStores.resetAuth();
        CabinetClientStores.setAuthToken('test-token');
        const state = CabinetClientStores.auth.getState();
        assert.equal(state.token, 'test-token');
        assert.equal(state.status, 'idle', 'setAuthToken should not change status');
    });

    test('setAuthUser updates user', () => {
        CabinetClientStores.setAuthUser({ username: 'player1', role: 'admin' });
        const state = CabinetClientStores.auth.getState();
        assert.equal(state.user.username, 'player1');
        assert.equal(state.user.role, 'admin');
    });

    test('setAuthBalance and setAuthWalletBalance update balances', () => {
        CabinetClientStores.setAuthBalance(5000);
        CabinetClientStores.setAuthWalletBalance(10000);
        const state = CabinetClientStores.auth.getState();
        assert.equal(state.balance, 5000);
        assert.equal(state.walletBalance, 10000);
    });

    test('resetAuth clears all state', () => {
        CabinetClientStores.setAuthToken('token');
        CabinetClientStores.setAuthBalance(999);
        CabinetClientStores.resetAuth();
        const state = CabinetClientStores.auth.getState();
        assert.equal(state.token, null);
        assert.equal(state.balance, 0);
        assert.equal(state.status, 'idle');
    });

    test('initAuthFromStorage reads from sessionStorage', () => {
        window.sessionStorage._store = {
            lucky5_token: 'storage-token',
            lucky5_username: 'storage-user',
            lucky5_role: 'agent'
        };
        CabinetClientStores.resetAuth();
        CabinetClientStores.initAuthFromStorage();
        const state = CabinetClientStores.auth.getState();
        assert.equal(state.token, 'storage-token');
        assert.equal(state.user.username, 'storage-user');
        assert.equal(state.user.role, 'agent');
        assert.equal(state.status, 'authenticated');
        delete window.sessionStorage._store;
    });

    test('subscribe notifies on changes', () => {
        CabinetClientStores.resetAuth();
        let notified = false;
        const unsub = CabinetClientStores.auth.subscribe(() => {
            notified = true;
        });
        CabinetClientStores.setAuthToken('sub-token');
        assert.ok(notified, 'Should have notified subscriber');
        unsub();
    });

    test('unsubscribe stops notifications', () => {
        CabinetClientStores.resetAuth();
        let count = 0;
        const unsub = CabinetClientStores.auth.subscribe(() => { count++; });
        CabinetClientStores.setAuthToken('a');
        CabinetClientStores.setAuthToken('b');
        unsub();
        CabinetClientStores.setAuthToken('c');
        assert.equal(count, 2, 'Should only notify twice before unsubscribe');
    });
});

// ── Game Store Tests ──
suite('Game Store', () => {
    test('initial state has correct defaults', () => {
        CabinetClientStores.resetGame(false);
        const state = CabinetClientStores.game.getState();
        assert.equal(state.machineId, 0);
        assert.equal(state.phase, 'idle');
        assert.deepEqual(state.cards, []);
        assert.deepEqual(state.holds, []);
        assert.equal(state.bet, 0);
        assert.equal(state.winMeter, 0);
        assert.equal(state.doubleUpState.dealerCard, null);
        assert.deepEqual(state.doubleUpState.trail, []);
        assert.equal(state.stateVersion, 0);
        assert.equal(state.sequenceNumber, 0);
    });

    test('setGamePhase updates phase', () => {
        CabinetClientStores.setGamePhase('hold');
        assert.equal(CabinetClientStores.game.getState().phase, 'hold');
        CabinetClientStores.setGamePhase(null);
        assert.equal(CabinetClientStores.game.getState().phase, 'idle');
    });

    test('setGameBet clamps to safe number', () => {
        CabinetClientStores.setGameBet('abc');
        assert.equal(CabinetClientStores.game.getState().bet, 0);
        CabinetClientStores.setGameBet(5000);
        assert.equal(CabinetClientStores.game.getState().bet, 5000);
    });

    test('setGameHolds sorts array', () => {
        CabinetClientStores.setGameHolds([3, 1, 2, 1]);
        assert.deepEqual(CabinetClientStores.game.getState().holds, [1, 1, 2, 3]);
    });

    test('setGameDoubleUpState updates nested state', () => {
        CabinetClientStores.setGameDoubleUpState({
            dealerCard: { code: 'AS' },
            switchesRemaining: 2,
            trail: [{ card: { code: 'KH' }, label: 'PLAYED' }]
        });
        const du = CabinetClientStores.game.getState().doubleUpState;
        assert.deepEqual(du.dealerCard, { code: 'AS' });
        assert.equal(du.switchesRemaining, 2);
        assert.equal(du.trail.length, 1);
        assert.equal(du.trail[0].card.code, 'KH');
    });

    test('setGameVersion updates stateVersion and sequenceNumber', () => {
        CabinetClientStores.setGameVersion(5, 10);
        const state = CabinetClientStores.game.getState();
        assert.equal(state.stateVersion, 5);
        assert.equal(state.sequenceNumber, 10);
    });

    test('resetGame clears state but preserves machineId when requested', () => {
        CabinetClientStores.setGameMachineId(42);
        CabinetClientStores.setGamePhase('hold');
        CabinetClientStores.setGameBet(1000);
        CabinetClientStores.resetGame(true);
        const state = CabinetClientStores.game.getState();
        assert.equal(state.machineId, 42);
        assert.equal(state.phase, 'idle');
        assert.equal(state.bet, 0);
        assert.deepEqual(state.holds, []);
    });

    test('resetGame clears machineId when not preserving', () => {
        CabinetClientStores.setGameMachineId(42);
        CabinetClientStores.resetGame(false);
        assert.equal(CabinetClientStores.game.getState().machineId, 0);
    });

    test('initGameFromGlobals reads from window globals', () => {
        global.window.machineId = 7;
        global.window.gameState = 'doubleup';
        global.window.currentBet = 2000;
        global.window.winAmount = 1500;
        global.window.holdIndexes = new Set([0, 2]);
        global.window.cards = [{ code: 'AH' }, { code: 'KH' }];
        global.window.duCardTrail = [{ card: { code: 'QD' }, label: 'PLAYED' }];
        global.window.duSwitchesRemaining = 3;
        global.window.duIsNoLoseActive = true;
        global.window.duLuckyMultiplier = 2;
        global.window.duSessionStarted = true;
        global.window.clientStateVersion = 9;
        global.window.clientSequenceNumber = 20;

        CabinetClientStores.initGameFromGlobals();
        const state = CabinetClientStores.game.getState();
        assert.equal(state.machineId, 7);
        assert.equal(state.phase, 'doubleup');
        assert.equal(state.bet, 2000);
        assert.equal(state.winMeter, 1500);
        assert.deepEqual(state.holds, [0, 2]);
        assert.equal(state.cards.length, 2);
        assert.equal(state.doubleUpState.trail.length, 1);
        assert.equal(state.doubleUpState.isNoLoseActive, true);
        assert.equal(state.doubleUpState.luckyMultiplier, 2);
        assert.equal(state.stateVersion, 9);
        assert.equal(state.sequenceNumber, 20);

        // Cleanup globals
        delete global.window.machineId;
        delete global.window.gameState;
        delete global.window.currentBet;
        delete global.window.winAmount;
        delete global.window.holdIndexes;
        delete global.window.cards;
        delete global.window.duCardTrail;
        delete global.window.duSwitchesRemaining;
        delete global.window.duIsNoLoseActive;
        delete global.window.duLuckyMultiplier;
        delete global.window.duSessionStarted;
        delete global.window.clientStateVersion;
        delete global.window.clientSequenceNumber;
    });

    test('syncGameFromSnapshot updates selected fields', () => {
        CabinetClientStores.resetGame(false);
        CabinetClientStores.syncGameFromSnapshot({
            machineId: 10,
            phase: 'win',
            bet: 500,
            winMeter: 3000,
            cards: [{ code: 'KS' }],
            holds: [1],
            stateVersion: 3,
            sequenceNumber: 7
        });
        const state = CabinetClientStores.game.getState();
        assert.equal(state.machineId, 10);
        assert.equal(state.phase, 'win');
        assert.equal(state.bet, 500);
        assert.equal(state.winMeter, 3000);
        assert.equal(state.cards.length, 1);
        assert.deepEqual(state.holds, [1]);
        assert.equal(state.stateVersion, 3);
        assert.equal(state.sequenceNumber, 7);
    });

    test('getState returns deep clone (mutations do not leak)', () => {
        CabinetClientStores.setGameCards([{ code: 'AH' }]);
        CabinetClientStores.setGameHolds([]);
        const s1 = CabinetClientStores.game.getState();
        s1.cards.push({ code: 'ZZ' });
        s1.holds.push(99);
        const s2 = CabinetClientStores.game.getState();
        assert.equal(s2.cards.length, 1, 'Original cards should not be mutated');
        assert.equal(s2.holds.length, 0, 'Original holds should not be mutated');
    });
});

// ── Integration: trail vs cardTrail ──
suite('Trail Consistency', () => {
    test('setGameDoubleUpState with trail works correctly', () => {
        CabinetClientStores.setGameDoubleUpState({
            trail: [{ card: { code: 'AS' }, label: 'DEALER' }]
        });
        const du = CabinetClientStores.game.getState().doubleUpState;
        assert.equal(du.trail.length, 1);
        assert.equal(du.trail[0].card.code, 'AS');
        assert.equal(du.cardTrail, undefined, 'cardTrail key should not exist');
    });
});

// ── Results ──
console.log(`\nResults: ${passed} passed, ${failed} failed`);
process.exit(failed > 0 ? 1 : 0);
