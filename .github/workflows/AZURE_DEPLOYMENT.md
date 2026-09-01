# Lucky5 v8 — Azure Container Apps Deployment

This repository ships with a ready-to-use CI/CD pipeline that builds and
deploys the Lucky5 cabinet API to Azure Container Apps on every push to the
default branch. A one-click local deploy script is also provided.

## What the pipeline does

```
push to main  ──►  Restore .NET deps
                 ►  Build solution (Release)
                 ►  Run regression test suite (Lucky5.Tests)
                 ────────────────────────────  (PRs stop here)
                 ►  Build container image via ACR build task
                 ►  Deploy new revision to Azure Container Apps (1 replica)
                 ►  Mount Azure Files share for in-memory persistence
                 ►  Inject JWT signing key as a Container Apps secret
                 ►  Probe /health/live and report the service URL
```

Pull requests run the **test job only**. The deploy job is gated on a
green test run and only fires on `push` to the default branch
(or via **Run workflow** in the Actions tab).

## One-time Azure setup

The workflow authenticates to Azure via **OIDC** (federated credentials).
You need to do this setup once:

### 1. Create an Azure AD app registration for GitHub

```bash
# Set variables
export SUBSCRIPTION_ID="your-subscription-id"
export RESOURCE_GROUP="lucky5-v8-rg"
export LOCATION="westeurope"
export APP_NAME="lucky5-v8-github"

# Create the Azure AD app
az ad app create --display-name $APP_NAME
APP_ID=$(az ad app list --display-name $APP_NAME --query '[0].appId' -o tsv)

# Create a service principal
az ad sp create --id $APP_ID

# Grant Contributor on the resource group
az role assignment create --assignee $APP_ID --role Contributor \
  --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP

# Add federated credential for GitHub
az ad app federated-credential create \
  --id $APP_ID \
  --parameters '{
    "name": "github-federated",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:YOUR_GITHUB_ORG/lucky5-v8:ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'
```

### 2. Set GitHub repository secrets

Go to **Settings → Secrets and variables → Actions** and add:

| Secret name | Value |
|-------------|-------|
| `AZURE_CLIENT_ID` | The Azure AD app ID (appId) |
| `AZURE_TENANT_ID` | Your Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Your Azure subscription ID |
| `AZURE_RESOURCE_GROUP` | e.g. `lucky5-v8-rg` |
| `AZURE_LOCATION` | e.g. `westeurope` |
| `AZURE_ACR_NAME` | ACR name (e.g. `lucky5v8acr`) |

That's it. The next push to `main` will build and deploy automatically.

## Local one-click deploy

To deploy from your laptop instead of CI:

```powershell
./server/deploy/deploy-azure.ps1
```

With custom options:

```powershell
./server/deploy/deploy-azure.ps1 `
  -ResourceGroup my-rg `
  -Location eastus `
  -AcrName myacr
```

The script creates all infrastructure (resource group, ACR, storage,
Container Apps environment), builds the image remotely via ACR build task,
and deploys the container app — all without requiring local Docker.

## What the deploy preserves

The deploy job reproduces the behavior of the manual script:

- **1 replica max** — required for in-memory correctness
  (`InMemoryDataStore` keeps the canonical game state).
- **Azure Files volume mount** at `/mnt/snapshots` — the API writes
  checkpoints here every 10s and on shutdown, so a redeploy, crash,
  or scale-to-zero doesn't lose player state.
- **JWT secret** — a strong 48-byte random key stored as a Container Apps
  secret, never from `appsettings.json` defaults.
- **Public ingress** — the service is accessible on port 8080.

## Verifying a deployment

After the workflow succeeds, the summary step prints something like:

```
### Deploy summary
- **Service**:   lucky5-v8
- **Resource Group**: lucky5-v8-rg
- **Region**:    westeurope
- **URL**:       https://lucky5-v8.jollysand-abc123.westeurope.azurecontainerapps.io
- **Commit**:    abcdef0123456789
- **Trigger**:   push (refs/heads/main)
```

Then `curl https://lucky5-v8.jollysand-abc123.westeurope.azurecontainerapps.io/health/live`
should return 200. Open the URL in a browser to play.

## Files touched by this pipeline

- **`.github/workflows/deploy-azure.yml`** — the workflow itself.
- **`server/deploy/deploy-azure.ps1`** — the one-click local deploy script.
- **`azure.yaml`** — Azure Developer CLI (azd) configuration.
- **`.azignore`** — keeps the Azure build context small.
