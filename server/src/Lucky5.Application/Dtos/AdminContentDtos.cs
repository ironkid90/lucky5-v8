namespace Lucky5.Application.Dtos;

public sealed record AdminOfferDto(int Id, string Title, string Description, decimal BonusAmount);
public sealed record AdminTermsDto(string Version, string BodyMarkdown, DateTime UpdatedUtc);
public sealed record AdminAppSettingDto(string Key, string Value);
