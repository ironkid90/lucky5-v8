namespace Lucky5.Infrastructure.Persistence;

public interface IPersistentStateCoordinator
{
    Task<PersistentStateSnapshot> CaptureAsync(CancellationToken cancellationToken);
    Task RestoreAsync(PersistentStateSnapshot snapshot, CancellationToken cancellationToken);
}
