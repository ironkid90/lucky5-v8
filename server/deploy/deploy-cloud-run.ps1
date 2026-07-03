#requires -Version 7.0
<#
.SYNOPSIS
    Reproducible build + deploy for the Lucky5 v8 API to Google Cloud Run with
    durable, seamless game-state persistence.

.DESCRIPTION
    Lucky5's authoritative game state lives in a single per-process in-memory
    store (Lucky5.Infrastructure.Services.InMemoryDataStore). Persistence is
    checkpoint/recover, not distributed - see
    server/src/Lucky5.Infrastructure/Services/ServiceCollectionExtensions.cs
    and server/src/Lucky5.Infrastructure/Persistence/*.
    This script deploys the service so that:
      - It is pinned to exactly 1 Cloud Run instance (correctness requirement -
        two concurrent instances would each have their own divergent
        in-memory truth).
      - A GCS bucket is mounted via the native Cloud Run Gen2 GCS FUSE volume
        feature at LUCKY5_STATE_DIR, so the 10-second periodic checkpoint and
        the on-shutdown final checkpoint both write to durable storage that
        survives redeploys, crashes, and scale-to-zero.
      - The JWT signing key comes from Secret Manager, never the checked-in
        appsettings.json dev default.
    No Docker is required locally - the container image is built remotely via
    Cloud Build.

.PARAMETER MakePublic
    Attempt to grant `allUsers` the Cloud Run invoker role so the game is
    playable without a Google/IAM login (the app has its own player login).
    NOTE: some GCP organizations enforce the `iam.allowedPolicyMemberDomains`
    org policy, which blocks `allUsers`/`allAuthenticatedUsers` bindings with
    "FAILED_PRECONDITION: ... do not belong to a permitted customer". If that
    happens and you have org-policy-admin rights on the project, this script
    will attempt a project-level policy override (allValues: ALLOW) before
    retrying. If you do NOT have that permission, re-run with -MakePublic:$false
    and grant access to specific principals instead, or ask an org admin to
    loosen the policy for this project.

.EXAMPLE
    ./deploy-cloud-run.ps1
    Builds and deploys with all defaults (matches the current production setup).
#>
[CmdletBinding()]
param(
    [string]$ProjectId = 'euphoric-axon-500505-f9',
    [string]$Region = 'europe-west1',
    [string]$Service = 'lucky5-v8',
    [string]$Bucket = 'lucky5-v8-snapshots-ew1',
    [string]$Secret = 'lucky5-jwt-signing-key',
    [string]$MountPath = '/mnt/snapshots',
    [string]$ImageTag = "europe-west1-docker.pkg.dev/$ProjectId/cloud-run-source-deploy/lucky5-v8:latest",
    [bool]$MakePublic = $true
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')

function Step($message) {
    Write-Host "==> $message" -ForegroundColor Cyan
}

Step "Ensuring required Google Cloud APIs are enabled on $ProjectId"
gcloud services enable run.googleapis.com cloudbuild.googleapis.com artifactregistry.googleapis.com `
    storage.googleapis.com secretmanager.googleapis.com --project $ProjectId | Out-Null

Step "Ensuring durable-snapshot bucket gs://$Bucket exists in $Region"
$bucketExists = gcloud storage buckets describe "gs://$Bucket" --project $ProjectId 2>$null
if (-not $bucketExists) {
    gcloud storage buckets create "gs://$Bucket" --project $ProjectId --location $Region --uniform-bucket-level-access
}
else {
    Write-Host "    bucket already exists, skipping create"
}

Step "Ensuring JWT signing key secret '$Secret' exists (never overwritten if present)"
$secretExists = gcloud secrets describe $Secret --project $ProjectId 2>$null
if (-not $secretExists) {
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $signingKey = [Convert]::ToBase64String($bytes)
    gcloud secrets create $Secret --project $ProjectId --replication-policy=automatic
    $signingKey | gcloud secrets versions add $Secret --project $ProjectId --data-file=-
    Remove-Variable signingKey, bytes
}
else {
    Write-Host "    secret already exists, leaving its value untouched"
}

Step "Resolving the Cloud Run runtime service account"
$projectNumber = gcloud projects describe $ProjectId --format='value(projectNumber)'
$runtimeServiceAccount = "$projectNumber-compute@developer.gserviceaccount.com"

Step "Granting $runtimeServiceAccount access to read '$Secret'"
gcloud secrets add-iam-policy-binding $Secret --project $ProjectId `
    --member "serviceAccount:$runtimeServiceAccount" --role roles/secretmanager.secretAccessor | Out-Null

Step "Building the container image via Cloud Build (no local Docker required)"
Push-Location $repoRoot
try {
    gcloud builds submit --config cloudbuild.yaml --project $ProjectId --substitutions "_IMAGE=$ImageTag" .
}
finally {
    Pop-Location
}

Step "Deploying $Service to Cloud Run ($Region), pinned to 1 instance with the GCS-mounted state dir"
gcloud run deploy $Service `
    --project $ProjectId `
    --region $Region `
    --image $ImageTag `
    --platform managed `
    --execution-environment gen2 `
    --min-instances 0 `
    --max-instances 1 `
    --concurrency 80 `
    --cpu 1 --memory 512Mi `
    --add-volume "name=snapshots,type=cloud-storage,bucket=$Bucket" `
    --add-volume-mount "volume=snapshots,mount-path=$MountPath" `
    --set-env-vars "LUCKY5_STATE_DIR=$MountPath,ASPNETCORE_ENVIRONMENT=Production" `
    --set-secrets "JWT__SIGNING_KEY=$($Secret):latest" `
    --quiet

if ($MakePublic) {
    Step "Granting public (unauthenticated) invoke access"
    try {
        gcloud run services add-iam-policy-binding $Service --project $ProjectId --region $Region `
            --member allUsers --role roles/run.invoker 2>$policyError | Out-Null
    }
    catch {
        Write-Host "    Initial attempt failed - checking for a domain-restricted-sharing org policy blocker..." -ForegroundColor Yellow
        $policyFile = Join-Path ([System.IO.Path]::GetTempPath()) 'lucky5-allow-all-domains-policy.yaml'
        @"
constraint: constraints/iam.allowedPolicyMemberDomains
listPolicy:
  allValues: ALLOW
"@ | Set-Content -Path $policyFile -Encoding utf8
        gcloud resource-manager org-policies set-policy $policyFile --project $ProjectId
        Remove-Item $policyFile
        gcloud run services add-iam-policy-binding $Service --project $ProjectId --region $Region `
            --member allUsers --role roles/run.invoker
    }
}

$url = gcloud run services describe $Service --project $ProjectId --region $Region --format='value(status.url)'
Step "Deployed. Service URL: $url"
Write-Host "Verify with: curl $url/health/live" -ForegroundColor Green
