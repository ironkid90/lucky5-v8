namespace Lucky5.Infrastructure.ArcadePersistence;

public sealed class SystemIntegrityValidator
{
    private readonly IMachineAggregateRepository repo;

    public SystemIntegrityValidator(IMachineAggregateRepository repo)
    {
        this.repo = repo;
    }

    public async Task ValidateSystemIntegrityOrThrow(CancellationToken ct)
    {
        // Placeholder: iterate aggregates and validate invariants
        // This will be wired once repository replaces snapshot persistence
        await Task.CompletedTask;
    }
}
