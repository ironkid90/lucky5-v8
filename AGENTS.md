You are an AI coding agent working in Lucky5 v8. Optimize for correctness, safety, reversibility, and small verified changes.

<context>
Ground factual claims in this repository's source files, linked docs, and command output from the current workspace.
</context>

## Operating Loop
- Inspect before editing. Read only the files needed for the task.
- Preserve existing conventions and user changes. Never revert unrelated work.
- Plan briefly for non-trivial work, then implement the smallest useful change.
- Verify with the tightest relevant check. Never claim completion without evidence.
- Report in concise markdown: what changed, what ran, assumptions, and remaining risk.

## Lucky5 Invariants
- `docs/` is the source of truth for product and engineering behavior.
- This repo is a web-native Lucky5 cabinet plus a .NET 10 API server.
- `server/src/Lucky5.Api/wwwroot/` is the primary playable client.
- `server/src/Lucky5.Domain/Game/CleanRoom/` owns deterministic authoritative game logic.
- The backend owns balance, machine state, session state, jackpots, and realtime behavior.
- Preserve the retro cabinet feel. Do not turn the product into a generic casino UI.
- Persistence is in-memory unless `Persistence:FileStore:RootPath` configures file snapshots.

## Commands
- Launch: `./dev.ps1`
- API only: `./dev.ps1 -Headless`
- Tests: `dotnet run --project server/tests/Lucky5.Tests/Lucky5.Tests.csproj`
- Build API: `dotnet build server/Lucky5.sln`

## Grounding Links
- Start with [README.md](README.md) for setup, commands, and repo structure.
- Use [docs/README.md](docs/README.md) and [docs/LUCKY5_AUTHORITATIVE_GAMEPLAY_REFERENCE.md](docs/LUCKY5_AUTHORITATIVE_GAMEPLAY_REFERENCE.md) for gameplay and cabinet behavior.
- Use [docs/GAME_FEEL_REFERENCE.md](docs/GAME_FEEL_REFERENCE.md) and [docs/WEB_NATIVE_STRATEGY.md](docs/WEB_NATIVE_STRATEGY.md) for visual direction and architecture.


## vexp <!-- vexp v2.0.27 -->

**MANDATORY: use `run_pipeline` - do NOT grep or glob the codebase.**
vexp returns pre-indexed, graph-ranked context in a single call.

### Workflow
1. `run_pipeline` with your task description - ALWAYS FIRST (replaces all other tools)
2. Make targeted changes based on the context returned
3. `run_pipeline` again only if you need more context

### Available MCP tools
- `run_pipeline` - **PRIMARY TOOL**. Runs capsule + impact + memory in 1 call.
  Auto-detects intent. Includes file content. Example: `run_pipeline({ "task": "fix auth bug" })`
- `get_skeleton` - compact file structure
- `index_status` - indexing status
- `expand_vexp_ref` - expand V-REF placeholders in v2 output

### Agentic search
- Do NOT use built-in file search, grep, or codebase indexing - always call `run_pipeline` first
- If you spawn sub-agents or background tasks, pass them the context from `run_pipeline`
  rather than letting them search the codebase independently

### Smart Features
Intent auto-detection, hybrid ranking, session memory, auto-expanding budget.

### Multi-Repo
`run_pipeline` auto-queries all indexed repos. Use `repos: ["alias"]` to scope. Run `index_status` to see aliases.
<!-- /vexp -->