namespace Lucky5.Infrastructure.Services;

using System.Text.Json;
using Lucky5.Domain.Entities;

internal sealed class PersistentStateSnapshot
{
    public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;
    public List<User> Profiles { get; set; } = [];
    public List<MemberProfile> MemberProfiles { get; set; } = [];
    public List<Machine> Machines { get; set; } = [];
    public List<MachineSessionState> MachineSessions { get; set; } = [];
    public List<MachineLedgerState> MachineLedgers { get; set; } = [];
    public List<GameRound> ActiveRounds { get; set; } = [];
    public List<WalletLedgerEntry> WalletLedgerEntries { get; set; } = [];
}

internal static class StateSnapshotCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(InMemoryDataStore store)
    {
        var snapshot = Capture(store);
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public static bool TryHydrate(InMemoryDataStore store, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var snapshot = JsonSerializer.Deserialize<PersistentStateSnapshot>(payload, JsonOptions);
        if (snapshot is null)
        {
            return false;
        }

        // Keep the existing pre-seeded defaults if snapshot is empty.
        if (snapshot.Machines.Count == 0 && snapshot.Profiles.Count == 0)
        {
            return false;
        }

        store.Profiles.Clear();
        store.Users.Clear();
        foreach (var profile in snapshot.Profiles)
        {
            store.Profiles[profile.Id] = profile;
            store.Users[profile.Id] = profile;
        }

        store.MemberProfiles.Clear();
        foreach (var profile in snapshot.MemberProfiles)
        {
            store.MemberProfiles[profile.UserId] = profile;
        }

        store.Machines.Clear();
        foreach (var machine in snapshot.Machines)
        {
            store.Machines[machine.Id] = machine;
        }
        store.MachinesList = store.Machines.Values.OrderBy(machine => machine.Id).ToList();

        store.MachineSessions.Clear();
        store.MachineSessionStates.Clear();
        foreach (var session in snapshot.MachineSessions)
        {
            store.MachineSessions[session.SessionId] = session;
            store.MachineSessionStates[$"{session.UserId:N}:{session.MachineId}"] = session;
        }

        store.MachineLedgers.Clear();
        foreach (var ledger in snapshot.MachineLedgers)
        {
            store.MachineLedgers[ledger.MachineId] = ledger;
        }

        store.ActiveRounds.Clear();
        foreach (var round in snapshot.ActiveRounds)
        {
            store.ActiveRounds[round.RoundId] = round;
        }

        while (store.Ledger.TryTake(out _))
        {
        }
        store.WalletLedger.Clear();
        foreach (var entry in snapshot.WalletLedgerEntries)
        {
            store.Ledger.Add(entry);
            store.WalletLedger.Add(entry);
        }

        return true;
    }

    private static PersistentStateSnapshot Capture(InMemoryDataStore store)
    {
        // We capture a best-effort snapshot under the existing synchronization lock.
        lock (store.LedgerSync)
        {
            return new PersistentStateSnapshot
            {
                CapturedUtc = DateTime.UtcNow,
                Profiles = store.Profiles.Values.ToList(),
                MemberProfiles = store.MemberProfiles.Values.ToList(),
                Machines = store.Machines.Values.ToList(),
                MachineSessions = store.MachineSessions.Values.ToList(),
                MachineLedgers = store.MachineLedgers.Values.ToList(),
                ActiveRounds = store.ActiveRounds.Values.ToList(),
                WalletLedgerEntries = store.WalletLedger.ToList()
            };
        }
    }
}