using Lucky5.Domain.Entities;
using Lucky5.Domain.Game.CleanRoom;
using System.Text.Json;

namespace Lucky5.Infrastructure.Persistence;

/// <summary>
/// In-memory implementation of IPersistentStateCoordinator that captures and restores state
/// from the current IDataStore. This bridges the gap between the existing in-memory store
/// and the new persistence layer.
/// </summary>
public sealed class InMemoryPersistentStateCoordinator : IPersistentStateCoordinator
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Lucky5.Infrastructure.Services.InMemoryDataStore inMemoryStore;

    public InMemoryPersistentStateCoordinator(Lucky5.Infrastructure.Services.InMemoryDataStore inMemoryStore)
    {
        this.inMemoryStore = inMemoryStore;
    }

    public Task<PersistentStateSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PersistentStateSnapshot snapshot;
        lock (inMemoryStore.LedgerSync)
        {
            snapshot = new PersistentStateSnapshot
            {
                SchemaVersion = PersistentStateSnapshot.CurrentSchemaVersion,
                CapturedUtc = DateTimeOffset.UtcNow,
                Machines = inMemoryStore.Machines.Values.OrderBy(machine => machine.Id).Select(CloneForSnapshot).ToArray(),
                Users = inMemoryStore.Users.Values.OrderBy(user => user.Id).Select(CloneForSnapshot).ToArray(),
                Profiles = inMemoryStore.MemberProfiles.Values.OrderBy(profile => profile.UserId).Select(CloneForSnapshot).ToArray(),
                MachineSessions = inMemoryStore.MachineSessions.Values
                    .OrderBy(session => session.UserId)
                    .ThenBy(session => session.MachineId)
                    .ThenBy(session => session.SessionId)
                    .Select(CloneForSnapshot)
                    .ToArray(),
                MachineLedgers = inMemoryStore.MachineLedgers.Values.OrderBy(ledger => ledger.MachineId).Select(CloneForSnapshot).ToArray(),
                ActiveRounds = inMemoryStore.ActiveRounds.Values
                    .OrderBy(round => round.UserId)
                    .ThenBy(round => round.MachineId)
                    .ThenBy(round => round.CreatedUtc)
                    .ThenBy(round => round.RoundId)
                    .Select(CloneForSnapshot)
                    .ToArray(),
                WalletLedgerEntries = inMemoryStore.Ledger
                    .OrderBy(entry => entry.CreatedUtc)
                    .ThenBy(entry => entry.Id)
                    .Select(CloneForSnapshot)
                    .ToArray()
            };
        }

        return Task.FromResult(snapshot);
    }

    public Task RestoreAsync(PersistentStateSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (snapshot.SchemaVersion != PersistentStateSnapshot.CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported persistent state schema version {snapshot.SchemaVersion}.");
        }

        lock (inMemoryStore.LedgerSync)
        {
            // Replace current in-memory authoritative state with the durable snapshot.
            // The legacy collections are kept in sync because several services still read them directly.
            inMemoryStore.Profiles.Clear();
            inMemoryStore.Users.Clear();
            inMemoryStore.MemberProfiles.Clear();
            inMemoryStore.Machines.Clear();
            inMemoryStore.MachineSessions.Clear();
            inMemoryStore.MachineSessionStates.Clear();
            inMemoryStore.MachineLedgers.Clear();
            inMemoryStore.ActiveRounds.Clear();
            inMemoryStore.WalletLedger.Clear();

            while (inMemoryStore.Ledger.TryTake(out _))
            {
            }

            foreach (var machine in snapshot.Machines.OrderBy(machine => machine.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                inMemoryStore.Machines[machine.Id] = machine;
            }

            if (snapshot.Machines.Count == 0)
            {
                foreach (var seededMachine in CreateSeedMachines())
                {
                    inMemoryStore.Machines[seededMachine.Id] = seededMachine;
                }
            }

            foreach (var user in snapshot.Users.OrderBy(user => user.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                inMemoryStore.Users[user.Id] = user;
                inMemoryStore.Profiles[user.Id] = user;
            }

            foreach (var profile in snapshot.Profiles.OrderBy(profile => profile.UserId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                inMemoryStore.MemberProfiles[profile.UserId] = profile;
            }

            foreach (var session in snapshot.MachineSessions
                .OrderBy(session => session.UserId)
                .ThenBy(session => session.MachineId)
                .ThenBy(session => session.SessionId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                NormalizeSession(session);
                inMemoryStore.MachineSessions[session.SessionId] = session;
                inMemoryStore.MachineSessionStates[$"{session.UserId:N}:{session.MachineId}"] = session;
            }

            foreach (var ledger in snapshot.MachineLedgers.OrderBy(ledger => ledger.MachineId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                NormalizeLedger(ledger);
                inMemoryStore.MachineLedgers[ledger.MachineId] = ledger;
            }

            EnsureSeedLedgerFallbacks();
            inMemoryStore.MachinesList = inMemoryStore.Machines.Values.OrderBy(machine => machine.Id).ToList();

            foreach (var round in snapshot.ActiveRounds
                .OrderBy(round => round.UserId)
                .ThenBy(round => round.MachineId)
                .ThenBy(round => round.CreatedUtc)
                .ThenBy(round => round.RoundId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                inMemoryStore.ActiveRounds[round.RoundId] = round;
            }

            foreach (var entry in snapshot.WalletLedgerEntries
                .OrderBy(entry => entry.CreatedUtc)
                .ThenBy(entry => entry.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                inMemoryStore.Ledger.Add(entry);
                inMemoryStore.WalletLedger.Add(entry);
            }
        }

        return Task.CompletedTask;
    }

    private static void NormalizeSession(MachineSessionState session)
    {
        if (session.MachineCredits <= 0m)
        {
            session.MachineCredits = 0m;
            session.TotalCashIn = 0m;
            session.IsMachineClosed = false;
            return;
        }

        session.IsMachineClosed = session.MachineCredits >= EngineConfig.Default.CloseThreshold || session.IsMachineClosed;
    }

    private static void NormalizeLedger(MachineLedgerState ledger)
    {
        if (ledger.TargetRtp <= 0m)
        {
            ledger.TargetRtp = EngineConfig.Default.TargetRtp;
        }

        if (ledger.LastPayoutScale <= 0m)
        {
            ledger.LastPayoutScale = EngineConfig.Default.DefaultPayoutScale;
        }

        ledger.NetSinceLastClose = Math.Max(ledger.CapitalIn - ledger.CapitalOut, 0m);
        ledger.ActiveFourOfAKindSlot = ledger.ActiveFourOfAKindSlot == 1 ? 1 : 0;
    }

    private void EnsureSeedLedgerFallbacks()
    {
        foreach (var machine in inMemoryStore.Machines.Values)
        {
            if (inMemoryStore.MachineLedgers.TryGetValue(machine.Id, out var ledger))
            {
                if (string.IsNullOrWhiteSpace(ledger.MachineSerial)) ledger.MachineSerial = machine.MachineSerial;
                if (string.IsNullOrWhiteSpace(ledger.MachineSerie)) ledger.MachineSerie = machine.MachineSerie;
                if (string.IsNullOrWhiteSpace(ledger.MachineKent)) ledger.MachineKent = machine.MachineKent;
                continue;
            }

            inMemoryStore.MachineLedgers[machine.Id] = new MachineLedgerState
            {
                MachineId = machine.Id,
                MachineSerial = machine.MachineSerial,
                MachineSerie = machine.MachineSerie,
                MachineKent = machine.MachineKent,
                TargetRtp = EngineConfig.Default.TargetRtp,
                LastPayoutScale = EngineConfig.Default.DefaultPayoutScale
            };
        }
    }

    private static T CloneForSnapshot<T>(T value)
    {
        var payload = JsonSerializer.Serialize(value, SnapshotJsonOptions);
        return JsonSerializer.Deserialize<T>(payload, SnapshotJsonOptions)
            ?? throw new InvalidOperationException($"Failed to clone {typeof(T).Name} for persistent snapshot.");
    }

    private static IReadOnlyList<Machine> CreateSeedMachines()
        =>
        [
            new() { Id = 1, GameId = 1, Name = "Beirut 5K", MachineSerial = "105001", MachineSerie = "27", MachineKent = "1", IsOpen = true, MinBet = 5000, MaxBet = 10000 },
            new() { Id = 2, GameId = 1, Name = "Hamra 10K", MachineSerial = "105002", MachineSerie = "27", MachineKent = "2", IsOpen = true, MinBet = 10000, MaxBet = 20000 },
            new() { Id = 3, GameId = 1, Name = "VIP 50K", MachineSerial = "105003", MachineSerie = "27", MachineKent = "3", IsOpen = false, MinBet = 50000, MaxBet = 100000 }
        ];
}
