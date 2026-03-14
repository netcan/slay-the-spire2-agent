using Sts2Mod.StateBridge.Configuration;
using Sts2Mod.StateBridge.Contracts;
using Sts2Mod.StateBridge.Core;
using Sts2Mod.StateBridge.Extraction;

namespace Sts2Mod.StateBridge.Providers;

public sealed class FixtureGameStateProvider : IGameStateProvider
{
    private readonly BridgeOptions _options;
    private readonly BridgeSessionState _sessionState;
    private readonly Dictionary<string, RuntimeWindowContext> _windows;
    private readonly Dictionary<string, RuntimeWindowContext> _rewardWindows;
    private readonly Dictionary<string, IWindowExtractor> _extractors;
    private string _currentPhase = DecisionPhase.Combat;
    private int _rewardStage;

    public FixtureGameStateProvider(BridgeOptions options)
    {
        _options = options;
        _sessionState = new BridgeSessionState(options);
        _extractors = new IWindowExtractor[]
        {
            new CombatWindowExtractor(),
            new RewardWindowExtractor(),
            new MapWindowExtractor(),
            new TerminalWindowExtractor(),
        }.ToDictionary(extractor => extractor.Phase, StringComparer.OrdinalIgnoreCase);
        _windows = CreateWindows();
        _rewardWindows = CreateRewardWindows();
    }

    public HealthResponse GetHealth()
    {
        return new HealthResponse(
            Healthy: true,
            ProtocolVersion: _options.ProtocolVersion,
            ModVersion: _options.ModVersion,
            GameVersion: _options.GameVersion,
            ProviderMode: _options.ProviderMode,
            ReadOnly: _options.ReadOnly,
            Status: "ok");
    }

    public DecisionSnapshot GetSnapshot(string? requestedPhase = null)
    {
        return Export(requestedPhase).Snapshot;
    }

    public IReadOnlyList<LegalAction> GetActions(string? requestedPhase = null)
    {
        return Export(requestedPhase).Actions;
    }

    public ActionResponse ApplyAction(ActionRequest request)
    {
        if (_options.ReadOnly)
        {
            return Reject(request, "read_only", "Bridge is running in read-only mode.");
        }

        var exported = Export(null);
        if (!string.Equals(request.DecisionId, exported.Snapshot.DecisionId, StringComparison.Ordinal))
        {
            return Reject(request, "stale_decision", "Requested decision_id is no longer current.");
        }

        var action = ResolveAction(exported.Actions, request);
        if (action is null)
        {
            return Reject(request, "illegal_action", "Requested action is not part of the current legal action set.");
        }

        switch (action.Type)
        {
            case "play_card":
            case "end_turn":
                _currentPhase = DecisionPhase.Reward;
                _rewardStage = 0;
                break;
            case "choose_reward":
                if (string.Equals(_currentPhase, DecisionPhase.Reward, StringComparison.OrdinalIgnoreCase) && _rewardStage == 0)
                {
                    // Simulate selecting "Add a card" reward which opens the card reward selection screen.
                    _rewardStage = 1;
                    _currentPhase = DecisionPhase.Reward;
                    break;
                }

                _rewardStage = 0;
                _currentPhase = DecisionPhase.Map;
                break;
            case "skip_reward":
            case "skip":
                _rewardStage = 0;
                _currentPhase = DecisionPhase.Map;
                break;
            case "choose_map_node":
                _currentPhase = DecisionPhase.Combat;
                break;
            default:
                return Reject(request, "unsupported_action", $"Fixture provider cannot execute action type '{action.Type}'.");
        }

        var nextSnapshot = Export(null).Snapshot;
        return Accept(request, action, "Fixture action applied.", new Dictionary<string, object?>
        {
            ["next_decision_id"] = nextSnapshot.DecisionId,
            ["next_phase"] = nextSnapshot.Phase,
        });
    }

    private ExportedWindow Export(string? requestedPhase)
    {
        var phase = ResolvePhase(requestedPhase);
        _currentPhase = phase;
        var context = phase == DecisionPhase.Reward
            ? (_rewardStage == 0 ? _rewardWindows["reward_choice"] : _rewardWindows["reward_card_selection"])
            : _windows[phase];
        return _extractors[phase].Export(context, _sessionState);
    }

    private static LegalAction? ResolveAction(IEnumerable<LegalAction> actions, ActionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ActionId))
        {
            return actions.FirstOrDefault(action => string.Equals(action.ActionId, request.ActionId, StringComparison.Ordinal));
        }

        return actions.FirstOrDefault(action =>
            string.Equals(action.Type, request.ActionType, StringComparison.OrdinalIgnoreCase) &&
            request.Params.All(pair => action.Params.TryGetValue(pair.Key, out var value) && Equals(value, pair.Value)));
    }

    private static ActionResponse Reject(ActionRequest request, string errorCode, string message)
    {
        return new ActionResponse(
            RequestId: request.RequestId ?? Guid.NewGuid().ToString("N"),
            DecisionId: request.DecisionId,
            ActionId: request.ActionId,
            Status: "rejected",
            ErrorCode: errorCode,
            Message: message,
            Metadata: new Dictionary<string, object?>());
    }

    private static ActionResponse Accept(ActionRequest request, LegalAction action, string message, IReadOnlyDictionary<string, object?> metadata)
    {
        return new ActionResponse(
            RequestId: request.RequestId ?? Guid.NewGuid().ToString("N"),
            DecisionId: request.DecisionId,
            ActionId: action.ActionId,
            Status: "accepted",
            ErrorCode: null,
            Message: message,
            Metadata: metadata);
    }

    private string ResolvePhase(string? requestedPhase)
    {
        if (!_options.AllowDebugPhaseOverride || string.IsNullOrWhiteSpace(requestedPhase))
        {
            return _currentPhase;
        }

        if (_windows.ContainsKey(requestedPhase))
        {
            return requestedPhase;
        }

        return requestedPhase.ToLowerInvariant() switch
        {
            "combat" => DecisionPhase.Combat,
            "reward" => DecisionPhase.Reward,
            "map" => DecisionPhase.Map,
            "terminal" => DecisionPhase.Terminal,
            _ => _currentPhase,
        };
    }

    private static Dictionary<string, RuntimeWindowContext> CreateWindows()
    {
        return new Dictionary<string, RuntimeWindowContext>(StringComparer.OrdinalIgnoreCase)
        {
            [DecisionPhase.Combat] = new RuntimeWindowContext(
                DecisionPhase.Combat,
                new RuntimePlayerState(
                    Hp: 70,
                    MaxHp: 80,
                    Block: 6,
                    Energy: 3,
                    Gold: 99,
                    Hand: new[]
                    {
                        new RuntimeCard("strike_red", "Strike", 1),
                        new RuntimeCard("defend_red", "Defend", 1),
                    },
                    DrawPile: 12,
                    DiscardPile: 4,
                    ExhaustPile: 0,
                    Relics: new[] { "Burning Blood" },
                    Potions: new[] { "Strength Potion" }),
                new[] { new RuntimeEnemyState("jaw_worm_1", "Jaw Worm", 38, 42, 0, "attack_11") },
                Array.Empty<string>(),
                Array.Empty<string>(),
                Terminal: false,
                Metadata: new Dictionary<string, object?> { ["room_type"] = "combat", ["turn"] = 1 },
                Actions: new[]
                {
                    new RuntimeActionDefinition("play_card", "Play Strike", new Dictionary<string, object?> { ["card_id"] = "strike_red" }, new[] { "jaw_worm_1" }),
                    new RuntimeActionDefinition("play_card", "Play Defend", new Dictionary<string, object?> { ["card_id"] = "defend_red" }),
                    new RuntimeActionDefinition("use_potion", "Use Strength Potion", new Dictionary<string, object?> { ["potion"] = "Strength Potion" }),
                    new RuntimeActionDefinition("end_turn", "End Turn", new Dictionary<string, object?>()),
                }),
            [DecisionPhase.Map] = new RuntimeWindowContext(
                DecisionPhase.Map,
                new RuntimePlayerState(70, 80, 0, 0, 116, Array.Empty<RuntimeCard>(), 12, 4, 0, new[] { "Burning Blood" }, Array.Empty<string>()),
                Array.Empty<RuntimeEnemyState>(),
                Array.Empty<string>(),
                new[] { "monster_left", "elite_center", "question_right" },
                Terminal: false,
                Metadata: new Dictionary<string, object?> { ["room_type"] = "map", ["floor"] = 2 },
                Actions: new[]
                {
                    new RuntimeActionDefinition("choose_map_node", "Choose monster_left", new Dictionary<string, object?> { ["node"] = "monster_left" }),
                    new RuntimeActionDefinition("choose_map_node", "Choose elite_center", new Dictionary<string, object?> { ["node"] = "elite_center" }),
                    new RuntimeActionDefinition("choose_map_node", "Choose question_right", new Dictionary<string, object?> { ["node"] = "question_right" }),
                }),
            [DecisionPhase.Terminal] = new RuntimeWindowContext(
                DecisionPhase.Terminal,
                new RuntimePlayerState(63, 80, 0, 0, 116, Array.Empty<RuntimeCard>(), 0, 0, 0, new[] { "Burning Blood" }, Array.Empty<string>()),
                Array.Empty<RuntimeEnemyState>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Terminal: true,
                Metadata: new Dictionary<string, object?> { ["room_type"] = "victory", ["result"] = "win" },
                Actions: Array.Empty<RuntimeActionDefinition>())
        };
    }

    private static Dictionary<string, RuntimeWindowContext> CreateRewardWindows()
    {
        var player = new RuntimePlayerState(70, 80, 0, 0, 116, Array.Empty<RuntimeCard>(), 12, 4, 0, new[] { "Burning Blood" }, Array.Empty<string>());

        var rewardChoiceLabels = new[]
        {
            "Add a card to your deck.",
            "Gain gold.",
        };

        var rewardChoiceActions = rewardChoiceLabels
            .Select((label, index) => new RuntimeActionDefinition(
                "choose_reward",
                $"Choose {label}",
                new Dictionary<string, object?> { ["reward"] = label, ["reward_index"] = index }))
            .Concat(new[]
            {
                new RuntimeActionDefinition("skip_reward", "Skip Reward", new Dictionary<string, object?>()),
            })
            .ToArray();

        var cardChoiceLabels = new[]
        {
            "Strike",
            "Defend",
            "Bash",
        };

        var cardChoiceActions = cardChoiceLabels
            .Select((label, index) => new RuntimeActionDefinition(
                "choose_reward",
                $"Choose {label}",
                new Dictionary<string, object?> { ["reward"] = label, ["reward_index"] = index }))
            .Concat(new[]
            {
                new RuntimeActionDefinition("skip_reward", "Skip Reward", new Dictionary<string, object?>()),
            })
            .ToArray();

        return new Dictionary<string, RuntimeWindowContext>(StringComparer.OrdinalIgnoreCase)
        {
            ["reward_choice"] = new RuntimeWindowContext(
                DecisionPhase.Reward,
                player,
                Array.Empty<RuntimeEnemyState>(),
                rewardChoiceLabels,
                Array.Empty<string>(),
                Terminal: false,
                Metadata: new Dictionary<string, object?>
                {
                    ["room_type"] = "reward",
                    ["window_kind"] = "reward_choice",
                    ["reward_subphase"] = "reward_choice",
                    ["reward_skip_available"] = true,
                },
                Actions: rewardChoiceActions),
            ["reward_card_selection"] = new RuntimeWindowContext(
                DecisionPhase.Reward,
                player,
                Array.Empty<RuntimeEnemyState>(),
                cardChoiceLabels,
                Array.Empty<string>(),
                Terminal: false,
                Metadata: new Dictionary<string, object?>
                {
                    ["room_type"] = "reward",
                    ["window_kind"] = "reward_card_selection",
                    ["reward_subphase"] = "card_reward_selection",
                    ["reward_skip_available"] = true,
                },
                Actions: cardChoiceActions),
        };
    }
}
