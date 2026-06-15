using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lucky5.Infrastructure.Persistence;

/// <summary>
/// Periodically checkpoints authoritative in-memory state into the durable store.
/// The interval is hard-pinned to 10 seconds by default per the migration target.
/// </summary>
public sealed class PersistentStateCheckpointService : BackgroundService
{
    private readonly IPersistentStateCoordinator coordinator;
    private readonly IPersistentStateStore store;
    private readonly IOptions<PersistentStateCheckpointOptions> options;
    private readonly ILogger<PersistentStateCheckpointService> logger;
    private DateTimeOffset? lastSuccessfulCheckpointUtc;
    private string? lastError;

    public PersistentStateCheckpointService(
        IPersistentStateCoordinator coordinator,
        IPersistentStateStore store,
        IOptions<PersistentStateCheckpointOptions> options,
        ILogger<PersistentStateCheckpointService> logger)
    {
        this.coordinator = coordinator;
        this.store = store;
        this.options = options;
        this.logger = logger;
    }

    public DateTimeOffset? LastSuccessfulCheckpointUtc => lastSuccessfulCheckpointUtc;
    public string? LastError => lastError;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Persistent state checkpoint service is disabled by configuration.");
            return;
        }

        using var timer = new PeriodicTimer(options.Value.CheckpointInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var snapshot = await coordinator.CaptureAsync(stoppingToken);
                await store.SaveAsync(snapshot with { SchemaVersion = PersistentStateSnapshot.CurrentSchemaVersion }, stoppingToken);
                lastSuccessfulCheckpointUtc = DateTimeOffset.UtcNow;
                lastError = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                logger.LogError(ex, "Persistent checkpoint failed.");

                if (!options.Value.GracefulDegradationEnabled)
                {
                    throw;
                }
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var snapshot = await coordinator.CaptureAsync(cancellationToken).ConfigureAwait(false);
            await store.SaveAsync(snapshot with { SchemaVersion = PersistentStateSnapshot.CurrentSchemaVersion }, cancellationToken).ConfigureAwait(false);
            lastSuccessfulCheckpointUtc = DateTimeOffset.UtcNow;
            lastError = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            logger.LogWarning(ex, "Final persistent checkpoint during shutdown failed.");

            if (!options.Value.GracefulDegradationEnabled)
            {
                throw;
            }
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
