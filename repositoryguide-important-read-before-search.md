Repository Exploration

Game/CleanRoom/ (the 8 requested files):

CoreModels.cs
DeterministicRng.cs
FiveCardDrawEngine.cs
LineageProfiles.cs
Lucky5DoubleUpEngine.cs
MachinePolicy.cs
MachinePolicyResolution.cs
PresentationNoiseGenerator.cs
Game/ (additional):

DeckBuilder.cs
HandTensionAnalyzer.cs
PokerHandEvaluator.cs
RoundNoiseRng.cs
Entities/ (23 files):

AdminAuditRecord.cs
Agent.cs
AppSetting.cs
CabinetCommandRecord.cs
CabinetDevice.cs
CabinetDeviceSession.cs
CabinetEventRecord.cs
CabinetStateCursor.cs
ContactReport.cs
ContactType.cs
DailyBonusConfig.cs
DeviceToken.cs
DistributionMode.cs
GameRound.cs
Machine.cs
MachineLedgerState.cs
MachineSessionState.cs
MemberProfile.cs
Offer.cs
PokerCard.cs
TermsDocument.cs
User.cs
WalletLedgerEntry.cs
Lucky5.Application (52 .cs files)
Dtos/ (30 files): ActiveRoundStateDto.cs, AdminAuditDto.cs, AdminDtos.cs, AgentDto.cs, ApiResponse.cs, AuthTokens.cs, BonusStatusDto.cs, CabinetContractsDto.cs, CabinetDeviceDto.cs, ContactTypeDto.cs, DefaultRulesDto.cs, DealResultDto.cs, DoubleUpResultDto.cs, DrawResultDto.cs, JackpotInfoDto.cs, MachineListingDto.cs, MachineSessionDto.cs, MachineTransparencyDto.cs, MemberProfileDto.cs, OfferDto.cs, PokerCardDto.cs, PlayerLobbyDto.cs, PendingOtpChallengeDto.cs, RewardStatusDto.cs, TermsResponseDto.cs, WalletLedgerEntryDto.cs

Contracts/ (10 files): IAgentService.cs, IAuthService.cs, IAdminAuditService.cs, IAdminService.cs, ICabinetDeviceAuthService.cs, IGameService.cs, IGeneralService.cs, IEntropyGenerator.cs, IMachineStateCache.cs, INotificationService.cs, IRewardService.cs, ITokenService.cs

Requests/ (3 files): Requests.cs, GameRequests.cs, CabinetDeviceRequests.cs, AdminRequests.cs

Interfaces/ (1 file): IDataStore.cs

Lucky5.Api (12 .cs files)
Program.cs
Controllers/AuthController.cs
Controllers/AdminController.cs
Controllers/AgentController.cs
Controllers/CabinetAuthController.cs
Controllers/ConfigController.cs
Controllers/GameController.cs
Controllers/GeneralController.cs
Controllers/NotificationController.cs
Controllers/RewardController.cs
Middleware/BearerTokenMiddleware.cs
Middleware/ApiExceptionMiddleware.cs
Models/HttpContextExtensions.cs
Lucky5.Realtime (4 .cs files)
CarrePokerGameHub.cs
Services/ConnectionRegistry.cs
Services/HeartbeatMonitorService.cs
Services/ServiceCollectionExtensions.cs
Lucky5.Infrastructure (45 .cs files)
Services/ (20 files): GameService.cs (2681 lines), GameServiceSimple.cs, AuthService.cs, AdminService.cs, AgentService.cs, GeneralService.cs, RewardService.cs, CabinetDeviceAuthService.cs, DefaultEntropyGenerator.cs, SimpleTokenService.cs, FirebaseNotificationService.cs, StubNotificationService.cs, InMemoryDataStore.cs, InMemoryMachineStateCache.cs, MachineCacheTtlOptions.cs, IPersistentStateStore.cs, RedisPersistentStateStore.cs, NoOpPersistentStateStore.cs, StateSnapshotModels.cs, StateRecoveryHostedService.cs, StateCheckpointHostedService.cs, ServiceCollectionExtensions.cs

Persistence/ (11 files): FilePersistentStateStore.cs, InMemoryPersistentStateCoordinator.cs, PersistentStateCheckpointOptions.cs, PersistentStateCheckpointService.cs, PersistentStateHealthCheck.cs, PersistentStateRecoveryService.cs, PersistentStateSnapshot.cs, PersistentStoreHealth.cs, IPersistentStateCoordinator.cs, RedisPersistentStateStore.cs, IPersistentStateStore.cs

Data/ (3 files): Lucky5DbContext.cs, Repositories/EfCoreDataStore.cs, Repositories/InMemoryDataStoreAdapter.cs

ArcadePersistence/ (3 files): SystemIntegrityValidator.cs, IMachineAggregateRepository.cs, MachineSessionAggregate.cs

CleanRoom Core Files - Full Contents Already Read
All 8 requested files were fully read and are available in the conversation above. Here are the key structural summaries:

CoreModels.cs (378 lines)
Defines the foundational domain types: RoundState, RoundPhase, RoundActionKind, BigSmallGuess, HandCategory, Lucky5DoubleUpOutcome, RoundAction, CleanRoomCard, FiveCardDrawState, HandEvaluation, PaytableProfile (Lebanese, JacksOrBetter, TwoPairMinimum variants), Lucky5DoubleUpOptions, Lucky5DoubleUpSession, Lucky5DoubleUpResolution, PresentationNoisePlan, and the massive EngineConfig record housing all RTP tuning parameters (~90 tunable knobs for payout scale, envelope/clamp, double-up deck pressure, deck alteration, streaks/pity, soft caps, jackpots).

DeterministicRng.cs (118 lines)
SplitMix64Rng class with NextUInt64(), NextInt(), NextUnit(), Shuffle<T>() using the SplitMix64 algorithm. DeterministicSeed static class with FromString(), Derive() overloads (string parts, ulong parts, purpose+roundIndex) — all use SHA256 hashing to derive 64-bit seeds.

FiveCardDrawEngine.cs (340 lines)
Static engine: BuildStandardDeck(), ShuffleDeck(), DealFiveCardDraw(), Reduce() (ToggleHold/SetHoldMask/Draw state transitions), ParseCards(), EvaluateHand() (full hand evaluation with straight detection including wheel), ResolvePayout(), ComputeAdvisedHolds() (quads, trips, flush, straight, pairs, near-straight), DetectStraight().

LineageProfiles.cs (67 lines)
DoubleUpStyle enum, OperatorSettingProfile, JackpotFeatureProfile, CabinetReferenceProfile (SupportsBonanzaBigSmall, GetJackpotFeature), and CabinetReferences with two static profiles: BonanzaGoldenPoker (Big/Small next-card gamble, 1981) and BonusPoker (jackpot emphasis, 1984).

Lucky5DoubleUpEngine.cs (384 lines)
Full double-up state machine: Start(), CreateSessionFromDeck(), SwitchDealer() (with 5S Lucky5 hit counting, multiplier calculation), SwapChallenger(), ResolveGuess() (Win/SafeFail/Lose/MachineClosed outcomes), IsWinningGuess() (Ace auto-win, Big/Small comparison), RefreshBoardSessionIfNeeded(), BuildContinuationDeck(), ResolveBoardBonus() (Lebanese paytable board bonus).

MachinePolicy.cs (833 lines)
MachinePolicyState (credits in/out, RTP tracking, streaks, cooldown), PayoutTier and PolicyDistributionMode enums, MachinePolicy static class with: ResolveDistributionMode() (Cold/Neutral/Hot based on drift, streaks, Lucky5 drought, cap pressure), ResolvePayoutScale() / ResolveLivePayoutScale() (symmetric controller with equilibrium, correction, warmup bias, pity boost, jackpot/double-up leak dampening), ResolvePolicy() (unified entry point with telemetry), ComputeCooldownLength(), BuildDoubleUpDeck() / BuildDoubleUpPlayDeck() (deck pressure with key card removal and sequence pressure), BuildPressureSequenceDeck(), AlterDeck() (bounded ±2 card alteration, never remove 5S in cold mode, pity 5S addition in hot mode).

MachinePolicyResolution.cs (53 lines)
PolicyEnvelopeMode enum (Recovery/Neutral/Pressure/Cooldown), MachinePolicyTelemetry record, MachinePolicyResolution record with ForTier() method for tier-specific scale lookup.

PresentationNoiseGenerator.cs (17 lines)
Build() method derives deterministic presentation timing (SuspenseMs 250-800, RevealMs 90-330, FlipFrames 8-16, PulseFrames 4-10, DecoySwaps 0-3) from a noise seed + round index via SplitMix64.

Complete Backend Architecture Summary
The backend is a .NET 10 API with the following layered architecture:

CleanRoom = authoritative deterministic game logic (poker hand evaluation, RNG, double-up engine, RTP balancing policy)
Domain.Entities = all entity models (User, MemberProfile, Machine, GameRound, MachineLedgerState, MachineSessionState, CabinetDevice, etc.)
Application.Contracts = service interfaces (IGameService, IAuthService, IAdminService, ITokenService, IEntropyGenerator, etc.)
Application.Dtos = all DTOs/response models (~30 files)
Application.Requests = command/query request records
Application.Interfaces = IDataStore abstraction
Api.Controllers = REST endpoints (Auth, Game, Admin, Agent, Cabinet, Config, General, Notification, Reward)
Api.Middleware = BearerTokenMiddleware, ApiExceptionMiddleware
Realtime = SignalR hub (CarrePokerGameHub) with seat occupancy lock, heartbeat monitor, connection registry
Infrastructure.Services = implementations (GameService at 2681 lines, AuthService, AdminService, InMemoryDataStore, FirebaseNotification, SimpleTokenService, persistence services)
Infrastructure.Data = EF Core DbContext + EfCoreDataStore + InMemoryDataStoreAdapter
Infrastructure.Persistence = durable state checkpoint/recovery (FilePersistentStateStore, RedisPersistentStateStore, PersistentStateCheckpointService, PersistentStateRecoveryService, InMemoryPersistentStateCoordinator)
ArcadePersistence = early-stage aggregate root concept (MachineSessionAggregate, IMachineAggregateRepository, SystemIntegrityValidator)
Key design patterns: in-memory-first with optional durable persistence (file or Redis), cabinet command lock/idempotency with state cursor, SignalR hub for realtime, deterministic poker engine with RTP-controlling payout scale policy, Lucky5 no-lose mode triggered by 5 of Spades, Jacpot system with FullHouse/FourOfAKind A+B/StraightFlush/Kent, double-up deck pressure system.


Here is the complete picture of the test project and solution structure for `server/`.

---

## Solution & Project Structure

### Solution Layout (`server\Lucky5.sln`)

```
Solution "Lucky5" (VS 17, Format 12.00)
├── src/ (solution folder)
│   ├── Lucky5.Api          → src\Lucky5.Api\Lucky5.Api.csproj
│   ├── Lucky5.Domain       → src\Lucky5.Domain\Lucky5.Domain.csproj
│   ├── Lucky5.Application  → src\Lucky5.Application\Lucky5.Application.csproj
│   ├── Lucky5.Infrastructure→ src\Lucky5.Infrastructure\Lucky5.Infrastructure.csproj
│   └── Lucky5.Realtime     → src\Lucky5.Realtime\Lucky5.Realtime.csproj
└── tests/ (solution folder)
    └── Lucky5.Tests        → tests\Lucky5.Tests\Lucky5.Tests.csproj
```

All projects target **`net10.0`** with `Nullable=enable` and `ImplicitUsings=enable`.

---

### Projects

| Project | Type | Key Dependencies |
|---------|------|-----------------|
| `Lucky5.Api` | `Microsoft.NET.Sdk.Web` | Application, Domain, Infrastructure, Realtime, `ASP.NET Core RateLimiting` |
| `Lucky5.Domain` | `Microsoft.NET.Sdk` | (none — pure domain) |
| `Lucky5.Application` | `Microsoft.NET.Sdk` | Domain |
| `Lucky5.Infrastructure` | `Microsoft.NET.Sdk` | Application, Domain, `EF Core`, `Npgsql`, `BCrypt`, `FirebaseAdmin`, `Redis`, `AspNetCore.App` |
| `Lucky5.Realtime` | `Microsoft.NET.Sdk` | Application, Domain, `AspNetCore.App` |
| `Lucky5.Simulation` | `Microsoft.NET.Sdk` (Exe) | Domain (standalone simulation exe) |
| `Lucky5.Tests` | `Microsoft.NET.Sdk` (Exe) | Api, Application, Domain, Infrastructure; `xunit`, `Moq`, `Redis`, `HealthChecks`, `Logging`, `Options` |

---

### Test Files (9 test .cs files + 1 harness)

**`tests\Lucky5.Tests\Program.cs`** — Harness that runs all suites sequentially, collecting failures in a `List<string>` and exiting 1 on any failure:
```csharp
await ReplayTests.RunAsync(failures);
await CabinetCommandIntegrityTests.RunAsync(failures);
await AdminAuditAndCabinetDeviceTests.RunAsync(failures);
await WebCabinetRegressionTests.RunAsync(failures);
await GameServiceRegressionTests.RunAsync(failures);
await CleanRoomEngineTests.RunAsync(failures);
await AuthSecurityRegressionTests.RunAsync(failures);
await FilePersistentStateStoreTests.RunAsync(failures);
await PersistentStateRecoveryTests.RunAsync(failures);
await HubTests.RunAsync(failures);
```

---

#### 1. `ReplayTests.cs` (81 lines) — Deterministic Replay Regression
Tests that two independent `GameService` instances with the same seed produce identical deal/draw results.
- Uses `MockEntropyGenerator` with a fixed seed
- Verifies `DealAsync` and `DrawAsync` produce identical card sequences across two stores

#### 2. `CabinetCommandIntegrityTests.cs` (460 lines) — Cabinet Command Envelope
Tests the authoritative cabinet command system:
- **CashInCommandUsesAuthoritativeDualWalletAndIsIdempotentAsync** — Credit→Wallet debit, idempotency key dedup
- **DuplicateCommandWithDifferentContentIsRejectedAsync** — Conflict on same idempotency key with different payload
- **StaleExpectedStateVersionRejectsBeforeMutationAsync** — Optimistic concurrency via `ExpectedStateVersion`, returns snapshot on stale
- **DoubleUpSwitchCommandsConsumeNextDealerAsync** — Both `double_up_switch` and legacy `swap_double_up_card` commands consume dealer cards deterministically
- **JackpotRankChangeCommandUpdatesIdleFundedSessionAsync** — `jackpot_rank_change` updates Full House jackpot rank through command envelope

#### 3. `AdminAuditAndCabinetDeviceTests.cs` (173 lines) — Admin Audit & Cabinet Device Security
- **CabinetDeviceProvisioningDoesNotLeakSecretsToOperatorViews** — Secret is `l5cabsec_*` prefix, stored only as SHA-256 hash, not leaked in operator/Audit JSON
- **CabinetDeviceRevocationInvalidatesActiveTokensAndBlocksLoginAsync** — Revoked devices can't authenticate, tokens invalidate immediately
- **AdminAuditRecordsAreAppendOnlyAndRedactSensitiveMetadata** — Monotonic sequence, metadata keys `access_token`/`device_secret` redacted to `<redacted>`

#### 4. `WebCabinetRegressionTests.cs` (95 lines) — Static Asset Regression
Reads shipped files from disk and asserts:
- `Program.cs` contains `UseDefaultFiles`, `UseStaticFiles`, `MapFallbackToFile("index.html")`
- `index.html` contains `auth-screen`, `paytable`, `machine-info-block`, `controls`, `btn-deal`, `btn-bet`, `btn-take-score`
- Loads `cabinet-stage-vnext.js`, `cabinet-audio-vnext.js`, `cabinet-orchestrator-vnext.js`, `cabinet-v8-effects.js`
- `game.js` contains auth/login API routes and lobby fallback
- README contains "web-native", `wwwroot`, `dev.ps1` references, and denies Godot

#### 5. `GameServiceRegressionTests.cs` (1099 lines) — Service Layer Regression (largest suite)
Tests `GameService` and `AdminService` through `InMemoryDataStore`:
- **JackpotSnapshotsExposeAuthoritativeMachineIdentity** — Serial/Serie/Kent propagated from machine seed
- **ZeroCreditClosedSessionIsNormalizedOnRead** — Zero-credit closed sessions reopen on read
- **MachineCloseCashOutAllowsContinuingNewSession** — CashOut clears close state, enables new cash-in
- **MachineSessionCashOutEligibilityFollowsRules** — 2x threshold + machine-close eligibility
- **CashOutRejectsBelowThresholdWhenMachineIsNotClosed** — Blocks cash-out below 2x
- **CompletedButUnsettledRoundRemainsRecoverable** — Drawn rounds stay recoverable until payout settled
- **GetActiveRoundRestoresDealtPhase / DrawnState / DoubleUpPhase** — Reconnect hydration preserves round state
- **StartDoubleUpUsesAlreadyAceMultipliedWinAmount** — No double-apply of Ace multiplier
- **ClosedMachineCashOutIsIdempotent** — Second cash-out is no-op
- **PlayerResetAfterClosePreservesClosedSession** — Reset blocked until explicit cash-out
- **PlayerResetBlocksRecoverableRound** — Active round blocks reset
- **AdminResetBlocksRecoverableRounds / AllowsClosedSessionsWithoutActiveRounds** — Admin reset rules
- **PlayerLobbyExposesWalletMachineSessionAndActiveRound** — Lobby DTO completeness
- **AdminDashboardAndDetailsExposeOperationalState** — Dashboard aggregates sessions/rounds/ledger/devices
- **CabinetSnapshotExposesAutoHoldAdvice** — Snapshot contains `ComputeAdvisedHolds` output
- Concurrent slot-mutation test (`SignalingEntropyGenerator` + `ManualResetEventSlim`)

#### 6. `CleanRoomEngineTests.cs` (464 lines) — Pure Game Engine Unit Tests
Tests `FiveCardDrawEngine`, `Lucky5DoubleUpEngine`, `MachinePolicy`, `PresentationNoiseGenerator`, `CabinetReferences`:
- Deterministic shuffle replay, hold/draw replay
- Hand evaluation: Royal Flush, Wheel straight, Full House tiebreaking
- Paytable resolution: JacksOrBetter vs TwoPairMinimum
- Double-Up engine: Ace safety auto-win, Lucky 5 dealer switch, no-lose mode, SafeFail, chained no-lose, repeat Lucky5, credit ceiling machine-close
- Double-Up board bonus evaluation
- Warmup/equilibrium payout scale bounds
- Base payout scaling (jackpot RTP reservation, double-up RTP reservation)
- Adaptive RTP smoothing
- Pressure deck: high-pressure reduces auto-win keys, recovery preserves Lucky 5 + aces
- `MachinePolicy.ShouldOfferDoubleUp` — always available
- Engine config defaults: TargetRtp=0.80, CloseThreshold=40M, payout scale 0.72–2.05

#### 7. `AuthSecurityRegressionTests.cs` (237 lines) — Auth & Security
- **SignupShouldHashPasswordsAndIssueExpiringOtp** — BCrypt hash, 6-digit OTP, 10-min expiry
- **LoginShouldMigrateLegacyPlaintextPasswords** — Plaintext→BCrypt on login
- **VerifyOtpShouldRejectExpiredCodes** — Expired OTP clears challenge
- **ResendOtpShouldRefreshThePendingChallenge** — Resend updates expiry
- **WebCabinetShouldUseIssuedOtpAndRecoverToCredentialLogin** — Cabinet HTML/JS references login/signup/verify-otp, `clearToken()` fallback, README docs
- Extension method `ShouldNotBePlaintext` for assertions

#### 8. `FilePersistentStateStoreTests.cs` (131 lines) — File-Backed Store Smoke
Tests `FilePersistentStateStore` using a temp directory:
- Round-trip save/load preserves schema version, users, ledgers
- Display snapshot round-trip
- Missing snapshot returns null
- Health reports ready for writable directory

#### 9. `PersistentStateRecoveryTests.cs` (133 lines) — State Recovery via Coordinator
Tests `InMemoryPersistentStateCoordinator`:
- Restore replaces seeded state, keeps legacy collections (`MachineSessionStates`, `Ledger`, `WalletLedger`) in sync
- Capture orders users by ID, clones mutable profiles (no in-flight mutation)
- Capture/restore closed session with recoverable DoubleUp round

#### 10. `HubTests.cs` (356 lines) — SignalR Hub Event Tests
Tests `CarrePokerGameHub` via reflection-injected mocks:
- `GetAvailableMachines` → emits `AvailableMachines`
- `JoinMachine` → emits `MachineStatusChanged`
- `LeaveMachine` → emits `MachineStatusChanged`
- `OnConnectedAsync` → emits `UserStatusChanged` ("Active")
- `OnDisconnectedAsync` → emits `UserStatusChanged` ("Reconnecting"), releases seat lock
- `Deal` → emits `BetPlaced` to group, `CardsDealt` to caller
- `Draw` → emits `HoldCardUpdated` to group, `CardRevealed` + `WalletUpdated` to caller
- `DoubleUp` → emits `DoubleUpWin` + `DoubleUpCard` to caller

---

### Program.cs (Entry Point) — `server\src\Lucky5.Api\Program.cs`

The full pipeline:
1. **Rate limiting** — Global IP-based (100/min), plus scoped `auth-strict` (5/min), `auth-moderate` (20/min), `api-general` (60/min)
2. **Kestrel** — listens on `PORT` / `WEBSITES_PORT` env, defaults to 8080
3. **Production config** — `appsettings.Production.json`
4. **Services** — Controllers, SignalR, `AddLucky5Realtime()`, CORS (configurable origins), `AddLucky5Infrastructure()`, health checks
5. **Middleware** — `ForwardedHeaders`, global exception handler (401/404/400/500 via `ApiResponse<T>.Fail`), Bearer token + query-token + cabinet device auth middleware
6. **Static files** — `UseDefaultFiles`, `UseStaticFiles`, `MapFallbackToFile("index.html")`
7. **Endpoints** — `MapControllers()`, `MapHub<CarrePokerGameHub>("/CarrePokerGameHub")`, health checks (`/health/live`, `/health/ready`, `/health/simple`, `/health/fallback`)

---

### SignalR Hub — `CarrePokerGameHub` (`Lucky5.Realtime`)

```csharp
public sealed class CarrePokerGameHub(IGameService gameService, ConnectionRegistry registry) : Hub
```

Key hub methods:
| Method | Purpose |
|--------|---------|
| `OnConnectedAsync()` | Registers connection, emits `UserStatusChanged` |
| `OnDisconnectedAsync()` | Removes connection, releases seat lock, emits `UserStatusChanged` + `MachineStatusChanged` |
| `JoinMachine(machineId)` | Acquires seat-occupancy lock, joins group, broadcasts state |
| `LeaveMachine(machineId)` | Releases lock, leaves group, broadcasts |
| `Deal(machineId, betAmount)` | Emits `BetPlaced` → calls `gameService.DealAsync` → responds `CardsDealt` |
| `Draw(roundId, holdIndexes)` | Emits `HoldCardUpdated` → calls `DrawAsync` → responds `CardRevealed` + `WalletUpdated` |
| `DoubleUp(roundId, guess)` | Calls `GuessDoubleUpAsync` → responds `DoubleUpWin` + `DoubleUpCard` |
| `Heartbeat()` | Touches `ConnectionRegistry` |
| `GetAvailableMachines(gameId)` | Returns machine list |
| `ReconnectSync(machineId, lastStateVersion, lastSequenceNumber)` | Returns replay + snapshot for cabinet reconnect |

Uses a static `ConcurrentDictionary<int, string>` for seat-occupancy locking.

---

### Controllers (9 controllers)

| Controller | Route | Key Endpoints |
|------------|-------|--------------|
| `AuthController` | `api/Auth` | `login`, `signup`, `verify-otp`, `resend-otp`, `GetUserById`, `MemberHistory`, `TransferBalance`, `MoveWinToBalance`, `UpdateCredit`, `Deposit`, `Withdraw`, `logout`, `refresh-token` |
| `GameController` | `api/Game` | `lobby`, `machines`, `rules`, `machine/{id}/session`, `cash-in`, `cash-out`, `active-round`, `cabinet-snapshot`, `cabinet/command`, `cabinet-replay`, `deal`, `draw`, `double-up/start`, `double-up/guess`, `double-up/switch`, `double-up/take-half`, `double-up/cashout`, `machine/{id}/state`, `jackpot/rank`, `machine/{id}/reset` |
| `AdminController` | `api/Admin` | `dashboard`, `audit`, `users`, `users/search`, `users/{id}`, `users/{id}/detail`, `users/credit`, `cabinet-devices`, `cabinet-devices/{id}`, `cabinet-devices` (provision), `cabinet-devices/{id}/revoke`, `machines`, `machines/{id}`, `machines/{id}/detail`, `machines/{id}/reset`, `machines/{id}/door-state`, `users/recharge-bonus` |
| `RewardController` | `api/Reward` | `status`, `claim` |
| `NotificationController` | `api/Notification` | `register-device` |
| `GeneralController` | `api/General` | `app-settings`, `contact-info`, `contact-types`, `contact-report`, `terms` |
| `ConfigController` | `api/Config` | `firebase` (public web config) |
| `CabinetAuthController` | `api/CabinetAuth` | `login`, `me` |
| `AgentController` | `api/Agent` | `GET /` (list), `POST /`, `{id}/load-credit`, `{id}/assign-user/{userId}` |

Auth pattern: Controllers use `HttpContext.RequireUserId()` and `HttpContext.RequireAdminRole()` extension methods (defined in `Lucky5.Api.Models`). Cabinet devices use `HttpContext.RequireCabinetDevice()`. Bearer tokens are validated in the middleware pipeline via `ITokenService`, with fallback to `ICabinetDeviceAuthService`.

Solution & Project Structure
Solution Layout (server\Lucky5.sln)
Solution "Lucky5" (VS 17, Format 12.00)
├── src/ (solution folder)
│   ├── Lucky5.Api          → src\Lucky5.Api\Lucky5.Api.csproj
│   ├── Lucky5.Domain       → src\Lucky5.Domain\Lucky5.Domain.csproj
│   ├── Lucky5.Application  → src\Lucky5.Application\Lucky5.Application.csproj
│   ├── Lucky5.Infrastructure→ src\Lucky5.Infrastructure\Lucky5.Infrastructure.csproj
│   └── Lucky5.Realtime     → src\Lucky5.Realtime\Lucky5.Realtime.csproj
└── tests/ (solution folder)
    └── Lucky5.Tests        → tests\Lucky5.Tests\Lucky5.Tests.csproj
All projects target net10.0 with Nullable=enable and ImplicitUsings=enable.

Projects
Project	Type	Key Dependencies
Lucky5.Api	Microsoft.NET.Sdk.Web	Application, Domain, Infrastructure, Realtime, ASP.NET Core RateLimiting
Lucky5.Domain	Microsoft.NET.Sdk	(none — pure domain)
Lucky5.Application	Microsoft.NET.Sdk	Domain
Lucky5.Infrastructure	Microsoft.NET.Sdk	Application, Domain, EF Core, Npgsql, BCrypt, FirebaseAdmin, Redis, AspNetCore.App
Lucky5.Realtime	Microsoft.NET.Sdk	Application, Domain, AspNetCore.App
Lucky5.Simulation	Microsoft.NET.Sdk (Exe)	Domain (standalone simulation exe)
Lucky5.Tests	Microsoft.NET.Sdk (Exe)	Api, Application, Domain, Infrastructure; xunit, Moq, Redis, HealthChecks, Logging, Options
Test Files (9 test .cs files + 1 harness)
tests\Lucky5.Tests\Program.cs — Harness that runs all suites sequentially, collecting failures in a List<string> and exiting 1 on any failure:

await ReplayTests.RunAsync(failures);
await CabinetCommandIntegrityTests.RunAsync(failures);
await AdminAuditAndCabinetDeviceTests.RunAsync(failures);
await WebCabinetRegressionTests.RunAsync(failures);
await GameServiceRegressionTests.RunAsync(failures);
await CleanRoomEngineTests.RunAsync(failures);
await AuthSecurityRegressionTests.RunAsync(failures);
await FilePersistentStateStoreTests.RunAsync(failures);
await PersistentStateRecoveryTests.RunAsync(failures);
await HubTests.RunAsync(failures);
1. ReplayTests.cs (81 lines) — Deterministic Replay Regression
Tests that two independent GameService instances with the same seed produce identical deal/draw results.

Uses MockEntropyGenerator with a fixed seed
Verifies DealAsync and DrawAsync produce identical card sequences across two stores
2. CabinetCommandIntegrityTests.cs (460 lines) — Cabinet Command Envelope
Tests the authoritative cabinet command system:

CashInCommandUsesAuthoritativeDualWalletAndIsIdempotentAsync — Credit→Wallet debit, idempotency key dedup
DuplicateCommandWithDifferentContentIsRejectedAsync — Conflict on same idempotency key with different payload
StaleExpectedStateVersionRejectsBeforeMutationAsync — Optimistic concurrency via ExpectedStateVersion, returns snapshot on stale
DoubleUpSwitchCommandsConsumeNextDealerAsync — Both double_up_switch and legacy swap_double_up_card commands consume dealer cards deterministically
JackpotRankChangeCommandUpdatesIdleFundedSessionAsync — jackpot_rank_change updates Full House jackpot rank through command envelope
3. AdminAuditAndCabinetDeviceTests.cs (173 lines) — Admin Audit & Cabinet Device Security
CabinetDeviceProvisioningDoesNotLeakSecretsToOperatorViews — Secret is l5cabsec_* prefix, stored only as SHA-256 hash, not leaked in operator/Audit JSON
CabinetDeviceRevocationInvalidatesActiveTokensAndBlocksLoginAsync — Revoked devices can't authenticate, tokens invalidate immediately
AdminAuditRecordsAreAppendOnlyAndRedactSensitiveMetadata — Monotonic sequence, metadata keys access_token/device_secret redacted to <redacted>
4. WebCabinetRegressionTests.cs (95 lines) — Static Asset Regression
Reads shipped files from disk and asserts:

Program.cs contains UseDefaultFiles, UseStaticFiles, MapFallbackToFile("index.html")
index.html contains auth-screen, paytable, machine-info-block, controls, btn-deal, btn-bet, btn-take-score
Loads cabinet-stage-vnext.js, cabinet-audio-vnext.js, cabinet-orchestrator-vnext.js, cabinet-v8-effects.js
game.js contains auth/login API routes and lobby fallback
README contains "web-native", wwwroot, dev.ps1 references, and denies Godot
5. GameServiceRegressionTests.cs (1099 lines) — Service Layer Regression (largest suite)
Tests GameService and AdminService through InMemoryDataStore:

JackpotSnapshotsExposeAuthoritativeMachineIdentity — Serial/Serie/Kent propagated from machine seed
ZeroCreditClosedSessionIsNormalizedOnRead — Zero-credit closed sessions reopen on read
MachineCloseCashOutAllowsContinuingNewSession — CashOut clears close state, enables new cash-in
MachineSessionCashOutEligibilityFollowsRules — 2x threshold + machine-close eligibility
CashOutRejectsBelowThresholdWhenMachineIsNotClosed — Blocks cash-out below 2x
CompletedButUnsettledRoundRemainsRecoverable — Drawn rounds stay recoverable until payout settled
GetActiveRoundRestoresDealtPhase / DrawnState / DoubleUpPhase — Reconnect hydration preserves round state
StartDoubleUpUsesAlreadyAceMultipliedWinAmount — No double-apply of Ace multiplier
ClosedMachineCashOutIsIdempotent — Second cash-out is no-op
PlayerResetAfterClosePreservesClosedSession — Reset blocked until explicit cash-out
PlayerResetBlocksRecoverableRound — Active round blocks reset
AdminResetBlocksRecoverableRounds / AllowsClosedSessionsWithoutActiveRounds — Admin reset rules
PlayerLobbyExposesWalletMachineSessionAndActiveRound — Lobby DTO completeness
AdminDashboardAndDetailsExposeOperationalState — Dashboard aggregates sessions/rounds/ledger/devices
CabinetSnapshotExposesAutoHoldAdvice — Snapshot contains ComputeAdvisedHolds output
Concurrent slot-mutation test (SignalingEntropyGenerator + ManualResetEventSlim)
6. CleanRoomEngineTests.cs (464 lines) — Pure Game Engine Unit Tests
Tests FiveCardDrawEngine, Lucky5DoubleUpEngine, MachinePolicy, PresentationNoiseGenerator, CabinetReferences:

Deterministic shuffle replay, hold/draw replay
Hand evaluation: Royal Flush, Wheel straight, Full House tiebreaking
Paytable resolution: JacksOrBetter vs TwoPairMinimum
Double-Up engine: Ace safety auto-win, Lucky 5 dealer switch, no-lose mode, SafeFail, chained no-lose, repeat Lucky5, credit ceiling machine-close
Double-Up board bonus evaluation
Warmup/equilibrium payout scale bounds
Base payout scaling (jackpot RTP reservation, double-up RTP reservation)
Adaptive RTP smoothing
Pressure deck: high-pressure reduces auto-win keys, recovery preserves Lucky 5 + aces
MachinePolicy.ShouldOfferDoubleUp — always available
Engine config defaults: TargetRtp=0.80, CloseThreshold=40M, payout scale 0.72–2.05
7. AuthSecurityRegressionTests.cs (237 lines) — Auth & Security
SignupShouldHashPasswordsAndIssueExpiringOtp — BCrypt hash, 6-digit OTP, 10-min expiry
LoginShouldMigrateLegacyPlaintextPasswords — Plaintext→BCrypt on login
VerifyOtpShouldRejectExpiredCodes — Expired OTP clears challenge
ResendOtpShouldRefreshThePendingChallenge — Resend updates expiry
WebCabinetShouldUseIssuedOtpAndRecoverToCredentialLogin — Cabinet HTML/JS references login/signup/verify-otp, clearToken() fallback, README docs
Extension method ShouldNotBePlaintext for assertions
8. FilePersistentStateStoreTests.cs (131 lines) — File-Backed Store Smoke
Tests FilePersistentStateStore using a temp directory:

Round-trip save/load preserves schema version, users, ledgers
Display snapshot round-trip
Missing snapshot returns null
Health reports ready for writable directory
9. PersistentStateRecoveryTests.cs (133 lines) — State Recovery via Coordinator
Tests InMemoryPersistentStateCoordinator:

Restore replaces seeded state, keeps legacy collections (MachineSessionStates, Ledger, WalletLedger) in sync
Capture orders users by ID, clones mutable profiles (no in-flight mutation)
Capture/restore closed session with recoverable DoubleUp round
10. HubTests.cs (356 lines) — SignalR Hub Event Tests
Tests CarrePokerGameHub via reflection-injected mocks:

GetAvailableMachines → emits AvailableMachines
JoinMachine → emits MachineStatusChanged
LeaveMachine → emits MachineStatusChanged
OnConnectedAsync → emits UserStatusChanged ("Active")
OnDisconnectedAsync → emits UserStatusChanged ("Reconnecting"), releases seat lock
Deal → emits BetPlaced to group, CardsDealt to caller
Draw → emits HoldCardUpdated to group, CardRevealed + WalletUpdated to caller
DoubleUp → emits DoubleUpWin + DoubleUpCard to caller
Program.cs (Entry Point) — server\src\Lucky5.Api\Program.cs
The full pipeline:

Rate limiting — Global IP-based (100/min), plus scoped auth-strict (5/min), auth-moderate (20/min), api-general (60/min)
Kestrel — listens on PORT / WEBSITES_PORT env, defaults to 8080
Production config — appsettings.Production.json
Services — Controllers, SignalR, AddLucky5Realtime(), CORS (configurable origins), AddLucky5Infrastructure(), health checks
Middleware — ForwardedHeaders, global exception handler (401/404/400/500 via ApiResponse<T>.Fail), Bearer token + query-token + cabinet device auth middleware
Static files — UseDefaultFiles, UseStaticFiles, MapFallbackToFile("index.html")
Endpoints — MapControllers(), MapHub<CarrePokerGameHub>("/CarrePokerGameHub"), health checks (/health/live, /health/ready, /health/simple, /health/fallback)
SignalR Hub — CarrePokerGameHub (Lucky5.Realtime)
public sealed class CarrePokerGameHub(IGameService gameService, ConnectionRegistry registry) : Hub
Key hub methods:

Method	Purpose
OnConnectedAsync()	Registers connection, emits UserStatusChanged
OnDisconnectedAsync()	Removes connection, releases seat lock, emits UserStatusChanged + MachineStatusChanged
JoinMachine(machineId)	Acquires seat-occupancy lock, joins group, broadcasts state
LeaveMachine(machineId)	Releases lock, leaves group, broadcasts
Deal(machineId, betAmount)	Emits BetPlaced → calls gameService.DealAsync → responds CardsDealt
Draw(roundId, holdIndexes)	Emits HoldCardUpdated → calls DrawAsync → responds CardRevealed + WalletUpdated
DoubleUp(roundId, guess)	Calls GuessDoubleUpAsync → responds DoubleUpWin + DoubleUpCard
Heartbeat()	Touches ConnectionRegistry
GetAvailableMachines(gameId)	Returns machine list
ReconnectSync(machineId, lastStateVersion, lastSequenceNumber)	Returns replay + snapshot for cabinet reconnect
Uses a static ConcurrentDictionary<int, string> for seat-occupancy locking.

Controllers (9 controllers)
Controller	Route	Key Endpoints
AuthController	api/Auth	login, signup, verify-otp, resend-otp, GetUserById, MemberHistory, TransferBalance, MoveWinToBalance, UpdateCredit, Deposit, Withdraw, logout, refresh-token
GameController	api/Game	lobby, machines, rules, machine/{id}/session, cash-in, cash-out, active-round, cabinet-snapshot, cabinet/command, cabinet-replay, deal, draw, double-up/start, double-up/guess, double-up/switch, double-up/take-half, double-up/cashout, machine/{id}/state, jackpot/rank, machine/{id}/reset
AdminController	api/Admin	dashboard, audit, users, users/search, users/{id}, users/{id}/detail, users/credit, cabinet-devices, cabinet-devices/{id}, cabinet-devices (provision), cabinet-devices/{id}/revoke, machines, machines/{id}, machines/{id}/detail, machines/{id}/reset, machines/{id}/door-state, users/recharge-bonus
RewardController	api/Reward	status, claim
NotificationController	api/Notification	register-device
GeneralController	api/General	app-settings, contact-info, contact-types, contact-report, terms
ConfigController	api/Config	firebase (public web config)
CabinetAuthController	api/CabinetAuth	login, me
AgentController	api/Agent	GET / (list), POST /, {id}/load-credit, {id}/assign-user/{userId}
Auth pattern: Controllers use HttpContext.RequireUserId() and HttpContext.RequireAdminRole() extension methods (defined in Lucky5.Api.Models). Cabinet devices use HttpContext.RequireCabinetDevice(). Bearer tokens are validated in the middleware pipeline via ITokenService, with fallback to ICabinetDeviceAuthService.

Here is the full breakdown of GameService.cs with line-referenced code and behavior for each of the requested areas.

1. CashInAsync — cash-in flow, validation, limits (lines 131–173)
private const decimal CashInUnit = 200_000m;
private const decimal MaxSessionCashIn = 1_000_000m;
Validation rules:

Amount must be a multiple of CashInUnit (200,000)
Amount must not exceed MaxSessionCashIn (1,000,000)
session.TotalCashIn + amount > MaxSessionCashIn is rejected
session.IsMachineClosed prevents cash-in until cash-out
totalAvailable = profile.WalletBalance + profile.Credit must cover the amount
Credit deduction order:

fromCredit = Math.Min(profile.Credit, amount)
fromBalance = amount - fromCredit
Ledger / persistence:

session.MachineCredits += amount
session.TotalCashIn += amount
session.IsMachineClosed = session.MachineCredits >= MachineCloseCredits
Wallet ledger entry with TransactionType = "MachineCashIn"
2. CashOutAsync — cash-out flow, validation, limits (lines 175–230)
Rules:

Rejects if MachineCredits <= 0
Requires no active uncompleted round; finalizes any unsettled DU session first
Uses CanCashOut(session):
true if IsMachineClosed
OR if TotalCashIn > 0 && MachineCredits >= TotalCashIn * 2
Enforces profile.MinimumOut threshold
Transfer:

profile.WalletBalance += session.MachineCredits
session.MachineCredits = 0
session.TotalCashIn = 0
session.IsMachineClosed = false
TransactionType = "MachineCashOut"
3. DealAsync — the deal flow (lines 232–329)
Validation:

session.IsMachineClosed rejected
request.BetAmount must be in [machine.MinBet, machine.MaxBet]
session.MachineCredits < request.BetAmount * 2 rejected (credits must cover both deal and draw bets)
Paytable / policy:

ulong seed = entropyGenerator.CreateSeed(userId, machine.Id, request.BetAmount, ledger);
PolicyDistributionMode policyMode = MachinePolicy.ResolvePolicy(policyState, seed).DistributionMode;
if (session.CounterplayScore >= 3 && policyMode == PolicyDistributionMode.Cold)
    policyMode = PolicyDistributionMode.Neutral;
Deck handling:

var standardDeck = FiveCardDrawEngine.BuildStandardDeck();
var alteredDeck = MachinePolicy.AlterDeck(standardDeck, policyMode, seed, policyState.ConsecutiveLosses);
var shuffledDeck = FiveCardDrawEngine.ShuffleDeck(seed, "hand", alteredDeck);
var hand = shuffledDeck.Take(5).ToArray();
Bet deduction and round creation:

session.MachineCredits -= request.BetAmount;
TransactionType = "Bet"
Ledger updates:

ledger.CapitalIn += request.BetAmount
ledger.RoundCount++
ledger.RoundsSinceMediumWin++
ledger.RoundsSinceLucky5Hit++
ApplyJackpotContributions(ledger, EngineCfg, active4kSlot)
ledger.NetSinceLastClose = Max(CapitalIn - CapitalOut, 0)
Returns:

return new DealResultDto(round.RoundId, ..., session.MachineCredits, jackpots, advisedHolds, AceCard: false, AceMultiplier: 0);
4. DrawAsync — draw flow, win computation, machine credit update (lines 331–497)
Prechecks:

Round must belong to player, be not completed, phase == Dealt
session.IsMachineClosed rejected
session.MachineCredits < round.BetAmount rejected
Draw:

session.MachineCredits -= round.BetAmount
Ledger: ledger.CapitalIn += round.BetAmount
Hold mask built from request.HoldIndexes
Counterplay:

UpdateCounterplay(session, AssessCounterplay(round.CleanRoomState.Hand, request.HoldIndexes));
Hand evaluation and base payout:

var evaluation = FiveCardDrawEngine.EvaluateHand(state.Hand);
var basePayout = FiveCardDrawEngine.ResolvePayout(evaluation, (int)round.BetAmount);
Ace multiplier:

If Winning hand contains an Ace: aceMultiplier = 2, basePayout *= 2, marks AceCard, AceMultiplier, AceMultiplierFired
Payout scaling from policy:

var policyResolution = MachinePolicy.ResolvePolicy(scaleState, round.RoundEntropySeed);
payoutScale = policyResolution.ForTier(MachinePolicy.ClassifyHand(evaluation.Category));
var payout = basePayout > 0 ? (int)Math.Round(basePayout * payoutScale, AwayFromZero) : 0;
Jackpot wins: checked in order (lines 428–456)

FullHouse (tied rank == ledger.JackpotFullHouseRank AND JackpotFullHouse > payout)
FourOfAKind slots A or B via ActiveFourOfAKindSlotAtDeal
StraightFlush
FiveOfAKind (Kent)
Win channel tracking:

ledger.LastWinChannel = payout > 0 ? (WinChannel.Jackpot or DoubleUp or BaseGame) : WinChannel.None;
Machine close re-evaluated:

session.IsMachineClosed = session.MachineCredits >= MachineCloseCredits;
Returns:

return new DrawResultDto(..., payout, session.MachineCredits, jackpotWon, jackpots, doubleUpAvailable);
5. GuessDoubleUpAsync — DU guess flow (lines 677–785)
Behavior:

var parsedGuess = guess.Equals("big", ...) ? BigSmallGuess.Big : BigSmallGuess.Small;
var resolution = Lucky5DoubleUpEngine.ResolveGuess(round!.DoubleUpSession!, parsedGuess);
Outcome handling:

Win / SafeFail / MachineClosed → FinalizeDoubleUpAsync(round, sessionBank, resolution.CashoutCredits) and machine credits are credited with CashoutCredits
Lose → FinalizeDoubleUpAsync(round, sessionBank, 0) and round.WinAmount = 0
FinalizeDoubleUpAsync does:

session.MachineCredits += cashoutCredits;
session.IsMachineClosed = session.MachineCredits >= MachineCloseCredits;
round.IsPayoutSettled = true;
round.SettledAmount += cashoutCredits;
var ledgerDelta = round.SettledAmount - round.OriginalWinAmount;
if (cashout > 0) ledger.DoubleUpCapitalOut += ledgerDelta;
6. StartDoubleUpAsync — starting DU (lines 518–578)
Validation:

Round must not be settled
Round must be completed with WinAmount > 0
Guards:

if (sessionBank.IsMachineClosed || sessionBank.MachineCredits >= MachineCloseCredits)
    throw new InvalidOperationException("Machine closed - take score and cash out to wallet");
Double-up deck:

var playDeck = MachinePolicy.BuildDoubleUpPlayDeck(...);
var session = Lucky5DoubleUpEngine.CreateSessionFromDeck(
    round.RoundEntropySeed, playDeck, startingAmount, machineCreditBaseline,
    new Lucky5DoubleUpOptions(MaxCreditLimit: Decimal.ToInt32(EngineCfg.CloseThreshold)),
    Decimal.ToInt32(round.BetAmount));
round.DoubleUpSession = session;
round.EnteredDoubleUp = true;
Returns:

return new DoubleUpResultDto(roundId, "Started", session.CurrentAmount, sessionBank.MachineCredits,
    ...);
7. TakeHalfDoubleUpAsync — take-half (lines 840–912)
Name in code: TakeHalfAsync

Logic:

var currentAmount = round.DoubleUpSession != null ? round.DoubleUpSession.CurrentAmount : (int)round.WinAmount;
if (currentAmount <= 1) throw new InvalidOperationException("Amount too small to split");
var half = currentAmount / 2;
var remaining = currentAmount - half;
session.MachineCredits += half;
session.IsMachineClosed = session.MachineCredits >= MachineCloseCredits;
round.TakeHalfUsed = true;
round.SettledAmount += half;
if (round.DoubleUpSession != null)
    round.DoubleUpSession = round.DoubleUpSession with { CurrentAmount = remaining };
Ledger: ledger.CapitalOut += half; ledger.NetSinceLastClose = Max(CapitalIn - CapitalOut, 0)

Ledger entry: TransactionType = "TakeHalf"

8. CashoutDoubleUpAsync — DU cashout (lines 787–838)
Logic:

var cashoutAmount = round.DoubleUpSession != null ? round.DoubleUpSession.CurrentAmount : (int)round.WinAmount;
if (round.IsPayoutSettled) 
    return new DoubleUpResultDto(roundId, session.IsMachineClosed ? "MachineClosed" : "Cashout", 0, ...);

if (round.DoubleUpSession != null && !round.DoubleUpSession.IsTerminal)
    await FinalizeDoubleUpAsync(round, session, cashoutAmount);
else if (round.DoubleUpSession == null)
{
    session.MachineCredits += cashoutAmount;
    round.SettledAmount += cashoutAmount;
    round.IsPayoutSettled = true;
    // ...ledger delta
    // Wallet ledger entry with TransactionType = "Cashout"
}
session.IsMachineClosed = session.MachineCredits >= MachineCloseCredits;
Returns status MachineClosed or Cashout based on close threshold after crediting.

9. SwitchDealerAsync — dealer switch (lines 580–640)
var session = Lucky5DoubleUpEngine.SwitchDealer(round.DoubleUpSession);
var isLucky = session.DealerCard.Rank == 5 && session.DealerCard.Suit == 'S';
if (isLucky)
{
    luckyMult = session.LuckyHitCount == 1 ? session.Options.FirstLuckyMultiplier : session.Options.RepeatLuckyMultiplier;
    ledger.RoundsSinceLucky5Hit = 0;
}
Terminal branch:

if (session.IsTerminal && session.TerminalOutcome == Lucky5DoubleUpOutcome.MachineClosed)
{
    await FinalizeDoubleUpAsync(round, sessionBank, session.CashoutCredits);
    return new DoubleUpResultDto(roundId, "MachineClosed", ...);
}
Returns additional fields: LuckyMultiplier, SwitchesRemaining.

10. Machine-close logic (MachineCloseCredits constant, line 25)
private static readonly decimal MachineCloseCredits = EngineCfg.CloseThreshold;
Checked at:

CashInAsync line 154: session.IsMachineClosed = session.MachineCredits >= MachineCloseCredits;
DrawAsync line 479: same
FinalizeDoubleUpAsync line 1829: same
TakeHalfAsync line 860: same
CashoutDoubleUpAsync line 819: same
NormalizeMachineSession line 2550: auto-escalates session to closed when credits reach threshold
StartDoubleUpAsync line 530: blocks starting DU when machine is closed/at threshold
Cash-out route: CanCashOut (line 2646):

private static bool CanCashOut(MachineSessionState session)
{
    if (session.IsMachineClosed) return true;
    return session.TotalCashIn > 0m && session.MachineCredits >= session.TotalCashIn * 2m;
}
11. How paytable values are computed
Static paytable (line 37–47):

private static readonly Dictionary<string, decimal> Rules = new(StringComparer.OrdinalIgnoreCase)
{
    ["RoyalFlush"] = 1000,
    ["StraightFlush"] = 75,
    ["FourOfAKind"] = 15,
    ["FullHouse"] = 12,
    ["Flush"] = 10,
    ["Straight"] = 8,
    ["ThreeOfAKind"] = 3,
    ["TwoPair"] = 2
};
Computed payout in DrawAsync (lines 384–406):

var evaluation = FiveCardDrawEngine.EvaluateHand(state.Hand);
var basePayout = FiveCardDrawEngine.ResolvePayout(evaluation, (int)round.BetAmount);
// Ace multiplier
if (basePayout > 0 && /* hand has Ace */)
{
    basePayout *= 2;
    round.AceCard = ...;
    round.AceMultiplier = 2;
    round.AceMultiplierFired = true;
}
var payoutScale = policyResolution.ForTier(MachinePolicy.ClassifyHand(evaluation.Category));
var payout = basePayout > 0 ? (int)Math.Round(basePayout * payoutScale, MidpointRounding.AwayFromZero) : 0;
ResolvePayout lives in FiveCardDrawEngine, not in GameService. GameService supplies the policy-scale multiplier.

12. MachineSessionDto and what fields it exposes
Mapping methods (lines 2660–2661, 2593–2628):

private static MachineSessionDto ToMachineSessionDto(MachineSessionState session, decimal walletBalance, bool canCashOut, MachineTransparencyDto? transparency = null)
    => new(session.SessionId, session.MachineId, session.MachineCredits, session.TotalCashIn,
            session.TotalCashIn * 2m, canCashOut, session.IsMachineClosed, walletBalance, transparency);
Fields exposed in MachineSessionDto (by constructor argument order):

#	Field	Source
1	SessionId	MachineSessionState.SessionId
2	MachineId	MachineSessionState.MachineId
3	MachineCredits	MachineSessionState.MachineCredits
4	TotalCashIn	MachineSessionState.TotalCashIn
5	CashOutThreshold	session.TotalCashIn * 2
6	CanCashOut	CanCashOut(session) AND no recoverable round
7	IsMachineClosed	MachineSessionState.IsMachineClosed
8	WalletBalance	profile.WalletBalance
9	Transparency	Optional MachineTransparencyDto with policy telemetry
The async variant (ToMachineSessionDtoAsync, line 2593) also attaches MachineTransparencyDto built from MachinePolicy.ResolvePolicy(ledger, seed: 0).

Additional important notes
FinalizeDoubleUpAsync (lines 1820–1878): central credits-update method for every DU terminus. It:
Adds cashoutCredits to MachineCredits
Re-checks MachineCloseCredits
Marks IsPayoutSettled = true
Computes ledgerDelta = SettledAmount - OriginalWinAmount; adds to ledger.DoubleUpCapitalOut
Sets LastWinChannel based on: no credits → None; above original → DoubleUp; jackpot present → Jackpot else BaseGame
DoubleUpAsync (wrapper) lines 499–516: calls GuessDoubleUpAsync and maps status:
"Win" / "SafeFail" / "MachineClosed" → "Won"
else → "Lost"
Counterplay (lines 2566–2591): when player discards advised-hold cards in strong hands, score increases up to 10; a high score forces Cold → Neutral policy.
Jackpot contributions (lines 2341–2354): only the starred (active4kSlot) 4OAK contributes; FullHouse, StraightFlush, Kent always contribute; each capped by EngineCfg.
Cabinet command mapping (lines 1268–1340): maps client commands like take_score → CashoutDoubleUpAsync, take_half → TakeHalfAsync, double_up_switch → SwitchDealerAsync, swap_double_up_card → SwitchDealerAsync.

Here are the full contents of all requested files.

---

## 1. `MachineSessionDto.cs`

```csharp
namespace Lucky5.Application.Dtos;

public sealed record MachineSessionDto(
    Guid SessionId,
    int MachineId,
    decimal MachineCredits,
    decimal TotalCashIn,
    decimal CashOutThreshold,
    bool CanCashOut,
    bool IsMachineClosed,
    decimal WalletBalance,
    MachineTransparencyDto? Transparency = null);
```

---

## 2. `DealResultDto.cs`

```csharp
namespace Lucky5.Application.Dtos;

public sealed record DealResultDto(
    Guid RoundId,
    IReadOnlyList<PokerCardDto> Cards,
    decimal BetAmount,
    decimal WalletBalanceAfterBet,
    JackpotInfoDto? Jackpots = null,
    int[]? AdvisedHolds = null,
    // CardsDealt snapshot fields from live protocol
    int MemberId = 0,
    HandResultDto? Result = null,
    int KentRounds = 0,
    bool InDoubleUp = false,
    PokerCardDto? DoubleUpCard = null,
    bool ShouldReset = false,
    int DealCount = 0,
    bool FullHouseBonus = false,
    decimal CurrentCarre1 = 0,
    decimal CurrentCarre2 = 0,
    decimal CurrentFullHouse = 0,
    decimal CurrentKent = 0,
    decimal CurrentStraightFlush = 0,
    int CarreIndex = 0,
    decimal CurrentStake = 0,
    bool ShouldDeductStake = true,
    bool OfferOccurred = false,
    decimal OfferAmount = 0,
    decimal Credit = 0,
    int GameId = 0,
    decimal? CurrentBonusAmount = null,
    int FreeGameCount = 0,
    bool WasFreeGameRound = false,
    int? Swap10 = null,
    bool AceCard = false,
    int AceMultiplier = 0
)
{
    public decimal MachineCreditsAfterBet => WalletBalanceAfterBet;
}

public sealed record HandResultDto(
    int HandRank,
    string Description,
    decimal WinAmount,
    bool IsWin,
    bool CanWin
);
```

---

## 3. `DrawResultDto.cs`

```csharp
namespace Lucky5.Application.Dtos;

public sealed record DrawResultDto(Guid RoundId, IReadOnlyList<PokerCardDto> Cards, string HandRank, decimal WinAmount, decimal WalletBalanceAfterRound, decimal JackpotWon = 0, JackpotInfoDto? Jackpots = null, bool DoubleUpAvailable = true)
{
    public decimal MachineCreditsAfterRound => WalletBalanceAfterRound;
}
```

---

## 4. `DoubleUpResultDto.cs`

```csharp
namespace Lucky5.Application.Dtos;

public sealed record DoubleUpResultDto(
    Guid RoundId,
    string Status,
    decimal CurrentAmount,
    decimal WalletBalance,
    PokerCardDto? DealerCard = null,
    PokerCardDto? ChallengerCard = null,
    int SwitchesRemaining = 0,
    bool IsNoLoseActive = false,
    int LuckyMultiplier = 0,
    int CurrentRoundIndex = 0,
    PresentationNoiseDto? Noise = null,
    IReadOnlyList<PokerCardDto>? CardTrail = null,
    string? BoardHandRank = null,
    decimal BoardBonusAmount = 0,
    int SlotIndex = 0,
    bool IsLucky5Active = false,
    // DoubleUpWin snapshot fields from live protocol
    int MemberId = 0,
    bool PickedBig = false,
    decimal NewWinAmount = 0,
    decimal LostAmount = 0,
    int DoubleUpCount = 0,
    bool CanContinue = false,
    bool IsWin = false,
    bool IsEquality = false,
    bool Lucky5 = false,
    bool WinLucky5 = false,
    bool Bonus = false,
    decimal BonusAmount = 0,
    int Opened = 0,
    decimal Credit = 0,
    bool FreeGame = false,
    int FreeGameCount = 0,
    bool BonusCard = false,
    decimal BonusCardAmount = 0,
    decimal? CurrentBonusAmount = null,
    bool AceCard = false,
    int AceMultiplier = 0,
    bool AceMultiplierFired = false,
    int? SwapActivePosition = null,
    int SwapActiveRemaining = 0,
    int GameId = 0
);

public sealed record PresentationNoiseDto(
    int SuspenseMs,
    int RevealMs,
    int FlipFrames,
    int PulseFrames);
```

---

## 5. `JackpotInfoDto.cs`

```csharp
namespace Lucky5.Application.Dtos;

public sealed record JackpotInfoDto(
    decimal FullHouse,
    int FullHouseRank,
    decimal FourOfAKindA,
    decimal FourOfAKindB,
    int ActiveFourOfAKindSlot,
    decimal StraightFlush,
    decimal Kent,
    string MachineSerial,
    string MachineSerie,
    string MachineKent);
```

---

## 6. `CabinetContractsDto.cs` (note: the "Cabinet" DTOs live here, not in separate Cabinet\*.cs files)

This file is large (380 lines). Key records within it:

### `CabinetSnapshotDto`
```csharp
public sealed record CabinetSnapshotDto(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("state_version")]   long StateVersion,
    [property: JsonPropertyName("sequence_number")] long SequenceNumber,
    [property: JsonPropertyName("server_time_utc")] DateTime ServerTimeUtc,
    CabinetSessionStateDto  Session,
    CabinetMachineStateDto  Machine,
    CabinetVariantRefDto    Variant,
    [property: JsonPropertyName("game_state")]       string GameState,
    CabinetCreditsDto       Credits,
    CabinetHandDto          Hand,
    CabinetEvaluationDto    Evaluation,
    [property: JsonPropertyName("double_up")]        CabinetDoubleUpDto DoubleUp,
    CabinetJackpotDto       Jackpot,
    IReadOnlyList<CabinetButtonStateDto> Buttons,
    CabinetPresentationStateDto Presentation,
    CabinetRecoveryStateDto Recovery,
    [property: JsonPropertyName("message_type")]     string MessageType = "cabinet_snapshot");
```

### `CabinetSessionStateDto`
```csharp
public sealed record CabinetSessionStateDto(
    [property: JsonPropertyName("session_id")]            Guid SessionId,
    [property: JsonPropertyName("authenticated_user_id")] string AuthenticatedUserId,
    [property: JsonPropertyName("machine_id")]            int MachineId,
    [property: JsonPropertyName("is_machine_closed")]     bool IsMachineClosed,
    [property: JsonPropertyName("can_cash_out")]          bool CanCashOut,
    string Visibility,
    [property: JsonPropertyName("started_at_utc")]        DateTime StartedAtUtc,
    [property: JsonPropertyName("last_seen_utc")]         DateTime LastSeenUtc);
```

### `CabinetMachineStateDto`
```csharp
public sealed record CabinetMachineStateDto(
    [property: JsonPropertyName("machine_id")]            int MachineId,
    string Name,
    [property: JsonPropertyName("is_open")]               bool IsOpen,
    [property: JsonPropertyName("min_bet")]               string MinBet,
    [property: JsonPropertyName("max_bet")]               string MaxBet,
    [property: JsonPropertyName("machine_serial")]        string MachineSerial,
    [property: JsonPropertyName("machine_serie")]         string MachineSerie,
    [property: JsonPropertyName("machine_kent")]          string MachineKent,
    [property: JsonPropertyName("first_recharge_credit")] string FirstRechargeCredit,
    [property: JsonPropertyName("second_recharge_credit")] string SecondRechargeCredit,
    [property: JsonPropertyName("first_recharge_bonus")]  string FirstRechargeBonus,
    [property: JsonPropertyName("second_recharge_bonus")] string SecondRechargeBonus,
    IReadOnlyDictionary<string, decimal> Paytable);
```

### Other records in this file
- `CabinetVariantRefDto`
- `CabinetHandDto` / `CabinetCardDto`
- `CabinetEvaluationDto`
- `CabinetDoubleUpDto`
- `CabinetCreditsDto`
- `CabinetJackpotDto`
- `CabinetButtonStateDto`
- `CabinetPresentationStateDto` / `CabinetBonusPresentationDto`
- `CabinetRecoveryStateDto`
- `CabinetEventDto`
- `CabinetReplayDto`
- `CabinetCommandDto`
- `CabinetCommandResultDto`
- `CabinetCommandErrorDto`
- `VariantDefinitionDto` / `DoubleUpProfileDto` / `CabinetSkinDto` / `PresentationProfileDto` / `MachinePolicyProfileDto` / `VariantGovernanceDto`

Full file path:  
`C:\Users\Gabi.WIN-CD45QMUUPFF\Documents\GitHub\lucky5-v8\lucky5-v8\server\src\Lucky5.Application\Dtos\CabinetContractsDto.cs`

(There is no separate `CabinetContractsDto.cs` — the file above contains all Cabinet-\* DTO records including `CabinetSnapshotDto`.)

---

## 7. `MachineTransparencyDto.cs`

```csharp
namespace Lucky5.Application.Dtos;

public sealed record MachineTransparencyDto(
    bool IsWarmupActive,
    bool IsPityActive,
    bool IsCrisisActive,
    decimal BaseScale,
    decimal WarmupBias,
    decimal PityBoost,
    decimal JackpotLeakAdjustment,
    decimal DoubleUpLeakAdjustment,
    decimal EffectiveScale,
    string EnvelopeMode,
    int RoundCount,
    int ConsecutiveLosses,
    int RoundsSinceMediumWin,
    decimal ObservedRtp,
    decimal TargetRtp);
```

---

## 8. `MachineSessionState.cs` (Domain Entity)

```csharp
namespace Lucky5.Domain.Entities;

public sealed class MachineSessionState
{
    public Guid SessionId { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public int MachineId { get; init; }
    public decimal MachineCredits { get; set; }
    public decimal TotalCashIn { get; set; }
    public bool IsMachineClosed { get; set; }
    public int CounterplayScore { get; set; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}
```

---

## 9. `MachineLedgerState.cs` (Domain Entity)

```csharp
namespace Lucky5.Domain.Entities;

using Lucky5.Domain.Game.CleanRoom;

public sealed class MachineLedgerState
{
    public int MachineId { get; init; }
    public string MachineSerial { get; set; } = string.Empty;
    public string MachineSerie { get; set; } = string.Empty;
    public string MachineKent { get; set; } = string.Empty;
    public decimal TargetRtp { get; set; } = EngineConfig.Default.TargetRtp;
    public decimal CapitalIn { get; set; }
    public decimal CapitalOut { get; set; }
    public int RoundCount { get; set; }
    public int ColdStreak { get; set; }
    public int HotStreak { get; set; }
    public DistributionMode LastDistributionMode { get; set; } = DistributionMode.Neutral;
    public DateTime LastRoundUtc { get; set; } = DateTime.UtcNow;

    public decimal JackpotFullHouse { get; set; } = EngineConfig.Default.JackpotFullHouseStart;
    public int JackpotFullHouseRank { get; set; } = 14;
    public decimal JackpotFourOfAKindA { get; set; } = EngineConfig.Default.JackpotFourOfAKindStart;
    public decimal JackpotFourOfAKindB { get; set; } = EngineConfig.Default.JackpotFourOfAKindStart;
    public int ActiveFourOfAKindSlot { get; set; }
    public decimal JackpotStraightFlush { get; set; } = EngineConfig.Default.JackpotStraightFlushStart;
    public decimal JackpotKent { get; set; } = EngineConfig.Default.JackpotKentStart;

    public decimal BaseCapitalOut { get; set; }
    public decimal JackpotCapitalOut { get; set; }
    public decimal DoubleUpCapitalOut { get; set; }
    public decimal LastPayoutScale { get; set; } = EngineConfig.Default.DefaultPayoutScale;

    public int ConsecutiveLosses { get; set; }
    public int RoundsSinceMediumWin { get; set; }
    public int CooldownRoundsRemaining { get; set; }

    public decimal NetSinceLastClose { get; set; }
    public int LastCloseRoundNumber { get; set; }
    public WinChannel LastWinChannel { get; set; } = WinChannel.None;
    public int RoundsSinceLucky5Hit { get; set; }

    // Operational fields from live protocol
    public DoorState DoorState { get; set; } = DoorState.Closed;
    public decimal MachineAmount { get; set; }
    public decimal CurrentUserAmount { get; set; }
    public decimal OpenAmount { get; set; }
    public bool CounterStatus { get; set; }
    public bool Active { get; set; } = true;
    public bool Ready { get; set; } = true;
    public decimal OpenedDoubleUpPercentage { get; set; } = 100m;
    public decimal ClosedDoubleUpPercentage { get; set; } = 1m;
    public bool AutoOpenClosePercentage { get; set; }
    public int CarreIndex { get; set; } = 1;
    public int? CurrentMemberId { get; set; }
    public decimal Profit { get; set; }
    public int WinBonusDefaultPokerRulesId { get; set; }
    public decimal WinBonusAmount { get; set; }
    public decimal MaxStraightFlush { get; set; } = 10000000m;
    public decimal MaxCarre1 { get; set; } = 2000000m;
    public decimal MaxCarre2 { get; set; } = 2000000m;
    public decimal MaxFullHouse { get; set; } = 20000000m;
    public decimal MaxKent { get; set; } = 5000000m;
    public decimal MinStraightFlush { get; set; } = 5000000m;
    public decimal MinCarre1 { get; set; }
    public decimal MinCarre2 { get; set; }
    public decimal MinFullHouse { get; set; }
    public decimal MinKent { get; set; }
    public decimal CurrentStraightFlush { get; set; }
    public decimal CurrentCarre1 { get; set; }
    public decimal CurrentCarre2 { get; set; }
    public decimal CurrentFullHouse { get; set; }
    public decimal CurrentKent { get; set; }
    public int KentRounds { get; set; }
    public decimal DoubleUpLimit { get; set; } = 10000m;
    public int FullHouseCardId { get; set; }
    public decimal FirstRechargeCredit { get; set; } = 200000m;
    public decimal FirstRechargeBonus { get; set; } = 15000m;
    public decimal SecondRechargeCredit { get; set; } = 400000m;
    public decimal SecondRechargeBonus { get; set; } = 30000m;

    public decimal ObservedRtp => CapitalIn <= 0m ? TargetRtp : decimal.Round(CapitalOut / CapitalIn, 4);
}

public enum WinChannel
{
    None = 0,
    BaseGame = 1,
    DoubleUp = 2,
    Lucky5 = 3,
    Jackpot = 4
}

public enum DoorState
{
    Closed = 0,
    Open = 1
}
```

---

## CabinetSnapshot\-related files

No files matching `CabinetSnapshot*` exist as standalone files anywhere under `server/src`. The culprit is `CabinetContractsDto.cs` (the DTO found at `server\src\Lucky5.Application\Dtos\CabinetContractsDto.cs`), which defines `CabinetSnapshotDto` along with all its nested support records (`CabinetSessionStateDto`, `CabinetMachineStateDto`, `CabinetVariantRefDto`, `CabinetHandDto`, `CabinetCardDto`, `CabinetEvaluationDto`, `CabinetDoubleUpDto`, `CabinetCreditsDto`, `CabinetJackpotDto`, `CabinetButtonStateDto`, `CabinetPresentationStateDto`, `CabinetBonusPresentationDto`, `CabinetRecoveryStateDto`) and also the contract/command related records (`CabinetEventDto`, `CabinetReplayDto`, `CabinetCommandDto`, `CabinetCommandResultDto`, `CabinetCommandErrorDto`).

Here is the complete contents of **GameController.cs** (245 lines).

---

## File: GameController.cs

**Route base:** `api/[controller]` → `api/Game`
**Constructor:** `GameController(IGameService gameService)`
**User identity:** `UserId` is extracted from `HttpContext.RequireUserId()` (a `Guid`).

---

### Endpoints

| HTTP Method | Route(s) | Method signature | Body | Calls `IGameService` | Returns (success) | Returns (error) |
|---|---|---|---|---|---|---|
| GET | `api/Game/lobby` | `GetLobby(CancellationToken)` | none | `GetLobbyAsync(UserId, ct)` | `200 OK` `ApiResponse<PlayerLobbyDto>` | — |
| GET | `api/Game/machines` <br/>`api/Game/games/machines` | `GetMachines(CancellationToken)` | none | `GetMachinesAsync(ct)` | `200 OK` `ApiResponse<IReadOnlyList<MachineListingDto>>` | — |
| GET | `api/Game/rules` <br/>`api/Game/defaultRules` | `GetRules(CancellationToken)` | none | `GetDefaultRulesAsync(ct)` | `200 OK` `ApiResponse<DefaultRulesDto>` | — |
| GET | `api/Game/machine/{machineId}/session` | `GetMachineSession(int machineId, CancellationToken)` | none | `GetMachineSessionAsync(UserId, machineId, ct)` | `200 OK` `ApiResponse<MachineSessionDto>` | — |
| POST | `api/Game/machine/{machineId}/cash-in` | `CashIn(int machineId, [FromBody] MachineCashRequest request, CancellationToken)` | **`{ "Amount": ... }`** | `CashInAsync(UserId, machineId, request.Amount, ct)` | `200 OK` `ApiResponse<MachineSessionDto>` | `400 Bad Request` on `InvalidOperationException` |
| POST | `api/Game/machine/{machineId}/cash-out` | `CashOut(int machineId, CancellationToken)` | none | `CashOutAsync(UserId, machineId, ct)` | `200 OK` `ApiResponse<MachineSessionDto>` | `400 Bad Request` on `InvalidOperationException` |
| GET | `api/Game/active-round/{machineId}` <br/>`api/Game/machine/{machineId}/active-round` | `GetActiveRound(int machineId, CancellationToken)` | none | `GetActiveRoundAsync(UserId, machineId, ct)` | `200 OK` `ApiResponse<ActiveRoundStateDto?>` | — |
| GET | `api/Game/machine/{machineId}/cabinet-snapshot` | `GetCabinetSnapshot(int machineId, CancellationToken)` | none | `GetCabinetSnapshotAsync(UserId, machineId, ct)` | `200 OK` `ApiResponse<CabinetSnapshotDto>` | — |
| POST | `api/Game/cabinet/command` | `SubmitCabinetCommand([FromBody] CabinetCommandDto command, CancellationToken)` | **entire `CabinetCommandDto`** | `SubmitCabinetCommandAsync(UserId, command, ct)` | <ul><li>`200 OK` for default/ok</li><li>`409 Conflict` when `result.Status == "stale_state"`</li><li>`400 Bad Request` when `invalid`, `rejected`, or `requires_snapshot`</li></ul> |
| POST | `api/Game/machine/{machineId}/cabinet-replay` | `GetCabinetReplay(int machineId, [FromBody] CabinetReconnectRequest request, CancellationToken)` | **`LastStateVersion`, `LastSequenceNumber`** | `GetCabinetReplayAsync(UserId, machineId, request.LastStateVersion, request.LastSequenceNumber, ct)` | `200 OK` `ApiResponse<CabinetReplayDto>` | — |
| POST | `api/Game/deal` <br/>`api/Game/cards/deal` | `Deal([FromBody] DealRequest request, CancellationToken)` | **`DealRequest`** | `DealAsync(UserId, request, ct)` | `200 OK` `ApiResponse<DealResultDto>` | `400 Bad Request` on `InvalidOperationException` |
| POST | `api/Game/draw` <br/>`api/Game/cards/draw` | `Draw([FromBody] DrawRequest request, CancellationToken)` | **`DrawRequest`** | `DrawAsync(UserId, request, ct)` | `200 OK` `ApiResponse<DrawResultDto>` | `400 Bad Request` on `InvalidOperationException` |
| POST | `api/Game/double-up/start` | `StartDoubleUp([FromBody] StartDoubleUpRequest request, CancellationToken)` | **`{ "RoundId": ... }`** | `StartDoubleUpAsync(UserId, request.RoundId, ct)` | `200 OK` `ApiResponse<DoubleUpResultDto>` | `400 Bad Request` on `InvalidOperationException` |
| POST | `api/Game/double-up/guess` | `GuessDoubleUp([FromBody] DoubleUpRequest request, CancellationToken)` | **`{ "RoundId": ..., "Guess": ... }`** | `GuessDoubleUpAsync(UserId, request.RoundId, request.Guess, ct)` | `200 OK` `ApiResponse<DoubleUpResultDto>` | `400 Bad Request` on `InvalidOperationException` |
| POST | `api/Game/double-up/switch` | `SwitchDealer([FromBody] SwitchDealerRequest request, CancellationToken)` | **`{ "RoundId": ... }`** | `SwitchDealerAsync(UserId, request.RoundId, ct)` | `200 OK` `ApiResponse<DoubleUpResultDto>` | `400 Bad Request` on `InvalidOperationException` |
| POST | `api/Game/double-up/take-half` | `TakeHalf([FromBody] TakeHalfRequest request, CancellationToken)` | **`{ "RoundId": ... }`** | `TakeHalfAsync(UserId, request.RoundId, ct)` | `200 OK` `ApiResponse<DoubleUpResultDto>` | `400 Bad Request` on `InvalidOperationException` |
| POST | `api/Game/double-up/cashout` | `CashoutDoubleUp([FromBody] CashoutDoubleUpRequest request, CancellationToken)` | **`{ "RoundId": ... }`** | `CashoutDoubleUpAsync(UserId, request.RoundId, ct)` | `200 OK` `ApiResponse<DoubleUpResultDto>` | `400 Bad Request` on `InvalidOperationException` |
| GET | `api/Game/machine/{id}/state` | `MachineState(int id, CancellationToken)` | none | `GetMachineStateAsync(id, ct)` <br/>*(note: no `UserId` — passes `id` only)* | `200 OK` `object` (untyped) | — |
| POST | `api/Game/jackpot/rank` | `ChangeJackpotRank([FromBody] ChangeJackpotRankRequest request, CancellationToken)` | **`{ "MachineId": ..., "Rank": ... }`** | `ChangeJackpotRankAsync(request.MachineId, request.Rank, ct)` <br/>*(note: no `UserId`)* | `200 OK` `ApiResponse<JackpotInfoDto>` | `400 Bad Request` on `ArgumentException` |
| POST | `api/Game/machine/{machineId}/reset` | `ResetMachine(int machineId, CancellationToken)` | none | `ResetMachineAsync(UserId, machineId, ct)` | `200 OK` `ApiResponse<object>` | `400 Bad Request` on `InvalidOperationException` |

---

## Key observations

1. **UserId is injected via `HttpContext.RequireUserId()`** — it's not a client-provided path/query parameter.
2. **Every successful response wraps data in `ApiResponse<T>.Ok(...)`** with `traceId` set to `HttpContext.TraceIdentifier`.
3. **Lobby / Machines / Rules endpoints** have no body and no error catch — they transcend directly from `IGameService`.
4. **Gameplay endpoints that mutate state** (`CashIn`, `CashOut`, `Deal`, `Draw`, `DoubleUp*`) catch `InvalidOperationException` and return `400 Bad Request` with the exception message — these are client-facing validation/state errors.
5. **`MachineState` and `ChangeJackpotRank`** are the only endpoints that do **NOT** pass `UserId` into `IGameService`, suggesting they are either admin-only or have different auth handling.
6. **`SubmitCabinetCommand`** has differentiated HTTP status codes: `409 Conflict` for `"stale_state"`, `400 Bad Request` for `"invalid"`/`"rejected"`/`"requires_snapshot"`, and `200 OK` otherwise.
7. **Duplicate routes:** `machines`/`games/machines`, `rules`/`defaultRules`, and `deal`/`cards/deal` (and similarly `draw`/`cards/draw`) — these are backward-compatible aliases.