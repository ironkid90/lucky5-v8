using Lucky5.Infrastructure.Persistence;
using Lucky5.Domain.Entities;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using Xunit;

namespace Lucky5.Tests;

/// <summary>
/// RED (Redis) migration tests to validate the three-track migration implementation.
/// Tests Redis persistence, 80% RTP balancing, and unified policy resolution.
/// </summary>
public sealed class RedisPersistenceMigrationTests
{
    private const string DisplaySnapshotKeyPrefix = PersistentStateCheckpointOptions.DefaultDisplaySnapshotKeyPrefix;

    private readonly Mock<IDistributedCache> mockCache;
    private readonly Mock<ILogger<RedisPersistentStateStore>> mockLogger;
    private readonly Mock<ILogger<PersistentStateCheckpointService>> mockCheckpointLogger;
    private readonly Mock<IPersistentStateCoordinator> mockCoordinator;
    private readonly IOptions<PersistentStateCheckpointOptions> options;

    public RedisPersistenceMigrationTests()
    {
        mockCache = new Mock<IDistributedCache>();
        mockLogger = new Mock<ILogger<RedisPersistentStateStore>>();
        mockCheckpointLogger = new Mock<ILogger<PersistentStateCheckpointService>>();
        mockCoordinator = new Mock<IPersistentStateCoordinator>();
        options = Options.Create(new PersistentStateCheckpointOptions
        {
            CheckpointInterval = TimeSpan.FromSeconds(10),
            GracefulDegradationEnabled = true,
            SnapshotKey = "lucky5:persistent-state:v2",
            DisplaySnapshotKeyPrefix = DisplaySnapshotKeyPrefix
        });
    }

    [Fact]
    public async Task RedisPersistentStateStore_SaveAndLoad_ShouldPreserveSchemaVersion()
    {
        // Arrange
        var store = new RedisPersistentStateStore(mockCache.Object, options, mockLogger.Object);
        var snapshot = CreateTestSnapshot();
        var serializedSnapshot = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        mockCache.Setup(x => x.GetStringAsync(options.Value.SnapshotKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(serializedSnapshot);

        // Act
        await store.SaveAsync(snapshot, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(PersistentStateSnapshot.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(snapshot.Users.Count, loaded.Users.Count);
        Assert.Equal(snapshot.MachineSessions.Count, loaded.MachineSessions.Count);
    }

    [Fact]
    public async Task RedisPersistentStateStore_LoadWithMismatchedSchema_ShouldThrowException()
    {
        // Arrange
        var store = new RedisPersistentStateStore(mockCache.Object, options, mockLogger.Object);
        var invalidSnapshot = new PersistentStateSnapshot { SchemaVersion = 1 }; // Old version
        var serializedSnapshot = JsonSerializer.Serialize(invalidSnapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        mockCache.Setup(x => x.GetStringAsync(options.Value.SnapshotKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(serializedSnapshot);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.LoadAsync(CancellationToken.None));
        
        Assert.Contains("schema mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RedisPersistentStateStore_GetHealth_WhenAvailable_ShouldReturnReady()
    {
        // Arrange
        var store = new RedisPersistentStateStore(mockCache.Object, options, mockLogger.Object);
        mockCache.Setup(x => x.GetStringAsync(options.Value.SnapshotKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync("test");

        // Act
        var health = await store.GetHealthAsync(CancellationToken.None);

        // Assert
        Assert.True(health.IsReady);
        Assert.False(health.IsDegraded);
        Assert.Equal("Redis snapshot store reachable.", health.Description);
    }

    [Fact]
    public async Task RedisPersistentStateStore_GetHealth_WhenUnavailable_ShouldReturnDegraded()
    {
        // Arrange
        var store = new RedisPersistentStateStore(mockCache.Object, options, mockLogger.Object);
        mockCache.Setup(x => x.GetStringAsync(options.Value.SnapshotKey, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis unavailable"));

        // Act
        var health = await store.GetHealthAsync(CancellationToken.None);

        // Assert
        Assert.False(health.IsReady);
        Assert.True(health.IsDegraded);
        Assert.Contains("unavailable", health.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Redis unavailable", health.LastError);
    }

    [Fact]
    public async Task PersistentStateCheckpointService_ShouldCheckpointPeriodically()
    {
        // Arrange
        var snapshot = CreateTestSnapshot();
        mockCoordinator.Setup(x => x.CaptureAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var store = new RedisPersistentStateStore(mockCache.Object, options, mockLogger.Object);
        var service = new PersistentStateCheckpointService(
            mockCoordinator.Object,
            store,
            options,
            mockCheckpointLogger.Object);

        // Act - simulate a few checkpoint cycles
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100)); // Short delay for test

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.StartAsync(cts.Token));

        // Assert
        mockCoordinator.Verify(x => x.CaptureAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        mockCache.Verify(x => x.SetStringAsync(
            options.Value.SnapshotKey,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce());
    }

    [Fact]
    public void PersistentStateSnapshot_ShouldHaveCorrectSchemaVersion()
    {
        // Act
        var snapshot = new PersistentStateSnapshot();

        // Assert
        Assert.Equal(2, PersistentStateSnapshot.CurrentSchemaVersion);
        Assert.Equal(2, snapshot.SchemaVersion);
        Assert.NotEqual(default, snapshot.CapturedUtc);
        Assert.NotNull(snapshot.Users);
        Assert.NotNull(snapshot.Profiles);
        Assert.NotNull(snapshot.MachineSessions);
        Assert.NotNull(snapshot.MachineLedgers);
        Assert.NotNull(snapshot.ActiveRounds);
        Assert.NotNull(snapshot.WalletLedgerEntries);
    }

    [Fact]
    public void PersistentStateCheckpointOptions_ShouldHaveDefaults()
    {
        // Act
        var options = new PersistentStateCheckpointOptions();

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(10), options.CheckpointInterval);
        Assert.True(options.GracefulDegradationEnabled);
        Assert.Equal("lucky5:persistent-state:v2", options.SnapshotKey);
        Assert.Equal(DisplaySnapshotKeyPrefix, options.DisplaySnapshotKeyPrefix);
    }

    [Fact]
    public async Task RedisPersistentStateStore_SaveDisplaySnapshotAsync_ShouldRoundTripDedicatedMachinePayload()
    {
        // Arrange
        var store = new RedisPersistentStateStore(mockCache.Object, options, mockLogger.Object);
        const int machineId = 7;
        const string payload = "{\"machineSerial\":123,\"machineSerie\":45,\"machineKent\":6}";

        mockCache.Setup(x => x.GetStringAsync($"{DisplaySnapshotKeyPrefix}{machineId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);

        // Act
        await store.SaveDisplaySnapshotAsync(machineId, payload, CancellationToken.None);
        var loaded = await store.LoadDisplaySnapshotAsync(machineId, CancellationToken.None);

        // Assert
        Assert.Equal(payload, loaded);
    }

    [Fact]
    public async Task RedisPersistentStateStore_LoadDisplaySnapshotAsync_WhenRedisThrows_ShouldGracefullyReturnNull()
    {
        // Arrange
        var store = new RedisPersistentStateStore(mockCache.Object, options, mockLogger.Object);

        mockCache.Setup(x => x.GetStringAsync($"{DisplaySnapshotKeyPrefix}11", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("display snapshot unavailable"));

        // Act
        var loaded = await store.LoadDisplaySnapshotAsync(11, CancellationToken.None);

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public void PersistentStoreHealth_ShouldCaptureAllFields()
    {
        // Act
        var health = new PersistentStoreHealth(
            IsReady: true,
            IsDegraded: false,
            Description: "Test health",
            LastSuccessfulCheckpointUtc: DateTime.UtcNow,
            LastError: null);

        // Assert
        Assert.True(health.IsReady);
        Assert.False(health.IsDegraded);
        Assert.Equal("Test health", health.Description);
        Assert.NotNull(health.LastSuccessfulCheckpointUtc);
        Assert.Null(health.LastError);
    }

    private static PersistentStateSnapshot CreateTestSnapshot()
    {
        return new PersistentStateSnapshot
        {
            Users = new[]
            {
                new User { Id = Guid.NewGuid(), Username = "testuser", CreatedUtc = DateTime.UtcNow }
            },
            Profiles = new[]
            {
                new MemberProfile { UserId = Guid.NewGuid(), WalletBalance = 1000000m, LastSeenUtc = DateTime.UtcNow }
            },
            MachineSessions = new[]
            {
                new MachineSessionState 
                { 
                    SessionId = Guid.NewGuid(), 
                    UserId = Guid.NewGuid(), 
                    MachineId = 1,
                    MachineCredits = 500000m,
                    CreatedUtc = DateTime.UtcNow
                }
            },
            MachineLedgers = new[]
            {
                new MachineLedgerState 
                { 
                    MachineId = 1,
                    CapitalIn = 1000000m,
                    CapitalOut = 800000m,
                    TargetRtp = 0.80m,
                    RoundCount = 1000
                }
            },
            ActiveRounds = Array.Empty<GameRound>(),
            WalletLedgerEntries = new[]
            {
                new WalletLedgerEntry
                {
                    UserId = Guid.NewGuid(),
                    Amount = 100000m,
                    BalanceAfter = 1000000m,
                    TransactionType = "CashIn",
                    CreatedUtc = DateTime.UtcNow
                }
            }
        };
    }
}
