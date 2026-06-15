using Lucky5.Domain.Entities;
using Lucky5.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lucky5.Tests;

/// <summary>
/// Smoke regression tests for the file-backed persistent state store wired in for Cloud Run
/// deployments that provide a durable mount (e.g., Cloud Storage FUSE volume).
/// </summary>
public static class FilePersistentStateStoreTests
{
    public static async Task RunAsync(List<string> failures)
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"lucky5-tests-{Guid.NewGuid():N}");

        try
        {
            await RoundTripSnapshotPreservesSchemaVersion(failures, rootDir);
            await DisplaySnapshotIsPersistedAndRead(failures, rootDir);
            await MissingSnapshotReturnsNull(failures, rootDir);
            await HealthReportsReadyWhenDirectoryWritable(failures, rootDir);
        }
        finally
        {
            try
            {
                if (Directory.Exists(rootDir))
                {
                    Directory.Delete(rootDir, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup; do not surface cleanup failures as regressions.
            }
        }
    }

    private static async Task RoundTripSnapshotPreservesSchemaVersion(List<string> failures, string rootDir)
    {
        var caseDir = Path.Combine(rootDir, "roundtrip");
        var store = CreateStore(caseDir);

        var snapshot = new PersistentStateSnapshot
        {
            Users = new[]
            {
                new User { Id = Guid.NewGuid(), Username = "file-store-tester", CreatedUtc = DateTime.UtcNow }
            },
            MachineLedgers = new[]
            {
                new MachineLedgerState
                {
                    MachineId = 1,
                    CapitalIn = 500_000m,
                    CapitalOut = 400_000m,
                    TargetRtp = 0.80m,
                    RoundCount = 50
                }
            }
        };

        await store.SaveAsync(snapshot, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert(failures, "File-backed snapshot should round-trip", loaded is not null);
        Assert(failures, "Snapshot should keep schema version", loaded?.SchemaVersion == PersistentStateSnapshot.CurrentSchemaVersion);
        Assert(failures, "Snapshot should preserve user count", loaded?.Users.Count == 1);
        Assert(failures, "Snapshot should preserve machine ledger count", loaded?.MachineLedgers.Count == 1);
        Assert(failures, "Snapshot should preserve user name", loaded?.Users[0].Username == "file-store-tester");
    }

    private static async Task DisplaySnapshotIsPersistedAndRead(List<string> failures, string rootDir)
    {
        var caseDir = Path.Combine(rootDir, "display");
        var store = CreateStore(caseDir);

        const int machineId = 7;
        const string payload = "{\"machineSerial\":\"105007\",\"credits\":42}";

        await store.SaveDisplaySnapshotAsync(machineId, payload, CancellationToken.None);
        var loaded = await store.LoadDisplaySnapshotAsync(machineId, CancellationToken.None);

        Assert(failures, "Display snapshot payload should round-trip", loaded == payload);
    }

    private static async Task MissingSnapshotReturnsNull(List<string> failures, string rootDir)
    {
        var caseDir = Path.Combine(rootDir, "missing");
        var store = CreateStore(caseDir);

        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert(failures, "Missing snapshot should return null", loaded is null);

        var display = await store.LoadDisplaySnapshotAsync(99, CancellationToken.None);
        Assert(failures, "Missing display snapshot should return null", display is null);
    }

    private static async Task HealthReportsReadyWhenDirectoryWritable(List<string> failures, string rootDir)
    {
        var caseDir = Path.Combine(rootDir, "health");
        var store = CreateStore(caseDir);

        var health = await store.GetHealthAsync(CancellationToken.None);
        Assert(failures, "Health should be ready for writable directory", health.IsReady);
        Assert(failures, "Health should not be degraded for writable directory", !health.IsDegraded);
    }

    private static FilePersistentStateStore CreateStore(string directory)
    {
        var options = Options.Create(new PersistentStateCheckpointOptions
        {
            CheckpointInterval = TimeSpan.FromSeconds(10),
            GracefulDegradationEnabled = true,
            SnapshotKey = "lucky5:persistent-state:v2",
            DisplaySnapshotKeyPrefix = PersistentStateCheckpointOptions.DefaultDisplaySnapshotKeyPrefix
        });

        return new FilePersistentStateStore(directory, options, NullLogger<FilePersistentStateStore>.Instance);
    }

    private static void Assert(List<string> failures, string description, bool condition)
    {
        if (!condition)
        {
            failures.Add($"FilePersistentStateStoreTests: {description}");
        }
    }
}
