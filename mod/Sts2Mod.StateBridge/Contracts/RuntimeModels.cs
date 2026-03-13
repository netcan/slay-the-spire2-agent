namespace Sts2Mod.StateBridge.Contracts;

public sealed record RuntimeCard(string CardId, string Name, int Cost, bool Playable = true);

public sealed record RuntimePlayerState(
    int Hp,
    int MaxHp,
    int Block,
    int Energy,
    int Gold,
    IReadOnlyList<RuntimeCard> Hand,
    int DrawPile,
    int DiscardPile,
    int ExhaustPile,
    IReadOnlyList<string> Relics,
    IReadOnlyList<string> Potions);

public sealed record RuntimeEnemyState(
    string EnemyId,
    string Name,
    int Hp,
    int MaxHp,
    int Block,
    string Intent,
    bool IsAlive = true);

public sealed record RuntimeActionDefinition(
    string Type,
    string Label,
    IReadOnlyDictionary<string, object?> Parameters,
    IReadOnlyList<string>? TargetConstraints = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record RuntimeWindowContext(
    string Phase,
    RuntimePlayerState? Player,
    IReadOnlyList<RuntimeEnemyState> Enemies,
    IReadOnlyList<string> Rewards,
    IReadOnlyList<string> MapNodes,
    bool Terminal,
    IReadOnlyDictionary<string, object?> Metadata,
    IReadOnlyList<RuntimeActionDefinition> Actions);
