# Lucky5 v8 Multi-Agent Coordination System

Windows-compatible multi-agent development system for Lucky5 v8 using Hermes Agent with git worktrees and background PowerShell jobs.

## Overview

This system enables parallel development with isolated agents:
- **Backend Agent** - .NET 10 API, CleanRoom game logic, SignalR hubs
- **Frontend Agent** - Vanilla JS/HTML/CSS web cabinet (retro AI9 parity)
- **Test Agent** - Unit/integration tests for game logic and API
- **Coordinator Agent** - Monitors and coordinates all agents

Each agent runs in its own **git worktree** with a dedicated branch, ensuring zero conflicts.

## Quick Start

```powershell
# Start all agents
.\agent-manager.ps1 -Action Start -Agent all

# Check status
.\agent-manager.ps1 -Action Status

# Send message to specific agent
.\agent-manager.ps1 -Action Send -Agent backend -Message "Add new jackpot endpoint"

# View logs
.\agent-manager.ps1 -Action Logs -Agent frontend

# Stop all agents
.\agent-manager.ps1 -Action Stop -Agent all
```

## Prerequisites

1. **Hermes Agent** installed and in PATH
   ```bash
   pip install hermes-agent
   # or
   curl -fsSL https://hermes-agent.nousresearch.com/install.sh | bash
   ```

2. **Git** with worktree support (Git 2.5+)

3. **.NET 10 SDK** for backend builds

4. **Windows Terminal** (for multi-tab support) or PowerShell 7+

## Agent Configurations

| Agent | Branch | Worktree | Focus |
|-------|--------|----------|-------|
| `backend` | `agent/backend` | `../lucky5-v8-worktrees/backend` | .NET API, CleanRoom logic, SignalR |
| `frontend` | `agent/frontend` | `../lucky5-v8-worktrees/frontend` | Vanilla JS cabinet, CSS, DOM cards |
| `test` | `agent/test` | `../lucky5-v8-worktrees/test` | Unit/integration tests |
| `coord` | `main` | (project root) | Coordination, context relay |

## Commands

### Start Agents
```powershell
# Start all agents
.\agent-manager.ps1 -Action Start

# Start specific agent(s)
.\agent-manager.ps1 -Action Start -Agent backend,frontend
```

### Check Status
```powershell
# All agents
.\agent-manager.ps1 -Action Status

# Specific agent
.\agent-manager.ps1 -Action Status -Agent test
```

### Send Messages
```powershell
# Send to single agent
.\agent-manager.ps1 -Action Send -Agent backend -Message "Implement Royal Flush jackpot trigger"

# Broadcast to multiple
.\agent-manager.ps1 -Action Send -Agent backend,frontend -Message "API contract changed: new field 'jackpotTier' in GameState"
```

### View Logs
```powershell
# Last 100 lines from all agents
.\agent-manager.ps1 -Action Logs

# Specific agent, more lines
.\agent-manager.ps1 -Action Logs -Agent backend
```

### Stop Agents
```powershell
# Stop all
.\agent-manager.ps1 -Action Stop

# Stop specific
.\agent-manager.ps1 -Action Stop -Agent frontend
```

### Clean Up (Removes Worktrees & Jobs)
```powershell
.\agent-manager.ps1 -Action Clean
```
⚠️ **Warning**: This deletes all worktrees and branches. Commit/push first!

## Architecture

```
lucky5-v8/                    # Main repo (coord agent)
├── agent-manager.ps1         # This manager script
├── .agent-jobs/              # Job state, logs, command queues
│   ├── backend.job           # Serialized PowerShell job
│   ├── backend.log           # Agent output log
│   ├── backend.pid           # Process ID
│   └── backend.commands      # Queued messages (JSON lines)
└── lucky5-v8-worktrees/      # Git worktrees (created on Start)
    ├── backend/              # agent/backend branch
    │   └── server/           # .NET solution
    ├── frontend/             # agent/frontend branch
    │   └── server/src/Lucky5.Api/wwwroot/
    └── test/                 # agent/test branch
        └── server/tests/Lucky5.Tests/
```

## Workflow Examples

### Feature: New Bonus Poker Variant
```powershell
# 1. Coordinator assigns work
.\agent-manager.ps1 -Action Send -Agent coord -Message "Starting Bonus Poker variant. Backend: add variant to CleanRoom. Frontend: add cabinet skin. Test: add hand eval tests."

# 2. Backend implements game logic
.\agent-manager.ps1 -Action Send -Agent backend -Message "Add BonusPoker variant to CleanRoom/LineageProfiles.cs. Handle 4K-A, 4K-B jackpots."

# 3. Frontend adds cabinet variant
.\agent-manager.ps1 -Action Send -Agent frontend -Message "Add Bonus Poker cabinet skin. Use cabinet-v8-realism.css for authentic look. Map DEAL/DRAW to red buttons."

# 4. Test writes validation
.\agent-manager.ps1 -Action Send -Agent test -Message "Add BonusPoker hand evaluation tests. Verify 4K-A (Aces) vs 4K-B (2-4) payouts."

# 5. Monitor progress
.\agent-manager.ps1 -Action Status
.\agent-manager.ps1 -Action Logs -Agent backend
```

### Bug Fix: Double-Up Ace High/Low
```powershell
.\agent-manager.ps1 -Action Send -Agent backend -Message "Fix Double-Up: Ace should be high by default, low only when player chooses 'Low'. Update CleanRoom/DoubleUp.cs"
.\agent-manager.ps1 -Action Send -Agent test -Message "Add test for Double-Up Ace high/low behavior. Verify 5 never loses when buying."
.\agent-manager.ps1 -Action Send -Agent frontend -Message "Update double-up UI to show Ace high/low choice clearly. Use cabinet-ai9-parity.css for button styling."
```

## Integration with Hermes Features

### Slash Commands (inside agent sessions)
Agents can use Hermes slash commands:
- `/goal "Implement Royal Flush jackpot"` - Standing goal across turns
- `/skills` - Load skills like `test-driven-development`
- `/model anthropic/claude-sonnet-4` - Switch model per agent
- `/checkpoint` - Save filesystem state

### Skills
Agents can load skills:
```powershell
# In agent message
.\agent-manager.ps1 -Action Send -Agent test -Message "/skill test-driven-development"
```

### Background Delegation
For subtasks, agents can use `delegate_task` tool (not this manager).

## Troubleshooting

### Agent won't start
```powershell
# Check prerequisites
hermes doctor

# Check logs
.\agent-manager.ps1 -Action Logs -Agent backend

# Clean and retry
.\agent-manager.ps1 -Action Clean
.\agent-manager.ps1 -Action Start -Agent backend
```

### Worktree conflicts
```powershell
# Check git status in worktree
cd ..\lucky5-v8-worktrees\backend
git status

# Reset if needed
git reset --hard HEAD
git clean -fd
```

### Port conflicts (API server)
Each agent's API runs on different ports if started manually. Default: 5051.
Configure in `appsettings.Development.json` per worktree.

### Hermes not found
```powershell
# Verify installation
hermes --version

# Add to PATH if needed
$env:PATH += ";$env:LOCALAPPDATA\hermes"
```

## Advanced: Custom Agent

Add to `$Agents` hashtable in `agent-manager.ps1`:

```powershell
myagent = @{
    Name = "myagent"
    DisplayName = "Custom Agent"
    Branch = "agent/myagent"
    WorktreePath = Join-Path $WORKTREE_BASE "myagent"
    WorkingDir = Join-Path $WORKTREE_BASE "myagent\path\to\work"
    Prompt = @"
Your custom prompt here...
"@
}
```

Then use: `.\agent-manager.ps1 -Action Start -Agent myagent`

## Files

- `agent-manager.ps1` - Main manager (PowerShell jobs, worktrees)
- `multi-agent-coordination.ps1` - Alternative (Windows Terminal tabs)
- `MULTI_AGENT_README.md` - This file

## License

Part of Lucky5 v8 project.