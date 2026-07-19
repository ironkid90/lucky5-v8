namespace Lucky5.Domain.Game.CleanRoom;

public enum DoubleUpStyle
{
    NextCardBigSmall = 0,
    DealerChallenge = 1
}

public sealed record OperatorSettingProfile(
    int? MaxBetCredits = null,
    int? AutoCollectThreshold = null,
    IReadOnlyList<int>? PercentageModeTargets = null,
    bool? AllowsHalfGamble = null,
    bool? SupportsMeters = null);

public sealed record JackpotFeatureProfile(
    HandCategory Category,
    string DisplayName,
    bool RequiresMaxBet = false,
    int? FixedCreditAward = null);

public sealed record CabinetReferenceProfile(
    string Id,
    string DisplayName,
    DoubleUpStyle? DoubleUpStyle = null,
    int? BigThresholdInclusive = null,
    int? SmallThresholdInclusive = null,
    OperatorSettingProfile? OperatorSettings = null,
    IReadOnlyList<JackpotFeatureProfile>? JackpotFeatures = null,
    string Notes = "")
{
    public bool SupportsBonanzaBigSmall =>
        DoubleUpStyle == Lucky5.Domain.Game.CleanRoom.DoubleUpStyle.NextCardBigSmall &&
        BigThresholdInclusive.HasValue &&
        SmallThresholdInclusive.HasValue;

    public JackpotFeatureProfile? GetJackpotFeature(HandCategory category)
        => JackpotFeatures?.FirstOrDefault(feature => feature.Category == category);
}

public static class CabinetReferences
{
    /// <summary>
    /// Bonanza Golden Poker (1981) — the root ICP-1 hardware/software lineage.
    /// Source-derived from the MAME goldnpkr driver and the full_rom.bin disassembly.
    /// </summary>
    public static CabinetReferenceProfile BonanzaGoldenPoker { get; } = new(
        Id: "bonanza-goldnpkr-1981",
        DisplayName: "Bonanza Golden Poker Double Up (1981)",
        DoubleUpStyle: Lucky5.Domain.Game.CleanRoom.DoubleUpStyle.NextCardBigSmall,
        BigThresholdInclusive: 8,
        SmallThresholdInclusive: 6,
        OperatorSettings: new OperatorSettingProfile(
            MaxBetCredits: 10,
            AutoCollectThreshold: 5000,
            PercentageModeTargets: [85, 30, 40, 50],
            AllowsHalfGamble: true,
            SupportsMeters: true),
        JackpotFeatures: [],
        Notes: "Source-derived from the MAME goldnpkr driver: TAKE SCORE vs DOUBLE UP, BIG/SMALL next-card gamble against a fixed 7 threshold, operator percentage modes (0~85%, 1~30%, 2~40%, 3~50% in the long run), timed learn/meter screens, and automatic storage over 5,000 credits.");

    /// <summary>
    /// Bonus Poker (Galanthis, 1984) — direct Galanthis predecessor that shifts paytable
    /// weight toward premium hands (4 of a Kind / Straight Flush). Shares the ICP-1 input
    /// multiplex and operator settings DNA with goldnpkr, but surfaces some options via
    /// DIP switches (Pair of Aces, Payout Mode) rather than NVRAM menus.
    /// </summary>
    public static CabinetReferenceProfile BonusPoker { get; } = new(
        Id: "galanthis-bonuspkr-1984",
        DisplayName: "Bonus Poker (Galanthis, 1984)",
        DoubleUpStyle: Lucky5.Domain.Game.CleanRoom.DoubleUpStyle.NextCardBigSmall,
        BigThresholdInclusive: 8,
        SmallThresholdInclusive: 6,
        OperatorSettings: new OperatorSettingProfile(
            MaxBetCredits: 10,
            AutoCollectThreshold: 5000,
            PercentageModeTargets: [85, 30, 40, 50],
            AllowsHalfGamble: true,
            SupportsMeters: true),
        JackpotFeatures:
        [
            new JackpotFeatureProfile(HandCategory.FourOfAKind, "Four of a Kind Jackpot"),
            new JackpotFeatureProfile(HandCategory.StraightFlush, "Straight Flush Jackpot")
        ],
        Notes: "ROM-derived from the MAME bonuspkr set. 12KB 6502 program at $5000-$7FFF. Paytable line '4 8 10 20 30 50 60 100' heavily weights 4OAK (60) and Straight Flush (100). 'ACE COUNTS HI OR LO' string indicates an ace-low straight option. No joker / 5-of-a-kind evidence in this dump. Percentage/RTP metering is presumed to follow the same goldnpkr-style long-run targets until a cabinet-specific manual or ROM proves otherwise.");

    /// <summary>
    /// Wild Witch (Video Klein, 1992-2001) — the later multi-game / 6T/12T Video Klein
    /// witch family. Parent set ww184a (32KB CPU ROM at $8000-$FFFF) plus three 32KB
    /// graphics ROMs. Switchable between Wild Witch and Witch Game via operator DIP.
    /// </summary>
    public static CabinetReferenceProfile WildWitch { get; } = new(
        Id: "videoklein-wildwitch-1992-2001",
        DisplayName: "Wild Witch (Video Klein, 1992-2001)",
        DoubleUpStyle: Lucky5.Domain.Game.CleanRoom.DoubleUpStyle.NextCardBigSmall,
        BigThresholdInclusive: 8,
        SmallThresholdInclusive: 6,
        OperatorSettings: new OperatorSettingProfile(
            MaxBetCredits: 100,
            AutoCollectThreshold: 5000,
            PercentageModeTargets: [85, 30, 40, 50],
            AllowsHalfGamble: true,
            SupportsMeters: true),
        JackpotFeatures:
        [
            new JackpotFeatureProfile(HandCategory.FourOfAKind, "Four Aces Jackpot (4K-A)", RequiresMaxBet: false),
            new JackpotFeatureProfile(HandCategory.FourOfAKind, "Four 2s-4s Jackpot (4K-B)", RequiresMaxBet: false),
            new JackpotFeatureProfile(HandCategory.StraightFlush, "Straight Flush Jackpot"),
            new JackpotFeatureProfile(HandCategory.RoyalFlush, "Royal Flush Jackpot")
        ],
        Notes: "ROM-derived from the MAME wldwitch set. Parent CPU is ww184a.bin (ver 1.84A, 2001-09-12). Versioned clones run 1.57-SP through 1.84A. DIP SW2 selects Wild Witch vs Witch Game, 6-button vs 12-button controls, max bet 10/20/50/100, minimal hand Two Pairs vs High Pair, and Royal Flush enable. German operator HALT settings for min bet, max payout, and time limits. Reset vector $E500. No joker / 5-of-a-kind strings in the parent program; this is the Video Klein UI/multi-game lineage, distinct from the joker+5oak Lebanese cabinet story. Key Wild Witch rules: 'ACE COUNTS HI OR LO' for straights and double-up, '5 NEVER LOSE WHEN BUYING' (5♠ auto-win in double-up). Uses 53-card deck with joker/wild card. Progressive meters: 4K-A (Aces only), 4K-B (2s-4s), SF, RF. Double-up is BIG/SMALL next-card against fixed threshold 7 with Ace auto-win both ways and 5♠ SafeFail.");

    /// <summary>
    /// Super 98 (MAME: 1998, 3-hands, ICP-1, witchcrd hardware). MAME parent is bsuerte;
    /// it plays three hands simultaneously and has complex protection. This is the ROM
    /// footprint most likely related to the Lebanese 'Robert's Ultimate' cabinets, but
    /// the live cabinet behaviour described by operators (dealer-card double-up, joker,
    /// 5-of-a-Kind jackpot) differs from the stock MAME Super 98 ROM and may be a
    /// localized/modified branch.
    /// </summary>
    public static CabinetReferenceProfile Super98 { get; } = new(
        Id: "super98-witchcrd-1998",
        DisplayName: "Super 98 (3-hands, ICP-1, 1998)",
        DoubleUpStyle: Lucky5.Domain.Game.CleanRoom.DoubleUpStyle.NextCardBigSmall,
        BigThresholdInclusive: 8,
        SmallThresholdInclusive: 6,
        OperatorSettings: new OperatorSettingProfile(
            MaxBetCredits: 10,
            AutoCollectThreshold: 5000,
            PercentageModeTargets: [85, 30, 40, 50],
            AllowsHalfGamble: true,
            SupportsMeters: true),
        JackpotFeatures: [],
        Notes: "MAME entry: GAME(1998, super98, bsuerte, witchcrd, super98, init_super98, ...). Runs on Witch Card hardware, not base goldnpkr. Plays 3 hands at once. The relationship to the Lebanese joker+5oak cabinets is an open research item; treat the live cabinet as a separate Robert's Ultimate lineage until original ROMs are dumped. Do not confuse Super 98 with Video Klein Wild Witch (wldwitch / ww184a).");

    /// <summary>
    /// Robert's Ultimate / Lebanese joker+5oak cabinet — the 'huge jackpot / long meter
    /// count-out' memory that seeded the Lucky5 governor. Clean-room evidence (dealer-card
    /// double-up, 5-of-a-Kind enum placeholder, 'Joker replacements' variant hook,
    /// 5♠/Ace auto-win, SafeFail) suggests this lineage, but original EPROMs have not yet
    /// been dumped. AI9/Flutter implementations are non-authoritative.
    /// </summary>
    public static CabinetReferenceProfile RobertsUltimate { get; } = new(
        Id: "roberts-ultimate-lebanese-joker-5oak",
        DisplayName: "Robert's Ultimate (Lebanese joker + 5-of-a-Kind cabinet)",
        DoubleUpStyle: Lucky5.Domain.Game.CleanRoom.DoubleUpStyle.DealerChallenge,
        OperatorSettings: new OperatorSettingProfile(
            MaxBetCredits: 10,
            AutoCollectThreshold: 5000,
            PercentageModeTargets: [85, 30, 40, 50],
            AllowsHalfGamble: true,
            SupportsMeters: true),
        JackpotFeatures:
        [
            new JackpotFeatureProfile(HandCategory.FiveOfAKind, "Five of a Kind Jackpot", RequiresMaxBet: true)
        ],
        Notes: "Live-cabinet profile based on operator photos/videos and Lucky5 clean-room placeholders. Characterized by joker card, 5-of-a-Kind jackpot, dealer-card BIG/SMALL double-up (tie = loss), 5♠/Ace auto-win, and long meter count-outs. Percentage mode values are assumed to follow the goldnpkr/Super 98 family map (0~85%, 1~30%, 2~40%, 3~50%) until original ROMs confirm otherwise.");

    /// <summary>
    /// Lucky5 clean-room C# engine (v8). This is the current authoritative implementation
    /// in server/src/Lucky5.Domain/Game/CleanRoom. It captures the Lebanese dealer-card
    /// double-up lineage, not the stock goldnpkr fixed-threshold double-up.
    /// </summary>
    public static CabinetReferenceProfile Lucky5CleanRoom { get; } = new(
        Id: "lucky5-cleanroom-v8",
        DisplayName: "Lucky5 (clean-room C# engine, v8)",
        DoubleUpStyle: Lucky5.Domain.Game.CleanRoom.DoubleUpStyle.DealerChallenge,
        OperatorSettings: new OperatorSettingProfile(
            MaxBetCredits: 10,
            AutoCollectThreshold: 5000,
            PercentageModeTargets: [85, 30, 40, 50],
            AllowsHalfGamble: true,
            SupportsMeters: true),
        JackpotFeatures:
        [
            new JackpotFeatureProfile(HandCategory.FourOfAKind, "Four of a Kind A/B bonus pool"),
            new JackpotFeatureProfile(HandCategory.StraightFlush, "Straight Flush jackpot"),
            new JackpotFeatureProfile(HandCategory.FullHouse, "Full House rank-armed jackpot"),
            new JackpotFeatureProfile(HandCategory.Straight, "Kent sequential-straight jackpot")
        ],
        Notes: "Authoritative v8 engine. Standard 52-card deck (53-card joker deck is a future variant). Double-up is dealer-card BIG/SMALL with 5♠ auto-win, Ace auto-win, SafeFail no-lose, and a 5-card progressive board bonus. RTP converges asymptotically over the machine ledger; percentage-mode selector values are treated as long-run target presets aligned with the goldnpkr family documentation.");
}
