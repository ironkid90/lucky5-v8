using System.Text.Json;
using StackExchange.Redis;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lucky5.Infrastructure.Persistence;

public sealed class RedisPersistentStateStore(
    IDistributedCache cache,
    IOptions<PersistentStateCheckpointOptions> options,
    ILogger<RedisPersistentStateStore> logger)
    : IPersistentStateStore
{
    private const string SnapshotKey = "lucky5:state:snapshot:v2";

    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private DateTimeOffset? lastSuccessfulCheckpointUtc;

    public async Task<PersistentStateSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        string? payload;
        try
        {
            payload = await cache.GetStringAsync(SnapshotKeyName(), cancellationToken).ConfigureAwait(false);
        }
        catch (RedisConnectionException ex)
        {
            logger.LogWarning(ex, "Redis state snapshot load unavailable; continuing without recovery.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var snapshot = JsonSerializer.Deserialize<PersistentStateSnapshot>(payload, jsonOptions);
        if (snapshot?.SchemaVersion != PersistentStateSnapshot.CurrentSchemaVersion)
        {
            logger.LogError(
                "Schema mismatch in Redis state snapshot: expected {Expected}, found {Found}.",
                PersistentStateSnapshot.CurrentSchemaVersion,
                snapshot?.SchemaVersion);
            throw new InvalidOperationException(
                $"Schema mismatch: expected {PersistentStateSnapshot.CurrentSchemaVersion}, found {snapshot?.SchemaVersion}.");
        }

        return snapshot;
    }

    public async Task SaveAsync(PersistentStateSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var payload = JsonSerializer.Serialize(snapshot, jsonOptions);
        try
        {
            await cache.SetStringAsync(SnapshotKeyName(), payload, CacheOptions(), cancellationToken).ConfigureAwait(false);
            lastSuccessfulCheckpointUtc = DateTimeOffset.UtcNow;
        }
        catch (RedisConnectionException ex)
        {
            logger.LogWarning(ex, "Redis state snapshot save unavailable; checkpoint skipped.");
        }
    }

    public async Task<string?> LoadDisplaySnapshotAsync(int machineId, CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetStringAsync(BuildDisplaySnapshotKey(machineId), cancellationToken).ConfigureAwait(false);
        }
        catch (RedisConnectionException ex)
        {
            logger.LogWarning(ex, "Redis cabinet display snapshot load unavailable for machine {MachineId}.", machineId);
            return null;
        }
    }

    public async Task SaveDisplaySnapshotAsync(int machineId, string payload, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetStringAsync(BuildDisplaySnapshotKey(machineId), payload, CacheOptions(), cancellationToken).ConfigureAwait(false);
        }
        catch (RedisConnectionException ex)
        {
            logger.LogWarning(ex, "Redis cabinet display snapshot save unavailable for machine {MachineId}.", machineId);
        }
    }

    public Task<PersistentStoreHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PersistentStoreHealth(
            IsReady: true,
            IsDegraded: false,
            Description: "Distributed cache persistent state store is configured.",
            LastSuccessfulCheckpointUtc: lastSuccessfulCheckpointUtc,
            LastError: null));
    }

    private DistributedCacheEntryOptions CacheOptions()
        => new();

    private string BuildDisplaySnapshotKey(int machineId)
        => $"{options.Value.DisplaySnapshotKeyPrefix}{machineId}";

    private string SnapshotKeyName()
        => string.IsNullOrWhiteSpace(options.Value.SnapshotKey) ? SnapshotKey : options.Value.SnapshotKey;
}