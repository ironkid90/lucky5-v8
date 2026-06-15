namespace Lucky5.Infrastructure.Persistence;

public sealed record PersistentStoreHealth(
    bool IsReady,
    bool IsDegraded,
    string Description,
    DateTimeOffset? LastSuccessfulCheckpointUtc,
    string? LastError);
