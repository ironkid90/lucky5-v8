namespace Lucky5.Domain.Game.CleanRoom;

/// <summary>
/// A generic double-up session interface that defines the contract across all video poker cabinet variants.
/// </summary>
public interface IDoubleUpSession
{
    int CurrentAmount { get; }
    int MachineCreditBaseline { get; }
    ulong SeedRoot { get; }
    CleanRoomCard DealerCard { get; }
    CleanRoomCard[]? CurrentBoardCards { get; }
    int LastBoardBonusAmount { get; }
    int BoardBonusTotal { get; }
    bool CurrentBoardComplete { get; }
    HandCategory? BoardHandRank { get; }
}
