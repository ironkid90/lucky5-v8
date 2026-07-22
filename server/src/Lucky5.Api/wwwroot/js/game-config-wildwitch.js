/**
 * game-config-wildwitch.js  —  Wild Witch (Video Klein) Variant Configuration
 * ═══════════════════════════════════════════════════════════════════════════
 * This configuration file implements the Wild Witch (Video Klein, 6502 lineage)
 * cabinet variant for Lucky5. Swap this file with game-config.js to enable
 * the Wild Witch variant.
 *
 * Based on MAME wldwitch (ww184a.bin ver 1.84A) and goldenpo/wildwitch reference.
 *
 * Key Wild Witch features:
 * - 53-card deck with Joker/Wild card
 * - BIG/SMALL double-up with fixed threshold 7
 * - Ace counts HI or LO (auto-win in double-up)
 * - 5♠ never loses when buying (SafeFail)
 * - Progressive jackpots: 4K-A (Aces), 4K-B (2s-4s), SF, RF
 * - Max bet 100 credits
 * - Percentage modes: 85%, 30%, 40%, 50%
 * - DIP SW2: Wild Witch vs Witch Game, 6-btn vs 12-btn, min hand Two Pairs vs High Pair
 */

/* global GAME_CONFIG */
const GAME_CONFIG = Object.freeze({

    // ── 1. META ─────────────────────────────────────────────────────────────
    meta: Object.freeze({
        variantId:   'wildwitch-video-klein',
        variantName: 'Wild Witch (Video Klein)',
        handSize:    5,
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
        adapterVNext: false,
        enableDisplaySnapshot: true,
        enableCabinetStage: true,
        wildCardEnabled: true,      // Joker/wild card support
        progressiveMeters: true,    // 4K-A, 4K-B, SF, RF meters
    }),

    // ── 2. TIMING — VSYNC-LOCKED (60Hz Cabinet Clock) ────────────────────────
    timing: Object.freeze({
        // Global stagger (one value drives everything)
        staggerFrames:        12,   // 200ms — AI9 cabinet frame analysis

        // Main-hand deal
        dealBaseFrames:        5,   //  83ms — pause before first card
        dealDurationFrames:   11,   // 183ms — slide settle time

        // Draw (replacing non-held cards) — slightly slower, more deliberate
        drawStaggerFrames:    18,   // 300ms — deliberate redraw stagger
        drawOutFrames:         1,   //   1 frame — old cards vanish instantly
        drawDurationFrames:   11,   // 183ms — replacement slide settle
        drawRevealStartFrames: 3,   //  50ms — delay before first replacement

        // Legacy ms aliases — derived from staggerFrames at 60fps
        get dealBaseMs()         { return this.staggerFrames <= 12 ?  80 : Math.round(this.dealBaseFrames    * 1000 / 60); },
        get dealStaggerMs()      { return Math.round(this.staggerFrames        * 1000 / 60); },
        get dealAnimDurationMs() { return Math.round(this.dealDurationFrames   * 1000 / 60); },
        get drawOutMs()          { return Math.round(this.drawOutFrames        * 1000 / 60); },
        get drawInMs()           { return Math.round(this.drawDurationFrames   * 1000 / 60); },
        get drawStaggerMs()      { return Math.round(this.staggerFrames        * 1000 / 60); },
        get drawRevealStartMs()  { return Math.round(this.drawRevealStartFrames * 1000 / 60); },

        // Double-up: shuffle animation (Video Klein style)
        shuffleFrameMs:       100, // Calibrated shuffle cadence (~100ms per frame swap)

        // Double-up: reveal sequence
        duRevealDelayMs:      150,  // wait after server responds before showing challenger card
        duWinHoldMs:          900,  // show WIN message before advancing the trail
        duStaggerPerCardMs:   80,   // stagger between cards on a fresh DU page

        // Win collection / drain-to-credits
        countUpMinMs:         1000,
        countUpMaxMs:         15000,
        creditTickMs:         50,

        // Jackpot fill animation
        jackpotFillMinMs:     750,
        jackpotFillMaxMs:     20000,

        // Lucky5 safe / machine-closed payout drain
        drainDelayMs:         500,

        // Double-up exit delays
        exitDuLoseMs:         1000,
        exitDuCatchMs:        1200,

        // Double-up loss siphon
        duLoseRevealMs:       1500,

        // Take-half continue delay
        takeHalfContinueMs:   800,

        // Idle overlay / attract
        idleTitleHoldMs:       2500,
        idleOverlayAppearMs:   2500,
        idleAttractModeMs:     15000,

        lucky5FlashDurationMs: 1000,
    }),

    // ── 3. API ───────────────────────────────────────────────────────────────
    // Uses same backend endpoints - variant handled server-side via gameId
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
    // Wild Witch specific game logic - must match backend WildWitchCabinetVariant
    rules: Object.freeze({
        // Wild card (Joker) - triggers wild substitutions
        wildCard:           'JK',

        // Lucky 5 card (5♠) - triggers no-lose double-up
        luckyCard:          '5S',

        // Machine credit ceiling before auto-cashout
        machineCreditLimit: 10_000_000,

        // Which hand ranks carry jackpot counters
        jackpotHands: Object.freeze(['FourOfAKind', 'StraightFlush', 'RoyalFlush']),

        // Jackpot seed / reset values — must mirror server EngineConfig defaults
        jackpotReset: Object.freeze({
            FourOfAKindA:   20_000,    // 4K-A (Aces only)
            FourOfAKindB:   10_000,    // 4K-B (2s-4s)
            StraightFlush:  850_000,
            RoyalFlush:     1_000_000,
            FullHouse:      90_000,
            Kent:           500_000,
        }),

        // Double-up threshold for BIG/SMALL (fixed threshold like Bonanza)
        doubleUpThreshold:  7,  // BIG >= 7, SMALL < 7 (but Ace auto-wins both ways)

        // Full-house rank selector: rank number → card-code suffix
        rankNames: Object.freeze({
            2:'2', 3:'3', 4:'4', 5:'5', 6:'6', 7:'7', 8:'8', 9:'9',
            10:'10', 11:'J', 12:'Q', 13:'K', 14:'A',
        }),

        // Wild Witch specific rules
        wildRules: Object.freeze({
            aceCountsHiOrLo: true,      // "ACE COUNTS HI OR LO"
            fiveNeverLose: true,        // "5 NEVER LOSE WHEN BUYING"
            wildCardSubstitutes: true,  // Joker substitutes for any rank/suit
            maxBetCredits: 100,         // DIP SW2: max bet 10/20/50/100
            minHandTwoPairs: true,      // DIP SW2: minimal hand Two Pairs vs High Pair
            royalFlushEnabled: true,    // DIP SW2: Royal Flush enable
        }),
    }),

    // ── 5. DOUBLE-UP PAGE MODEL ──────────────────────────────────────────────
    doubleUp: Object.freeze({
        maxTrailPerPage: 4,    // trail cards visible per page (+ 1 active slot = 5 total)
        copy: Object.freeze({
            label:        'HI LO GAMBLE',
            aceRule:      'ACE COUNTS',
            guessRule:    'HI OR LO',
            luckyRule:    '5 ♠ NEVER LOSE',
            buyingRule:   'WHEN BUYING',
            prompt:       'BIG / SMALL ?',
            activeSuffix: 'ACTIVE',
        }),
    }),

    // ── 6. ASSETS ────────────────────────────────────────────────────────────
    // Wild Witch cabinet skin assets
    assets: Object.freeze({
        cardBack:   '/assets/images/cards/wildwitch-bside.png',
        boardImage: '/assets/images/board-wildwitch.png',
        pressSound: '/assets/images/press.mp3',
        // Wild card face
        wildCardFace: '/assets/images/cards/joker.png',
        // Cabinet skin
        cabinetSkin: '/assets/images/cabinet-wildwitch.png',
    }),

    // ── 6b. AUDIO ──────────────────────────────────────────────────────────
    audio: Object.freeze({
        events: Object.freeze({
            press:        Object.freeze({ src: '/assets/images/press.mp3', volume: 0.30, priority: 'normal' }),
            invalid:      Object.freeze({ src: '/assets/images/press.mp3', volume: 0.18, priority: 'high' }),
            deal:         Object.freeze({ src: '/assets/images/press.mp3', volume: 0.24, priority: 'normal' }),
            draw:         Object.freeze({ src: '/assets/images/press.mp3', volume: 0.24, priority: 'normal' }),
            doubleup:     Object.freeze({ src: '/assets/images/press.mp3', volume: 0.22, priority: 'normal' }),
            collect:      Object.freeze({ src: '/assets/images/press.mp3', volume: 0.20, priority: 'low' }),
            lucky5:       Object.freeze({ src: '/assets/images/press.mp3', volume: 0.40, priority: 'high' }),
            machineClose: Object.freeze({ src: '/assets/images/press.mp3', volume: 0.40, priority: 'high' }),
            wildHit:      Object.freeze({ src: '/assets/images/wild-hit.mp3', volume: 0.35, priority: 'high' }),
            jackpotHit:   Object.freeze({ src: '/assets/images/jackpot-hit.mp3', volume: 0.50, priority: 'high' }),
        })
    }),

    // ── 7. PAYTABLE DISPLAY MAP ──────────────────────────────────────────────
    // Wild Witch paytable with wild card and Five of a Kind
    paytableMap: Object.freeze({
        FiveOfAKind:    '5 OF A KIND',
        RoyalFlush:     'ROYAL FLUSH',
        StraightFlush:  'STRAIGHT FLUSH',
        FourOfAKind:    '4 OF A KIND',
        FullHouse:      'FULL HOUSE',
        Flush:          'FLUSH',
        Straight:       'STRAIGHT',
        ThreeOfAKind:   '3 OF A KIND',
        TwoPair:        '2 PAIR',
        OnePair:        'JACKS OR BETTER',
        Nothing:        'NO WIN',
    }),
});

// Expose to window so code using window.GAME_CONFIG works
window.GAME_CONFIG = GAME_CONFIG;