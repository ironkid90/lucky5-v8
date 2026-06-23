# Game Feel Reference

Primary source: the local gameplay recording set, with curated sample frames tracked in `docs/assets/recording/`.

Secondary source: the AI9 Poker reference capture in [`docs/assets/ai9poker-reference-current-2026-06-04.jpg`](assets/ai9poker-reference-current-2026-06-04.jpg), which remains the closest existing implementation to the target feel.

## Capture Metadata

- duration: `00:11:19`
- orientation: portrait
- frame size: `720x1280`
- frame rate: `30 fps`

## Visual Direction

- black CRT-like playfield with minimal chrome
- rainbow pixel paytable fixed at the top-left
- credit counter fixed at the top-right; stake and wallet details live in the menu
- oversized card row centered in the upper-middle
- warm brown control deck occupying the lower third
- beveled, glowing cabinet buttons instead of flat mobile controls

## ai9poker Clone Visual Reference (Authoritative Target)

The clone at ai9poker.com is the closest existing playable reference for our target aesthetic. The older 720x1280 recording remains useful for color, button, and cabinet proportions; the 2026-06-04 live capture and `temp/main.dart.js` source bundle supersede it for double-up board and idle timing details.

### Layout Zones (top to bottom)

1. **Paytable** — Fixed top-left, always visible
   - 8 rows in rainbow pixel font, each with hand name (left-aligned) and payout amount (right-aligned)
   - Row colors (from top): RF=red/white, SF=red, 4K=cyan, FH=yellow, Flush=red, Straight=green, 3K=cyan, 2P=yellow
   - Active jackpot hand has a **solid box/selection highlight** around the text (not just a glow)
   - Payout values update dynamically based on stake: RF×1000, SF×75, 4K×15, FH×12, Fl×10, St×8, 3K×3, 2P×2

2. **Credit Meter** — Top-right area
   - "CREDIT" label in green, value in white below
   - Stake, wallet, bonus credit, and cash-in totals are consolidated into the menu so only credit remains outside during play
   - Uses pixel font at ~18px

3. **Card Area** — Center of screen, large
   - During normal play: 5 cards across
   - During double-up: **one visible five-slot deck row** with the current dealer/revealed trail in the left slots, the active reveal slot shuffling/asking `BIG / SMALL ?`, and unused slots dimmed as backs
   - During idle: black CRT field shows the `LUCKY 5` title first; after the idle delay, show only the armed Full House rank card in the middle slot
   - Card art is procedurally rendered via DOM/CSS (ivory background, gold inner rings, pixelated font for suits/ranks). There are no external PNG images used.

4. **Paytable Win Drain** — No separate winnings HUD
   - Active winnings appear on the matching paytable row, then drain from that row into the credit counter
   - Do not render a separate winnings score below the card area

5. **Jackpot Info Block** — Below cards, above controls
   - "SERIE - 1" in green text
   - "KENT /3 : 1" in green text  
   - Three jackpot counter values in a row: "× 368977" / "10000000" / "999999" in amber/gold
   - "S/N: 4500000" in green text
   - "4 OF A KIND   WINS BONUS" in large white pixel text, full width

6. **Control Deck** — Bottom third
   - Warm brown wooden surface gradient (not flat dark)
   - Three rows of chunky beveled buttons with 3D depth (shadow below each)

### Button Colors (CRITICAL — differs from most poker games)

| Button | Background | Text | Shadow |
|--------|-----------|------|--------|
| HOLD ×5 | Amber gradient (#e8a020 → #b87818) | Black | Dark brown |
| BIG | Amber gradient (same as HOLD) | Black | Dark brown |
| SMALL | Amber gradient (same as HOLD) | Black | Dark brown |
| CANCEL HOLD | Cream/beige gradient (#e8dcc8 → #c8b8a0) | Dark grey | Tan |
| **DEAL DRAW** | **RED gradient (#ee4444 → #cc2222)** | **White** | **Dark red** |
| **BET** | **GREEN gradient (#44cc44 → #228822)** | **White** | **Dark green** |
| TAKE HALF | Red gradient (same as DEAL DRAW) | White | Dark red |
| MENU | Dark circle (#333 → #1a1a1a) | Grey | Black |
| TAKE SCORE | Orange/amber gradient (warm, brighter than HOLD) | Black | Brown |

> **NOTE**: DEAL DRAW is RED and BET is GREEN. This is the opposite of many Western video poker machines. The Lebanese cabinet lineage uses this color convention.

### Paytable Values (Lebanese Profile at Stake 5,000)

| Hand | Multiplier | Pay at 5K |
|------|-----------|-----------|
| Royal Flush | 1000× | 5,000,000 |
| Straight Flush | 75× | 375,000 |
| 4 of a Kind | 15× | 75,000 |
| Full House | 12× | 60,000 |
| Flush | 10× | 50,000 |
| Straight | 8× | 40,000 |
| 3 of a Kind | 3× | 15,000 |
| 2 Pair | 2× | 10,000 |

### Jackpot System

- Three visible progressive jackpot counters in the info area
- "4 OF A KIND WINS BONUS" message displayed prominently when 4K jackpot is active
- Full House jackpot has a selectable rank (A, K, Q, J, etc.) — indicated by highlight box on paytable row
- Active 4K slot alternates between A and B slots
- SERIE / KENT / S/N are machine identity counters (session/series tracking)
- Jackpot display is always visible even during double-up

### Double-Up Mode

- Five-slot progressive board displayed in the same cabinet rhythm as base cards
- Dealer/result cards remain in the visible trail; the next available slot shuffles until the backend returns the challenger card
- "HI LO GAMBLE" and "ACE ALWAYS WINS" text visible
- Card shuffle animation while waiting for player choice
- BIG = 8 or higher wins, SMALL = 6 or lower wins (7 is a push/lose depending on config)
- Lucky 5 in the shipped clone is now restricted to the SWITCH path: switching onto 5♠ triggers the 4× multiplier + no-lose safety net, while an opening dealer 5♠ or a revealed BIG/SMALL result 5♠ does not
- TAKE HALF and TAKE SCORE always accessible during double-up

### Gameplay Pace

- Deal animation: cards drop sequentially with ~180ms stagger — each card visibly lands before the next one starts, like a mechanical dealer
- Draw: non-held cards flip out, new cards flip in with ~100ms stagger between slots
- Double-up transition: ~800ms after win before auto-entering DU mode
- Card shuffle in DU: random card faces cycling at ~130ms per frame — visible reel-spin effect
- Win collection: animated credit drain from win display into credit counter — scales with amount (~1.5s at 500K, ~60s at 40M)
- Losing card display: the losing challenger card is shown for ~1.5s before siphon begins — player always sees what beat them
- Jackpot drain: freezes everything, jackpot counter drains over ~60s for 40M — player watches their winnings tick in
- Auto-siphon on machine close: starts immediately, no player input needed — the machine handles it
- No excessive pauses or modal dialogs — everything flows within the cabinet screen
- All timing values are centralized in `game-config.js` GAME_CONFIG.timing — single source of truth

## Interaction Cues

- the game keeps the paytable visible during play
- `BIG` and `SMALL` live on the main control deck, not in a detached modal flow
- `TAKE HALF` and `TAKE SCORE` are first-class cabinet actions
- the title / idle state reuses the same machine screen instead of switching to a modern menu shell

## Reconstruction Constraints

- keep the cabinet silhouette intact
- keep portrait-first ergonomics
- use pixel or pixel-adjacent typography for paytable and status text
- keep the control deck physically chunky and color-coded
- avoid generic lobby UI, chip stacks, glossy casino theming, or modern card-table layouts
- match the ai9poker clone's button color conventions (DEAL=red, BET=green)
- match the Lebanese paytable multiplier profile exactly
- jackpot info block must be visible and always present (not hidden behind tabs)

## Sample Frames

### 00:00:30

![Frame at 00:00:30](assets/recording/frame-30s.png)

Notes:

- five-card playfield visible
- `DOUBLE UP` cue present
- amber button deck and red `DEAL / DRAW` button are clearly established

### 00:02:00

![Frame at 00:02:00](assets/recording/frame-120s.png)

Notes:

- card art remains crisp and simple
- score drains through the paytable row; credit remains persistent while stake stays in the menu
- there is no extra HUD clutter beyond machine essentials

### 00:06:00

![Frame at 00:06:00](assets/recording/frame-360s.png)

Notes:

- idle/title mode still lives inside the cabinet screen
- machine identity is embedded into the playfield, not separated into a branding screen
- bottom controls remain visible even when no active hand is on screen
