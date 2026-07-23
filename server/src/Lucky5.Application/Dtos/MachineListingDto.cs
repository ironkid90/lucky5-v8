namespace Lucky5.Application.Dtos;

public sealed record MachineListingDto(
    int Id,
    string Name,
    bool IsOpen,
    decimal MinBet,
    decimal MaxBet,
    bool IsOccupied = false,
    string? OccupiedByUsername = null,
    DateTime? ReservedUntilUtc = null,
    int IdleSecondsRemaining = 0,
    int SpectatorCount = 0);
