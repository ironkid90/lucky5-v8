namespace Lucky5.Infrastructure.Services;

using StackExchange.Redis;

public sealed class RedisPersistentStateStore : IPersistentStateStore
{
    private const string SnapshotKey = "lucky5:state:snapshot:v1";
    private const string SnapshotVersionKey = "lucky5:state:snapshot:version";
    private const string WriteLockKey = "lucky5:state:lock:write";

    private readonly IConnectionMultiplexer redis;
    private readonly TimeSpan lockTtl = TimeSpan.FromSeconds(8);
    private readonly string lockValue = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public RedisPersistentStateStore(IConnectionMultiplexer redis)
    {
        this.redis = redis;
    }

    public async Task<bool> TryLoadIntoAsync(InMemoryDataStore store, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = redis.GetDatabase();

        var payload = await db.StringGetAsync(SnapshotKey).ConfigureAwait(false);
        if (!payload.HasValue)
        {
            return false;
        }

        return StateSnapshotCodec.TryHydrate(store, payload!);
    }

    public async Task PersistAsync(InMemoryDataStore store, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = redis.GetDatabase();

        var lockTaken = await db.StringSetAsync(WriteLockKey, lockValue, lockTtl, When.NotExists).ConfigureAwait(false);
        if (!lockTaken)
        {
            // Another instance is actively writing the snapshot.
            return;
        }

        try
        {
            var payload = StateSnapshotCodec.Serialize(store);
            var batch = db.CreateBatch();
            var setSnapshot = batch.StringSetAsync(SnapshotKey, payload);
            var incrVersion = batch.StringIncrementAsync(SnapshotVersionKey);
            batch.Execute();
            await Task.WhenAll(setSnapshot, incrVersion).ConfigureAwait(false);
        }
        finally
        {
            // Best-effort release; if this fails TTL will free it.
            var current = await db.StringGetAsync(WriteLockKey).ConfigureAwait(false);
            if (current.HasValue && string.Equals(current!, lockValue, StringComparison.Ordinal))
            {
                await db.KeyDeleteAsync(WriteLockKey).ConfigureAwait(false);
            }
        }
    }
}