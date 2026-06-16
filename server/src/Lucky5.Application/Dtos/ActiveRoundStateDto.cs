namespace Lucky5.Application.Dtos;

/// <summary>
/// Read model returned by GetActiveRoundAsync.  Allows the Flutter client to
/// reconstruct the exact visual state after a disconnect mid-round.
/// </summary>
public sealed record ActiveRoundStateDto(
    Guid RoundId,
    int MachineId,
    decimal BetAmount,

    /// <summary>
    /// "Dealt"  – cards have been dealt, player must choose holds / draw.
    /// "Drawn"  – draw completed, optional double-up is pending.
    /// "DoubleUp" – player is mid-gamble session.
    /// </summary>
    string Phase,

    string HandRank,

    IReadOnlyList<PokerCardDto> Cards,

    IReadOnlyList<PokerCardDto> ResultCards,

    /// <summary>Indexes that were held when the disconnect occurred (Dealt phase only).</summary>
    IReadOnlyList<int> HeldIndexes,

    decimal PendingWinAmount,

    bool DoubleUpAvailable,

    bool TakeHalfUsed,

    DoubleUpStateDto? DoubleUpSession);

/// <summary>
/// Snapshot of the Lucky5 double-up session for reconnect hydration.
/// CardTrail is presentation-only data for the cabinet viewport and does not
/// influence engine outcomes.
/// </summary>
public sealed record DoubleUpStateDto(
    PokerCardDto DealerCard,
    int CurrentAmount,
    int SwitchesRemaining,
    bool IsNoLoseActive,
    int LuckyMultiplier,
    int CurrentRoundIndex = 0,
    IReadOnlyList<PokerCardDto>? CardTrail = null,
    bool IsLucky5Active = false,
    string? BoardHandRank = null,
    decimal BoardBonusAmount = 0,
    decimal CurrentBonusAmount = 0,
    int SlotIndex = 0);
