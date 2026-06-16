namespace Lucky5.Application.Contracts;

public sealed record TokenValidationResult(bool IsValid, Guid UserId, string Role);

public interface ITokenService
{
    string IssueToken(Guid userId, TimeSpan lifetime, string role = "player");
    Task<TokenValidationResult> ValidateTokenAsync(string token);
    Task Revoke(string token);
}
