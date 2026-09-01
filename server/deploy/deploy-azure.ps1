#requires -Version 7.0
<#
.SYNOPSIS
    One-click deploy for the Lucky5 v8 API to Azure Container Apps with
    durable, seamless game-state persistence.

.DESCRIPTION
    Lucky5's authoritative game state lives in a single per-process in-memory
    store (Lucky5.Infrastructure.Services.InMemoryDataStore). Persistence is
    checkpoint/recover, not distributed. This script deploys the service so that:
      - It is pinned to exactly 1 Container Apps replica (correctness
        requirement - two concurrent replicas would each have their own divergent
        in-memory truth).
      - An Azure Files share is mounted at LUCKY5_STATE_DIR, so the 10-second
        periodic checkpoint and the on-shutdown final checkpoint both write to
        durable storage that survives redeploys, crashes, and scale-to-zero.
      - The JWT signing key is a strong random key stored as a Container Apps
        secret, never the checked-in appsettings.json dev default.
    No Docker is required locally - the container image is built remotely via
    Azure Container Registry (ACR) build task (az acr build).

.PARAMETER ResourceGroup
    The Azure resource group to deploy into. Created if it does not exist.

.PARAMETER Location
    The Azure region (e.g. westeurope, eastus). Must support Container Apps.

.PARAMETER MakePublic
    If set (default), the container app ingress is configured for external
    (public) access on port 8080.

.EXAMPLE
    ./deploy-azure.ps1
    Deploys with all defaults (creates the resource group if needed).

.EXAMPLE
    ./deploy-azure.ps1 -ResourceGroup my-rg -Location eastus
    Deploys to a specific resource group and region.
#>
[CmdletBinding()]
param(
    [string]$Subscription = '',
    [string]$ResourceGroup = 'lucky5-v8-rg',
    [string]$Location = 'westeurope',
    [string]$Environment = 'lucky5-v8',
    [string]$ContainerApp = 'lucky5-v8',
    [string]$AcrName = '',
    [string]$StorageAccountName = '',
    [string]$FileShareName = 'lucky5-snapshots',
    [string]$MountPath = '/mnt/snapshots',
    [bool]$MakePublic = $true
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')

# Derive default names from resource group to avoid global uniqueness collisions.
if (-not $AcrName)   { $AcrName = ($ResourceGroup -replace '[^a-zA-Z0-9]', '').ToLower() }
if (-not $StorageAccountName) { $StorageAccountName = ($ResourceGroup -replace '[^a-z0-9]', '').ToLower() }
if ($StorageAccountName.Length -gt 24) { $StorageAccountName = $StorageAccountName.Substring(0, 24) }

function Step($message) {
    Write-Host "==> $message" -ForegroundColor Cyan
}

# ── 0. Subscription (optional) ────────────────────────────────────────────
if ($Subscription) {
    Step "Setting active subscription to $Subscription"
    az account set --subscription $Subscription
}

# ── 1. Resource group ──────────────────────────────────────────────────────
Step "Ensuring resource group '$ResourceGroup' exists in $Location"
if ((az group exists --name $ResourceGroup) -eq 'false') {
    az group create --name $ResourceGroup --location $Location | Out-Null
}

# ── 2. Azure Container Registry ────────────────────────────────────────────
Step "Ensuring ACR '$AcrName' exists"
if ((az acr check-name --name $AcrName --query 'nameAvailable' -o tsv) -eq 'true') {
    az acr create --resource-group $ResourceGroup --name $AcrName --sku Basic --admin-enabled true --location $Location | Out-Null
}
else {
    Write-Host "    ACR already exists, skipping create"
}
$acrLoginServer = az acr show --name $AcrName --resource-group $ResourceGroup --query 'loginServer' -o tsv
$acrCreds = az acr credential show --name $AcrName --resource-group $ResourceGroup --query 'passwords[0].value' -o tsv

# ── 3. Storage account + Azure Files share (state persistence) ──────────────
Step "Ensuring storage account '$StorageAccountName' exists"
if ((az storage account check-name --name $StorageAccountName --query 'nameAvailable' -o tsv) -eq 'true') {
    az storage account create --name $StorageAccountName --resource-group $ResourceGroup --location $Location --sku Standard_LRS --kind StorageV2 | Out-Null
}
else {
    Write-Host "    Storage account already exists, skipping create"
}

Step "Ensuring file share '$FileShareName' exists"
if ((az storage share-rm exists --name $FileShareName --storage-account $StorageAccountName --resource-group $ResourceGroup --query 'exists' -o tsv) -ne 'true') {
    az storage share-rm create --name $FileShareName --storage-account $StorageAccountName --resource-group $ResourceGroup --quota 1 | Out-Null
}

# ── 4. Generate strong JWT signing key ─────────────────────────────────────
Step "Generating strong JWT signing key (48-byte random, base64)"
$bytes = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$jwtSigningKey = [Convert]::ToBase64String($bytes)
Remove-Variable bytes

# ── 5. Build container image via ACR build task (no local Docker) ───────────
Step "Building container image via ACR build task (no local Docker required)"
$imageTag = "$acrLoginServer/lucky5-v8:latest"
Push-Location $repoRoot
try {
    az acr build --registry $AcrName --resource-group $ResourceGroup --image $imageTag --file Dockerfile .
}
finally {
    Pop-Location
}

# ── 6. Container Apps environment ──────────────────────────────────────────
Step "Ensuring Container Apps environment '$Environment' exists"
if (-not (az containerapp env show --name $Environment --resource-group $ResourceGroup 2>$null)) {
    az containerapp env create --name $Environment --resource-group $ResourceGroup --location $Location --logs-destination none | Out-Null
}
else {
    Write-Host "    Container Apps environment already exists, skipping create"
}

# ── 7. Deploy Container App ────────────────────────────────────────────────
Step "Deploying Container App '$ContainerApp' (pinned to 1 replica, Azure Files mounted)"
$ingressType = if ($MakePublic) { 'external' } else { 'internal' }

# Volume and secret configuration (applied identically on create and update).
$volumeDef = "name=snapshots,storageType=azureFile,storageName=$StorageAccountName"
$volumeMount = "volumeName=snapshots,mountPath=$MountPath"
$secretDef = "jwt-signing-key=$jwtSigningKey"
$envVars = "LUCKY5_STATE_DIR=$MountPath", "ASPNETCORE_ENVIRONMENT=Production"
$secretEnvVar = "JWT__SIGNING_KEY=secretref:jwt-signing-key"

$appExists = az containerapp show --name $ContainerApp --resource-group $ResourceGroup 2>$null
if (-not $appExists) {
    az containerapp create `
        --name $ContainerApp `
        --resource-group $ResourceGroup `
        --environment $Environment `
        --image $imageTag `
        --registry-server $acrLoginServer `
        --registry-username $AcrName `
        --registry-password $acrCreds `
        --target-port 8080 `
        --ingress $ingressType `
        --min-replicas 1 `
        --max-replicas 1 `
        --cpu 1.0 `
        --memory 2.0Gi `
        --env-vars $envVars `
        --secrets $secretDef `
        --env-vars $secretEnvVar `
        --volumes $volumeDef `
        --volume-mounts $volumeMount
}
else {
    Write-Host "    Container app already exists, updating..." -ForegroundColor Yellow
    az containerapp update `
        --name $ContainerApp `
        --resource-group $ResourceGroup `
        --image $imageTag `
        --min-replicas 1 `
        --max-replicas 1 `
        --cpu 1.0 `
        --memory 2.0Gi `
        --env-vars $envVars `
        --secrets $secretDef `
        --env-vars $secretEnvVar `
        --volumes $volumeDef `
        --volume-mounts $volumeMount
}

# ── 8. Print result ────────────────────────────────────────────────────────
$url = az containerapp show --name $ContainerApp --resource-group $ResourceGroup --query 'properties.configuration.ingress.fqdn' -o tsv
Step "Deployed. Service URL: https://$url"
Write-Host "Verify with: curl https://$url/health/live" -ForegroundColor Green
