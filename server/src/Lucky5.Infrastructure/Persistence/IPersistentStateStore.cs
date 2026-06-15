namespace Lucky5.Infrastructure.Persistence;

public interface IPersistentStateStore
{
    Task<PersistentStateSnapshot?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(PersistentStateSnapshot snapshot, CancellationToken cancellationToken);
    Task<string?> LoadDisplaySnapshotAsync(int machineId, CancellationToken cancellationToken);
    Task SaveDisplaySnapshotAsync(int machineId, string payload, CancellationToken cancellationToken);
    Task<PersistentStoreHealth> GetHealthAsync(CancellationToken cancellationToken);
}
