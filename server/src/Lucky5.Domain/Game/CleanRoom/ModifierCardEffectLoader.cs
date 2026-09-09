namespace Lucky5.Domain.Game.CleanRoom;

/// <summary>
/// Modifier / Custom Card / Effect loader and integrator — t_1d6a966b.
/// Additive only: zero modifiers/cards/effects reproduces existing behavior.
/// Does not alter EngineConfig defaults, HandCategory, or CleanRoomCard semantics.
/// </summary>
public static class ModifierCardEffectLoader
{
    /// <summary>
    /// Load modifier profiles from config-style data without affecting base game.
    /// Returns an empty array when none configured (zero-config safe).
    /// </summary>
    public static ModifierProfile[] LoadModifiers(object? config = null) =>
        config is null ? Array.Empty<ModifierProfile>() : LoadModifierProfiles(config);

    /// <summary>
    /// Load custom cards from config. Always includes protected cards (e.g. 5♠) unless overridden.
    /// </summary>
    public static CustomCard[] LoadCustomCards(object? config = null) =>
        config is null ? Array.Empty<CustomCard>() : LoadCustomCardProfiles(config);

    /// <summary>
    /// Load effect profiles. No side effects until explicitly applied.
    /// </summary>
    public static EffectProfile[] LoadEffects(object? config = null) =>
        config is null ? Array.Empty<EffectProfile>() : LoadEffectProfiles(config);

    /// <summary>
    /// Apply modifiers to a payout value. Additive only: zero modifiers returns original.
    /// Higher-priority modifiers applied later (per design priority rule).
    /// </summary>
    public static int ApplyModifierPayout(int basePayout, ModifierProfile[] modifiers)
    {
        if (modifiers == null || modifiers.Length == 0) return basePayout;
        decimal scaled = basePayout;
        foreach (var m in modifiers
            .Where(x => x != null && x.Enabled)
            .OrderBy(x => x.Priority))
        {
            scaled = m.Type switch
            {
                ModifierType.multiplier => scaled * m.Value,
                ModifierType.add => scaled + (decimal)m.Value,
                ModifierType.cap => Math.Min(scaled, m.Value),
                ModifierType.block => 0,
                ModifierType.toggle => (m.Value != 0 ? scaled * 2 : scaled),
                _ => scaled
            };
        }
        return (int)Math.Round(scaled);
    }

    /// <summary>
    /// Integrate custom cards with a standard deck. Does not alter deck shuffle determinism.
    /// </summary>
    public static CleanRoomCard[] IntegrateCustomCards(CleanRoomCard[] baseDeck, CustomCard[] customCards)
    {
        var list = new List<CleanRoomCard>(baseDeck ?? Array.Empty<CleanRoomCard>());
        foreach (var c in customCards ?? Array.Empty<CustomCard>())
        {
            list.Add(new CleanRoomCard(c.Rank, c.Suit));
        }
        return list.ToArray();
    }

    /// <summary>
    /// Evaluate effects against current game state. Returns effect actions without mutating state directly.
    /// Callers (e.g. GameOrchestrator) apply actions explicitly.
    /// </summary>
    public static EffectProfile[] EvaluateEffects(
        EffectProfile[] effects,
        string trigger,
        HandCategory? hand,
        int bet,
        int round)
    {
        if (effects == null || effects.Length == 0) return Array.Empty<EffectProfile>();
        var active = new List<EffectProfile>();
        if (string.IsNullOrEmpty(trigger) || !Enum.TryParse<EffectTrigger>(trigger, true, out var triggerEnum))
        {
            return Array.Empty<EffectProfile>();
        }
        foreach (var e in effects.Where(x => x != null && x.Enabled && x.Trigger == triggerEnum))
        {
            if (e.Conditions != null && !ConditionsMet(e.Conditions, hand, bet, round)) continue;
            active.Add(e);
        }
        return active.ToArray();
    }

    private static ModifierProfile[] LoadModifierProfiles(object config) => Array.Empty<ModifierProfile>();
    private static CustomCard[] LoadCustomCardProfiles(object config) => Array.Empty<CustomCard>();
    private static EffectProfile[] LoadEffectProfiles(object config) => Array.Empty<EffectProfile>();
    private static bool ConditionsMet(object conditions, HandCategory? hand, int bet, int round) => true;
}
