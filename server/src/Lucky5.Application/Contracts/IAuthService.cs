namespace Lucky5.Application.Contracts;

using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;

public interface IAuthService
{
    Task<(AuthTokens Tokens, MemberProfileDto Profile)> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<(MemberProfileDto Profile, PendingOtpChallengeDto Challenge)> SignupAsync(SignupRequest request, CancellationToken cancellationToken);
    Task<bool> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken cancellationToken);
    Task<PendingOtpChallengeDto?> ResendOtpAsync(ResendOtpRequest request, CancellationToken cancellationToken);
    Task<AuthTokens> RefreshTokenAsync(TokenRefreshRequest request, CancellationToken cancellationToken);
    Task<MemberProfileDto> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WalletLedgerEntryDto>> GetMemberHistoryAsync(Guid userId, CancellationToken cancellationToken);
    Task<WalletLedgerEntryDto> TransferBalanceAsync(Guid userId, TransferRequest request, CancellationToken cancellationToken);
    Task<WalletLedgerEntryDto> MoveWinToBalanceAsync(Guid userId, TransferRequest request, CancellationToken cancellationToken);
    Task<WalletLedgerEntryDto> UpdateCreditAsync(Guid userId, TransferRequest request, CancellationToken cancellationToken);
    Task<WalletLedgerEntryDto> RechargeBonusAsync(Guid userId, decimal rechargeAmount, CancellationToken cancellationToken);
    Task LogoutAsync(string accessToken, CancellationToken cancellationToken);
}
