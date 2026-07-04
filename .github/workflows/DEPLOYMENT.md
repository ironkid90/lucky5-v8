# Lucky5 v8 — GitHub Actions → Cloud Run Deployment

This repository ships with a ready-to-use CI/CD pipeline that builds and
deploys the Lucky5 cabinet API to Google Cloud Run on every push to the
default branch.

## What the pipeline does

```
push to main  ──►  Restore .NET deps
                 ►  Build solution (Release)
                 ►  Run regression test suite (Lucky5.Tests)
                 ────────────────────────────  (PRs stop here)
                 ►  Submit container build to Cloud Build
                 ►  Tag the same image as both :COMMIT_SHA and :latest
                 ►  Deploy new revision to Cloud Run (pinned to 1 instance)
                 ►  Mount the GCS snapshot bucket for in-memory persistence
                 ►  Inject JWT signing key from Secret Manager
                 ►  Probe /health/live and report the service URL
```

Pull requests run the **test job only**. The deploy job is gated on a
green test run and only fires on `push` to the default branch
(or via **Run workflow** in the Actions tab).

## One-time GCP setup

The workflow authenticates to Google Cloud via **Workload Identity
Federation** — no long-lived service-account keys ever touch GitHub.
You need to do this setup once, from a local machine with `gcloud`
authenticated as a project owner:

```bash
export PROJECT_ID=euphoric-axon-500505-f9
export REGION=europe-west1
export REPO=YOUR_GITHUB_ORG/lucky5-v8           # e.g. gabid8r/lucky5-v8

# 1. Create the deployer service account
gcloud iam service-accounts create github-deployer \
  --project=$PROJECT_ID --display-name="GitHub Actions deployer"

# 2. Grant the roles it needs
for ROLE in run.admin cloudbuild.builds.editor artifactregistry.writer \
            storage.admin secretmanager.secretAccessor iam.serviceAccountUser; do
  gcloud projects add-iam-policy-binding $PROJECT_ID \
    --member="serviceAccount:github-deployer@$PROJECT_ID.iam.gserviceaccount.com" \
    --role="roles/$ROLE"
done

# 3. Create a Workload Identity Pool
gcloud iam workload-identity-pools create github-pool \
  --project=$PROJECT_ID --location=global \
  --display-name="GitHub Actions pool"

# 4. Create an OIDC provider for GitHub inside that pool
gcloud iam workload-identity-pools providers create-oidc github-provider \
  --project=$PROJECT_ID --location=global \
  --workload-identity-pool=github-pool \
  --display-name="GitHub provider" \
  --issuer-uri="https://token.actions.githubusercontent.com" \
  --attribute-mapping="google.subject=assertion.sub,attribute.actor=assertion.actor,attribute.repository=assertion.repository,attribute.repository_owner=assertion.repository_owner" \
  --attribute-condition="assertion.repository_owner == 'YOUR_GITHUB_ORG'"

# 5. Allow the pool to impersonate the deployer SA (scoped to one repo)
PROJECT_NUMBER=$(gcloud projects describe $PROJECT_ID --format='value(projectNumber)')
gcloud iam service-accounts add-iam-policy-binding \
  github-deployer@$PROJECT_ID.iam.gserviceaccount.com \
  --project=$PROJECT_ID \
  --role="roles/iam.workloadIdentityUser" \
  --member="principalSet://iam.googleapis.com/projects/${PROJECT_NUMBER}/locations/global/workloadIdentityPools/github-pool/attribute.repository/${REPO}"
```

## One-time GitHub setup

Go to **Settings → Secrets and variables → Actions** on the repository
and add these seven repository secrets:

| Secret name | Example value |
|-------------|---------------|
| `GCP_PROJECT_ID` | `euphoric-axon-500505-f9` |
| `GCP_REGION` | `europe-west1` |
| `GCP_SERVICE_NAME` | `lucky5-v8` |
| `GCP_SNAPSHOT_BUCKET` | `lucky5-v8-snapshots-ew1` |
| `GCP_JWT_SECRET_NAME` | `lucky5-jwt-signing-key` |
| `GCP_WORKLOAD_IDENTITY_PROVIDER` | `projects/123456789012/locations/global/workloadIdentityPools/github-pool/providers/github-provider` |
| `GCP_SERVICE_ACCOUNT` | `github-deployer@euphoric-axon-500505-f9.iam.gserviceaccount.com` |

That's it. The next push to `main` will:

1. Build the project on a fresh Ubuntu runner.
2. Run `Lucky5.Tests` (the same suite you run locally with
   `dotnet run --project server/tests/Lucky5.Tests/Lucky5.Tests.csproj`).
3. Submit a `cloudbuild.yaml` build to Cloud Build — using the existing
   `server/src/Lucky5.Api/Dockerfile` and your custom `.gcloudignore`
   to keep uploads small.
4. Push the resulting image to Artifact Registry at
   `europe-west1-docker.pkg.dev/$PROJECT_ID/cloud-run-source-deploy/lucky5-v8`
   with both `:COMMIT_SHA` and `:latest` tags.
5. Deploy a new Cloud Run revision pinned to **1 instance** with the
   GCS-mounted snapshot directory and the JWT secret injected as an
   env var.
6. Probe `/health/live` for up to 60 seconds and emit a summary with
   the public service URL.

## What the deploy preserves

The deploy job reproduces **every** flag from the manual
`server/deploy/deploy-cloud-run.ps1` script, so behavior is identical
whether you trigger it from your laptop or from a CI run:

- **1 instance max** — required for in-memory correctness
  (`InMemoryDataStore` keeps the canonical game state).
- **GCS FUSE volume mount** at `/mnt/snapshots` — the API writes
  checkpoints here every 10s and on shutdown, so a redeploy, crash,
  or scale-to-zero doesn't lose player state.
- **JWT secret from Secret Manager** — never from
  `appsettings.json` defaults.
- **Public invoker policy** — granted as a best-effort step; if your
  organization enforces `iam.allowedPolicyMemberDomains`, the deploy
  will succeed but the warning tells you to grant access manually.

## Local reproduction

The CI workflow mirrors the manual deploy script one-for-one. If you
want to deploy from your laptop instead, run:

```powershell
./server/deploy/deploy-cloud-run.ps1 \
  -ProjectId euphoric-axon-500505-f9 \
  -Region europe-west1
```

That script has the same pre-flight checks (API enablement, bucket
creation, secret creation with random key bootstrap, SA IAM grants)
that the GitHub Actions workflow now performs in `gcloud run services
add-iam-policy-binding` style.

## Verifying a deployment

After the workflow succeeds, the summary step prints something like:

```
### Deploy summary
- **Service**:   lucky5-v8
- **Region**:    europe-west1
- **Image**:     europe-west1-docker.pkg.dev/.../lucky5-v8:abcdef0
- **URL**:       https://lucky5-v8-xyz-ew.a.run.app
- **Commit**:    abcdef0123456789
- **Trigger**:   push (refs/heads/main)
```

Then `curl https://lucky5-v8-xyz-ew.a.run.app/health/live` should
return 200. Open the URL in a browser to play.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| `failed to impersonate` | WIF pool not bound to this repo | Re-run step 5 with the correct `attribute.repository` value |
| `403 missing iam.serviceAccountUser` | Deployer SA can't deploy | Add `roles/iam.serviceAccountUser` on the runtime SA |
| Health check times out | App cold-start > 60s | Increase the `for` loop in the `Print service URL` step |
| Public binding blocked | Org policy `iam.allowedPolicyMemberDomains` | Loosen the policy or grant invoker role to a specific principal |
| Build fails on `dotnet restore` | NuGet feed misconfigured | Check that `server/Lucky5.sln` is at the path the workflow expects |

## Files touched by this pipeline

- **`.github/workflows/deploy-cloud-run.yml`** — the workflow itself.
- **`cloudbuild.yaml`** — extended to tag both `:COMMIT_SHA` and
  `:latest` from one build, so deployments are reproducible per commit.

No code in `server/src/` had to change for the pipeline to work.
