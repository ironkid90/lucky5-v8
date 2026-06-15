namespace Lucky5.Infrastructure.Services;

using Lucky5.Domain.Entities;
using Lucky5.Domain.Game.CleanRoom;
using System.Collections.Concurrent;

public sealed class InMemoryDataStore
{
    public InMemoryDataStore()
    {
        PreSeedData();
    }

    public readonly ConcurrentDictionary<Guid, User> Profiles = new();
    public readonly ConcurrentDictionary<int, Machine> Machines = new();
    public readonly ConcurrentDictionary<Guid, MachineSessionState> MachineSessions = new();
    public readonly ConcurrentDictionary<int, MachineLedgerState> MachineLedgers = new();
    public readonly ConcurrentDictionary<Guid, GameRound> ActiveRounds = new();
    public readonly ConcurrentDictionary<string, CabinetCommandRecord> CabinetCommandRecords = new(StringComparer.OrdinalIgnoreCase);
    public readonly ConcurrentDictionary<string, CabinetStateCursor> CabinetStateCursors = new(StringComparer.OrdinalIgnoreCase);
    public readonly ConcurrentQueue<CabinetEventRecord> CabinetEventRecords = new();
    public readonly ConcurrentDictionary<Guid, CabinetDevice> CabinetDevices = new();
    public readonly ConcurrentDictionary<Guid, CabinetDeviceSession> CabinetDeviceSessions = new();
    public readonly ConcurrentBag<WalletLedgerEntry> Ledger = new();
    public readonly ConcurrentQueue<AdminAuditRecord> AdminAuditRecords = new();
    public readonly object LedgerSync = new();
    public readonly object AdminAuditSync = new();
    public long AdminAuditSequence;

    // Legacy properties for compatibility
    public ConcurrentDictionary<Guid, User> Users { get; } = new();
    public ConcurrentDictionary<Guid, MemberProfile> MemberProfiles { get; } = new();
    public List<WalletLedgerEntry> WalletLedger { get; } = [];
    public ConcurrentDictionary<string, MachineSessionState> MachineSessionStates { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Machine> MachinesList { get; set; } = [];
    public List<Offer> Offers { get; } =
    [
        new() { Id = 1, Title = "Welcome Bonus", Description = "First deposit bonus", BonusAmount = 10 },
        new() { Id = 2, Title = "Weekend Cashback", Description = "5% cashback on losses", BonusAmount = 5 }
    ];

    public List<ContactType> ContactTypes { get; } =
    [
        new() { Id = 1, Name = "Technical" },
        new() { Id = 2, Name = "Billing" },
        new() { Id = 3, Name = "General" }
    ];

    public List<ContactReport> ContactReports { get; } = [];

    public TermsDocument Terms { get; } = new()
    {
        Version = "1.0.0",
        BodyMarkdown = "# Terms\n\nUse this clean-room build for testing and internal validation only.",
        UpdatedUtc = DateTime.UtcNow
    };

    public Dictionary<string, string> AppSettings { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["game.houseRulesetVersion"] = "v2",
        ["signalr.heartbeatSeconds"] = "20",
        ["wallet.currency"] = "USD"
    };

    public Dictionary<string, string> ContactInfo { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["email"] = "support@lucky5.local",
        ["phone"] = "+961-01-000-000"
    };

    public void PreSeedData()
    {
        var defaultRtp = EngineConfig.Default.TargetRtp;
        var defaultScale = EngineConfig.Default.DefaultPayoutScale;

        var machine1 = new Machine { Id = 1, GameId = 1, Name = "Beirut 5K", MachineSerial = "105001", MachineSerie = "27", MachineKent = "1", IsOpen = true, MinBet = 5000, MaxBet = 10000 };
        Machines.TryAdd(1, machine1);
        MachineLedgers.TryAdd(1, CreateSeededLedger(machine1, defaultRtp, defaultScale));

        var machine2 = new Machine { Id = 2, GameId = 1, Name = "Hamra 10K", MachineSerial = "105002", MachineSerie = "27", MachineKent = "2", IsOpen = true, MinBet = 10000, MaxBet = 20000 };
        Machines.TryAdd(2, machine2);
        MachineLedgers.TryAdd(2, CreateSeededLedger(machine2, defaultRtp, defaultScale));

        var machine3 = new Machine { Id = 3, GameId = 1, Name = "VIP 50K", MachineSerial = "105003", MachineSerie = "27", MachineKent = "3", IsOpen = false, MinBet = 50000, MaxBet = 100000 };
        Machines.TryAdd(3, machine3);
        MachineLedgers.TryAdd(3, CreateSeededLedger(machine3, defaultRtp, defaultScale));

        // Update legacy collections
        MachinesList = Machines.Values.ToList();

        var adminId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var adminUser = new User
        {
            Id = adminId,
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
            PhoneNumber = "+96100000000",
            Role = "Admin",
            IsOtpVerified = true
        };
        Profiles.TryAdd(adminId, adminUser);
        Users[adminId] = adminUser;

        MemberProfiles[adminId] = new MemberProfile
        {
            UserId = adminId,
            Username = adminUser.Username,
            DisplayName = adminUser.Username,
            Email = "admin@lucky5.local",
            PhoneNumber = adminUser.PhoneNumber,
            WalletBalance = 1_000_000_000
        };

        var testId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var testUser = new User
        {
            Id = testId,
            Username = "tester",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            PhoneNumber = "+96101000000",
            Role = "Player",
            IsOtpVerified = true
        };
        Profiles.TryAdd(testId, testUser);
        Users[testId] = testUser;

        MemberProfiles[testId] = new MemberProfile
        {
            UserId = testId,
            Username = testUser.Username,
            DisplayName = testUser.Username,
            Email = "tester@lucky5.local",
            PhoneNumber = testUser.PhoneNumber,
            WalletBalance = 50_000_000
        };
    }

    public void ClearStaleRounds(TimeSpan maxAge)
    {
        var now = DateTime.UtcNow;
        var staleIds = ActiveRounds
            .Where(kvp => now - kvp.Value.CreatedUtc > maxAge)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in staleIds)
        {
            ActiveRounds.TryRemove(id, out _);
        }
    }

    private static MachineLedgerState CreateSeededLedger(Machine machine, decimal defaultRtp, decimal defaultScale)
        => new()
        {
            MachineId = machine.Id,
            MachineSerial = machine.MachineSerial,
            MachineSerie = machine.MachineSerie,
            MachineKent = machine.MachineKent,
            TargetRtp = defaultRtp,
            LastPayoutScale = defaultScale
        };
}
