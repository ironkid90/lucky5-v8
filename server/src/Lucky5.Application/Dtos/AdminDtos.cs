namespace Lucky5.Application.Dtos;

public sealed record AdminUserDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string PhoneNumber,
    decimal WalletBalance,
    string Role,
    DateTime CreatedUtc,
    DateTime LastSeenUtc);

public sealed record AdminMachineSessionDto(
    Guid SessionId,
    Guid UserId,
    string Username,
    decimal MachineCredits,
    decimal TotalCashIn,
    bool IsMachineClosed,
    int CounterplayScore,
    DateTime LastUpdatedUtc);

public sealed record AdminMachineDto(
    int MachineId,
    string Name,
    bool IsOpen,
    decimal MinBet,
    decimal MaxBet,
    decimal ObservedRtp,
    decimal TargetRtp,
    decimal BaseRtp,
    string Phase,
    decimal LastPayoutScale,
    int RoundCount,
    int ConsecutiveLosses,
    int RoundsSinceMediumWin,
    int CooldownRemaining,
    decimal NetSinceLastClose,
    int RoundsSinceLucky5Hit,
    DateTime LastRoundUtc,
    decimal JackpotFullHouse,
    int JackpotFullHouseRank,
    decimal JackpotFourOfAKindA,
    decimal JackpotFourOfAKindB,
    int ActiveFourOfAKindSlot,
    decimal JackpotStraightFlush,
    decimal JackpotKent,
    int ActiveRounds,
    int ActivePlayers,
    IReadOnlyList<AdminMachineSessionDto> Sessions);

public sealed record AdminDashboardDto(
    int UserCount,
    int PlayerCount,
    int AdminCount,
    decimal TotalWalletBalance,
    decimal TotalMachineCredits,
    int MachineCount,
    int OpenMachineCount,
    int ClosedMachineCount,
    int ActiveMachineSessions,
    int RecoverableRounds,
    int CabinetDeviceCount,
    int ActiveCabinetDeviceSessions,
    int RevokedCabinetDeviceCount,
    decimal TotalCapitalIn,
    decimal TotalCapitalOut,
    decimal ObservedRtp);

public sealed record AdminActiveRoundDto(
    Guid RoundId,
    Guid UserId,
    string Username,
    int MachineId,
    string MachineName,
    decimal BetAmount,
    string Phase,
    string HandRank,
    decimal WinAmount,
    bool IsCompleted,
    bool IsPayoutSettled,
    bool EnteredDoubleUp,
    DateTime CreatedUtc,
    int AgeSeconds);

public sealed record AdminUserSessionDto(
    Guid SessionId,
    int MachineId,
    string MachineName,
    decimal MachineCredits,
    decimal TotalCashIn,
    bool IsMachineClosed,
    int CounterplayScore,
    DateTime CreatedUtc,
    DateTime LastUpdatedUtc);

public sealed record AdminUserDetailDto(
    AdminUserDto User,
    string Email,
    string FullName,
    decimal Credit,
    int? AgentId,
    string GeneratedId,
    decimal MinimumOut,
    DateTime? BonusDate,
    int BonusRechargeCount,
    decimal SessionNetLoss,
    int TotalWins,
    IReadOnlyList<WalletLedgerEntryDto> RecentLedger,
    IReadOnlyList<AdminUserSessionDto> Sessions,
    IReadOnlyList<AdminActiveRoundDto> ActiveRounds);

public sealed record AdminMachineDetailDto(
    AdminMachineDto Machine,
    string DoorState,
    bool Active,
    bool Ready,
    decimal CapitalIn,
    decimal CapitalOut,
    decimal BaseCapitalOut,
    decimal JackpotCapitalOut,
    decimal DoubleUpCapitalOut,
    decimal MachineAmount,
    decimal CurrentUserAmount,
    decimal OpenAmount,
    decimal Profit,
    IReadOnlyList<AdminActiveRoundDto> ActiveRounds,
    IReadOnlyList<CabinetDeviceDto> CabinetDevices);
