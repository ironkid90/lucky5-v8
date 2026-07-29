# Cross-IDE AI Resource Consolidation

**Date:** 2026-07-29

## Summary

Consolidated compatible MCP configuration, skills, durable user memory, and one portable agent mode into ToCodex and the canonical workstation MCP hub. Existing settings and client-managed ContextStream registrations were preserved. A dedicated Python 3.14 environment was created for `windows-mcp`, and the server was activated only after a bounded MCP `initialize` handshake succeeded.

## Changes

### Canonical MCP hub and runtime

- Added `windows-mcp` as the only verified hub server in `C:\Users\Gabi.WIN-CD45QMUUPFF\.mcp-hub\mcp.json`.
- Kept the other eight hub definitions in candidate state; they were not activated.
- Installed `windows-mcp==0.8.2` in the isolated runtime at `C:\Users\Gabi.WIN-CD45QMUUPFF\.mcp-hub\servers\windows-mcp\.venv-py314\`.
- Scoped `PYTHONPATH` to the isolated environment because the host Python 3.14 base library otherwise shadowed virtual-environment packages.
- Disabled anonymized telemetry for the server process.
- Fixed Codex synchronization idempotency in `C:\Users\Gabi.WIN-CD45QMUUPFF\.mcp-hub\sync.py` by removing the prior managed marker before appending the regenerated managed TOML block.

### MCP client registrations

Synchronized the verified server while preserving unrelated settings and client-managed authentication in:

- `C:\Users\Gabi.WIN-CD45QMUUPFF\AppData\Roaming\Code\User\mcp.json`
- `C:\Users\Gabi.WIN-CD45QMUUPFF\AppData\Roaming\Code - Insiders\User\mcp.json`
- `C:\Users\Gabi.WIN-CD45QMUUPFF\.codex\config.toml`
- `C:\Users\Gabi.WIN-CD45QMUUPFF\.codex\mcp_servers.toml`
- `C:\Users\Gabi.WIN-CD45QMUUPFF\.claude.json`
- `C:\Users\Gabi.WIN-CD45QMUUPFF\.gemini\settings.json`
- `C:\Users\Gabi.WIN-CD45QMUUPFF\AppData\Roaming\ToCodex Desktop\instances\6ef0e0c696de5027\extension-storage\global-storage\settings\mcp_settings.json`
- `.tocodex/mcp.json`
- `.mcp.json`
- `.claude/settings.local.json`

ToCodex global and workspace MCP files contain both preserved `contextstream` and verified `windows-mcp` registrations.

### Skills

- Promoted all 69 complete directories from `plugins/skills/` to `C:\Users\Gabi.WIN-CD45QMUUPFF\.tocodex\skills\`.
- No existing global skill directory was overwritten.
- Verified every promoted package by exact directory-tree hash and required `SKILL.md` frontmatter.

### Memory

- Created a sanitized 31-line global memory at `C:\Users\Gabi.WIN-CD45QMUUPFF\.tocodex\memory\GLOBAL_MEMORY.md`.
- Imported only durable cross-project collaboration, engineering, Windows, and safety preferences.
- Excluded raw histories, credentials, tokens, stale ports and versions, transient machine state, and Lucky5-specific facts that belong in project memory.

### Custom mode

- Added one portable global mode, `agent-governance-reviewer`, to `C:\Users\Gabi.WIN-CD45QMUUPFF\AppData\Roaming\ToCodex Desktop\instances\6ef0e0c696de5027\extension-storage\global-storage\settings\custom_modes.yaml`.
- Removed source-specific model and VS Code/GitHub/Azure tool declarations during conversion.
- Did not import `API Architect` because it duplicates the existing `api-designer` mode and imposes an unnecessary generation gate.
- Did not import the GitHub Agentic Workflows or Azure Principal Architect agents because they depend on unavailable or unverified tool surfaces.
- Did not import `plugins/rules/coder.instructions.md` because it targets a different repository and framework and would conflict with Lucky5 guidance.

## Backups

Primary pre-change backup:

- `C:\Users\Gabi.WIN-CD45QMUUPFF\AppData\Roaming\ToCodex Desktop\backups\cross-ide-import-20260729-212817\`
- Manifest: `backup-manifest.json`

Additional synchronizer backup:

- `C:\Users\Gabi.WIN-CD45QMUUPFF\.mcp-hub\sync.py.before-codex-idempotency-fix-20260729-214152.bak`

The hub synchronizer also created timestamped backups for changed client targets during real synchronization.

## Verification

- `windows-mcp` module import: passed.
- `windows-mcp` CLI help: passed.
- Bounded MCP JSON-RPC `initialize`: passed; no desktop automation action was invoked.
- Hub verification: `1 passed, 0 failed`.
- ToCodex global and workspace MCP JSON parsing: passed.
- Preserved ContextStream registration checks: passed.
- Global skill validation: `69/69` exact tree hashes and frontmatter checks passed.
- Global memory: UTF-8 without BOM; zero credential-like assignments.
- Governance mode: required fields present and slug occurs exactly once.
- `sync.py` Python compile check: passed.
- Final real sync followed by dry-run: every client target reported `unchanged`, proving two-pass idempotency.

## Operational Notes

- Restart ToCodex Desktop and other affected desktop clients before using the new MCP registration.
- Hermes was not modified automatically because it uses its own registry. The audited manual commands remain:
  - `hermes mcp add windows-mcp --command C:\Users\Gabi.WIN-CD45QMUUPFF\.mcp-hub\servers\windows-mcp\.venv-py314\Scripts\python.exe --args -m windows_mcp serve`
  - `hermes mcp test windows-mcp`
