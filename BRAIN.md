# The Global Brain Hub

This document serves as the centralized knowledge base for the Lucky5 v8 project, consolidating information from various agent configurations, documentation, and memory files. Its purpose is to provide a single source of truth and eliminate redundancy.

## 1. Core Project Identity & Principles

*   **Project:** Lucky5 v8, a web-native recreation of a 1990s Lebanese Bonanza-style arcade video poker cabinet.
*   **Philosophy:** The primary goal is to preserve the **absolute retro physical cabinet feel**. Modernization of the UI is strictly forbidden.
*   **Tech Stack:**
    *   **Backend:** .NET 10 API with SignalR for realtime communication. Authoritative game logic resides in `server/src/Lucky5.Domain/Game/CleanRoom/`.
    *   **Frontend:** Pure Vanilla HTML/CSS/JS served from `server/src/Lucky5.Api/wwwroot/`. **No Godot, React, or Vue.**
*   **Key Command:** Launch the full stack with `./dev.ps1`.

## 2. Universal Agent Directives

This project uses multiple agent frameworks (`vexp`, `contextstream`). The following rules are mandatory for any agent interacting with this codebase.

### 2.1. Codebase Interaction (`vexp`)

A tool named `vexp` is the **mandatory** interface for all codebase exploration.

*   **PRIMARY RULE:** You **MUST** use the `run_pipeline` tool for all code discovery.
*   **FORBIDDEN TOOLS:** Do **NOT** use `grep`, `glob`, `find`, or any other manual file search tools.
*   **Workflow:**
    1.  Call `run_pipeline({ "task": "your task description" })` first.
    2.  Use the context returned to make targeted changes.
    3.  Only run `run_pipeline` again if more context is needed.

*(Source: Consolidated from `.antigravity/rules.md`, `.kiro/steering/vexp.md`, `.windsurf/rules.md`, `.zed/rules.md`, `.cursor/rules`)*

### 2.2. Session & Knowledge Management (`contextstream`)

The `contextstream` tool is used for session management, planning, and knowledge persistence.

*   **SESSION START:** At the beginning of **every session**, you **MUST** call `init(folder_path="...")` and then `context(user_message="...")`.
*   **SUBSEQUENT TURNS:** Call `context(user_message="...")` at the start of each subsequent turn.
*   **SEARCH FIRST:** Before any file system search, you **MUST** use `search(mode="auto", query="...")`.
*   **PERSISTENCE:** Use `contextstream` tools (`session(action="capture_plan")`, `memory(action="create_task")`, `session(action="capture_lesson")`) for all planning, tasks, and memory. Do **NOT** use local markdown files or other agent-specific memory.

*(Source: Consolidated from `.contextstream/rules.md`, `.windsurf/rules/contextstream.md`)*

## 3. Authoritative Documentation

The `docs/` directory contains the official project documentation. These are the most critical files to understand the project's history, gameplay, and architecture.

*   [**Development History & State**](docs/DEVELOPMENT_HISTORY_AND_CURRENT_STATE.md): The complete onboarding manual. Captures history, pivots, failures, and technical state.
*   [**Authoritative Gameplay Reference**](docs/LUCKY5_AUTHORITATIVE_GAMEPLAY_REFERENCE.md): The presentation authority for gameplay, including the 5-card double-up bonus board and Kent counter.
*   [**Machine Behavior Reference**](docs/MACHINE_BEHAVIOR_REFERENCE.md): Defines wallet vs. machine credits, cash-in/out logic, and machine close behavior.
*   [**Game Feel Reference**](docs/GAME_FEEL_REFERENCE.md): Visual direction, pacing, button colors, and cabinet proportions.
*   [**AI9 Parity Worklog**](docs/AI9_PARITY_GROUND_TRUTH_AND_WORKLOG.md): Ground truth measurements and engineering worklog for the AI9 visual parity overhaul.

## 4. Skills Inventory

The following skills have been identified across various configurations:

*   **`contextstream-workflow`**: Manages persistent AI memory across sessions using ContextStream. (Source: `.github/skills/contextstream-workflow`)
*   **`elasticsearch-onboarding`**: (Source: `.kiro/skills/elasticsearch-onboarding`, `.windsurf/skills/elasticsearch-onboarding`) - Content not read.

## 5. Configuration Hub (`mcp.json` / `config.json`)

Multiple configuration files exist to define "MCP Servers" (Multi-Agent Control Plane). They all point to the same thing: launching the `vexp` server.

*   **Files:** `.cursor/mcp.json`, `.kiro/settings/mcp.json`, `.comp/config.json`, `.contextstream/config.json`.
*   **Purpose:** Configure the launch command for the `vexp` tool's backend process.
*   **Consolidated Config:**
    ```json
    {
      "mcpServers": {
        "vexp": {
          "command": "node",
          "args": [
            "c:\Users\Gabi.WIN-CD45QMUUPFF\.vscode-insiders\extensions\vexp.vexp-vscode-2.1.0-win32-x64\dist\mcp-server.cjs"
          ],
          "env": {
            "VEXP_WORKSPACE": "c:\Users\Gabi.WIN-CD45QMUUPFF\Documents\GitHub\lucky5-v8\lucky5-v8"
          }
        }
      }
    }
    ```

## 6. Redundancy Report & Cleanup Plan

The following files and directories are redundant and can be consolidated into this `BRAIN.md` document and then removed.

### Rule & Context Files (to be merged and deleted)

*   `.antigravity/rules.md` (vexp rules)
*   `.contextstream/rules.md` (contextstream rules)
*   `.cursor/rules` (vexp rules)
*   `.kiro/steering/vexp.md` (vexp rules)
*   `.windsurf/rules.md` (vexp rules)
*   `.windsurf/rules/contextstream.md` (contextstream rules)
*   `.zed/rules.md` (vexp rules)
*   `AGENTS.md` (partial vexp/project rules, superseded by `GEMINI.md` and `docs/`)
*   `mem.md` (project memory, better stored in `docs/` or a dedicated memory system)

### Configuration Files (to be consolidated and deleted)

*   `.cursor/mcp.json`
*   `.kiro/settings/mcp.json`
*   `.comp/config.json`
*   `.contextstream/config.json`
*   (Proposal: Keep one canonical `.vexp/mcp.json` or similar)

### Skill Directories (to be consolidated)

*   `.github/skills/`
*   `.kiro/skills/`
*   `.windsurf/skills/`
*   (Proposal: Move all to a single `.brain/skills/` directory)

---

## Next Steps

1.  **Approve Consolidation:** Please review this `BRAIN.md` file.
2.  **Execute Consolidation:** Upon approval, I will:
    a.  Move all skills to a central `/.brain/skills` directory.
    b.  Create a single canonical `mcp.json` in a `.brain/` directory.
    c.  Delete all the redundant rule and configuration files listed above.
3.  **Update Agent Integrations:** You will need to manually update your various editor integrations (Cursor, Windsurf, etc.) to point to the new centralized configuration in the `.brain/` directory.
