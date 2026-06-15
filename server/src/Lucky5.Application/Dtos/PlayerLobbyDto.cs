namespace Lucky5.Application.Dtos;

public sealed record PlayerLobbyDto(
    Guid UserId,
    string Username,
    decimal WalletBalance,
    decimal Credit,
    IReadOnlyList<PlayerLobbyMachineDto> Machines);

public sealed record PlayerLobbyMachineDto(
    int Id,
    string Name,
    bool IsOpen,
    decimal MinBet,
    decimal MaxBet,
    JackpotInfoDto Jackpots,
    decimal ObservedRtp,
    string Phase,
    int RoundCount,
    MachineSessionDto? Session,
    ActiveRoundStateDto? ActiveRound);
