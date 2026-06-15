namespace Lucky5.Application.Dtos;

public sealed record MachineSessionDto(
    Guid SessionId,
    int MachineId,
    decimal MachineCredits,
    decimal TotalCashIn,
    decimal CashOutThreshold,
    bool CanCashOut,
    bool IsMachineClosed,
    decimal WalletBalance,
    MachineTransparencyDto? Transparency = null);
