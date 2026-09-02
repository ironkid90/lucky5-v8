#requires -Version 7.0
<#
.SYNOPSIS
    One-click local deploy of Lucky5 v8 to Azure Container Apps.

.DESCRIPTION
    Creates (idempotently) the resource group, ACR, storage account with an
    Azure Files share, Container Apps environment with the share mounted, a
    user-assigned pull identity, and the container app itself. The image is
    built in ACR (no local Docker required). Replicas are pinned to 1 for
    in-memory correctness — state snapshots persist to Azure Files via
    LUCKY5_STATE_DIR=/mnt/state.

    Every input can come from parameters or environment variables with the
    same names as the GitHub Actions secrets, so the script matches CI:
      AZURE_SUBSCRIPTION_ID, AZURE_RESOURCE_GROUP, AZURE_LOCATION, AZURE_ACR_NAME

.EXAMPLE
    ./server/deploy/deploy-azure.ps1

.EXAMPLE
    ./server/deploy/deploy-azure.ps1 -ResourceGroup lucky5-v8-rg -Location westeurope -AcrName lucky5v8acr
#>
[CmdletBinding()]
param(
    [string] $SubscriptionId = $env:AZURE_SUBSCRIPTION_ID,
    [string] $ResourceGroup  = $(if ($env:AZURE_RESOURCE_GROUP) { $env:AZURE_RESOURCE_GROUP } else { 'lucky5-v8-rg' }),
    [string] $Location       = $(if ($env:AZURE_LOCATION) { $env:AZURE_LOCATION } else { 'westeurope' }),
    [string] $AcrName        = $env:AZURE_ACR_NAME,
    [string] $ServiceName    = 'lucky5-v8',
    [string] $ImageTag       = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Default the image tag to the short SHA of the current commit (when available).
if ([string]::IsNullOrWhiteSpace($ImageTag)) {
    $ImageTag = (git rev-parse --short HEAD 2>$null)
    if ([string]::IsNullOrWhiteSpace($ImageTag)) { $ImageTag = 'local' }
}

function Step([string] $Message) { Write-Host "`n=== $Message ===" -ForegroundColor Cyan }

# ── Prerequisites ────────────────────────────────────────────────────────────
Step 'Checking prerequisites'
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI (az) not found. Install from https://aka.ms/installazurecliwindows'
}
az account show --output none 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Not logged in — launching az login...'
    az login | Out-Null
}
if ($SubscriptionId) { az account set --subscription $SubscriptionId }
$SubscriptionId = (az account show --query id -o tsv)
if (-not $AcrName) { throw 'ACR name is required: pass -AcrName or set AZURE_ACR_NAME (globally unique, lowercase alphanumeric).' }
$AcrName = $AcrName.ToLower()
Write-Host "Subscription: $SubscriptionId"
Write-Host "Resource group: $ResourceGroup | Location: $Location | ACR: $AcrName | Tag: $ImageTag"

az provider register --namespace Microsoft.App --wait --output none 2>$null
az provider register --namespace Microsoft.OperationalInsights --wait --output none 2>$null
az extension add --name containerapp --upgrade --yes --output none 2>$null

# ── Resource group + ACR ─────────────────────────────────────────────────────
Step 'Ensuring resource group and ACR'
az group create --name $ResourceGroup --location $Location --output none
$acrExists = $false
az acr show --name $AcrName --resource-group $ResourceGroup --output none 2>$null
$acrExists = ($LASTEXITCODE -eq 0)
if (-not $acrExists) {
    az acr create --name $AcrName --resource-group $ResourceGroup --location $Location --sku Basic --output none
}

# ── Build image in ACR ───────────────────────────────────────────────────────
Step "Building image in ACR (tag $ImageTag)"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
az acr build --registry $AcrName --resource-group $ResourceGroup `
    --image "${ServiceName}:${ImageTag}" --image "${ServiceName}:latest" `
    --file Dockerfile $RepoRoot
$Image = "$AcrName.azurecr.io/${ServiceName}:${ImageTag}"

# ── Pull identity ────────────────────────────────────────────────────────────
Step 'Ensuring AcrPull identity'
$PullIdentityName = "$ServiceName-pull"
az identity show --name $PullIdentityName --resource-group $ResourceGroup --output none 2>$null
if ($LASTEXITCODE -ne 0) {
    az identity create --name $PullIdentityName --resource-group $ResourceGroup --location $Location --output none
}
$PullIdentityId = az identity show --name $PullIdentityName --resource-group $ResourceGroup --query id -o tsv
$PullPrincipal  = az identity show --name $PullIdentityName --resource-group $ResourceGroup --query principalId -o tsv
$AcrId = az acr show --name $AcrName --resource-group $ResourceGroup --query id -o tsv
az role assignment create --assignee $PullPrincipal --role AcrPull --scope $AcrId --output none 2>$null

# ── Storage account + Azure Files share ──────────────────────────────────────
Step 'Ensuring storage account and Azure Files share'
$StorageName = ("${AcrName}state" -replace '[^a-z0-9]', '')
$StorageName = $StorageName.Substring(0, [Math]::Min(24, $StorageName.Length))
az storage account show --name $StorageName --resource-group $ResourceGroup --output none 2>$null
if ($LASTEXITCODE -ne 0) {
    az storage account create --name $StorageName --resource-group $ResourceGroup `
        --location $Location --sku Standard_LRS --kind StorageV2 --output none
}
az storage share create --name lucky5-state --account-name $StorageName --output none

# ── Container Apps environment with mounted share ────────────────────────────
Step 'Ensuring Container Apps environment'
$EnvName = "$ServiceName-env"
$StorageKey = az storage account keys list --account-name $StorageName --resource-group $ResourceGroup --query '[0].value' -o tsv
az containerapp env show --name $EnvName --resource-group $ResourceGroup --output none 2>$null
if ($LASTEXITCODE -ne 0) {
    az containerapp env create --name $EnvName --resource-group $ResourceGroup --location $Location --output none
}
az containerapp env storage set --name $EnvName --resource-group $ResourceGroup `
    --storage-name lucky5state `
    --azure-file-account-name $StorageName `
    --azure-file-account-key $StorageKey `
    --azure-file-share-name lucky5-state `
    --access-mode ReadWrite --output none
$EnvId = az containerapp env show --name $EnvName --resource-group $ResourceGroup --query id -o tsv

# ── JWT signing key (stable across deploys) ──────────────────────────────────
Step 'Resolving JWT signing key'
$JwtKey = $null
az containerapp show --name $ServiceName --resource-group $ResourceGroup --output none 2>$null
$appExists = ($LASTEXITCODE -eq 0)
if ($appExists) {
    $JwtKey = az containerapp secret show --name $ServiceName --resource-group $ResourceGroup --secret-name jwt-signing-key --query value -o tsv 2>$null
}
if (-not $JwtKey) {
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $JwtKey = [Convert]::ToBase64String($bytes)
    Write-Host 'Generated a fresh JWT signing key (first deploy).'
}

# ── Container app manifest (declarative upsert) ──────────────────────────────
Step 'Applying container app manifest'
$ManifestPath = Join-Path $env:TEMP 'lucky5-containerapp.yaml'
$Manifest = @"
name: $ServiceName
type: Microsoft.App/containerApps
location: $Location
identity:
  type: UserAssigned
  userAssignedIdentities:
    "${PullIdentityId}": {}
properties:
  environmentId: $EnvId
  configuration:
    ingress:
      external: true
      targetPort: 8080
    registries:
      - server: $AcrName.azurecr.io
        identity: $PullIdentityId
    secrets:
      - name: jwt-signing-key
        value: $JwtKey
  template:
    containers:
      - name: $ServiceName
        image: $Image
        resources:
          cpu: 0.5
          memory: 1.0Gi
        env:
          - name: LUCKY5_STATE_DIR
            value: /mnt/state
          - name: ASPNETCORE_ENVIRONMENT
            value: Production
          - name: JWT__SIGNING_KEY
            secretRef: jwt-signing-key
        volumeMounts:
          - volumeName: lucky5state
            mountPath: /mnt/state
    volumes:
      - name: lucky5state
        storageType: AzureFile
        storageName: lucky5state
    scale:
      minReplicas: 1
      maxReplicas: 1
"@
Set-Content -Path $ManifestPath -Value $Manifest -Encoding utf8

if ($appExists) {
    az containerapp update --name $ServiceName --resource-group $ResourceGroup --yaml $ManifestPath --output none
} else {
    az containerapp create --name $ServiceName --resource-group $ResourceGroup --yaml $ManifestPath --output none
}
Remove-Item $ManifestPath -Force

# ── Verify ───────────────────────────────────────────────────────────────────
Step 'Verifying deployment'
$Fqdn = az containerapp show --name $ServiceName --resource-group $ResourceGroup --query properties.configuration.ingress.fqdn -o tsv
$Url = "https://$Fqdn"
Write-Host "App URL: $Url" -ForegroundColor Green
$healthy = $false
foreach ($i in 1..12) {
    try {
        Invoke-RestMethod -Uri "$Url/health/live" -TimeoutSec 5 | Out-Null
        Write-Host "Health check passed on attempt $i" -ForegroundColor Green
        $healthy = $true
        break
    } catch {
        Write-Host "Waiting for revision to come up (attempt $i)..."
        Start-Sleep -Seconds 5
    }
}
if (-not $healthy) {
    Write-Warning "Health check did not pass within 60s; the revision may still be starting. Logs: az containerapp logs show --name $ServiceName --resource-group $ResourceGroup --follow"
}
Write-Host "`nDone. $Url" -ForegroundColor Cyan
