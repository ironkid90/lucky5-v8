namespace Lucky5.Domain.Entities;

public sealed class Offer
{
    public int Id { get; init; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal BonusAmount { get; set; }
}
