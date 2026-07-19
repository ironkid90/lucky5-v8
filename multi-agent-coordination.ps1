<#
.SYNOPSIS
    Lucky5 v8 Multi-Agent Coordination System for Windows
    Spawns and manages multiple Hermes agents for parallel development tasks.

.DESCRIPTION
    This script provides Windows-compatible multi-agent coordination using Windows Terminal.
    It spawns separate Hermes instances for backend, frontend, and testing tasks,
    allowing parallel development with isolated git worktrees.

.PARAMETER Action
    Action to perform: Start, Stop, Status, Send, Capture, Coordinate

.PARAMETER Agents
    Comma-separated list of agents to manage: backend, frontend, test, all

.PARAMETER Message
    Message to send to specific agent (for Send action)

.PARAMETER WorktreePath
    Base path for git worktrees (default: ..\lucky5-v8-worktrees)

.EXAMPLE
    .\multi-agent-coordination.ps1 -Action Start -Agents all
    .\multi-agent-coordination.ps1 -Action Send -Agents backend -Message "Build REST API for user management"
    .\multi-agent-coordination.ps1 -Action Status
    .\multi-agent-coordination.ps1 -Action Capture -Agents frontend
    .\multi-agent-coordination.ps1 -Action Stop -Agents all
#>

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("Start", "Stop", "Status", "Send", "Capture", "Coordinate")]
    [string]$Action,

    [Parameter(Mandatory=$false)]
    [string]$Agents = "all",

    [Parameter(Mandatory=$false)]
    [string]$Message = "",

    [Parameter(Mandatory=$false)]
    [string]$WorktreePath = "..\lucky5-v8-worktrees"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Configuration
$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Definition
$PROJECT_ROOT = Resolve-Path $SCRIPT_DIR
$WORKTREE_BASE = Join-Path $PROJECT_ROOT $WorktreePath
$SESSION_PREFIX = "lucky5-agent"
$TERMINAL_WIDTH = 140
$TERMINAL_HEIGHT = 50

# Agent configurations
$AgentConfigs = @{
    backend = @{
        SessionName = "$SESSION_PREFIX-backend"
        WorktreePath = Join-Path $WORKTREE_BASE "backend"
        Branch = "agent/backend"
        InitialPrompt = "You are a backend developer for Lucky5 v8. Work in the .NET 10 API server at server/src/Lucky5.Api/. Focus on: API controllers, SignalR hubs, game logic in Lucky5.Domain/Game/CleanRoom/, database models, authentication, and real-time game state management. Use 'dotnet build server/Lucky5.sln' to build and 'dotnet run --project server/tests/Lucky5.Tests/Lucky5.Tests.csproj' to test."
        WorkingDir = Join-Path $WORKTREE_BASE "backend\server\src\Lucky5.Api"
    }
    frontend = @{
        SessionName = "$SESSION_PREFIX-frontend"
        WorktreePath = Join-Path $WORKTREE_BASE "frontend"
        Branch = "agent/frontend"
        InitialPrompt = "You are a frontend developer for Lucky5 v8. Work in the web cabinet at server/src/Lucky5.Api/wwwroot/. Focus on: vanilla HTML/CSS/JS (no frameworks), cabinet UI (portrait 9:16), card animations, button interactions, SignalR client, retro AI9/ai9poker aesthetic, VSYNC-locked 60Hz animations with 12-frame stagger. Key files: index.html, js/game.js, js/cabinet-*.js, css/*.css."
        WorkingDir = Join-Path $WORKTREE_BASE "frontend\server\src\Lucky5.Api\wwwroot"
    }
    test = @{
        SessionName = "$SESSION_PREFIX-test"
        WorktreePath = Join-Path $WORKTREE_BASE "test"
        Branch = "agent/test"
        InitialPrompt = "You are a test engineer for Lucky5 v8. Work in server/tests/Lucky5.Tests/. Focus on: unit tests for CleanRoom game logic, integration tests for API endpoints, SignalR hub testing, game state machine validation, jackpot and double-up logic. Run tests with 'dotnet run --project server/tests/Lucky5.Tests/Lucky5.Tests.csproj'."
        WorkingDir = Join-Path $WORKTREE_BASE "test\server\tests\Lucky5.Tests"
    }
    coord = @{
        SessionName = "$SESSION_PREFIX-coord"
        WorktreePath = $PROJECT_ROOT
        Branch = "main"
        InitialPrompt = "You are the coordinator for Lucky5 v8 multi-agent development. Monitor other agents (backend, frontend, test), relay context between them, resolve conflicts, and ensure consistency. Use the coordinate action to send messages to other agents."
        WorkingDir = $PROJECT_ROOT
    }
}

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "HH:mm:ss"
    $color = switch ($Level) {
        "INFO" { "Cyan" }
        "WARN" { "Yellow" }
        "ERROR" { "Red" }
        "SUCCESS" { "Green" }
        default { "White" }
    }
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

function Check-Prerequisites {
    Write-Log "Checking prerequisites..."
    
    # Check Windows Terminal
    if (-not (Get-Command wt -ErrorAction SilentlyContinue)) {
        Write-Log "Windows Terminal (wt) not found. Install from Microsoft Store." -Level "ERROR"
        return $false
    }
    
    # Check Hermes
    if (-not (Get-Command hermes -ErrorAction SilentlyContinue)) {
        Write-Log "Hermes CLI not found. Run 'pip install hermes-agent' or use the installer." -Level "ERROR"
        return $false
    }
    
    # Check git
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Log "Git not found in PATH." -Level "ERROR"
        return $false
    }
    
    # Check .NET
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Log ".NET SDK not found." -Level "ERROR"
        return $false
    }
    
    Write-Log "All prerequisites satisfied." -Level "SUCCESS"
    return $true
}

function Setup-Worktrees {
    Write-Log "Setting up git worktrees at $WORKTREE_BASE..."
    
    if (-not (Test-Path $WORKTREE_BASE)) {
        New-Item -ItemType Directory -Path $WORKTREE_BASE -Force | Out-Null
    }
    
    Push-Location $PROJECT_ROOT
    
    foreach ($agentName in $AgentConfigs.Keys) {
        if ($agentName -eq "coord") { continue }  # Coordinator uses main repo
        
        $config = $AgentConfigs[$agentName]
        $worktreePath = $config.WorktreePath
        $branch = $config.Branch
        
        if (Test-Path $worktreePath) {
            Write-Log "Worktree for $agentName already exists at $worktreePath" -Level "WARN"
            continue
        }
        
        # Create branch if it doesn't exist
        $branchExists = git branch -a --list $branch | Select-String $branch
        if (-not $branchExists) {
            Write-Log "Creating branch $branch for $agentName..."
            git branch $branch main
        }
        
        # Create worktree
        Write-Log "Creating worktree for $agentName at $worktreePath..."
        git worktree add -b $branch $worktreePath main
        
        if ($LASTEXITCODE -ne 0) {
            Write-Log "Failed to create worktree for $agentName" -Level "ERROR"
        } else {
            Write-Log "Worktree created for $agentName" -Level "SUCCESS"
        }
    }
    
    Pop-Location
}

function Start-Agent {
    param([string]$AgentName)
    
    if (-not $AgentConfigs.ContainsKey($AgentName)) {
        Write-Log "Unknown agent: $AgentName" -Level "ERROR"
        return $false
    }
    
    $config = $AgentConfigs[$AgentName]
    $sessionName = $config.SessionName
    
    # Check if session already exists
    $existing = wt list | Where-Object { $_ -like "*$sessionName*" }
    if ($existing) {
        Write-Log "Agent $AgentName already running in session $sessionName" -Level "WARN"
        return $true
    }
    
    Write-Log "Starting $AgentName agent in session $sessionName..."
    
    # Build the command to run in the new tab
    $hermesCmd = "hermes -w"
    $initialPrompt = $config.InitialPrompt
    
    # Use Windows Terminal to create a new tab with the agent
    $wtArgs = @(
        "new-tab",
        "--title", $sessionName,
        "--profile", "PowerShell",
        "--startingDirectory", $config.WorkingDir,
        "powershell", "-NoExit", "-Command",
        "& { Write-Host 'Starting Hermes agent: $AgentName' -ForegroundColor Cyan; hermes chat -q `"$initialPrompt`" }"
    )
    
    # Start the terminal tab
    try {
        Start-Process "wt" -ArgumentList $wtArgs -NoNewWindow
        Start-Sleep -Seconds 3
        
        # Verify session started
        $check = wt list | Where-Object { $_ -like "*$sessionName*" }
        if ($check) {
            Write-Log "$AgentName agent started successfully" -Level "SUCCESS"
            return $true
        } else {
            Write-Log "Failed to verify $AgentName session" -Level "ERROR"
            return $false
        }
    } catch {
        Write-Log "Error starting $AgentName: $_" -Level "ERROR"
        return $false
    }
}

function Stop-Agent {
    param([string]$AgentName)
    
    if (-not $AgentConfigs.ContainsKey($AgentName)) {
        Write-Log "Unknown agent: $AgentName" -Level "ERROR"
        return $false
    }
    
    $config = $AgentConfigs[$AgentName]
    $sessionName = $config.SessionName
    
    Write-Log "Stopping $AgentName agent (session: $sessionName)..."
    
    # Send exit command to the session
    try {
        wt send-keys --tab-title $sessionName "/exit`r"
        Start-Sleep -Seconds 2
        
        # Force close if still running
        $check = wt list | Where-Object { $_ -like "*$sessionName*" }
        if ($check) {
            wt close-tab --tab-title $sessionName
            Start-Sleep -Seconds 1
        }
        
        Write-Log "$AgentName agent stopped" -Level "SUCCESS"
        return $true
    } catch {
        Write-Log "Error stopping $AgentName: $_" -Level "ERROR"
        return $false
    }
}

function Get-AgentStatus {
    param([string]$AgentName)
    
    if (-not $AgentConfigs.ContainsKey($AgentName)) {
        return @{ Name = $AgentName; Running = $false; Error = "Unknown agent" }
    }
    
    $config = $AgentConfigs[$AgentName]
    $sessionName = $config.SessionName
    
    $wtList = wt list 2>$null
    $running = $wtList | Where-Object { $_ -like "*$sessionName*" }
    
    return @{
        Name = $AgentName
        Running = [bool]$running
        SessionName = $sessionName
        WorktreePath = $config.WorktreePath
        Branch = $config.Branch
    }
}

function Send-MessageToAgent {
    param([string]$AgentName, [string]$Message)
    
    if (-not $AgentConfigs.ContainsKey($AgentName)) {
        Write-Log "Unknown agent: $AgentName" -Level "ERROR"
        return $false
    }
    
    if ([string]::IsNullOrWhiteSpace($Message)) {
        Write-Log "Message cannot be empty" -Level "ERROR"
        return $false
    }
    
    $config = $AgentConfigs[$AgentName]
    $sessionName = $config.SessionName
    
    Write-Log "Sending message to $AgentName..."
    
    # Escape quotes and special characters for PowerShell
    $escapedMsg = $Message -replace '"', '`"' -replace '`$', '`$'
    
    try {
        wt send-keys --tab-title $sessionName "hermes chat -q `"$escapedMsg`"`r"
        Write-Log "Message sent to $AgentName" -Level "SUCCESS"
        return $true
    } catch {
        Write-Log "Error sending message to $AgentName: $_" -Level "ERROR"
        return $false
    }
}

function Capture-AgentOutput {
    param([string]$AgentName)
    
    if (-not $AgentConfigs.ContainsKey($AgentName)) {
        Write-Log "Unknown agent: $AgentName" -Level "ERROR"
        return $null
    }
    
    $config = $AgentConfigs[$AgentName]
    $sessionName = $config.SessionName
    
    Write-Log "Capturing output from $AgentName..."
    
    try {
        # Capture the pane content
        $output = wt capture-pane --tab-title $sessionName -p 2>$null
        return $output
    } catch {
        Write-Log "Error capturing output from $AgentName: $_" -Level "ERROR"
        return $null
    }
}

function Coordinate-Agents {
    param([string]$Message)
    
    if ([string]::IsNullOrWhiteSpace($Message)) {
        Write-Log "Coordination message cannot be empty" -Level "ERROR"
        return $false
    }
    
    Write-Log "Coordinating agents with message: $Message"
    
    # Send to coordinator first
    Send-MessageToAgent -AgentName "coord" -Message "COORDINATION: $Message"
    Start-Sleep -Seconds 1
    
    # Then broadcast to all working agents
    foreach ($agentName in @("backend", "frontend", "test")) {
        Send-MessageToAgent -AgentName $agentName -Message "COORDINATION FROM COORDINATOR: $Message"
        Start-Sleep -Milliseconds 500
    }
    
    Write-Log "Coordination message sent to all agents" -Level "SUCCESS"
    return $true
}

function Show-Status {
    Write-Log "=== Multi-Agent Status ==="
    
    $allAgents = if ($Agents -eq "all") { $AgentConfigs.Keys } else { $Agents -split ',' }
    
    foreach ($agentName in $allAgents) {
        $status = Get-AgentStatus -AgentName $agentName.Trim()
        
        $runningStatus = if ($status.Running) { "RUNNING" } else { "STOPPED" }
        $color = if ($status.Running) { "Green" } else { "Red" }
        
        Write-Host "  $($status.Name.PadRight(10)) : " -NoNewline
        Write-Host "$runningStatus" -ForegroundColor $color
        Write-Host "    Session: $($status.SessionName)"
        Write-Host "    Worktree: $($status.WorktreePath)"
        Write-Host "    Branch: $($status.Branch)"
        Write-Host ""
    }
}

# Main execution
Write-Log "Lucky5 v8 Multi-Agent Coordination System"
Write-Log "Action: $Action, Agents: $Agents"

if (-not (Check-Prerequisites)) {
    exit 1
}

$targetAgents = if ($Agents -eq "all") { @("backend", "frontend", "test", "coord") } else { $Agents -split ',' }

switch ($Action) {
    "Start" {
        Setup-Worktrees
        foreach ($agent in $targetAgents) {
            Start-Agent -AgentName $agent.Trim()
            Start-Sleep -Seconds 2
        }
        Show-Status
    }
    
    "Stop" {
        foreach ($agent in $targetAgents) {
            Stop-Agent -AgentName $agent.Trim()
        }
        Show-Status
    }
    
    "Status" {
        Show-Status
    }
    
    "Send" {
        if ([string]::IsNullOrWhiteSpace($Message)) {
            Write-Log "Message parameter required for Send action" -Level "ERROR"
            exit 1
        }
        foreach ($agent in $targetAgents) {
            Send-MessageToAgent -AgentName $agent.Trim() -Message $Message
        }
    }
    
    "Capture" {
        foreach ($agent in $targetAgents) {
            $output = Capture-AgentOutput -AgentName $agent.Trim()
            if ($output) {
                Write-Log "=== Output from $agent ==="
                Write-Host $output
                Write-Host ""
            }
        }
    }
    
    "Coordinate" {
        if ([string]::IsNullOrWhiteSpace($Message)) {
            Write-Log "Message parameter required for Coordinate action" -Level "ERROR"
            exit 1
        }
        Coordinate-Agents -Message $Message
    }
}

Write-Log "Done."