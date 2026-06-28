namespace Lucky5.Application.Contracts;

using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;

public interface IGameService
{
    Task<IReadOnlyList<string>> GetGamesAsync(CancellationToken cancellationToken);
    Task<PlayerLobbyDto> GetLobbyAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MachineListingDto>> GetMachinesAsync(CancellationToken cancellationToken);
    Task<DefaultRulesDto> GetDefaultRulesAsync(CancellationToken cancellationToken);
    Task<MachineSessionDto> GetMachineSessionAsync(Guid userId, int machineId, CancellationToken cancellationToken);
    Task<MachineSessionDto> CashInAsync(Guid userId, int machineId, decimal amount, CancellationToken cancellationToken);
    Task<MachineSessionDto> CashOutAsync(Guid userId, int machineId, CancellationToken cancellationToken);

    Task<CabinetSnapshotDto> GetCabinetSnapshotAsync(Guid userId, int machineId, CancellationToken cancellationToken);
    Task<CabinetCommandResultDto> SubmitCabinetCommandAsync(Guid userId, CabinetCommandDto command, CancellationToken cancellationToken);
    Task<CabinetReplayDto> GetCabinetReplayAsync(Guid userId, int machineId, long lastStateVersion, long lastSequenceNumber, CancellationToken cancellationToken);
    Task<ActiveRoundStateDto?> GetActiveRoundAsync(Guid userId, int machineId, CancellationToken cancellationToken);
    Task<object> GetMachineStateAsync(int machineId, CancellationToken cancellationToken);

    Task<DealResultDto> DealAsync(Guid userId, DealRequest request, CancellationToken cancellationToken);
    Task<DrawResultDto> DrawAsync(Guid userId, DrawRequest request, CancellationToken cancellationToken);

    Task<DoubleUpResultDto> StartDoubleUpAsync(Guid userId, Guid roundId, CancellationToken cancellationToken);
    Task<DoubleUpResultDto> SwitchDealerAsync(Guid userId, Guid roundId, CancellationToken cancellationToken);
    Task<DoubleUpResultDto> SwapDoubleUpCardAsync(Guid userId, Guid roundId, int swapPosition, CancellationToken cancellationToken);
    Task<DoubleUpResultDto> GuessDoubleUpAsync(Guid userId, Guid roundId, string guess, CancellationToken cancellationToken);
    Task<DoubleUpResultDto> CashoutDoubleUpAsync(Guid userId, Guid roundId, CancellationToken cancellationToken);
    Task<DoubleUpResultDto> TakeHalfAsync(Guid userId, Guid roundId, CancellationToken cancellationToken);

    Task<JackpotInfoDto> ChangeJackpotRankAsync(int machineId, int rank, CancellationToken cancellationToken);
    Task<JackpotInfoDto> ChangeCabinetJackpotRankAsync(Guid userId, int machineId, int rank, CancellationToken cancellationToken);
    Task<object> ResetMachineAsync(Guid userId, int machineId, CancellationToken cancellationToken);
}
