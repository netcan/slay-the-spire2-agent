namespace Sts2Mod.StateBridge.Contracts;

public static class DecisionPhase
{
    public const string Combat = "combat";
    public const string Reward = "reward";
    public const string Map = "map";
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

public sealed record CardView(string CardId, string Name, int Cost, bool Playable);

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
    IReadOnlyList<string> Potions);

public sealed record EnemyState(
    string EnemyId,
    string Name,
    int Hp,
    int MaxHp,
    int Block,
    string Intent,
    bool IsAlive);

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
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record HealthResponse(
    bool Healthy,
    string ProtocolVersion,
    string ModVersion,
    string GameVersion,
    string ProviderMode,
    bool ReadOnly,
    string Status);

public sealed record ErrorResponse(string ErrorCode, string Message, string? TraceId = null);

public sealed record ExportedWindow(DecisionSnapshot Snapshot, IReadOnlyList<LegalAction> Actions);
