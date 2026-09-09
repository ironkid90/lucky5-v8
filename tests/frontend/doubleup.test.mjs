import assert from 'node:assert/strict';
import { test } from 'node:test';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';

const root = 'server/src/Lucky5.Api/wwwroot/js/';
const game = readFileSync(root + 'game.js', 'utf8');
function declaration(name) {
    const start = game.search(new RegExp(`^(?:async )?function ${name}\\(`, 'm'));
    assert.ok(start >= 0, name);
    const end = game.indexOf('\n}', start) + 2;
    return game.slice(start, end);
}
function runtime() {
    const pending = [];
    const requests = [];
    const image = { src: 'back' };
    const c = {
        console, pending, requests, image,
        gameState: 'doubleup', _actionLock: false, jackpotDrainActive: false,
        duSwitchesRemaining: 2, roundId: 'round-fixture', duDealerCard: { code: '7H' },
        duCardTrail: [], duCallToken: null, shuffleTickHandler: null,
        winAmount: 100, currentHandRank: 'TwoPair', duHighlightHandRank: null,
        duBoardBonusAmount: 0, CARD_BACK_SRC: 'back', T: {},
        GAME_CONFIG: { api: { duSwitch: '/switch', duGuess: '/guess' } },
        document: { querySelector: () => image, getElementById: () => null },
        CabinetClock: { unregisterHandler() {}, cancelNamespace() {}, delayMs: (_, cb) => pending.push(cb) },
        CabinetState: { locked: false, setPresentationLocked(v) { this.locked = v; } },
        cardImagePath: card => `generated:${card.code}`,
        apiCall: async (method, url, body) => {
            requests.push({ method, url, body });
            return { status: 'Win', currentAmount: 200, switchesRemaining: 1,
                dealerCard: { code: '9H' }, challengerCard: { code: '9H' }, cardTrail: [] };
        },
        syncDoubleUpPanelState(result) { c.duSwitchesRemaining = result.switchesRemaining; },
        syncDoubleUpTrailFromServer: trail => trail,
        formatNum: String,
        getFourOfAKindSlotTag() {},
        renderDoubleUpCards(...args) { c.rendered = args; },
        showMessage(text) { c.message = text; }
    };
    for (const name of ['playPress', '_noteAppliedStateVersion', 'updatePaytable', 'setButtonStates',
        'updateCredits', 'updateWinIndicator', 'updateWinAmountDisplay', '_flushDeferredServerSnapshot']) c[name] = () => {};
    c.window = c;
    vm.createContext(c);
    vm.runInContext(['stopShuffle', 'doSwitchDealer', 'doDoubleUp'].map(declaration).join('\n'), c);
    return c;
}

test('stopShuffle uses the existing card resolver without an undefined global', () => {
    const c = runtime();
    c.stopShuffle({ code: '5S' });
    assert.equal(c.image.src, 'generated:5S');
});

for (const guess of ['Big', 'Small']) {
    test(`${guess} reaches the API and releases both locks after the win reveal`, async () => {
        const c = runtime();
        await c.doDoubleUp(guess);
        assert.equal(c.requests.length, 1);
        assert.equal(c.requests[0].body.guess, guess);
        assert.equal(c.requests[0].body.roundId, 'round-fixture');
        assert.equal(c.image.src, 'back');
        assert.equal(c._actionLock, true);
        assert.equal(c.CabinetState.locked, true);
        while (c.pending.length) await c.pending.shift()();
        assert.equal(c.gameState, 'doubleup');
        assert.equal(c._actionLock, false);
        assert.equal(c.CabinetState.locked, false);
        assert.equal(c.duCallToken, null);
    });
}

test('switch serializes requests and excludes guesses until its response finishes', async () => {
    const c = runtime();
    let resolve;
    c.apiCall = (method, url, body) => {
        c.requests.push({ method, url, body });
        return new Promise(r => { resolve = r; });
    };
    const first = c.doSwitchDealer();
    void c.doSwitchDealer();
    void c.doDoubleUp('Small');
    assert.equal(c.requests.length, 1);
    assert.equal(c.requests[0].url, '/switch');
    assert.equal(c.requests[0].body.roundId, 'round-fixture');
    resolve({ status: 'Switched', currentAmount: 100, switchesRemaining: 1, dealerCard: { code: '8H' }, cardTrail: [] });
    await first;
    assert.equal(c._actionLock, false);
    assert.equal(c.CabinetState.locked, false);
    assert.equal(c.duSwitchesRemaining, 1);
});

test('a rejected switch releases locks and permits another switch', async () => {
    const c = runtime();
    c.apiCall = async () => { throw new Error('switch rejected'); };
    await c.doSwitchDealer();
    assert.equal(c.message, 'switch rejected');
    assert.equal(c._actionLock, false);
    assert.equal(c.CabinetState.locked, false);
    assert.equal(c.gameState, 'doubleup');
});

test('orchestrator forwards DU reveal options and does not recurse through button sync', () => {
    const c = { console, calls: 0, window: null, document: { addEventListener() {}, getElementById: () => null, querySelector: () => null },
        addEventListener() {}, innerWidth: 720, innerHeight: 1280,
        CabinetAudio: {}, CabinetTransition: {},
        setButtonStates() { c.calls++; }, renderDoubleUpCards(...args) { c.args = args; }
    };
    c.window = c;
    c.CabinetState = { DEFAULT_LAYOUT: { width: 720, height: 1280 },
        updateLayout() {}, updatePresentation() {}, subscribe(fn) { c.subscriber = fn; },
        updateMachine() {}, syncFromRuntime() { const s = { machine: {}, presentation: {} }; c.subscriber?.(s); return s; } };
    vm.createContext(c);
    vm.runInContext(readFileSync(root + 'cabinet-orchestrator-vnext.js', 'utf8'), c);
    c.CabinetOrchestrator.install();
    c.setButtonStates();
    assert.equal(c.calls, 1);
    const options = { challengerLabel: 'BIG', outcome: 'Win' };
    c.renderDoubleUpCards({}, false, {}, options);
    assert.equal(c.args[3], options);
});
