# Lucky5 Modular Video Poker Engine — Architecture & Configuration Schema

Status: reviewed by api-designer (task t_27bb9a76)
Compatibility: preserves existing Lucky5 base game behavior

## 1. Intent

This document defines the modular architecture and configuration schema for upgrading
the existing Lucky5 engine into a feature-rich, configurable video poker platform
without breaking the base game. It specifies component boundaries, interfaces,
config shape, and backward-compatibility rules.

## 2. Design principles

- Backward compatible by default: existing Lucky5 behavior is the zero-config baseline.
- Composition over inheritance: variants are configured, not subclassed.
- Determinism preserved: all engine-critical decisions remain seed-driven and reproducible.
- Observability first: config and policy telemetry are first-class.
- Strict type boundaries: public contracts are records/interfaces; internals are private.

## 3. Modular components

### 3.1 Component map

| Component | Responsibility | Boundary rule |
|---|---|---|
| `ICabinetVariantEngine` | Variant-specific deck, hand evaluation, double-up bootstrap, progressive condition | Only reads config; no payout scaling |
| `IDoubleUpSession` | Generic gamble session contract | State-only record; no policy logic |
| `DeckManager` | Deck building, shuffling, pressure alterations, card mutation | Pure deck transforms; no hand/payout logic |
| `HandEvaluator` | Maps card arrays to `HandCategory` and tiebreak data | Reads only card values; no config/RTP |
| `PayoutCalculator` | Resolves payouts from `PaytableProfile` and bet | No deck/reachability logic |
| `EngineConfig` | Tunable engine parameters and feature toggles | Data record only; no runtime mutation |
| `MachinePolicy` | RTP controller, distribution mode, envelope, deck pressure | Reads config + state; emits telemetry |
| `GameOrchestrator` | Orchestrates round lifecycle and delegates to modules | No rule implementations |
| `JackpotManager` | Fixed-increment jackpot pools and Kent streak clearing | Only updates/reads jackpot state |
| `VariantFactory` | Resolves `GameId` → `ICabinetVariantEngine` | Registration only |

### 3.2 Module interaction contract

```
GameOrchestrator
  -> VariantFactory.GetEngine(GameId)
  -> DeckManager.BuildDeck(engine, config)
  -> HandEvaluator.Evaluate(hand)
  -> PayoutCalculator.Resolve(hand, bet, paytable)
  -> MachinePolicy.ResolvePolicy(state, seed, config)
  -> JackpotManager.Update(...)
  -> IDoubleUpSession (created by engine.StartDoubleUp)
```

## 4. Configuration schema

### 4.1 Root schema

```json
{
  "$schema": "lucky5-engine-config/v1",
  "gameId": "lucky5",
  "enabled": true,
  "runtime": {
    "persistence": "in-memory",
    "logLevel": "info",
    "deterministicSeeds": true
  },
  "engine": {
    "$ref": "#/definitions/engineConfig"
  },
  "variant": {
    "$ref": "#/definitions/variantProfile"
  },
  "paytable": {
    "$ref": "#/definitions/paytableProfile"
  },
  "jackpots": {
    "$ref": "#/definitions/jackpotProfile"
  },
  "modifiers": {
    "$ref": "#/definitions/modifierProfile"
  },
  "cards": {
    "$ref": "#/definitions/cardProfile"
  },
  "effects": {
    "$ref": "#/definitions/effectProfile"
  }
}
```

### 4.2 Engine config (`EngineConfig`)

Backward-compatible subset of the existing `EngineConfig` record. New fields are additive.

| Field | Type | Default | Notes |
|---|---|---|---|
| `targetRtp` | decimal | 0.80 | Composite RTP target |
| `targetDoubleUpRtp` | decimal | 0.35 | DU contribution target |
| `targetJackpotRtp` | decimal | 0.0325 | Fixed derived target |
| `minimumObservedBaseRtp` | decimal | 1.50 | Unscaled base EV denominator |
| `defaultPayoutScale` | decimal | 1.25 | Baseline scale |
| `minPayoutScale` / `maxPayoutScale` | decimal | 0.25 / 2.15 | Scale clamps |
| `warmupRounds` | int | 40 | Rounds before controller activates |
| `convergenceHorizon` | int | 400 | Ramp window |
| `correctionGain` / `maxCorrection` / `deadZone` | decimal | 0.85 / 0.28 / 0.02 | PID-style controller |
| `rtpSmoothingWindow` / `rtpMinSamplesForControl` | int | 300 / 25 | Smoothing parameters |
| `maxDriftClamp` | decimal | 0.20 | Drift ceiling |
| `jitterAmplitude` | decimal | 0.025 | Deterministic noise amplitude |
| `tierFactors` | {small, medium, big} | 1.00/1.04/1.08 | Payout tier multipliers |
| `warmupOpeningScales` | {small, medium, big} | 1.25/1.28/1.30 | Opening scales |
| `envelopeScaleClamp` / `rollingMeanAlpha` / `houseEdgeBufferCap` | decimal | 0.18/0.05/0.06 | Envelope controls |
| `jackpotRtpSoftCap` / `jackpotLeakDamp` | decimal | 0.060/0.40 | Jackpot leak damping |
| `doubleUpRtpHardCap` | decimal | 0.40 | DU leak clamp |
| `pityBoostCap` | decimal | 0.10 | Max pity boost |
| `doubleUpDeckPressure` | {minRounds, softDrift, maxKeyRemovals, recoveryDroughtRounds, minDeckSize, ...} | See mem.md | Deck pressure tuning |
| `streakCooldown` | {soft, hard, crisis, mediumDrought, cooldownLength} | See mem.md | Streak and cooldown tuning |
| `softCapWarning` / `softCapHard` / `closeThreshold` | decimal | 12M/18M/40M | Machine envelope |
| `jackpots` | object | See section 4.5 | Jackpot pool config |
| `specialRules` | `Lucky5SpecialRules?` | null | Backward-compatible special rules |
| `anteModel` | {firstDrawMultiplier, secondDrawMultiplier} | 1.0/2.0 | Draw ante model |
| `deckAlterationBounds` | {maxColdRemovals, maxHotAdditions, neverRemove5OfSpades, minDeckSize} | 1/2/true/51 | Deck alteration bounds |

### 4.3 Variant profile

| Field | Type | Default | Notes |
|---|---|---|---|
| `id` | string | Required | Canonical variant ID |
| `displayName` | string | Required | UI label |
| `lineage` | string | Optional | ROM/historical lineage reference |
| `doubleUpStyle` | enum | `dealerChallenge` | `nextCardBigSmall` or `dealerChallenge` |
| `bigThresholdInclusive` / `smallThresholdInclusive` | int? | null | Next-card thresholds when applicable |
| `operatorSettings` | object | null | Max bet, auto-collect, percentage targets |
| `jackpotFeatures` | array | [] | Variant-specific jackpot features |
| `specialRules` | `Lucky5SpecialRules?` | null | Variant overrides |
| `paytableRef` | string | null | Reference to paytable profile |
| `enabled` | bool | true | Hot-swap toggle |

### 4.4 Modifier profile (custom modifiers placeholder)

Modifiers are composable, stateless rule overrides applied per variant or per machine.

| Field | Type | Default | Notes |
|---|---|---|---|
| `id` | string | Required | Unique modifier ID |
| `name` | string | Required | Human label |
| `target` | enum | `doubleUp` | `basePayout`, `jackpot`, `deck`, `progressive` |
| `type` | enum | Required | `multiplier`, `add`, `cap`, `block`, `toggle` |
| `value` | decimal | Required | Modifier magnitude |
| `conditions` | object | null | Activation conditions (streak, net, round, etc.) |
| `priority` | int | 0 | Higher = later in chain |
| `enabled` | bool | true | Toggle |

### 4.5 Jackpot profile

| Field | Type | Default | Notes |
|---|---|---|---|
| `fourOfAKind` | object | Required | A/B side pools |
| `straightFlush` | object | Required | Single pool |
| `fullHouse` | object | Required | Rank-armed pool |
| `kent` | object | Required | Sequential-streak pool |
| `royalFlush` | object | null | Optional |
| `fiveOfAKind` | object | null | Optional variant feature |

Each pool object:

| Field | Type | Notes |
|---|---|---|
| `cap` | decimal | Maximum pool value |
| `start` | decimal | Initial pool value |
| `contributionPerRound` | decimal | Fixed increment |
| `activeSlotAlternate` | bool | For A/B pools |
| `requiresMaxBet` | bool | Eligibility |
| `clearStreakCount` | int | Kent-specific streak to clear |

### 4.6 Card profile (custom cards placeholder)

| Field | Type | Default | Notes |
|---|---|---|---|
| `standardDeckSize` | int | 52 | 52 or 53 (joker) |
| `customCards` | array | [] | Additional cards beyond standard 52 |
| `deckAlterations` | array | [] | Pressure-based card modifications |
| `neverRemove` | array | ["5♠"] | Protected cards |
| `duplicateRanks` | array | [] | Ranks allowed to duplicate under pressure |

Each custom card:

| Field | Type | Notes |
|---|---|---|
| `rank` | int | 2–14 |
| `suit` | char | C/D/H/S |
| `kind` | enum | `standard`, `joker`, `wild`, `trap` |
| `effectRef` | string | Optional effect to apply |

### 4.7 Effect profile (custom effects placeholder)

Effects are pluggable behaviors triggered by card events or game state.

| Field | Type | Default | Notes |
|---|---|---|---|
| `id` | string | Required | Unique effect ID |
| `name` | string | Required | Human label |
| `trigger` | enum | Required | `onDeal`, `onDraw`, `onDoubleUpSwitch`, `onProgressiveHit`, `onMachineClose` |
| `target` | enum | Required | `deck`, `hand`, `jackpot`, `machine`, `player` |
| `action` | enum | Required | `modifyDeck`, `grantCredits`, `lockCard`, `revealCard`, `resetState`, `applyMultiplier` |
| `value` | decimal | null | Magnitude |
| `conditions` | object | null | Activation conditions |
| `maxInstances` | int | null | Cooldown/limit |
| `enabled` | bool | true | Toggle |

## 5. Backward compatibility guarantees

1. **Zero-config defaults**: A config file with only `{ "gameId": "lucky5" }` must reproduce the current Lucky5 behavior exactly.
2. **No default value changes**: All existing `EngineConfig` defaults remain unchanged. `Lucky5DoubleUpOptions` defaults remain unchanged.
3. **Additive only**: New config fields never alter existing field semantics. Breaking changes require a new major version.
4. **Interface preservation**: `ICabinetVariantEngine` and `IDoubleUpSession` method signatures remain stable.
5. **Variant factory default**: The default `VariantFactory` resolution returns the existing `Lucky5CabinetVariant` behavior.
6. **Determinism preserved**: Existing seed derivation and shuffle paths are unchanged for the base variant.

## 6. Implementation roadmap (task t_788915ae)

1. Define module interfaces in `Lucky5.Domain/Game/CleanRoom/` (records + interfaces).
2. Extract deck management into `DeckManager` using existing `FiveCardDrawEngine` and `MachinePolicy` internals.
3. Extract hand evaluation into `HandEvaluator` (currently in `PokerHandEvaluator`).
4. Extract payout calculation into `PayoutCalculator` using existing `PaytableProfile`.
5. Introduce `EngineConfig` as the single config schema record (already exists; extend with additive fields only).
6. Add `ModifierProfile`, `CardProfile`, `EffectProfile` placeholder records.
7. Wire `GameOrchestrator` to delegate to modules.
8. Add unit tests per module.
9. Verify base Lucky5 game behavior is unchanged via existing test suite.

## 7. Acceptance criteria checklist

- [ ] Architecture doc reviewed
- [ ] Schema defined in this document with placeholders
- [ ] Component boundaries clear
- [ ] Design can be implemented without breaking base Lucky5 game
- [ ] Config is additive and backward-compatible
- [ ] Existing `EngineConfig` defaults unchanged
- [ ] `Lucky5DoubleUpOptions` defaults unchanged

## 8. Risks and notes

- Adding `DeckManager` as a new abstraction must preserve existing `MachinePolicy.BuildDoubleUpDeck` behavior exactly.
- The `HandEvaluator` extraction must preserve `PokerHandEvaluator`'s tiebreak logic.
- New config fields should be validated at deserialization boundaries, not inside hot paths.
- Variant hot-swap (`enabled: false`) must gracefully fall back to the default Lucky5 variant without runtime errors.
