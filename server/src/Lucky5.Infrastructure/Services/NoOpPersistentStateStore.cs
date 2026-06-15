namespace Lucky5.Infrastructure.Services;

public sealed class NoOpPersistentStateStore : IPersistentStateStore
{
    public Task<bool> TryLoadIntoAsync(InMemoryDataStore store, CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task PersistAsync(InMemoryDataStore store, CancellationToken cancellationToken)
        => Task.CompletedTask;
}