import assert from 'node:assert/strict';
import { test } from 'node:test';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';
import { webcrypto } from 'node:crypto';

const source = readFileSync('server/src/Lucky5.Api/wwwroot/js/game.js', 'utf8');
function declaration(name) {
    const start = source.search(new RegExp(`^(?:async )?function ${name}\\(`, 'm'));
    assert.ok(start >= 0, name);
    return source.slice(start, source.indexOf('\n}', start) + 2);
}
function snapshot(version, reserved = 100, phase = 'idle') {
    return { state_version: version, sequence_number: version, game_state: phase,
        credits: { machine_credits: 200 - reserved, reserved_stake: reserved, reservation_id: reserved ? 'reservation-1' : null,
            reservation_expires_utc: reserved ? '2030-01-01T00:00:00Z' : null, stake: 100 },
        session: {}, hand: { round_id: phase === 'hold' ? 'round-1' : null, cards: ['AS', 'KH', 'QC', 'JD', '9S'] } };
}
function runtime() {
    const storage = new Map();
    const c = { console, crypto: webcrypto, Date, setTimeout: cb => cb(),
        sessionStorage: { getItem: k => storage.get(k), setItem: (k,v) => storage.set(k,v), removeItem: k => storage.delete(k) },
        currentUsername: 'player', machineId: 1, clientStateVersion: 0, clientSequenceNumber: 0, _lastAppliedStateVersion: 0,
        _pendingCabinetCommand: null, _cabinetCommandBusy: false, _deferredCabinetSnapshot: null,
        reservedStake: 0, reservationId: null, reservationExpiresUtc: null, currentBet: 0,
        balance: 200, walletBalance: 0, gameState: 'idle', currentHandRank: null,
        jackpotRankArmed: false, betRampRunning: false, betResetPending: false, _actionLock: false, jackpotDrainActive: false,
        machines: [{ id: 1, minBet: 100, maxBet: 200, betIncrement: 100 }], GAME_RULES: { betStep: 100 }, T: {},
        machineJoined: true, holdIndexes: new Set(), duDealerCard: null,
        $$: () => [], requests: [], _isPresentationBusy: () => false,
        normalizeApiPayload: v => v,
        normalizeCabinetJackpots: () => null,
        parseCabinetNumber: (v, fallback = 0) => v == null ? fallback : Number(v),
        readCabinetField: (obj, ...keys) => keys.map(k => obj?.[k]).find(v => v !== undefined),
        showMessage: text => { c.message = text; },
        syncMachineCreditsFromResponse: v => { if (v.machineCredits !== undefined) c.balance = Number(v.machineCredits); },
        buildRoundSnapshotFromCabinetSnapshot: s => s?.game_state === 'hold' ? { roundId: s.hand.round_id, cards: s.hand.cards } : null,
        restoreRoundFromSnapshot: r => { c.roundId = r.roundId; c.gameState = 'hold'; },
        refreshIdleMachineState: () => { c.gameState = 'idle'; },
        renderDealStage: (_cards, done) => done(),
        apiCall: async (_method, path, command) => { c.requests.push({ path, command }); return { accepted: true, snapshot: snapshot(c.clientStateVersion + 1, command.command_type === 'reserve_stake' ? command.payload.bet_amount : 0, command.command_type === 'deal' ? 'hold' : 'idle') }; },
        fetchCabinetSnapshot: async () => c.latest || snapshot(c.clientStateVersion, c.reservedStake)
    };
    for (const n of ['setButtonStates', 'updateStakeDisplay', 'updatePaytable', 'updateLobbyBalance', 'syncMachineSessionState',
        'playPress', 'updateBonusHandText', '_flushDeferredServerSnapshot', 'updateBonusBar', 'updateIdleOverlayVisibility',
        'updateWinIndicator', 'hideDuInfo', 'hideIdleTitle', 'resetDoubleUpPanelState', 'updateWinAmountDisplay', 'applyAutoHold', 'showIdleTitle']) c[n] = () => {};
    c.window = c;
    vm.createContext(c);
    vm.runInContext(['syncStakeReservation', 'pendingCommandStorageKey', 'savePendingCabinetCommand', 'submitCabinetCommand',
        'recoverPendingCabinetCommand', 'applyCabinetSnapshot', 'receiveCabinetSnapshot', 'fetchAndRestoreFromSnapshot',
        'cancelStakeReservation', 'doBet', 'doDeal', 'leaveMachine'].map(declaration).join('\n'), c);
    return c;
}
test('BET reserves server credits before DEAL and mirrors the server reservation', async () => {
    const c = runtime();
    await c.doBet();
    assert.equal(c.requests[0].command.command_type, 'reserve_stake');
    assert.equal(c.requests[0].command.payload.bet_amount, 100);
    assert.equal(c.requests[0].command.schema_version, 'cabinet.v1');
    assert.equal(c.balance, 100);
    assert.equal(c.currentBet, 100);
    assert.equal(c.reservationId, 'reservation-1');
    c.balance = 0; // Reserved money does not need to exist in available credits again.
    await c.doDeal();
    assert.equal(c.requests[1].command.command_type, 'deal');
    assert.equal(c.requests[1].command.payload.reservation_id, 'reservation-1');
    assert.equal(c.requests[1].command.payload.bet_amount, 100);
    assert.equal(c.gameState, 'hold');
    assert.equal(c.reservationId, null);
});
test('ambiguous reserve/deal retries retain the complete identity across reload', async () => {
    for (const kind of ['reserve_stake', 'deal']) {
        const c = runtime();
        let original;
        c.apiCall = async (_m,_p,command) => { original = JSON.stringify(command); throw new Error('lost response'); };
        await assert.rejects(c.submitCabinetCommand(kind, { bet_amount: 100, reservation_id: 'r' }));
        assert.ok(c._pendingCabinetCommand);
        c._pendingCabinetCommand = null; // simulate reload, keeping sessionStorage
        c.clientStateVersion = 42;
        c.apiCall = async (_m,_p,command) => {
            assert.equal(JSON.stringify(command), original);
            return { accepted: true, snapshot: snapshot(43, kind === 'deal' ? 0 : 100, kind === 'deal' ? 'hold' : 'idle') };
        };
        await c.recoverPendingCabinetCommand();
        assert.equal(c._pendingCabinetCommand, null);
    }
});
test('cancel refunds only after the authoritative response', async () => {
    const c = runtime();
    await c.doBet();
    await c.cancelStakeReservation();
    assert.equal(c.requests[1].command.command_type, 'cancel_stake');
    assert.equal(c.requests[1].command.payload.reservation_id, 'reservation-1');
    assert.equal(c.balance, 200);
    assert.equal(c.reservationId, null);
    assert.equal(c.currentBet, 0);
});
test('reconnect restores reservation, ignores stale snapshots, and applies expiry', async () => {
    const c = runtime();
    c.latest = snapshot(5);
    await c.fetchAndRestoreFromSnapshot();
    assert.equal(c.reservationId, 'reservation-1');
    assert.equal(c.currentBet, 100);
    assert.equal(c.receiveCabinetSnapshot(snapshot(4, 0)), false);
    assert.equal(c.balance, 100);
    assert.equal(c.clientStateVersion, 5);
    c.receiveCabinetSnapshot(snapshot(6, 0));
    assert.equal(c.reservationId, null);
    assert.equal(c.balance, 200);
});
test('lost DEAL is recovered before cancellation and cannot refund a consumed stake', async () => {
    const c = runtime();
    await c.doBet();
    c.apiCall = async () => { throw new Error('lost deal response'); };
    await c.doDeal();
    assert.equal(c._pendingCabinetCommand.command_type, 'deal');
    c.apiCall = async (_m,_p,command) => {
        assert.equal(command.command_type, 'deal');
        return { accepted: true, snapshot: snapshot(2, 0, 'hold') };
    };
    c.latest = snapshot(2, 0, 'hold');
    await c.cancelStakeReservation();
    assert.equal(c.gameState, 'hold');
    assert.equal(c.reservationId, null);
});
test('leave releases reservation even when realtime is disconnected', async () => {
    const c = runtime();
    await c.doBet();
    c.isHubConnected = () => false;
    await c.leaveMachine(1);
    assert.equal(c.requests.at(-1).command.command_type, 'cancel_stake');
    assert.equal(c.reservationId, null);
});
