using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lucky5.Infrastructure.Persistence;

/// <summary>
/// Restores the latest durable snapshot into the in-memory authoritative store on startup.
/// </summary>
public sealed class PersistentStateRecoveryService : IHostedService
{
    private readonly IPersistentStateStore store;
    private readonly IPersistentStateCoordinator coordinator;
    private readonly ILogger<PersistentStateRecoveryService> logger;

    public PersistentStateRecoveryService(
        IPersistentStateStore store,
        IPersistentStateCoordinator coordinator,
        ILogger<PersistentStateRecoveryService> logger)
    {
        this.store = store;
        this.coordinator = coordinator;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await store.LoadAsync(cancellationToken);
            if (snapshot is null)
            {
                logger.LogInformation("No durable snapshot found during startup recovery.");
                return;
            }

            await coordinator.RestoreAsync(snapshot, cancellationToken);
            logger.LogInformation(
                "Restored durable snapshot captured at {CapturedUtc}. Users={Users}, Sessions={Sessions}, Rounds={Rounds}.",
                snapshot.CapturedUtc,
                snapshot.Users.Count,
                snapshot.MachineSessions.Count,
                snapshot.ActiveRounds.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to restore durable snapshot on startup. Service continues with seeded in-memory state.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
