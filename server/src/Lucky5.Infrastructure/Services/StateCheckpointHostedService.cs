namespace Lucky5.Infrastructure.Services;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class StateCheckpointHostedService : BackgroundService
{
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(15);

    private readonly InMemoryDataStore store;
    private readonly IPersistentStateStore persistentStore;
    private readonly ILogger<StateCheckpointHostedService> logger;

    public StateCheckpointHostedService(
        InMemoryDataStore store,
        IPersistentStateStore persistentStore,
        ILogger<StateCheckpointHostedService> logger)
    {
        this.store = store;
        this.persistentStore = persistentStore;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckpointInterval, stoppingToken).ConfigureAwait(false);
                await persistentStore.PersistAsync(store, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Periodic Lucky5 state checkpoint to external persistence failed.");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await persistentStore.PersistAsync(store, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Final Lucky5 state checkpoint during shutdown failed.");
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
