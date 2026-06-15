namespace Lucky5.Infrastructure.Services;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class StateRecoveryHostedService : IHostedService
{
    private readonly InMemoryDataStore store;
    private readonly IPersistentStateStore persistentStore;
    private readonly ILogger<StateRecoveryHostedService> logger;

    public StateRecoveryHostedService(
        InMemoryDataStore store,
        IPersistentStateStore persistentStore,
        ILogger<StateRecoveryHostedService> logger)
    {
        this.store = store;
        this.persistentStore = persistentStore;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var restored = await persistentStore.TryLoadIntoAsync(store, cancellationToken).ConfigureAwait(false);
            if (restored)
            {
                logger.LogInformation("Loaded Lucky5 machine/session state from external persistence.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to restore Lucky5 state from external persistence. Falling back to in-memory seed data.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}