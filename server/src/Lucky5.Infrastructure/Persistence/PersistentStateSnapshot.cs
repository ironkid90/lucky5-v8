using Lucky5.Domain.Entities;

namespace Lucky5.Infrastructure.Persistence;

/// <summary>
/// Versioned authoritative checkpoint image for durable recovery.
/// v2 is the first schema that is safe for hybrid in-memory + durable restore.
/// </summary>
public sealed record PersistentStateSnapshot
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTimeOffset CapturedUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<Machine> Machines { get; init; } = Array.Empty<Machine>();
    public IReadOnlyList<User> Users { get; init; } = Array.Empty<User>();
    public IReadOnlyList<MemberProfile> Profiles { get; init; } = Array.Empty<MemberProfile>();
    public IReadOnlyList<MachineSessionState> MachineSessions { get; init; } = Array.Empty<MachineSessionState>();
    public IReadOnlyList<MachineLedgerState> MachineLedgers { get; init; } = Array.Empty<MachineLedgerState>();
    public IReadOnlyList<GameRound> ActiveRounds { get; init; } = Array.Empty<GameRound>();
    public IReadOnlyList<WalletLedgerEntry> WalletLedgerEntries { get; init; } = Array.Empty<WalletLedgerEntry>();
}
