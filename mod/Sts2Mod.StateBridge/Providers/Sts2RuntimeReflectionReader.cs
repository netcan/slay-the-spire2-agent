using System.Collections;
using System.Reflection;
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
            DecisionPhase.Map => BuildMapWindow(root.RunState),
            DecisionPhase.Terminal => BuildTerminalWindow(root.RunState),
            _ => BuildCombatWindow(root.RunState),
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
            "end_turn" => ExecuteEndTurn(request),
            "choose_reward" => ExecuteChooseReward(root.RunNode, request, action),
            "skip_reward" => ExecuteSkipReward(root.RunNode, request),
            "choose_map_node" => ExecuteChooseMapNode(request, action),
            _ => new RuntimeActionResult(false, $"Action type '{action.Type}' is not supported yet.", "unsupported_action"),
        };
    }

    private RuntimeWindowContext BuildCombatWindow(object runState)
    {
        var textDiagnostics = new TextDiagnosticsCollector();
        var player = BuildPlayerState(runState, textDiagnostics);
        var enemies = BuildEnemies(runState, textDiagnostics);
        var metadata = CreateBaseMetadata(runState, DecisionPhase.Combat);
        var actions = new List<RuntimeActionDefinition>();
        var liveEnemyIds = enemies.Where(enemy => enemy.IsAlive).Select(enemy => enemy.EnemyId).ToArray();

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
        var rewards = ExtractRewards(runNode, textDiagnostics);
        var player = BuildPlayerState(runState, textDiagnostics);
        var metadata = CreateBaseMetadata(runState, DecisionPhase.Reward);
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
            actions.Add(new RuntimeActionDefinition("skip_reward", "Skip Reward", new Dictionary<string, object?>()));
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

    private RuntimeWindowContext BuildMapWindow(object runState)
    {
        var textDiagnostics = new TextDiagnosticsCollector();
        var mapNodes = ExtractMapNodes(runState, textDiagnostics);
        var player = BuildPlayerState(runState, textDiagnostics);
        var metadata = CreateBaseMetadata(runState, DecisionPhase.Map);
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

    private RuntimeWindowContext BuildTerminalWindow(object runState)
    {
        var textDiagnostics = new TextDiagnosticsCollector();
        var player = BuildPlayerState(runState, textDiagnostics);
        var metadata = CreateBaseMetadata(runState, DecisionPhase.Terminal);
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
                    ResolveCardId(card, index),
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
                CardId: ResolveCardId(card, index),
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
        if (rewardScreen is null)
        {
            return new List<RewardOption>();
        }

        return EnumerateObjects(GetMemberValue(rewardScreen, "_rewardButtons"))
            .Select((button, index) => DescribeReward(GetMemberValue(button, "Reward"), $"rewards[{index}]", textDiagnostics))
            .OfType<RewardOption>()
            .Where(reward => !string.IsNullOrWhiteSpace(reward.Label))
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

        var rewardScreen = GetRewardScreen(runNode);
        if (rewardScreen is not null && !GetBoolean(rewardScreen, "IsComplete"))
        {
            return DecisionPhase.Reward;
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

        return DecisionPhase.Combat;
    }

    private object? GetRewardScreen(object runNode)
    {
        var screenTracker = GetMemberValue(runNode, "ScreenStateTracker");
        return GetMemberValue(screenTracker, "_connectedRewardsScreen");
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

    private static string ResolveCardId(object? card, int index)
    {
        return ConvertToText(GetMemberValue(card, "ModelId"))
               ?? ConvertToText(GetMemberValue(card, "Title"))
               ?? ConvertToText(GetMemberValue(card, "Name"))
               ?? $"card_{index}";
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
            .FirstOrDefault(candidate => string.Equals(ResolveCardId(candidate, 0), cardId, StringComparison.Ordinal));
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

    private RuntimeActionResult ExecuteEndTurn(ActionRequest request)
    {
        var managerType = FindSts2Assembly()?.GetType("MegaCrit.Sts2.Core.Combat.CombatManager");
        var manager = GetMemberValue(managerType, "Instance");
        var method = manager?.GetType().GetMethod("OnEndedTurnLocally", BindingFlags.Public | BindingFlags.Instance);
        if (method is null || manager is null)
        {
            return new RuntimeActionResult(false, "CombatManager.OnEndedTurnLocally is not available.", "runtime_incompatible");
        }

        method.Invoke(manager, null);
        return new RuntimeActionResult(true, "Ended the current turn.", metadata: new Dictionary<string, object?>
        {
            ["action_type"] = "end_turn",
        });
    }

    private RuntimeActionResult ExecuteChooseReward(object runNode, ActionRequest request, LegalAction action)
    {
        var rewardScreen = GetRewardScreen(runNode);
        if (rewardScreen is null)
        {
            return new RuntimeActionResult(false, "Rewards screen is not available.", "runtime_not_ready");
        }

        var rewardButtons = EnumerateObjects(GetMemberValue(rewardScreen, "_rewardButtons")).ToArray();
        if (rewardButtons.Length == 0)
        {
            return new RuntimeActionResult(false, "No reward buttons are currently available.", "stale_action");
        }

        var rewardIndex = GetNullableIntFromObject(GetDictionaryValue(action.Params, "reward_index"));
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
        var rewardScreen = GetRewardScreen(runNode);
        if (rewardScreen is null)
        {
            return new RuntimeActionResult(false, "Rewards screen is not available.", "runtime_not_ready");
        }

        var rewardButtons = EnumerateObjects(GetMemberValue(rewardScreen, "_rewardButtons")).ToArray();
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
