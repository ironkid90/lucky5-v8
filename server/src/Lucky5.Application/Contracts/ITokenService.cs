namespace Lucky5.Application.Contracts;

public interface ITokenService
{
    string IssueToken(Guid userId, TimeSpan lifetime, string role = "player");
    Task<bool> TryValidate(string token, out Guid userId, out string role);
    Task Revoke(string token);
}
