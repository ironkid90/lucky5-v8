namespace Lucky5.Realtime.Services;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class HeartbeatMonitorService : BackgroundService
{
    private readonly ConnectionRegistry _registry;
    private readonly IHubContext<CarrePokerGameHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HeartbeatMonitorService> _logger;
    private readonly TimeSpan _staleAfter;
    private readonly TimeSpan _pollEvery;

    public HeartbeatMonitorService(
        ConnectionRegistry registry,
        IHubContext<CarrePokerGameHub> hubContext,
        IServiceScopeFactory scopeFactory,
        ILogger<HeartbeatMonitorService> logger,
        IConfiguration configuration)
    {
        _registry = registry;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
        _logger = logger;

        var heartbeatSeconds = configuration.GetValue<int?>("SIGNALR:HEARTBEAT_SECONDS") ?? 20;
        heartbeatSeconds = Math.Max(5, heartbeatSeconds);
        _staleAfter = TimeSpan.FromSeconds((heartbeatSeconds * 2) + 5);
        _pollEvery = TimeSpan.FromSeconds(Math.Max(5, heartbeatSeconds / 2));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var staleConnections = _registry.GetStaleConnections(_staleAfter);
            foreach (var connectionId in staleConnections)
            {
                try
                {
                    await _hubContext.Clients.Client(connectionId).SendAsync(
                        "Error",
                        new { code = "HEARTBEAT_TIMEOUT", message = "Heartbeat timeout" },
                        stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to notify stale SignalR connection {ConnectionId}", connectionId);
                }

                // Coordinate with the hub's occupancy model: if the stale connection
                // holds a machine seat, the hub arms its standard grace countdown (or
                // settles immediately when no owner can be derived). Without this the
                // occupancy entry would survive the registry prune with no timer ever
                // armed — the machine stays locked to other players forever.
                if (CarrePokerGameHub.HandleStaleConnection(connectionId, _scopeFactory, _hubContext, _registry))
                {
                    _logger.LogInformation(
                        "Stale connection {ConnectionId} held a machine seat; grace/settlement path engaged",
                        connectionId);
                }

                _registry.Remove(connectionId);
            }

            await Task.Delay(_pollEvery, stoppingToken);
        }
    }
}
