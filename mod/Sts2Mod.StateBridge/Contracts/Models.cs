namespace Sts2Mod.StateBridge.Contracts;

public static class DecisionPhase
{
    public const string Combat = "combat";
    public const string Reward = "reward";
    public const string Map = "map";
    public const string Menu = "menu";
    public const string Terminal = "terminal";
}

public sealed record CompatibilityMetadata(
    string ProtocolVersion,
    string ModVersion,
    string GameVersion,
    string ProviderMode,
    bool ReadOnly,
    bool Ready,
    string? Notes = null);

public sealed record PowerView(
    string PowerId,
    string Name,
    int? Amount = null,
    string? Description = null,
    string? CanonicalPowerId = null);

public sealed record RunMapState(
    string? CurrentCoord = null,
    string? CurrentNodeType = null,
    IReadOnlyList<string>? ReachableNodes = null,
    string? Source = null);

public sealed record RunState(
    int? Act = null,
    int? Floor = null,
    string? CurrentRoomType = null,
    string? CurrentLocationType = null,
    int? CurrentActIndex = null,
    int? AscensionLevel = null,
    RunMapState? Map = null);

public sealed record CardView(
    string CardId,
    string Name,
    int Cost,
    bool Playable,
    string? InstanceCardId = null,
    string? CanonicalCardId = null,
    string? Description = null,
    int? CostForTurn = null,
    bool? Upgraded = null,
    string? TargetType = null,
    string? CardType = null,
    string? Rarity = null,
    IReadOnlyList<string>? Traits = null,
    IReadOnlyList<string>? Keywords = null);

public sealed record PlayerState(
    int Hp,
    int MaxHp,
    int Block,
    int Energy,
    int Gold,
    IReadOnlyList<CardView> Hand,
    int DrawPile,
    int DiscardPile,
    int ExhaustPile,
    IReadOnlyList<string> Relics,
    IReadOnlyList<string> Potions,
    IReadOnlyList<PowerView>? Powers = null);

public sealed record EnemyState(
    string EnemyId,
    string Name,
    int Hp,
    int MaxHp,
    int Block,
    string Intent,
    bool IsAlive,
    string? InstanceEnemyId = null,
    string? CanonicalEnemyId = null,
    string? IntentRaw = null,
    string? IntentType = null,
    int? IntentDamage = null,
    int? IntentHits = null,
    int? IntentBlock = null,
    IReadOnlyList<string>? IntentEffects = null,
    IReadOnlyList<PowerView>? Powers = null);

public sealed record LegalAction(
    string ActionId,
    string Type,
    string Label,
    IReadOnlyDictionary<string, object?> Params,
    IReadOnlyList<string> TargetConstraints,
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record DecisionSnapshot(
    string SessionId,
    string DecisionId,
    int StateVersion,
    string Phase,
    PlayerState? Player,
    IReadOnlyList<EnemyState> Enemies,
    IReadOnlyList<string> Rewards,
    IReadOnlyList<string> MapNodes,
    bool Terminal,
    CompatibilityMetadata Compatibility,
    IReadOnlyDictionary<string, object?> Metadata,
    RunState? RunState = null);

public sealed record HealthResponse(
    bool Healthy,
    string ProtocolVersion,
    string ModVersion,
    string GameVersion,
    string ProviderMode,
    bool ReadOnly,
    string Status);

public sealed record ActionRequest(
    string DecisionId,
    string? ActionId,
    string? ActionType,
    IReadOnlyDictionary<string, object?> Params,
    string? RequestId = null);

public sealed record ActionResponse(
    string RequestId,
    string DecisionId,
    string? ActionId,
    string Status,
    string? ErrorCode,
    string Message,
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record ErrorResponse(string ErrorCode, string Message, string? TraceId = null);

public sealed record ExportedWindow(DecisionSnapshot Snapshot, IReadOnlyList<LegalAction> Actions);
