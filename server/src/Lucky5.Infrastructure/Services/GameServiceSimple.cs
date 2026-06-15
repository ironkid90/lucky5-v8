namespace Lucky5.Infrastructure.Services;

using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Lucky5.Infrastructure.Data.Repositories;

// Kept as a compatibility shim while the repo converges on GameService again.
public sealed class GameServiceSimple : IGameService
{
    private readonly GameService inner;

    public GameServiceSimple(
        InMemoryDataStore store,
        IEntropyGenerator entropyGenerator,
        IPersistentStateStore persistentStateStore)
    {
        _ = persistentStateStore;
        inner = new GameService(new InMemoryDataStoreAdapter(store), entropyGenerator, new InMemoryMachineStateCache(new MachineCacheTtlOptions()));
    }

    public Task<IReadOnlyList<string>> GetGamesAsync(CancellationToken cancellationToken) => inner.GetGamesAsync(cancellationToken);
    public Task<PlayerLobbyDto> GetLobbyAsync(Guid userId, CancellationToken cancellationToken) => inner.GetLobbyAsync(userId, cancellationToken);
    public Task<IReadOnlyList<MachineListingDto>> GetMachinesAsync(CancellationToken cancellationToken) => inner.GetMachinesAsync(cancellationToken);
    public Task<DefaultRulesDto> GetDefaultRulesAsync(CancellationToken cancellationToken) => inner.GetDefaultRulesAsync(cancellationToken);
    public Task<MachineSessionDto> GetMachineSessionAsync(Guid userId, int machineId, CancellationToken cancellationToken) => inner.GetMachineSessionAsync(userId, machineId, cancellationToken);
    public Task<MachineSessionDto> CashInAsync(Guid userId, int machineId, decimal amount, CancellationToken cancellationToken) => inner.CashInAsync(userId, machineId, amount, cancellationToken);
    public Task<MachineSessionDto> CashOutAsync(Guid userId, int machineId, CancellationToken cancellationToken) => inner.CashOutAsync(userId, machineId, cancellationToken);
    public Task<CabinetSnapshotDto> GetCabinetSnapshotAsync(Guid userId, int machineId, CancellationToken cancellationToken) => inner.GetCabinetSnapshotAsync(userId, machineId, cancellationToken);
    public Task<CabinetCommandResultDto> SubmitCabinetCommandAsync(Guid userId, CabinetCommandDto command, CancellationToken cancellationToken) => inner.SubmitCabinetCommandAsync(userId, command, cancellationToken);
    public Task<CabinetReplayDto> GetCabinetReplayAsync(Guid userId, int machineId, long lastStateVersion, long lastSequenceNumber, CancellationToken cancellationToken) => inner.GetCabinetReplayAsync(userId, machineId, lastStateVersion, lastSequenceNumber, cancellationToken);
    public Task<ActiveRoundStateDto?> GetActiveRoundAsync(Guid userId, int machineId, CancellationToken cancellationToken) => inner.GetActiveRoundAsync(userId, machineId, cancellationToken);
    public Task<object> GetMachineStateAsync(int machineId, CancellationToken cancellationToken) => inner.GetMachineStateAsync(machineId, cancellationToken);
    public Task<DealResultDto> DealAsync(Guid userId, DealRequest request, CancellationToken cancellationToken) => inner.DealAsync(userId, request, cancellationToken);
    public Task<DrawResultDto> DrawAsync(Guid userId, DrawRequest request, CancellationToken cancellationToken) => inner.DrawAsync(userId, request, cancellationToken);
    public Task<DoubleUpResultDto> StartDoubleUpAsync(Guid userId, Guid roundId, CancellationToken cancellationToken) => inner.StartDoubleUpAsync(userId, roundId, cancellationToken);
    public Task<DoubleUpResultDto> SwitchDealerAsync(Guid userId, Guid roundId, CancellationToken cancellationToken) => inner.SwitchDealerAsync(userId, roundId, cancellationToken);
    public Task<DoubleUpResultDto> SwapDoubleUpCardAsync(Guid userId, Guid roundId, int swapPosition, CancellationToken cancellationToken) => inner.SwapDoubleUpCardAsync(userId, roundId, swapPosition, cancellationToken);
    public Task<DoubleUpResultDto> GuessDoubleUpAsync(Guid userId, Guid roundId, string guess, CancellationToken cancellationToken) => inner.GuessDoubleUpAsync(userId, roundId, guess, cancellationToken);
    public Task<DoubleUpResultDto> CashoutDoubleUpAsync(Guid userId, Guid roundId, CancellationToken cancellationToken) => inner.CashoutDoubleUpAsync(userId, roundId, cancellationToken);
    public Task<DoubleUpResultDto> TakeHalfAsync(Guid userId, Guid roundId, CancellationToken cancellationToken) => inner.TakeHalfAsync(userId, roundId, cancellationToken);
    public Task<JackpotInfoDto> ChangeJackpotRankAsync(int machineId, int rank, CancellationToken cancellationToken) => inner.ChangeJackpotRankAsync(machineId, rank, cancellationToken);
    public Task<object> ResetMachineAsync(Guid userId, int machineId, CancellationToken cancellationToken) => inner.ResetMachineAsync(userId, machineId, cancellationToken);
}
