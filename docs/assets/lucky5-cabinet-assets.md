# Lucky5 Cabinet Card and Button Asset Spec

Status: carried forward as the cabinet asset spec for the web-native runtime.

Source of truth: `docs/LUCKY5_AUTHORITATIVE_GAMEPLAY_REFERENCE.md`. This spec does not change payout tables, RNG, hand evaluation, double-up math, jackpots, settlement, or backend authority.

Historical note: this file was written during the earlier split-frontend phase. Any `src/web/...` paths in this document are archival references, not active v8 source locations.

## Web asset pipeline

- Generator: `src/web/scripts/generate-lucky5-assets.mjs`
- Smoke check: `src/web/scripts/smoke-lucky5-assets.mjs`
- Static output root: `src/web/public/assets/lucky5/cards/`
- Runtime URL root: `/assets/lucky5/cards/`
- Cabinet integration: `src/web/components/lucky5-cabinet.tsx`
- Button normal/pressed states: `src/web/app/globals.css`

Run from `src/web`:

```powershell
npm run assets:smoke
```

The smoke command regenerates the assets and verifies the cabinet references, CSS button-state hooks, and this handoff spec.

## Card deck deliverables

The generated custom 52-card deck uses vector SVG for responsive/retina display while retaining a hard-edged arcade look. The SVGs intentionally use the existing `Lucky5Arcade` font stack so `ARCADE.ttf` remains the cabinet type identity.

Naming convention:

- Faces: `<rank><suit>.svg`
- Ranks: `A K Q J 10 9 8 7 6 5 4 3 2`
- Suits: `S H D C`
- Examples: `AS.svg`, `KS.svg`, `10D.svg`, `5S.svg`
- Back: `bside.svg`
- Held/back variant: `hold-bside.svg`

Visual requirements preserved:

- Warm ivory card face with dark cabinet outline and gold inner rule.
- Red hearts/diamonds and black spades/clubs.
- Large arcade-pip center treatment for quick portrait readability.
- `5S.svg` includes a small `NEVER LOSE` cabinet strip to support the Lucky 5 / 5 spade never-lose visual language.
- `bside.svg` uses a dark blue B-side cabinet back with `LUCKY 5♠` mark.
- Idle title mode keeps the black cabinet field clear, then reveals only the armed Full House rank card in slot 3 after the idle delay. Card backs still define deal staging and unused double-up slots.

## Button normal/pressed states

CSS variables on `.apk-btn`, `.apk-hold-btn`, and `.apk-menu-btn-label` define the button asset colors for normal and pressed states. Pressed states are rendered with `:active` and a depressed shadow so the controls read as physical Lebanese retro cabinet buttons rather than modern app buttons.

Required cabinet controls:

| Control | Selector | Normal/pressed color intent |
| --- | --- | --- |
| BET | `.apk-btn-bet` | green, darker green pressed |
| BIG | `.apk-btn-big` | orange/yellow, brown-orange pressed |
| SMALL | `.apk-btn-small` | orange, darker orange pressed |
| CANCEL HOLD | `.apk-btn-cancel` | off-white/cream, tan pressed |
| DEAL DRAW | `.apk-btn-deal` | red, dark red pressed |
| HOLD | `.apk-hold-btn` | yellow/orange, amber pressed |
| TAKE HALF | `.apk-btn-take-half` | red, dark red pressed |
| TAKE SCORE | `.apk-btn-take-score` | yellow/orange, amber pressed |
| MENU | `.apk-menu-btn-label` | black/grey round button, near-black pressed |

Do not replace these with Material, Tailwind utility-only, glassmorphism, or generic casino styling.

## Runtime authority boundary

Render card DTOs from the backend only. The web client must not compute hand truth, payout truth, jackpot truth, wallet truth, or double-up state truth.

## Regression checklist

- `cardImgSrc` returns `/assets/lucky5/cards/<code>.svg`, not an empty placeholder.
- Empty card slots render `bside.svg`.
- Rotating Full House idle card stays in slot 3 after the delayed idle reveal, matching the authoritative reference.
- `KENT /3`, `S/N`, `4 OF A KIND WINS BONUS`, and the double-up `5 ♠ NEVER LOSE` text remain presentation elements only.
- Responsive/retina rendering stays crisp because cards are SVG and `.card-img` avoids global pixelated raster degradation.
