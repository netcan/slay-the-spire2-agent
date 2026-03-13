using Sts2Mod.StateBridge.Contracts;
using Sts2Mod.StateBridge.Core;
using System.Text.Json;

namespace Sts2Mod.StateBridge.Extraction;

public abstract class WindowExtractorBase : IWindowExtractor
{
    public abstract string Phase { get; }

    public ExportedWindow Export(RuntimeWindowContext context, BridgeSessionState sessionState)
    {
        sessionState.AdvanceIfNeeded(context.Phase, CreateFingerprint(context));
        var snapshot = BuildSnapshot(context, sessionState);
        var actions = context.Actions.Select(action => new LegalAction(
            sessionState.CreateActionId(action.Type, action.Parameters),
            action.Type,
            action.Label,
            action.Parameters,
            action.TargetConstraints ?? Array.Empty<string>(),
            action.Metadata ?? new Dictionary<string, object?>())).ToArray();
        return new ExportedWindow(snapshot, actions);
    }

    private static string CreateFingerprint(RuntimeWindowContext context)
    {
        var canonical = new
        {
            context.Phase,
            context.Terminal,
            Player = context.Player is null
                ? null
                : new
                {
                    context.Player.Hp,
                    context.Player.MaxHp,
                    context.Player.Block,
                    context.Player.Energy,
                    context.Player.Gold,
                    Hand = context.Player.Hand.Select(card => new { card.CardId, card.Name, card.Cost, card.Playable }).ToArray(),
                    context.Player.DrawPile,
                    context.Player.DiscardPile,
                    context.Player.ExhaustPile,
                    Relics = context.Player.Relics.ToArray(),
                    Potions = context.Player.Potions.ToArray(),
                },
            Enemies = context.Enemies.Select(enemy => new
            {
                enemy.EnemyId,
                enemy.Name,
                enemy.Hp,
                enemy.MaxHp,
                enemy.Block,
                enemy.Intent,
                enemy.IsAlive,
            }).ToArray(),
            Rewards = context.Rewards.ToArray(),
            MapNodes = context.MapNodes.ToArray(),
            Metadata = FilterStableMetadata(context.Metadata),
            Actions = context.Actions.Select(action => new
            {
                action.Type,
                action.Label,
                Parameters = action.Parameters.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value),
                TargetConstraints = action.TargetConstraints?.ToArray() ?? Array.Empty<string>(),
                Metadata = FilterStableMetadata(action.Metadata ?? new Dictionary<string, object?>()),
            }).ToArray(),
        };
        return JsonSerializer.Serialize(canonical);
    }

    private static IReadOnlyDictionary<string, object?> FilterStableMetadata(IReadOnlyDictionary<string, object?> metadata)
    {
        return metadata
            .Where(pair => !IsDiagnosticsKey(pair.Key))
            .OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static bool IsDiagnosticsKey(string key)
    {
        return string.Equals(key, "text_diagnostics", StringComparison.Ordinal) ||
               string.Equals(key, "diagnostics", StringComparison.Ordinal);
    }

    protected DecisionSnapshot BuildSnapshot(RuntimeWindowContext context, BridgeSessionState sessionState)
    {
        return new DecisionSnapshot(
            sessionState.SessionId,
            sessionState.DecisionId,
            sessionState.StateVersion,
            context.Phase,
            context.Player is null ? null : Convert(context.Player),
            context.Enemies.Select(Convert).ToArray(),
            context.Rewards.ToArray(),
            context.MapNodes.ToArray(),
            context.Terminal,
            sessionState.Compatibility,
            BuildMetadata(context));
    }

    protected virtual IReadOnlyDictionary<string, object?> BuildMetadata(RuntimeWindowContext context)
    {
        return new Dictionary<string, object?>(context.Metadata);
    }

    protected static PlayerState Convert(RuntimePlayerState player)
    {
        return new PlayerState(
            player.Hp,
            player.MaxHp,
            player.Block,
            player.Energy,
            player.Gold,
            player.Hand.Select(card => new CardView(card.CardId, card.Name, card.Cost, card.Playable)).ToArray(),
            player.DrawPile,
            player.DiscardPile,
            player.ExhaustPile,
            player.Relics.ToArray(),
            player.Potions.ToArray());
    }

    protected static EnemyState Convert(RuntimeEnemyState enemy)
    {
        return new EnemyState(enemy.EnemyId, enemy.Name, enemy.Hp, enemy.MaxHp, enemy.Block, enemy.Intent, enemy.IsAlive);
    }
}

public sealed class CombatWindowExtractor : WindowExtractorBase
{
    public override string Phase => DecisionPhase.Combat;

    protected override IReadOnlyDictionary<string, object?> BuildMetadata(RuntimeWindowContext context)
    {
        var metadata = new Dictionary<string, object?>(context.Metadata)
        {
            ["supports_targeting"] = true,
            ["window_kind"] = "player_turn"
        };
        return metadata;
    }
}

public sealed class RewardWindowExtractor : WindowExtractorBase
{
    public override string Phase => DecisionPhase.Reward;

    protected override IReadOnlyDictionary<string, object?> BuildMetadata(RuntimeWindowContext context)
    {
        var metadata = new Dictionary<string, object?>(context.Metadata)
        {
            ["reward_count"] = context.Rewards.Count,
            ["window_kind"] = "reward_choice"
        };
        return metadata;
    }
}

public sealed class MapWindowExtractor : WindowExtractorBase
{
    public override string Phase => DecisionPhase.Map;

    protected override IReadOnlyDictionary<string, object?> BuildMetadata(RuntimeWindowContext context)
    {
        var metadata = new Dictionary<string, object?>(context.Metadata)
        {
            ["node_count"] = context.MapNodes.Count,
            ["window_kind"] = "map_choice"
        };
        return metadata;
    }
}

public sealed class TerminalWindowExtractor : WindowExtractorBase
{
    public override string Phase => DecisionPhase.Terminal;

    protected override IReadOnlyDictionary<string, object?> BuildMetadata(RuntimeWindowContext context)
    {
        var metadata = new Dictionary<string, object?>(context.Metadata)
        {
            ["window_kind"] = "terminal"
        };
        return metadata;
    }
}
