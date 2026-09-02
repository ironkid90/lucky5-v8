# Azure Deployment (Lucky5 v8 → Azure Container Apps)

This repo ships two Azure deploy paths:

| Path | File | Use |
| ---- | ---- | --- |
| CI auto-deploy | `.github/workflows/deploy-azure.yml` | Push to `main` builds + tests + deploys |
| Local one-click | `server/deploy/deploy-azure.ps1` | Manual deploy from your machine |

Both paths are idempotent and converge on the same topology:

- **Azure Container Apps** — 1 replica (pinned; the game state is in-memory by design)
- **Azure Container Registry** — image built in the cloud (`az acr build`), no local Docker needed
- **Azure Files** — mounted at `/mnt/state` and exposed to the app as `LUCKY5_STATE_DIR`, so
  file-based state snapshots survive container restarts
- **User-assigned managed identity** — pulls images from ACR via the `AcrPull` role
- **JWT signing key** — stored as a Container Apps secret (`jwt-signing-key`), generated once
  on first deploy and reused afterwards so player sessions survive redeploys

## One-time setup

1. **Create the deployer app registration** (used by GitHub Actions via OIDC — no stored secrets):

   ```bash
   az ad app create --display-name lucky5-v8-github-deployer
   APP_ID=$(az ad app list --display-name lucky5-v8-github-deployer --query "[0].appId" -o tsv)
   az ad sp create --id $APP_ID
   ```

2. **Create the resource group and grant Contributor:**

   ```bash
   az group create --name lucky5-v8-rg --location westeurope
   az role assignment create --assignee $APP_ID --role Contributor \
     --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/lucky5-v8-rg
   ```

3. **Add a federated credential for GitHub OIDC** (replace `OWNER/REPO`):

   ```bash
   az ad app federated-credential create --id $APP_ID --parameters '{
     "name": "github-main",
     "issuer": "https://token.actions.githubusercontent.com",
     "subject": "repo:OWNER/REPO:ref:refs/heads/main",
     "audiences": ["api://AzureADTokenExchange"] }'
   ```

4. **Set the GitHub repository secrets** (Settings → Secrets and variables → Actions):

   | Secret | Example |
   | ------ | ------- |
   | `AZURE_CLIENT_ID` | app ID from step 1 |
   | `AZURE_TENANT_ID` | Azure AD tenant |
   | `AZURE_SUBSCRIPTION_ID` | subscription GUID |
   | `AZURE_RESOURCE_GROUP` | `lucky5-v8-rg` |
   | `AZURE_LOCATION` | `westeurope` |
   | `AZURE_ACR_NAME` | `lucky5v8acr` (globally unique, lowercase alphanumeric) |

5. Push to `main` — the workflow tests, builds, and deploys automatically.

## Local deploy

```powershell
# Uses the same env-var names as the CI secrets for defaults
$env:AZURE_SUBSCRIPTION_ID = "..."
$env:AZURE_ACR_NAME        = "lucky5v8acr"
./server/deploy/deploy-azure.ps1
```

The script prints the app URL and waits for `/health/live` to respond.

## Notes

- The container listens on port **8080** (see `Dockerfile`).
- `azure.yaml` marks this as an azd-compatible `dotnet` containerapp service if you
  prefer `azd up`; the two CI/manual paths above do not require azd.
- Scale stays at 1 replica intentionally: balance, machine sessions, and jackpots are
  managed in-process. Raising replicas without an external state store will corrupt
  gameplay accounting.
