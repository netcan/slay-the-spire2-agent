using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using Sts2Mod.StateBridge.Configuration;
using Sts2Mod.StateBridge.Contracts;

namespace Sts2Mod.StateBridge.Providers;

internal sealed class RuntimeStatusReport(bool healthy, string status)
{
    public bool Healthy { get; } = healthy;

    public string Status { get; } = status;
}

internal sealed class RuntimeActionResult(bool accepted, string message, string? errorCode = null, IReadOnlyDictionary<string, object?>? metadata = null)
{
    public bool Accepted { get; } = accepted;

    public string Message { get; } = message;

    public string? ErrorCode { get; } = errorCode;

    public IReadOnlyDictionary<string, object?> Metadata { get; } = metadata ?? new Dictionary<string, object?>();
}

internal sealed class Sts2RuntimeReflectionReader
{
    private const string Sts2AssemblyName = "sts2";
    private const string NGameTypeName = "MegaCrit.Sts2.Core.Nodes.NGame";
    private const string NRunTypeName = "MegaCrit.Sts2.Core.Nodes.NRun";
    private const string OverlayStackTypeName = "MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack";
    private const string RewardScreenTypeName = "MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen";
    private static readonly string[] CardRewardSelectionTypeHints =
    {
        "CardReward",
        "RewardCard",
        "CardSelection",
        "CardSelect",
        "CardGrid",
    };

    private static readonly string[] CardRewardChoiceCollectionMembers =
    {
        "_cards",
        "Cards",
        "_rewardCards",
        "RewardCards",
        "_cardChoices",
        "CardChoices",
        "_choices",
        "Choices",
        "Options",
        "_options",
        "_cardButtons",
        "CardButtons",
        "_buttons",
        "Buttons",
    };

    private static readonly string[] CardRewardChoiceCardMembers =
    {
        "Card",
        "_card",
        "Reward",
        "_reward",
        "Value",
        "_value",
        "Data",
        "_data",
    };

    private static readonly string[] CardRewardChoiceSelectMethodNames =
    {
        "SelectCard",
        "ChooseCard",
        "OnCardSelected",
        "OnCardChosen",
        "OnChoiceSelected",
        "CardSelectedFrom",
        "CardChosenFrom",
        "ConfirmSelection",
    };

    private static readonly string[] CardRewardChoiceSkipMethodNames =
    {
        "Skip",
        "OnSkip",
        "OnSkipped",
        "SkipReward",
        "Cancel",
        "OnCancel",
        "Close",
        "Dismiss",
    };
    private readonly BridgeOptions _options;
    private readonly InstallationProbeResult _probe;

    public Sts2RuntimeReflectionReader(BridgeOptions options, InstallationProbeResult probe)
    {
        _options = options;
        _probe = probe;
    }

    public RuntimeStatusReport GetStatusReport()
    {
        var assembly = FindSts2Assembly();
        if (assembly is null)
        {
            return new RuntimeStatusReport(
                healthy: false,
                status: $"sts2 assembly is not loaded in the current process; launch the bridge inside the game. managed_dir={_probe.ManagedDir ?? "missing"}");
        }

        if (!TryGetRuntimeRoot(assembly, out var root, out var status))
        {
            return new RuntimeStatusReport(healthy: true, status: status);
        }

        var phase = DetectPhase(root.RunNode, root.RunState);
        return new RuntimeStatusReport(
            healthy: true,
            status: $"live runtime attached; phase={phase}; game_version={_probe.GameVersion ?? _options.GameVersion}");
    }

    public bool IsAssemblyLoaded()
    {
        return FindSts2Assembly() is not null;
    }

    public RuntimeWindowContext CaptureWindow()
    {
        var assembly = FindSts2Assembly()
            ?? throw new InvalidOperationException("sts2 assembly is not loaded in the current process. Start the bridge from inside the game runtime.");

        if (!TryGetRuntimeRoot(assembly, out var root, out var status))
        {
            throw new InvalidOperationException(status);
        }

        var phase = DetectPhase(root.RunNode, root.RunState);
        return phase switch
        {
            DecisionPhase.Reward => BuildRewardWindow(root.RunNode, root.RunState),
            DecisionPhase.Map => BuildMapWindow(root.RunNode, root.RunState),
            DecisionPhase.Terminal => BuildTerminalWindow(root.RunNode, root.RunState),
            _ => BuildCombatWindow(root.RunNode, root.RunState),
        };
    }

    public RuntimeActionResult ExecuteAction(ActionRequest request, LegalAction action)
    {
        var assembly = FindSts2Assembly();
        if (assembly is null)
        {
            return new RuntimeActionResult(false, "sts2 assembly is not loaded in the current process.", "runtime_not_ready");
        }

        if (!TryGetRuntimeRoot(assembly, out var root, out var status))
        {
            return new RuntimeActionResult(false, status, "runtime_not_ready");
        }

        return action.Type switch
        {
            "play_card" => ExecutePlayCard(root.RunState, request, action),
            "end_turn" => ExecuteEndTurn(root.RunState, request),
            "choose_reward" => ExecuteChooseReward(root.RunNode, request, action),
            "skip_reward" => ExecuteSkipReward(root.RunNode, request),
            "choose_map_node" => ExecuteChooseMapNode(request, action),
            _ => new RuntimeActionResult(false, $"Action type '{action.Type}' is not supported yet.", "unsupported_action"),
        };
    }

    private RuntimeWindowContext BuildCombatWindow(object runNode, object runState)
    {
        var textDiagnostics = new TextDiagnosticsCollector();
        var player = BuildPlayerState(runState, textDiagnostics);
        var enemies = BuildEnemies(runState, textDiagnostics);
        var metadata = CreateBaseMetadata(runNode, runState, DecisionPhase.Combat);
        var rewardAnalysis = AnalyzeRewardPhase(runNode, runState);
        metadata["phase_detection"] = rewardAnalysis.ToMetadata();
        var actions = new List<RuntimeActionDefinition>();
        var liveEnemyIds = enemies.Where(enemy => enemy.IsAlive).Select(enemy => enemy.EnemyId).ToArray();
        if (liveEnemyIds.Length == 0)
        {
            metadata["window_kind"] = "combat_transition";
            metadata["reward_pending"] = true;
            metadata["text_diagnostics"] = textDiagnostics.ToMetadata();
            return new RuntimeWindowContext(
                DecisionPhase.Combat,
                player,
                enemies,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Terminal: false,
                Metadata: metadata,
                Actions: Array.Empty<RuntimeActionDefinition>());
        }

        foreach (var card in GetHandCardDescriptors(runState, textDiagnostics).Where(card => card.Playable))
        {
            var parameters = new Dictionary<string, object?>
            {
                ["card_id"] = card.CardId,
                ["card_name"] = card.Name,
            };
            if (!string.IsNullOrWhiteSpace(card.TargetType))
            {
                parameters["target_type"] = card.TargetType;
            }

            var actionMetadata = new Dictionary<string, object?> { ["playable"] = true };
            if (!string.Equals(card.NameResolution.Status, "resolved", StringComparison.Ordinal))
            {
                foreach (var pair in RuntimeTextResolver.CreateActionDiagnostics($"actions.play_card[{card.CardId}].label", card.NameResolution))
                {
                    actionMetadata[pair.Key] = pair.Value;
                }
            }

            actions.Add(new RuntimeActionDefinition(
                "play_card",
                $"Play {card.Name}",
                parameters,
                BuildTargetConstraints(card.TargetType, liveEnemyIds),
                actionMetadata));
        }

        if (player is not null)
        {
            foreach (var potion in player.Potions.Where(potion => !string.IsNullOrWhiteSpace(potion)))
            {
                actions.Add(new RuntimeActionDefinition(
                    "use_potion",
                    $"Use {potion}",
                    new Dictionary<string, object?> { ["potion"] = potion }));
            }
        }

        actions.Add(new RuntimeActionDefinition("end_turn", "End Turn", new Dictionary<string, object?>()));
        metadata["text_diagnostics"] = textDiagnostics.ToMetadata();
        return new RuntimeWindowContext(
            DecisionPhase.Combat,
            player,
            enemies,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Terminal: false,
            Metadata: metadata,
            Actions: actions);
    }

    private RuntimeWindowContext BuildRewardWindow(object runNode, object runState)
    {
        var textDiagnostics = new TextDiagnosticsCollector();
        var rewardAnalysis = AnalyzeRewardPhase(runNode, runState);
        var rewards = ExtractRewards(runNode, textDiagnostics);
        var player = BuildPlayerState(runState, textDiagnostics);
        var metadata = CreateBaseMetadata(runNode, runState, DecisionPhase.Reward);
        metadata["phase_detection"] = rewardAnalysis.ToMetadata();
        metadata["reward_subphase"] = rewardAnalysis.RewardSubphase;
        metadata["detection_source"] = rewardAnalysis.DetectionSource;
        if (string.Equals(rewardAnalysis.RewardSubphase, "card_reward_selection", StringComparison.Ordinal))
        {
            metadata["window_kind"] = "reward_card_selection";
        }
        metadata["reward_count"] = rewards.Count;
        var actions = rewards
            .Select((reward, index) =>
            {
                var actionMetadata = new Dictionary<string, object?>();
                if (!string.Equals(reward.Resolution.Status, "resolved", StringComparison.Ordinal))
                {
                    foreach (var pair in RuntimeTextResolver.CreateActionDiagnostics($"actions.choose_reward[{index}].label", reward.Resolution))
                    {
                        actionMetadata[pair.Key] = pair.Value;
                    }
                }

                return new RuntimeActionDefinition(
                    "choose_reward",
                    $"Choose {reward.Label}",
                    new Dictionary<string, object?> { ["reward"] = reward.Label, ["reward_index"] = index },
                    Metadata: actionMetadata);
            })
            .ToList();

        if (rewards.Count > 0)
        {
            var skipAvailability = ResolveRewardSkipAvailability(runNode, rewardAnalysis);
            metadata["reward_skip_available"] = skipAvailability.Available;
            if (!skipAvailability.Available && !string.IsNullOrWhiteSpace(skipAvailability.Reason))
            {
                metadata["reward_skip_reason"] = skipAvailability.Reason;
            }

            if (skipAvailability.Available)
            {
                actions.Add(new RuntimeActionDefinition("skip_reward", "Skip Reward", new Dictionary<string, object?>()));
            }
        }

        metadata["text_diagnostics"] = textDiagnostics.ToMetadata();

        return new RuntimeWindowContext(
            DecisionPhase.Reward,
            player,
            Array.Empty<RuntimeEnemyState>(),
            rewards.Select(reward => reward.Label).ToArray(),
            Array.Empty<string>(),
            Terminal: false,
            Metadata: metadata,
            Actions: actions);
    }

    private RuntimeWindowContext BuildMapWindow(object runNode, object runState)
    {
        var textDiagnostics = new TextDiagnosticsCollector();
        var mapNodes = ExtractMapNodes(runState, textDiagnostics);
        var player = BuildPlayerState(runState, textDiagnostics);
        var metadata = CreateBaseMetadata(runNode, runState, DecisionPhase.Map);
        metadata["node_count"] = mapNodes.Count;
        metadata["text_diagnostics"] = textDiagnostics.ToMetadata();
        var actions = mapNodes
            .Select(node => new RuntimeActionDefinition(
                "choose_map_node",
                $"Choose {node}",
                new Dictionary<string, object?> { ["node"] = node }))
            .ToList();

        return new RuntimeWindowContext(
            DecisionPhase.Map,
            player,
            Array.Empty<RuntimeEnemyState>(),
            Array.Empty<string>(),
            mapNodes,
            Terminal: false,
            Metadata: metadata,
            Actions: actions);
    }

    private RuntimeWindowContext BuildTerminalWindow(object runNode, object runState)
    {
        var textDiagnostics = new TextDiagnosticsCollector();
        var player = BuildPlayerState(runState, textDiagnostics);
        var metadata = CreateBaseMetadata(runNode, runState, DecisionPhase.Terminal);
        metadata["result"] = GetBoolean(runState, "IsGameOver") ? "game_over" : "terminal";
        metadata["text_diagnostics"] = textDiagnostics.ToMetadata();
        return new RuntimeWindowContext(
            DecisionPhase.Terminal,
            player,
            Array.Empty<RuntimeEnemyState>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Terminal: true,
            Metadata: metadata,
            Actions: Array.Empty<RuntimeActionDefinition>());
    }

    private Dictionary<string, object?> CreateBaseMetadata(object runState, string phase)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["source"] = "sts2_runtime",
            ["phase_detected"] = phase,
            ["game_version"] = _probe.GameVersion ?? _options.GameVersion,
            ["managed_dir"] = _probe.ManagedDir,
            ["current_room_type"] = GetTypeName(GetMemberValue(runState, "CurrentRoom")),
            ["current_location_type"] = GetTypeName(GetMemberValue(runState, "CurrentLocation")),
            ["act_floor"] = GetNullableInt(runState, "ActFloor"),
            ["current_act_index"] = GetNullableInt(runState, "CurrentActIndex"),
            ["ascension_level"] = GetNullableInt(runState, "AscensionLevel"),
            ["is_game_over"] = GetBoolean(runState, "IsGameOver"),
        };

        var combatState = GetCombatState(runState);
        if (combatState is not null)
        {
            metadata["round_number"] = GetNullableInt(combatState, "RoundNumber");
            metadata["current_side"] = ConvertToText(GetMemberValue(combatState, "CurrentSide"));
        }

        var currentMapPoint = GetMemberValue(runState, "CurrentMapPoint");
        if (currentMapPoint is not null)
        {
            metadata["current_map_coord"] = DescribeMapCoord(GetMemberValue(currentMapPoint, "coord"));
            metadata["current_map_point_type"] = ConvertToText(GetMemberValue(currentMapPoint, "PointType"));
        }

        var player = GetPlayers(runState).FirstOrDefault();
        var playerCombatState = GetMemberValue(player, "PlayerCombatState");
        if (playerCombatState is not null)
        {
            metadata["stars"] = GetNullableInt(playerCombatState, "Stars");
            metadata["max_energy"] = GetNullableInt(playerCombatState, "MaxEnergy");
        }

        return metadata;
    }

    private Dictionary<string, object?> CreateBaseMetadata(object runNode, object runState, string phase)
    {
        var metadata = CreateBaseMetadata(runState, phase);
        var overlayTop = GetOverlayTopScreen(runNode);
        if (overlayTop is not null)
        {
            metadata["overlay_top_type"] = GetTypeName(overlayTop);
        }
        return metadata;
    }

    private object? GetOverlayTopScreen(object runNode)
    {
        var overlayStack = GetOverlayStack(runNode);
        if (overlayStack is null)
        {
            return null;
        }

        return TryInvokeParameterlessMethod(overlayStack, "Peek");
    }

    private RuntimePlayerState? BuildPlayerState(object runState, TextDiagnosticsCollector textDiagnostics)
    {
        var player = GetPlayers(runState).FirstOrDefault();
        if (player is null)
        {
            return null;
        }

        var creature = GetMemberValue(player, "Creature");
        var playerCombatState = GetMemberValue(player, "PlayerCombatState");
        var handCards = ExtractCards(GetMemberValue(playerCombatState, "Hand"), "player.hand", textDiagnostics);
        var relics = ExtractLabels(GetMemberValue(player, "Relics"), "player.relics", textDiagnostics);
        var potions = ExtractLabels(GetMemberValue(player, "PotionSlots"), "player.potions", textDiagnostics);

        return new RuntimePlayerState(
            Hp: GetNullableInt(creature, "CurrentHp") ?? 0,
            MaxHp: GetNullableInt(creature, "MaxHp") ?? 0,
            Block: GetNullableInt(creature, "Block") ?? 0,
            Energy: GetNullableInt(playerCombatState, "Energy") ?? 0,
            Gold: GetNullableInt(player, "Gold") ?? 0,
            Hand: handCards,
            DrawPile: CountCards(GetMemberValue(playerCombatState, "DrawPile")),
            DiscardPile: CountCards(GetMemberValue(playerCombatState, "DiscardPile")),
            ExhaustPile: CountCards(GetMemberValue(playerCombatState, "ExhaustPile")),
            Relics: relics,
            Potions: potions);
    }

    private IReadOnlyList<RuntimeEnemyState> BuildEnemies(object runState, TextDiagnosticsCollector textDiagnostics)
    {
        var combatState = GetCombatState(runState);
        if (combatState is null)
        {
            return Array.Empty<RuntimeEnemyState>();
        }

        return EnumerateObjects(GetMemberValue(combatState, "Enemies"))
            .Select((enemy, index) => new RuntimeEnemyState(
                EnemyId: ResolveEnemyId(enemy, index),
                Name: ConvertToText(GetMemberValue(enemy, "Name"), $"enemies[{index}].name", textDiagnostics) ?? $"enemy_{index}",
                Hp: GetNullableInt(enemy, "CurrentHp") ?? 0,
                MaxHp: GetNullableInt(enemy, "MaxHp") ?? 0,
                Block: GetNullableInt(enemy, "Block") ?? 0,
                Intent: ResolveEnemyIntent(enemy),
                IsAlive: GetBoolean(enemy, "IsAlive", defaultValue: true)))
            .ToArray();
    }

    private IReadOnlyList<HandCardDescriptor> GetHandCardDescriptors(object runState, TextDiagnosticsCollector textDiagnostics)
    {
        var player = GetPlayers(runState).FirstOrDefault();
        var playerCombatState = GetMemberValue(player, "PlayerCombatState");
        var hand = GetMemberValue(playerCombatState, "Hand");
        return EnumerateObjects(GetMemberValue(hand, "Cards"))
            .Select((card, index) =>
            {
                var nameResolution = RuntimeTextResolver.Resolve(
                    GetMemberValue(card, "Title") ?? GetMemberValue(card, "Name") ?? card,
                    $"player.hand[{index}].display_name",
                    textDiagnostics,
                    "Title",
                    "Name");
                return new HandCardDescriptor(
                    RuntimeCardIdentity.CreateCardId(card, index),
                    nameResolution.Text ?? $"card_{index}",
                    nameResolution,
                    ConvertToText(GetMemberValue(card, "TargetType")),
                    GetBoolean(card, "IsPlayable", defaultValue: true));
            })
            .ToArray();
    }

    private IReadOnlyList<RuntimeCard> ExtractCards(object? pile, string path, TextDiagnosticsCollector textDiagnostics)
    {
        return EnumerateObjects(GetMemberValue(pile, "Cards"))
            .Select((card, index) => new RuntimeCard(
                CardId: RuntimeCardIdentity.CreateCardId(card, index),
                Name: ConvertToText(GetMemberValue(card, "Title") ?? GetMemberValue(card, "Name") ?? card, $"{path}[{index}].name", textDiagnostics, "Title", "Name")
                      ?? $"card_{index}",
                Cost: ResolveCardCost(card),
                Playable: GetBoolean(card, "IsPlayable", defaultValue: true)))
            .ToArray();
    }

    private int CountCards(object? pile)
    {
        return EnumerateObjects(GetMemberValue(pile, "Cards")).Count();
    }

    private List<RewardOption> ExtractRewards(object runNode, TextDiagnosticsCollector textDiagnostics)
    {
        var rewardScreen = GetRewardScreen(runNode);
        var cardRewardScreen = GetCardRewardSelectionScreen(runNode, rewardScreen);
        if (cardRewardScreen is not null)
        {
            return ExtractCardRewardSelectionRewards(cardRewardScreen, textDiagnostics);
        }

        if (rewardScreen is null)
        {
            return new List<RewardOption>();
        }

        return GetRewardButtons(rewardScreen)
            .Select((button, index) => DescribeReward(GetMemberValue(button, "Reward"), $"rewards[{index}]", textDiagnostics))
            .OfType<RewardOption>()
            .Where(reward => !string.IsNullOrWhiteSpace(reward.Label))
            .ToList();
    }

    private List<RewardOption> ExtractCardRewardSelectionRewards(object cardRewardScreen, TextDiagnosticsCollector textDiagnostics)
    {
        var choices = ExtractCardRewardChoiceItems(cardRewardScreen);
        if (choices.Count == 0)
        {
            return new List<RewardOption>();
        }

        var options = new List<RewardOption>(choices.Count);
        for (var index = 0; index < choices.Count; index++)
        {
            var card = ResolveCardRewardChoiceCard(choices[index]);
            var display = GetMemberValue(card, "Title") ?? GetMemberValue(card, "Name") ?? card;
            var resolution = RuntimeTextResolver.Resolve(display, $"rewards[{index}].card", textDiagnostics, "Title", "Name");
            var label = resolution.Text ?? $"card_{index}";
            options.Add(new RewardOption(label, resolution));
        }

        return options
            .Where(option => !string.IsNullOrWhiteSpace(option.Label))
            .ToList();
    }

    private List<string> ExtractMapNodes(object runState, TextDiagnosticsCollector textDiagnostics)
    {
        var currentMapPoint = GetMemberValue(runState, "CurrentMapPoint");
        var nodes = EnumerateObjects(GetMemberValue(currentMapPoint, "Children"))
            .Select((node, index) => DescribeMapNode(node, $"map_nodes[{index}]", textDiagnostics))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (nodes.Count > 0)
        {
            return nodes;
        }

        var map = GetMemberValue(runState, "Map");
        var startingPoint = GetMemberValue(map, "StartingMapPoint");
        return EnumerateObjects(GetMemberValue(startingPoint, "Children"))
            .Select((node, index) => DescribeMapNode(node, $"map_nodes[{index}]", textDiagnostics))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private bool TryGetRuntimeRoot(Assembly assembly, out RuntimeRoot root, out string status)
    {
        var gameType = assembly.GetType(NGameTypeName);
        if (gameType is null)
        {
            root = default;
            status = "MegaCrit.Sts2.Core.Nodes.NGame was not found in sts2.dll.";
            return false;
        }

        var gameInstance = GetMemberValue(gameType, "Instance");
        if (gameInstance is null)
        {
            root = default;
            status = "sts2 assembly loaded; waiting for NGame.Instance.";
            return false;
        }

        var runNode = GetMemberValue(gameInstance, "CurrentRunNode");
        if (runNode is null)
        {
            var runType = assembly.GetType(NRunTypeName);
            runNode = GetMemberValue(runType, "Instance");
        }

        if (runNode is null)
        {
            root = default;
            status = "game runtime attached; waiting for an active run.";
            return false;
        }

        var runState = GetMemberValue(runNode, "_state");
        if (runState is null)
        {
            root = default;
            status = "run node found, but RunState is not available yet.";
            return false;
        }

        root = new RuntimeRoot(gameInstance, runNode, runState);
        status = "ok";
        return true;
    }

    private string DetectPhase(object runNode, object runState)
    {
        if (GetBoolean(runState, "IsGameOver"))
        {
            return DecisionPhase.Terminal;
        }

        var screenTracker = GetMemberValue(runNode, "ScreenStateTracker");
        if (GetBoolean(screenTracker, "_mapScreenVisible"))
        {
            return DecisionPhase.Map;
        }

        var currentRoomType = GetTypeName(GetMemberValue(runState, "CurrentRoom"));
        if (currentRoomType is not null && currentRoomType.Contains("Map", StringComparison.OrdinalIgnoreCase))
        {
            return DecisionPhase.Map;
        }

        if (AnalyzeRewardPhase(runNode, runState).TreatAsReward)
        {
            return DecisionPhase.Reward;
        }

        return DecisionPhase.Combat;
    }

    private object? GetRewardScreen(object runNode)
    {
        var screenTracker = GetMemberValue(runNode, "ScreenStateTracker");
        var trackerRewardScreen = GetMemberValue(screenTracker, "_connectedRewardsScreen")
                                  ?? GetMemberValue(screenTracker, "ConnectedRewardsScreen")
                                  ?? GetMemberValue(screenTracker, "_rewardScreen")
                                  ?? GetMemberValue(screenTracker, "RewardScreen");
        if (trackerRewardScreen is not null)
        {
            return trackerRewardScreen;
        }

        var overlayRewardScreen = GetOverlayRewardScreen(runNode);
        if (overlayRewardScreen is not null)
        {
            return overlayRewardScreen;
        }

        return GetMemberValue(GetRewardScreenType(), "Instance");
    }

    private object? GetCardRewardSelectionScreen(object runNode, object? rewardScreen = null)
    {
        rewardScreen ??= GetRewardScreen(runNode);
        var overlayTop = GetOverlayTopScreen(runNode);
        if (overlayTop is null || IsRewardScreenObject(overlayTop))
        {
            return null;
        }

        var typeName = GetTypeName(overlayTop) ?? string.Empty;
        var nameHint = CardRewardSelectionTypeHints.Any(hint => typeName.Contains(hint, StringComparison.OrdinalIgnoreCase));
        if (!nameHint && rewardScreen is null)
        {
            // Avoid accidentally treating unrelated card screens (deck view/shop/etc.) as reward.
            if (!typeName.Contains("Reward", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        var choices = ExtractCardRewardChoiceItems(overlayTop);
        if (choices.Count == 0)
        {
            return null;
        }

        var hasSelectHook = HasAnyMethod(overlayTop, CardRewardChoiceSelectMethodNames) ||
                            choices.Any(choice => HasAnyMethod(choice, CardRewardChoiceSelectMethodNames));
        if (!nameHint && !hasSelectHook)
        {
            return null;
        }

        return overlayTop;
    }

    private RewardPhaseAnalysis AnalyzeRewardPhase(object runNode, object runState)
    {
        var rewardScreen = GetRewardScreen(runNode);
        var rewardButtons = GetRewardButtons(rewardScreen).ToArray();
        var hasRewardScreen = rewardScreen is not null;
        var rewardScreenComplete = hasRewardScreen && GetBoolean(rewardScreen, "IsComplete");
        var rewardScreenVisible = hasRewardScreen && IsRewardScreenVisible(runNode, rewardScreen!);
        var hasLiveEnemies = BuildEnemies(runState, new TextDiagnosticsCollector()).Any(enemy => enemy.IsAlive);
        var cardRewardSelectionDetected = GetCardRewardSelectionScreen(runNode, rewardScreen) is not null;
        var treatAsReward = hasRewardScreen &&
                            (!rewardScreenComplete ||
                             rewardButtons.Length > 0 ||
                             rewardScreenVisible ||
                             !hasLiveEnemies);
        if (cardRewardSelectionDetected)
        {
            treatAsReward = true;
        }

        var rewardSubphase = cardRewardSelectionDetected
            ? "card_reward_selection"
            : hasRewardScreen ? "reward_choice" : "none";
        var detectionSource = cardRewardSelectionDetected
            ? "overlay_stack.card_reward_selection"
            : ResolveRewardScreenSource(runNode, rewardScreen);

        return new RewardPhaseAnalysis(
            TreatAsReward: treatAsReward,
            HasRewardScreen: hasRewardScreen,
            RewardScreenComplete: rewardScreenComplete,
            RewardScreenVisible: rewardScreenVisible,
            RewardButtonCount: rewardButtons.Length,
            HasLiveEnemies: hasLiveEnemies,
            RewardScreenSource: ResolveRewardScreenSource(runNode, rewardScreen),
            CardRewardSelectionDetected: cardRewardSelectionDetected,
            RewardSubphase: rewardSubphase,
            DetectionSource: detectionSource,
            OverlayTopType: GetTypeName(GetOverlayTopScreen(runNode)));
    }

    private IEnumerable<object> GetRewardButtons(object? rewardScreen)
    {
        return EnumerateObjects(
            GetMemberValue(rewardScreen, "_rewardButtons")
            ?? GetMemberValue(rewardScreen, "RewardButtons")
            ?? GetMemberValue(rewardScreen, "Buttons"));
    }

    private readonly record struct RewardSkipAvailability(bool Available, string? Reason);

    private RewardSkipAvailability ResolveRewardSkipAvailability(object runNode, RewardPhaseAnalysis rewardAnalysis)
    {
        if (string.Equals(rewardAnalysis.RewardSubphase, "card_reward_selection", StringComparison.Ordinal))
        {
            var cardRewardScreen = GetCardRewardSelectionScreen(runNode);
            if (cardRewardScreen is null)
            {
                return new RewardSkipAvailability(false, "card_reward_screen_missing");
            }

            return HasAnyMethod(cardRewardScreen, CardRewardChoiceSkipMethodNames)
                ? new RewardSkipAvailability(true, null)
                : new RewardSkipAvailability(false, "skip_hook_not_found");
        }

        return new RewardSkipAvailability(true, null);
    }

    private List<object> ExtractCardRewardChoiceItems(object cardRewardScreen)
    {
        foreach (var memberName in CardRewardChoiceCollectionMembers)
        {
            var value = GetMemberValue(cardRewardScreen, memberName);
            var items = EnumerateObjects(value).ToList();
            if (items.Count > 0)
            {
                return items;
            }
        }

        // Some screens keep the choice list nested under another node.
        var nestedContainers = new[]
        {
            GetMemberValue(cardRewardScreen, "CardGrid"),
            GetMemberValue(cardRewardScreen, "_cardGrid"),
            GetMemberValue(cardRewardScreen, "Grid"),
            GetMemberValue(cardRewardScreen, "_grid"),
            GetMemberValue(cardRewardScreen, "Selection"),
            GetMemberValue(cardRewardScreen, "_selection"),
        };
        foreach (var container in nestedContainers.Where(container => container is not null))
        {
            foreach (var memberName in CardRewardChoiceCollectionMembers)
            {
                var value = GetMemberValue(container, memberName);
                var items = EnumerateObjects(value).ToList();
                if (items.Count > 0)
                {
                    return items;
                }
            }
        }

        return new List<object>();
    }

    private static object? ResolveCardRewardChoiceCard(object choice)
    {
        foreach (var memberName in CardRewardChoiceCardMembers)
        {
            var value = GetMemberValue(choice, memberName);
            if (value is not null)
            {
                return value;
            }
        }

        return choice;
    }

    private static bool HasAnyMethod(object target, IEnumerable<string> methodNames)
    {
        var type = target.GetType();
        foreach (var name in methodNames)
        {
            if (type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryInvokeFirstCompatibleMethod(
        object target,
        IEnumerable<string> methodNames,
        IReadOnlyList<object?[]> argCandidates,
        out string? invokedMethod)
    {
        invokedMethod = null;
        var type = target.GetType();
        foreach (var name in methodNames)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(method => string.Equals(method.Name, name, StringComparison.Ordinal))
                .ToArray();
            if (methods.Length == 0)
            {
                continue;
            }

            foreach (var method in methods)
            {
                foreach (var args in argCandidates)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length != args.Length)
                    {
                        continue;
                    }

                    try
                    {
                        method.Invoke(target, args);
                        invokedMethod = method.Name;
                        return true;
                    }
                    catch
                    {
                        // Continue probing other signatures/argument sets.
                    }
                }
            }
        }

        return false;
    }

    private static bool IsRewardScreenVisible(object runNode, object rewardScreen)
    {
        var screenTracker = GetMemberValue(runNode, "ScreenStateTracker");
        return GetBoolean(screenTracker, "_rewardScreenVisible") ||
               GetBoolean(screenTracker, "RewardScreenVisible") ||
               GetBoolean(rewardScreen, "Visible") ||
               GetBoolean(rewardScreen, "IsVisible") ||
               InvokeBooleanMethod(rewardScreen, "IsVisibleInTree");
    }

    private object? GetOverlayRewardScreen(object runNode)
    {
        var overlayStack = GetOverlayStack(runNode);
        var overlayScreen = TryInvokeParameterlessMethod(overlayStack, "Peek");
        return IsRewardScreenObject(overlayScreen) ? overlayScreen : null;
    }

    private object? GetOverlayStack(object runNode)
    {
        var globalUi = GetMemberValue(runNode, "GlobalUi");
        return GetMemberValue(globalUi, "Overlays")
               ?? GetMemberValue(GetOverlayStackType(), "Instance");
    }

    private Type? GetOverlayStackType()
    {
        return FindSts2Assembly()?.GetType(OverlayStackTypeName);
    }

    private Type? GetRewardScreenType()
    {
        return FindSts2Assembly()?.GetType(RewardScreenTypeName);
    }

    private string ResolveRewardScreenSource(object runNode, object? rewardScreen)
    {
        if (rewardScreen is null)
        {
            return "none";
        }

        var screenTracker = GetMemberValue(runNode, "ScreenStateTracker");
        var trackerRewardScreen = GetMemberValue(screenTracker, "_connectedRewardsScreen")
                                  ?? GetMemberValue(screenTracker, "ConnectedRewardsScreen")
                                  ?? GetMemberValue(screenTracker, "_rewardScreen")
                                  ?? GetMemberValue(screenTracker, "RewardScreen");
        if (ReferenceEquals(trackerRewardScreen, rewardScreen))
        {
            return "screen_state_tracker";
        }

        var overlayRewardScreen = GetOverlayRewardScreen(runNode);
        if (ReferenceEquals(overlayRewardScreen, rewardScreen))
        {
            return "overlay_stack";
        }

        if (ReferenceEquals(GetMemberValue(GetRewardScreenType(), "Instance"), rewardScreen))
        {
            return "reward_screen_instance";
        }

        return "other";
    }

    private static bool IsRewardScreenObject(object? target)
    {
        if (target is null)
        {
            return false;
        }

        var typeName = GetTypeName(target);
        return string.Equals(typeName, RewardScreenTypeName, StringComparison.Ordinal) ||
               string.Equals(target.GetType().Name, "NRewardsScreen", StringComparison.Ordinal) ||
               GetMemberValue(target, "_rewardButtons") is not null ||
               GetMemberValue(target, "RewardButtons") is not null;
    }

    private object? GetCombatState(object runState)
    {
        var player = GetPlayers(runState).FirstOrDefault();
        var creature = GetMemberValue(player, "Creature");
        return GetMemberValue(creature, "CombatState");
    }

    private IReadOnlyList<object> GetPlayers(object? runState)
    {
        return EnumerateObjects(GetMemberValue(runState, "Players")).ToArray();
    }

    private Assembly? FindSts2Assembly()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, Sts2AssemblyName, StringComparison.OrdinalIgnoreCase));
    }

    private static int ResolveCardCost(object? card)
    {
        if (GetBoolean(card, "HasEnergyCostX"))
        {
            return -1;
        }

        return GetNullableInt(card, "CanonicalEnergyCost")
               ?? GetNullableInt(card, "CurrentStarCost")
               ?? 0;
    }

    private static string ResolveEnemyId(object enemy, int index)
    {
        return ConvertToText(GetMemberValue(enemy, "CombatId"))
               ?? ConvertToText(GetMemberValue(enemy, "SlotName"))
               ?? ConvertToText(GetMemberValue(enemy, "Name"))
               ?? $"enemy_{index}";
    }

    private static string ResolveEnemyIntent(object enemy)
    {
        return ConvertToText(GetMemberValue(enemy, "Intent"))
               ?? ConvertToText(GetMemberValue(GetMemberValue(enemy, "Monster"), "Intent"))
               ?? "unknown";
    }

    private static IReadOnlyList<string> BuildTargetConstraints(string? targetType, IReadOnlyList<string> liveEnemyIds)
    {
        if (string.IsNullOrWhiteSpace(targetType))
        {
            return Array.Empty<string>();
        }

        var normalized = targetType.ToLowerInvariant();
        if (!normalized.Contains("enemy", StringComparison.Ordinal) &&
            !normalized.Contains("monster", StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        return liveEnemyIds;
    }

    private static List<string> ExtractLabels(object? collection, string path, TextDiagnosticsCollector textDiagnostics)
    {
        return EnumerateObjects(collection)
            .Select((item, index) => DescribeInventoryItem(item, $"{path}[{index}]", textDiagnostics))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!)
            .ToList();
    }

    private static string? DescribeInventoryItem(object? item, string path, TextDiagnosticsCollector textDiagnostics)
    {
        return ConvertToText(item, path, textDiagnostics, "Potion", "Relic", "Name", "Title", "Description", "Label", "Text");
    }

    private static RewardOption? DescribeReward(object? reward, string path, TextDiagnosticsCollector textDiagnostics)
    {
        var resolution = RuntimeTextResolver.Resolve(reward, path, textDiagnostics, "Description", "Label", "Name", "Title", "RewardType");
        return resolution.HasText ? new RewardOption(resolution.Text!, resolution) : null;
    }

    private static string? DescribeMapNode(object? mapPoint, string path, TextDiagnosticsCollector textDiagnostics)
    {
        var pointType = ConvertToText(GetMemberValue(mapPoint, "PointType"), $"{path}.point_type", textDiagnostics) ?? "unknown";
        var coord = DescribeMapCoord(GetMemberValue(mapPoint, "coord"));
        return $"{pointType}@{coord}";
    }

    private static string DescribeMapCoord(object? coord)
    {
        var col = GetNullableInt(coord, "col") ?? -1;
        var row = GetNullableInt(coord, "row") ?? -1;
        return $"{col},{row}";
    }

    private static string? ConvertToText(object? value, string path, TextDiagnosticsCollector? textDiagnostics = null, params string[] preferredMembers)
    {
        return RuntimeTextResolver.Resolve(value, path, textDiagnostics, preferredMembers).Text;
    }

    private static string? ConvertToText(object? value, params string[] preferredMembers)
    {
        return RuntimeTextResolver.Resolve(value, "_", null, preferredMembers).Text;
    }

    private static object? GetMemberValue(object? target, string memberName)
    {
        if (target is null)
        {
            return null;
        }

        var type = target as Type ?? target.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic |
                    (target is Type ? BindingFlags.Static : BindingFlags.Instance);
        var property = type.GetProperty(memberName, flags);
        if (property is not null)
        {
            try
            {
                return property.GetValue(target is Type ? null : target);
            }
            catch
            {
                return null;
            }
        }

        var field = type.GetField(memberName, flags);
        if (field is null)
        {
            return null;
        }

        try
        {
            return field.GetValue(target is Type ? null : target);
        }
        catch
        {
            return null;
        }
    }

    private static object? TryInvokeParameterlessMethod(object? target, string methodName)
    {
        if (target is null)
        {
            return null;
        }

        var method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (method is null)
        {
            return null;
        }

        try
        {
            return method.Invoke(target, null);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<object> EnumerateObjects(object? source)
    {
        if (source is null)
        {
            yield break;
        }

        if (source is string text)
        {
            yield return text;
            yield break;
        }

        if (source is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is not null)
                {
                    yield return item;
                }
            }

            yield break;
        }

        yield return source;
    }

    private static bool GetBoolean(object? target, string memberName, bool defaultValue = false)
    {
        var value = GetMemberValue(target, memberName);
        return value is bool boolean ? boolean : defaultValue;
    }

    private static bool InvokeBooleanMethod(object? target, string methodName, bool defaultValue = false)
    {
        var value = TryInvokeParameterlessMethod(target, methodName);
        return value is bool boolean ? boolean : defaultValue;
    }

    private static int? GetNullableInt(object? target, string memberName)
    {
        var value = GetMemberValue(target, memberName);
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetTypeName(object? target)
    {
        return target?.GetType().FullName ?? target?.GetType().Name;
    }

    private readonly record struct RuntimeRoot(object GameInstance, object RunNode, object RunState);

    private readonly record struct HandCardDescriptor(string CardId, string Name, TextResolutionResult NameResolution, string? TargetType, bool Playable);
    private readonly record struct RewardOption(string Label, TextResolutionResult Resolution);
    private readonly record struct RewardPhaseAnalysis(
        bool TreatAsReward,
        bool HasRewardScreen,
        bool RewardScreenComplete,
        bool RewardScreenVisible,
        int RewardButtonCount,
        bool HasLiveEnemies,
        string RewardScreenSource,
        bool CardRewardSelectionDetected,
        string RewardSubphase,
        string DetectionSource,
        string? OverlayTopType)
    {
        public IReadOnlyDictionary<string, object?> ToMetadata()
        {
            return new Dictionary<string, object?>
            {
                ["treat_as_reward"] = TreatAsReward,
                ["has_reward_screen"] = HasRewardScreen,
                ["reward_screen_complete"] = RewardScreenComplete,
                ["reward_screen_visible"] = RewardScreenVisible,
                ["reward_button_count"] = RewardButtonCount,
                ["has_live_enemies"] = HasLiveEnemies,
                ["reward_screen_source"] = RewardScreenSource,
                ["card_reward_selection_detected"] = CardRewardSelectionDetected,
                ["reward_subphase"] = RewardSubphase,
                ["detection_source"] = DetectionSource,
                ["overlay_top_type"] = OverlayTopType,
            };
        }
    }

    private RuntimeActionResult ExecutePlayCard(object runState, ActionRequest request, LegalAction action)
    {
        var player = GetPlayers(runState).FirstOrDefault();
        var playerCombatState = GetMemberValue(player, "PlayerCombatState");
        var hand = GetMemberValue(playerCombatState, "Hand");
        var cardId = ConvertToText(GetDictionaryValue(action.Params, "card_id"));
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return new RuntimeActionResult(false, "Action does not contain a card_id.", "invalid_action");
        }

        var card = EnumerateObjects(GetMemberValue(hand, "Cards"))
            .Select((candidate, index) => new { Card = candidate, CardId = RuntimeCardIdentity.CreateCardId(candidate, index) })
            .FirstOrDefault(candidate => string.Equals(candidate.CardId, cardId, StringComparison.Ordinal))
            ?.Card;
        if (card is null)
        {
            return new RuntimeActionResult(false, $"Card '{cardId}' is no longer in hand.", "stale_action");
        }

        object? target = null;
        var targetId = ConvertToText(GetDictionaryValue(request.Params, "target_id"));
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            target = EnumerateObjects(GetMemberValue(GetCombatState(runState), "Enemies"))
                .FirstOrDefault(enemy => string.Equals(ResolveEnemyId(enemy, 0), targetId, StringComparison.Ordinal));

            if (target is null)
            {
                return new RuntimeActionResult(false, $"Target '{targetId}' is no longer available.", "invalid_target");
            }
        }

        var tryManualPlay = card.GetType().GetMethod("TryManualPlay", BindingFlags.Public | BindingFlags.Instance);
        if (tryManualPlay is null)
        {
            return new RuntimeActionResult(false, "Card.TryManualPlay is not available in this runtime.", "runtime_incompatible");
        }

        var played = tryManualPlay.Invoke(card, new[] { target }) as bool?;
        if (played != true)
        {
            return new RuntimeActionResult(false, $"Card '{cardId}' could not be played.", "play_rejected");
        }

        return new RuntimeActionResult(true, $"Played card '{cardId}'.", metadata: new Dictionary<string, object?>
        {
            ["card_id"] = cardId,
            ["target_id"] = targetId,
        });
    }

    private RuntimeActionResult ExecuteEndTurn(object runState, ActionRequest request)
    {
        var assembly = FindSts2Assembly();
        var playerCommandType = assembly?.GetType("MegaCrit.Sts2.Core.Commands.PlayerCmd");
        var method = playerCommandType?.GetMethod(
            "EndTurn",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var player = GetPlayers(runState).FirstOrDefault();
        if (method is null || player is null)
        {
            return new RuntimeActionResult(false, "PlayerCmd.EndTurn is not available.", "runtime_incompatible");
        }

        var callback = new Func<Task>(() => Task.CompletedTask);
        method.Invoke(null, new object?[] { player, false, callback });
        return new RuntimeActionResult(true, "Ended the current turn.", metadata: new Dictionary<string, object?>
        {
            ["action_type"] = "end_turn",
            ["runtime_handler"] = "PlayerCmd.EndTurn",
        });
    }

    private RuntimeActionResult ExecuteChooseReward(object runNode, ActionRequest request, LegalAction action)
    {
        var rewardIndex = GetNullableIntFromObject(GetDictionaryValue(action.Params, "reward_index"));

        var cardRewardScreen = GetCardRewardSelectionScreen(runNode);
        if (cardRewardScreen is not null)
        {
            var choices = ExtractCardRewardChoiceItems(cardRewardScreen);
            if (choices.Count == 0)
            {
                return new RuntimeActionResult(false, "No card reward choices are currently available.", "stale_action");
            }

            var selectedIndex = rewardIndex ?? 0;
            if (selectedIndex < 0 || selectedIndex >= choices.Count)
            {
                return new RuntimeActionResult(false, "Card reward selection target is no longer available.", "stale_action");
            }

            var choice = choices[selectedIndex];
            var card = ResolveCardRewardChoiceCard(choice);
            var handlers = new List<(object Target, string Label)>
            {
                (cardRewardScreen, "card_reward_screen"),
                (choice, "card_reward_choice"),
            };
            if (card is not null)
            {
                handlers.Add((card, "card_reward_card"));
            }

            var argSets = new List<object?[]>
            {
                Array.Empty<object?>(),
                new object?[] { choice },
                new object?[] { card },
                new object?[] { selectedIndex },
                new object?[] { choice, selectedIndex },
                new object?[] { card, selectedIndex },
            };

            foreach (var handler in handlers)
            {
                if (TryInvokeFirstCompatibleMethod(handler.Target, CardRewardChoiceSelectMethodNames, argSets, out var methodName))
                {
                    return new RuntimeActionResult(true, "Selected card reward.", metadata: new Dictionary<string, object?>
                    {
                        ["reward"] = ConvertToText(GetDictionaryValue(action.Params, "reward")),
                        ["reward_index"] = selectedIndex,
                        ["runtime_handler"] = $"{handler.Label}.{methodName}",
                    });
                }
            }

            return new RuntimeActionResult(false, "Card reward selection hooks are not available.", "runtime_incompatible");
        }

        var rewardScreen = GetRewardScreen(runNode);
        if (rewardScreen is null)
        {
            return new RuntimeActionResult(false, "Rewards screen is not available.", "runtime_not_ready");
        }

        var rewardButtons = GetRewardButtons(rewardScreen).ToArray();
        if (rewardButtons.Length == 0)
        {
            return new RuntimeActionResult(false, "No reward buttons are currently available.", "stale_action");
        }

        var button = rewardIndex is not null && rewardIndex.Value >= 0 && rewardIndex.Value < rewardButtons.Length
            ? rewardButtons[rewardIndex.Value]
            : rewardButtons.FirstOrDefault();
        if (button is null)
        {
            return new RuntimeActionResult(false, "Reward selection target is no longer available.", "stale_action");
        }

        var reward = GetMemberValue(button, "Reward");
        var onSelectWrapper = reward?.GetType().GetMethod("OnSelectWrapper", BindingFlags.Public | BindingFlags.Instance);
        var rewardCollectedFrom = rewardScreen.GetType().GetMethod("RewardCollectedFrom", BindingFlags.Public | BindingFlags.Instance);
        if (reward is null || onSelectWrapper is null || rewardCollectedFrom is null)
        {
            return new RuntimeActionResult(false, "Reward selection hooks are not available.", "runtime_incompatible");
        }

        _ = onSelectWrapper.Invoke(reward, null);
        rewardCollectedFrom.Invoke(rewardScreen, new[] { button });
        return new RuntimeActionResult(true, "Selected reward.", metadata: new Dictionary<string, object?>
        {
            ["reward"] = ConvertToText(GetDictionaryValue(action.Params, "reward")),
            ["reward_index"] = rewardIndex,
        });
    }

    private RuntimeActionResult ExecuteSkipReward(object runNode, ActionRequest request)
    {
        var cardRewardScreen = GetCardRewardSelectionScreen(runNode);
        if (cardRewardScreen is not null)
        {
            var argSets = new List<object?[]>
            {
                Array.Empty<object?>(),
                new object?[] { false },
                new object?[] { 0 },
            };

            if (TryInvokeFirstCompatibleMethod(cardRewardScreen, CardRewardChoiceSkipMethodNames, argSets, out var methodName))
            {
                return new RuntimeActionResult(true, "Skipped card reward selection.", metadata: new Dictionary<string, object?>
                {
                    ["action_type"] = "skip_reward",
                    ["runtime_handler"] = $"card_reward_screen.{methodName}",
                });
            }

            return new RuntimeActionResult(false, "Card reward skip hooks are not available.", "runtime_incompatible");
        }

        var rewardScreen = GetRewardScreen(runNode);
        if (rewardScreen is null)
        {
            return new RuntimeActionResult(false, "Rewards screen is not available.", "runtime_not_ready");
        }

        var rewardButtons = GetRewardButtons(rewardScreen).ToArray();
        if (rewardButtons.Length == 0)
        {
            return new RuntimeActionResult(false, "No reward buttons are currently available.", "stale_action");
        }

        var button = rewardButtons[0];
        var reward = GetMemberValue(button, "Reward");
        var onSkipped = reward?.GetType().GetMethod("OnSkipped", BindingFlags.Public | BindingFlags.Instance);
        var rewardSkippedFrom = rewardScreen.GetType().GetMethod("RewardSkippedFrom", BindingFlags.Public | BindingFlags.Instance);
        if (reward is null || onSkipped is null || rewardSkippedFrom is null)
        {
            return new RuntimeActionResult(false, "Reward skip hooks are not available.", "runtime_incompatible");
        }

        onSkipped.Invoke(reward, null);
        rewardSkippedFrom.Invoke(rewardScreen, new[] { button });
        return new RuntimeActionResult(true, "Skipped current reward.", metadata: new Dictionary<string, object?>
        {
            ["action_type"] = "skip_reward",
        });
    }

    private RuntimeActionResult ExecuteChooseMapNode(ActionRequest request, LegalAction action)
    {
        var mapScreenType = FindSts2Assembly()?.GetType("MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen");
        var mapScreen = GetMemberValue(mapScreenType, "Instance");
        if (mapScreen is null)
        {
            return new RuntimeActionResult(false, "Map screen is not available.", "runtime_not_ready");
        }

        var node = ConvertToText(GetDictionaryValue(action.Params, "node"));
        if (string.IsNullOrWhiteSpace(node))
        {
            return new RuntimeActionResult(false, "Action does not contain a node label.", "invalid_action");
        }

        var coord = ParseMapCoord(node);
        if (coord is null)
        {
            return new RuntimeActionResult(false, $"Could not parse map node '{node}'.", "invalid_action");
        }

        var mapCoordType = FindSts2Assembly()?.GetType("MegaCrit.Sts2.Core.Map.MapCoord");
        var travelMethod = mapScreen.GetType().GetMethod("TravelToMapCoord", BindingFlags.Public | BindingFlags.Instance);
        if (mapCoordType is null || travelMethod is null)
        {
            return new RuntimeActionResult(false, "Map travel hooks are not available.", "runtime_incompatible");
        }

        var mapCoord = Activator.CreateInstance(mapCoordType, coord.Value.Col, coord.Value.Row);
        _ = travelMethod.Invoke(mapScreen, new[] { mapCoord });
        return new RuntimeActionResult(true, $"Traveling to map node '{node}'.", metadata: new Dictionary<string, object?>
        {
            ["node"] = node,
            ["coord"] = $"{coord.Value.Col},{coord.Value.Row}",
        });
    }

    private static object? GetDictionaryValue(IReadOnlyDictionary<string, object?> dictionary, string key)
    {
        return dictionary.TryGetValue(key, out var value) ? value : null;
    }

    private static int? GetNullableIntFromObject(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static (int Col, int Row)? ParseMapCoord(string nodeLabel)
    {
        var atIndex = nodeLabel.LastIndexOf('@');
        if (atIndex < 0 || atIndex + 1 >= nodeLabel.Length)
        {
            return null;
        }

        var coordinate = nodeLabel[(atIndex + 1)..].Split(',');
        if (coordinate.Length != 2 ||
            !int.TryParse(coordinate[0], out var col) ||
            !int.TryParse(coordinate[1], out var row))
        {
            return null;
        }

        return (col, row);
    }
}
