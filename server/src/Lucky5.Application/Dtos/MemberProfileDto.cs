namespace Lucky5.Application.Dtos;

public sealed record MemberProfileDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string FullName,
    string Email,
    string PhoneNumber,
    DateTime? DateOfBirth,
    decimal WalletBalance,
    decimal Credit,
    int TotalWins,
    int? AgentId,
    string GeneratedID,
    decimal MinimumOut,
    DateTime? BonusDate,
    int BonusRechargeCount,
    DateTime LastSeenUtc,
    string Role = "player");
