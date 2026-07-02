# Goal: Fully functional Lucky5 v8 on Google Cloud Run with seamless game-state persistence

## User Request

> build a working version of the game and upload to google cloud run for the project
> /goal continue until a fully functional and seamlessly game state persistence online on google cloud run
> (Follow-up when asked clarifying questions: "The user is not available to respond and will review your work
> later. Work autonomously and make good decisions." for every question — treat all "recommended" options below
> as accepted.)

## Refined Goal

Lucky5 v8 (an ASP.NET Core / SignalR "Lebanese arcade cabinet" poker game, see
[.github/copilot-instructions.md](../../.github/copilot-instructions.md)) must run correctly as a public Google
Cloud Run service and **retain player/game state across container restarts, redeploys, and scale-to-zero events**
without manual intervention ("seamless persistence"). A Cloud Run service named `lucky5-v8` already exists
(see "Current State" below) but is **not yet publicly reachable and has zero durable persistence configured** —
it is running on in-memory-only state. This goal closes that gap: verify the app builds/tests locally, wire up
durable persistence, lock the service to a single instance for correctness, make it publicly playable, and prove
(via an independent restart test) that state survives a full container recycle.

## Current State (discovered — do not re-derive from scratch, verify instead)

- **GCP project**: `euphoric-axon-500505-f9`, active account `gd90@ik90play.com`, billing **enabled**.
  APIs already enabled: `run`, `cloudbuild`, `artifactregistry`, `containerregistry`, `storage`.
- **gcloud CLI**: v574.0.0 installed and authenticated locally. **Docker Desktop is NOT installed locally** —
  all container builds must go through Cloud Build (`gcloud builds submit` / `gcloud run deploy --source`),
  never `docker build` on this machine.
- **Existing Cloud Run service**: `lucky5-v8` in region **europe-west1** (NOT europe-west4, which is the
  gcloud default compute/region config — always pass `--region europe-west1` explicitly, or change the default).
  - URLs: `https://lucky5-v8-b2avt2akqq-ew.a.run.app` and `https://lucky5-v8-49886528256.europe-west1.run.app`
  - Deployed 2026-07-01 via `gcloud run deploy --source` (image lives in Artifact Registry repo
    `cloud-run-source-deploy`, build id `6a4b36f1-1ff4-4479-ad9c-c718ce3b1ad1`).
  - Container port 8080, 1 vCPU / 512Mi, `containerConcurrency: 80`, `timeoutSeconds: 300`.
  - **Problem 1**: IAM policy has zero bindings → anonymous requests get HTTP 403. Not yet publicly playable.
  - **Problem 2**: No env vars set at all on the running revision → persistence falls through to the
    in-process `AddDistributedMemoryCache()` fallback (see below) → **all game state is lost on every
    restart/redeploy/scale-to-zero**.
  - **Problem 3**: `run.googleapis.com/maxScale: '3'` — service can scale to up to 3 concurrent instances.
- **Persistence architecture already implemented in code** (do not reinvent — read before changing):
  `server/src/Lucky5.Infrastructure/Services/ServiceCollectionExtensions.cs` (`AddLucky5Infrastructure`)
  picks a persistence backend in this priority order:
  1. `Persistence:SnapshotDirectory` / `LUCKY5_STATE_DIR` env var set → `FilePersistentStateStore`
     (`server/src/Lucky5.Infrastructure/Persistence/FilePersistentStateStore.cs`). Comment in that file
     explicitly says this is "intended... most notably Cloud Run Gen 2 with a Cloud Storage FUSE volume
     mounted at `LUCKY5_STATE_DIR`". Atomic temp-file+rename writes, fails soft.
  2. Else a Redis connection string configured → `RedisPersistentStateStore` via `IDistributedCache`.
  3. Else falls back to `AddDistributedMemoryCache()` + `RedisPersistentStateStore` (i.e. **not actually
     durable** — this is the fallback currently active in production).
  - `PersistentStateCheckpointService` (BackgroundService) captures a full snapshot of `InMemoryDataStore`
    every 10 seconds (`PersistentStateCheckpointOptions.DefaultCheckpointInterval`) AND on graceful shutdown
    (`StopAsync` override) and saves it via whichever store is registered.
  - `PersistentStateRecoveryService` (IHostedService) loads the latest snapshot and restores it into
    `InMemoryDataStore` on startup.
  - **Important correctness constraint**: the authoritative game state lives in a per-process
    `InMemoryDataStore` singleton. There is no distributed/shared live state — persistence is
    checkpoint/recover only. If 2+ Cloud Run instances run concurrently, each has its own divergent
    in-memory truth and players could see inconsistent balances depending which instance serves them.
    **The service must be pinned to exactly 1 instance** (`--max-instances=1`, and keep `--min-instances=0`
    is fine since `StopAsync` checkpoints on graceful shutdown before scale-to-zero).
- **Dockerfiles that already exist** (Cloud-Run-shaped, port 8080, `ASPNETCORE_URLS=http://+:8080`):
  `server/src/Lucky5.Api/Dockerfile` (build context = **repo root**, references `server/...` paths) and
  `server/Dockerfile.azure` (same shape, Azure-flavored, non-root user + healthcheck). Prior deploy likely
  used Buildpacks or one of these — verify which by inspecting `run.googleapis.com/build-image-uri` history
  or just re-deploying explicitly with `--source server` (buildpacks will detect the .sln) or with an explicit
  Dockerfile reference; pick whichever reliably reproduces a working image and document the choice.
- **appsettings.json** currently ships a hardcoded dev JWT signing key (`"dev-signing-key-change-me"`) and a
  mismatched Firebase `ProjectId` ("lucky5-v7"). Firebase init failure is caught and non-fatal (see
  `FirebaseNotificationService.cs`), so it is not a blocker, but the JWT key **must not** stay as a checked-in
  default in a publicly reachable production deployment.
- **`PORT` handling**: `Program.cs` already reads `PORT`/`WEBSITES_PORT` env vars and binds Kestrel to it —
  Cloud Run's injected `PORT=8080` will just work, no code change needed there.
- **Working tree hygiene**: `git status` shows exactly 3 pre-existing unrelated deletions (from other AI tool
  sessions): `goldenpoker/goldenpoker rom/disassembly_full.asm`, `luck5-v8/.comp/config.json`,
  `luck5-v8/GEMINI.md`. **Do not stage, restore, or commit these.** Only ever `git add` the specific files you
  create/modify for this goal — never `git add -A` / `git add .` / `git commit -am` in this repo.

## Acceptance Criteria

- [ ] Local build passes: `dotnet build server/Lucky5.sln` succeeds with no errors.
- [ ] Local regression tests pass: `dotnet run --project server/tests/Lucky5.Tests/Lucky5.Tests.csproj` reports
      zero failures (this is a hand-rolled runner that prints failures to a `List<string>` — check its exit
      output/summary, not just exit code, since some existing runners swallow failures — verify actual behavior).
- [ ] A GCS bucket exists (or is created) dedicated to Lucky5 durable snapshots, in region `europe-west1`
      (or a region compatible with the Cloud Run service — same region preferred to minimize latency and
      avoid cross-region egress).
- [ ] The Cloud Run service `lucky5-v8` (region `europe-west1`) is redeployed/updated so that:
      - A GCS FUSE volume is mounted (`--add-volume` + `--add-volume-mount`, requires
        `--execution-environment=gen2`) at a path referenced by `LUCKY5_STATE_DIR`.
      - `--max-instances=1` (single-instance pin for correctness).
      - The JWT signing key is sourced from Secret Manager (`--set-secrets`), not the checked-in default.
      - CORS / other required config env vars are set as needed (review `appsettings.json` keys actually
        read via `builder.Configuration[...]` in `Program.cs` / `ServiceCollectionExtensions.cs`).
- [ ] `allUsers` is granted `roles/run.invoker` on the service (or deployed with `--allow-unauthenticated`) so
      the game is reachable without a Google/IAM login (the app has its own player login layer).
- [ ] Health check endpoint (`/health/live` and `/health/ready`) returns HTTP 200 when fetched anonymously from
      the public Cloud Run URL.
- [ ] The game's homepage (`/`) loads the cabinet UI (static `wwwroot` content) with HTTP 200 anonymously.
- [ ] **Persistence proof**: with the service running, cause some game state to change (e.g. via the API/DB
      directly, or by driving a play round), force a new revision deploy (or manually stop/replace the
      instance), and confirm — after the new instance starts — that the previously-written state is still
      present (via an API call or the persisted snapshot file in the bucket). This is the single most
      important criterion; do not mark the goal complete without demonstrating an actual restart survives.
- [ ] Any new deployment steps are captured in a reusable script checked into the repo (e.g.
      `server/deploy/deploy-cloud-run.ps1` or similar) so the deployment is reproducible, not just a one-off
      sequence of terminal commands lost to history.
- [ ] No secrets (JWT signing key, service account keys, etc.) are committed to git in plaintext.

## Scope Boundaries

**In scope:**
- Making the existing `lucky5-v8` Cloud Run service public, durable, and single-instance-correct.
- Any C#/config changes strictly required to support the above (e.g. wiring additional config reads, fixing
  a startup bug blocking public access) — but avoid unrelated refactors.
- A deployment script + brief notes on how to redeploy in the future.
- Creating GCP resources: 1 GCS bucket, 1 Secret Manager secret, IAM binding for public invoke. These are
  low-cost/reversible; proceed without further confirmation.

**Out of scope:**
- Fixing the mismatched Firebase project ID / notifications (non-fatal, not required for this goal).
- Any of the other sibling folders in this monorepo (`ai9/`, `goldenpoker/`, `guestbook-1/`, `hello-world-1/`,
  `luck5-v8/`) — do not touch them.
- Custom domain mapping, CDN, or load testing.
- Migrating to Redis/Cloud SQL/Postgres (the file-snapshot approach was explicitly chosen as the simplest
  correct solution given the single-instance constraint).
- Restoring or touching the 3 pre-existing unrelated deleted files noted above.

## Applicable Project Conventions

**Quality gate commands:**
- `dotnet build server/Lucky5.sln` (must succeed)
- `dotnet run --project server/tests/Lucky5.Tests/Lucky5.Tests.csproj` (regression suite; must report 0 failures)

**Commit convention:**
- No CONTRIBUTING.md/CONSTITUTION.md commit convention found in this repo. Use conventional commits
  (`type(scope): [B] description`, ≤72 chars) as per the Goal skill default.
- **Critical**: only `git add` the exact files you created/modified. Never `git add -A`/`git add .`/
  `git commit -am` — this repo's working tree has unrelated pre-existing deletions from other tools that
  must not be swept into this goal's commits.
- Assisted-by trailer required per Goal skill convention.

**Guidelines:**
- [.github/copilot-instructions.md](../../.github/copilot-instructions.md) — architecture layers, aesthetic
  rules (do not touch client rendering/aesthetics for this goal), zero-allocation backend expectations.
- [docs/DEVELOPMENT_HISTORY_AND_CURRENT_STATE.md](../../docs/DEVELOPMENT_HISTORY_AND_CURRENT_STATE.md) —
  historical bug lessons; note especially: never drive game-state transitions with un-sequenced timers, and
  Double-Up losses must only zero the virtual WIN meter, never the base balance. Not directly relevant to
  this deployment goal but do not regress them if you touch any shared backend code.

**Rules:**
- Treat GCP resource creation (bucket, secret, IAM binding, service redeploy) for this one project/service as
  pre-approved per explicit user instruction ("work autonomously and make good decisions"). Still avoid
  destructive actions (do not delete existing resources; do not run `gcloud projects delete`, do not touch
  other Cloud Run services/projects).
- Prefer Cloud Build over local Docker for all image builds (no Docker locally).
- Region for all new resources: `europe-west1` (match existing service) unless a resource type is unavailable
  there, in which case pick the nearest available region and note why.
