namespace Lucky5.Application.Dtos;

public sealed record PendingOtpChallengeDto(string OtpCode, DateTime ExpiresAtUtc);
