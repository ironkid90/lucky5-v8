namespace Lucky5.Application.Dtos;

using System.Text.Json.Serialization;

public sealed record CabinetSnapshotDto(
    [property: JsonPropertyName("schema_version")]
    string SchemaVersion,
    [property: JsonPropertyName("state_version")]
    long StateVersion,
    [property: JsonPropertyName("sequence_number")]
    long SequenceNumber,
    [property: JsonPropertyName("server_time_utc")]
    DateTime ServerTimeUtc,
    CabinetSessionStateDto Session,
    CabinetMachineStateDto Machine,
    CabinetVariantRefDto Variant,
    [property: JsonPropertyName("game_state")]
    string GameState,
    CabinetCreditsDto Credits,
    CabinetHandDto Hand,
    CabinetEvaluationDto Evaluation,
    [property: JsonPropertyName("double_up")]
    CabinetDoubleUpDto DoubleUp,
    CabinetJackpotDto Jackpot,
    IReadOnlyList<CabinetButtonStateDto> Buttons,
    CabinetPresentationStateDto Presentation,
    CabinetRecoveryStateDto Recovery,
    [property: JsonPropertyName("message_type")]
    string MessageType = "cabinet_snapshot");

public sealed record CabinetSessionStateDto(
    [property: JsonPropertyName("session_id")]
    Guid SessionId,
    [property: JsonPropertyName("authenticated_user_id")]
    string AuthenticatedUserId,
    [property: JsonPropertyName("machine_id")]
    int MachineId,
    [property: JsonPropertyName("is_machine_closed")]
    bool IsMachineClosed,
    [property: JsonPropertyName("can_cash_out")]
    bool CanCashOut,
    string Visibility,
    [property: JsonPropertyName("started_at_utc")]
    DateTime StartedAtUtc,
    [property: JsonPropertyName("last_seen_utc")]
    DateTime LastSeenUtc);

public sealed record CabinetMachineStateDto(
    [property: JsonPropertyName("machine_id")]
    int MachineId,
    string Name,
    [property: JsonPropertyName("is_open")]
    bool IsOpen,
    [property: JsonPropertyName("min_bet")]
    string MinBet,
    [property: JsonPropertyName("max_bet")]
    string MaxBet,
    [property: JsonPropertyName("machine_serial")]
    string MachineSerial,
    [property: JsonPropertyName("machine_serie")]
    string MachineSerie,
    [property: JsonPropertyName("machine_kent")]
    string MachineKent,
    [property: JsonPropertyName("first_recharge_credit")]
    string FirstRechargeCredit,
    [property: JsonPropertyName("second_recharge_credit")]
    string SecondRechargeCredit,
    [property: JsonPropertyName("first_recharge_bonus")]
    string FirstRechargeBonus,
    [property: JsonPropertyName("second_recharge_bonus")]
    string SecondRechargeBonus,
    IReadOnlyDictionary<string, decimal> Paytable);

public sealed record CabinetVariantRefDto(
    [property: JsonPropertyName("variant_id")]
    string VariantId,
    [property: JsonPropertyName("variant_schema_version")]
    string VariantSchemaVersion,
    [property: JsonPropertyName("paytable_hash")]
    string PaytableHash,
    [property: JsonPropertyName("display_name")]
    string DisplayName,
    [property: JsonPropertyName("cabinet_skin_id")]
    string CabinetSkinId,
    [property: JsonPropertyName("presentation_profile_id")]
    string PresentationProfileId);

public sealed record CabinetHandDto(
    IReadOnlyList<CabinetCardDto> Cards,
    [property: JsonPropertyName("result_cards")]
    IReadOnlyList<CabinetCardDto> ResultCards,
    [property: JsonPropertyName("held_indexes")]
    IReadOnlyList<int> HeldIndexes,
    [property: JsonPropertyName("round_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? RoundId = null,
    [property: JsonPropertyName("advised_holds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<int>? AdvisedHolds = null);

public sealed record CabinetCardDto(
    string Code,
    string Rank,
    string Suit,
    [property: JsonPropertyName("face_up")]
    bool FaceUp,
    bool Held = false,
    [property: JsonPropertyName("asset_key")]
    string AssetKey = "");

public sealed record CabinetEvaluationDto(
    [property: JsonPropertyName("hand_rank")]
    string HandRank,
    [property: JsonPropertyName("win_amount")]
    string WinAmount,
    [property: JsonPropertyName("jackpot_won")]
    string JackpotWon,
    [property: JsonPropertyName("double_up_available")]
    bool DoubleUpAvailable,
    string Message);

public sealed record CabinetDoubleUpDto(
    bool Active,
    [property: JsonPropertyName("current_amount")]
    string CurrentAmount,
    [property: JsonPropertyName("switches_remaining")]
    int SwitchesRemaining,
    [property: JsonPropertyName("is_no_lose_active")]
    bool IsNoLoseActive,
    [property: JsonPropertyName("is_lucky5_active")]
    bool IsLucky5Active,
    [property: JsonPropertyName("current_round_index")]
    int CurrentRoundIndex,
    string Status,
    [property: JsonPropertyName("round_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? RoundId = null,
    [property: JsonPropertyName("dealer_card"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CabinetCardDto? DealerCard = null,
    [property: JsonPropertyName("challenger_card"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CabinetCardDto? ChallengerCard = null,
    [property: JsonPropertyName("card_trail"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CabinetCardDto>? CardTrail = null,
    [property: JsonPropertyName("lucky_multiplier")]
    int LuckyMultiplier = 1,
    [property: JsonPropertyName("board_hand_rank"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BoardHandRank = null,
    [property: JsonPropertyName("board_bonus_amount")]
    string BoardBonusAmount = "0",
    [property: JsonPropertyName("current_bonus_amount")]
    string CurrentBonusAmount = "0",
    [property: JsonPropertyName("slot_index")]
    int SlotIndex = 0);

public sealed record CabinetCreditsDto(
    [property: JsonPropertyName("machine_credits")]
    string MachineCredits,
    [property: JsonPropertyName("wallet_balance")]
    string WalletBalance,
    [property: JsonPropertyName("credit_balance")]
    string CreditBalance,
    string Stake,
    [property: JsonPropertyName("total_cash_in")]
    string TotalCashIn,
    [property: JsonPropertyName("cash_out_threshold")]
    string CashOutThreshold,
    [property: JsonPropertyName("pending_win_amount")]
    string PendingWinAmount);

public sealed record CabinetJackpotDto(
    [property: JsonPropertyName("full_house")]
    string FullHouse,
    [property: JsonPropertyName("full_house_rank")]
    int FullHouseRank,
    [property: JsonPropertyName("four_of_a_kind_a")]
    string FourOfAKindA,
    [property: JsonPropertyName("four_of_a_kind_b")]
    string FourOfAKindB,
    [property: JsonPropertyName("active_four_of_a_kind_slot")]
    string ActiveFourOfAKindSlot,
    [property: JsonPropertyName("straight_flush")]
    string StraightFlush);

public sealed record CabinetButtonStateDto(
    string Id,
    bool Enabled,
    bool Visible,
    bool Pressed = false,
    string Reason = "");

public sealed record CabinetPresentationStateDto(
    [property: JsonPropertyName("layout_profile")]
    string LayoutProfile,
    [property: JsonPropertyName("skin_id")]
    string SkinId,
    string Message,
    [property: JsonPropertyName("message_tone")]
    string MessageTone,
    [property: JsonPropertyName("pacing_profile")]
    string PacingProfile,
    IReadOnlyList<string> Effects,
    [property: JsonPropertyName("bonus"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CabinetBonusPresentationDto? Bonus = null);

public sealed record CabinetBonusPresentationDto(
    bool Active,
    string Kind,
    [property: JsonPropertyName("card"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CabinetCardDto? Card,
    string Amount,
    [property: JsonPropertyName("free_game_count")]
    int FreeGameCount,
    string Message);

public sealed record CabinetRecoveryStateDto(
    bool Connected,
    [property: JsonPropertyName("commands_allowed")]
    bool CommandsAllowed,
    [property: JsonPropertyName("requires_full_snapshot")]
    bool RequiresFullSnapshot,
    [property: JsonPropertyName("last_applied_state_version")]
    long LastAppliedStateVersion,
    [property: JsonPropertyName("last_applied_sequence_number")]
    long LastAppliedSequenceNumber,
    string Reason);

public sealed record CabinetEventDto(
    [property: JsonPropertyName("message_type")]
    string MessageType,
    [property: JsonPropertyName("schema_version")]
    string SchemaVersion,
    [property: JsonPropertyName("event_id")]
    Guid EventId,
    [property: JsonPropertyName("event_type")]
    string EventType,
    [property: JsonPropertyName("state_version")]
    long StateVersion,
    IReadOnlyDictionary<string, object?> Payload,
    [property: JsonPropertyName("sequence_number")]
    long SequenceNumber,
    [property: JsonPropertyName("server_time_utc")]
    DateTime ServerTimeUtc);

public sealed record CabinetReplayDto(
    bool ReplayAvailable,
    [property: JsonPropertyName("requires_full_snapshot")]
    bool RequiresFullSnapshot,
    [property: JsonPropertyName("from_sequence_number")]
    long FromSequenceNumber,
    [property: JsonPropertyName("to_sequence_number")]
    long ToSequenceNumber,
    IReadOnlyList<CabinetEventDto> Events,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CabinetSnapshotDto? Snapshot = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CabinetCommandErrorDto? Error = null);

public sealed record CabinetCommandDto(
    [property: JsonPropertyName("message_type")]
    string MessageType,
    [property: JsonPropertyName("schema_version")]
    string SchemaVersion,
    [property: JsonPropertyName("command_id")]
    Guid CommandId,
    [property: JsonPropertyName("command_type")]
    string CommandType,
    [property: JsonPropertyName("session_id")]
    Guid? SessionId,
    [property: JsonPropertyName("machine_id")]
    int MachineId,
    [property: JsonPropertyName("expected_state_version")]
    long ExpectedStateVersion,
    [property: JsonPropertyName("idempotency_key")]
    string IdempotencyKey,
    [property: JsonPropertyName("client_sequence_number")]
    long ClientSequenceNumber,
    [property: JsonPropertyName("sent_at_utc")]
    DateTime SentAtUtc,
    IReadOnlyDictionary<string, object?> Payload,
    DateTime? Timestamp = null);

public sealed record CabinetCommandResultDto(
    [property: JsonPropertyName("message_type")]
    string MessageType,
    [property: JsonPropertyName("schema_version")]
    string SchemaVersion,
    [property: JsonPropertyName("command_id")]
    Guid CommandId,
    [property: JsonPropertyName("idempotency_key")]
    string IdempotencyKey,
    bool Accepted,
    string Status,
    [property: JsonPropertyName("state_version")]
    long StateVersion,
    [property: JsonPropertyName("sequence_number")]
    long SequenceNumber,
    [property: JsonPropertyName("server_time_utc")]
    DateTime ServerTimeUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CabinetSnapshotDto? Snapshot = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CabinetEventDto? Event = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CabinetCommandErrorDto? Error = null);

public sealed record CabinetCommandErrorDto(
    string Code,
    string Message,
    bool Retryable);

public sealed record VariantDefinitionDto(
    [property: JsonPropertyName("schema_version")]
    string SchemaVersion,
    [property: JsonPropertyName("variant_id")]
    string VariantId,
    [property: JsonPropertyName("display_name")]
    string DisplayName,
    string Description,
    [property: JsonPropertyName("variant_schema_version")]
    string VariantSchemaVersion,
    [property: JsonPropertyName("ruleset_version")]
    string RulesetVersion,
    IReadOnlyDictionary<string, object?> Rules,
    IReadOnlyDictionary<string, decimal> Paytable,
    [property: JsonPropertyName("paytable_hash")]
    string PaytableHash,
    [property: JsonPropertyName("rtp_model_version")]
    string RtpModelVersion,
    [property: JsonPropertyName("double_up_profile")]
    DoubleUpProfileDto DoubleUpProfile,
    [property: JsonPropertyName("cabinet_skin")]
    CabinetSkinDto CabinetSkin,
    [property: JsonPropertyName("presentation_profile")]
    PresentationProfileDto PresentationProfile,
    [property: JsonPropertyName("machine_policy")]
    MachinePolicyProfileDto MachinePolicy,
    VariantGovernanceDto Governance);

public sealed record DoubleUpProfileDto(
    bool Enabled,
    [property: JsonPropertyName("max_attempts")]
    int MaxAttempts,
    [property: JsonPropertyName("card_count")]
    int CardCount,
    [property: JsonPropertyName("house_edge")]
    decimal HouseEdge);

public sealed record CabinetSkinDto(
    [property: JsonPropertyName("theme_id")]
    string ThemeId,
    [property: JsonPropertyName("asset_pack")]
    string AssetPack,
    [property: JsonPropertyName("color_palette")]
    IReadOnlyDictionary<string, string> ColorPalette);

public sealed record PresentationProfileDto(
    [property: JsonPropertyName("animation_set")]
    string AnimationSet,
    [property: JsonPropertyName("sound_pack")]
    string SoundPack,
    [property: JsonPropertyName("pacing_config")]
    IReadOnlyDictionary<string, int> PacingConfig);

public sealed record MachinePolicyProfileDto(
    [property: JsonPropertyName("min_bet")]
    decimal MinBet,
    [property: JsonPropertyName("max_bet")]
    decimal MaxBet,
    decimal Denomination,
    [property: JsonPropertyName("rtp_target")]
    decimal RtpTarget);

public sealed record VariantGovernanceDto(
    [property: JsonPropertyName("approved_by")]
    string ApprovedBy,
    [property: JsonPropertyName("approved_at")]
    DateTime ApprovedAt,
    [property: JsonPropertyName("simulation_report_id")]
    string SimulationReportId,
    [property: JsonPropertyName("enabled_for_production")]
    bool EnabledForProduction,
    [property: JsonPropertyName("migration_notes")]
    string MigrationNotes);
