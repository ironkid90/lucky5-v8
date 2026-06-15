namespace Lucky5.Infrastructure.Services;

public interface IPersistentStateStore
{
    Task<bool> TryLoadIntoAsync(InMemoryDataStore store, CancellationToken cancellationToken);
    Task PersistAsync(InMemoryDataStore store, CancellationToken cancellationToken);
}