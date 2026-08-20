namespace Lucky5.Domain.Entities;

public sealed class TermsDocument
{
    public string Version { get; set; } = "1.0";
    public string BodyMarkdown { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
