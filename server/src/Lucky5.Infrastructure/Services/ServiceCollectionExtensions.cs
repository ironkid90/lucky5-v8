namespace Lucky5.Infrastructure.Services;

using System.Net;
using Lucky5.Application.Contracts;
using Lucky5.Application.Interfaces;
using Lucky5.Infrastructure.Data.Repositories;
using Lucky5.Infrastructure.Persistence;
using PersistenceCoordinator = Lucky5.Infrastructure.Persistence.IPersistentStateCoordinator;
using PersistenceStore = Lucky5.Infrastructure.Persistence.IPersistentStateStore;
using RedisSnapshotStore = Lucky5.Infrastructure.Persistence.RedisPersistentStateStore;
using FileSnapshotStore = Lucky5.Infrastructure.Persistence.FilePersistentStateStore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLucky5Infrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<InMemoryDataStore>();
        services.AddSingleton<IDataStore, InMemoryDataStoreAdapter>();
        
        // Register new persistence services from migration pack
        services.AddOptions<PersistentStateCheckpointOptions>()
            .Bind(configuration.GetSection("Persistence"));
        
        // Persistent state store selection:
        //   1. If LUCKY5_STATE_DIR / Persistence:SnapshotDirectory is set, use file-backed snapshots.
        //      On Cloud Run Gen 2 this path can be a mounted Cloud Storage FUSE volume, giving durable
        //      cross-revision persistence with zero extra infra.
        //   2. Otherwise, if a Redis connection string is configured, use Redis via IDistributedCache.
        //   3. Otherwise, fall back to an in-process distributed memory cache. State will NOT survive
        //      a container restart, but the app runs fine for local/dev.
        var snapshotDirectory = configuration["Persistence:SnapshotDirectory"]
            ?? configuration["LUCKY5_STATE_DIR"]
            ?? configuration["PERSISTENCE__SNAPSHOTDIRECTORY"];

        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? configuration["LUCKY5_REDIS_CONNECTION"]
            ?? configuration["Redis:ConnectionString"]
            ?? configuration["REDIS:CONNECTION"];

        if (!string.IsNullOrWhiteSpace(snapshotDirectory))
        {
            services.AddDistributedMemoryCache();
            services.AddSingleton<PersistenceStore>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<PersistentStateCheckpointOptions>>();
                var logger = sp.GetRequiredService<ILogger<FileSnapshotStore>>();
                return new FileSnapshotStore(snapshotDirectory!, opts, logger);
            });
        }
        else if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                var configOptions = ConfigurationOptions.Parse(redisConnectionString);

                if (configOptions.EndPoints.Any(endpoint => endpoint is DnsEndPoint dns && dns.Host.EndsWith(".redis.azure.net", StringComparison.OrdinalIgnoreCase)))
                {
                    configOptions.Ssl = true;
                    configOptions.ConnectTimeout = 20000;
                    configOptions.SyncTimeout = 20000;
                    configOptions.AsyncTimeout = 20000;
                    configOptions.AbortOnConnectFail = false;
                    configOptions.ConnectRetry = 5;
                    configOptions.ReconnectRetryPolicy = new ExponentialRetry(2000);
                }
                else
                {
                    // General Redis configuration
                    configOptions.ConnectTimeout = 10000;
                    configOptions.SyncTimeout = 10000;
                    configOptions.AsyncTimeout = 10000;
                }

                options.ConfigurationOptions = configOptions;
            });
            services.AddSingleton<PersistenceStore, RedisSnapshotStore>();
        }
        else
        {
            services.AddDistributedMemoryCache();
            services.AddSingleton<PersistenceStore, RedisSnapshotStore>();
        }

        services.AddSingleton<PersistenceCoordinator, InMemoryPersistentStateCoordinator>();
        services.AddHostedService<PersistentStateRecoveryService>();
        services.AddSingleton<PersistentStateCheckpointService>();
        services.AddHostedService(sp => sp.GetRequiredService<PersistentStateCheckpointService>());
        
        // Add health checks
        services.AddHealthChecks()
            .AddCheck<PersistentStateHealthCheck>("persistence")
            .AddCheck("basic", () => HealthCheckResult.Healthy("Application is running"));
        services.AddOptions<MachineCacheTtlOptions>()
            .Bind(configuration.GetSection("MachineCache"));
        services.AddSingleton<IMachineStateCache>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MachineCacheTtlOptions>>().Value;
            return new InMemoryMachineStateCache(opts);
        });
        services.AddSingleton<ITokenService, SimpleTokenService>();
        services.AddSingleton<IEntropyGenerator, DefaultEntropyGenerator>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IGameService, GameService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddSingleton<IAdminAuditService, AdminAuditService>();
        services.AddSingleton<ICabinetDeviceAuthService, CabinetDeviceAuthService>();
        services.AddScoped<IGeneralService, GeneralService>();
        services.AddScoped<IRewardService, RewardService>();
        services.AddSingleton<INotificationService, FirebaseNotificationService>();
        services.AddScoped<IAgentService, AgentService>();

        return services;
    }
}
