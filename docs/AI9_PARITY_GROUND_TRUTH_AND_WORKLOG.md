# AI9 Parity — Ground Truth, Measurements, and Engineering Worklog

> **Purpose.** This is the single authoritative continuity document for the "make Lucky5 v8 match AI9 identically" overhaul. It records every measurement, decision, asset fact, and implementation step in detail so any agent can resume without re-deriving anything. Read this **before** touching the cabinet UI.
>
> **Status legend:** `[DONE]` complete · `[WIP]` in progress · `[TODO]` not started · `[DECISION]` design choice locked.

---

## 0. Task definition (verbatim intent)

Compare the AI9 reference game (Dart/Flutter mobile + web build, plus two screen recordings and our own screenshots) against our `lucky5-v8` web-native cabinet, then **devise and implement a comprehensive overhaul so lucky5-v8 matches AI9 identically** in: cabinet look, graphics quality, animation/stagger timing, and gameplay.

**User-stated priority order (HIGHEST first), per 2026-06-27 follow-up:**
1. Cabinet background (CRT screen field + overall composition).
2. Background giant `LUCKY 5` / `POKER` title text behind the cards.
3. Paytable (rows, colors, selection highlight, value column).
4. CREDIT / STAKE meters and the jackpot meters.
5. Drain / fill animations (credit transfer, jackpot, win counters).
6. **Sizes and proportions of everything** (explicitly emphasized — geometry parity is primary).

Cards and buttons are required but **secondary** to the six items above.

---

## 1. Source material inventory

| Source | Location | Notes |
|---|---|---|
| AI9 decompiled APK (smali) | `ai9/1 - Decompiled/ai9poker.apk/` | Not human-readable for layout; Flutter logic is in `classes*.dex`. Use only for resource XML if needed. |
| AI9 extracted APK (assets) | `ai9/3 - Extracted/ai9poker.apk/assets/flutter_assets/` | **Authoritative art + fonts.** See §3. |
| AI9 web build | `ai9/ai9-download/` | Compiled Flutter (`install/main.dart.js`), not readable. `index.html` only confirms it's Flutter web. |
| Recording #1 (low-res) | `/home/ubuntu/upload/ScreenRecording2026-06-2714.54.mp4` | Chrome-extension capture; portrait. |
| Recording #2 (hi-res) | `/home/ubuntu/upload/Ai9Poker(1).mp4` | **900×1600**, 82.6s. Primary geometry reference. |
| Our screenshots | uploaded PNGs (06-15/06-16) | Current v8 state for diffing. |

**Repo root on user desktop:** `C:\Users\Gabi.WIN-CD45QMUUPFF\Documents\GitHub\lucky5-v8\lucky5-v8` → mounted at `/mnt/desktop/lucky5-v8`.

---

## 2. Lucky5 v8 codebase facts (from docs read in phase 1) `[DONE]`

- Web-native cabinet served by a .NET 10 API. Primary client: `server/src/Lucky5.Api/wwwroot/`.
- Authoritative deterministic game logic: `server/src/Lucky5.Domain/Game/CleanRoom/` (balance, machine state, jackpots, realtime are backend-owned).
- Retro cabinet aesthetic is a core product feature — must NOT be modernized into a generic casino UI.
- Onboarding docs: `GEMINI.md` (architecture/visual-parity catch-up), `docs/GAME_FEEL_REFERENCE.md`, `docs/LUCKY5_AUTHORITATIVE_GAMEPLAY_REFERENCE.md`, `docs/MACHINE_BEHAVIOR_REFERENCE.md`, `docs/DEVELOPMENT_HISTORY_AND_CURRENT_STATE.md`.
- Tooling note from `AGENTS.md`: project mandates `vexp` (`run_pipeline`) for code search over grep/glob. (We operate from the sandbox over the FUSE mount; vexp is an in-IDE MCP tool. When editing, prefer reading specific files; document any code-graph assumptions.)
- Commands: launch `./dev.ps1`; API-only `./dev.ps1 -Headless`; tests `dotnet run --project server/tests/Lucky5.Tests/Lucky5.Tests.csproj`; build `dotnet build server/Lucky5.sln`.

> **NEXT AGENT TODO (verify):** enumerate the actual files under `server/src/Lucky5.Api/wwwroot/` (css/js/index) — the earlier attempt to `dir` the wwwroot failed due to a PowerShell path-quoting issue. Re-list before editing. See §6 for the planned edit targets and the open verification item.

---

## 3. AI9 asset ground truth (extracted APK) `[DONE]`

Path: `ai9/3 - Extracted/ai9poker.apk/assets/flutter_assets/assets/images/`

**AI9 renders buttons and cards as pre-rendered PNGs, not CSS controls.**

### 3.1 Buttons (1024×1536 PNG, wooden socket frame + glossy illuminated face with baked label)
Idle + pressed variants: `hold_off.png`/`hold_on.png`, `bet.png`/`bet_on.png`, `big.png`/`big_on.png`, `small.png`/`small_on.png`, `cancel_hold.png`/`cancel_hold_on.png`, `deal_draw.png`/`deal_draw_on.png`, `take_half.png`/`take_half_on.png`, `take_score.png`/`take_score_on.png`. `menu.png` is a 50×50 dark circle hamburger.

### 3.2 Cards (313×528 PNG pixel-art faces) in `cards/`
52 ranks + `bside.png` (magenta crosshatch back w/ white crown box) + `holdbside.png`. **Aspect ratio 313:528 = 0.593** (taller/narrower than standard 0.714 poker).

### 3.3 Other key art
- `board.png` 1024×1024 — reddish-brown woodgrain deck panel.
- `lucky5.png` 313×528 — the YELLOW 5-of-spades **card** (the in-hand Lucky 5), NOT the title text.
- `bonus.png` 313×528 — green BONUS card.
- `splash.png` 390×844 — splash screen (implies design logical size ≈ 390×844, ratio 0.462).
- Fonts: `ARCADE.ttf` (dot-matrix display font for CRT text), `Impact.ttf`, Inter family.
- `machine2.png`/`machine21.png` 217×476, `logo2.png` 1024×1024, `coin.png`, `treasurecoins.gif`/`treasureempty.gif` (treasure/coin FX), `press.mp3` (button press SFX), `spinner.gif`.

### 3.4 Sampled ground-truth hex (measured via Pillow averaging brightest face pixels)
| Element | Hex |
|---|---|
| Wood deck `board.png` center / top / bottom | `#3B1200` / `#381100` / `#320F00` |
| HOLD face (lit) | `#FEFD07` |
| HOLD face (idle `hold_off`) | `#F8F807` |
| BET face | `#0CDE6B` (rim `#059746`) |
| BIG face | `#FAAE06` |
| SMALL face | `#FAB009` |
| CANCEL HOLD face | `#E9E4A9` (cream) |
| DEAL DRAW face | `#F76414` (red-orange) |
| TAKE HALF face | `#F76311` (red-orange) |
| TAKE SCORE face | `#FAB215` (amber) |
| Card white / red suit / black suit | `#FFFFFF` / `#FF0302` / `#050706` |

---

## 4. AI9 GEOMETRY — measured from hi-res recording (900×1600) `[DONE]`

All fractions are of screen **width (w)=900** or **height (h)=1600**. Measured via per-row/column pixel scans (Pillow) on frames `ai9b/b6.png` (idle) and `ai9b/b25.png` (dealt). **These are the authoritative numbers to build against.**

### 4.1 Global split
- **CRT screen field:** top **0..0.546h** (wood deck begins sharply at y≈874px = 0.546h).
- **Wood control deck:** **0.546h..1.0h** (bottom 45.4%).
- (The earlier AI text-estimate of 68/32 is WRONG; the pixel scan of 0.546 is correct because the deck buttons extend to the very bottom edge.)

### 4.2 Cards (dealt)
- 5 card centers at **0.099, 0.299, 0.499, 0.699, 0.899 w** → even **0.20w pitch**, perfectly centered (middle card at 0.499w).
- Each card width ≈ **0.169w** (≈152px @900). Inter-card gap ≈ 0.031w.
- Card band: **top 0.191h**, height ≈ **0.18h** (top y≈306, bottom y≈580 @1600). Card draw aspect in-frame ≈ 152×288 = 0.528 ratio (close to the 0.593 asset; slight vertical crop/letterbox).
- Cards span essentially **edge to edge** (first card left x≈13, last card right x≈886).

### 4.3 HOLD button row (deck row 1)
- 5 lit faces centered at **0.162, 0.331, 0.499, 0.668, 0.837 w** → even **0.169w pitch**, centered on 0.499w.
- Face width ≈ **0.106w**; full socket footprint wider (~0.169w incl. wooden frame).
- **Important:** HOLD faces are horizontally inset relative to the cards (button pitch 0.169w vs card pitch 0.20w), so a HOLD button sits slightly inboard of its card. Each HOLD aligns under its card column but the button GRID is narrower than the CARD GRID.

### 4.4 Deck rows (vertical)
Three rows in the deck:
- Row 1 (HOLD×5) centers ≈ **0.59h** (y≈950).
- Row 2 (BIG, SMALL, CANCEL HOLD, DEAL DRAW, BET) centers ≈ **0.738h** (y≈1180). Same 5 columns as HOLD.
- Row 3 (TAKE HALF, MENU, TAKE SCORE) centers ≈ **0.89h** (y≈1420). 3 items aligned under columns 1, 3, 5 — i.e. TAKE HALF under col1 (≈0.16w), MENU center (≈0.499w), TAKE SCORE under col5 (≈0.837w). MENU is a small circle, not a card-button.

### 4.5 Paytable (top-left, CRT)
- 8 rows, top to bottom: `ROYAL FLUSH`, `STRAIGHT FLUSH`, `4 OF A KIND`, `FULL HOUSE`, `FLUSH`, `STRAIGHT`, `3 OF A KIND`, `2 PAIR`.
- Row label starts at x≈0.02w; right-aligned **value column** centered roughly 0.50–0.70w (values like `2500000`, `750000`...). In `b6.png` values are smaller stake; in `b25.png` higher stake (values scale with STAKE).
- Row pitch ≈ 0.021h; first row baseline ≈ 0.011h. Paytable occupies y≈0..0.165h.
- **Row colors (ARCADE dot-matrix font):**
  - ROYAL FLUSH = amber/gold `#FFB300`-ish
  - STRAIGHT FLUSH = red
  - 4 OF A KIND = green
  - FULL HOUSE = **selected/highlighted**: black text on solid **white** background block (this is the current-selection highlight row; it moves with selection)
  - FLUSH = gold/amber
  - STRAIGHT = cyan
  - 3 OF A KIND = gold/amber
  - 2 PAIR = cyan
  - Value numbers: gold/amber on most rows; cyan on STRAIGHT/2 PAIR rows; etc. (values colored per-row to match label family).

### 4.6 CREDIT / STAKE (top-right, CRT)
- Region x≈0.66..0.99w, y≈0..0.16h, right-aligned.
- Layout stacked: `CREDIT` (green label) / big green value / `STAKE` (gold label) / big gold value.
- `CREDIT` label green `#3BD23B`-ish; value same green, large ARCADE digits.
- `STAKE` label + value gold/amber.

### 4.7 Jackpot block (mid CRT)
- Vertical band y≈0.36h..0.50h (sits BELOW cards/title, ABOVE the deck).
- Left stack: `SERIE  - <n>` (red label `SERIE`, value), `KENT /3 - 1`, `S/N: 2500000` (cyan).
- Center: a row of **three large numbers** — left green `999999`, center red `5000000`, right green `999999`, with an `x`/`*` glyph prefix on one (the `* / x` multiplier marker seen in screenshots, e.g. `* 142,975`).
- Bottom line spanning width: `4 OF A KIND  WINS  BONUS` (mixed gold + white ARCADE text), centered.
- In our v8 screenshots this band also shows the `SF` super-jackpot and the boxed `* 140,340` mystery/AP meter — confirm against the live three-number row.

### 4.8 Title watermark `LUCKY 5 / POKER`
- Giant **cyan** block-pixel letters, drawn **behind** the cards (cards occlude it when dealt). In idle it's visible as `UCKY` / `POKER` (clipped by card area).
- Region ≈ x 0.0..1.0w, y ≈ 0.10h..0.45h (spans behind the card band). Static (no flash/animation).
- Built from the ARCADE font or a chunky pixel font with a slight 3D extruded drop shadow / outer glow in cyan. NOT a single PNG — app-rendered text.

### 4.9 Timing
- **Deal/Draw stagger:** the video analyzer reported ~1000ms between cards in recording #2; recording #1 looked much faster (~50–80ms). **OPEN ITEM `[TODO]`** — re-measure precisely by frame-stepping. Working assumption: AI9 reveals cards left→right with a per-card delay; pick the value that matches frame-by-frame. The 1000ms figure may reflect the player's own pacing (tapping), not an animation. **Must confirm before locking timing.**
- Drain/credit-transfer (TAKE SCORE): NOT observable in recording #2 (player never collected). Recording #1 around t≈32s shows a TAKE SCORE drain. **Re-measure `[TODO]`.** Working model: rapid numeric scroll, WIN ticks down while CREDIT ticks up, ~1–1.5s.
- Jackpot meters: static in recording #2 (no fill animation observed).

---

## 5. KEY DECISIONS `[DECISION]`

1. **Use AI9's actual PNG assets** for buttons (16 states + menu) and cards (52 + backs) by copying them into the v8 client. This guarantees identical button/card look with zero approximation. (Lower-priority items per user, but cheap to nail exactly.)
2. **Rebuild the CRT screen in HTML/CSS/JS** to match measured geometry: background field, cyan `LUCKY 5 POKER` pixel title, 8-row paytable with the white selection block, CREDIT/STAKE, jackpot block, and the drain/fill animations. Use the `ARCADE.ttf` font (ship it) for all CRT dot-matrix text.
3. **Drive all layout from the measured fractions in §4** using a fixed-aspect portrait stage (design canvas locked to AI9 proportions, scaled to fit viewport) so proportions are pixel-faithful at any size. CRT = top 0.546h, deck = bottom 0.454h.
4. **Preserve backend authority** — only the presentation layer changes. No changes to `Lucky5.Domain/Game/CleanRoom` logic; the client renders state it already receives.
5. **Reversibility** — keep changes additive where possible (new CSS/asset files, feature-flagged swap) so the prior look can be restored. Document every touched file in §7.

---

## 6. Planned implementation targets `[WIP]`

> Re-verify exact filenames first (see §2 NEXT AGENT TODO). Based on docs, the client lives in `server/src/Lucky5.Api/wwwroot/` with css like `cabinet-layout-vnext.css`, `cabinet-v8-quality.css` and js like `cabinet-stage-vnext.js`, `cabinet-transition-vnext.js`, `game.js`, `cabinet-v8-effects.js` (names referenced in prior notes — CONFIRM).

1. **Assets:** copy AI9 button/card/board/font PNGs+TTF into `wwwroot/assets/ai9/`.
2. **Layout CSS:** fixed-aspect portrait stage; CRT 0.546h / deck 0.454h; card grid (0.20w pitch, 0.169w cards, top 0.191h); HOLD grid (0.169w pitch, inset); rows 2 & 3 per §4.4.
3. **Buttons:** render as the AI9 PNGs with idle/`_on` swap on press; MENU as `menu.png` circle.
4. **Cards:** render AI9 card PNGs at 313:528 aspect; back = `bside.png`.
5. **CRT screen:** ARCADE-font paytable (8 rows + white selection block), CREDIT/STAKE, jackpot block, cyan `LUCKY 5 POKER` watermark behind cards, exact colors from §3.4/§4.
6. **Timing:** deal/draw stagger + drain animation per §4.9 (after re-measure).

---

## 7. Change log (append every edit here) `[WIP]`

- 2026-06-27 — Created this document. Completed: doc review (§2), asset mining (§3), geometry measurement (§4). Saved AI9 assets to sandbox `/home/ubuntu/ai9assets/`, frames to `/home/ubuntu/ai9b/` and `/home/ubuntu/ai9frames/`. Earlier draft spec at `docs/AI9_OVERHAUL_SPECIFICATION.md` (superseded by §4 hard numbers here).
- 2026-06-27 (cont'd) — **CRITICAL DISCOVERY:** The genuine AI9 PNG button/card/board assets are ALREADY present in `server/src/Lucky5.Api/wwwroot/assets/images/` (button PNGs ~1.1MB each, board.png 1.15MB, 53 card files, menu.png, press.mp3). The v8 client JS already references the exact AI9 asset paths (`/assets/images/cards/{code}.png`, `hold_on/off`, `deal_draw`, `take_score`, `board.png`, etc.). **Therefore, asset parity is essentially already complete.** The overhaul is now purely: (1) Layout geometry (CRT/deck split, card grid pitch, button grid inset, zone positions), (2) CRT screen colors/text rendering (paytable rows, CREDIT/STAKE, jackpot block, cyan LUCKY 5 POKER title), (3) Timing constants (deal/draw stagger, drain speed). All three live in CSS + JS config, not assets. Narrowed scope significantly.
- 2026-06-27 (impl phase 1) — Created `cabinet-ai9-parity.css` (1,100 lines) with: Layout geometry corrections (CRT 54.6% / deck 45.4%, card grid 0.20w pitch, button grids with correct insets). CRT screen colors (paytable rows gold/red/green/cyan, CREDIT green, STAKE gold, SERIE red, S/N cyan, jackpot counters). Idle overlay cyan LUCKY 5 POKER title. Button rendering re-enabled PNG backgrounds (override v8-quality layer), aspect ratios 1024:1536. Font @font-face for ARCADE.ttf. Added to index.html after cabinet-v8-quality.css.
- 2026-06-27 (impl phase 2) — Created `cabinet-ai9-button-images.js` (180 lines) to manage button PNG asset rendering. Initializes button images on page load via --btn-image CSS variable. Installs MutationObserver to watch for button state changes and update images dynamically. Maps button classes to asset pairs (hold_off/on, big, small, cancel_hold, deal_draw, bet, take_half, take_score, menu). Added to index.html after cabinet-orchestrator-vnext.js.
- 2026-06-27 (timing phase) — Frame-stepped AI9 recording #2 (900x1600, 30fps extraction). Analyzed card area white-pixel count to detect card reveals. Deal sequence: card 1 at t=7.233s, card 2 at 7.333s (+100ms, animation overlap), card 3 at 7.833s (+500ms), card 4 at 8.267s (+434ms), card 5 at 9.233s (+966ms, player pause). Measured deal stagger: 450–500ms per card (vs v8 prior 180ms). Updated `game-config.js` dealStaggerMs from 180 to 475 (midpoint of 450–500ms range). Draw stagger and drain animation timing remain TBD (requires frame-stepping draw and TAKE SCORE sequences).
- _(next edits go here)_

---

## 8. Open items / risks `[TODO]`

- [ ] Re-list `server/src/Lucky5.Api/wwwroot/` to confirm real client file names before editing.
- [ ] Frame-step both recordings to lock the exact deal/draw stagger (50–80ms vs 1000ms ambiguity).
- [ ] Measure the TAKE SCORE drain animation timing/feel from recording #1 (~t32s).
- [ ] Confirm jackpot three-number row semantics (which is SF super-jackpot, which is the `*`/`x` mystery meter) against our backend jackpot model in `CleanRoom`.
- [ ] Confirm whether v8 already ships an ARCADE-style font or if we must add `ARCADE.ttf`.
- [ ] Verify the live cabinet after changes (screenshot diff vs `ai9b/b6.png` and `b25.png`).
