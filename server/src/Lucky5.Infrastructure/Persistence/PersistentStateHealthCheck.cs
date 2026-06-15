using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Lucky5.Infrastructure.Persistence;

public sealed class PersistentStateHealthCheck : IHealthCheck
{
    private readonly IPersistentStateStore store;
    private readonly PersistentStateCheckpointService checkpointService;
    private readonly IOptions<PersistentStateCheckpointOptions> options;

    public PersistentStateHealthCheck(
        IPersistentStateStore store,
        PersistentStateCheckpointService checkpointService,
        IOptions<PersistentStateCheckpointOptions> options)
    {
        this.store = store;
        this.checkpointService = checkpointService;
        this.options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var storeHealth = await store.GetHealthAsync(cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["gracefulDegradationEnabled"] = options.Value.GracefulDegradationEnabled,
            ["checkpointEnabled"] = options.Value.Enabled,
            ["checkpointIntervalSeconds"] = options.Value.CheckpointInterval.TotalSeconds,
            ["lastSuccessfulCheckpointUtc"] = checkpointService.LastSuccessfulCheckpointUtc?.ToString() ?? string.Empty,
            ["lastError"] = checkpointService.LastError ?? string.Empty,
            ["storeDescription"] = storeHealth.Description
        };

        if (storeHealth.IsReady)
        {
            return HealthCheckResult.Healthy("Durable persistence is ready.", data);
        }

        if (options.Value.GracefulDegradationEnabled)
        {
            return HealthCheckResult.Degraded("Durable persistence unavailable; service remains live on in-memory state.", null, data);
        }

        return HealthCheckResult.Unhealthy("Durable persistence unavailable and graceful degradation is disabled.", null, data);
    }
}
