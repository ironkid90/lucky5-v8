import assert from 'node:assert/strict';
import { test } from 'node:test';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';

function loadStoreModules() {
    const win = { console, window: null };
    win.window = win;
    const sandbox = { window: win, console };
    vm.runInNewContext(
        readFileSync('server/src/Lucky5.Api/wwwroot/js/cabinet-state-vnext.js', 'utf8'),
        sandbox
    );
    vm.runInNewContext(
        readFileSync('server/src/Lucky5.Api/wwwroot/js/cabinet-store-vnext.js', 'utf8'),
        sandbox
    );
    return win.CabinetStore;
}

function jsonEqual(a, b) {
    return JSON.stringify(a) === JSON.stringify(b);
}

test('selectors return correct derived values from state', () => {
    const Store = loadStoreModules();
    Store.dispatch({ type: 'SET_SCREEN', payload: 'lobby' });
    Store.dispatch({ type: 'SET_BALANCE', payload: 50000, meta: 25000 });
    Store.dispatch({ type: 'SET_GAME_STATE', payload: 'idle' });

    const state = Store.getState();
    assert.equal(Store.selectors.isAuthorized(state), true, 'isAuthorized');
    assert.equal(Store.selectors.displayBalance(state), 75000, 'displayBalance');
    assert.equal(state.machine.balance, 50000, 'machine balance');
    assert.equal(Store.selectors.canDraw(state), false, 'canDraw when idle');
    assert.equal(Store.selectors.isInRound(state), false, 'isInRound when idle');
});

test('dispatch updates game state and presentation lock', () => {
    const Store = loadStoreModules();
    Store.dispatch({ type: 'SET_GAME_STATE', payload: 'dealing' });
    assert.equal(Store.getState().machine.gameState, 'dealing');

    Store.dispatch({ type: 'SET_PRESENTATION_LOCKED', payload: true });
    assert.equal(Store.getState().presentation.locked, true);

    Store.dispatch({ type: 'SET_GAME_STATE', payload: 'result' });
    assert.equal(Store.getState().machine.gameState, 'result');
    assert.equal(Store.getState().presentation.locked, false);
});

test('optimistic updates apply immediately, roll back on error', async () => {
    const Store = loadStoreModules();
    Store.dispatch({ type: 'SET_BALANCE', payload: 100000 });
    assert.equal(Store.getState().machine.balance, 100000);

    const err = await Store.optimistic(
        { type: 'SET_BALANCE', payload: 80000 },
        { type: 'SET_BALANCE', payload: 100000 },
        Promise.reject(new Error('server denied'))
    ).catch(e => e);

    assert.equal(err.message, 'server denied');
    assert.equal(Store.getState().machine.balance, 100000);
});

test('createSelector recomputes when store changes, memoizes when unchanged', async () => {
    const Store = loadStoreModules();
    let computeCalls = 0;
    const sel = Store.createSelector((state) => {
        computeCalls++;
        return state.machine.balance || 0;
    });

    // First set
    Store.dispatch({ type: 'SET_BALANCE', payload: 42000 });
    const res1 = sel();
    assert.equal(res1, 42000);
    assert.equal(computeCalls, 1);

    // Same state, should memoize
    const res1b = sel();
    assert.equal(res1b, 42000);
    assert.equal(computeCalls, 1, 'no extra compute on same state');

    // Second set (different value) — ensure distinct timestamp
    await new Promise(r => setTimeout(r, 2));
    Store.dispatch({ type: 'SET_BALANCE', payload: 43000 });
    const res2 = sel();
    assert.equal(res2, 43000, 'selector returns updated value');
    assert.equal(computeCalls, 2, 'one extra compute after dispatch');
});

test('APPLY_SERVER_SNAPSHOT hydrates all machine fields', () => {
    const Store = loadStoreModules();
    Store.dispatch({
        type: 'APPLY_SERVER_SNAPSHOT',
        payload: {
            screen: 'game',
            gameState: 'hold',
            balance: 250000,
            currentBet: 5000,
            cards: [{ code: 'AS', rank: 14, suit: 'S' }],
            holdIndexes: [0],
            roundId: 'round-42'
        }
    });
    const state = Store.getState();
    assert.equal(state.machine.screen, 'game');
    assert.equal(state.machine.gameState, 'hold');
    assert.equal(state.machine.balance, 250000);
    assert.equal(state.machine.roundId, 'round-42');
    assert.ok(jsonEqual(state.machine.cards, [{ code: 'AS', rank: 14, suit: 'S' }]));
    assert.ok(jsonEqual(state.machine.holdIndexes, [0]));
});