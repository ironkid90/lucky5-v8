// ╔════════════════════════════════════════════════════════════════════════╗
// ║  LUCKY5 ENGINE  —  game.js                                           ║
// ║  Variant config lives in game-config.js (loaded before this file)    ║
// ╚════════════════════════════════════════════════════════════════════════╝
//
// Section map:
//   1. ENGINE BOOTSTRAP     — GAME_CONFIG aliases, card-code builder
//   2. RUNTIME STATE        — mutable session variables
//   3. API LAYER            — apiCall wrapper + endpoint helpers
//   4. SESSION MANAGEMENT   — machine session, token, credits
//   5. STATE MACHINE        — refreshIdleMachineState, restoreRoundFromSnapshot
//   6. RENDERING            — renderCards, renderDoubleUpCards
//   7. ANIMATION HELPERS    — startShuffle / stopShuffle, flash effects
//   8. JACKPOT & PAYTABLE   — updateJackpotDisplay, updatePaytable
//   9. ACTIONS              — doDeal, doDraw, doDoubleUp, doSwitch, TAKE/HOLD
//  10. SHELL / LOBBY        — showLobby, showWallet, showAdmin, initGame
//  11. DOM BOOTSTRAP        — DOMContentLoaded initialization

const API = resolveApiBase();

function resolveApiBase() {
    const meta = document.querySelector('meta[name="lucky5-api-base"]')?.content?.trim();
    const globalBase = typeof window !== 'undefined' ? window.LUCKY5_API_BASE_URL : '';
    const stored = (() => {
        try { return localStorage.getItem('lucky5_api_base') || ''; } catch (_) { return ''; }
    })();
    return (globalBase || meta || stored || '').replace(/\/$/, '');
}

const DEBUG_ENABLED = (() => {
    try {
        return window.location.search.includes('debug=1') || localStorage.getItem('lucky5_debug') === '1';
    } catch (_) {
        return false;
    }
})();

function debugLog(event, payload) {
    if (!DEBUG_ENABLED) return;
    console.debug(`[Lucky5] ${event}`, payload ?? '');
}

function normalizeRole(role) {
    return String(role || 'player').trim().toLowerCase();
}

let token = sessionStorage.getItem('lucky5_token') || null;
let currentUsername = sessionStorage.getItem('lucky5_username') || '';
let currentRole = normalizeRole(sessionStorage.getItem('lucky5_role'));
let balance = 0;
let walletBalance = 0;
let currentBet = 5000;
let machineId = Number.parseInt(sessionStorage.getItem('lucky5_machineId') || '0', 10) || 0;
let roundId = null;
let cards = [];
let holdIndexes = new Set();
let gameState = 'idle';
let winAmount = 0;
let machines = [];
let paytable = {};
let pressSound = null;
let duSwitchesRemaining = 0;
let duIsNoLoseActive = false;
let duLuckyMultiplier = 1;
let duSessionStarted = false;
let duDealerCard = null;
let duCardTrail = [];
let duLastRenderedTrailLength = 0;
let duHighlightHandRank = null;
let duBoardBonusAmount = 0;
let duCurrentBonusAmount = 0;
let duSlotIndex = 0;
let roundDoubleUpAvailable = false;
let takeHalfUsedThisRound = false;
let jackpots = null;
let shuffleInterval = null;
let takeScoreAnimating = false;
let handsPlayed = 0;
let currentHandRank = null;
let jackpotRank = 14;
let active4kSlot = 0;
let machineSerial = 0;
let machineSerie = 0;
let machineKent = 0;
let hubConnection = null;
let machineJoined = false;
let jackpotRankArmed = false;
let machineCanCashOut = false;
let machineSessionClosed = false;
let machineCashOutThreshold = 0;
let adminUsers = [];
let adminAgents = [];
let adminMachines = [];
let lucky5FlashResetTimer = null;

// ── 1. ENGINE BOOTSTRAP ───────────────────────────────────────────────────
// Local aliases so engine logic never hard-codes variant-specific values.
// All values come from GAME_CONFIG (game-config.js, loaded first).

const MACHINE_CREDIT_LIMIT = GAME_CONFIG.rules.machineCreditLimit;
const RANK_NAMES           = GAME_CONFIG.rules.rankNames;
const JACKPOT_HANDS        = GAME_CONFIG.rules.jackpotHands;
const JACKPOT_RESET        = GAME_CONFIG.rules.jackpotReset;
const HAND_DISPLAY         = GAME_CONFIG.paytableMap;
const CARD_BACK_SRC        = GAME_CONFIG.assets.cardBack;
const VARIANT_NAME         = String(GAME_CONFIG.meta.variantName || 'Lucky 5');
const DU_COPY              = GAME_CONFIG.doubleUp.copy || {};
const DU_LABEL_TEXT        = String(DU_COPY.label || 'HI LO GAMBLE');
const DU_ACE_RULE_TEXT     = String(DU_COPY.aceRule || 'ACE COUNTS');
const DU_GUESS_RULE_TEXT   = String(DU_COPY.guessRule || 'HI OR LO');
const DU_LUCKY_RULE_TEXT   = String(DU_COPY.luckyRule || '5 \u2660 NEVER LOSE');
const DU_BUYING_RULE_TEXT  = String(DU_COPY.buyingRule || 'WHEN BUYING');
const DU_PROMPT_TEXT       = String(DU_COPY.prompt || 'BIG / SMALL ?');
const DU_ACTIVE_SUFFIX     = String(DU_COPY.activeSuffix || 'ACTIVE');

// T = short timing alias; use T.propMs throughout instead of magic numbers.
const T = GAME_CONFIG.timing;

// Build the full 52-card code list (e.g. '2H', 'AS', '10D' …)
const ALL_CARD_CODES = [];
(function buildCardCodes() {
    const suits = ['H','D','C','S'];
    const ranks = ['2','3','4','5','6','7','8','9','10','J','Q','K','A'];
    for (const r of ranks) {
        for (const s of suits) { ALL_CARD_CODES.push(r + s); }
    }
})();

// ── 2. RUNTIME STATE ──────────────────────────────────────────────────────
// Mutable session variables — reset on logout / machine leave.

const preloadedImages = {};

function preloadAllAssets() {
    return new Promise((resolve) => {
        const allPaths = [];

        // Card images are no longer preloaded as they are now DOM-based
        allPaths.push(CARD_BACK_SRC);

const buttonFiles = [
             'bet.png', 'bet_on.png',
             'big.png', 'big_on.png',
             'small.png', 'small_on.png',
             'deal_draw.png', 'deal_draw_on.png',
             'cancel_hold.png', 'cancel_hold_on.png',
             'hold_off.png', 'hold_on.png',
             'take_half.png', 'take_half_on.png',
             'take_score.png', 'take_score_on.png'
         ];
         buttonFiles.forEach(f => allPaths.push(`/assets/images/${f}`));

        allPaths.push('/assets/images/board.png');
        allPaths.push('/assets/images/lucky5.png');
        allPaths.push('/assets/images/coin.png');
        allPaths.push('/assets/images/splash.png');

        const total = allPaths.length;
        let loaded = 0;
        const fillEl = document.getElementById('loader-fill');
        const textEl = document.getElementById('loader-text');

        function onDone() {
            loaded++;
            const pct = Math.round((loaded / total) * 100);
            if (fillEl) fillEl.style.width = pct + '%';
            if (textEl) textEl.textContent = `LOADING ${loaded}/${total}`;
            if (loaded >= total) {
                const loader = document.getElementById('asset-loader');
                if (loader) {
                    loader.classList.add('done');
                    setTimeout(() => { loader.style.display = 'none'; }, 500);
                }
                resolve();
            }
        }

        allPaths.forEach(src => {
            const img = new Image();
            img.onload = onDone;
            img.onerror = onDone;
            img.src = src;
            preloadedImages[src] = img;
        });
    });
}

function randomCardSrc() {
    const code = ALL_CARD_CODES[Math.floor(Math.random() * ALL_CARD_CODES.length)];
    return `/assets/images/cards/${code}.png`;
}

function $(sel) { return document.querySelector(sel); }
function $$(sel) { return document.querySelectorAll(sel); }

// ── 3. API LAYER ─────────────────────────────────────────────────────────
// All backend calls go through apiCall().  Endpoint strings come from
// GAME_CONFIG.api so swapping the backend only requires editing game-config.js.
function normalizeApiPayload(value) {
    if (Array.isArray(value)) {
        return value.map(normalizeApiPayload);
    }

    if (!value || typeof value !== 'object') {
        return value;
    }

    const normalized = {};
    for (const [key, val] of Object.entries(value)) {
        const normalizedKey = key.length > 0
            ? key.charAt(0).toLowerCase() + key.slice(1)
            : key;
        normalized[normalizedKey] = normalizeApiPayload(val);
    }
    return normalized;
}
async function apiCall(method, path, body) {
    const opts = {
        method,
        headers: { 'Content-Type': 'application/json' }
    };
    if (token) opts.headers['Authorization'] = `Bearer ${token}`;
    if (body) opts.body = JSON.stringify(body);
    const url = `${API}${path}`;
    debugLog('api:request', { method, url, body });
    const res = await fetch(url, opts);
    const raw = await res.text();
    let json = null;
    try {
        json = raw ? JSON.parse(raw) : null;
    } catch (_) {
        throw new Error(`Non-JSON response from ${path}: ${raw.slice(0, 160)}`);
    }
    const statusText = json?.status ?? json?.Status;
    const errors = json?.errors ?? json?.Errors;
    const message = json?.message ?? json?.Message;
    const payload = normalizeApiPayload(json?.data ?? json?.Data ?? json ?? null);

    if (!res.ok || String(statusText || '').toLowerCase() === 'error') {
        throw new Error(message || errors?.[0] || 'Request failed');
    }

    debugLog('api:response', { method, url, status: res.status, data: payload });
    return payload;
}

function updateViewportUnit() {
    const vh = window.innerHeight * 0.01;
    document.body.style.setProperty('--app-vh', `${vh}px`);
}

function hasCabinetStage() {
    return Boolean(window.CabinetStage) && Boolean(window.GAME_CONFIG?.features?.enableCabinetStage);
}

function renderDealStage(cardData, onComplete) {
    if (hasCabinetStage()) {
        CabinetStage.dealCards(cardData, onComplete);
        return;
    }
    renderCards(cardData, true);
    if (onComplete) {
        setTimeout(onComplete, T.dealBaseMs + (4 * T.dealStaggerMs) + T.dealAnimDurationMs + 160);
    }
}

function renderDrawStage(cardData, held, onComplete) {
    if (hasCabinetStage()) {
        CabinetStage.drawCards(cardData, held, onComplete);
        return;
    }
    renderCards(cardData, false);
    if (onComplete) {
        setTimeout(onComplete, 400);
    }
}

function normalizeDoubleUpTrailEntry(entry) {
    if (!entry) return null;

    if (entry.card || entry.label) {
        const card = entry.card && entry.card.code ? entry.card : null;
        return card ? {
            card,
            label: String(entry.label || '').trim().toUpperCase()
        } : null;
    }

    return entry.code ? { card: entry, label: '' } : null;
}

function getCabinetDoubleUpTrailEntries() {
    const trail = Array.isArray(duCardTrail)
        ? duCardTrail.map(normalizeDoubleUpTrailEntry).filter(Boolean)
        : [];

    if (!duDealerCard || trail.length === 0) {
        return trail;
    }

    const lastEntry = trail[trail.length - 1];
    return lastEntry?.card?.code === duDealerCard?.code
        ? trail.slice(0, -1)
        : trail;
}

function syncDoubleUpTrailFromServer(cardTrail, dealerCard, fallbackTrail = duCardTrail) {
    const normalizedFallback = Array.isArray(fallbackTrail)
        ? fallbackTrail.map(normalizeDoubleUpTrailEntry).filter(Boolean)
        : [];

    const normalizedServer = Array.isArray(cardTrail)
        ? cardTrail
            .filter(card => Boolean(card?.code))
            .map((card, index, all) => {
                const directMatch = normalizedFallback[index];
                const fallbackMatch = directMatch?.card?.code === card.code
                    ? directMatch
                    : normalizedFallback.find(entry => entry?.card?.code === card.code && entry.label && entry.label !== 'DEALER');
                const isDealer = dealerCard?.code && card.code === dealerCard.code && index === all.length - 1;
                return {
                    card,
                    label: isDealer ? 'DEALER' : (fallbackMatch?.label || '')
                };
            })
        : [];

    if (normalizedServer.length > 0) {
        return normalizedServer;
    }

    return normalizedFallback;
}

function getCabinetDoubleUpTrailCards() {
    return getCabinetDoubleUpTrailEntries().map(entry => entry.card);
}

function bindSingleButton(id, handler) {
    const nodes = document.querySelectorAll(`#${id}`);
    if (nodes.length !== 1) {
        console.error(`[Lucky5] expected 1 node for #${id}, found ${nodes.length}`);
    }
    const node = nodes[0];
    if (!node) return;
    node.addEventListener('click', handler);
}

window.render_game_to_text = function renderGameToText() {
    return JSON.stringify({
        mode: gameState,
        machineId,
        roundId,
        balance,
        currentBet,
        winAmount,
        machineJoined,
        machineSessionClosed,
        cards: Array.isArray(cards) ? cards.map(c => c?.code || null) : [],
        holds: Array.from(holdIndexes),
        doubleUp: {
            started: duSessionStarted,
            dealer: duDealerCard?.code || null,
            switchesRemaining: duSwitchesRemaining,
            noLose: duIsNoLoseActive,
            luckyMultiplier: duLuckyMultiplier,
            trail: getCabinetDoubleUpTrailEntries().map(entry => ({
                card: entry.card?.code || null,
                label: entry.label || ''
            }))
        },
        jackpots: {
            fullHouseRank: jackpotRank,
            activeFourOfAKindSlot: active4kSlot,
            machineSerial,
            machineSerie,
            machineKent
        },
        viewport: { innerWidth: window.innerWidth, innerHeight: window.innerHeight }
    });
};

window.advanceTime = function advanceTime(ms) {
    return new Promise(resolve => setTimeout(resolve, Math.max(0, Number(ms) || 0)));
};

function setActiveScreen(screenName) {
    debugLog('setActiveScreen', { screenName });
    ['lobby','wallet','admin','game'].forEach(name => {
        const el = document.getElementById(`${name}-screen`);
        if (!el) return;
        const isActive = name === screenName;
        el.classList.toggle('active', isActive);
        debugLog('screen:toggle', { name, isActive, display: window.getComputedStyle(el).display, zIndex: window.getComputedStyle(el).zIndex });
    });
}

function setLobbyNavActive(target) {
    document.querySelectorAll('.lobby-nav-item').forEach(n => n.classList.remove('active'));
    if (!target) return;
    const activeNav = document.getElementById(`nav-${target}`);
    if (activeNav) activeNav.classList.add('active');
}

function setMenuPanelOpen(isOpen) {
    const menuPanel = document.getElementById('menu-panel');
    if (!menuPanel) return;
    const nextOpen = Boolean(isOpen);
    menuPanel.classList.toggle('is-open', nextOpen);
    menuPanel.classList.toggle('visible', nextOpen);
    document.body.classList.toggle('menu-open', nextOpen);
}

function activateShellScreen(screenName, navTarget = screenName === 'game' ? null : screenName) {
    setMenuPanelOpen(false);
    setActiveScreen(screenName);
    setLobbyNavActive(navTarget);
}

function getMachineSessionPath(targetMachineId = machineId) {
    return `/api/Game/machine/${targetMachineId}/session`;
}

function getMachineActiveRoundPath(targetMachineId = machineId) {
    return `/api/Game/machine/${targetMachineId}/active-round`;
}

function getMachineCabinetSnapshotPath(targetMachineId = machineId) {
    return GAME_CONFIG.api.machineCabinetSnapshot(targetMachineId);
}

function getPlayerMachineResetPath(targetMachineId = machineId) {
    return `/api/Game/machine/${targetMachineId}/reset`;
}

function getAdminMachineResetPath(targetMachineId) {
    return `/api/Admin/machines/${targetMachineId}/reset`;
}

function getMachineCloseMessage(context = 'menu') {
    switch (context) {
        case 'take-score':
            return 'MACHINE CLOSED - TAKE SCORE THEN CASH OUT FROM MENU';
        case 'cashing-out':
            return 'MACHINE CLOSED - CASHING OUT...';
        default:
            return 'MACHINE CLOSED - CASH OUT FROM MENU TO CONTINUE';
    }
}

function clearCurrentMachineSelection() {
    machineId = 0;
    machineCanCashOut = false;
    machineSessionClosed = false;
    machineCashOutThreshold = 0;
    sessionStorage.removeItem('lucky5_machineId');
}

function resetGameRuntimeState({ clearSelection = false } = {}) {
    stopShuffle();
    hideDuInfo();
    clearLucky5Effects();
    duCardTrail = [];
    duLastRenderedTrailLength = 0;
    duSessionStarted = false;
    resetDoubleUpPanelState();
    duDealerCard = null;
    roundDoubleUpAvailable = false;
    takeHalfUsedThisRound = false;
    takeScoreAnimating = false;
    currentHandRank = null;
    holdIndexes.clear();
    cards = [];
    gameState = 'idle';
    jackpotRankArmed = false;
    window.jackpotRankArmed = false;
    winAmount = 0;
    roundId = null;
    machineJoined = false;

    if (clearSelection) {
        clearCurrentMachineSelection();
    }
}

function syncMachineSessionState(session) {
    machineSessionClosed = Boolean(session?.isMachineClosed);
    machineCanCashOut = Boolean(session?.canCashOut);
    const nextThreshold = Number(session?.cashOutThreshold);
    if (Number.isFinite(nextThreshold)) {
        machineCashOutThreshold = nextThreshold;
    }
}

function readCabinetField(source, ...keys) {
    if (!source || typeof source !== 'object') {
        return undefined;
    }

    for (const key of keys) {
        if (Object.prototype.hasOwnProperty.call(source, key) && source[key] != null) {
            return source[key];
        }
    }

    return undefined;
}

function parseCabinetNumber(value, fallback = 0) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
}

function normalizeCabinetHandRank(handRank) {
    switch (String(handRank || '').trim()) {
        case 'RoyalFlush': return 'RoyalFlush';
        case 'StraightFlush': return 'StraightFlush';
        case 'FourOfAKind': return 'FourOfAKind';
        case 'FullHouse': return 'FullHouse';
        case 'Flush': return 'Flush';
        case 'Straight': return 'Straight';
        case 'ThreeOfAKind': return 'ThreeOfAKind';
        case 'TwoPair': return 'TwoPair';
        default: return 'Nothing';
    }
}

function parseCabinetCard(cardLike) {
    if (!cardLike) return null;

    const code = String(readCabinetField(cardLike, 'code', 'Code') || '').toUpperCase();
    if (!code) return null;

    return {
        code,
        rank: String(readCabinetField(cardLike, 'rank', 'Rank') || code.slice(0, -1)).toUpperCase(),
        suit: String(readCabinetField(cardLike, 'suit', 'Suit') || code.slice(-1)).toUpperCase()
    };
}

function normalizeCabinetJackpots(snapshotJackpot) {
    if (!snapshotJackpot) return null;

    const activeSlot = String(readCabinetField(snapshotJackpot, 'activeFourOfAKindSlot', 'active_four_of_a_kind_slot') || 'A').toUpperCase();
    return {
        fullHouse: parseCabinetNumber(readCabinetField(snapshotJackpot, 'fullHouse', 'full_house')),
        fullHouseRank: parseCabinetNumber(readCabinetField(snapshotJackpot, 'fullHouseRank', 'full_house_rank'), jackpotRank),
        fourOfAKindA: parseCabinetNumber(readCabinetField(snapshotJackpot, 'fourOfAKindA', 'four_of_a_kind_a')),
        fourOfAKindB: parseCabinetNumber(readCabinetField(snapshotJackpot, 'fourOfAKindB', 'four_of_a_kind_b')),
        activeFourOfAKindSlot: activeSlot === 'B' ? 1 : 0,
        straightFlush: parseCabinetNumber(readCabinetField(snapshotJackpot, 'straightFlush', 'straight_flush')),
        machineSerial: String(readCabinetField(snapshotJackpot, 'machineSerial', 'machine_serial') || machineSerial || ''),
        machineSerie: String(readCabinetField(snapshotJackpot, 'machineSerie', 'machine_serie') || machineSerie || ''),
        machineKent: String(readCabinetField(snapshotJackpot, 'machineKent', 'machine_kent') || machineKent || '')
    };
}

function isCabinetButtonEnabled(snapshot, buttonId) {
    const buttons = readCabinetField(snapshot, 'buttons');
    if (!Array.isArray(buttons)) return false;

    return buttons.some((button) => {
        const id = String(readCabinetField(button, 'id', 'Id') || '').toLowerCase();
        const enabled = readCabinetField(button, 'enabled', 'Enabled');
        return id === String(buttonId).toLowerCase() && Boolean(enabled);
    });
}

function applyCabinetSnapshot(snapshot) {
    if (!snapshot) return null;

    const credits = readCabinetField(snapshot, 'credits') || {};
    const presentation = readCabinetField(snapshot, 'presentation') || {};
    const evaluation = readCabinetField(snapshot, 'evaluation') || {};
    const sessionState = readCabinetField(snapshot, 'session') || {};
    const normalizedJackpots = normalizeCabinetJackpots(readCabinetField(snapshot, 'jackpot'));

    const nextStake = parseCabinetNumber(readCabinetField(credits, 'stake'), currentBet);
    if (nextStake > 0) {
        currentBet = nextStake;
    }

    syncMachineCreditsFromResponse({
        machineCredits: readCabinetField(credits, 'machineCredits', 'machine_credits'),
        walletBalance: readCabinetField(credits, 'walletBalance', 'wallet_balance')
    });

    walletBalance = parseCabinetNumber(readCabinetField(credits, 'walletBalance', 'wallet_balance'), walletBalance);
    syncMachineSessionState({
        isMachineClosed: readCabinetField(sessionState, 'isMachineClosed', 'is_machine_closed'),
        canCashOut: readCabinetField(sessionState, 'canCashOut', 'can_cash_out'),
        cashOutThreshold: readCabinetField(credits, 'cashOutThreshold', 'cash_out_threshold'),
        isArmed: readCabinetField(sessionState, 'isArmed', 'is_armed')
    });

    updateLobbyBalance();
    updateStakeDisplay();
    updatePaytable(currentHandRank);

    if (normalizedJackpots) {
        updateJackpotDisplay(normalizedJackpots);
    }

    return {
        gameState: String(readCabinetField(snapshot, 'gameState', 'game_state') || 'idle').toLowerCase(),
        pendingWinAmount: parseCabinetNumber(
            readCabinetField(credits, 'pendingWinAmount', 'pending_win_amount')
                ?? readCabinetField(evaluation, 'winAmount', 'win_amount'),
            0),
        message: String(
            readCabinetField(presentation, 'message')
                ?? readCabinetField(evaluation, 'message')
                ?? '').trim()
    };
}

function buildRoundSnapshotFromCabinetSnapshot(snapshot) {
    if (!snapshot) return null;

    const cabinetGameState = String(readCabinetField(snapshot, 'gameState', 'game_state') || '').toLowerCase();
    if (!['hold', 'win', 'double_up'].includes(cabinetGameState)) {
        return null;
    }

    const hand = readCabinetField(snapshot, 'hand') || {};
    const credits = readCabinetField(snapshot, 'credits') || {};
    const evaluation = readCabinetField(snapshot, 'evaluation') || {};
    const doubleUp = readCabinetField(snapshot, 'doubleUp', 'double_up') || {};

    const roundId = readCabinetField(doubleUp, 'roundId', 'round_id')
        || readCabinetField(hand, 'roundId', 'round_id');
    if (!roundId) {
        return null;
    }

    const heldIndexes = Array.isArray(readCabinetField(hand, 'heldIndexes', 'held_indexes'))
        ? readCabinetField(hand, 'heldIndexes', 'held_indexes')
            .map((index) => parseInt(index, 10))
            .filter((index) => Number.isFinite(index))
        : [];

    const rawCards = (cabinetGameState === 'win' || cabinetGameState === 'double_up')
        ? readCabinetField(hand, 'resultCards', 'result_cards') || readCabinetField(hand, 'cards')
        : readCabinetField(hand, 'cards');

    const cardsForRestore = Array.isArray(rawCards)
        ? rawCards.map(parseCabinetCard).filter(Boolean)
        : [];

    const pendingWinAmount = parseCabinetNumber(
        readCabinetField(credits, 'pendingWinAmount', 'pending_win_amount')
            ?? readCabinetField(evaluation, 'winAmount', 'win_amount'),
        0);

    const takeHalfUsed = cabinetGameState !== 'hold'
        && !isCabinetButtonEnabled(snapshot, 'take_half')
        && isCabinetButtonEnabled(snapshot, 'take_score');

    const snapshotRound = {
        roundId,
        betAmount: parseCabinetNumber(readCabinetField(credits, 'stake'), currentBet),
        phase: cabinetGameState === 'hold' ? 'Dealt'
            : cabinetGameState === 'double_up' ? 'DoubleUp'
            : 'Drawn',
        handRank: normalizeCabinetHandRank(readCabinetField(evaluation, 'handRank', 'hand_rank')),
        cards: cardsForRestore,
        heldIndexes,
        pendingWinAmount,
        doubleUpAvailable: Boolean(readCabinetField(evaluation, 'doubleUpAvailable', 'double_up_available')),
        takeHalfUsed
    };

    if (snapshotRound.phase === 'DoubleUp') {
        snapshotRound.doubleUpSession = {
            dealerCard: parseCabinetCard(readCabinetField(doubleUp, 'dealerCard', 'dealer_card')),
            currentAmount: parseCabinetNumber(readCabinetField(doubleUp, 'currentAmount', 'current_amount'), pendingWinAmount),
            switchesRemaining: parseCabinetNumber(readCabinetField(doubleUp, 'switchesRemaining', 'switches_remaining')),
            isNoLoseActive: Boolean(readCabinetField(doubleUp, 'isNoLoseActive', 'is_no_lose_active')),
            isLucky5Active: Boolean(readCabinetField(doubleUp, 'isLucky5Active', 'is_lucky5_active')),
            luckyMultiplier: parseCabinetNumber(readCabinetField(doubleUp, 'luckyMultiplier', 'lucky_multiplier'), 1),
            cardTrail: Array.isArray(readCabinetField(doubleUp, 'cardTrail', 'card_trail'))
                ? readCabinetField(doubleUp, 'cardTrail', 'card_trail').map(parseCabinetCard).filter(Boolean)
                : []
        };
    }

    return snapshotRound;
}

function hasRecoverableMachineSession(session, cabinetSnapshot) {
    const sessionCredits = Number(session?.machineCredits);
    if (Number.isFinite(sessionCredits) && sessionCredits > 0) {
        return true;
    }

    if (Boolean(session?.isMachineClosed) || Boolean(session?.canCashOut)) {
        return true;
    }

    if (!cabinetSnapshot) {
        return false;
    }

    const cabinetGameState = String(readCabinetField(cabinetSnapshot, 'gameState', 'game_state') || '').toLowerCase();
    if (['hold', 'win', 'double_up', 'closed'].includes(cabinetGameState)) {
        return true;
    }

    const credits = readCabinetField(cabinetSnapshot, 'credits') || {};
    const pendingWinAmount = parseCabinetNumber(readCabinetField(credits, 'pendingWinAmount', 'pending_win_amount'));
    const machineCredits = parseCabinetNumber(readCabinetField(credits, 'machineCredits', 'machine_credits'));

    return pendingWinAmount > 0 || machineCredits > 0;
}

function isMachineClosedForUi() {
    return machineSessionClosed || balance >= MACHINE_CREDIT_LIMIT;
}

// ── 4. SESSION MANAGEMENT ──────────────────────────────────────────────
// Machine session, token helpers, credit sync.
async function fetchMachineSession() {
    const session = await apiCall('GET', getMachineSessionPath());
    syncMachineCreditsFromResponse(session);
    syncMachineSessionState(session);
    walletBalance = session.walletBalance ?? walletBalance;
    updateLobbyBalance();
    return session;
}

async function fetchActiveRoundState() {
    return await apiCall('GET', getMachineActiveRoundPath());
}

async function fetchCabinetSnapshot() {
    if (!GAME_CONFIG.features.enableDisplaySnapshot) {
        return null;
    }

    return await apiCall('GET', getMachineCabinetSnapshotPath());
}

async function cashInMachine(amount) {
    const session = await apiCall('POST', GAME_CONFIG.api.machineCashIn(machineId), { amount });
    syncMachineCreditsFromResponse(session);
    syncMachineSessionState(session);
    walletBalance = session.walletBalance ?? walletBalance;
    updateLobbyBalance();
    return session;
}

async function cashOutMachine() {
    const session = await apiCall('POST', GAME_CONFIG.api.machineCashOut(machineId));
    syncMachineCreditsFromResponse(session);
    syncMachineSessionState(session);
    walletBalance = session.walletBalance ?? walletBalance;
    updateLobbyBalance();
    return session;
}

function syncMachineCreditsFromResponse(source) {
    const rawCredits = source?.machineCredits
        ?? source?.machineCreditsAfterRound
        ?? source?.walletBalanceAfterBet
        ?? source?.walletBalanceAfterRound
        ?? source?.walletBalance;
    const nextBalance = Number(rawCredits);
    if (Number.isFinite(nextBalance)) {
        balance = nextBalance;
    }
    updateCredits();
    return balance;
}

// ── 5. STATE MACHINE ──────────────────────────────────────────────────
function refreshIdleMachineState(messageText = null, type = 'win') {
    stopShuffle();
    hideDuInfo();
    clearLucky5Effects();
    holdIndexes.clear();
    duSessionStarted = false;
    resetDoubleUpPanelState();
    duDealerCard = null;
    duCardTrail = [];
    duLastRenderedTrailLength = 0;
    roundDoubleUpAvailable = false;
    takeHalfUsedThisRound = false;
    currentHandRank = null;
    winAmount = 0;
    roundId = null;
    takeScoreAnimating = false;
    gameState = 'idle';
    jackpotRankArmed = false;
    window.jackpotRankArmed = false;
    updatePaytable();
    updateBonusBar(null);
    updateWinIndicator(0);
    updateWinAmountDisplay(0);
    setButtonStates();

    if (messageText) {
        showMessage(messageText, type);
    } else if (isMachineClosedForUi()) {
        // Machine is closed — credits are at/above the limit.
        // Player can still cash out or continue DU if in progress.
        // No new deals are allowed.
        if (balance > 0) {
            showMessage(`MACHINE CLOSED - ${formatNum(balance)} CR - CASH OUT FROM MENU`, 'win');
        } else {
            showMessage(getMachineCloseMessage(), 'win');
        }
    } else if (balance > 0 && machineCanCashOut) {
        showMessage(`CASH OUT AVAILABLE AT ${formatNum(machineCashOutThreshold)}`, 'win');
    } else if (balance > 0) {
        showMessage('PLACE YOUR BET');
    } else {
        showMessage('CASH IN FROM MENU');
    }

    showIdleTitle();
}

function playPress() {
     if (!pressSound) {
         pressSound = new Audio('/assets/images/press.mp3');
         pressSound.volume = 0.3;
     }
     pressSound.currentTime = 0;
     pressSound.play().catch(() => {});
 }

function formatNum(n) {
    return Math.floor(n).toLocaleString();
}

function updateCredits() {
    $('#credits span').textContent = formatNum(balance);
}

function updateStakeDisplay() {
    $('#stake-display span').textContent = formatNum(currentBet);
}

function showMessage(text, type) {
    const msg = $('#game-message');
    msg.textContent = text;
    msg.className = type || '';
}

function normalizeLuckyMultiplier(value, fallback = 1) {
    const next = Number(value);
    if (Number.isFinite(next) && next > 1) {
        return Math.floor(next);
    }
    return fallback;
}

function normalizeHandRank(value) {
    const text = String(value || '').trim();
    if (!text || text === 'Nothing' || text === 'None') {
        return null;
    }

    return text.replace(/\s+/g, '');
}

function resetDoubleUpAwardState() {
    duHighlightHandRank = null;
    duBoardBonusAmount = 0;
    duCurrentBonusAmount = 0;
    duSlotIndex = 0;
}

function applyDoubleUpInfoCopy() {
    const labelEl = document.getElementById('du-label');
    const aceEl = document.getElementById('du-ace-info');
    const guessEl = document.getElementById('du-guess-info');
    const luckyEl = document.getElementById('du-lucky-info');

    if (labelEl) labelEl.textContent = DU_LABEL_TEXT;
    if (aceEl) aceEl.textContent = DU_ACE_RULE_TEXT;
    if (guessEl) guessEl.textContent = DU_GUESS_RULE_TEXT;
    if (luckyEl) luckyEl.textContent = DU_LUCKY_RULE_TEXT;
}

function updateDoubleUpInfoPanel() {
    applyDoubleUpInfoCopy();

    const panel = document.getElementById('du-info-panel');
    const luckyEl = document.getElementById('du-lucky-info');
    const buyingEl = document.getElementById('du-buying-info');
    const isLuckyActive = Boolean(duIsNoLoseActive);

    if (panel) {
        panel.classList.toggle('lucky5-active', isLuckyActive);
    }

    if (luckyEl) {
        luckyEl.classList.toggle('is-active', isLuckyActive);
    }

    if (buyingEl) {
        const luckySuffix = isLuckyActive
            ? duLuckyMultiplier > 1
                ? ` • x${duLuckyMultiplier} ${DU_ACTIVE_SUFFIX}`
                : ` • ${DU_ACTIVE_SUFFIX}`
            : '';
        buyingEl.textContent = `${DU_BUYING_RULE_TEXT}${luckySuffix}`;
    }
}

function syncDoubleUpPanelState(source, { preserveMultiplier = false } = {}) {
    duSwitchesRemaining = Number(source?.switchesRemaining || 0);
    duIsNoLoseActive = Boolean(source?.isLucky5Active || source?.isNoLoseActive);
    duHighlightHandRank = normalizeHandRank(source?.boardHandRank);
    duBoardBonusAmount = Number(source?.boardBonusAmount || 0);
    duCurrentBonusAmount = Number(source?.currentBonusAmount || 0);
    duSlotIndex = Number(source?.slotIndex || 0);

    if (duIsNoLoseActive) {
        const fallbackMultiplier = preserveMultiplier ? duLuckyMultiplier : 1;
        duLuckyMultiplier = normalizeLuckyMultiplier(source?.luckyMultiplier, fallbackMultiplier);
    } else {
        duLuckyMultiplier = 1;
    }

    updateDoubleUpInfoPanel();
}

function resetDoubleUpPanelState() {
    duSwitchesRemaining = 0;
    duIsNoLoseActive = false;
    duLuckyMultiplier = 1;
    resetDoubleUpAwardState();
    updateDoubleUpInfoPanel();
}

function getLuckyActiveBannerText() {
    const variant = VARIANT_NAME.toUpperCase();
    const multiplierSuffix = duLuckyMultiplier > 1 ? ` X${duLuckyMultiplier}` : '';
    return `5\u2660 ${variant}${multiplierSuffix} ${DU_ACTIVE_SUFFIX}`;
}

function canStartDoubleUpFromWin() {
    return gameState === 'win' && roundDoubleUpAvailable && winAmount > 0 && !duSessionStarted;
}

function showWinActionMessage() {
    if (winAmount <= 0) {
        return;
    }

    if (roundDoubleUpAvailable) {
        showMessage(`WIN: ${formatNum(winAmount)} - DOUBLE UP`, 'win');
        return;
    }

    if (takeHalfUsedThisRound) {
        showMessage(`WIN: ${formatNum(winAmount)} - TAKE SCORE`, 'win');
        return;
    }

    showMessage(`WIN: ${formatNum(winAmount)} - TAKE SCORE OR TAKE HALF`, 'win');
}

function updateWinIndicator(amount) {
    const el = $('#win-indicator');
    if (!el) return;
    if (amount > 0) {
        el.textContent = `WIN ${formatNum(amount)}`;
        el.classList.add('growing');
        setTimeout(() => el.classList.remove('growing'), 500);
    } else {
        el.textContent = '';
    }
}

function updatePaytable(activeHand) {
    const highlightHand = gameState === 'doubleup'
        ? (duHighlightHandRank || normalizeHandRank(activeHand) || currentHandRank)
        : normalizeHandRank(activeHand);
    const highlightAmount = gameState === 'doubleup'
        ? (winAmount > 0 ? winAmount : duBoardBonusAmount)
        : (gameState === 'win' && winAmount > 0 ? winAmount : 0);
    const highlightClass = gameState === 'doubleup' ? 'du-highlight' : 'active';

    if (typeof PaytableCanvas !== 'undefined') {
        PaytableCanvas.setBet(currentBet);
        PaytableCanvas.setHighlight(highlightHand, highlightAmount);
    }

    $$('.pay-row').forEach(row => {
        const hand = row.dataset.hand;
        const mult = parseInt(row.querySelector('.pay-amount').dataset.mult) || 0;
        row.querySelector('.pay-amount').textContent = formatNum(mult * currentBet);
        row.classList.remove('active', 'du-highlight');
        if (highlightHand && hand === highlightHand) {
            row.classList.add(highlightClass);
            if (highlightAmount > 0) {
                row.querySelector('.pay-amount').textContent = formatNum(highlightAmount);
            }
        }
    });
}

function highlightPaytableDU(handRank, amount) {
    duHighlightHandRank = normalizeHandRank(handRank);
    updatePaytable(currentHandRank);
}

// ── 8. JACKPOT & PAYTABLE ───────────────────────────────────────────────
function updateJackpotDisplay(jp) {
    const prevJackpots = jackpots ? { ...jackpots } : null;
    if (jp) {
        jackpots = jp;
        if (jp.fullHouseRank) jackpotRank = jp.fullHouseRank;
        if (jp.activeFourOfAKindSlot !== undefined) active4kSlot = jp.activeFourOfAKindSlot;
    }
    if (!jackpots) return;
    if (window.CabinetPace) CabinetPace.animateJackpotCounters(jackpots, prevJackpots);

    // Machine-info-block jackpot counters: 4K-A, SF, 4K-B plus a dedicated FH meter.
    const jpA = document.querySelector('#jp-counter-a .jp-cval');
    const jpFh = document.querySelector('#jp-counter-fh .jp-cval');
    const jpCenter = document.querySelector('#jp-counter-center .jp-cval');
    const jpB = document.querySelector('#jp-counter-b .jp-cval');
    if (jpA) jpA.textContent = formatNum(jackpots.fourOfAKindA || 0);
    if (jpFh) jpFh.textContent = formatNum(jackpots.fullHouse || 0);
    if (jpCenter) jpCenter.textContent = formatNum(jackpots.straightFlush || 0);
    if (jpB) jpB.textContent = formatNum(jackpots.fourOfAKindB || 0);

    machineSerial = jackpots.machineSerial || 0;
    machineSerie = jackpots.machineSerie || 0;
    machineKent = jackpots.machineKent || 0;

    const serialEl = document.getElementById('mi-serial');
    if (serialEl) {
        serialEl.textContent = String(machineSerial || 0);
    }

    const serieEl = document.getElementById('mi-serie');
    if (serieEl) {
        serieEl.textContent = String(machineSerie || 0);
    }

    const kentEl = document.getElementById('mi-kent');
    if (kentEl) {
        kentEl.textContent = String(machineKent || 0);
    }

    // Update Full House rank display (jackpot-selected highlight on paytable)
    const rankEl = document.getElementById('jp-fh-rank');
    if (rankEl) rankEl.textContent = RANK_NAMES[jackpotRank] || 'A';
    updateJackpotSelectedRow();
    updateActive4kHighlight();
    updateBonusHandText();
    if (gameState === 'idle') {
        showIdleTitle();
    }

    // Legacy jackpot bar (if elements exist)
    const el4kA = document.getElementById('jp-4k-a-val');
    const el4kB = document.getElementById('jp-4k-b-val');
    const elFh = document.getElementById('jp-fh-val');
    const elSf = document.getElementById('jp-sf-val');
    if (el4kA) el4kA.textContent = formatNum(jackpots.fourOfAKindA || 0);
    if (el4kB) el4kB.textContent = formatNum(jackpots.fourOfAKindB || 0);
    if (elFh) elFh.textContent = formatNum(jackpots.fullHouse || 0);
    if (elSf) elSf.textContent = formatNum(jackpots.straightFlush || 0);
}

function updateActive4kHighlight() {
    document.querySelectorAll('[data-jackpot-slot^="4k-"]').forEach(node => {
        node.classList.remove('jp-active');
    });
    const activeCounter = document.querySelector(active4kSlot === 0
        ? '[data-jackpot-slot="4k-a"]'
        : '[data-jackpot-slot="4k-b"]');
    if (activeCounter) activeCounter.classList.add('jp-active');

    const starA = document.getElementById('jp-star-a');
    const starB = document.getElementById('jp-star-b');
    if (starA) starA.style.display = active4kSlot === 0 ? 'inline' : 'none';
    if (starB) starB.style.display = active4kSlot === 1 ? 'inline' : 'none';
}

function updateJackpotSelectedRow() {
    // Show solid box around the Full House row (FH jackpot rank is selected there).
    document.querySelectorAll('.pay-row').forEach(row => row.classList.remove('jackpot-selected'));
    const fhRow = document.querySelector('.pay-row.fh');
    if (fhRow) fhRow.classList.add('jackpot-selected');
}

function updateBonusHandText() {
    const el = document.getElementById('bonus-hand-text');
    if (!el) return;
    if (gameState === 'idle') {
        el.textContent = `FULL HOUSE ${RANK_NAMES[jackpotRank] || 'A'} SELECTED`;
    } else if (active4kSlot === 0 || active4kSlot === 1) {
        el.textContent = '4  OF  A  KIND    WINS  BONUS';
    } else {
        el.textContent = '';
    }
}

function getFourOfAKindSlotTag(handRank = currentHandRank) {
    if (handRank !== 'FourOfAKind') {
        return '';
    }

    if (active4kSlot === 0) {
        return 'A';
    }

    if (active4kSlot === 1) {
        return 'B';
    }

    return '';
}

function updateWinAmountDisplay(amount, slotTag) {
    const valEl = document.getElementById('win-amount-value');
    const tagEl = document.getElementById('win-slot-tag');
    const container = document.getElementById('win-amount-display');
    const showSlotTag = slotTag === 'A' || slotTag === 'B';
    if (!valEl || !container) return;
    if (amount > 0) {
        valEl.textContent = formatNum(amount);
        if (tagEl) tagEl.textContent = showSlotTag ? slotTag : '';
        container.classList.add('visible');
    } else {
        valEl.textContent = '';
        if (tagEl) tagEl.textContent = '';
        container.classList.remove('visible');
    }
}

function updateBonusBar(handRank, jackpotWon) {
    const el = document.getElementById('bonus-text');
    const handTextEl = document.getElementById('bonus-hand-text');
    if (jackpotWon > 0) {
        const slotTag = getFourOfAKindSlotTag(handRank);
        const handLabel = handRank === 'FullHouse'
            ? `FH ${RANK_NAMES[jackpotRank] || 'A'}`
            : handRank === 'FourOfAKind' && slotTag
                ? `4K ${slotTag}`
                : HAND_DISPLAY[handRank] || 'JACKPOT';
        const jackpotMessage = `${handLabel} JACKPOT WON`;
        if (el) el.textContent = jackpotMessage;
        if (handTextEl) handTextEl.textContent = jackpotMessage;
    } else if (handRank && JACKPOT_HANDS.includes(handRank)) {
        const msg = handRank === 'FullHouse'
            ? `FH ${RANK_NAMES[jackpotRank]} JACKPOT`
            : `${HAND_DISPLAY[handRank]} JACKPOT`;
        if (el) el.textContent = msg;
    } else {
        if (el) el.textContent = '';
        updateBonusHandText();
    }
}

function cardImagePath(card) {
 if (hasCabinetStage() && typeof CabinetStage.resolveCardFaceSrc === 'function') {
 return CabinetStage.resolveCardFaceSrc(card);
 }
    if (!card || !card.code) return CARD_BACK_SRC;
    return `/assets/images/cards/${card.code}.png`;
}

function fullHouseSelectorCode(rank = jackpotRank) {
    return `${RANK_NAMES[rank] || 'A'}S`;
}

function canAdjustJackpotRank() {
    return gameState === 'idle' && jackpotRankArmed;
}

// ── Idle overlay management ────────────────────────────────────────────────
let idleOverlayTimer = null;
let idleOverlayVisible = false;

function clearIdleOverlayTimer() {
    if (idleOverlayTimer) {
        clearTimeout(idleOverlayTimer);
        idleOverlayTimer = null;
    }
}

function showIdleOverlay() {
    const overlay = $('#idle-overlay');
    if (overlay && !idleOverlayVisible) {
        overlay.classList.add('visible');
        idleOverlayVisible = true;
    }
}

function hideIdleOverlay() {
    const overlay = $('#idle-overlay');
    if (overlay && idleOverlayVisible) {
        overlay.classList.remove('visible');
        idleOverlayVisible = false;
    }
}

function scheduleIdleSelectorReveal(onReveal) {
    clearIdleOverlayTimer();

    const holdMs = Math.max(
        0,
        Number(T.idleTitleHoldMs ?? T.idleOverlayAppearMs ?? 2200) || 0
    );

    idleOverlayTimer = setTimeout(() => {
        if (gameState === 'idle' && holdIndexes.size === 0 && !isDoubleUpMode()) {
            hideIdleOverlay();
            if (typeof onReveal === 'function') {
                onReveal();
            }
        }
    }, holdMs);
}

function updateIdleOverlayVisibility() {
    if (gameState !== 'idle' || holdIndexes.size > 0 || isDoubleUpMode()) {
        hideIdleOverlay();
        clearIdleOverlayTimer();
    }
}

function showIdleTitle(animateSelector = false) {
    const area = $('#card-area');
    area.innerHTML = '';
    area.classList.remove('du-mode');
    const selector = document.createElement('div');
    selector.className = 'idle-selector';
    const card = document.createElement('div');
    card.className = 'idle-selector-card';
    if (animateSelector) card.classList.add('is-flipping');
    card.innerHTML = CabinetStage.renderDomCard(fullHouseSelectorCode());
    selector.appendChild(card);
    area.appendChild(selector);

    if (animateSelector) {
        hideIdleOverlay();
        clearIdleOverlayTimer();
        return;
    }

    selector.style.visibility = 'hidden';
    showIdleOverlay();
    scheduleIdleSelectorReveal(() => {
        if (!area.contains(selector)) {
            return;
        }

        selector.style.visibility = 'visible';
        card.classList.remove('is-flipping');
        void card.offsetWidth;
        card.classList.add('is-flipping');
    });
}

function hideIdleTitle() {
    const area = $('#card-area');
    area.innerHTML = '';
    hideIdleOverlay();
    clearIdleOverlayTimer();
}

// ── 6. RENDERING ─────────────────────────────────────────────────────────
function renderCards(cardData, animate) {
 if (hasCabinetStage()) {
 const normalizedCards = Array.isArray(cardData) ? cardData : [];
 if (animate) {
 CabinetStage.dealCards(normalizedCards);
 return;
 }
 CabinetStage.renderHand(normalizedCards, holdIndexes);
 return;
 }

 const area = $('#card-area');
    area.innerHTML = '';
    area.classList.remove('du-mode');
    for (let i = 0; i < 5; i++) {
        const slot = document.createElement('div');
        slot.className = 'card-slot';
        slot.dataset.slot = i;
        if (holdIndexes.has(i)) slot.classList.add('held');

        // Held cards on a draw stay visually stable — no drop animation.
        const isHeld = holdIndexes.has(i);
        const shouldAnimate = animate && !isHeld;

        if (shouldAnimate) {
            slot.classList.add('deal-in');
        }

        const badge = document.createElement('div');
        badge.className = 'hold-badge';
        badge.textContent = 'HOLD';

        const cardImg = document.createElement('div');
        cardImg.className = 'card-face';
        cardImg.innerHTML = `<img src="${cardImagePath(cardData[i])}" alt="card">`;

        slot.appendChild(cardImg);
        slot.appendChild(badge);

        slot.addEventListener('click', () => toggleHold(i));
        area.appendChild(slot);

        if (shouldAnimate) {
            setTimeout(() => {
                slot.classList.remove('deal-in');
                slot.classList.add('deal-in-done');
            }, T.dealBaseMs + i * T.dealStaggerMs);
        } else {
            slot.classList.add('deal-in-done');
        }
    }
}

function flashWinCards() {
    $$('.card-slot').forEach(slot => slot.classList.add('winning'));
}

function toggleHold(index) {
    if (gameState !== 'hold') return;
    playPress();
    if (holdIndexes.has(index)) {
        holdIndexes.delete(index);
    } else {
        holdIndexes.add(index);
    }
    const slots = $$('.card-slot');
    slots[index].classList.toggle('held', holdIndexes.has(index));

    const holdBtns = $$('.cab-hold');
    holdBtns[index].classList.toggle('active', holdIndexes.has(index));
    if (window.CabinetStage) CabinetStage.setHold(index, holdIndexes.has(index));
    
    // Update idle overlay visibility when holds change
    updateIdleOverlayVisibility();
}

function cycleJackpotRank() {
    if (!canAdjustJackpotRank()) return;
    jackpotRank = jackpotRank >= 14 ? 2 : jackpotRank + 1;
    apiCall('POST', GAME_CONFIG.api.jackpotRank, { machineId, rank: jackpotRank })
        .then(jp => updateJackpotDisplay(jp))
        .catch(() => {});
    const elRank = $('#jp-fh-rank');
    if (elRank) elRank.textContent = RANK_NAMES[jackpotRank] || 'A';
    updateBonusHandText();
    showIdleTitle(true);
}

function setButtonStates() {
    const betBtn = $('#btn-bet');
    const dealBtn = $('#btn-deal');
    const cancelBtn = $('#btn-cancel');
    const holdBtns = $$('.cab-hold');
    const bigBtn = $('#btn-big');
    const smallBtn = $('#btn-small');
    const takeScoreBtn = $('#btn-take-score');
    const takeHalfBtn = $('#btn-take-half');

    if (takeScoreAnimating) {
        betBtn.disabled = true;
        dealBtn.disabled = true;
        cancelBtn.disabled = true;
        bigBtn.disabled = true;
        smallBtn.disabled = true;
        takeScoreBtn.disabled = true;
        takeHalfBtn.disabled = true;
        holdBtns.forEach(btn => btn.disabled = true);
        return;
    }

    const machineClosed = isMachineClosedForUi();
    const isDoubleUp = gameState === 'doubleup';

    // BET button doubles as SWITCH during Double Up
    if (isDoubleUp) {
        betBtn.disabled = duSwitchesRemaining <= 0 || machineClosed;
        betBtn.classList.add('is-switch');
    } else {
        betBtn.disabled = gameState !== 'idle' || machineClosed;
        betBtn.classList.remove('is-switch');
    }

    dealBtn.disabled = !(gameState === 'idle' || gameState === 'hold') || machineClosed;
    cancelBtn.disabled = gameState !== 'hold';
    bigBtn.disabled = !(isDoubleUp || canStartDoubleUpFromWin());
    smallBtn.disabled = !(isDoubleUp || canStartDoubleUpFromWin());
    takeScoreBtn.disabled = !(gameState === 'win' || isDoubleUp);
    takeHalfBtn.disabled = !(gameState === 'win' || isDoubleUp) || takeHalfUsedThisRound;

    holdBtns.forEach((btn, i) => {
        if (i === 0 && canAdjustJackpotRank()) {
            btn.disabled = false;
        } else {
            btn.disabled = gameState !== 'hold';
        }
        // Visual affordance: the first hold button doubles as the
        // Full-House jackpot rank cycler during idle+armed state. Tag it
        // with .fh-target so the v8 CSS paints a red-edged "FH" label,
        // making the dual behavior unambiguous to the player.
        if (i === 0) {
            btn.classList.toggle('fh-target', canAdjustJackpotRank());
        }
    });
}

let betResetPending = false;

async function doBet() {
    if (gameState === 'doubleup') {
        await doSwitchDealer();
        return;
    }
    if (gameState !== 'idle') return;
    playPress();
    const machine = machines.find(m => m.id === machineId);
    if (!machine) return;
    if (betResetPending) {
        currentBet = machine.minBet;
        betResetPending = false;
    } else if (currentBet >= machine.maxBet) {
        currentBet = machine.maxBet;
    } else {
        currentBet = Math.min(currentBet + 100, machine.maxBet);
    }
    jackpotRankArmed = true;
    updateStakeDisplay();
    updatePaytable();
    updateBonusHandText();
    window.jackpotRankArmed = jackpotRankArmed;
    setButtonStates();
}

async function doSwitchDealer() {
    if (gameState !== 'doubleup' || duSwitchesRemaining <= 0) return;
    playPress();
    stopShuffle();

    try {
        const result = await apiCall('POST', GAME_CONFIG.api.duSwitch, { roundId });
        syncDoubleUpPanelState(result);
        winAmount = result.currentAmount;
        duDealerCard = result.dealerCard;

        duCardTrail = syncDoubleUpTrailFromServer(result.cardTrail, duDealerCard, duCardTrail);
        if ((!Array.isArray(duCardTrail) || duCardTrail.length === 0) && duDealerCard) {
            duCardTrail = [{ card: duDealerCard, label: 'DEALER' }];
        }

        const isLucky5 = result.status === 'Lucky5';
        if (isLucky5) {
            triggerLucky5Flash();
        }

        renderDoubleUpCards(duDealerCard, true, null, { pending: true });
        updatePaytable(currentHandRank);
        if (isLucky5) {
            showMessage(`${getLuckyActiveBannerText()}! WIN: ${formatNum(result.currentAmount)}`, 'win');
        } else {
            showMessage(`SWITCHED - WIN: ${formatNum(result.currentAmount)} (${duSwitchesRemaining} left)`, 'win');
        }
        setButtonStates();
    } catch (e) {
        showMessage(e.message, 'lose');
    }
}

function clearLucky5Effects() {
    const banner = document.getElementById('lucky5-banner');
    if (banner) banner.classList.remove('active');

    const flash = document.getElementById('lucky5-flash');
    if (flash) flash.classList.remove('active');

    const screen = document.getElementById('game-screen');
    if (screen) screen.classList.remove('lucky5-active');

    if (lucky5FlashResetTimer) {
        clearTimeout(lucky5FlashResetTimer);
        lucky5FlashResetTimer = null;
    }
}

function triggerLucky5Flash() {
    clearLucky5Effects();

    const banner = document.getElementById('lucky5-banner');
    if (banner) {
        void banner.offsetWidth;
        banner.classList.add('active');
    }

    const flash = document.getElementById('lucky5-flash');
    if (flash) {
        void flash.offsetWidth;
        flash.classList.add('active');
    }

    if (hasCabinetStage()) CabinetStage.showLucky5Active();

    const screen = document.getElementById('game-screen');
    if (screen) {
        void screen.offsetWidth;
        screen.classList.add('lucky5-active');
        lucky5FlashResetTimer = setTimeout(() => {
            screen.classList.remove('lucky5-active');
            lucky5FlashResetTimer = null;
        }, T.lucky5FlashDurationMs);
    }
}

function computeAutoHold(cardList) {
    if (!cardList || cardList.length !== 5) return new Set();

    const parsed = cardList.map((c, i) => {
        if (!c || !c.code) return null;
        const code = c.code;
        let rankStr, suit;
        if (code.length === 3) {
            rankStr = code.substring(0, 2);
            suit = code[2];
        } else {
            rankStr = code[0];
            suit = code[1];
        }
        const rankMap = { 'A': 14, 'K': 13, 'Q': 12, 'J': 11 };
        const rank = rankMap[rankStr] || parseInt(rankStr);
        return { rank, suit, index: i };
    }).filter(Boolean);

    if (parsed.length !== 5) return new Set();

    const rankGroups = {};
    parsed.forEach(c => {
        if (!rankGroups[c.rank]) rankGroups[c.rank] = [];
        rankGroups[c.rank].push(c.index);
    });

    const suitGroups = {};
    parsed.forEach(c => {
        if (!suitGroups[c.suit]) suitGroups[c.suit] = [];
        suitGroups[c.suit].push(c);
    });

    const pairs = Object.entries(rankGroups).filter(([, v]) => v.length === 2);
    const trips = Object.entries(rankGroups).filter(([, v]) => v.length === 3);
    const quads = Object.entries(rankGroups).filter(([, v]) => v.length === 4);

    if (quads.length > 0) {
        const hold = new Set(quads[0][1]);
        return hold;
    }

    if (trips.length > 0 && pairs.length > 0) {
        const hold = new Set([...trips[0][1], ...pairs[0][1]]);
        return hold;
    }

    const flushSuit = Object.entries(suitGroups).find(([, v]) => v.length === 5);
    if (flushSuit) {
        return new Set([0, 1, 2, 3, 4]);
    }

    const sortedRanks = parsed.map(c => c.rank).sort((a, b) => a - b);
    const uniqueRanks = [...new Set(sortedRanks)];
    if (uniqueRanks.length === 5) {
        const isStraight = (uniqueRanks[4] - uniqueRanks[0] === 4) ||
            (uniqueRanks[0] === 2 && uniqueRanks[3] === 5 && uniqueRanks[4] === 14);
        if (isStraight) {
            return new Set([0, 1, 2, 3, 4]);
        }
    }

    if (trips.length > 0) {
        return new Set(trips[0][1]);
    }

    if (pairs.length >= 2) {
        const hold = new Set([...pairs[0][1], ...pairs[1][1]]);
        return hold;
    }

    const flush4 = Object.entries(suitGroups).find(([, v]) => v.length === 4);
    if (flush4) {
        return new Set(flush4[1].map(c => c.index));
    }

    function findStraight4(cards) {
        const sorted = [...cards].sort((a, b) => a.rank - b.rank);
        for (let i = 0; i <= sorted.length - 4; i++) {
            const window4 = sorted.slice(i, i + 4);
            const uRanks = [...new Set(window4.map(c => c.rank))];
            if (uRanks.length === 4 && (uRanks[3] - uRanks[0] <= 4)) {
                return new Set(window4.map(c => c.index));
            }
        }
        const hasAce = sorted.find(c => c.rank === 14);
        if (hasAce) {
            const lowCards = sorted.filter(c => c.rank >= 2 && c.rank <= 5);
            if (lowCards.length >= 3) {
                const combo = [hasAce, ...lowCards.slice(0, 3)];
                const uRanks = [...new Set(combo.map(c => c.rank))];
                if (uRanks.length === 4) {
                    return new Set(combo.map(c => c.index));
                }
            }
        }
        return null;
    }

    const straight4 = findStraight4(parsed);
    if (straight4) {
        return straight4;
    }

    if (pairs.length === 1) {
        return new Set(pairs[0][1]);
    }

    return new Set();
}

function applyAutoHold(cardList) {
    const autoHolds = computeAutoHold(cardList);
    if (autoHolds.size === 0) return;

    holdIndexes = autoHolds;
    applyHeldIndexes(Array.from(autoHolds));
}

function applyHeldIndexes(indexes) {
    const slots = $$('.card-slot');
    const holdBtns = $$('.cab-hold');
    indexes.forEach(i => {
        if (slots[i]) slots[i].classList.add('held');
        if (holdBtns[i]) holdBtns[i].classList.add('active');
        if (window.CabinetStage) CabinetStage.setHold(i, true);
    });
}

function applyServerAdvisedHolds(advisedArray) {
    if (!advisedArray || advisedArray.length === 0) return;

    holdIndexes = new Set(advisedArray);
    applyHeldIndexes(advisedArray);
}

function restoreRoundFromSnapshot(snapshot) {
    if (!snapshot) return;

    stopShuffle();
    clearLucky5Effects();
    hideDuInfo();

    const phase = snapshot.phase;
    roundId = snapshot.roundId;
    currentBet = Number(snapshot.betAmount || currentBet);
    cards = Array.isArray(snapshot.cards) ? snapshot.cards : [];
    holdIndexes = new Set(Array.isArray(snapshot.heldIndexes) ? snapshot.heldIndexes : []);
    currentHandRank = snapshot.handRank && snapshot.handRank !== 'Nothing' ? snapshot.handRank : null;
    roundDoubleUpAvailable = Boolean(snapshot.doubleUpAvailable);
    takeHalfUsedThisRound = Boolean(snapshot.takeHalfUsed);
    jackpotRankArmed = false;
    window.jackpotRankArmed = false;
    takeScoreAnimating = false;
    updateStakeDisplay();
    updatePaytable(currentHandRank);
    updateBonusBar(currentHandRank);

    if (phase === 'Dealt') {
        winAmount = 0;
        duSessionStarted = false;
        resetDoubleUpPanelState();
        duDealerCard = null;
        duCardTrail = [];
        duLastRenderedTrailLength = 0;
        gameState = 'hold';
        renderCards(cards, false);
        applyHeldIndexes(Array.from(holdIndexes));
        updateWinIndicator(0);
        updateWinAmountDisplay(0);
        showMessage('ROUND RESTORED - DRAW OR ADJUST');
        setButtonStates();
        return;
    }

    if (phase === 'DoubleUp' && snapshot.doubleUpSession) {
        const duSnapshot = snapshot.doubleUpSession;
        winAmount = Number(duSnapshot.currentAmount || snapshot.pendingWinAmount || 0);
        duSessionStarted = true;
        duDealerCard = duSnapshot.dealerCard;
        syncDoubleUpPanelState(duSnapshot);
        duCardTrail = syncDoubleUpTrailFromServer(duSnapshot.cardTrail, duDealerCard, duCardTrail);
        if ((!Array.isArray(duCardTrail) || duCardTrail.length === 0) && duDealerCard) {
            duCardTrail = [{ card: duDealerCard, label: 'DEALER' }];
        }
        duLastRenderedTrailLength = 0;
        gameState = 'doubleup';
        showDuInfo();
        updateIdleOverlayVisibility();
        renderDoubleUpCards(duDealerCard, true, null, { pending: true });
        updateWinIndicator(winAmount);
        updateWinAmountDisplay(winAmount, getFourOfAKindSlotTag(currentHandRank));
        updatePaytable(currentHandRank);
        if (duIsNoLoseActive) {
            triggerLucky5Flash();
            showMessage(`${getLuckyActiveBannerText()}! DOUBLE UP: ${formatNum(winAmount)}`, 'win');
        } else {
            showMessage(`DOUBLE UP RESTORED - WIN: ${formatNum(winAmount)}`, 'win');
        }
        setButtonStates();
        return;
    }

    winAmount = Number(snapshot.pendingWinAmount || 0);
    duSessionStarted = false;
    resetDoubleUpPanelState();
    duDealerCard = null;
    duCardTrail = [];
    duLastRenderedTrailLength = 0;
    
    if (winAmount > 0) {
        gameState = 'win';
        renderCards(cards, false);
        updateWinIndicator(winAmount);
        updateWinAmountDisplay(winAmount, getFourOfAKindSlotTag(currentHandRank));
        showWinActionMessage();
    } else {
        gameState = 'idle';
        renderCards(cards, false);
        updateWinIndicator(0);
        updateWinAmountDisplay(0);
        showMessage('PLACE YOUR BET');
        setTimeout(() => {
            if (gameState === 'idle') showIdleTitle();
        }, T.postLossIdleTitleMs || 2500);
    }
    
    setButtonStates();
}

// ── 9. ACTIONS ───────────────────────────────────────────────────────────
async function doDeal() {
    if (gameState === 'idle') {
        if (!machineJoined) {
            if (!isHubConnected()) {
                await setupSignalR();
            }
            await joinMachine(machineId);
            if (!machineJoined) {
                console.warn('Machine join unavailable; continuing without realtime sync.');
            }
        }
        if (balance < currentBet * 2) {
            showMessage('NEED ENOUGH CREDITS FOR DEAL + DRAW', 'lose');
            return;
        }
        playPress();
        jackpotRankArmed = false;
        gameState = 'dealing';
        setButtonStates();
        showMessage('DEALING...');
        updateBonusBar(null);
        updateIdleOverlayVisibility();
        updateWinIndicator(0);
        hideDuInfo();
        hideIdleTitle();
        roundDoubleUpAvailable = false;
        takeHalfUsedThisRound = false;
        duSessionStarted = false;
        resetDoubleUpPanelState();
        duDealerCard = null;
        jackpotRankArmed = false;
        window.jackpotRankArmed = false;

        try {
            const result = await apiCall('POST', GAME_CONFIG.api.deal, {
                machineId,
                betAmount: currentBet
            });
            if (!result || !Array.isArray(result.cards)) {
                throw new Error(`Deal response missing cards (keys: ${Object.keys(result || {}).join(',')})`);
            }
            roundId = result.roundId;
            cards = result.cards;
            syncMachineCreditsFromResponse(result);
            if (result.jackpots) updateJackpotDisplay(result.jackpots);
            updateWinAmountDisplay(0);
            holdIndexes.clear();
            $$('.cab-hold').forEach(btn => btn.classList.remove('active'));
            gameState = 'hold';

            const serverHolds = result.advisedHolds;

            renderDealStage(cards, () => {
                if (serverHolds && serverHolds.length > 0) {
                    applyServerAdvisedHolds(serverHolds);
                } else {
                    applyAutoHold(cards);
                }
                setButtonStates();
                if (holdIndexes.size > 0) {
                    showMessage('AUTO HOLD');
                } else {
                    showMessage('PRESS HOLDS TO KEEP CARD');
                }
            });
        } catch (e) {
            showMessage(e.message, 'lose');
            gameState = 'idle';
            setButtonStates();
            showIdleTitle();
            updateIdleOverlayVisibility();
        }
    } else if (gameState === 'hold') {
        if (balance < currentBet) {
            showMessage('NOT ENOUGH MACHINE CREDITS FOR DRAW', 'lose');
            return;
        }
        playPress();
        gameState = 'drawing';
        setButtonStates();
        showMessage('DRAWING...');
        updateIdleOverlayVisibility();

        try {
            const result = await apiCall('POST', GAME_CONFIG.api.draw, {
                roundId,
                holdIndexes: Array.from(holdIndexes)
            });
            if (!result || !Array.isArray(result.cards)) {
                throw new Error(`Draw response missing cards (keys: ${Object.keys(result || {}).join(',')})`);
            }
            cards = result.cards;
            winAmount = result.winAmount;
            syncMachineCreditsFromResponse(result);
            if (result.jackpots) updateJackpotDisplay(result.jackpots);

            renderDrawStage(cards, holdIndexes, () => {
                const handName = result.handRank || 'Nothing';
                currentHandRank = handName !== 'Nothing' ? handName : null;
                roundDoubleUpAvailable = Boolean(result.doubleUpAvailable);
                updatePaytable(currentHandRank);
                handsPlayed++;
                betResetPending = true;
                jackpotRankArmed = false;

                if (winAmount > 0) {
                    const jackpotWon = result.jackpotWon || 0;
                    const finalMachineCredits = result.walletBalanceAfterRound;
                    let msg = `${HAND_DISPLAY[handName] || handName} - WIN ${formatNum(winAmount)}!`;
                    if (jackpotWon > 0) {
                        msg = `${HAND_DISPLAY[handName] || handName} - JACKPOT WON!`;
                    }
                    showMessage(msg, 'win');
                    flashWinCards();
                    updateBonusBar(handName, result.jackpotWon);
                    if (currentHandRank) highlightPaytableDU(currentHandRank, winAmount);
                    updateWinIndicator(winAmount);
                    updateWinAmountDisplay(winAmount, getFourOfAKindSlotTag(handName));
                    gameState = 'win';
                    setButtonStates();
                    updateIdleOverlayVisibility();

                    const proceedToDoubleUp = async () => {
                        if (jackpotWon > 0) {
                            await animateJackpotFill(jackpotWon, balance, handName);
                            if (result.jackpots) updateJackpotDisplay(result.jackpots);
                        }
                        machineSessionClosed = Number(finalMachineCredits) >= MACHINE_CREDIT_LIMIT;
                        if (gameState === 'win') {
                            if (isMachineClosedForUi()) {
                                // Machine closed by this win — auto-siphon the winnings
                                // into credits, then auto cash-out to wallet.
                                showMessage('MACHINE CLOSED — SIPHONING WINNINGS...', 'win');
                                machineSessionClosed = true;
                                const siphonAmount = winAmount;
                                winAmount = 0;
                                const preSiphonCredits = balance - siphonAmount;
                                updateWinIndicator(siphonAmount);
                                updateWinAmountDisplay(siphonAmount, getFourOfAKindSlotTag(handName));
                                (async () => {
                                    await animateDrainToCredits(siphonAmount, preSiphonCredits, handName);
                                    balance = finalMachineCredits;
                                    updateCredits();
                                    syncMachineCreditsFromResponse(result);
                                    try {
                                        await cashOutMachine();
                                        await fetchMachineSession();
                                        refreshIdleMachineState('CASHED OUT — MACHINE READY', 'win');
                                    } catch (_) {
                                        showMessage(getMachineCloseMessage(), 'win');
                                    }
                                })();
                                return;
                            }
                            if (roundDoubleUpAvailable) {
                                startDoubleUpFlow();
                            } else {
                                showWinActionMessage();
                                setButtonStates();
                            }
                        }
                    };

                    proceedToDoubleUp();
                } else {
                    showMessage(HAND_DISPLAY[handName] || 'NO WIN', 'lose');
                    resetDoubleUpAwardState();
                    gameState = 'idle';
                    setButtonStates();
                    updatePaytable();
                    updateWinAmountDisplay(0);
                    setTimeout(() => {
                        if (gameState === 'idle') showIdleTitle();
                    }, T.postLossIdleTitleMs || 2500);
                }
            });
        } catch (e) {
            showMessage(e.message, 'lose');
            gameState = 'idle';
            setButtonStates();
        }
    }
}

function cancelHold() {
    if (gameState !== 'hold') return;
    playPress();
    holdIndexes.clear();
    $$('.card-slot').forEach(s => s.classList.remove('held'));
    $$('.cab-hold').forEach(btn => btn.classList.remove('active'));
    if (hasCabinetStage()) CabinetStage.clearAllHolds();
    
    // Update idle overlay visibility when holds are cleared
    updateIdleOverlayVisibility();
}

function showDuInfo() {
    updateDoubleUpInfoPanel();
    $('#du-info-panel').classList.add('visible');
    if (typeof DuBoardCanvas !== 'undefined') {
        DuBoardCanvas.setState('active', duBoardBonusAmount);
    }
}

function hideDuInfo() {
    const panel = $('#du-info-panel');
    if (!panel) return;
    panel.classList.remove('visible');
    panel.classList.remove('lucky5-active');

    const luckyEl = document.getElementById('du-lucky-info');
    if (luckyEl) luckyEl.classList.remove('is-active');
    
    if (typeof DuBoardCanvas !== 'undefined') {
        DuBoardCanvas.setState('hidden', 0);
    }
}

function renderDoubleUpCards(dealerCard, showShuffle, challengerCard) {
    if (typeof DuBoardCanvas !== 'undefined') {
        DuBoardCanvas.setCards(dealerCard, duCardTrail);
    }
    const stageOptions = arguments.length > 3 ? arguments[3] : null;
    if (hasCabinetStage()) {
        const trailCards = getCabinetDoubleUpTrailCards();
        const trailEntries = getCabinetDoubleUpTrailEntries();
        const cabinetTrail = trailEntries.length > 0 ? trailEntries : trailCards;
        const options = stageOptions || (showShuffle ? { pending: true } : {});
        if (challengerCard) {
            CabinetStage.updateDoubleUpTrail(cabinetTrail, dealerCard, challengerCard, options);
            return;
        }
        if (showShuffle) {
            if (cabinetTrail.length > 0) {
                CabinetStage.updateDoubleUpTrail(cabinetTrail, dealerCard, null, options);
            } else {
                CabinetStage.enterDoubleUp(dealerCard, cabinetTrail);
            }
            return;
        }
        CabinetStage.updateDoubleUpTrail(cabinetTrail, dealerCard, null, options);
        return;
    }

    const area = $('#card-area');
    area.innerHTML = '';
    area.classList.add('du-mode');

    stopShuffle();

    // 5-slot page model with carry-over.
    // Each page shows 4 trail cards + 1 active slot (shuffle or challenger) = 5 visible.
    // The LAST trail card of page N carries over as the FIRST trail card of page N+1,
    // giving players visual continuity across pages (step = maxTrailPerPage - 1 = 3).
    const MAX_TRAIL_PER_PAGE = GAME_CONFIG.doubleUp.maxTrailPerPage; // from game-config.js
    const CARRY_STEP = MAX_TRAIL_PER_PAGE - 1; // carry-over step
    let startIndex = 0;
    if (duCardTrail.length > MAX_TRAIL_PER_PAGE) {
        const overshoot = duCardTrail.length - MAX_TRAIL_PER_PAGE;
        const pages = Math.ceil(overshoot / CARRY_STEP);
        startIndex = pages * CARRY_STEP;
    }
    // isPageBreak: the viewport just advanced to a new page since the last render
    const isPageBreak = startIndex > 0 && duLastRenderedTrailLength <= startIndex;

    // Render visible trail cards (current page only)
    const cardsOnPage = duCardTrail.slice(startIndex);
    cardsOnPage.forEach((entry, pageSlot) => {
        const absIndex = startIndex + pageSlot;
        // Carry-over card (pageSlot=0 on a page break) was already visible — no animation.
        // New cards on the fresh page stagger in, skipping slot 0 (the carry-over).
        const isCarryOver = isPageBreak && pageSlot === 0;
        const isNew = !isCarryOver && (isPageBreak || absIndex >= duLastRenderedTrailLength);
        const staggerMs = isPageBreak && pageSlot > 0 ? (pageSlot - 1) * T.duStaggerPerCardMs : 0;

        const slot = document.createElement('div');
        slot.className = 'du-card-slot du-trail-card' + (isNew ? ' du-new' : '');
        if (isNew && staggerMs > 0) slot.style.setProperty('--du-stagger', staggerMs + 'ms');

        const label = document.createElement('div');
        label.className = 'du-card-label';
        label.textContent = entry.label || '';

        const frame = document.createElement('div');
        frame.className = 'du-card-frame';
        if (pageSlot === cardsOnPage.length - 1) frame.classList.add('dealer-card');
        const isLucky = entry.card && entry.card.code === '5S';
        if (isLucky) frame.classList.add('lucky5-glow');
        frame.innerHTML = `<img src="${cardImagePath(entry.card)}" alt="card">`;

        slot.appendChild(label);
        slot.appendChild(frame);
        area.appendChild(slot);
    });

    // Update tracker so next call knows which cards were already visible
    duLastRenderedTrailLength = duCardTrail.length;

    if (challengerCard) {
        const challSlot = document.createElement('div');
        challSlot.className = 'du-card-slot du-chall-in';
        const challLabel = document.createElement('div');
        challLabel.className = 'du-card-label';
        challLabel.textContent = '';
        const challFrame = document.createElement('div');
        challFrame.className = 'du-card-frame';
        const challLucky = challengerCard.code === '5S';
        if (challLucky) challFrame.classList.add('lucky5-glow');
        challFrame.innerHTML = `<img src="${cardImagePath(challengerCard)}" alt="result">`;
        challSlot.appendChild(challLabel);
        challSlot.appendChild(challFrame);
        area.appendChild(challSlot);
    } else if (showShuffle) {
        const challSlot = document.createElement('div');
        challSlot.className = 'du-card-slot';
        challSlot.id = 'du-shuffle-slot';
        const challLabel = document.createElement('div');
        challLabel.className = 'du-card-label';
        challLabel.textContent = DU_PROMPT_TEXT;
        const challFrame = document.createElement('div');
        challFrame.className = 'du-card-frame';
        challFrame.id = 'du-shuffle-frame';
        challFrame.innerHTML = `<img src="${CARD_BACK_SRC}" alt="card">`;
        challSlot.appendChild(challLabel);
        challSlot.appendChild(challFrame);
        area.appendChild(challSlot);
        startShuffle();
    }
}

// ── 7. ANIMATION HELPERS ────────────────────────────────────────────────
let shuffleRAF = null;
let shuffleLastTime = 0;

function startShuffle() {
    stopShuffle();
    shuffleLastTime = 0;
    const frame = document.getElementById('du-shuffle-frame');
    if (frame) frame.classList.add('du-flip-in');

    function tick(ts) {
        if (ts - shuffleLastTime >= T.shuffleFrameMs) {
            shuffleLastTime = ts;
            const f = document.querySelector('#du-shuffle-frame img');
            if (f) {
                const frame = document.getElementById('du-shuffle-frame');
                if (frame) {
                    frame.classList.remove('du-flip-in');
                    frame.classList.add('du-flip-out');
                    setTimeout(() => {
                        f.src = randomCardSrc();
                        frame.classList.remove('du-flip-out');
                        frame.classList.add('du-flip-in');
                    }, 60);
                }
            }
        }
        shuffleRAF = requestAnimationFrame(tick);
    }
    shuffleRAF = requestAnimationFrame(tick);
}

function stopShuffle() {
    if (shuffleRAF) {
        cancelAnimationFrame(shuffleRAF);
        shuffleRAF = null;
    }
    if (shuffleInterval) {
        clearInterval(shuffleInterval);
        shuffleInterval = null;
    }
}

async function startDoubleUpFlow() {
    if (gameState !== 'win') return;
    if (!roundDoubleUpAvailable || winAmount <= 0) {
        showWinActionMessage();
        setButtonStates();
        return;
    }

    try {
        const result = await apiCall('POST', GAME_CONFIG.api.duStart, { roundId });
        duSessionStarted = true;
        syncDoubleUpPanelState(result);
        duDealerCard = result.dealerCard;
        duCardTrail = syncDoubleUpTrailFromServer(result.cardTrail, duDealerCard, duCardTrail);
        if ((!Array.isArray(duCardTrail) || duCardTrail.length === 0) && duDealerCard) {
            duCardTrail = [{ card: duDealerCard, label: 'DEALER' }];
        }
        duLastRenderedTrailLength = 0;
        gameState = 'doubleup';

        showDuInfo();
        if (duIsNoLoseActive) {
            triggerLucky5Flash();
            showMessage(`${getLuckyActiveBannerText()}! DOUBLE UP: ${formatNum(result.currentAmount)}`, 'win');
        } else {
            showMessage(`DOUBLE UP - WIN: ${formatNum(result.currentAmount)}`, 'win');
        }
        updateWinAmountDisplay(result.currentAmount, getFourOfAKindSlotTag(currentHandRank));
        updateWinIndicator(result.currentAmount);
        updatePaytable(currentHandRank);
        renderDoubleUpCards(duDealerCard, true, null, { pending: true });
        setButtonStates();
    } catch (e) {
        if ((e.message || '').toLowerCase().includes('not available')) {
            roundDoubleUpAvailable = false;
            showWinActionMessage();
            setButtonStates();
            return;
        }

        showMessage(e.message, 'lose');
    }
}

async function doDoubleUp(guess) {
    if (gameState !== 'doubleup') return;
    playPress();
    gameState = 'du-waiting';
    setButtonStates();
    showMessage('FLIPPING...', '');

    // Snap shuffle frame to the card back BEFORE stopping so the loop
    // never freezes on a random intermediate card face.
    // Players only see the actual server-provided card result.
    const shuffleImg = document.querySelector('#du-shuffle-frame img');
    if (shuffleImg) shuffleImg.src = CARD_BACK_SRC;
    stopShuffle();

    try {
        const result = await apiCall('POST', GAME_CONFIG.api.duGuess, { roundId, guess });
        const challengerLabel = String(guess || '').trim().toUpperCase();

        // Reveal phase: show the challenger card and outcome.
        // This delay simulates the physical card flip on old hardware —
        // the player sees the result before anything else happens.
        setTimeout(() => {
            syncDoubleUpPanelState(result, { preserveMultiplier: true });
            renderDoubleUpCards(duDealerCard, false, result.challengerCard, {
                challengerLabel,
                outcome: result.status
            });

            if (result.status === 'Win') {
                winAmount = result.currentAmount;
                updateCredits();
                updateWinIndicator(winAmount);
                updateWinAmountDisplay(winAmount, getFourOfAKindSlotTag(currentHandRank));
                updatePaytable(currentHandRank);

                // Board bonus: if the 5-card DU board forms a paying hand,
                // it pays the base paytable as a bonus. Show it as a prominent
                // overlay that drains into credits separately from the DU doubling.
                if (duHighlightHandRank && duBoardBonusAmount > 0) {
                    showBoardBonusPopup(duHighlightHandRank, duBoardBonusAmount, winAmount);
                } else {
                    showMessage(`WIN! ${formatNum(winAmount)} — DOUBLE AGAIN?`, 'win');
                }
                gameState = 'doubleup';

                // Hold the revealed card visible for a moment (like a real machine),
                // then advance to the next shuffle slot.
                setTimeout(() => {
                    if (gameState === 'doubleup') {
                        duCardTrail.push({ card: result.challengerCard, label: challengerLabel });
                        duDealerCard = result.dealerCard;
                        duCardTrail = syncDoubleUpTrailFromServer(result.cardTrail, duDealerCard, duCardTrail);
                        renderDoubleUpCards(duDealerCard, true, null, { pending: true });
                        syncDoubleUpPanelState(result, { preserveMultiplier: true });
                        updatePaytable(currentHandRank);
                        setButtonStates();
                    }
                }, T.duWinHoldMs);
            } else if (result.status === 'SafeFail') {
                roundDoubleUpAvailable = false;
                triggerLucky5Flash();
                const safeAmount = result.currentAmount;
                const collectHandRank = duHighlightHandRank || currentHandRank;
                // Credits are already settled server-side via FinalizeDoubleUp.
                // Show the protected amount, then animate drain to credits.
                const settledMachineCredits = Number(result.walletBalance ?? balance);
                balance = settledMachineCredits - safeAmount;
                updateCredits();
                updateWinIndicator(safeAmount);
                updateWinAmountDisplay(safeAmount, getFourOfAKindSlotTag(currentHandRank));
                showMessage(`SAFE! 5\u2660 SAVED ${formatNum(safeAmount)}`, 'win');
                stopShuffle();
                hideDuInfo();
                duSessionStarted = false;
                resetDoubleUpPanelState();
                clearLucky5Effects();
                setTimeout(async () => {
                    await animateDrainToCredits(safeAmount, balance, collectHandRank);
                    syncMachineCreditsFromResponse(result);
                    await fetchMachineSession();
                    refreshIdleMachineState();
                }, T.drainDelayMs);
            } else if (result.status === 'MachineClosed') {
                roundDoubleUpAvailable = false;
                const closedAmount = result.currentAmount;
                const collectHandRank = duHighlightHandRank || currentHandRank;
                // The backend returns settled machine credits in WalletBalance.
                // The DU winnings (closedAmount) need to drain from the display into credits.
                const settledMachineCredits = Number(result.walletBalance ?? balance);
                balance = settledMachineCredits - closedAmount;
                updateCredits();
                updateWinIndicator(closedAmount);
                updateWinAmountDisplay(closedAmount, getFourOfAKindSlotTag(currentHandRank));
                showMessage('MACHINE CLOSED — SIPHONING WINNINGS...', 'win');
                stopShuffle();
                hideDuInfo();
                duSessionStarted = false;
                resetDoubleUpPanelState();
                clearLucky5Effects();

                // Auto-siphon: drain the DU winnings into machine credits.
                // No player input needed — the machine does this automatically.
                // After drain completes, auto cash-out to wallet.
                machineSessionClosed = true;
                (async () => {
                    await animateDrainToCredits(closedAmount, balance, collectHandRank);
                    balance = settledMachineCredits;
                    updateCredits();
                    syncMachineCreditsFromResponse(result);
                    try {
                        await cashOutMachine();
                        await fetchMachineSession();
                        refreshIdleMachineState('CASHED OUT — MACHINE READY', 'win');
                    } catch (_) {
                        showMessage(getMachineCloseMessage(), 'win');
                    }
                })();
            } else {
                roundDoubleUpAvailable = false;
                const lossAmount = winAmount;
                winAmount = 0;
                syncMachineCreditsFromResponse(result);
                resetDoubleUpAwardState();
                showMessage('YOU LOSE!', 'lose');
                stopShuffle();
                hideDuInfo();
                duSessionStarted = false;
                resetDoubleUpPanelState();
                clearLucky5Effects();

                // Siphon: show the losing card briefly, then animate the displayed
                // win amount draining back to zero — player watches winnings disappear.
                if (lossAmount > 0) {
                    updateWinIndicator(lossAmount);
                    updateWinAmountDisplay(lossAmount, getFourOfAKindSlotTag(currentHandRank));
                    const collectHandRank = duHighlightHandRank || currentHandRank;
                    const preLossCredits = Math.max(0, balance);
                    updatePaytable(collectHandRank);
                    setTimeout(async () => {
                        await animateReverseDrain(lossAmount, preLossCredits, collectHandRank);
                        updateWinIndicator(0);
                        updateWinAmountDisplay(0);
                        updatePaytable();
                        exitDoubleUp();
                    }, T.duLoseRevealMs);
                } else {
                    updateWinIndicator(0);
                    updateWinAmountDisplay(0);
                    updatePaytable();
                    setTimeout(() => exitDoubleUp(), T.exitDuLoseMs);
                }
            }
            setButtonStates();
        }, T.duRevealDelayMs);
    } catch (e) {
        showMessage(e.message, 'lose');
        setTimeout(() => exitDoubleUp(), T.exitDuCatchMs);
    }
}

/// Shows the board bonus popup when the DU board forms a paying hand.
/// This appears as an overlay after the 5th card lands, showing the bonus
/// hand and amount, then drains into credits.
/// The board bonus pays the BASE PAYTABLE (not the DU doubled amount).
/// It's a separate win on top of the DU doubling.
function showBoardBonusPopup(handRank, bonusAmount, duWinAmount) {
    const handName = HAND_DISPLAY[handRank] || handRank;

    // Show the board bonus overlay prominently
    const bonusEl = document.getElementById('du-board-bonus');
    if (bonusEl) {
        bonusEl.querySelector('.du-board-bonus-hand').textContent = `${handName} BONUS!`;
        bonusEl.querySelector('.du-board-bonus-amount').textContent = `+${formatNum(bonusAmount)}`;
        bonusEl.classList.add('visible');
    }

    // Highlight the matching paytable row
    highlightPaytableDU(handRank, bonusAmount);

    // After the overlay displays, drain the bonus into credits
    setTimeout(() => {
        if (bonusEl) bonusEl.classList.remove('visible');

        // Animate the board bonus draining into credits
        const payRow = document.querySelector(`.pay-row[data-hand="${handRank}"]`);
        const payAmountEl = payRow ? payRow.querySelector('.pay-amount') : null;
        const creditsSpan = document.querySelector('#credits span');
        const msgEl = document.getElementById('game-message');

        const duration = Math.min(3000, Math.max(800, bonusAmount / 500000 * 2000));
        let startTime = null;

        function frame(ts) {
            if (!startTime) startTime = ts;
            const elapsed = ts - startTime;
            const progress = Math.min(elapsed / duration, 1);
            const ease = 1 - Math.pow(1 - progress, 3);
            const credited = Math.floor(bonusAmount * ease);
            const remaining = bonusAmount - credited;

            balance = balance - bonusAmount + credited;
            if (creditsSpan) creditsSpan.textContent = formatNum(balance);
            if (payAmountEl) payAmountEl.textContent = remaining > 0 ? formatNum(remaining) : '0';
            if (msgEl) {
                msgEl.textContent = `BOARD BONUS: ${formatNum(credited)} / ${formatNum(bonusAmount)}`;
                msgEl.className = 'win';
            }

            if (progress < 1) {
                requestAnimationFrame(frame);
            } else {
                syncMachineCreditsFromResponse({ machineCredits: balance });
                updateWinIndicator(duWinAmount);
                updateWinAmountDisplay(duWinAmount, getFourOfAKindSlotTag(currentHandRank));
                showMessage(`WIN ${formatNum(duWinAmount)} + ${formatNum(bonusAmount)} BOARD BONUS`, 'win');
            }
        }

        requestAnimationFrame(frame);
    }, 2500);
}

function exitDoubleUp() {
    stopShuffle();
    hideDuInfo();
    if (hasCabinetStage()) CabinetStage.exitDoubleUp();
    duSessionStarted = false;
    resetDoubleUpPanelState();
    duDealerCard = null;
    duCardTrail = [];
    duLastRenderedTrailLength = 0;
    clearLucky5Effects();
    updateWinAmountDisplay(0);

    if (winAmount > 0) {
        gameState = 'win';
        updatePaytable(currentHandRank);
        setButtonStates();
        showWinActionMessage();
        showIdleTitle();
    } else {
        roundDoubleUpAvailable = false;
        takeHalfUsedThisRound = false;
        currentHandRank = null;
        gameState = 'idle';
        setButtonStates();
        updatePaytable();
        updateBonusBar(null);
        updateWinIndicator(0);
        showMessage('PLACE YOUR BET');
        showIdleTitle();
    }
}

function animateJackpotFill(amount, startBalance, handName) {
    return new Promise((resolve) => {
        // Jackpot drain: same scaling as animateDrainToCredits.
        // 40M → ~60s. The player watches the jackpot counter drain into their credits.
        const duration = Math.min(T.jackpotFillMaxMs, Math.max(T.jackpotFillMinMs, amount / 1_000_000 * 1500));
        const creditsSpan = $('#credits span');
        const winEl = $('#win-indicator');
        const msgEl = $('#game-message');
        let counterEl = null;
        let resetValue = 0;

        if (handName === 'FullHouse') {
            counterEl = document.querySelector('#jp-counter-fh .jp-cval');
        } else if (handName === 'FourOfAKind') {
            // slot 0 = counter-a, slot 1 = counter-b
            counterEl = document.querySelector(
                active4kSlot === 0 ? '#jp-counter-a .jp-cval' : '#jp-counter-b .jp-cval'
            );
        } else if (handName === 'StraightFlush') {
            counterEl = document.querySelector('#jp-counter-center .jp-cval');
        }
        resetValue = JACKPOT_RESET[handName] || 0;

        // Pre-win counter value equals the full amount won (entire jackpot is awarded).
        const jackpotStart = amount;
        let startTime = null;

        function frame(ts) {
            if (!startTime) startTime = ts;
            const progress = Math.min((ts - startTime) / duration, 1);
            const credited = Math.floor(amount * progress);
            balance = startBalance + credited;
            if (creditsSpan) creditsSpan.textContent = formatNum(balance);
            if (winEl) winEl.textContent = `JACKPOT ${formatNum(amount - credited)}`;
            if (msgEl) {
                msgEl.textContent = `JACKPOT TRANSFER ${formatNum(credited)} / ${formatNum(amount)}`;
                msgEl.className = 'win';
            }
            if (counterEl) {
                const current = Math.max(resetValue, jackpotStart - credited);
                counterEl.textContent = formatNum(current);
            }
            if (progress < 1) {
                requestAnimationFrame(frame);
            } else {
                if (winEl) winEl.textContent = '';
                resolve();
            }
        }

        requestAnimationFrame(frame);
    });
}

function animateDrainToCredits(amount, startBalance, handRank = null) {
    return new Promise((resolve) => {
        takeScoreAnimating = true;
        setButtonStates();

        // Duration scales with amount: ~1.5s at 500K, ~60s at 40M.
        // Formula: amount / 1_000_000 * 1500, clamped to [countUpMinMs, countUpMaxMs].
        const totalDuration = Math.min(T.countUpMaxMs, Math.max(T.countUpMinMs, amount / 1_000_000 * 1500));
        const creditsEl = $('#credits');
        const creditsSpan = $('#credits span');
        const winEl = $('#win-indicator');
        const winAmountEl = $('#win-amount-value');
        const msgEl = $('#game-message');
        const payRow = handRank ? document.querySelector(`.pay-row[data-hand="${handRank}"]`) : null;
        const payAmountEl = payRow ? payRow.querySelector('.pay-amount') : null;
        let startTime = null;
        let lastTickToggle = 0;

        if (payRow) {
            payRow.classList.remove('active');
            payRow.classList.add('du-highlight');
        }

        function frame(ts) {
            if (!startTime) startTime = ts;
            const elapsed = ts - startTime;
            const rawProgress = Math.min(elapsed / totalDuration, 1);
            // ease-out cubic so the drain starts fast and slows as it finishes
            const ease = 1 - Math.pow(1 - rawProgress, 3);
            const drained = Math.floor(amount * ease);
            const remaining = amount - drained;

            // Credits count UP (collecting winnings into balance)
            balance = startBalance + drained;
            if (creditsSpan) creditsSpan.textContent = formatNum(balance);

            // Win amount display counts DOWN
            if (winAmountEl) winAmountEl.textContent = remaining > 0 ? formatNum(remaining) : '';
            if (payAmountEl) payAmountEl.textContent = remaining > 0 ? formatNum(remaining) : '0';

            if (ts - lastTickToggle > T.creditTickMs) {
                lastTickToggle = ts;
                creditsEl.classList.toggle('credit-ticking');
                if (payRow) {
                    payRow.classList.toggle('du-highlight');
                }
            }

            if (remaining > 0) {
                if (winEl) winEl.textContent = `WIN ${formatNum(remaining)}`;
            } else {
                if (winEl) winEl.textContent = '';
            }

            if (msgEl) {
                msgEl.textContent = `COLLECTING...`;
                msgEl.className = 'win';
            }

            if (rawProgress < 1) {
                requestAnimationFrame(frame);
            } else {
                balance = startBalance + amount;
                updateCredits();
                if (winEl) winEl.textContent = '';
                if (winAmountEl) winAmountEl.textContent = '';
                creditsEl.classList.remove('credit-ticking');
                if (payRow) payRow.classList.remove('du-highlight');
                takeScoreAnimating = false;
                updatePaytable();
                resolve();
            }
        }

        requestAnimationFrame(frame);
    });
}

/// Reverse drain: siphons displayed WIN amount back to zero.
/// Base CREDITS meter remains UNCHANGED — only the WIN display counts down.
/// Used for DU loss siphon — the player watches their winnings disappear.
function animateReverseDrain(amount, startBalance, handRank = null) {
    return new Promise((resolve) => {
        takeScoreAnimating = true;
        setButtonStates();

        // Same duration formula as animateDrainToCredits
        const totalDuration = Math.min(T.countUpMaxMs, Math.max(T.countUpMinMs, amount / 1_000_000 * 1500));
        const winEl = $('#win-indicator');
        const winAmountEl = $('#win-amount-value');
        const msgEl = $('#game-message');
        const payRow = handRank ? document.querySelector(`.pay-row[data-hand="${handRank}"]`) : null;
        const payAmountEl = payRow ? payRow.querySelector('.pay-amount') : null;
        let startTime = null;
        let lastTickToggle = 0;

        if (payRow) {
            payRow.classList.remove('active');
            payRow.classList.add('du-highlight');
        }

        function frame(ts) {
            if (!startTime) startTime = ts;
            const elapsed = ts - startTime;
            const rawProgress = Math.min(elapsed / totalDuration, 1);
            const ease = 1 - Math.pow(1 - rawProgress, 3);
            const drained = Math.floor(amount * ease);
            const remaining = amount - drained;

            // Base CREDITS meter remains UNCHANGED during loss siphon
            // Do NOT update creditsSpan — it stays perfectly stable

            // Win amount display counts DOWN simultaneously
            if (winAmountEl) winAmountEl.textContent = remaining > 0 ? formatNum(remaining) : '';
            if (payAmountEl) payAmountEl.textContent = remaining > 0 ? formatNum(remaining) : '0';

            if (remaining > 0) {
                if (winEl) winEl.textContent = `LOSE ${formatNum(remaining)}`;
            } else {
                if (winEl) winEl.textContent = '';
            }

            if (msgEl) {
                msgEl.textContent = `SIPHONING...`;
                msgEl.className = 'lose';
            }

            if (rawProgress < 1) {
                requestAnimationFrame(frame);
            } else {
                // Restore balance to original startBalance (no subtraction)
                balance = startBalance;
                updateCredits();
                if (winEl) winEl.textContent = '';
                if (winAmountEl) winAmountEl.textContent = '';
                takeScoreAnimating = false;
                updatePaytable();
                resolve();
            }
        }

        requestAnimationFrame(frame);
    });
}

async function mainTakeScore() {
    if (!(gameState === 'win' || gameState === 'doubleup') || takeScoreAnimating) return;
    playPress();
    stopShuffle();
    const collectHandRank = gameState === 'doubleup'
        ? (duHighlightHandRank || currentHandRank)
        : currentHandRank;
    hideDuInfo();
    duSessionStarted = false;
    roundDoubleUpAvailable = false;
    takeHalfUsedThisRound = false;
    resetDoubleUpPanelState();
    clearLucky5Effects();

    const amount = winAmount;
    winAmount = 0;

    gameState = 'idle';
    updatePaytable();
    updateBonusBar(null);
    updateWinAmountDisplay(0);
    updateWinIndicator(0);
    setButtonStates();
    showIdleTitle();
    updateIdleOverlayVisibility();

    let machineClosed = false;
    try {
        const result = await apiCall('POST', GAME_CONFIG.api.duCashout, { roundId });
        const cashoutAmount = result.currentAmount;

        await animateDrainToCredits(cashoutAmount, balance, collectHandRank);
        syncMachineCreditsFromResponse(result);

        if (result.status === 'MachineClosed') {
            machineClosed = true;
            showMessage(getMachineCloseMessage('cashing-out'), 'win');
            try {
                await cashOutMachine();
                await fetchMachineSession();
                refreshIdleMachineState('CASHED OUT - MACHINE READY', 'win');
            } catch (_) {
                showMessage(getMachineCloseMessage(), 'win');
            }
        }
    } catch (e) {
        balance += amount;
        updateCredits();
    }

    if (!machineClosed) {
        await fetchMachineSession();
        refreshIdleMachineState();
    }
}

async function mainTakeHalf() {
    if (!(gameState === 'win' || gameState === 'doubleup') || takeScoreAnimating) return;
    playPress();

    const wasInDoubleUp = gameState === 'doubleup';

    try {
        const result = await apiCall('POST', GAME_CONFIG.api.duTakeHalf, { roundId });

        syncMachineCreditsFromResponse(result);
        winAmount = result.currentAmount;
        takeHalfUsedThisRound = true;
        updateWinIndicator(winAmount);

        // If take-half pushed credits to/above the close threshold, the backend
        // returns status "MachineClosed". The half is already credited, the
        // remainder stays in the DU session, and the player must continue DU
        // or cash out — no new deals are allowed.
        if (result.status === 'MachineClosed') {
            machineSessionClosed = true;
            showMessage(getMachineClosedMessage('take-score'), 'win');
        }

        if (winAmount <= 0) {
            stopShuffle();
            hideDuInfo();
            duSessionStarted = false;
            resetDoubleUpPanelState();
            clearLucky5Effects();
            currentHandRank = null;
            gameState = 'idle';
            setButtonStates();
            updatePaytable();
            updateBonusBar(null);
            updateWinAmountDisplay(0);
            showMessage(machineSessionClosed ? getMachineClosedMessage('take-score') : 'PLACE YOUR BET');
            showIdleTitle();
            updateIdleOverlayVisibility();
        } else {
            const msg = machineSessionClosed
                ? `${formatNum(winAmount)} REMAINS - MACHINE CLOSED - DOUBLE UP OR CASH OUT`
                : `${formatNum(winAmount)} REMAINS - DOUBLE UP!`;
            showMessage(msg, 'win');
            updatePaytable(currentHandRank);

            if (wasInDoubleUp && duSessionStarted) {
                gameState = 'doubleup';
                setButtonStates();
            } else {
                gameState = 'win';
                setButtonStates();
                if (!machineSessionClosed) {
                    setTimeout(() => {
                        if (gameState === 'win') {
                            if (roundDoubleUpAvailable) {
                                startDoubleUpFlow();
                            } else {
                                showWinActionMessage();
                                setButtonStates();
                            }
                        }
                    }, T.takeHalfContinueMs);
                }
            }
        }
    } catch (e) {
        showMessage(e.message, 'lose');
    }
}

function duTakeScore() {
    mainTakeScore();
}

function duTakeHalf() {
    mainTakeHalf();
}

async function doLogin(username, password) {
    const res = await fetch(`${API}/api/Auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password })
    });
    const json = await res.json();
    if (!res.ok || json.status === 'error') {
        throw new Error(json.message || 'Login failed');
    }
    return json.data;
}

async function doSignup(username, password) {
    const res = await fetch(`${API}/api/Auth/signup`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password, phoneNumber: '0000000000' })
    });
    const json = await res.json();
    if (!res.ok || json.status === 'error') {
        throw new Error(json.message || 'Signup failed');
    }
    return json.data;
}

async function doVerifyOtp(username, otpCode) {
    const res = await fetch(`${API}/api/Auth/verify-otp`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, otpCode })
    });
    const json = await res.json();
    if (!res.ok || json?.success === false) {
        throw new Error(json?.message || 'OTP verification failed');
    }
}

function storeToken(t) {
    token = t;
    sessionStorage.setItem('lucky5_token', t);
}

function storeUserInfo(username, role) {
    currentUsername = username;
    currentRole = normalizeRole(role);
    sessionStorage.setItem('lucky5_username', currentUsername);
    sessionStorage.setItem('lucky5_role', currentRole);
}

function clearToken() {
    token = null;
    currentUsername = '';
    currentRole = 'player';
    sessionStorage.removeItem('lucky5_token');
    sessionStorage.removeItem('lucky5_username');
    sessionStorage.removeItem('lucky5_role');
    sessionStorage.removeItem('lucky5_machineId');
}

async function setupSignalR() {
    if (!token) return;
    if (typeof signalR === 'undefined' || !signalR?.HubConnectionBuilder) {
        console.warn('SignalR client unavailable; continuing without realtime machine sync.');
        hubConnection = null;
        machineJoined = false;
        return;
    }
    if (hubConnection) {
        try { await hubConnection.stop(); } catch (_) {}
    }
    hubConnection = new signalR.HubConnectionBuilder()
        .withUrl(`${API}/CarrePokerGameHub`, { accessTokenFactory: () => token })
        .withAutomaticReconnect()
        .build();

    hubConnection.on('MachineStateUpdated', (state) => {
        if (state && state.jackpots) {
            updateJackpotDisplay(state.jackpots);
        }
    });

    hubConnection.on('Error', (err) => {
        console.error('SignalR error:', err);
    });

    hubConnection.onreconnected(async () => {
        if (machineId > 0) {
            try { await hubConnection.invoke('JoinMachine', machineId); } catch (_) {}
        }
    });

    try {
        await hubConnection.start();
    } catch (e) {
        console.error('SignalR connection failed:', e);
        machineJoined = false;
        try { await hubConnection.stop(); } catch (_) {}
        hubConnection = null;
    }
}

function isHubConnected() {
    if (!hubConnection) return false;
    if (typeof signalR !== 'undefined' && signalR.HubConnectionState) {
        return hubConnection.state === signalR.HubConnectionState.Connected;
    }
    return hubConnection.state === 'Connected';
}

async function joinMachine(id) {
    if (!isHubConnected()) return;
    try {
        await hubConnection.invoke('JoinMachine', id);
        machineJoined = true;
    } catch (e) {
        console.error('JoinMachine failed:', e);
        machineJoined = false;
    }
}

async function leaveMachine(id) {
    if (!isHubConnected()) return;
    try {
        await hubConnection.invoke('LeaveMachine', id);
        machineJoined = false;
    } catch (_) {}
}

async function doLogout() {
    if (machineJoined && machineId > 0) {
        await leaveMachine(machineId);
    }
    if (hubConnection) {
        try { await hubConnection.stop(); } catch (_) {}
        hubConnection = null;
    }
    setMenuPanelOpen(false);
    clearToken();
    resetGameRuntimeState({ clearSelection: true });
    balance = 0;
    walletBalance = 0;
    setActiveScreen(null);
    $('#auth-screen').style.display = '';
    $('#auth-error').textContent = '';
}

async function addDemoCredits() {
    return Promise.resolve();
}

// Available games will be populated from backend machines
let AVAILABLE_GAMES = [];

// ── 10. SHELL / LOBBY ──────────────────────────────────────────────────
async function loadAvailableMachines() {
    try {
        const machineData = await apiCall('GET', GAME_CONFIG.api.machines);
        // Convert machines to game cards
        AVAILABLE_GAMES = machineData.map(machine => ({
            id: `machine-${machine.id}`,
            machineId: machine.id,
            name: machine.name.toUpperCase(),
            icon: '/assets/images/lucky5.png',
            status: machine.isOpen ? 'playable' : 'unavailable',
            minBet: machine.minBet,
            maxBet: machine.maxBet
        }));
        return AVAILABLE_GAMES;
    } catch (e) {
        console.error('Failed to load machines:', e);
        // Fallback to single game if API fails
        AVAILABLE_GAMES = [{
            id: 'machine-1',
            machineId: 1,
            name: 'LUCKY 5',
            icon: '/assets/images/lucky5.png',
            status: 'playable'
        }];
        return AVAILABLE_GAMES;
    }
}


function updateLobbyBalance() {
    const fmt = formatNum(walletBalance);
    const lobbyBal = document.getElementById('lobby-balance');
    const lobbyWalBal = document.getElementById('lobby-wallet-bal');
    const walletBal = document.getElementById('wallet-balance');
    if (lobbyBal) lobbyBal.textContent = fmt;
    if (lobbyWalBal) lobbyWalBal.textContent = fmt;
    if (walletBal) walletBal.textContent = fmt;
    if (window.CabinetShell) CabinetShell.updateLobbyBalance(walletBalance);
}

function updateLobbyUsername() {
    const el = document.getElementById('lobby-username');
    if (el) el.textContent = currentUsername.toUpperCase() || 'PLAYER';
    const navAdmin = document.getElementById('nav-admin');
    if (navAdmin) navAdmin.style.display = currentRole === 'admin' ? '' : 'none';
    const resetBtn = document.getElementById('btn-reset-machine');
    if (resetBtn) resetBtn.style.display = currentRole === 'admin' ? '' : 'none';
}

function renderGameGrid() {
    if (window.CabinetShell) {
        const rawMachines = AVAILABLE_GAMES.map(g => ({
            id: g.machineId,
            name: g.name,
            minBet: g.minBet,
            maxBet: g.maxBet,
            isOpen: g.status === 'playable'
        }));
        CabinetShell.renderLobbyMachineCards(rawMachines, machine => openGame(`machine-${machine.id}`, machine.id));
        return;
    }

    const grid = document.getElementById('lobby-game-grid');
    if (!grid) return;
    grid.innerHTML = '';

    AVAILABLE_GAMES.forEach(game => {
        const card = document.createElement('div');
        card.className = 'game-card' + (game.status !== 'playable' ? ' unavailable' : '');

        const iconDiv = document.createElement('div');
        iconDiv.className = 'game-card-icon';
        if (game.icon) {
            iconDiv.innerHTML = `<img src="${game.icon}" alt="${game.name}">`;
        } else {
            iconDiv.innerHTML = game.iconText || '?';
        }

        const nameDiv = document.createElement('div');
        nameDiv.className = 'game-card-name';
        nameDiv.textContent = game.name;

        // Show bet range if available
        const betInfo = document.createElement('div');
        betInfo.className = 'game-card-bet-info';
        betInfo.style.fontSize = '8px';
        betInfo.style.color = '#888';
        betInfo.style.marginTop = '4px';
        if (game.minBet && game.maxBet) {
            betInfo.textContent = `BET: ${formatNum(game.minBet)} - ${formatNum(game.maxBet)}`;
        }

        const badge = document.createElement('div');
        badge.className = 'game-card-badge ' + (game.status === 'playable' ? 'playable' : 'coming-soon');
        badge.textContent = game.status === 'playable' ? 'PLAY NOW' : game.status === 'unavailable' ? 'CLOSED' : 'COMING SOON';

        card.appendChild(iconDiv);
        card.appendChild(nameDiv);
        if (game.minBet && game.maxBet) {
            card.appendChild(betInfo);
        }
        card.appendChild(badge);

        if (game.status === 'playable') {
            card.addEventListener('click', () => openGame(game.id, game.machineId));
        }

        grid.appendChild(card);
    });
}

async function openGame(gameId, selectedMachineId, options = {}) {
    // If a machine ID was provided, set it as the current machine
    if (selectedMachineId) {
        machineId = selectedMachineId;
    }
    sessionStorage.setItem('lucky5_machineId', machineId);

    // All our games are Lucky 5 machines, so just open the game screen
    if (gameId.startsWith('machine-')) {
        activateShellScreen('game', null);
        await initGame(options);
    }
}

async function showLobby() {
    activateShellScreen('lobby', 'lobby');
    updateLobbyBalance();
    updateLobbyUsername();
    if (window.CabinetBonus) CabinetBonus.checkAndShowBanner();
    // Load machines from backend before rendering
    await loadAvailableMachines();
    renderGameGrid();
}

function showWallet() {
    activateShellScreen('wallet', 'wallet');
    updateLobbyBalance();
    loadWalletHistory();
}

async function loadWalletHistory() {
    const list = document.getElementById('wallet-history-list');
    if (!list) return;

    try {
        const history = await apiCall('GET', GAME_CONFIG.api.memberHistory);
        if (!history || history.length === 0) {
            list.innerHTML = '<div class="wallet-history-empty">NO TRANSACTIONS YET</div>';
            return;
        }

        const recent = history.slice(0, 50);
        list.innerHTML = '';
        recent.forEach(entry => {
            const row = document.createElement('div');
            row.className = 'wallet-history-row';

            const info = document.createElement('div');
            info.className = 'wallet-history-info';

            const typeEl = document.createElement('div');
            typeEl.className = 'wallet-history-type';
            typeEl.textContent = formatTransactionType(entry.type);

            const dateEl = document.createElement('div');
            dateEl.className = 'wallet-history-date';
            dateEl.textContent = formatTransactionDate(entry.createdUtc);

            info.appendChild(typeEl);
            info.appendChild(dateEl);

            const amountEl = document.createElement('div');
            amountEl.className = 'wallet-history-amount ' + (entry.amount >= 0 ? 'positive' : 'negative');
            amountEl.textContent = (entry.amount >= 0 ? '+' : '') + formatNum(entry.amount);

            row.appendChild(info);
            row.appendChild(amountEl);
            list.appendChild(row);
        });
    } catch (e) {
        list.innerHTML = '<div class="wallet-history-empty">FAILED TO LOAD HISTORY</div>';
    }
}

function formatTransactionType(type) {
    if (!type) return 'UNKNOWN';
    const map = {
        'Bet': 'BET',
        'Win': 'WIN',
        'TransferBalance': 'TRANSFER',
        'MoveWinToBalance': 'WIN COLLECT',
        'UpdateCredit': 'CREDIT UPDATE',
        'DoubleUpWin': 'DOUBLE UP WIN',
        'DoubleUpLoss': 'DOUBLE UP LOSS',
        'JackpotWin': 'JACKPOT',
        'Cashout': 'CASHOUT',
        'TakeHalf': 'TAKE HALF',
        'MachineCashIn': 'MACHINE CASH IN',
        'MachineCashOut': 'MACHINE CASH OUT',
        'AdminCredit': 'ADMIN CREDIT',
        'AdminDebit': 'ADMIN DEBIT',
        'AdminMachineReset': 'ADMIN RESET'
    };
    return map[type] || type.toUpperCase();
}

function formatTransactionDate(utcStr) {
    try {
        const d = new Date(utcStr);
        return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    } catch {
        return utcStr;
    }
}

function showAdmin() {
    if (currentRole !== 'admin') return;
    activateShellScreen('admin', 'admin');
    loadAdminUsers();
    loadAdminAgents();
    loadAdminMachines();
}

async function loadAdminUsers(query = '') {
    const wrap = document.getElementById('admin-users-list');
    if (!wrap) return;
    wrap.innerHTML = '<div class="wallet-history-empty">LOADING USERS...</div>';
    try {
        adminUsers = query
            ? await apiCall('GET', GAME_CONFIG.api.adminUserSearch(query))
            : await apiCall('GET', GAME_CONFIG.api.adminUsers);
        if (!adminUsers.length) {
            wrap.innerHTML = '<div class="wallet-history-empty">NO USERS FOUND</div>';
            return;
        }
        wrap.innerHTML = '';
        adminUsers.forEach(user => {
            const row = document.createElement('div');
            row.className = 'wallet-history-row';
            row.innerHTML = `
                <div class="wallet-history-info">
                    <div class="wallet-history-type">${user.username.toUpperCase()} • ${(user.role || 'player').toUpperCase()}</div>
                    <div class="wallet-history-date">${formatNum(user.walletBalance)} • ${formatTransactionDate(user.lastSeenUtc)}</div>
                </div>
                <div style="display:flex;gap:6px;flex-wrap:wrap;justify-content:flex-end;">
                    <button class="lobby-btn lobby-btn-sm" data-credit="${user.userId}">+CREDIT</button>
                    <button class="lobby-btn lobby-btn-sm" data-debit="${user.userId}">-DEBIT</button>
                </div>
            `;
            wrap.appendChild(row);
        });
        wrap.querySelectorAll('[data-credit]').forEach(btn => btn.addEventListener('click', () => adminAdjustWallet(btn.dataset.credit, false)));
        wrap.querySelectorAll('[data-debit]').forEach(btn => btn.addEventListener('click', () => adminAdjustWallet(btn.dataset.debit, true)));
    } catch (e) {
        wrap.innerHTML = `<div class="wallet-history-empty">${e.message}</div>`;
    }
}

async function adminAdjustWallet(userId, isDebit) {
    const amountRaw = prompt(isDebit ? 'Debit amount:' : 'Credit amount:', '200000');
    if (!amountRaw) return;
    const amount = Number(amountRaw);
    if (!amount || amount <= 0) return;
    const reason = prompt('Reason / note:', isDebit ? 'manual debit' : 'manual credit');
    if (!reason) return;
    try {
        await apiCall('POST', GAME_CONFIG.api.adminCredit, {
            targetUserId: userId,
            amount: isDebit ? -amount : amount,
            reason
        });
        loadAdminUsers(document.getElementById('admin-user-search')?.value || '');
        const profile = await apiCall('GET', GAME_CONFIG.api.profile);
        walletBalance = profile.walletBalance;
        updateLobbyBalance();
    } catch (e) {
        alert('Failed: ' + e.message);
    }
}

async function loadAdminAgents() {
    const wrap = document.getElementById('admin-agents-list');
    if (!wrap) return;
    wrap.innerHTML = '<div class="wallet-history-empty">LOADING AGENTS...</div>';
    try {
        adminAgents = await apiCall('GET', GAME_CONFIG.api.agents);
        if (!adminAgents.length) {
            wrap.innerHTML = '<div class="wallet-history-empty">NO AGENTS FOUND</div>';
            return;
        }
        wrap.innerHTML = '';
        adminAgents.forEach(agent => {
            const row = document.createElement('div');
            row.className = 'wallet-history-row';
            row.innerHTML = `
                <div class="wallet-history-info">
                    <div class="wallet-history-type">${(agent.name || 'AGENT').toUpperCase()} • ${String(agent.code || '').toUpperCase()}</div>
                    <div class="wallet-history-date">${agent.phoneNumber || 'NO PHONE'} • POOL ${formatNum(agent.creditPool || 0)}</div>
                    <div class="wallet-history-date">${agent.isActive ? 'ACTIVE' : 'INACTIVE'} • CREATED ${formatTransactionDate(agent.createdUtc)}</div>
                </div>
                <div style="display:flex;gap:6px;flex-wrap:wrap;justify-content:flex-end;">
                    <button class="lobby-btn lobby-btn-sm" data-agent-credit="${agent.id}">LOAD</button>
                    <button class="lobby-btn lobby-btn-sm" data-agent-assign="${agent.id}">ASSIGN USER</button>
                </div>
            `;
            wrap.appendChild(row);
        });
        wrap.querySelectorAll('[data-agent-credit]').forEach(btn => btn.addEventListener('click', () => loadCreditForAgent(btn.dataset.agentCredit)));
        wrap.querySelectorAll('[data-agent-assign]').forEach(btn => btn.addEventListener('click', () => assignUserToAgent(btn.dataset.agentAssign)));
    } catch (e) {
        wrap.innerHTML = `<div class="wallet-history-empty">${e.message}</div>`;
    }
}

async function createAdminAgent() {
    const nameInput = document.getElementById('admin-agent-name');
    const codeInput = document.getElementById('admin-agent-code');
    const phoneInput = document.getElementById('admin-agent-phone');
    const name = nameInput?.value.trim();
    const code = codeInput?.value.trim();
    const phoneNumber = phoneInput?.value.trim();
    if (!name || !code || !phoneNumber) {
        alert('Agent name, code, and phone are required.');
        return;
    }
    try {
        await apiCall('POST', GAME_CONFIG.api.agents, { name, code, phoneNumber });
        if (nameInput) nameInput.value = '';
        if (codeInput) codeInput.value = '';
        if (phoneInput) phoneInput.value = '';
        await loadAdminAgents();
    } catch (e) {
        alert('Failed: ' + e.message);
    }
}

async function loadCreditForAgent(agentId) {
    const amountRaw = prompt('Agent credit amount:', '200000');
    if (!amountRaw) return;
    const amount = Number(amountRaw);
    if (!amount || amount <= 0) return;
    try {
        await apiCall('POST', GAME_CONFIG.api.agentLoadCredit(agentId), { amount });
        await loadAdminAgents();
    } catch (e) {
        alert('Failed: ' + e.message);
    }
}

async function assignUserToAgent(agentId) {
    const userId = prompt('User ID to assign to this agent:');
    if (!userId) return;
    try {
        await apiCall('POST', GAME_CONFIG.api.agentAssignUser(agentId, userId.trim()));
        await loadAdminAgents();
        await loadAdminUsers(document.getElementById('admin-user-search')?.value || '');
    } catch (e) {
        alert('Failed: ' + e.message);
    }
}

async function loadAdminMachines() {
    const wrap = document.getElementById('admin-machines-list');
    if (!wrap) return;
    wrap.innerHTML = '<div class="wallet-history-empty">LOADING MACHINES...</div>';
    try {
        adminMachines = await apiCall('GET', GAME_CONFIG.api.adminMachines);
        if (!adminMachines.length) {
            wrap.innerHTML = '<div class="wallet-history-empty">NO MACHINES FOUND</div>';
            return;
        }
        wrap.innerHTML = '';
        adminMachines.forEach(machine => {
            const row = document.createElement('div');
            row.className = 'wallet-history-row';
            const obsRtp = (Number(machine.observedRtp || 0) * 100).toFixed(2);
            const tgtRtp = (Number(machine.targetRtp || 0) * 100).toFixed(2);
            const sessionsHtml = (machine.sessions || []).map(s =>
                `<div class="wallet-history-date">\u25B6 ${(s.username || 'unknown').toUpperCase()} \u2022 ${formatNum(s.machineCredits)} CR \u2022 IN ${formatNum(s.totalCashIn)}${s.isMachineClosed ? ' \u2022 CLOSED' : ''}</div>`
            ).join('');
            row.innerHTML = `
                <div class="wallet-history-info">
                    <div class="wallet-history-type">#${machine.machineId} ${machine.name || 'MACHINE'}</div>
                    <div class="wallet-history-date">RTP ${obsRtp}% / TGT ${tgtRtp}% / ${machine.phase || 'N/A'}</div>
                    <div class="wallet-history-date">NET ${formatNum(machine.netSinceLastClose || 0)}</div>
                    <div class="wallet-history-date">5\u2660 DROUGHT ${machine.roundsSinceLucky5Hit || 0} \u2022 LIVE ${machine.activeRounds || 0}</div>
                    <div class="wallet-history-date">FH ${formatNum(machine.jackpotFullHouse || 0)} (${RANK_NAMES[machine.jackpotFullHouseRank] || 'A'})</div>
                    <div class="wallet-history-date">4K-A ${formatNum(machine.jackpotFourOfAKindA || 0)} / 4K-B ${formatNum(machine.jackpotFourOfAKindB || 0)}</div>
                    <div class="wallet-history-date">SF ${formatNum(machine.jackpotStraightFlush || 0)}</div>
                    ${sessionsHtml}
                </div>
                <div style="display:flex;align-items:flex-start;">
                    <button class="lobby-btn lobby-btn-sm" data-reset-machine="${machine.machineId}">RESET</button>
                </div>
            `;
            wrap.appendChild(row);
        });
        wrap.querySelectorAll('[data-reset-machine]').forEach(btn => btn.addEventListener('click', async () => {
            if (!confirm(`Reset machine ${btn.dataset.resetMachine}? Active rounds must be empty.`)) return;
            try {
                await apiCall('POST', getAdminMachineResetPath(btn.dataset.resetMachine));
                await loadAdminMachines();
            } catch (e) {
                alert(e.message);
            }
        }));
    } catch (e) {
        wrap.innerHTML = `<div class="wallet-history-empty">${e.message}</div>`;
    }
}

async function backToLobbyFromGame() {
    if (gameState !== 'idle' && gameState !== 'win') {
        if (!confirm('Leave the game? Any current round may be affected.')) return;
    }
    const previousMachineId = machineId;
    setMenuPanelOpen(false);
    if (machineJoined && previousMachineId > 0) {
        await leaveMachine(previousMachineId);
    }
    resetGameRuntimeState({ clearSelection: true });
    await showLobby();
}

async function enterLobbyAfterLogin(profileData) {
    walletBalance = profileData.walletBalance;
    storeUserInfo(profileData.username, profileData.role);
    $('#auth-screen').style.display = 'none';
    if (window.CabinetFirebase) {
        try {
            const cfg = await apiCall('GET', '/api/config/firebase');
            if (cfg && cfg.configured && cfg.config) {
                window.LUCKY5_FIREBASE_CONFIG = cfg.config;
                CabinetFirebase.init();
            }
        } catch (_) { /* non-critical */ }
    }
    await showLobby();
}

async function initGame(options = {}) {
    const { allowLobbyFallback = false } = options;
    try {
        const [machineData, rulesData] = await Promise.all([
            apiCall('GET', GAME_CONFIG.api.machines),
            apiCall('GET', GAME_CONFIG.api.defaultRules)
        ]);
        machines = machineData;
        paytable = rulesData.payoutMultipliers;
        if (machines.length > 0) {
            const selectedMachine = machines.find(m => m.id === machineId);
            const hasExplicitSelection = Number.isInteger(machineId) && machineId > 0;
            const activeMachine = selectedMachine || (!hasExplicitSelection ? machines[0] : null);

            if (!activeMachine) {
                throw new Error(`Selected machine ${machineId} is unavailable`);
            }

            if (!selectedMachine) {
                machineId = activeMachine.id;
            }

            sessionStorage.setItem('lucky5_machineId', machineId);
            if (currentBet < activeMachine.minBet || currentBet > activeMachine.maxBet) {
                currentBet = activeMachine.minBet;
            }
        }

        const profile = await apiCall('GET', GAME_CONFIG.api.profile);
        walletBalance = profile.walletBalance;
        const session = await fetchMachineSession();
        updateCredits();
        updateStakeDisplay();
        updatePaytable();
        updateJackpotSelectedRow();
        updateBonusHandText();

        let cabinetSnapshot = null;
        try {
            const [machineState, cabinetSnapshotResponse] = await Promise.all([
                apiCall('GET', GAME_CONFIG.api.machineState(machineId)).catch(() => null),
                fetchCabinetSnapshot().catch(() => null)
            ]);
            cabinetSnapshot = cabinetSnapshotResponse;

            if (machineState && machineState.jackpots) {
                updateJackpotDisplay(machineState.jackpots);
            }

            applyCabinetSnapshot(cabinetSnapshot);
        } catch (e) {}

        const activeRound = await fetchActiveRoundState();
        const cabinetRoundSnapshot = buildRoundSnapshotFromCabinetSnapshot(cabinetSnapshot);
        const roundSnapshot = activeRound || cabinetRoundSnapshot;
        if (roundSnapshot) {
            restoreRoundFromSnapshot(roundSnapshot);
        } else {
            if (allowLobbyFallback && !hasRecoverableMachineSession(session, cabinetSnapshot)) {
                resetGameRuntimeState({ clearSelection: true });
                await showLobby();
                return;
            }
            gameState = 'idle';
            jackpotRankArmed = false;
            window.jackpotRankArmed = false;
            const snapshotState = applyCabinetSnapshot(cabinetSnapshot);
            refreshIdleMachineState(snapshotState?.message || null);
        }

        await setupSignalR();
        await joinMachine(machineId);

    } catch (e) {
        showMessage('Error: ' + e.message, 'lose');
    }
}

// ── 11. DOM BOOTSTRAP ──────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
    applyDoubleUpInfoCopy();
    updateDoubleUpInfoPanel();
    updateViewportUnit();
    window.addEventListener('resize', updateViewportUnit);
    window.addEventListener('orientationchange', updateViewportUnit);
    debugLog('boot', { apiBase: API, userAgent: navigator.userAgent });
    if (window.CabinetBonus) CabinetBonus.init();
    const authBtn = $('#auth-submit');
    authBtn.disabled = true;
    authBtn.textContent = 'LOADING...';

    let assetsReady = false;
    preloadAllAssets().then(() => {
        assetsReady = true;
        authBtn.disabled = false;
        authBtn.textContent = 'LOGIN';
        if (window.CabinetStage) {
            CabinetStage.initButtonAssets();
            CabinetStage.initCardSlots();
        }
    });

    const authScreen = $('#auth-screen');
    const authError = $('#auth-error');
    const authToggle = $('#auth-toggle');
    let isLogin = true;

    authToggle.addEventListener('click', () => {
        isLogin = !isLogin;
        $('#auth-title').textContent = isLogin ? 'LOGIN' : 'SIGN UP';
        authBtn.textContent = isLogin ? 'LOGIN' : 'SIGN UP';
        authToggle.innerHTML = isLogin
            ? '<span class="auth-toggle-label">NO ACCOUNT?</span> <span>SIGN UP</span>'
            : '<span class="auth-toggle-label">HAVE ACCOUNT?</span> <span>LOGIN</span>';
        authError.textContent = '';
    });

    authBtn.addEventListener('click', async () => {
        if (!assetsReady) {
            authError.textContent = 'Assets still loading, please wait';
            return;
        }
        const username = $('#auth-username').value.trim();
        const password = $('#auth-password').value.trim();
        if (!username || !password) {
            authError.textContent = 'Fill in all fields';
            return;
        }
        authError.textContent = '';
        authBtn.disabled = true;
        authBtn.textContent = 'LOADING...';

        try {
            let profileData;
            if (isLogin) {
                const data = await doLogin(username, password);
                storeToken(data.tokens.accessToken);
                profileData = data.profile;
            } else {
                const signup = await doSignup(username, password);
                const previewCode = signup?.otp?.previewCode;
                if (!previewCode) {
                    throw new Error('OTP preview unavailable. Verify the account before cabinet login.');
                }
                await doVerifyOtp(username, previewCode);
                const data = await doLogin(username, password);
                storeToken(data.tokens.accessToken);
                profileData = data.profile;
            }
            await enterLobbyAfterLogin(profileData);
        } catch (e) {
            authError.textContent = e.message;
            authBtn.disabled = false;
            authBtn.textContent = isLogin ? 'LOGIN' : 'SIGN UP';
        }
    });

    bindSingleButton('btn-bet', doBet);
    bindSingleButton('btn-deal', doDeal);
    bindSingleButton('btn-cancel', cancelHold);
    bindSingleButton('btn-take-score', () => {
        if (gameState === 'doubleup') duTakeScore();
        else mainTakeScore();
    });
    bindSingleButton('btn-take-half', () => {
        if (gameState === 'doubleup') duTakeHalf();
        else mainTakeHalf();
    });

    $$('.cab-hold').forEach(btn => {
        btn.addEventListener('click', () => {
            const idx = parseInt(btn.dataset.index);
            if (idx === 0 && canAdjustJackpotRank()) {
                cycleJackpotRank();
                return;
            }
            toggleHold(idx);
        });
    });

    $('#btn-big').addEventListener('click', () => {
        if (gameState === 'win') {
            startDoubleUpFlow();
        } else if (gameState === 'doubleup') {
            doDoubleUp('Big');
        }
    });
    $('#btn-small').addEventListener('click', () => {
        if (gameState === 'win') {
            startDoubleUpFlow();
        } else if (gameState === 'doubleup') {
            doDoubleUp('Small');
        }
    });

    const menuBtn = $('#btn-menu');
    const menuPanel = $('#menu-panel');
    if (menuBtn && menuPanel) {
        menuBtn.addEventListener('click', () => {
            setMenuPanelOpen(true);
        });
        $('#btn-close-menu').addEventListener('click', () => {
            setMenuPanelOpen(false);
        });
        $('#btn-logout-menu').addEventListener('click', () => {
            setMenuPanelOpen(false);
            doLogout();
        });
        const cashInBtn = document.getElementById('btn-cash-in');
        if (cashInBtn) cashInBtn.addEventListener('click', async () => {
            try {
                if (gameState !== 'idle') throw new Error('Finish the current round first');
                const maxIn = walletBalance;
                if (maxIn <= 0) throw new Error('No wallet balance to deposit');
                const raw = prompt(`Cash in amount (up to ${formatNum(maxIn)}):`, formatNum(Math.min(200000, maxIn)));
                if (!raw) return;
                const amount = Number(raw.replace(/,/g, ''));
                if (!amount || amount <= 0) throw new Error('Invalid amount');
                if (amount > maxIn) throw new Error('Amount exceeds wallet balance');
                const session = await cashInMachine(amount);
                await fetchMachineSession();
                refreshIdleMachineState(`CASHED IN ${formatNum(amount)} - MACHINE ${formatNum(session.machineCredits)}`, 'win');
                setMenuPanelOpen(false);
            } catch (e) {
                showMessage(e.message, 'lose');
            }
        });
        const cashOutBtn = document.getElementById('btn-cash-out');
        if (cashOutBtn) cashOutBtn.addEventListener('click', async () => {
            try {
                if (gameState !== 'idle') throw new Error('Finish the current round first');
                if (!machineCanCashOut) throw new Error(`Cash out unlocks at ${formatNum(machineCashOutThreshold)} or when the machine closes`);
                const session = await cashOutMachine();
                await fetchMachineSession();
                refreshIdleMachineState(`CASHED OUT - WALLET ${formatNum(session.walletBalance)}`, 'win');
                setMenuPanelOpen(false);
            } catch (e) {
                showMessage(e.message, 'lose');
            }
        });
        $('#btn-reset-machine').addEventListener('click', async () => {
            if (!confirm('Reset machine state only? Active rounds must be empty.')) return;
            try {
                await apiCall('POST', getPlayerMachineResetPath());
                await fetchMachineSession();
                setMenuPanelOpen(false);
                showMessage('MACHINE RESET COMPLETE', 'win');
                refreshIdleMachineState('MACHINE RESET COMPLETE', 'win');
            } catch (e) {
                showMessage('RESET FAILED: ' + e.message, 'lose');
            }
        });
    }

    window.addEventListener('beforeunload', () => {
        if (machineJoined && isHubConnected() && machineId > 0) {
            hubConnection.invoke('LeaveMachine', machineId).catch(() => {});
        }
    });

    const gameBackBtn = document.getElementById('game-back-lobby');
    if (gameBackBtn) {
        gameBackBtn.addEventListener('click', backToLobbyFromGame);
    }

    const lobbyLogoutBtn = document.getElementById('lobby-logout-btn');
    if (lobbyLogoutBtn) {
        lobbyLogoutBtn.addEventListener('click', doLogout);
    }

    const lobbyWalletBtn = document.getElementById('lobby-wallet-btn');
    if (lobbyWalletBtn) {
        lobbyWalletBtn.addEventListener('click', showWallet);
    }

    const walletBackBtn = document.getElementById('wallet-back-btn');
    if (walletBackBtn) {
        walletBackBtn.addEventListener('click', showLobby);
    }

    const adminBackBtn = document.getElementById('admin-back-btn');
    if (adminBackBtn) adminBackBtn.addEventListener('click', showLobby);
    const adminUserSearchBtn = document.getElementById('admin-user-search-btn');
    if (adminUserSearchBtn) adminUserSearchBtn.addEventListener('click', () => loadAdminUsers(document.getElementById('admin-user-search')?.value || ''));
    const adminUserRefreshBtn = document.getElementById('admin-user-refresh-btn');
    if (adminUserRefreshBtn) adminUserRefreshBtn.addEventListener('click', () => {
        const input = document.getElementById('admin-user-search');
        if (input) input.value = '';
        loadAdminUsers('');
    });
    const adminMachineRefreshBtn = document.getElementById('admin-machine-refresh-btn');
    if (adminMachineRefreshBtn) adminMachineRefreshBtn.addEventListener('click', loadAdminMachines);
    const adminAgentCreateBtn = document.getElementById('admin-agent-create-btn');
    if (adminAgentCreateBtn) adminAgentCreateBtn.addEventListener('click', createAdminAgent);
    const adminAgentRefreshBtn = document.getElementById('admin-agent-refresh-btn');
    if (adminAgentRefreshBtn) adminAgentRefreshBtn.addEventListener('click', loadAdminAgents);

    const navLobby = document.getElementById('nav-lobby');
    const navWallet = document.getElementById('nav-wallet');
    const navAdmin = document.getElementById('nav-admin');
    if (navLobby) navLobby.addEventListener('click', showLobby);
    if (navWallet) navWallet.addEventListener('click', showWallet);
    if (navAdmin) navAdmin.addEventListener('click', showAdmin);

    if (token) {
        authScreen.style.display = 'none';
        (async () => {
            try {
                const profile = await apiCall('GET', GAME_CONFIG.api.profile);
                walletBalance = profile.walletBalance;
                storeUserInfo(profile.username, profile.role);
                const savedMachine = sessionStorage.getItem('lucky5_machineId');
                if (savedMachine) {
                    machineId = parseInt(savedMachine, 10);
                    activateShellScreen('game', null);
                    await initGame({ allowLobbyFallback: true });
                } else {
                    await showLobby();
                }
            } catch (e) {
                clearToken();
                authScreen.style.display = '';
            }
        })();
    }

    if (typeof PaytableCanvas !== 'undefined') {
        PaytableCanvas.init();
    }
    
    if (typeof DuBoardCanvas !== 'undefined') {
        DuBoardCanvas.init();
    }

    scaleCabinet();
});

function scaleCabinet() {
    // FIXED 2026-06-27: Removed JS transform scaling that caused letterboxing.
    // The CSS layout system (cabinet-layout-vnext.css) now handles responsive
    // scaling via container queries (cqh/cqw units) and viewport-relative sizing.
    // This function is kept as a no-op for backward compatibility.
}

window.addEventListener('resize', scaleCabinet);
