namespace Lucky5.Domain.Entities;

public sealed class MemberProfile
{
    public Guid UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public decimal WalletBalance { get; set; }
    public decimal Credit { get; set; }
    public int TotalWins { get; set; }
    public int? AgentId { get; set; }
    public string GeneratedID { get; set; } = string.Empty;
    public decimal MinimumOut { get; set; }
    public DateTime? BonusDate { get; set; }
    public int BonusRechargeCount { get; set; }
    public decimal SessionNetLoss { get; set; } // Tracks cumulative loss for pity timer (SuperBonus)
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}
