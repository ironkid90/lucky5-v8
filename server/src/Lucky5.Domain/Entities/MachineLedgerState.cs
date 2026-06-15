namespace Lucky5.Domain.Entities;

using Lucky5.Domain.Game.CleanRoom;

public sealed class MachineLedgerState
{
    public int MachineId { get; init; }
    public string MachineSerial { get; set; } = string.Empty;
    public string MachineSerie { get; set; } = string.Empty;
    public string MachineKent { get; set; } = string.Empty;
    public decimal TargetRtp { get; set; } = EngineConfig.Default.TargetRtp;
    public decimal CapitalIn { get; set; }
    public decimal CapitalOut { get; set; }
    public int RoundCount { get; set; }
    public int ColdStreak { get; set; }
    public int HotStreak { get; set; }
    public DistributionMode LastDistributionMode { get; set; } = DistributionMode.Neutral;
    public DateTime LastRoundUtc { get; set; } = DateTime.UtcNow;

    public decimal JackpotFullHouse { get; set; } = EngineConfig.Default.JackpotFullHouseStart;
    public int JackpotFullHouseRank { get; set; } = 14;
    public decimal JackpotFourOfAKindA { get; set; } = EngineConfig.Default.JackpotFourOfAKindStart;
    public decimal JackpotFourOfAKindB { get; set; } = EngineConfig.Default.JackpotFourOfAKindStart;
    public int ActiveFourOfAKindSlot { get; set; }
    public decimal JackpotStraightFlush { get; set; } = EngineConfig.Default.JackpotStraightFlushStart;
    public decimal JackpotKent { get; set; } = EngineConfig.Default.JackpotKentStart;

    public decimal BaseCapitalOut { get; set; }
    public decimal JackpotCapitalOut { get; set; }
    public decimal DoubleUpCapitalOut { get; set; }
    public decimal LastPayoutScale { get; set; } = EngineConfig.Default.DefaultPayoutScale;

    public int ConsecutiveLosses { get; set; }
    public int RoundsSinceMediumWin { get; set; }
    public int CooldownRoundsRemaining { get; set; }

    public decimal NetSinceLastClose { get; set; }
    public int LastCloseRoundNumber { get; set; }
    public WinChannel LastWinChannel { get; set; } = WinChannel.None;
    public int RoundsSinceLucky5Hit { get; set; }

    // Operational fields from live protocol
    public DoorState DoorState { get; set; } = DoorState.Closed;
    public decimal MachineAmount { get; set; }
    public decimal CurrentUserAmount { get; set; }
    public decimal OpenAmount { get; set; }
    public bool CounterStatus { get; set; }
    public bool Active { get; set; } = true;
    public bool Ready { get; set; } = true;
    public decimal OpenedDoubleUpPercentage { get; set; } = 100m;
    public decimal ClosedDoubleUpPercentage { get; set; } = 1m;
    public bool AutoOpenClosePercentage { get; set; }
    public int CarreIndex { get; set; } = 1;
    public int? CurrentMemberId { get; set; }
    public decimal Profit { get; set; }
    public int WinBonusDefaultPokerRulesId { get; set; }
    public decimal WinBonusAmount { get; set; }
    public decimal MaxStraightFlush { get; set; } = 10000000m;
    public decimal MaxCarre1 { get; set; } = 2000000m;
    public decimal MaxCarre2 { get; set; } = 2000000m;
    public decimal MaxFullHouse { get; set; } = 20000000m;
    public decimal MaxKent { get; set; } = 5000000m;
    public decimal MinStraightFlush { get; set; } = 5000000m;
    public decimal MinCarre1 { get; set; }
    public decimal MinCarre2 { get; set; }
    public decimal MinFullHouse { get; set; }
    public decimal MinKent { get; set; }
    public decimal CurrentStraightFlush { get; set; }
    public decimal CurrentCarre1 { get; set; }
    public decimal CurrentCarre2 { get; set; }
    public decimal CurrentFullHouse { get; set; }
    public decimal CurrentKent { get; set; }
    public int KentRounds { get; set; }
    public decimal DoubleUpLimit { get; set; } = 10000m;
    public int FullHouseCardId { get; set; }
    public decimal FirstRechargeCredit { get; set; } = 200000m;
    public decimal FirstRechargeBonus { get; set; } = 15000m;
    public decimal SecondRechargeCredit { get; set; } = 400000m;
    public decimal SecondRechargeBonus { get; set; } = 30000m;

    public decimal ObservedRtp => CapitalIn <= 0m ? TargetRtp : decimal.Round(CapitalOut / CapitalIn, 4);
}

public enum WinChannel
{
    None = 0,
    BaseGame = 1,
    DoubleUp = 2,
    Lucky5 = 3,
    Jackpot = 4
}

public enum DoorState
{
    Closed = 0,
    Open = 1
}
