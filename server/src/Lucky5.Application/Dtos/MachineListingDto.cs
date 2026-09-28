namespace Lucky5.Application.Dtos;

// Lobby payloads are occupancy-only BY DESIGN (Ai9 Finding 2 class fix):
// they intentionally carry NO occupant identity — no OccupiedByUsername and no
// OccupantUserId/pseudo-id. "Who is at which machine" is player-tracking data and
// is revealed only to clients that joined that machine as a spectator
// (JoinMachineAsSpectator), never on the floor list. Do not re-add identity fields here.

public sealed record LobbyMachineInfo(int MachineId, bool IsOccupied, int SpectatorCount, int IdleSecondsRemaining = 0, DateTime? ReservedUntilUtc = null);

public sealed record MachineListingDto(
    int Id,
    string Name,
    bool IsOpen,
    decimal MinBet,
    decimal MaxBet,
    decimal BetIncrement = 100m,
    bool IsOccupied = false,
    DateTime? ReservedUntilUtc = null,
    int IdleSecondsRemaining = 0,
    int SpectatorCount = 0);
