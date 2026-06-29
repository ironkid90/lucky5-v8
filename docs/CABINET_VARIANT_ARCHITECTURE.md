# Cabinet Variant Architecture (Video Poker Multi-Game Engine)

## Overview
The "Lucky5" codebase has been modernized from a single-game monolith into a highly extensible Arcade Cabinet Video Poker engine. The core engine strictly implements mathematical probability and standard 52-card mechanics, while dynamic rules (Jackpots, unique draw behaviors, gamble variants) are injected via the **Cabinet Variant Plugin Architecture**.

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
