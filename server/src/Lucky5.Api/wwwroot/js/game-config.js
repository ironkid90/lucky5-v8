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

    // ── 2. TIMING — VSYNC-LOCKED (60Hz Cabinet Clock) ────────────────────────
    // All card animation timing is expressed in FRAMES at 60Hz (~16.67ms/frame).
    // This replicates old VSYNC arcade cabinets where every animation beat
    // derives from the vertical blank interrupt — no floating-point jitter.
    //
    // staggerFrames is the ONE global stagger governing all card reveals:
    // deal, draw, and double-up page turns all use this same fixed count.
    //
    // Non-card timings (drain, idle, DU exits) remain in ms for backward
    // compatibility with the legacy animation system. They're quantized to
    // frames at runtime by CabinetClock.delayMs().
    timing: Object.freeze({
        // ── Global stagger (one value drives everything) ──
        staggerFrames:        11,   // ~183ms — AI9 cabinet frame analysis (~180ms requirement)

        // Main-hand deal
        dealBaseFrames:        5,   //  83ms — pause before first card
        dealDurationFrames:   11,   // 183ms — slide settle time

        // Draw (replacing non-held cards) — equal timing to initial deal
        drawStaggerFrames:   11,   // 183ms — equal to deal stagger (staggerFrames)
        drawOutFrames:        1,   //   1 frame — old cards vanish instantly
        drawDurationFrames:  11,   // 183ms — replacement slide settle (equal to dealDurationFrames)
        drawRevealStartFrames: 5,  //  83ms — equal to dealBaseFrames

        // Legacy ms aliases — derived from staggerFrames at 60fps
        // Kept for backward-compat with game.js helpers that still use delayMs.
        // All cabinet-stage-vnext.js paths use frames directly now.
        get dealBaseMs()         { return this.staggerFrames <= 12 ?  80 : Math.round(this.dealBaseFrames    * 1000 / 60); },
        get dealStaggerMs()      { return Math.round(this.staggerFrames        * 1000 / 60); },
        get dealAnimDurationMs() { return Math.round(this.dealDurationFrames   * 1000 / 60); },
        get drawOutMs()          { return Math.round(this.drawOutFrames        * 1000 / 60); },
        get drawInMs()           { return Math.round(this.drawDurationFrames   * 1000 / 60); },
        get drawStaggerMs()      { return Math.round(this.drawStaggerFrames    * 1000 / 60); },
        get drawRevealStartMs()  { return Math.round(this.drawRevealStartFrames * 1000 / 60); },

        // Double-up: shuffle animation
        // The active slot cycles through card faces visibly, like a spinning reel.
        shuffleFrameMs:       130, // Calibrated shuffle cadence (~130ms per frame swap)

        // Double-up: reveal sequence
        duRevealDelayMs:      150,  // wait after server responds before showing challenger card
        duWinHoldMs:          900,  // show WIN message before advancing the trail (player sees the win)
        duStaggerPerCardMs:   80,  // stagger between cards on a fresh DU page

        // Win collection / drain-to-credits
        //   Duration scales with amount: ~1.5s at 500K, ~60s at 40M.
        countUpMinMs:         1500,
        countUpMaxMs:         60000,
        creditTickMs:         50,  // digit-flash toggle during count-up (mechanical reel tick cadence)

        // Jackpot fill animation (for jackpot-level wins)
        //   Same scaling as animateDrainToCredits: amount / 1_000_000 * 1500.
        jackpotFillMinMs:     1500,
        jackpotFillMaxMs:     60000,

        // Lucky5 safe / machine-closed payout drain
        drainDelayMs:         500,   // brief pause before starting the drain animation

        // Double-up transition delay
        winToDoubleUpDelayMs: 800,   // delay before auto-entering DU mode after a win

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
        configRules:      '/api/Config/rules',

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

// Expose to window so code using window.GAME_CONFIG works
// (const at top-level does NOT create a window property)
window.GAME_CONFIG = GAME_CONFIG;
