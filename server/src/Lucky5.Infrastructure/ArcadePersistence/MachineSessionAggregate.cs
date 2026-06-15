namespace Lucky5.Infrastructure.ArcadePersistence;

using Lucky5.Domain.Entities;
using Lucky5.Domain.Game.CleanRoom;

public enum MachinePhase
{
    Idle = 0,
    Dealt = 1,
    Drawn = 2,
    DoubleUp = 3,
    Settled = 4,
    Closed = 5
}

/// <summary>
/// Arcade-grade aggregate root for machine state.
/// Encapsulates session, ledger, active round and double-up state
/// for deterministic persistence and reconstruction.
/// </summary>
public sealed class MachineSessionAggregate
{
    public Guid AggregateId { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public int MachineId { get; init; }

    public MachineLedgerState Ledger { get; private set; } = new();
    public MachineSessionState Session { get; private set; } = new();

    public GameRound? ActiveRound { get; private set; }
    public Lucky5DoubleUpSession? DoubleUp { get; private set; }

    public long Version { get; private set; }
    public DateTime LastUpdatedUtc { get; private set; } = DateTime.UtcNow;

    public MachinePhase Phase =>
        Session.IsMachineClosed ? MachinePhase.Closed :
        DoubleUp is not null && !DoubleUp.IsTerminal ? MachinePhase.DoubleUp :
        ActiveRound is not null && !ActiveRound.IsCompleted ? MachinePhase.Dealt :
        ActiveRound is not null && ActiveRound.IsCompleted && !ActiveRound.IsPayoutSettled ? MachinePhase.Drawn :
        ActiveRound is not null && ActiveRound.IsPayoutSettled ? MachinePhase.Settled :
        MachinePhase.Idle;

    public void Recompute(decimal closeThreshold)
    {
        Session.IsMachineClosed = Session.MachineCredits >= closeThreshold;
        LastUpdatedUtc = DateTime.UtcNow;
    }

    public void EnsureIntegrity(decimal closeThreshold)
    {
        if (Session.IsMachineClosed != (Session.MachineCredits >= closeThreshold))
            throw new InvalidOperationException("MachineClosed flag mismatch");

        if (DoubleUp is not null && ActiveRound is null)
            throw new InvalidOperationException("DoubleUp without ActiveRound");
    }

    public void Touch()
    {
        Version++;
        LastUpdatedUtc = DateTime.UtcNow;
    }
}
