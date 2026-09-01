namespace Lucky5.Application.Dtos;

public sealed record LobbyMachineInfo(int MachineId, bool IsOccupied, int? OccupantUserId, int SpectatorCount, string? OccupiedByUsername = null, int IdleSecondsRemaining = 0, DateTime? ReservedUntilUtc = null);

public sealed record MachineListingDto(
    int Id,
    string Name,
    bool IsOpen,
    decimal MinBet,
    decimal MaxBet,
    decimal BetIncrement = 100m,
    bool IsOccupied = false,
    string? OccupiedByUsername = null,
    DateTime? ReservedUntilUtc = null,
    int IdleSecondsRemaining = 0,
    int SpectatorCount = 0);
