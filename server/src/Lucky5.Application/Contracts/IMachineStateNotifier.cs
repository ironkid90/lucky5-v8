namespace Lucky5.Application.Contracts;

/// <summary>
/// Pushes authoritative machine state to realtime watchers (the active player's
/// other tabs and all spectators) after a state-changing action. Implemented by
/// the realtime layer; REST controllers call this after mutations so spectators
/// see live gameplay even though gameplay itself flows over REST.
/// Implementations must never throw — fan-out failure must not fail the
/// authoritative game action.
/// </summary>
public interface IMachineStateNotifier
{
    Task MachineStateChangedAsync(int machineId, Guid? userId, CancellationToken cancellationToken = default);

    Task MachineStateChangedForRoundAsync(Guid roundId, Guid userId, CancellationToken cancellationToken = default);
}
