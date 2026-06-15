namespace Lucky5.Application.Dtos;

public sealed record PokerCardDto(
    int CardId,
    string Title,
    string Suit,
    string Rank,
    // Legacy fields for backward compatibility
    string? Code = null
);
