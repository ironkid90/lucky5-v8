namespace Lucky5.Application.Dtos;

public sealed record DoubleUpResultDto(
    Guid RoundId,
    string Status,
    decimal CurrentAmount,
    decimal WalletBalance,
    PokerCardDto? DealerCard = null,
    PokerCardDto? ChallengerCard = null,
    int SwitchesRemaining = 0,
    bool IsNoLoseActive = false,
    int LuckyMultiplier = 0,
    int CurrentRoundIndex = 0,
    PresentationNoiseDto? Noise = null,
    IReadOnlyList<PokerCardDto>? CardTrail = null,
    bool IsLucky5Active = false,
    // DoubleUpWin snapshot fields from live protocol
    int MemberId = 0,
    bool PickedBig = false,
    decimal NewWinAmount = 0,
    decimal LostAmount = 0,
    int DoubleUpCount = 0,
    bool CanContinue = false,
    bool IsWin = false,
    bool IsEquality = false,
    bool Lucky5 = false,
    bool WinLucky5 = false,
    bool Bonus = false,
    decimal BonusAmount = 0,
    int Opened = 0,
    decimal Credit = 0,
    bool FreeGame = false,
    int FreeGameCount = 0,
    bool BonusCard = false,
    decimal BonusCardAmount = 0,
    decimal? CurrentBonusAmount = null,
    bool AceCard = false,
    int AceMultiplier = 0,
    bool AceMultiplierFired = false,
    int? SwapActivePosition = null,
    int SwapActiveRemaining = 0,
    int GameId = 0
);

public sealed record PresentationNoiseDto(
    int SuspenseMs,
    int RevealMs,
    int FlipFrames,
    int PulseFrames);
