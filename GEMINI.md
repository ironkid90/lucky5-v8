# Workspace: Lucky5 v8 (Agent Onboarding Guide)

Welcome to the Lucky5 project! This document serves as your immediate "catch-up" context to start coding rapidly.

## 1. Project Overview & Architecture
This repository contains a **web-native arcade cabinet** (HTML/CSS/Vanilla JS) powered by a **.NET 10 API server**.
There is NO Godot, no Flutter, and no complex JS framework (like React or Vue) in use for the client.
- **Frontend / Client:** Located in `server/src/Lucky5.Api/wwwroot/`.
    - **`game.js`**: Core client engine, state management, and communication with the backend.
    - **`cabinet-stage-vnext.js`**: UI choreography, visual states, and layout manipulation.
    - **`css/cabinet-v8-quality.css`**: Core aesthetics, typography, animations, and gradients.
- **Backend / Game Logic:** The absolute source of truth. Located in `server/src/Lucky5.Domain/Game/CleanRoom/`. The web client merely *presents* the game states issued by the server.

## 2. The Aesthetic Target (`ai9poker.com`)
The visual parity target for this project is the classic "Lebanese arcade cabinet" style, historically found on machines or clones like `ai9poker.com`.
- **Colors:** Warm brown wooden control deck, CRT-style black backgrounds, and a glowing pixelated font.
- **Buttons:** Chunky 3D-beveled buttons. **CRITICAL:** The `DEAL DRAW` button is **RED**, and the `BET` button is **GREEN**. This is the opposite of many Western poker machines.
- **Cards:** We recently **removed all PNG image dependencies** for cards. Cards are now **100% DOM-based**, featuring an ivory background, gold inner border, and pixelated font symbols. *Do not attempt to write code that loads `/assets/images/cards/`.*

## 3. Core Commands
- **Launch Full Stack:** `./dev.ps1`
- **API Only:** `./dev.ps1 -Headless`
- **Build Server:** `dotnet build server/Lucky5.sln`

## 4. Key Documentation
Read the following files in the `docs/` folder for deeper context before making structural changes:
- `docs/DEVELOPMENT_HISTORY_AND_CURRENT_STATE.md`: Comprehensive development history, strategic pivots, notable achievements, lessons from failure modes, and detailed technical software/mechanical module state.
- `docs/LUCKY5_AUTHORITATIVE_GAMEPLAY_REFERENCE.md`: Mechanics, double-up logic, and jackpot structures.
- `docs/GAME_FEEL_REFERENCE.md`: Button colors, layout mapping, and timing metrics.

## 5. Development Constraints
- **Preserve the retro feel:** Do not modernize the UI into a generic flat design or sleek casino app.
- **No external frameworks:** Stick to Vanilla JS and CSS variables.
- **Use `run_pipeline`:** When exploring the codebase, use the `run_pipeline` tool provided by `vexp` rather than searching blindly with `grep` or `find`.
