using Lucky5.Application.Dtos;

namespace Lucky5.Application.Contracts;

public interface ISpectatorTracker
{
    int GetSpectatorCount(int machineId);
    void AddSpectator(int machineId, string connectionId);
    void RemoveSpectator(int machineId, string connectionId);
    IReadOnlyList<LobbyMachineInfo> GetLobbySnapshot();
}
