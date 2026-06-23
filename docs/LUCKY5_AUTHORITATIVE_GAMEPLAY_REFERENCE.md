# Lucky5 Authoritative Gameplay Reference

**Status:** Source-of-truth gameplay specification for the Lucky5 cabinet.
**Audience:** Any agent (Codex, Claude, Cascade, human) working on Lucky5 frontend, cabinet presentation, contract DTOs, or game-feel parity.
**Last verified:** Live capture from `https://ai9poker.com/install` (Lucky 5 Poker variant) on 2026-05-05.
**Hard rule:** Do not change game rules. This document captures what the cabinet *must* render and how the player *experiences* the game. The clean-room domain in `server/src/Lucky5.Domain/Game/CleanRoom/` is the rules authority; this document is the presentation authority.

Historical note: some later sections still mention Godot because this document was carried forward from v7. In v8, treat those references as presentation requirements to preserve, not as an instruction to reintroduce the Godot client.

---

## 1. Why This Document Exists

Previous agent attempts (and earlier passes by Cascade) modeled the Lucky5 double-up as a simple Hi-Lo gamble. **That is wrong.** The double-up is itself a 5-card progressive bonus round where forming poker hands across the accumulating board pays the base paytable on top of the doubled stake. Misunderstanding this has led to incorrect DTO shapes, missing card-trail fields, and cabinet drafts that do not match the classic cabinet feel.

This file is the canonical reference for:

- The **5-card double-up bonus board** (not a single Hi-Lo guess).
- The **shuffle-and-stop** card-arrival animation classic to arcade poker cabinets.
- The **rotating Full House rank card** rendered in the middle slot when the cabinet is idle.
- The **Kent counter** mechanic (sequentially-ordered straights → progressive jackpot).
- The **"4 OF A KIND WINS BONUS"** banner and the **background "Lucky 5"** print that anchor the Lebanese arcade identity.
- The **Lucky 5 / 5♠ never-lose** state visualization.
- Asset mapping reference (Deprecated: we now use DOM/CSS instead of PNG assets).

---

## 2. Cabinet Visual Anatomy (Portrait)

```
┌──────────────────────────────────────────────────┐
│  PAYTABLE (left, green pixel font)               │
│    ROYAL FLUSH       20,000,000                  │
│    STRAIGHT FLUSH     6,000,000                  │
│    4 OF A KIND        2,400,000                  │  ← rotating: white
│   [FULL HOUSE]          400,000                  │    border = currently
│    FLUSH                280,000                  │    armed FH rank
│    STRAIGHT             200,000                  │
│    3 OF A KIND          120,000                  │
│    2 PAIR                80,000  K  35,000,000   │  ← K = current rotating
│                                                  │    Full House rank +
│                                                  │    K-jackpot value
│   CREDIT  380,000      STAKE   20,000            │
├──────────────────────────────────────────────────┤
│  ┌────┐ ┌────┐ ┌────┐ ┌────┐ ┌────┐              │
│  │ 8♦ │ │ K♣ │ │ FH │ │ J♠ │ │ K♠ │  ← 5-card    │
│  │    │ │HOLD│ │CARD│ │    │ │HOLD│    playfield │
│  └────┘ └────┘ └────┘ └────┘ └────┘    (middle = │
│                                          rotating │
│                                          FH card  │
│                                          when idle)│
│                                                  │
│   PRESS HOLDS TO KEEP CARD     (cyan)            │
│                                                  │
│   SERIE   _ 2                                    │
│   KENT /3 _ 2          (red label, green digit)  │
├──────────────────────────────────────────────────┤
│   × 5,000,000   20,000,000   × 5,000,000         │  ← jackpot pool row
│   S/N: 10,000,000                                │     (3-of-a-kind /
│   4 OF A KIND WINS BONUS  (yellow banner)        │      bonus pools)
├──────────────────────────────────────────────────┤
│   [HOLD] [HOLD] [HOLD] [HOLD] [HOLD]   yellow    │
│                                                  │
│   [BIG] [SMALL] [CANCEL  ] [DEAL ] [BET]         │
│   orng  orng    [HOLD    ] [DRAW ]  green        │
│                  white      red                  │
│                                                  │
│   [TAKE HALF]    ( MENU )   [TAKE SCORE]         │
│      red          black        orange            │
└──────────────────────────────────────────────────┘
```

### 2.1 Field-by-field source of truth

| Visual element                             | Backend field (existing)                                       | Notes                                                |
| ------------------------------------------ | -------------------------------------------------------------- | ---------------------------------------------------- |
| Paytable values                            | Computed from `BetAmount × multiplier` per `HandCategory` | Scale linearly with `STAKE`                        |
| `[FULL HOUSE]` highlighted row           | `MachineLedgerState.JackpotFullHouseRank`                    | White border on the row matching current rank        |
| `K  35,000,000` jackpot tag              | `JackpotFullHouseRank` + `JackpotFullHouse`                | Rank glyph + value                                   |
| `CREDIT`                                 | `MachineSessionState.MachineCredits`                         | Top-right green digits                               |
| `STAKE`                                  | `GameRound.BetAmount`                                        | Top-right yellow digits                              |
| 5 card slots (idle)                        | Black `LUCKY 5` title first; after the idle delay, slot 3 = rotating FH rank card and the other slots are empty | See §4                                              |
| 5 card slots (dealt)                       | `GameRound.InitialCards` then `GameRound.FinalCards`       | See §3                                              |
| `HOLD` badge under a card                | `bool[5] held` per card                                      | Cyan text under card image                           |
| `PRESS HOLDS TO KEEP CARD`               | UI hint when phase =`Dealt`                                  | Cyan retro pixel font                                |
| `SERIE _ N`                              | `MachineLedgerState.Serie` count                             | Progressive run counter                              |
| `KENT /3 _ N`                            | `MachineLedgerState.JackpotKent` round counter               | See §5 — sequentially-ordered straight progression |
| `× 5,000,000  20,000,000  × 5,000,000` | 4-of-a-Kind A / Straight Flush / 4-of-a-Kind B pool values     | Three-pool bonus row                                 |
| `S/N: 10,000,000`                        | `JackpotStraightFlush` (or Serial-Number-tagged pool)        |                                                      |
| `4 OF A KIND WINS BONUS`                 | Static banner; lights/animates on 4OAK hit                     | Yellow pixel font                                    |

---

## 3. Base Game Flow (Five-Card Draw Poker)

1. **Idle** — the cabinet first shows a black CRT field with the **`LUCKY 5`** title. After the idle delay, the middle slot shows the **rotating Full House rank card** (the rank currently armed for the Full House jackpot, e.g., a King face) and the other slots remain empty.
2. **Bet adjust** — `BET` button cycles `STAKE` (e.g., 20,000 → 40,000 → ...). Paytable values scale instantly.
3. **DEAL DRAW press** — deducts stake from `CREDIT`, animates a **classic arcade deal**:
   - Cards arrive **one at a time**, left-to-right, slot 1 → slot 5.
   - Each card slides in from off-screen-top (or flips face-up in place — cabinet-classic feel; see §7 for timing).
   - The middle "rotating FH" card is replaced by the dealt card.
4. **Hold phase** — player toggles `HOLD` per card. Held cards show a `HOLD` badge underneath (cyan pixel text).
5. **DEAL DRAW second press** — replaces non-held cards with new cards from the deck (same one-by-one animation, only on the slots being redrawn).
6. **Evaluate** — final hand category lights up on the paytable (the matching row glows / inverts color). `WIN` amount displays. If 4-of-a-kind: `4 OF A KIND WINS BONUS` banner pulses and pays the bonus pool.
7. **Settlement options:**
   - `TAKE SCORE` — credits paid to `CREDIT`, return to idle.
   - `TAKE HALF` — half paid to `CREDIT`, remainder enters double-up.
   - Player wins are auto-eligible to enter double-up via guess buttons.

---

## 4. Idle State — The Rotating Full House Card

**Critical feel detail.** When the cabinet is idle (no active round, post-settlement, or pre-deal), the **middle card slot (index 2 of 0..4)** shows a face-up card representing the currently-armed Full House jackpot rank.

- If `JackpotFullHouseRank == 14` (Ace) → middle slot shows `AS.png` (or any suit; convention is spades).
- If `JackpotFullHouseRank == 13` (King) → middle slot shows `KS.png`.
- ... and so on.

**Why:** The classic cabinet teases the player — *"Full House of Kings is paying 35M right now"* — visible at all times so the player anchors their bet.

**Other 4 slots while idle:** empty/blank after the delayed FH rank reveal. Do not render a full facedown five-card deck in idle title mode; card backs still appear during deal staging and double-up unused board slots.

**Transition on DEAL:** the middle FH card is *replaced* by the dealt card; it does **not** persist or stack.

### 4.1 Player-initiated FH-rank switch (top-left HOLD button)

The player may **rotate the armed Full House rank to any rank 2..A at the start of a round**, subject to the following gate:

- The player must have pressed `BET` **at least once** in the current session (so the machine has taken the first round of credits and is no longer in a "fresh" lockout state).
- Once that gate is open, pressing the **top-left HOLD button (HOLD[0])** while the cabinet is `idle` (no active dealt board) opens an FH-rank picker.
- Selecting a rank updates `MachineLedgerState.JackpotFullHouseRank`, the rotating middle-slot card, and the `[FULL HOUSE]` paytable highlight.
- The switch is free (no credit cost). It only works pre-deal; once cards are dealt for the round, HOLD[0] reverts to its normal hold-card-1 function.

**Rationale:** the player can target a Full House rank they think they are about to hit (e.g., the player has been seeing lots of Kings on draws) without waiting for the machine's automatic rotation (which only happens after an FH jackpot is paid out).

**Backend mapping:** reuse the existing `ChangeJackpotRankAsync(machineId, rank)` (currently exposed admin-side in `GameService.cs`). Add a player-callable endpoint `POST /api/Game/switch-fh-rank { machineId, rank }` that:

- requires the player's session for that machine,
- requires `MachineSession.HasPressedBetThisSession == true`,
- forbids switching while an active dealt round exists,
- delegates to the same ledger-mutation path.

---

## 5. Kent Counter — Sequentially-Ordered Straights

**This is a unique Lucky5 mechanic. Earlier specs (and this doc, before 2026-05-05) got the reset and direction rules wrong. The rules below are authoritative and supersede any earlier description.**

A normal straight (e.g., `5♥ 7♣ 4♠ 6♦ 3♣`) pays the standard `STRAIGHT` row.

A **Kent** is a straight where the **5 cards in the slots are in sequential positional order** — either **ascending** (`2,3,4,5,6` left→right) **or descending** (`6,5,4,3,2` left→right). Either direction qualifies. The cards must be in order in their slots **without any rearrangement**.

### 5.1 Detection rules

- Evaluated on the **initial dealt 5 cards** (`GameRound.InitialCards`), before any HOLD/DRAW cycle. Draw replacements do not retro-actively trigger Kent.
- **Direction:** ascending OR descending — both qualify.
- The hand must also be a valid `Straight` (handles wheel `A,2,3,4,5` only when slots present them in that exact order; the high-Ace wheel `5,4,3,2,A` is **not** a Kent because A is positionally out of sequence).
- The FH-rank switch (§4.1) is a pre-deal action and does not affect Kent evaluation either way; Kent is detected on whatever cards land on the dealt board, regardless of whether the player used the switch on that round.

### 5.2 Counter and progressive rules (CRITICAL — corrects prior spec)

- The **`KENT /3` counter is progressive and never resets on a non-Kent round.** A player can play hundreds of non-Kent rounds in between; the counter holds its value.
- Each Kent round increments the counter by 1.
- The counter resets to **0 only when** the **3rd Kent triggers the Kent jackpot payout** (`MachineLedgerState.JackpotKent`).
- A `Straight Flush` whose cards are also positionally sequential pays the **Straight Flush jackpot** (§7.4), not the Kent jackpot — Kent is reserved for non-flush straights in sequence. The Kent counter is **not** incremented by a Straight-Flush hit (confirm: separate jackpot pool).

### 5.3 Backend mapping (required fields)

```
GameRound.IsKent           : bool   // true iff straight + slot-sequential (asc OR desc)
MachineLedgerState.KentStreak : int // current /3 progress, never decremented on non-Kent
```

The rule logic (sequential check, asc/desc detection) lives in `Lucky5.Domain.Game.CleanRoom` (e.g., `FiveCardDrawEngine.IsSequentialBoard`).

The cabinet renders `KENT /3 _ N` where N = `KentStreak`. When `N == 3`, the next round starts at 0 after the jackpot pays.

**Current implementation note:** the backend still has the `JackpotKent` pool and contribution/reset fields, but the live payout branch currently resolves that pool through `HandCategory.FiveOfAKind` in `GameService.DrawAsync`. Until `IsKent`/`KentStreak` are implemented as first-class round state, simulation financial telemetry mirrors the current backend pool behavior rather than inventing a separate sequential-straight payout path.

---

## 6. Double-Up Bonus Round (CRITICAL — This Is Not Just Hi-Lo)

### 6.1 The misconception to avoid

Earlier docs and DTOs treated the double-up as: *one dealer card, player picks BIG/SMALL, one challenger card revealed, win or lose, repeat*.

**Reality:** the double-up screen is a **5-card progressive bonus board**. Cards accumulate as the player keeps guessing correctly. Forming poker hands across the accumulating board pays the **base paytable** (in addition to doubling the stake on each correct guess).

### 6.2 Screen layout

```
┌──────────────────────────────────────────────────┐
│  PAYTABLE (same as base game)                    │
│  CREDIT / STAKE                                  │
├──────────────────────────────────────────────────┤
│  DOUBLE UP                                       │  ← red label
│  ┌────┐ ┌────┐ ┌────┐ ┌────┐ ┌────┐              │
│  │ K♥ │ │ ?? │ │ ?? │ │ ?? │ │ ?? │              │
│  │    │ │SHFL│ │    │ │    │ │    │              │
│  └────┘ └────┘ └────┘ └────┘ └────┘              │
│   dealer  ←shuffling cycle through slot 2→       │
│                                                  │
│            HI LO GAMBLE                          │
│            ACE COUNTS                            │
│            HI OR LO                              │
│            5 ♠ NEVER LOSE                        │
│            WHEN BUYING                           │
│                                                  │
│   SERIE   _ 2                                    │
│   KENT /3 _ 2                                    │
├──────────────────────────────────────────────────┤
│   × 5M   20M   × 5M                              │
│   S/N: 10M                                       │
│   4 OF A KIND WINS BONUS                         │
├──────────────────────────────────────────────────┤
│   [HOLD] [HOLD] [HOLD] [HOLD] [HOLD]   dimmed    │
│   [BIG ] [SMLL] [CXL ] [DEAL] [BET ]   BIG/SMALL │
│                                         active   │
│   [TAKE HALF]   ( MENU )   [TAKE SCORE]          │
└──────────────────────────────────────────────────┘
```

### 6.3 Round-by-round flow

**Entry:**

- Player wins base game and presses (or auto-enters) double-up, **or** chooses `TAKE HALF` which sends half the win into double-up.
- Cabinet transitions to double-up screen with **5 empty slots** (card backs) plus the **dealer card revealed in slot 1**.

**Per guess cycle:**

1. **Shuffle phase** — slot 2 (the next available slot) cycles rapidly through random card faces. Visually: the card image cycles every ~50-80 ms, giving the appearance of a shuffling reel.
2. **Player input** — player presses `BIG` or `SMALL`. The shuffle **stops on the next frame**, locking the card in.
3. **Reveal & resolve:**
   - **Guess correct** (challenger HI/LO of dealer matching guess): card stays in the slot; current double-up amount doubles; **next guess** uses this new card as the dealer for slot 3, and so on.
   - **Tie (equal rank):** rules vary by ruleset version (typically push or loss; check `Lucky5DoubleUpEngine.ResolveGuess` for current behaviour).
   - **Guess wrong:** if `IsNoLoseActive` (Lucky5/5♠ buy state) → `SafeFail` outcome, current accumulated amount returned; otherwise `Lose` → all credits forfeit.
4. **Special checks after each card lands** — on any of the 5 slots:
   - **4-of-a-Kind** appears across the populated slots (e.g., 4 Kings + 1 other) → award the **base 4-of-a-Kind paytable bonus** *in addition to* the doubled stake (this is **not** a jackpot trigger; it is the paytable multiplier × current `STAKE`).
   - **Other paytable hands** (Full House, Flush, Straight, etc.) — confirm against current rules whether they pay during double-up. Reference cabinet behaviour: 4-of-a-Kind specifically is the documented bonus.
5. **Slot 5 reached (5 cards filled):**
   - Round resolves; if any paytable bonus was hit, it is paid in addition to the double-up multiplier.
   - **If player did not press `TAKE SCORE` and did not lose:** cabinet transitions to a **new double-up screen** with the last card carried forward as the new dealer (or fresh dealer — confirm rule). Cycle repeats.

**Player options at any time:**

- `BIG` / `SMALL` — guess for the next card.
- `TAKE SCORE` — bank the current double-up amount + any paytable bonuses earned so far; return to idle.
- `TAKE HALF` — partial cashout (rules-dependent).
- `MENU` — open menu overlay (does not abandon the round).

### 6.4 The "5 ♠ NEVER LOSE WHEN BUYING" rule

Cyan rule text printed on the right side of the double-up screen:

```
HI LO GAMBLE
ACE COUNTS
HI OR LO
5 ♠ NEVER LOSE
WHEN BUYING
```

Meaning:

- **HI LO GAMBLE** — section title.
- **ACE COUNTS** — the Ace card (`Rank == 14`) triggers the Ace multiplier on the round payout. Rendered in `GameRound.AceMultiplier` and `AceMultiplierFired`.
- **Ace settlement invariant** — the Ace multiplier is applied to the base-game payout before payout scaling and stored in `GameRound.WinAmount`. Double-up starts from that stored `WinAmount`; it must not apply the Ace multiplier a second time.
- **HI OR LO** — guess direction labels.
- **5 ♠ NEVER LOSE WHEN BUYING** — if the player has activated the Lucky 5 buy/no-lose state (`IsNoLoseActive == true`) and the next dealt double-up card is the **5 of Spades**, a wrong guess does **not** forfeit credits; the round resolves as `SafeFail` returning the accumulated amount.
- **Availability invariant** — every positive base-game win remains eligible for double-up. RTP tuning must not hide or skip the double-up screen; balancing belongs in base-game scaling and bounded server-side double-up deck pressure.
- **Pressure invariant** — bounded pressure may remove high-leverage cards, sequence trap-heavy adjacent pairs during hot/near-close states, and release a small deterministic share of low-exposure chains for suspense. It must not change the displayed rules, duplicate cards, invent cards, block player choices, or make the cabinet feel like a dry spell followed by one sudden close.

**Visual binding:**

- When `IsNoLoseActive == true` → render the entire cyan rule panel at full brightness (or pulse the `5 ♠ NEVER LOSE` line).
- When inactive → render dimmed (50% alpha).

### 6.5 Backend DTO requirements (already partially present)

Required fields on `DoubleUpResultDto`:

```
RoundId, Status (Win|Lose|SafeFail|MachineClosed),
CurrentAmount, WalletBalance,
DealerCard, ChallengerCard,
SwitchesRemaining, IsNoLoseActive, IsLucky5Active,
SwapActivePosition, AceCard, AceMultiplier, AceMultiplierFired,
CardTrail : PokerCardDto[],          // ← THE 5-CARD ACCUMULATING BOARD
BoardHandRank : string?,             // ← e.g. "FourOfAKind" if formed
BoardBonusAmount : decimal,          // ← paytable × STAKE if hand formed
SlotIndex : int,                     // ← which slot just filled (1..5)
Noise : ...                          // existing pacing hint
```

`CardTrail` is the source-of-truth for the 5-slot rendering. The frontend client reads it and lays out cards left-to-right.

---

## 7. Card-Dealing Animation Spec (Classic Arcade Feel)

**Reference:** every classic arcade poker (IGT Game King, Aristocrat Reels, Lebanese cabinets) uses the same pattern.

### 7.1 Base game deal (5 cards)

| Step | Slot | Time (ms)  | Animation                                                                             |
| ---- | ---- | ---------- | ------------------------------------------------------------------------------------- |
| 0    | all  | 0          | All slots show `bside.png` card back (or middle = rotating FH face)                 |
| 1    | 1    | 0–250     | Slide from above, ease-out, lands face-down for 50 ms, then flip-Y reveal over 200 ms |
| 2    | 2    | 250–500   | Same                                                                                  |
| 3    | 3    | 500–750   | Same (replaces idle FH card)                                                          |
| 4    | 4    | 750–1000  | Same                                                                                  |
| 5    | 5    | 1000–1250 | Same                                                                                  |
| 6    | all  | 1300       | `PRESS HOLDS TO KEEP CARD` cyan text appears                                        |

**Sound:** `press.mp3` (single click) per card landing. Optional deeper "thud" on slot 5.

### 7.2 Draw (replace non-held cards)

Same animation per replaced slot, sequentially in slot order. Held cards remain untouched (the `HOLD` badge stays lit).

### 7.3 Double-up shuffle-and-stop

The defining feel of the double-up:

| Step                             | Time              | Animation                                                                 |
| -------------------------------- | ----------------- | ------------------------------------------------------------------------- |
| Card cycle                       | t=0 → indefinite | Slot 2 (or next empty slot) cycles random card faces every 60 ms          |
| Player presses `BIG`/`SMALL` | t=X               | Snap to current card frame + 1 (deterministic from server seed `Noise`) |
| Reveal                           | t=X → X+150      | Brief flash/glow on the locked card                                       |
| Resolve                          | t=X+150 → X+400  | Win: cyan glow + chime. Lose: red flash + buzzer. SafeFail: yellow flash. |
| Settle                           | t=X+500           | Card stays in slot, becomes new dealer, shuffle restarts on next slot     |

**Important:** the shuffle is **visual only**. The actual challenger card is determined server-side at `BIG`/`SMALL` press time using `RoundEntropySeed + sequence`. The shuffle stops on the server-determined card, regardless of which frame the visual cycle was on. The frontend client must use the `Noise` field from `DoubleUpResultDto` to deterministically replay the same shuffle sequence (for fairness audit / replay tests).

### 7.4 Win celebration

**Note: 1-Pair (and Jacks-or-Better) does not pay in Lucky5.** The minimum paying hand is **2 Pair**. The paytable rows below 2 Pair never light up.

| Hand rank                     | Visual                                                                                            |
| ----------------------------- | ------------------------------------------------------------------------------------------------- |
| 2 Pair / 3 of a Kind          | Paytable row inverts color, win amount counter ticks up                                           |
| Straight / Flush / Full House | Row inverts + cards glow gold + 3-tone chime                                                      |
| 4 of a Kind                   | `4 OF A KIND WINS BONUS` banner pulses yellow→white, cards shake, win amount roll-up over 1.5s |
| Straight Flush                | Cards glow rainbow, banner pulses, fanfare                                                        |
| Royal Flush                   | Full screen flash, all jackpots illuminate, extended fanfare 3s                                   |
| Kent (3rd in row)             | Kent counter flashes, jackpot value rolls into win                                                |

### 7.5 Settlement-drain animations (TAKE SCORE / TAKE HALF / Jackpot)

The defining "feel" of the cabinet at settlement is that the win value visibly **drains out of the paytable row** (the row that lit up at evaluation) directly **into the `CREDIT` counter** — there is **no separate score counter**. Three speeds:

| Settlement                                  | Animation                                                                                                               | Approx duration |
| ------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- | --------------- |
| `TAKE HALF`                                | **Instant snap.** Half the win is added to `CREDIT` immediately; the remainder enters double-up with no drain animation. | ~0 ms (instant) |
| `TAKE SCORE` (regular paytable win)        | The paytable amount counts down from full → 0 while `CREDIT` simultaneously counts up by the same amount. Linear/eased. | ~1.5–2.5 s     |
| `TAKE SCORE` of a **jackpot win**          | Same drain, but **slower and louder** — the jackpot pool counter (FH / 4OAK / SF / Kent) drains into `CREDIT` with held audio cue and paytable-row pulsing throughout. | ~4–6 s          |

**Implementation rule:** the drain is a single coupled animation — `paytableValue -= dx` and `creditCounter += dx` per frame, locked. Do **not** use a hidden score buffer or a separate counter widget.

**Audio:** `press.mp3` per ~10 cents tick during regular drain; sustained tone during jackpot drain; fanfare layers on top for `StraightFlush` / `RoyalFlush` / ranked `FullHouse` / Kent.

---

## 8. Asset Inventory (Legacy Decompiled Reference)

*Note: The following image-based assets (PNG/SVG) were part of the decompiled APK reference but are now **deprecated** in v8. We achieve visual parity entirely through CSS gradients, box-shadows, and DOM manipulation (e.g., cards are pure HTML/CSS, buttons are CSS gradients).*

Path (Historical): `c:\Users\Gabi.WIN-CD45QMUUPFF\Documents\GitHub\Lucky5-v7\ai9poker\root\assets\flutter_assets\assets\`

### 8.1 Card assets (Deprecated)
We no longer use `{rank}{suit}.png`. All cards are rendered procedurally via `_renderDomCard()` with ivory backgrounds and CSS pixel-font symbols.

### 8.2 UI buttons (Deprecated)
All buttons (DEAL DRAW, BET, HOLD, etc.) are now rendered via pure CSS using warm brown, red, and green linear gradients and `box-shadow` for the 3D bevel effect.

### 8.3 Cabinet chrome (Deprecated)
The wood-grain panel is now a CSS linear gradient on `.deck-background`.

### 8.4 Suit glyphs (Deprecated)
We use HTML unicode character glyphs and the pixel font instead of SVGs.

### 8.5 Fonts — `fonts/`

| Font                                                | Use                                                                                                         |
| --------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| `ARCADE.ttf`                                      | **The retro pixel arcade font.** Used for paytable, CREDIT, STAKE, KENT, SERIE, all in-game numerics. |
| `Impact.ttf`                                      | Display headers (rare)                                                                                      |
| `InterRegular/Medium/SemiBold/Bold/ExtraBold.ttf` | Lobby UI, menus, account screens (modern UI only —**never on the cabinet itself**)                   |

### 8.6 Sounds — `images/` (yes, MP3 lives next to images in this APK)

| Sound         | Use                        |
| ------------- | -------------------------- |
| `press.mp3` | Generic button press click |

(Additional sounds — deal, win, fanfare, jackpot — must be sourced separately or recorded; APK contains only `press.mp3`.)

### 8.7 Animated GIFs

| Asset                                         | Use                         |
| --------------------------------------------- | --------------------------- |
| `spinner.gif`                               | Loading spinner             |
| `treasurecoins.gif` / `treasureempty.gif` | Win celebration coin shower |

---

## 9. Identity Rules — Do Not Modernize

The Lebanese arcade cabinet identity must be preserved. **Reject any of the following changes:**

- ❌ Replacing `ARCADE.ttf` with a modern UI font on the cabinet face.
- ❌ Adding gradients, drop shadows, blur, or modern Material/Tailwind styling to the cabinet.
- ❌ Replacing the wood-grain button panel with a flat dark theme.
- ❌ Using SVG vector cards or modern flat card designs.
- ❌ Animating cards with bouncy modern springs (Material Motion / iOS) — must be classic snap/flip.
- ❌ Replacing the rotating FH card in the middle slot with a static label.
- ❌ Hiding the "4 OF A KIND WINS BONUS" banner or making it conditional.
- ❌ Removing the `5 ♠ NEVER LOSE WHEN BUYING` rule text from the double-up screen.
- ❌ Replacing the `KENT /3` counter with a modern progress bar.
- ❌ Using particle effects, parallax, or 3D card flips.

**Allowed modernization (Client quality upgrades):**

- ✅ Higher-resolution rendering of the same assets (point-filtering preserved for pixel font).
- ✅ Smooth interpolation of counter rollups (still using `ARCADE.ttf` and same color palette).
- ✅ Hardware-accelerated rendering of the same 2D scene tree.
- ✅ Better audio mixing (still using same sound effects; just clean playback).
- ✅ Touch + gamepad + kiosk-button input mapping to the same on-screen buttons.

---

## 10. Client DOM Tree (Required Structure)

```
res://scenes/cabinet/CabinetMain.tscn
├── PaytablePanel (Control, top)
│   ├── PaytableRows (VBoxContainer)
│   │   └── Row[0..8] : Label (ARCADE.ttf, color per rank)
│   ├── FullHouseHighlight : NinePatchRect (white border, position bound to JackpotFullHouseRank)
│   ├── CreditLabel : Label (top-right, green)
│   ├── StakeLabel : Label (top-right, yellow)
│   └── FullHouseJackpotTag : Label ("K  35,000,000")
│
├── PlayfieldPanel (Control, middle)
│   ├── CardSlot[0..4] : TextureRect
│   │   ├── CardSprite : Sprite2D (atlas frame from cards/)
│   │   ├── HoldBadge : Label (cyan "HOLD", visible if held)
│   │   └── DealAnimator : AnimationPlayer (slide+flip)
│   ├── HintLabel : Label ("PRESS HOLDS TO KEEP CARD" or "DOUBLE UP" etc.)
│   ├── SerieLabel : Label
│   ├── KentLabel : Label ("KENT /3 _ N")
│   └── DoubleUpRulesPanel : Control (visible only in double-up phase)
│       ├── HiLoGambleLabel : Label
│       ├── AceCountsLabel : Label
│       ├── HiOrLoLabel : Label
│       ├── FiveSpadeNeverLoseLabel : Label (modulated by IsNoLoseActive)
│       └── WhenBuyingLabel : Label
│
├── BonusBannerPanel (Control)
│   ├── PoolRow : HBoxContainer (× 5M | 20M | × 5M)
│   ├── SerialNumberLabel : Label ("S/N: 10,000,000")
│   └── FourOfAKindBanner : Label ("4 OF A KIND WINS BONUS")
│
├── ButtonPanel (Control, bottom)
│   ├── HoldRow : HBoxContainer
│   │   └── HoldButton[0..4] : TextureButton (hold_off/on)
│   ├── ActionRow : HBoxContainer
│   │   ├── BigButton : TextureButton (big/big_on)
│   │   ├── SmallButton : TextureButton (small/small_on)
│   │   ├── CancelHoldButton : TextureButton
│   │   ├── DealDrawButton : TextureButton
│   │   └── BetButton : TextureButton
│   └── SettlementRow : HBoxContainer
│       ├── TakeHalfButton : TextureButton
│       ├── MenuButton : TextureButton (menu.png)
│       └── TakeScoreButton : TextureButton
│
└── BackgroundLayer (CanvasLayer, behind all)
    ├── MachineFrame : Sprite2D (machine2.png)
    └── Lucky5Watermark : Sprite2D (lucky5.png, low alpha)
```

Every label/sprite reads from `CabinetStateStore.current_snapshot` via signals. **No scene script computes game outcomes.**

---

## 11. State → Visual Binding Table

| `CabinetSnapshot.game_state` | Playfield                                 | Hint label                 | Active buttons                                    |
| ------------------------------ | ----------------------------------------- | -------------------------- | ------------------------------------------------- |
| `idle`                       | `LUCKY 5` title, then delayed slot 2 FH card alone | ""                         | `BET`, `DEAL DRAW`                            |
| `dealing`                    | Cards animating in                        | "" (or muted)              | none                                              |
| `dealt`                      | 5 face-up cards                           | "PRESS HOLDS TO KEEP CARD" | `HOLD[0..4]`, `CANCEL HOLD`, `DEAL DRAW`    |
| `drawn`                      | 5 final cards, paytable row lit           | win amount                 | `TAKE HALF`, `TAKE SCORE`, `BIG`, `SMALL` |
| `double_up_staging`          | Dealer card + cycling slot                | "DOUBLE UP"                | `BIG`, `SMALL`, `TAKE SCORE`, `TAKE HALF` |
| `double_up_revealed`         | Card just locked, glow                    | win/safefail/lose flash    | (transient)                                       |
| `complete`                   | Settlement summary                        | ""                         | none                                              |
| `closed`                     | Machine closed overlay                    | "MACHINE CLOSED"           | `MENU` only                                     |

## 12. Common Misunderstandings (FAQ)

**Q: Is the double-up just a Hi-Lo guess?**
A: **No.** It is a 5-card progressive bonus board. See §6.

**Q: Does the dealer card change every guess?**
A: Yes — after a correct guess, the just-dealt card becomes the new dealer for the next guess. The 5 cards accumulate visually across the slots.

**Q: What does "ACE COUNTS" mean?**
A: When an Ace appears as the dealt double-up card, it triggers the Ace multiplier (`AceMultiplierFired = true`), multiplying the round payout. The visible rule text reminds the player.

**Q: Can the player form a Royal Flush on the double-up board for the jackpot?**
A: **No** — paytable hands formed during double-up pay the **base paytable**, not jackpots. Jackpots are reserved for the base 5-card draw round. (Confirm exact rule against `Lucky5DoubleUpEngine` resolution code; this matches reference cabinet behaviour.)

**Q: What happens if the player gets 4-of-a-Kind on the double-up board?**
A: They get the standard 4-of-a-Kind paytable amount × `STAKE` paid in addition to the doubled stake. They may continue or `TAKE SCORE`.

**Q: When is `5 ♠ NEVER LOSE` active?**
A: When `IsNoLoseActive == true` (set when the player triggers the Lucky 5 buy or earns the no-lose bonus per the engine's rules). The cyan rule text is rendered at full brightness; otherwise dimmed.

**Q: What is a Kent?**
A: A straight where the cards are in **sequential positional order** in their slots (e.g., slots 1..5 = `2,3,4,5,6` ascending). Three Kents in a row → Kent jackpot. Counter shown as `KENT /3 _ N`.

**Q: What is the rotating Full House rank?**
A: The rank (2 through Ace) that currently has the Full House jackpot armed. Rendered as a face-up card in the middle slot when idle, and as a small `K  35,000,000` tag on the paytable. Rotates per game policy in `MachineLedgerState.JackpotFullHouseRank`.

**Q: Why does the middle slot show a card when idle?**
A: It signals the currently armed Full House rank to the player (e.g., *"Full House of Kings is paying 35M right now"*). Replaced by the dealt card on `DEAL DRAW`.

---

## 13. Acceptance Checklist — Client Cabinet Parity

The HTML/CSS migration is considered visually correct only when **all** of the following are true:

- [ ] Idle state shows rotating FH face-up card in middle slot (slot index 2).
- [ ] All other idle slots show `bside.png`.
- [ ] Paytable uses `ARCADE.ttf` at 1:1 pixel-perfect scale (or integer-multiple).
- [ ] Currently-armed Full House row has a white border (NinePatchRect).
- [ ] `K  35,000,000` jackpot tag matches `JackpotFullHouseRank` + `JackpotFullHouse`.
- [ ] Deal animation: cards arrive one-at-a-time, slot 1 → slot 5, ~250ms each, slide-from-top + flip.
- [ ] Held card shows `HOLD` cyan label below it.
- [ ] Cyan `PRESS HOLDS TO KEEP CARD` appears post-deal.
- [ ] `KENT /3 _ N` counter displays current Kent streak.
- [ ] `4 OF A KIND WINS BONUS` banner is always visible during base game and double-up.
- [ ] Pool row (×5M | 20M | ×5M | S/N: 10M) is always visible.
- [ ] Double-up screen: dealer card + 4 empty slots + shuffle animation on next slot.
- [ ] Cyan rule text (HI LO GAMBLE / ACE COUNTS / HI OR LO / 5♠ NEVER LOSE / WHEN BUYING) visible.
- [ ] `5 ♠ NEVER LOSE` line modulates by `IsNoLoseActive`.
- [ ] Shuffle stops deterministically based on server `Noise` field.
- [ ] Locked card glows briefly, then becomes the new dealer.
- [ ] After 5 cards filled, transition to next double-up screen if player did not `TAKE SCORE`.
- [ ] 4-of-a-Kind on the double-up board triggers paytable bonus pay-out + banner pulse.
- [ ] Win celebration matches §7.4 per hand rank.
- [ ] Lebanese arcade identity preserved per §9.
- [ ] All buttons use the exact `*_on.png` pressed states.
- [ ] `press.mp3` plays on every button press.
- [ ] No modern UI flourishes (gradients, blurs, springs).

---

## 14. References

- Live capture session: `https://ai9poker.com/install` Lucky 5 Poker variant, 2026-05-05.
- Decompiled reference APK: `c:\Users\Gabi.WIN-CD45QMUUPFF\Documents\GitHub\Lucky5-v7\ai9poker\`.
- Backend authority: `c:\Users\Gabi.WIN-CD45QMUUPFF\Documents\GitHub\Lucky5-v7\server\src\Lucky5.Domain\Game\CleanRoom\`.
- Existing forensics:
  - `docs/forensics/live_protocol_2026-05-02.md`
  - `docs/forensics/gameplay_event_catalog.md`
  - `docs/forensics/indexed_gameplay_pacing_2026-04-05.md`
- Migration plan: `docs/GODOT_CABINET_MIGRATION.md`.
- Game-feel reference (older): `docs/GAME_FEEL_REFERENCE.md`.

---

## 15. Document Maintenance

Update this file when:

- A new mechanic is observed in the live reference cabinet that is not described here.
- The clean-room engine adds a new field that affects visual rendering.
- A frontend view is implemented and discovers a parity gap.
- A previously-incorrect understanding is corrected.

**Do not delete sections without leaving a redirection note.** Other agents rely on this as the authoritative reference.

---

## 16. Machine Close, Serial Number, and Win-Floor Invariants

Three operational invariants the cabinet must enforce, captured here so they don't get lost across context changes:

### 16.1 40M credit machine-close cap

- A machine **stops accepting play once `MachineSession.MachineCredits >= 40_000_000`** (`EngineConfig.CloseThreshold`).
- The cabinet must show a **MACHINE CLOSED** overlay. Lobby/menu controls stay available, and the player can explicitly `TAKE SCORE` / `CASH OUT`; bet, cash-in, deal, hold, and double-up actions stay disabled until the closed session has been paid out.
- A player reset or closed-machine reopen **must not auto-cash-out machine credits**. Positive machine credits remain in the closed session and block further play until the player explicitly cashes out to wallet. Zero-credit resets may still clear an empty machine.
- The two highest-leverage paths to hitting the cap are:
  1. **Straight Flush jackpot** — biggest single payout event in the game (currently `JackpotStraightFlushCap = 7,500,000`, but the live capture showed `S/N: 10,000,000` aligned with this pool; values rotate per machine).
  2. **Ranked Full House jackpot** — the rank-armed Full House (`JackpotFullHouse`, e.g., 35M at capture).
- Both jackpot drains take the slow `~4–6 s` settlement animation (§7.5). Hitting either typically pushes a near-cap session over the 40M close threshold without needing chained double-up wins.
- The Web cabinet currently mis-keys close detection on `profile.walletBalance` (`lucky5-cabinet.tsx:258`); the correct axis is `MachineCredits` (chips currently on the machine), not the player's overall wallet. Fix at next pass.

### 16.2 S/N — Serial Number row

- `S/N` is the **machine's iconic serial number**, rendered as a numeric label in the bonus banner row (e.g., `S/N: 10,000,000`).
- Source field: `MachineLedgerState.MachineSerial` (already present, already wired through `JackpotInfoDto.MachineSerial`).
- The cabinet's `MachineInfoBlock` (`src/web/components/lucky5-cabinet.tsx`) **does not currently render S/N**. Add a row beneath the SERIE / KENT identity row.
- This is presentation-only; it has no gameplay effect — it preserves the cabinet's classic identity feel.

### 16.3 Win floor — 2 Pair and above (no 1-Pair, no Jacks-or-Better)

- The Lucky5 paytable starts at **2 Pair** and goes up to **Royal Flush**. There is **no `1-Pair` row** and **no `Jacks-or-Better` row**.
- Already enforced in code: `PaytableProfile.Lebanese` excludes `OnePair` from its `Payouts` dict and sets `MinimumPairRankForPayout = int.MaxValue` (`server/src/Lucky5.Domain/Game/CleanRoom/CoreModels.cs`).
- Web cabinet `PAYTABLE_ROWS` (`src/web/components/lucky5-cabinet.tsx:43-52`) correctly omits 1-Pair and JoB.
- **Do not regress this.** Any future "make double-up easier" idea must not lower the base-game win floor.

### 16.4 Simulation fidelity checkpoints

The server-side Monte Carlo harness (`server/src/Lucky5.Simulation/Program.cs`) is expected to mirror the current backend economics before it is used for RTP claims:

- Completed hands count both live stakes: one at deal and one at draw.
- Base wins apply the Ace multiplier once, then scale the payout, then store the result as the double-up entry amount.
- Jackpot telemetry separates ranked Full House, 4OAK-A, 4OAK-B, Straight Flush, and the current backend Kent pool.
- Double-up telemetry records offers, accepts, win/loss/safe-fail outcomes, dealer switches, take-half events, settlement deltas, and machine-close source channels.
- Counterplay runs must include intentionally bad holds and wrong-way double-up guesses, plus the current `CounterplayScore >= 3` cold-to-neutral policy override.

---

## 17. Revision Log

| Date       | Change                                                                                                                                                                                                                                  |
| ---------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-06-04 | Clarified suspense-aware double-up pressure: always-on availability, bounded key-card removal, high-pressure sequencing, deterministic low-exposure release, and no duplicated/synthetic cards. |
| 2026-06-04 | Clarified double-up availability and RTP tuning: every positive base-game win remains eligible for double-up; balancing uses base-game scaling and bounded server-side double-up deck pressure. |
| 2026-06-04 | Clarified 40M close persistence: reset/reopen does not auto-cash-out positive machine credits; closed sessions remain blocked until explicit player cash-out. |
| 2026-05-05 | Initial authoritative capture from `ai9poker.com/install`.                                                                                                                                                                            |
| 2026-05-05 | Corrected §5 Kent: progressive (no reset on non-Kent, only resets on jackpot payout); ascending **or** descending qualifies. Added §4.1 player-initiated FH-rank switch (HOLD[0]). Added §7.5 settlement-drain animation tiers. Added §16 (40M cap, S/N, win floor). Removed duplicate §5. |
