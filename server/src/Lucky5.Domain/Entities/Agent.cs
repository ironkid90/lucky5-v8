namespace Lucky5.Domain.Entities;

public sealed class Agent
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public decimal CreditPool { get; set; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
}
