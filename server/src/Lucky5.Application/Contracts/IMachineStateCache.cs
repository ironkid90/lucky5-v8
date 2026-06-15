namespace Lucky5.Application.Contracts;

using Lucky5.Application.Dtos;

public interface IMachineStateCache
{
    Task<ActiveRoundStateDto?> GetActiveRoundAsync(Guid userId, int machineId);
    void SetActiveRound(Guid userId, int machineId, ActiveRoundStateDto? dto);
    void InvalidateActiveRound(Guid userId, int machineId);

    Task<MachineSessionDto?> GetMachineSessionAsync(Guid userId, int machineId);
    void SetMachineSession(Guid userId, int machineId, MachineSessionDto dto);
    void InvalidateMachineSession(Guid userId, int machineId);
}
