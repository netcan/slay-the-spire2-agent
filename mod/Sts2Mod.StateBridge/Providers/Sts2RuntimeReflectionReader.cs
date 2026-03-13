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

    private RuntimeWindowContext BuildCombatWindow(object runState)
    {
        var player = BuildPlayerState(runState);
        var enemies = BuildEnemies(runState);
        var metadata = CreateBaseMetadata(runState, DecisionPhase.Combat);
        var actions = new List<RuntimeActionDefinition>();
        var liveEnemyIds = enemies.Where(enemy => enemy.IsAlive).Select(enemy => enemy.EnemyId).ToArray();

        foreach (var card in GetHandCardDescriptors(runState).Where(card => card.Playable))
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

            actions.Add(new RuntimeActionDefinition(
                "play_card",
                $"Play {card.Name}",
                parameters,
                BuildTargetConstraints(card.TargetType, liveEnemyIds),
                new Dictionary<string, object?> { ["playable"] = true }));
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
        var rewards = ExtractRewards(runNode);
        var metadata = CreateBaseMetadata(runState, DecisionPhase.Reward);
        metadata["reward_count"] = rewards.Count;
        var actions = rewards
            .Select((reward, index) => new RuntimeActionDefinition(
                "choose_reward",
                $"Choose {reward}",
                new Dictionary<string, object?> { ["reward"] = reward, ["reward_index"] = index }))
            .ToList();

        if (rewards.Count > 0)
        {
            actions.Add(new RuntimeActionDefinition("skip_reward", "Skip Reward", new Dictionary<string, object?>()));
        }

        return new RuntimeWindowContext(
            DecisionPhase.Reward,
            BuildPlayerState(runState),
            Array.Empty<RuntimeEnemyState>(),
            rewards,
            Array.Empty<string>(),
            Terminal: false,
            Metadata: metadata,
            Actions: actions);
    }

    private RuntimeWindowContext BuildMapWindow(object runState)
    {
        var mapNodes = ExtractMapNodes(runState);
        var metadata = CreateBaseMetadata(runState, DecisionPhase.Map);
        metadata["node_count"] = mapNodes.Count;
        var actions = mapNodes
            .Select(node => new RuntimeActionDefinition(
                "choose_map_node",
                $"Choose {node}",
                new Dictionary<string, object?> { ["node"] = node }))
            .ToList();

        return new RuntimeWindowContext(
            DecisionPhase.Map,
            BuildPlayerState(runState),
            Array.Empty<RuntimeEnemyState>(),
            Array.Empty<string>(),
            mapNodes,
            Terminal: false,
            Metadata: metadata,
            Actions: actions);
    }

    private RuntimeWindowContext BuildTerminalWindow(object runState)
    {
        var metadata = CreateBaseMetadata(runState, DecisionPhase.Terminal);
        metadata["result"] = GetBoolean(runState, "IsGameOver") ? "game_over" : "terminal";
        return new RuntimeWindowContext(
            DecisionPhase.Terminal,
            BuildPlayerState(runState),
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

    private RuntimePlayerState? BuildPlayerState(object runState)
    {
        var player = GetPlayers(runState).FirstOrDefault();
        if (player is null)
        {
            return null;
        }

        var creature = GetMemberValue(player, "Creature");
        var playerCombatState = GetMemberValue(player, "PlayerCombatState");
        var handCards = ExtractCards(GetMemberValue(playerCombatState, "Hand"));
        var relics = ExtractLabels(GetMemberValue(player, "Relics"));
        var potions = ExtractLabels(GetMemberValue(player, "PotionSlots"));

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

    private IReadOnlyList<RuntimeEnemyState> BuildEnemies(object runState)
    {
        var combatState = GetCombatState(runState);
        if (combatState is null)
        {
            return Array.Empty<RuntimeEnemyState>();
        }

        return EnumerateObjects(GetMemberValue(combatState, "Enemies"))
            .Select((enemy, index) => new RuntimeEnemyState(
                EnemyId: ResolveEnemyId(enemy, index),
                Name: ConvertToText(GetMemberValue(enemy, "Name")) ?? $"enemy_{index}",
                Hp: GetNullableInt(enemy, "CurrentHp") ?? 0,
                MaxHp: GetNullableInt(enemy, "MaxHp") ?? 0,
                Block: GetNullableInt(enemy, "Block") ?? 0,
                Intent: ResolveEnemyIntent(enemy),
                IsAlive: GetBoolean(enemy, "IsAlive", defaultValue: true)))
            .ToArray();
    }

    private IReadOnlyList<HandCardDescriptor> GetHandCardDescriptors(object runState)
    {
        var player = GetPlayers(runState).FirstOrDefault();
        var playerCombatState = GetMemberValue(player, "PlayerCombatState");
        var hand = GetMemberValue(playerCombatState, "Hand");
        return EnumerateObjects(GetMemberValue(hand, "Cards"))
            .Select((card, index) => new HandCardDescriptor(
                ResolveCardId(card, index),
                ConvertToText(GetMemberValue(card, "Title"))
                    ?? ConvertToText(GetMemberValue(card, "Name"))
                    ?? $"card_{index}",
                ConvertToText(GetMemberValue(card, "TargetType")),
                GetBoolean(card, "IsPlayable", defaultValue: true)))
            .ToArray();
    }

    private IReadOnlyList<RuntimeCard> ExtractCards(object? pile)
    {
        return EnumerateObjects(GetMemberValue(pile, "Cards"))
            .Select((card, index) => new RuntimeCard(
                CardId: ResolveCardId(card, index),
                Name: ConvertToText(GetMemberValue(card, "Title"))
                      ?? ConvertToText(GetMemberValue(card, "Name"))
                      ?? $"card_{index}",
                Cost: ResolveCardCost(card),
                Playable: GetBoolean(card, "IsPlayable", defaultValue: true)))
            .ToArray();
    }

    private int CountCards(object? pile)
    {
        return EnumerateObjects(GetMemberValue(pile, "Cards")).Count();
    }

    private List<string> ExtractRewards(object runNode)
    {
        var rewardScreen = GetRewardScreen(runNode);
        if (rewardScreen is null)
        {
            return new List<string>();
        }

        return EnumerateObjects(GetMemberValue(rewardScreen, "_rewardButtons"))
            .Select(button => DescribeReward(GetMemberValue(button, "Reward")))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!)
            .ToList();
    }

    private List<string> ExtractMapNodes(object runState)
    {
        var currentMapPoint = GetMemberValue(runState, "CurrentMapPoint");
        var nodes = EnumerateObjects(GetMemberValue(currentMapPoint, "Children"))
            .Select(DescribeMapNode)
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
            .Select(DescribeMapNode)
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

    private static List<string> ExtractLabels(object? collection)
    {
        return EnumerateObjects(collection)
            .Select(DescribeInventoryItem)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!)
            .ToList();
    }

    private static string? DescribeInventoryItem(object? item)
    {
        return ConvertToText(GetMemberValue(item, "Potion"))
               ?? ConvertToText(GetMemberValue(item, "Relic"))
               ?? ConvertToText(GetMemberValue(item, "Name"))
               ?? ConvertToText(GetMemberValue(item, "Title"))
               ?? ConvertToText(GetMemberValue(item, "Description"))
               ?? ConvertToText(item);
    }

    private static string? DescribeReward(object? reward)
    {
        return ConvertToText(GetMemberValue(reward, "Description"))
               ?? ConvertToText(GetMemberValue(reward, "RewardType"))
               ?? ConvertToText(reward);
    }

    private static string? DescribeMapNode(object? mapPoint)
    {
        var pointType = ConvertToText(GetMemberValue(mapPoint, "PointType")) ?? "unknown";
        var coord = DescribeMapCoord(GetMemberValue(mapPoint, "coord"));
        return $"{pointType}@{coord}";
    }

    private static string DescribeMapCoord(object? coord)
    {
        var col = GetNullableInt(coord, "col") ?? -1;
        var row = GetNullableInt(coord, "row") ?? -1;
        return $"{col},{row}";
    }

    private static string? ConvertToText(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        var name = GetTypeName(value);
        if (name is not null && name.Contains("LocString", StringComparison.Ordinal))
        {
            var localized = value.ToString();
            return string.IsNullOrWhiteSpace(localized) ? null : localized;
        }

        var textValue = value.ToString();
        if (string.IsNullOrWhiteSpace(textValue))
        {
            return null;
        }

        if (string.Equals(textValue, value.GetType().FullName, StringComparison.Ordinal))
        {
            return null;
        }

        return textValue;
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

    private readonly record struct HandCardDescriptor(string CardId, string Name, string? TargetType, bool Playable);
}
