/**
 * game-config.js  —  Lucky5 Variant Configuration
 * ═══════════════════════════════════════════════════════════════════════════
 * This is the SINGLE FILE you swap when creating a new arcade video poker
 * variant.  The engine (game.js) reads everything from GAME_CONFIG and must
 * never hard-code values that belong here.
 *
 * Sections:
 *   1. meta        — variant identity
 *   2. timing      — all animation / delay values in one place
 *   3. api         — backend endpoint paths (swap for a different backend)
 *   4. rules       — game logic constants (hands, jackpots, limits)
 *   5. doubleUp    — double-up page model
 *   6. assets      — card back, board image paths
 *   7. paytableMap — display names for hand ranks (UI only)
 */

/* global GAME_CONFIG */
const GAME_CONFIG = Object.freeze({

    // ── 1. META ─────────────────────────────────────────────────────────────
    meta: Object.freeze({
        variantId:   'lucky5-video-poker',
        variantName: 'Lucky 5',
        handSize:    5,       // cards in a hand (change to 3 for 3-card variants)
    }),

    // ── 1b. CABINET MODEL ──────────────────────────────────────────────────
    cabinet: Object.freeze({
        fps: 60,
        layout: Object.freeze({
            width: 720,
            height: 1280,
            zones: Object.freeze({
                paytable: Object.freeze({ left: 8, top: 8, width: 350, height: 250 }),
                counters: Object.freeze({ left: 505, top: 8, width: 190, height: 185 }),
                cards: Object.freeze({ left: 8, top: 238, width: 705, height: 270 }),
                machine: Object.freeze({ left: 8, top: 505, width: 705, height: 120 }),
                controls: Object.freeze({ left: 0, top: 640, width: 720, height: 640 })
            })
        })
    }),

    // ── 1c. FEATURE FLAGS ──────────────────────────────────────────────────
    features: Object.freeze({
        adapterVNext: false,       // planned clone-parity adapter gate
        enableDisplaySnapshot: true, // cabinet snapshot restore is active for reconnect/session recovery
        enableCabinetStage: true,  // cabinet stage is the active render path for this parity slice
    }),

    // ── 2. TIMING ────────────────────────────────────────────────────────────
    // All durations are in milliseconds.
    //
    // 2026-06-27 AI9 PARITY CALIBRATION.
    // Reference: AI9 Dart/Flutter cabinet (frame-by-frame video analysis).
    // Measured via 30fps frame extraction and white-pixel detection in card area.
    // Current parity guide target: deal cards at 350ms stagger and redraw replacements at 100ms.
    // Drain animation: TBD (frame-step TAKE SCORE sequence).
    // If you change these, mirror the feel-check in GAME_FEEL_REFERENCE.md and
    // docs/AI9_PARITY_GROUND_TRUTH_AND_WORKLOG.md.
    timing: Object.freeze({
        // Main-hand deal animation
        // Cards drop from above one at a time, like a mechanical dealer.
        // Each card has visible travel + settle time.
        // FAST DEAL (2026-06-28): Reduced stagger for snappier feel.
        // Target: ~15% faster total deal time for more responsive gameplay.
        dealBaseMs:           60,  // pause before first card lands (cabinet "thunk")
        dealStaggerMs:        340, // ~20% faster than 475ms (cards appear quicker left-to-right)
        dealAnimDurationMs:   100, // slightly faster settle time

        // Draw animation (re-dealing only non-held cards)
        // Held cards stay put visibly. Replaced cards flip out, new cards flip in.
        drawOutMs:            60,  // fade-out / flip-out duration on replaced cards
        drawInMs:             80,  // fade-in / flip-in duration on new cards
        drawStaggerMs:        100, // AI9 parity guide target
        drawRevealStartMs:    60,  // delay before first replaced card starts dropping

        // Double-up: shuffle animation
        // The active slot cycles through card faces visibly, like a spinning reel.
        shuffleFrameMs:       130,  // how often the shuffle swaps to a random card

        // Double-up: reveal sequence
        duRevealDelayMs:      250,  // wait after server responds before showing challenger card
        duWinHoldMs:          900,  // show WIN message before advancing the trail (player sees the win)
        duStaggerPerCardMs:   120,  // stagger between cards on a fresh DU page

        // Win collection / drain-to-credits
        //   Duration scales with amount: ~1.5s at 500K, ~60s at 40M.
        //   Formula in animateDrainToCredits: amount / 1_000_000 * 1500, clamped.
        //   Min clamped at countUpMinMs (1.0s), max clamped at countUpMaxMs (65s).
        countUpMinMs:         1000,
        countUpMaxMs:         65000,
        creditTickMs:         120,  // digit-flash toggle during count-up (mechanical reel tick cadence)

        // Jackpot fill animation (for jackpot-level wins)
        //   Same scaling as animateDrainToCredits: amount / 1_000_000 * 1500.
        //   500K → 750ms, 10M → 15s, 40M → 60s.
        //   Jackpot wins behave like a mini machine-close: everything freezes,
        //   the jackpot counter drains slowly into credits, then DU page appears.
        jackpotFillMinMs:     750,
        jackpotFillMaxMs:     65000,

        // Lucky5 safe / machine-closed payout drain
        drainDelayMs:         500,   // brief pause before starting the drain animation

        // Double-up exit delays
        exitDuLoseMs:         1000,  // delay before exiting DU after a loss (no siphon)
        exitDuCatchMs:        1200,  // delay before exiting DU after a network error

        // Double-up loss siphon: brief pause to show losing card before drain starts
        duLoseRevealMs:       1500,  // how long the losing card is visible before siphon begins

        // Take-half continue delay
        takeHalfContinueMs:   800,   // delay before re-offering DU after taking half

        // Idle overlay / attract
        idleTitleHoldMs:       2500,  // how long the LUCKY 5 title stays up before the FH selector card returns
        idleOverlayAppearMs:   2500,  // legacy fallback for older idle-title timing paths
        idleAttractModeMs:     15000, // full arcade attract sequence kicks in after this long

        lucky5FlashDurationMs: 1000,  // 1-second full-screen white flash when landing 5S
    }),

    // ── 3. API ───────────────────────────────────────────────────────────────
    // All backend endpoint strings live here.
    // Swap this section to point the engine at a different server or route prefix.
    api: Object.freeze({
        // Auth
        login:            '/api/auth/login',
        profile:          '/api/Auth/GetUserById',
        wallet:           '/api/Auth/wallet',
        memberHistory:    '/api/Auth/MemberHistory',

        // Lobby / machines
        machines:         '/api/Game/games/machines',
        defaultRules:     '/api/Game/defaultRules',

        // Machine session
        machineSession:   (id) => `/api/Game/machine/${id}/session`,
        machineState:     (id) => `/api/Game/machine/${id}/state`,
        machineRound:     (id) => `/api/Game/machine/${id}/active-round`,
        machineCabinetSnapshot: (id) => `/api/Game/machine/${id}/cabinet-snapshot`,
        machineCashIn:    (id) => `/api/Game/machine/${id}/cash-in`,
        machineCashOut:   (id) => `/api/Game/machine/${id}/cash-out`,
        machineReset:     (id) => `/api/Game/machine/${id}/reset`,

        // Core game actions
        deal:             '/api/Game/cards/deal',
        draw:             '/api/Game/cards/draw',

        // Jackpot
        jackpotRank:      '/api/Game/jackpot/rank',

        // Double-up
        duStart:          '/api/Game/double-up/start',
        duGuess:          '/api/Game/double-up/guess',
        duCashout:        '/api/Game/double-up/cashout',
        duTakeHalf:       '/api/Game/double-up/take-half',
        duSwitch:         '/api/Game/double-up/switch',

        // Admin
        adminUsers:       '/api/Admin/users',
        adminUserSearch:  (q) => `/api/Admin/users/search?q=${encodeURIComponent(q)}`,
        adminCredit:      '/api/Admin/users/credit',
        adminMachines:    '/api/Admin/machines',
        agents:           '/api/Agent',
        agentLoadCredit:  (agentId) => `/api/Agent/${agentId}/load-credit`,
        agentAssignUser:  (agentId, userId) => `/api/Agent/${agentId}/assign-user/${userId}`,
    }),

    // ── 4. RULES ─────────────────────────────────────────────────────────────
    // Variant-specific game logic.  These must stay in sync with the backend
    // EngineConfig / CleanRoom defaults.
    rules: Object.freeze({
        // The Lucky5 special card — triggers no-lose double-up
        luckyCard:          '5S',

        // Machine credit ceiling before auto-cashout
        machineCreditLimit: 40_000_000,

        // Which hand ranks carry jackpot counters
        jackpotHands: Object.freeze(['FourOfAKind', 'FullHouse', 'StraightFlush']),

        // Jackpot seed / reset values — must mirror server EngineConfig defaults
        jackpotReset: Object.freeze({
            FullHouse:     90_000,
            FourOfAKind:   140_000,
            StraightFlush: 850_000,
        }),

        // Full-house rank selector: rank number → card-code suffix
        // (e.g. 14 = Ace → 'A', used to show the FH selector card)
        rankNames: Object.freeze({
            2:'2', 3:'3', 4:'4', 5:'5', 6:'6', 7:'7', 8:'8', 9:'9',
            10:'10', 11:'J', 12:'Q', 13:'K', 14:'A',
        }),
    }),

    // ── 5. DOUBLE-UP PAGE MODEL ──────────────────────────────────────────────
    // Controls how the DU history trail is paginated.
    // Change maxTrailPerPage to 3 for a 4-slot variant, etc.
    doubleUp: Object.freeze({
        maxTrailPerPage: 4,    // trail cards visible per page (+ 1 active slot = 5 total)
        // carryStep = maxTrailPerPage - 1; last card of page N is first card of page N+1
        copy: Object.freeze({
            label:        'HI LO GAMBLE',
            aceRule:      'ACE COUNTS',
            guessRule:    'HI OR LO',
            luckyRule:    '5 \u2660 NEVER LOSE',
            buyingRule:   'WHEN BUYING',
            prompt:       'BIG / SMALL ?',
            activeSuffix: 'ACTIVE',
        }),
    }),

    // ── 6. ASSETS ────────────────────────────────────────────────────────────
    // Card and button images live in /assets/images/, sounds also there (fallback for /assets/sounds/)
    assets: Object.freeze({
        cardBack:   '/assets/images/cards/bside.png',
        boardImage: '/assets/images/board.png',
        pressSound: '/assets/images/press.mp3',
    }),

    // ── 6b. AUDIO ──────────────────────────────────────────────────────────
    // Audio files live alongside images at /assets/images/*.mp3 (fallback for missing sounds dir)
    audio: Object.freeze({
        events: Object.freeze({
            press:        Object.freeze({ src: '/assets/images/press.mp3', volume: 0.30, priority: 'normal' }),
            invalid:      Object.freeze({ src: '/assets/images/press.mp3', volume: 0.18, priority: 'high' }),
            deal:         Object.freeze({ src: '/assets/images/press.mp3', volume: 0.24, priority: 'normal' }),
            draw:         Object.freeze({ src: '/assets/images/press.mp3', volume: 0.24, priority: 'normal' }),
            doubleup:     Object.freeze({ src: '/assets/images/press.mp3', volume: 0.22, priority: 'normal' }),
            collect:      Object.freeze({ src: '/assets/images/press.mp3', volume: 0.20, priority: 'low' }),
            lucky5:       Object.freeze({ src: '/assets/images/press.mp3', volume: 0.40, priority: 'high' }),
            machineClose: Object.freeze({ src: '/assets/images/press.mp3', volume: 0.40, priority: 'high' })
        })
    }),

    // ── 7. PAYTABLE DISPLAY MAP ──────────────────────────────────────────────
    // Maps backend hand-rank enum values to cabinet label text.
    // Override for a variant with different hand names.
    paytableMap: Object.freeze({
        RoyalFlush:    'ROYAL FLUSH',
        StraightFlush: 'STRAIGHT FLUSH',
        FourOfAKind:   '4 OF A KIND',
        FullHouse:     'FULL HOUSE',
        Flush:         'FLUSH',
        Straight:      'STRAIGHT',
        ThreeOfAKind:  '3 OF A KIND',
        TwoPair:       '2 PAIR',
        Nothing:       'NO WIN',
    }),
});
