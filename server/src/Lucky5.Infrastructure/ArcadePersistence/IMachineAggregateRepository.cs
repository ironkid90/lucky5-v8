namespace Lucky5.Infrastructure.ArcadePersistence;

public interface IMachineAggregateRepository
{
    Task<MachineSessionAggregate?> GetAsync(Guid userId, int machineId, CancellationToken ct);
    Task SaveAsync(MachineSessionAggregate aggregate, long expectedVersion, CancellationToken ct);
}
