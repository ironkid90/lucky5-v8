namespace Lucky5.Application.Dtos;

public sealed record DrawResultDto(Guid RoundId, IReadOnlyList<PokerCardDto> Cards, string HandRank, decimal WinAmount, decimal WalletBalanceAfterRound, decimal JackpotWon = 0, JackpotInfoDto? Jackpots = null, bool DoubleUpAvailable = true, long StateVersion = 0, long SequenceNumber = 0)
{
    public decimal MachineCreditsAfterRound => WalletBalanceAfterRound;
}
