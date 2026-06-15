using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lucky5.Infrastructure.Persistence;

/// <summary>
/// File-backed snapshot store that writes <see cref="PersistentStateSnapshot"/> and cabinet display
/// payloads to a configurable directory. Intended for environments that do not (yet) have Redis, but
/// do provide a durable mount point - most notably Cloud Run Gen 2 with a Cloud Storage FUSE volume
/// mounted at <c>LUCKY5_STATE_DIR</c>. The implementation is deliberately defensive: all writes are
/// atomic (temp file + rename) and every IO path fails soft so an unavailable filesystem never
/// takes down the authoritative in-memory service.
/// </summary>
public sealed class FilePersistentStateStore : IPersistentStateStore
{
    private const string SnapshotFileName = "snapshot.json";
    private const string DisplaySnapshotFolder = "cabinet-display";
    private const string DisplaySnapshotExtension = ".json";

    private readonly string rootDirectory;
    private readonly IOptions<PersistentStateCheckpointOptions> options;
    private readonly ILogger<FilePersistentStateStore> logger;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private DateTimeOffset? lastSuccessfulCheckpointUtc;

    public FilePersistentStateStore(
        string rootDirectory,
        IOptions<PersistentStateCheckpointOptions> options,
        ILogger<FilePersistentStateStore> logger)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Root directory must be a non-empty path.", nameof(rootDirectory));
        }

        this.rootDirectory = Path.GetFullPath(rootDirectory);
        this.options = options;
        this.logger = logger;

        TryEnsureDirectory(this.rootDirectory);
        TryEnsureDirectory(Path.Combine(this.rootDirectory, DisplaySnapshotFolder));
    }

    public async Task<PersistentStateSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(rootDirectory, SnapshotFileName);

        try
        {
            if (!File.Exists(path))
            {
                logger.LogInformation("No persistent state snapshot file found at {Path}.", path);
                return null;
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            var snapshot = await JsonSerializer.DeserializeAsync<PersistentStateSnapshot>(
                stream,
                jsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (snapshot is null)
            {
                logger.LogWarning("Snapshot file at {Path} deserialized to null; discarding.", path);
                return null;
            }

            if (snapshot.SchemaVersion != PersistentStateSnapshot.CurrentSchemaVersion)
            {
                logger.LogError(
                    "Schema mismatch in snapshot file {Path}: expected {Expected}, found {Found}.",
                    path,
                    PersistentStateSnapshot.CurrentSchemaVersion,
                    snapshot.SchemaVersion);
                throw new InvalidOperationException(
                    $"Schema mismatch: expected {PersistentStateSnapshot.CurrentSchemaVersion}, found {snapshot.SchemaVersion}.");
            }

            logger.LogInformation(
                "Loaded persistent snapshot from {Path} with {UserCount} users and {SessionCount} sessions.",
                path,
                snapshot.Users.Count,
                snapshot.MachineSessions.Count);

            return snapshot;
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is JsonException)
        {
            // Data errors are fatal to recovery - surface them so startup logs capture the root cause.
            logger.LogError(ex, "Failed to load persistent state snapshot from {Path} due to a data error.", path);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read persistent state snapshot at {Path}; continuing without recovery.", path);
            return null;
        }
    }

    public async Task SaveAsync(PersistentStateSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var path = Path.Combine(rootDirectory, SnapshotFileName);
        var tempPath = path + ".tmp";

        try
        {
            TryEnsureDirectory(rootDirectory);

            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, jsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
            lastSuccessfulCheckpointUtc = DateTimeOffset.UtcNow;

            logger.LogDebug(
                "Persistent state checkpoint saved at {CapturedUtc} using schema v{SchemaVersion} -> {Path}.",
                snapshot.CapturedUtc,
                snapshot.SchemaVersion,
                path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save persistent state snapshot to {Path}.", path);

            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception cleanupEx)
                {
                    logger.LogWarning(cleanupEx, "Failed to cleanup temporary snapshot file {TempPath}.", tempPath);
                }
            }
        }
    }

    public async Task<string?> LoadDisplaySnapshotAsync(int machineId, CancellationToken cancellationToken)
    {
        var path = BuildDisplaySnapshotPath(machineId);

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load cabinet display snapshot for machine {MachineId} from {Path}.", machineId, path);
            return null;
        }
    }

    public async Task SaveDisplaySnapshotAsync(int machineId, string payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var path = BuildDisplaySnapshotPath(machineId);
        var tempPath = path + ".tmp";

        try
        {
            TryEnsureDirectory(Path.GetDirectoryName(path)!);

            await File.WriteAllTextAsync(tempPath, payload, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to save cabinet display snapshot for machine {MachineId} to {Path}; checkpoint flow remains non-fatal.",
                machineId,
                path);

            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // swallow - best-effort cleanup
                }
            }
        }
    }

    public Task<PersistentStoreHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!Directory.Exists(rootDirectory))
            {
                return Task.FromResult(new PersistentStoreHealth(
                    IsReady: false,
                    IsDegraded: true,
                    Description: $"Persistent state directory missing: {rootDirectory}.",
                    LastSuccessfulCheckpointUtc: lastSuccessfulCheckpointUtc,
                    LastError: "Directory missing"));
            }

            // Touch a tiny probe file to confirm write access. Cheap on GCS Fuse mounts as well.
            var probePath = Path.Combine(rootDirectory, ".health-probe");
            File.WriteAllText(probePath, DateTime.UtcNow.ToString("O"));

            return Task.FromResult(new PersistentStoreHealth(
                IsReady: true,
                IsDegraded: false,
                Description: $"File-backed snapshot store healthy at {rootDirectory}.",
                LastSuccessfulCheckpointUtc: lastSuccessfulCheckpointUtc ?? DateTimeOffset.UtcNow,
                LastError: null));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "File-backed snapshot store health check failed at {Directory}.", rootDirectory);
            return Task.FromResult(new PersistentStoreHealth(
                IsReady: false,
                IsDegraded: true,
                Description: $"File-backed snapshot store unavailable at {rootDirectory}.",
                LastSuccessfulCheckpointUtc: lastSuccessfulCheckpointUtc,
                LastError: ex.Message));
        }
    }

    private string BuildDisplaySnapshotPath(int machineId)
    {
        var prefix = options.Value.DisplaySnapshotKeyPrefix;
        // Avoid accidental path traversal from external-supplied prefixes by taking only the final segment.
        var safePrefix = string.IsNullOrWhiteSpace(prefix)
            ? "cabinet-display"
            : prefix.Replace(':', '-').TrimEnd('-', '/', '\\');
        if (string.IsNullOrEmpty(safePrefix))
        {
            safePrefix = "cabinet-display";
        }

        var fileName = $"{safePrefix}-machine-{machineId}{DisplaySnapshotExtension}";
        return Path.Combine(rootDirectory, DisplaySnapshotFolder, fileName);
    }

    private void TryEnsureDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to ensure persistent state directory {Directory}.", directory);
        }
    }
}
