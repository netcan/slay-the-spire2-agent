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
    private readonly Dictionary<string, IWindowExtractor> _extractors;
    private string _currentPhase = DecisionPhase.Combat;

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

    private ExportedWindow Export(string? requestedPhase)
    {
        var phase = ResolvePhase(requestedPhase);
        _currentPhase = phase;
        var context = _windows[phase];
        return _extractors[phase].Export(context, _sessionState);
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
            [DecisionPhase.Reward] = new RuntimeWindowContext(
                DecisionPhase.Reward,
                new RuntimePlayerState(70, 80, 0, 0, 116, Array.Empty<RuntimeCard>(), 12, 4, 0, new[] { "Burning Blood" }, Array.Empty<string>()),
                Array.Empty<RuntimeEnemyState>(),
                new[] { "Inflame", "Pommel Strike", "Shrug It Off" },
                Array.Empty<string>(),
                Terminal: false,
                Metadata: new Dictionary<string, object?> { ["room_type"] = "reward" },
                Actions: new[]
                {
                    new RuntimeActionDefinition("choose_reward", "Choose Inflame", new Dictionary<string, object?> { ["reward"] = "Inflame" }),
                    new RuntimeActionDefinition("choose_reward", "Choose Pommel Strike", new Dictionary<string, object?> { ["reward"] = "Pommel Strike" }),
                    new RuntimeActionDefinition("skip", "Skip Reward", new Dictionary<string, object?>()),
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
}
