# Cabinet Variant Architecture (Video Poker Multi-Game Engine)

## Overview
The "Lucky5" codebase has been modernized from a single-game monolith into a highly extensible Arcade Cabinet Video Poker engine. The core engine strictly implements mathematical probability and standard 52-card mechanics, while dynamic rules (Jackpots, unique draw behaviors, gamble variants) are injected via the **Cabinet Variant Plugin Architecture**.

### Historical lineage (source of truth)
The live Lucky5 cabinet is a clean-room descendant of the Lebanese arcade video-poker lineage, not a direct port of the MAME `goldnpkr` ROM. The ROM-derived profiles in `server/src/Lucky5.Domain/Game/CleanRoom/LineageProfiles.cs` track the following branches:

| Profile | Era / Manufacturer | Double-up model | Distinctive features |
|---------|-------------------|-----------------|----------------------|
| `BonanzaGoldenPoker` | 1981, Bonanza Enterprises | Next-card BIG/SMALL vs fixed 7 | Root ICP-1 family; percentage mode 0-3 ≈ 85/30/40/50; NVRAM settings |
| `BonusPoker` | 1984, Galanthis | Next-card BIG/SMALL vs fixed 7 | Premium-hand jackpot emphasis (4OAK/Straight Flush); paytable line `4 8 10 20 30 50 60 100`; ACE COUNTS HI OR LO |
| `WildWitch` | 1992-2001, Video Klein | Next-card BIG/SMALL vs fixed 7 | Multi-game switch (Wild Witch / Witch Game), 6/12-button controls, German operator HALT settings |
| `Super98` | 1998, unknown (MAME `witchcrd`) | Next-card BIG/SMALL vs fixed 7 (assumed) | 3-hand play, complex protection, MAME parent `bsuerte` |
| `RobertsUltimate` | Lebanese live cabinet | Dealer-card BIG/SMALL | Joker + 5-of-a-Kind jackpot, 5♠/Ace auto-win, SafeFail; original ROMs not yet dumped |
| `Lucky5CleanRoom` | v8 C# engine | Dealer-card BIG/SMALL | Authoritative implementation: 5-card progressive board, Kent jackpot, rank-armed Full House jackpot |

**Important:** `AI9` and `playpoker` Flutter implementations are useful reference captures but are **not authoritative** for original arcade behaviour. Prefer ROM evidence and the clean-room C# engine.

**Naming correction:** The lineage previously referred to as "WILO" is **WILD** — Video Klein's *Wild Witch* family. Strip any remaining "WILO" references from documentation and notes.

## 1. Database & State Generics
Previously, the Machine database entity contained hardcoded properties for specific games (e.g., MachineKent). This has been abolished to prevent EF Core migrations for every new game release.
- **Machine.GameId**: Defines the canonical game running on the cabinet (e.g., "1" for Lucky5, "2" for Jacks or Better).
- **Machine.VariantState**: A dynamic JSON column (e.g., {"MachineKent": "1"}) that holds persistent, variant-specific ledger tracking states.

## 2. Core Variant Interfaces
All new games MUST implement these interfaces located in Lucky5.Domain/Game/CleanRoom/:

*   **ICabinetVariantEngine**: The central strategy plugin.
    *   EvaluateHand(): Translates standard 52-card poker arrays into a HandCategory.
    *   MeetsVariantSpecificProgressiveCondition(): Evaluates board conditions dependent on VariantState (e.g., the Lucky 5 explicit 5-card positional sequential "Kent" jackpot).
    *   StartDoubleUp(): Bootstraps the gamble logic specific to the variant.

*   **IDoubleUpSession**: The generic contract for "Gamble" states. Custom variant records (like Lucky5DoubleUpSession) must implement this to guarantee the GameService can orchestrate risk modes generically.

## 3. The Service Layer (GameService.cs)
GameService.cs is strictly a workflow orchestrator. It does not contain rules.
When a transaction occurs, GameService processes it dynamically:
1. Reads Machine.GameId
2. Invokes CabinetVariantFactory.GetEngine(GameId)
3. Delegates outcome evaluations to the returned ICabinetVariantEngine.

## Standard Operating Procedure: Adding a New Arcade Game
For an AI Agent or Developer adding a new game (e.g., "Deuces Wild"):
1. Create DeucesWildCabinetVariant.cs implementing ICabinetVariantEngine.
2. Add custom double-up limits by creating a DeucesWildDoubleUpSession record implementing IDoubleUpSession.
3. Register the new GameId in the CabinetVariantFactory.
4. The frontend UI configs will pull down the new behavior dynamically via the unified /api/game/config/{gameId} endpoint.
