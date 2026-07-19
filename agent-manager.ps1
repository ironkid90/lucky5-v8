<#
.SYNOPSIS
    Lucky5 v8 Agent Manager - Simplified background agent management for Windows

.DESCRIPTION
    Manages background Hermes agents using PowerShell jobs for true background execution.
    Each agent runs in its own git worktree with isolated context.

.PARAMETER Action
    Start, Stop, Status, Send, List, Logs

.PARAMETER Agent
    Agent name: backend, frontend, test, coord, or "all"

.PARAMETER Message
    Message to send to agent (for Send action)

.PARAMETER WorktreeBase
    Base directory for git worktrees
#>

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("Start", "Stop", "Status", "Send", "List", "Logs", "Clean")]
    [string]$Action,

    [Parameter(Mandatory=$false)]
    [string]$Agent = "all",

    [Parameter(Mandatory=$false)]
    [string]$Message = "",

    [Parameter(Mandatory=$false)]
    [string]$WorktreeBase = "..\lucky5-v8-worktrees"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Definition
$PROJECT_ROOT = Resolve-Path $SCRIPT_DIR
$WORKTREE_BASE = Join-Path $PROJECT_ROOT $WorktreeBase
$JOBS_DIR = Join-Path $PROJECT_ROOT ".agent-jobs"

# Agent configurations
$Agents = @{
    backend = @{
        Name = "backend"
        DisplayName = "Backend API"
        Branch = "agent/backend"
        WorktreePath = Join-Path $WORKTREE_BASE "backend"
        WorkingDir = Join-Path $WORKTREE_BASE "backend\server\src\Lucky5.Api"
        Prompt = @"
You are a backend developer for Lucky5 v8 (.NET 10). Work in server/src/Lucky5.Api/.
Focus areas:
- API Controllers (Controllers/)
- SignalR Hubs (Real-time game state)
- Game Logic: Lucky5.Domain/Game/CleanRoom/ (deterministic, authoritative)
- Database Models & Migrations (Lucky5.Infrastructure)
- Authentication/Authorization
- Jackpot & Double-Up logic

Build: dotnet build server/Lucky5.sln
Test: dotnet run --project server/tests/Lucky5.Tests/Lucky5.Tests.csproj
Run API: dotnet run --project server/src/Lucky5.Api/Lucky5.Api.csproj --no-launch-profile

Key files to know:
- server/src/Lucky5.Api/Program.cs
- server/src/Lucky5.Api/Controllers/*.cs
- server/src/Lucky5.Domain/Game/CleanRoom/*.cs
- server/src/Lucky5.Application/*.cs

Retro cabinet aesthetic is CORE - backend serves the web cabinet at wwwroot/
"@
    }
    frontend = @{
        Name = "frontend"
        DisplayName = "Frontend Cabinet"
        Branch = "agent/frontend"
        WorktreePath = Join-Path $WORKTREE_BASE "frontend"
        WorkingDir = Join-Path $WORKTREE_BASE "frontend\server\src\Lucky5.Api\wwwroot"
        Prompt = @"
You are a frontend developer for Lucky5 v8 web cabinet. Work in server/src/Lucky5.Api/wwwroot/.
Focus areas:
- Vanilla HTML/CSS/JS (NO React/Vue/Godot - retro cabinet requirement)
- Portrait 9:16 cabinet layout
- Card rendering: 100% DOM-based (no /assets/images/cards/)
- VSYNC-locked 60Hz animations, 12-frame stagger (dealStaggerMs:350, drawStaggerMs:100)
- Button system: PNG image mapping (DEAL/DRAW=red, BET=green)
- SignalR client for real-time game state
- AI9/ai9poker visual parity (cabinet-ai9-parity.css)
- CRT effects, scanlines, pixel-perfect retro aesthetic

Key files:
- index.html (cabinet structure)
- js/game.js (main game logic)
- js/cabinet-*.js (orchestrator, stage, pace, state, shell, audio, transition, effects)
- css/game.css, css/cabinet-*.css

Build: No build step - static files served by API
Test: Open http://localhost:5051 in browser
"@
    }
    test = @{
        Name = "test"
        DisplayName = "Test Engineer"
        Branch = "agent/test"
        WorktreePath = Join-Path $WORKTREE_BASE "test"
        WorkingDir = Join-Path $WORKTREE_BASE "test\server\tests\Lucky5.Tests"
        Prompt = @"
You are a test engineer for Lucky5 v8. Work in server/tests/Lucky5.Tests/.
Focus areas:
- Unit tests for CleanRoom game logic (deterministic, no randomness in tests)
- Integration tests for API endpoints
- SignalR hub testing
- Game state machine validation
- Jackpot, Double-Up, Bonus Poker logic
- Card dealing/drawing/holding mechanics

Run tests: dotnet run --project server/tests/Lucky5.Tests/Lucky5.Tests.csproj
Build: dotnet build server/Lucky5.sln

Key test areas:
- Hand evaluation (Royal Flush → High Card)
- Hold/discard strategy validation
- Double-Up (gamble) logic: Ace high/low, 5 never loses when buying
- Jackpot contributions & triggers (4K-A, 4K-B, SF)
- Session state persistence (in-memory default)
"@
    }
    coord = @{
        Name = "coord"
        DisplayName = "Coordinator"
        Branch = "main"
        WorktreePath = $PROJECT_ROOT
        WorkingDir = $PROJECT_ROOT
        Prompt = @"
You are the Coordinator for Lucky5 v8 multi-agent development.
Monitor backend, frontend, and test agents. Responsibilities:
- Relay context between agents
- Resolve merge conflicts in worktrees
- Ensure API/frontend contract consistency
- Coordinate testing with implementation
- Track overall progress and blockers

Use Send-MessageToAgent to communicate with other agents.
Main repo at: $PROJECT_ROOT
Worktrees at: $WORKTREE_BASE
"@
    }
}

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $ts = Get-Date -Format "HH:mm:ss"
    $color = @{INFO="Cyan"; WARN="Yellow"; ERROR="Red"; SUCCESS="Green"}[$Level]
    Write-Host "[$ts] [$Level] $Message" -ForegroundColor $color
}

function Ensure-JobsDir {
    if (-not (Test-Path $JOBS_DIR)) {
        New-Item -ItemType Directory -Path $JOBS_DIR -Force | Out-Null
    }
}

function Get-AgentJobPath {
    param([string]$AgentName)
    return Join-Path $JOBS_DIR "$AgentName.job"
}

function Get-AgentLogPath {
    param([string]$AgentName)
    return Join-Path $JOBS_DIR "$AgentName.log"
}

function Get-AgentPidPath {
    param([string]$AgentName)
    return Join-Path $JOBS_DIR "$AgentName.pid"
}

function Setup-Worktree {
    param([hashtable]$Agent)
    
    $worktreePath = $Agent.WorktreePath
    $branch = $Agent.Branch
    
    if (Test-Path $worktreePath) {
        Write-Log "Worktree exists for $($Agent.Name) at $worktreePath" -Level "WARN"
        return $true
    }
    
    Push-Location $PROJECT_ROOT
    
    # Create branch if needed - check using show-ref for reliability
    $branchRef = "refs/heads/$branch"
    $branchExists = git show-ref --verify $branchRef 2>$null
    if (-not $branchExists) {
        Write-Log "Creating branch $branch for $($Agent.Name)..."
        git branch $branch main
        if ($LASTEXITCODE -ne 0) {
            Write-Log "Failed to create branch $branch" -Level "ERROR"
            Pop-Location
            return $false
        }
        $useNewBranch = $true
    } else {
        Write-Log "Branch $branch already exists for $($Agent.Name)" -Level "WARN"
        $useNewBranch = $false
    }
    
    # Create worktree
    Write-Log "Creating worktree for $($Agent.Name) at $worktreePath..."
    if ($useNewBranch) {
        git worktree add -b $branch $worktreePath main
    } else {
        git worktree add $worktreePath $branch
    }
    $result = $LASTEXITCODE -eq 0
    
    Pop-Location
    
    if ($result) {
        Write-Log "Worktree created for $($Agent.Name)" -Level "SUCCESS"
    } else {
        Write-Log "Failed to create worktree for $($Agent.Name)" -Level "ERROR"
    }
    
    return $result
}

function Start-Agent {
    param([hashtable]$Agent)
    
    $name = $Agent.Name
    $logPath = Get-AgentLogPath $name
    $pidPath = Get-AgentPidPath $name
    $jobPath = Get-AgentJobPath $name
    
    # Check if already running
    if (Test-Path $pidPath) {
        $existingPid = Get-Content $pidPath -Raw
        $proc = Get-Process -Id $existingPid -ErrorAction SilentlyContinue
        if ($proc) {
            Write-Log "$($Agent.DisplayName) already running (PID: $existingPid)" -Level "WARN"
            return $true
        } else {
            Write-Log "Stale PID file for $name, cleaning up..." -Level "WARN"
            Remove-Item $pidPath -Force -ErrorAction SilentlyContinue
        }
    }
    
    # Setup worktree (skip for coordinator)
    if ($name -ne "coord") {
        if (-not (Setup-Worktree $Agent)) {
            return $false
        }
    }
    
    Write-Log "Starting $($Agent.DisplayName) agent..."
    
    # Create the PowerShell script that runs the agent
    $agentScript = @"
`$ErrorActionPreference = 'Stop'
`$progressPreference = 'SilentlyContinue'

# Set up working directory
Set-Location '$($Agent.WorkingDir)'

# Log start
Write-Host \"[$(Get-Date -Format 'HH:mm:ss')] Starting $($Agent.DisplayName) agent\" -ForegroundColor Cyan
Write-Host \"[$(Get-Date -Format 'HH:mm:ss')] Working directory: \$PWD\" -ForegroundColor Gray

# Run hermes with the initial prompt
hermes chat -q @'
$($Agent.Prompt -replace "@", "@@")
'@ 2>&1 | Tee-Object -FilePath '$logPath' -Append

Write-Host \"[$(Get-Date -Format 'HH:mm:ss')] $($Agent.DisplayName) agent exited\" -ForegroundColor Yellow
"@
    
    # Start as background job
    $job = Start-Job -ScriptBlock ([ScriptBlock]::Create($agentScript)) -Name "Lucky5Agent-$name"
    
    # Save job and PID
    $job | Export-Clixml $jobPath
    $job.Id | Out-File $pidPath
    
    Write-Log "$($Agent.DisplayName) started as background job (Job ID: $($job.Id))" -Level "SUCCESS"
    Write-Log "  Log: $logPath" -Level "INFO"
    Write-Log "  Job: $jobPath" -Level "INFO"
    
    return $true
}

function Stop-Agent {
    param([hashtable]$Agent)
    
    $name = $Agent.Name
    $pidPath = Get-AgentPidPath $name
    $jobPath = Get-AgentJobPath $name
    
    Write-Log "Stopping $($Agent.DisplayName)..."
    
    # Try to get and stop the job
    if (Test-Path $jobPath) {
        try {
            $job = Import-Clixml $jobPath
            if ($job.State -eq 'Running') {
                Stop-Job $job
                Write-Log "Job stopped for $name" -Level "SUCCESS"
            }
        } catch {
            Write-Log "Error stopping job: $_" -Level "WARN"
        }
        Remove-Item $jobPath -Force -ErrorAction SilentlyContinue
    }
    
    # Kill process if PID exists
    if (Test-Path $pidPath) {
        $agentPid = Get-Content $pidPath -Raw
        try {
            Stop-Process -Id $agentPid -Force -ErrorAction SilentlyContinue
            Write-Log "Process $agentPid terminated for $name" -Level "INFO"
        } catch {
            Write-Log "Could not terminate process $agentPid" -Level "WARN"
        }
        Remove-Item $pidPath -Force -ErrorAction SilentlyContinue
    }
    
    # Also kill any hermes processes for this worktree
    if ($name -ne "coord") {
        $worktreePath = $Agent.WorktreePath
        Get-Process hermes -ErrorAction SilentlyContinue | Where-Object { 
            $_.Path -like "*$worktreePath*" 
        } | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    
    Write-Log "$($Agent.DisplayName) stopped" -Level "SUCCESS"
}

function Get-AgentStatus {
    param([hashtable]$Agent)
    
    $name = $Agent.Name
    $pidPath = Get-AgentPidPath $name
    $jobPath = Get-AgentJobPath $name
    $logPath = Get-AgentLogPath $name
    
    $running = $false
    $agentPid = $null
    $jobState = "Unknown"
    
    if (Test-Path $pidPath) {
        $agentPid = Get-Content $pidPath -Raw
        $proc = Get-Process -Id $agentPid -ErrorAction SilentlyContinue
        if ($proc) {
            $running = $true
        }
    }
    
    if (Test-Path $jobPath) {
        try {
            $job = Import-Clixml $jobPath
            $jobState = $job.State
            if ($jobState -eq 'Running') { $running = $true }
        } catch { }
    }
    
    $lastLog = ""
    if (Test-Path $logPath) {
        $lines = Get-Content $logPath -Tail 3 -ErrorAction SilentlyContinue
        $lastLog = $lines -join " | "
    }
    
    return @{
        Name = $name
        DisplayName = $Agent.DisplayName
        Running = $running
        PID = $agentPid
        JobState = $jobState
        LogPath = $logPath
        WorktreePath = $Agent.WorktreePath
        Branch = $Agent.Branch
        LastLog = $lastLog
    }
}

function Send-MessageToAgent {
    param([hashtable]$Agent, [string]$Message)
    
    # For background jobs, we can't easily send messages mid-execution
    # Instead, we'll append to a command queue file that the agent can poll
    $name = $Agent.Name
    $queuePath = Join-Path $JOBS_DIR "$name.commands"
    
    $cmd = @{
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        Message = $Message
    } | ConvertTo-Json -Compress
    
    Add-Content -Path $queuePath -Value $cmd
    Write-Log "Message queued for $($Agent.DisplayName): $Message" -Level "SUCCESS"
}

function Show-AgentLogs {
    param([hashtable]$Agent, [int]$Lines = 50)
    
    $logPath = Get-AgentLogPath $Agent.Name
    
    if (-not (Test-Path $logPath)) {
        Write-Log "No log file for $($Agent.DisplayName)" -Level "WARN"
        return
    }
    
    Write-Log "=== Last $Lines lines from $($Agent.DisplayName) ==="
    Get-Content $logPath -Tail $Lines | ForEach-Object { Write-Host $_ }
}

function Clean-Worktrees {
    Write-Log "Cleaning up worktrees and job files..."
    
    Push-Location $PROJECT_ROOT
    
    foreach ($agent in $Agents.Values) {
        if ($agent.Name -ne "coord") {
            $wtPath = $agent.WorktreePath
            if (Test-Path $wtPath) {
                Write-Log "Removing worktree: $wtPath"
                git worktree remove $wtPath -Force -ErrorAction SilentlyContinue
            }
            # Remove branch - quote the branch name to handle slashes
            $branchName = $agent.Branch
            git branch -D "$branchName" -ErrorAction SilentlyContinue
        }
    }
    
    # Clean job files
    if (Test-Path $JOBS_DIR) {
        Remove-Item $JOBS_DIR -Recurse -Force -ErrorAction SilentlyContinue
    }
    
    Pop-Location
    Write-Log "Cleanup complete" -Level "SUCCESS"
}

# Main
Ensure-JobsDir

$targetAgents = if ($Agent -eq "all") { $Agents.Values } else { 
        $agentNames = $Agent -split ',' | ForEach-Object { $_.Trim() }
        @($agentNames | ForEach-Object { $Agents[$_] })
    }

Write-Log "Lucky5 v8 Agent Manager - Action: $Action, Target: $($targetAgents.Count) agent(s)"

switch ($Action) {
    "Start" {
        foreach ($a in $targetAgents) {
            Start-Agent $a
            Start-Sleep -Seconds 2
        }
        Start-Sleep -Seconds 3
        # Show status after start
        foreach ($a in $targetAgents) {
            $s = Get-AgentStatus $a
            $status = if ($s.Running) { "RUNNING" } else { "STOPPED" }
            $color = if ($s.Running) { "Green" } else { "Red" }
            Write-Host "  $($s.DisplayName.PadRight(18)) : " -NoNewline
            Write-Host $status -ForegroundColor $color
        }
    }
    
    "Stop" {
        foreach ($a in $targetAgents) {
            Stop-Agent $a
        }
    }
    
    "Status" {
        Write-Log "=== Agent Status ==="
        foreach ($a in $targetAgents) {
            $s = Get-AgentStatus $a
            $status = if ($s.Running) { "RUNNING" } else { "STOPPED" }
            $color = if ($s.Running) { "Green" } else { "Red" }
            Write-Host "  $($s.DisplayName.PadRight(18)) : " -NoNewline
            Write-Host $status -ForegroundColor $color
            Write-Host "    Job State: $($s.JobState)"
            if ($s.PID) { Write-Host "    PID: $($s.PID)" }
            Write-Host "    Worktree: $($s.WorktreePath)"
            Write-Host "    Branch: $($s.Branch)"
            if ($s.LastLog) { Write-Host "    Last: $($s.LastLog)" }
            Write-Host ""
        }
    }
    
    "Send" {
        if ([string]::IsNullOrWhiteSpace($Message)) {
            Write-Log "Message required for Send action" -Level "ERROR"
            exit 1
        }
        foreach ($a in $targetAgents) {
            Send-MessageToAgent $a $Message
        }
    }
    
    "Logs" {
        foreach ($a in $targetAgents) {
            Show-AgentLogs $a 100
            Write-Host ""
        }
    }
    
    "List" {
        Write-Log "Available agents:"
        foreach ($a in $Agents.Values) {
            Write-Host "  $($a.Name.PadRight(10)) - $($a.DisplayName)"
        }
    }
    
    "Clean" {
        # Stop all first
        foreach ($a in $Agents.Values) {
            Stop-Agent $a
        }
        Clean-Worktrees
    }
}

Write-Log "Done."