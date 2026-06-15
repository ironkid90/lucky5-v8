namespace Lucky5.Infrastructure.Persistence;

public sealed class PersistentStateCheckpointOptions
{
    public static readonly TimeSpan DefaultCheckpointInterval = TimeSpan.FromSeconds(10);
    public const string DefaultDisplaySnapshotKeyPrefix = "lucky5:cabinet-display:v1:machine:";

    public TimeSpan CheckpointInterval { get; set; } = DefaultCheckpointInterval;
    public bool GracefulDegradationEnabled { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string SnapshotKey { get; set; } = "lucky5:persistent-state:v2";
    public string DisplaySnapshotKeyPrefix { get; set; } = DefaultDisplaySnapshotKeyPrefix;
}
