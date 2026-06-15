namespace Lucky5.Application.Dtos;

public sealed record DealResultDto(
    Guid RoundId,
    IReadOnlyList<PokerCardDto> Cards,
    decimal BetAmount,
    decimal WalletBalanceAfterBet,
    JackpotInfoDto? Jackpots = null,
    int[]? AdvisedHolds = null,
    // CardsDealt snapshot fields from live protocol
    int MemberId = 0,
    HandResultDto? Result = null,
    int KentRounds = 0,
    bool InDoubleUp = false,
    PokerCardDto? DoubleUpCard = null,
    bool ShouldReset = false,
    int DealCount = 0,
    bool FullHouseBonus = false,
    decimal CurrentCarre1 = 0,
    decimal CurrentCarre2 = 0,
    decimal CurrentFullHouse = 0,
    decimal CurrentKent = 0,
    decimal CurrentStraightFlush = 0,
    int CarreIndex = 0,
    decimal CurrentStake = 0,
    bool ShouldDeductStake = true,
    bool OfferOccurred = false,
    decimal OfferAmount = 0,
    decimal Credit = 0,
    int GameId = 0,
    decimal? CurrentBonusAmount = null,
    int FreeGameCount = 0,
    bool WasFreeGameRound = false,
    int? Swap10 = null,
    bool AceCard = false,
    int AceMultiplier = 0
)
{
    public decimal MachineCreditsAfterBet => WalletBalanceAfterBet;
}

public sealed record HandResultDto(
    int HandRank,
    string Description,
    decimal WinAmount,
    bool IsWin,
    bool CanWin
);
