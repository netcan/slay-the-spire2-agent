using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Sts2Mod.StateBridge.Configuration;
using Sts2Mod.StateBridge.Contracts;
using Sts2Mod.StateBridge.Logging;

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
    private const string PhaseMenu = DecisionPhase.Menu;
    private static readonly string[] MenuContinueLabelHints = { "continue", "resume", "继续", "继续游戏", "继续旅程" };
    private static readonly string[] RewardAdvanceLabelHints = { "advance", "continue", "proceed", "next", "forward", "前进", "继续", "下一步" };
    private static readonly string[] MenuNewRunLabelHints = { "new run", "new game", "start new", "开始新", "新游戏", "新旅程" };
    private static readonly string[] MenuConfirmLabelHints = { "start", "begin", "confirm", "ok", "开始", "确认", "确定" };
    private static readonly string[] MenuDangerLabelHints = { "exit", "quit", "abandon", "delete", "退出", "放弃", "删除" };
    private static readonly string[] MenuCharacterLabelHints =
    {
        // Keep this list conservative; only emit select_character when we are fairly confident.
        "ironclad", "silent", "defect", "watcher",
        "铁甲", "静默", "机器人", "观者",
    };
    private static readonly string[] MenuActivationMethodNames =
    {
        "Click",
        "Press",
        "Activate",
        "OnPressed",
        "OnPress",
        "Confirm",
        "Select",
    };
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
        "_cardHolders",
        "CardHolders",
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
        "CardModel",
        "CardNode",
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
        "CancelFree",
        "Skip",
        "OnSkip",
        "OnSkipped",
        "SkipReward",
        "Cancel",
        "OnCancel",
        "Close",
        "Dismiss",
    };
    private static readonly string[] RewardAdvanceMethodNames =
    {
        "Advance",
        "Continue",
        "Proceed",
        "Next",
        "GoNext",
        "OnContinuePressed",
        "OnContinueButtonPressed",
        "OnProceedButtonPressed",
        "OnProceedPressed",
    };
    private static readonly Regex RichTextTagRegex = new(@"\[(?:/?)[^\]]+\]", RegexOptions.Compiled);
    private static readonly Regex RichTextPairRegex = new(@"\[(?<tag>[A-Za-z0-9_]+)\](?<content>.*?)\[/\k<tag>\]", RegexOptions.Compiled);
    private static readonly Regex PlaceholderRegex = new(@"\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::(?<expr>[^}]+))?\}", RegexOptions.Compiled);
    private static readonly string[] DescriptionSearchNestedMembers =
    {
        "Data",
        "Definition",
        "RuntimeData",
        "RuntimeState",
        "DynamicData",
        "DynamicState",
        "CardData",
        "CardModel",
        "CardNode",
        "Model",
        "Card",
        "CardState",
        "CardDefinition",
        "Stats",
        "State",
        "Effect",
        "Effects",
        "Action",
        "Actions",
        "Preview",
        "PreviewData",
        "CombatData",
        "Computed",
        "Values",
        "DynamicVars",
        "CanonicalVars",
    };
    private static readonly IReadOnlyDictionary<string, string[]> DescriptionVariableMemberAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["damage"] = new[] { "Damage", "CurrentDamage", "DisplayedDamage", "PreviewDamage", "FinalDamage", "ModifiedDamage", "ComputedDamage", "BaseDamage", "AttackDamage", "DamageAmount" },
            ["block"] = new[] { "Block", "CurrentBlock", "DisplayedBlock", "PreviewBlock", "FinalBlock", "ModifiedBlock", "ComputedBlock", "BaseBlock", "BlockAmount" },
            ["draw"] = new[] { "Draw", "DrawAmount", "Cards", "CardsToDraw", "DrawCount", "CardDraw", "CardsDrawn" },
            ["strength"] = new[] { "Strength", "StrengthAmount" },
            ["magic"] = new[] { "MagicNumber", "Magic", "MagicValue", "ModifiedMagicNumber", "CurrentMagicNumber", "Value" },
            ["energy"] = new[] { "Energy", "EnergyGain", "EnergyAmount", "Cost", "CurrentEnergyCost" },
            ["vulnerable"] = new[] { "Vulnerable", "VulnerableAmount" },
            ["weak"] = new[] { "Weak", "WeakAmount" },
            ["frail"] = new[] { "Frail", "FrailAmount" },
            ["dexterity"] = new[] { "Dexterity", "DexterityAmount" },
            ["amount"] = new[] { "Amount", "Stacks", "Value" },
        };
    private static readonly IReadOnlyDictionary<string, GlossarySpec> KnownGlossarySpecs =
        new Dictionary<string, GlossarySpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["block"] = new("block", "格挡", "在下个回合前，阻挡伤害。", new[] { "格挡", "block" }),
            ["strength"] = new("strength", "力量", "使攻击造成更多伤害。", new[] { "力量", "strength" }),
            ["vulnerable"] = new("vulnerable", "易伤", "受到的攻击伤害提高。", new[] { "易伤", "vulnerable" }),
            ["weak"] = new("weak", "虚弱", "攻击造成的伤害降低。", new[] { "虚弱", "weak" }),
            ["frail"] = new("frail", "脆弱", "从卡牌获得的格挡减少。", new[] { "脆弱", "frail" }),
            ["dexterity"] = new("dexterity", "敏捷", "会影响获得的格挡量。", new[] { "敏捷", "dexterity" }),
            ["damage"] = new("damage", "伤害", "会降低目标生命值。", new[] { "伤害", "damage" }),
            ["draw"] = new("draw", "抽牌", "将牌堆中的牌加入手牌。", new[] { "抽", "draw" }),
            ["energy"] = new("energy", "能量", "用于打出卡牌。", new[] { "能量", "energy" }),
            ["exhaust"] = new("exhaust", "消耗", "本场战斗中移出该牌。", new[] { "消耗", "exhaust" }),
            ["discard"] = new("discard", "弃牌", "将手牌移入弃牌堆。", new[] { "弃", "discard" }),
            ["status"] = new("status", "状态", "战斗中加入或产生的负面牌。", new[] { "状态", "status" }),
            ["debuff"] = new("debuff", "负面效果", "会削弱目标。", new[] { "负面效果", "debuff" }),
            ["buff"] = new("buff", "增益", "会强化目标。", new[] { "增益", "buff" }),
            ["metallicize"] = new("metallicize", "金属化", "回合结束时获得格挡。", new[] { "金属化", "metallicize" }),
        };
    private readonly BridgeOptions _options;
    private readonly InstallationProbeResult _probe;
    private readonly IBridgeLogger? _logger;

    public Sts2RuntimeReflectionReader(BridgeOptions options, InstallationProbeResult probe, IBridgeLogger? logger = null)
    {
        _options = options;
        _probe = probe;
        _logger = logger;
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

        var phase = root.RunNode is not null && root.RunState is not null
            ? DetectPhase(root.RunNode, root.RunState)
            : PhaseMenu;
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

        if (root.RunNode is null || root.RunState is null)
        {
            return BuildMenuWindow(root.GameInstance);
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

        if (root.RunNode is null || root.RunState is null)
        {
            return action.Type switch
            {
                "continue_run" => ExecuteMenuAction(root.GameInstance, request, action, "continue_run"),
                "start_new_run" => ExecuteMenuAction(root.GameInstance, request, action, "start_new_run"),
                "select_character" => ExecuteMenuAction(root.GameInstance, request, action, "select_character"),
                "confirm_start_run" => ExecuteMenuAction(root.GameInstance, request, action, "confirm_start_run"),
                _ => new RuntimeActionResult(false, "No active run is available yet.", "runtime_not_ready"),
            };
        }

        return action.Type switch
        {
            "play_card" => ExecutePlayCard(root.RunState, request, action),
            "end_turn" => ExecuteEndTurn(root.RunState, request),
            "choose_reward" => ExecuteChooseReward(root.RunNode, request, action),
            "skip_reward" => ExecuteSkipReward(root.RunNode, request),
            "advance_reward" => ExecuteAdvanceReward(root.RunNode, request, action),
            "choose_map_node" => ExecuteChooseMapNode(request, action),
            _ => new RuntimeActionResult(false, $"Action type '{action.Type}' is not supported yet.", "unsupported_action"),
        };
    }

    private RuntimeWindowContext BuildMenuWindow(object gameInstance)
    {
        var textDiagnostics = new TextDiagnosticsCollector();
        var metadata = CreateMenuMetadata(gameInstance);
        var buttons = DiscoverMenuButtons(gameInstance, textDiagnostics, metadata);
        LogTextDiagnostics("menu", textDiagnostics);

        var actions = new List<RuntimeActionDefinition>();
        foreach (var candidate in buttons)
        {
            var parameters = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(candidate.Label))
            {
                parameters["button_label"] = candidate.Label;
            }

            if (string.Equals(candidate.Kind, "select_character", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(candidate.CharacterId))
            {
                parameters["character_id"] = candidate.CharacterId;
                parameters["character_label"] = candidate.Label;
            }

            actions.Add(new RuntimeActionDefinition(
                candidate.Kind,
                candidate.Label,
                parameters,
                Metadata: candidate.Diagnostics));
        }

        if (actions.Count == 0)
        {
            metadata["menu_action_suppressed"] = true;
            metadata["menu_action_suppressed_reason"] = metadata.TryGetValue("menu_action_suppressed_reason", out var reason)
                ? reason
                : "no_safe_menu_actions_detected";
        }

        return new RuntimeWindowContext(
            DecisionPhase.Menu,
            Player: null,
            Enemies: Array.Empty<RuntimeEnemyState>(),
            Rewards: Array.Empty<string>(),
            MapNodes: Array.Empty<string>(),
            Terminal: false,
            Metadata: metadata,
            Actions: actions);
    }

    private sealed record MenuActionCandidate(
        string Kind,
        string Label,
        string? CharacterId,
        IReadOnlyDictionary<string, object?> Diagnostics);

    private List<MenuActionCandidate> DiscoverMenuButtons(object gameInstance, TextDiagnosticsCollector textDiagnostics, Dictionary<string, object?> metadata)
    {
        var roots = EnumerateMenuRoots(gameInstance).ToList();
        metadata["menu_root_count"] = roots.Count;

        var discovered = new List<(object Node, string Label, string Source)>();
        var seenNodes = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var suppressedNonInteractable = 0;
        var suppressedUnlabeled = 0;
        foreach (var root in roots)
        {
            var rootNode = root.Node;
            if (rootNode is null)
            {
                continue;
            }

            var rootType = GetTypeName(rootNode) ?? string.Empty;
            foreach (var node in EnumerateNodeDescendants(rootNode, maxDepth: 7).Prepend(rootNode))
            {
                if (!seenNodes.Add(node))
                {
                    continue;
                }

                if (!IsPotentialMenuButton(node))
                {
                    continue;
                }

                if (!IsMenuNodeInteractable(node))
                {
                    suppressedNonInteractable += 1;
                    continue;
                }

                var label = GetMenuNodeLabel(node, textDiagnostics);
                if (string.IsNullOrWhiteSpace(label))
                {
                    suppressedUnlabeled += 1;
                    continue;
                }

                if (IsDangerLabel(label))
                {
                    continue;
                }

                discovered.Add((node, label!, root.Source));
                if (discovered.Count >= 128)
                {
                    break;
                }
            }

            if (discovered.Count >= 128)
            {
                metadata["menu_scan_truncated"] = true;
                metadata["menu_scan_truncated_root_type"] = rootType;
                break;
            }
        }

        metadata["menu_button_candidate_count"] = discovered.Count;
        metadata["menu_suppressed_non_interactable_count"] = suppressedNonInteractable;
        metadata["menu_suppressed_unlabeled_count"] = suppressedUnlabeled;

        // Classify actions conservatively.
        var actions = new List<MenuActionCandidate>();
        var windowKind = "main_menu";

        var continueButton = discovered.FirstOrDefault(item => IsContinueLabel(item.Label));
        if (continueButton.Node is not null)
        {
            actions.Add(new MenuActionCandidate(
                Kind: "continue_run",
                Label: continueButton.Label,
                CharacterId: null,
                Diagnostics: new Dictionary<string, object?>
                {
                    ["menu_detection_source"] = continueButton.Source,
                    ["menu_target_type"] = GetTypeName(continueButton.Node),
                }));
        }

        var newRunButton = discovered.FirstOrDefault(item => IsNewRunLabel(item.Label));
        if (newRunButton.Node is not null)
        {
            actions.Add(new MenuActionCandidate(
                Kind: "start_new_run",
                Label: newRunButton.Label,
                CharacterId: null,
                Diagnostics: new Dictionary<string, object?>
                {
                    ["menu_detection_source"] = newRunButton.Source,
                    ["menu_target_type"] = GetTypeName(newRunButton.Node),
                }));
        }

        // Only attempt character/confirm actions when the UI looks like a new run setup flow.
        var characterButtons = discovered
            .Where(item => IsCharacterLabel(item.Label) || IsCharacterNode(item.Node))
            .Select(item => new
            {
                item.Node,
                item.Label,
                item.Source,
                CharacterId = NormalizeCharacterId(item.Node, item.Label),
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.CharacterId))
            .Take(8)
            .ToList();

        if (characterButtons.Count > 0)
        {
            windowKind = "new_run_setup";
            foreach (var button in characterButtons)
            {
                actions.Add(new MenuActionCandidate(
                    Kind: "select_character",
                    Label: button.Label,
                    CharacterId: button.CharacterId,
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["menu_detection_source"] = button.Source,
                        ["menu_target_type"] = GetTypeName(button.Node),
                    }));
            }

            var confirm = discovered.FirstOrDefault(item => IsConfirmLabel(item.Label));
            if (confirm.Node is not null)
            {
                actions.Add(new MenuActionCandidate(
                    Kind: "confirm_start_run",
                    Label: confirm.Label,
                    CharacterId: null,
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["menu_detection_source"] = confirm.Source,
                        ["menu_target_type"] = GetTypeName(confirm.Node),
                    }));
            }
        }

        metadata["window_kind"] = windowKind;

        // If we found a lot of buttons but none classified as safe actions, explain why.
        if (discovered.Count > 0 && actions.Count == 0)
        {
            metadata["menu_action_suppressed_reason"] = "candidates_found_but_no_safe_classification";
        }

        return actions;
    }

    private IEnumerable<(object? Node, string Source)> EnumerateMenuRoots(object gameInstance)
    {
        // Prefer overlay stack (works even when runNode is not available).
        var overlayStack = GetMemberValue(GetOverlayStackType(), "Instance");
        var overlayTop = TryInvokeParameterlessMethod(overlayStack, "Peek");
        if (overlayTop is not null)
        {
            yield return (overlayTop, "overlay_stack.peek");
        }

        // Commonly useful roots on NGame.Instance.
        foreach (var member in new[] { "GlobalUi", "UI", "Ui", "MainMenu", "_mainMenu", "Menu", "Menus", "Screen", "Screens", "Root" })
        {
            var candidate = GetMemberValue(gameInstance, member);
            if (candidate is not null)
            {
                yield return (candidate, $"game_instance.{member}");
            }
        }

        yield return (gameInstance, "game_instance");
    }

    private static bool IsPotentialMenuButton(object node)
    {
        var typeName = GetTypeName(node) ?? string.Empty;
        if (typeName.Contains("Button", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var type = node.GetType();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var hasActivationMethod = methods.Any(method => MenuActivationMethodNames.Contains(method.Name, StringComparer.Ordinal));
        if (!hasActivationMethod)
        {
            return false;
        }

        return type.GetProperty("Text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null ||
               type.GetProperty("Label", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null ||
               type.GetProperty("Title", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null ||
               type.GetProperty("Name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;
    }

    private static bool IsMenuNodeInteractable(object node)
    {
        if (GetMemberValue(node, "Visible") is bool visible && !visible)
        {
            return false;
        }

        if (TryInvokeParameterlessMethod(node, "IsVisibleInTree") is bool visibleInTree && !visibleInTree)
        {
            return false;
        }

        if (GetMemberValue(node, "IsEnabled") is bool isEnabled && !isEnabled)
        {
            return false;
        }

        if (GetMemberValue(node, "_isEnabled") is bool privateEnabled && !privateEnabled)
        {
            return false;
        }

        if (GetMemberValue(node, "Disabled") is bool disabled && disabled)
        {
            return false;
        }

        if (GetMemberValue(node, "IsLocked") is bool isLocked && isLocked)
        {
            return false;
        }

        if (GetMemberValue(node, "_isLocked") is bool privateLocked && privateLocked)
        {
            return false;
        }

        return true;
    }

    private static string? GetMenuNodeLabel(object node, TextDiagnosticsCollector? textDiagnostics = null)
    {
        var label = ConvertToText(
            GetMemberValue(node, "Text") ?? GetMemberValue(node, "Label") ?? GetMemberValue(node, "Title"),
            "menu.button.label",
            textDiagnostics,
            "Text",
            "Label",
            "Title");
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label.Trim();
        }

        if (!CanUseMenuNodeNameFallback(node))
        {
            return null;
        }

        label = ConvertToText(
            GetMemberValue(node, "Name") ?? node,
            "menu.button.name",
            textDiagnostics,
            "Name");
        return string.IsNullOrWhiteSpace(label) ? null : label.Trim();
    }

    private static bool CanUseMenuNodeNameFallback(object node)
    {
        var typeName = GetTypeName(node) ?? string.Empty;
        return typeName.Contains("Button", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("CharacterSelect", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("Confirm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDangerLabel(string label)
    {
        var normalized = NormalizeLabel(label);
        return MenuDangerLabelHints.Any(hint => normalized.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsContinueLabel(string label)
    {
        var normalized = NormalizeLabel(label);
        return MenuContinueLabelHints.Any(hint => normalized.Contains(hint, StringComparison.OrdinalIgnoreCase)) &&
               !IsNewRunLabel(label);
    }

    private static bool IsNewRunLabel(string label)
    {
        var normalized = NormalizeLabel(label);
        if (MenuNewRunLabelHints.Any(hint => normalized.Contains(hint, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // "Start" is ambiguous; only treat it as new-run when paired with "new"/"run"/"game"/"新".
        if (normalized.Contains("start", StringComparison.OrdinalIgnoreCase) || normalized.Contains("开始", StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Contains("new", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("run", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("game", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("新", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsConfirmLabel(string label)
    {
        var normalized = NormalizeLabel(label);
        return MenuConfirmLabelHints.Any(hint => normalized.Contains(hint, StringComparison.OrdinalIgnoreCase)) &&
               !IsDangerLabel(label);
    }

    private static bool IsCharacterLabel(string label)
    {
        var normalized = NormalizeLabel(label);
        return MenuCharacterLabelHints.Any(hint => normalized.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCharacterNode(object node)
    {
        var typeName = GetTypeName(node) ?? string.Empty;
        return typeName.Contains("Character", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("Hero", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("Class", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLabel(string label)
    {
        return label.Trim().ToLowerInvariant();
    }

    private static string? NormalizeCharacterId(object node, string label)
    {
        var candidate = ConvertToText(GetMemberValue(node, "CharacterId") ?? GetMemberValue(node, "Id") ?? GetMemberValue(node, "Character") ?? label);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalized = candidate.Trim().ToLowerInvariant();
        // Keep it stable and readable for action params.
        normalized = new string(normalized.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private RuntimeActionResult ExecuteMenuAction(object gameInstance, ActionRequest request, LegalAction action, string expectedKind)
    {
        var textDiagnostics = new TextDiagnosticsCollector();
        var metadata = new Dictionary<string, object?>();
        var candidates = DiscoverMenuButtons(gameInstance, textDiagnostics, metadata);
        var label = ConvertToText(GetDictionaryValue(action.Params, "button_label"));
        var characterId = ConvertToText(GetDictionaryValue(action.Params, "character_id"));

        MenuActionCandidate? target = null;
        foreach (var candidate in candidates)
        {
            if (!string.Equals(candidate.Kind, expectedKind, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(characterId))
            {
                if (!string.Equals(candidate.CharacterId, characterId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }
            else if (!string.IsNullOrWhiteSpace(label))
            {
                if (!string.Equals(candidate.Label, label, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            target = candidate;
            break;
        }

        if (target is null)
        {
            return new RuntimeActionResult(false, "Menu target is no longer available.", "stale_action", metadata: new Dictionary<string, object?>
            {
                ["action_type"] = expectedKind,
                ["menu_label"] = label,
                ["menu_character_id"] = characterId,
                ["menu_candidate_count"] = candidates.Count,
                ["menu_diagnostics"] = metadata,
            });
        }

        // Re-scan roots and find a matching node to click based on label/id.
        if (!TryResolveMenuTargetNode(gameInstance, expectedKind, label, characterId, out var node, out var resolveMetadata))
        {
            return new RuntimeActionResult(false, "Menu target node could not be resolved.", "runtime_incompatible", resolveMetadata);
        }

        if (!TryExecuteMenuActionHighLevel(gameInstance, expectedKind, node, out var handler) &&
            !TryActivateMenuNode(node, out handler))
        {
            return new RuntimeActionResult(false, "Menu target node is not clickable.", "not_clickable", new Dictionary<string, object?>
            {
                ["runtime_handler"] = "menu_node.activate",
                ["target_type"] = GetTypeName(node),
                ["expected_kind"] = expectedKind,
            });
        }

        return new RuntimeActionResult(true, $"Executed menu action '{expectedKind}'.", metadata: new Dictionary<string, object?>
        {
            ["action_type"] = expectedKind,
            ["runtime_handler"] = handler,
            ["target_type"] = GetTypeName(node),
            ["resolve_metadata"] = resolveMetadata,
        });
    }

    private bool TryResolveMenuTargetNode(object gameInstance, string expectedKind, string? label, string? characterId, out object node, out IReadOnlyDictionary<string, object?> metadata)
    {
        foreach (var root in EnumerateMenuRoots(gameInstance))
        {
            if (root.Node is null)
            {
                continue;
            }

            foreach (var child in EnumerateNodeDescendants(root.Node, maxDepth: 7).Prepend(root.Node))
            {
                if (!IsPotentialMenuButton(child))
                {
                    continue;
                }

                if (!IsMenuNodeInteractable(child))
                {
                    continue;
                }

                var childLabel = GetMenuNodeLabel(child);
                if (string.IsNullOrWhiteSpace(childLabel))
                {
                    continue;
                }

                childLabel = childLabel.Trim();

                if (!string.IsNullOrWhiteSpace(characterId) && string.Equals(expectedKind, "select_character", StringComparison.Ordinal))
                {
                    var id = NormalizeCharacterId(child, childLabel);
                    if (!string.IsNullOrWhiteSpace(id) && string.Equals(id, characterId, StringComparison.OrdinalIgnoreCase))
                    {
                        node = child;
                        metadata = new Dictionary<string, object?>
                        {
                            ["menu_detection_source"] = root.Source,
                            ["menu_target_type"] = GetTypeName(child),
                            ["menu_character_id"] = id,
                        };
                        return true;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(label) && string.Equals(childLabel, label, StringComparison.OrdinalIgnoreCase))
                {
                    node = child;
                    metadata = new Dictionary<string, object?>
                    {
                        ["menu_detection_source"] = root.Source,
                        ["menu_target_type"] = GetTypeName(child),
                        ["menu_label"] = childLabel,
                    };
                    return true;
                }
            }
        }

        node = default!;
        metadata = new Dictionary<string, object?>
        {
            ["expected_kind"] = expectedKind,
            ["menu_label"] = label,
            ["menu_character_id"] = characterId,
        };
        return false;
    }

    private bool TryExecuteMenuActionHighLevel(object gameInstance, string expectedKind, object node, out string handler)
    {
        return expectedKind switch
        {
            "continue_run" => TryExecuteContinueRunAction(gameInstance, node, out handler),
            "start_new_run" => TryExecuteStartNewRunAction(gameInstance, node, out handler),
            "select_character" => TryExecuteSelectCharacterAction(gameInstance, node, out handler),
            "confirm_start_run" => TryExecuteConfirmStartRunAction(gameInstance, node, out handler),
            _ => TryReturnUnhandled(out handler),
        };
    }

    private bool TryExecuteContinueRunAction(object gameInstance, object node, out string handler)
    {
        var mainMenu = GetMemberValue(node, "_mainMenu")
            ?? GetMemberValue(node, "MainMenu")
            ?? FindMainMenuOwner(gameInstance, node);
        if (mainMenu is not null)
        {
            if (TryInvokeFirstCompatibleMethod(mainMenu, new[] { "OnContinueButtonPressed" }, new[] { new object?[] { node } }, out var methodName))
            {
                handler = $"main_menu.{methodName}";
                return true;
            }

            if (TryInvokeFirstCompatibleMethod(mainMenu, new[] { "OnContinueButtonPressedAsync" }, new[] { Array.Empty<object?>() }, out methodName))
            {
                handler = $"main_menu.{methodName}";
                return true;
            }
        }

        handler = string.Empty;
        return false;
    }

    private bool TryExecuteStartNewRunAction(object gameInstance, object node, out string handler)
    {
        var submenu = FindSingleplayerSubmenu(gameInstance, node);
        if (submenu is not null &&
            TryInvokeFirstCompatibleMethod(submenu, new[] { "OpenCharacterSelect" }, new[] { new object?[] { node } }, out var submenuMethod))
        {
            handler = $"singleplayer_submenu.{submenuMethod}";
            return true;
        }

        var mainMenu = GetMemberValue(node, "_mainMenu")
            ?? GetMemberValue(node, "MainMenu")
            ?? FindMainMenuOwner(gameInstance, node);
        if (mainMenu is not null)
        {
            if (TryInvokeFirstCompatibleMethod(mainMenu, new[] { "OpenSingleplayerSubmenu" }, new[] { Array.Empty<object?>() }, out var methodName))
            {
                handler = $"main_menu.{methodName}";
                return true;
            }

            if (TryInvokeFirstCompatibleMethod(mainMenu, new[] { "SingleplayerButtonPressed" }, new[] { new object?[] { node } }, out methodName))
            {
                handler = $"main_menu.{methodName}";
                return true;
            }
        }

        handler = string.Empty;
        return false;
    }

    private bool TryExecuteSelectCharacterAction(object gameInstance, object node, out string handler)
    {
        var character = GetMemberValue(node, "Character") ?? GetMemberValue(node, "_character");
        var screen = FindCharacterSelectScreen(gameInstance, node);
        if (screen is not null && character is not null &&
            TryInvokeFirstCompatibleMethod(screen, new[] { "SelectCharacter" }, new[] { new object?[] { node, character } }, out var screenMethod))
        {
            handler = $"character_select_screen.{screenMethod}";
            return true;
        }

        var buttonDelegate = GetMemberValue(node, "_delegate") ?? GetMemberValue(node, "Delegate");
        if (buttonDelegate is not null && character is not null &&
            TryInvokeFirstCompatibleMethod(buttonDelegate, new[] { "SelectCharacter" }, new[] { new object?[] { node, character } }, out var delegateMethod))
        {
            handler = $"character_select_delegate.{delegateMethod}";
            return true;
        }

        if (TryInvokeFirstCompatibleMethod(node, new[] { "Select" }, new[] { Array.Empty<object?>() }, out var methodName))
        {
            handler = $"menu_node.{methodName}";
            return true;
        }

        handler = string.Empty;
        return false;
    }

    private bool TryExecuteConfirmStartRunAction(object gameInstance, object node, out string handler)
    {
        var screen = FindCharacterSelectScreen(gameInstance, node);
        if (screen is not null &&
            TryInvokeFirstCompatibleMethod(screen, new[] { "OnEmbarkPressed" }, new[] { new object?[] { node } }, out var methodName))
        {
            handler = $"character_select_screen.{methodName}";
            return true;
        }

        handler = string.Empty;
        return false;
    }

    private object? FindMainMenuOwner(object gameInstance, object node)
    {
        foreach (var root in EnumerateMenuRoots(gameInstance))
        {
            if (root.Node is null)
            {
                continue;
            }

            foreach (var candidate in EnumerateNodeDescendants(root.Node, maxDepth: 6).Prepend(root.Node))
            {
                var typeName = GetTypeName(candidate) ?? string.Empty;
                if (!typeName.Contains("NMainMenu", StringComparison.Ordinal))
                {
                    continue;
                }

                if (ReferenceEquals(GetMemberValue(candidate, "_continueButton"), node) ||
                    ContainsNodeReference(candidate, node, maxDepth: 4))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private object? FindSingleplayerSubmenu(object gameInstance, object node)
    {
        foreach (var root in EnumerateMenuRoots(gameInstance))
        {
            if (root.Node is null)
            {
                continue;
            }

            foreach (var candidate in EnumerateNodeDescendants(root.Node, maxDepth: 6).Prepend(root.Node))
            {
                var typeName = GetTypeName(candidate) ?? string.Empty;
                if (!typeName.Contains("NSingleplayerSubmenu", StringComparison.Ordinal))
                {
                    continue;
                }

                if (ReferenceEquals(GetMemberValue(candidate, "_standardButton"), node) ||
                    ContainsNodeReference(candidate, node, maxDepth: 4))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private object? FindCharacterSelectScreen(object gameInstance, object node)
    {
        var buttonDelegate = GetMemberValue(node, "_delegate") ?? GetMemberValue(node, "Delegate");
        if (buttonDelegate is not null)
        {
            var delegateTypeName = GetTypeName(buttonDelegate) ?? string.Empty;
            if (delegateTypeName.Contains("NCharacterSelectScreen", StringComparison.Ordinal))
            {
                return buttonDelegate;
            }
        }

        foreach (var root in EnumerateMenuRoots(gameInstance))
        {
            if (root.Node is null)
            {
                continue;
            }

            foreach (var candidate in EnumerateNodeDescendants(root.Node, maxDepth: 6).Prepend(root.Node))
            {
                var typeName = GetTypeName(candidate) ?? string.Empty;
                if (!typeName.Contains("NCharacterSelectScreen", StringComparison.Ordinal))
                {
                    continue;
                }

                if (ReferenceEquals(GetMemberValue(candidate, "_embarkButton"), node) ||
                    ReferenceEquals(GetMemberValue(candidate, "_selectedButton"), node) ||
                    ContainsNodeReference(candidate, node, maxDepth: 4))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private bool ContainsNodeReference(object root, object target, int maxDepth)
    {
        if (ReferenceEquals(root, target))
        {
            return true;
        }

        return EnumerateNodeDescendants(root, maxDepth)
            .Any(child => ReferenceEquals(child, target));
    }

    private static bool TryReturnUnhandled(out string handler)
    {
        handler = string.Empty;
        return false;
    }

    private static bool TryActivateMenuNode(object node, out string handler)
    {
        var argSets = new List<object?[]>
        {
            Array.Empty<object?>(),
            new object?[] { false },
            new object?[] { true },
            new object?[] { "pressed" },
        };

        if (TryInvokeFirstCompatibleMethod(node, MenuActivationMethodNames, argSets, out var methodName))
        {
            handler = $"menu_node.{methodName}";
            return true;
        }

        var emitSignalCandidates = node.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(method => string.Equals(method.Name, "EmitSignal", StringComparison.Ordinal))
            .ToArray();
        foreach (var emitSignal in emitSignalCandidates)
        {
            var parameters = emitSignal.GetParameters();
            if (parameters.Length == 0)
            {
                continue;
            }

            // Prefer EmitSignal(StringName signal, Variant[] args)
            if (parameters.Length == 2 && parameters[1].ParameterType.IsArray)
            try
            {
                var signalArg = CreateSignalArgument(parameters[0].ParameterType, "pressed");
                if (signalArg is null)
                {
                    continue;
                }

                var elementType = parameters[1].ParameterType.GetElementType();
                if (elementType is null)
                {
                    continue;
                }

                var argsArray = Array.CreateInstance(elementType, 0);
                emitSignal.Invoke(node, new[] { signalArg, argsArray });
                handler = $"menu_node.{emitSignal.Name}(pressed)";
                return true;
            }
            catch
            {
                // ignore
            }

            // Fallback: EmitSignal(string signal) style overloads.
            if (parameters.Length == 1)
            {
                try
                {
                    var signalArg = CreateSignalArgument(parameters[0].ParameterType, "pressed");
                    if (signalArg is null)
                    {
                        continue;
                    }

                    emitSignal.Invoke(node, new[] { signalArg });
                    handler = $"menu_node.{emitSignal.Name}(pressed)";
                    return true;
                }
                catch
                {
                    // ignore
                }
            }
        }

        handler = "menu_node.unhandled";
        return false;
    }

    private static object? CreateSignalArgument(Type parameterType, string signal)
    {
        if (parameterType == typeof(string))
        {
            return signal;
        }

        // Godot's C# bindings usually use Godot.StringName for signal identifiers.
        if (string.Equals(parameterType.Name, "StringName", StringComparison.Ordinal) ||
            string.Equals(parameterType.FullName, "Godot.StringName", StringComparison.Ordinal))
        {
            try
            {
                return Activator.CreateInstance(parameterType, signal);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private Dictionary<string, object?> CreateMenuMetadata(object gameInstance)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["source"] = "sts2_runtime",
            ["phase_detected"] = DecisionPhase.Menu,
            ["game_version"] = _probe.GameVersion ?? _options.GameVersion,
            ["managed_dir"] = _probe.ManagedDir,
            ["game_instance_type"] = GetTypeName(gameInstance),
        };

        var overlayStack = GetMemberValue(GetOverlayStackType(), "Instance");
        var overlayTop = TryInvokeParameterlessMethod(overlayStack, "Peek");
        if (overlayTop is not null)
        {
            metadata["overlay_top_type"] = GetTypeName(overlayTop);
        }

        return metadata;
    }

    private RuntimeWindowContext BuildCombatWindow(object runNode, object runState)
    {
        var textDiagnostics = new TextDiagnosticsCollector();
        var playerBuild = BuildPlayerState(runState, textDiagnostics);
        var player = playerBuild.Player;
        var enemyBuild = BuildEnemies(runState, textDiagnostics);
        var enemies = enemyBuild.Enemies;
        var metadata = CreateBaseMetadata(runNode, runState, DecisionPhase.Combat);
        foreach (var pair in playerBuild.Diagnostics)
        {
            metadata[pair.Key] = pair.Value;
        }
        metadata["enemy_export"] = new Dictionary<string, object?>
        {
            ["enemy_count"] = enemies.Count,
            ["degraded"] = enemyBuild.Diagnostics.Count > 0,
            ["entry_count"] = enemyBuild.Diagnostics.Count,
            ["entries"] = enemyBuild.Diagnostics.Take(12).ToArray(),
        };
        var runStateSnapshot = BuildRunState(runState, textDiagnostics);
        var rewardAnalysis = AnalyzeRewardPhase(runNode, runState);
        metadata["phase_detection"] = rewardAnalysis.ToMetadata();
        var actions = new List<RuntimeActionDefinition>();
        var liveEnemyIds = enemies.Where(enemy => enemy.IsAlive).Select(enemy => enemy.EnemyId).ToArray();
        if (liveEnemyIds.Length == 0)
        {
            metadata["window_kind"] = "combat_transition";
            metadata["reward_pending"] = true;
            metadata["transition_kind"] = "combat_reward_transition";
            LogTextDiagnostics("combat_transition", textDiagnostics);
            return new RuntimeWindowContext(
                DecisionPhase.Combat,
                player,
                enemies,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Terminal: false,
                Metadata: metadata,
                Actions: Array.Empty<RuntimeActionDefinition>(),
                RunState: runStateSnapshot);
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
            actionMetadata["card_preview"] = BuildCardPreview(card);
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
        LogTextDiagnostics("combat", textDiagnostics);
        return new RuntimeWindowContext(
            DecisionPhase.Combat,
            player,
            enemies,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Terminal: false,
            Metadata: metadata,
            Actions: actions,
            RunState: runStateSnapshot);
    }

    private RuntimeWindowContext BuildRewardWindow(object runNode, object runState)
    {
        var textDiagnostics = new TextDiagnosticsCollector();
        var rewardAnalysis = AnalyzeRewardPhase(runNode, runState);
        var rewards = ExtractRewards(runNode, textDiagnostics);
        var playerBuild = BuildPlayerState(runState, textDiagnostics);
        var player = playerBuild.Player;
        var metadata = CreateBaseMetadata(runNode, runState, DecisionPhase.Reward);
        foreach (var pair in playerBuild.Diagnostics)
        {
            metadata[pair.Key] = pair.Value;
        }
        var runStateSnapshot = BuildRunState(runState, textDiagnostics);
        metadata["phase_detection"] = rewardAnalysis.ToMetadata();
        metadata["reward_subphase"] = rewardAnalysis.RewardSubphase;
        metadata["detection_source"] = rewardAnalysis.DetectionSource;
        metadata["window_kind"] = rewardAnalysis.RewardSubphase switch
        {
            "card_reward_selection" => "reward_card_selection",
            "reward_advance" => "reward_advance",
            "reward_transition" => "reward_transition",
            _ => "reward_choice",
        };
        metadata["reward_count"] = rewards.Count;
        metadata["reward_screen_complete"] = rewardAnalysis.RewardScreenComplete;
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

        if (rewardAnalysis.AdvanceButtonDetected &&
            TryBuildRewardAdvanceAction(runNode, textDiagnostics, out var advanceAction, out var advanceMetadata))
        {
            actions.Add(advanceAction);
            foreach (var pair in advanceMetadata)
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        if (actions.Count == 0 && rewards.Count == 0 && !metadata.ContainsKey("reward_advance_available"))
        {
            metadata["reward_advance_available"] = false;
        }

        LogTextDiagnostics("reward", textDiagnostics);

        return new RuntimeWindowContext(
            DecisionPhase.Reward,
            player,
            Array.Empty<RuntimeEnemyState>(),
            rewards.Select(reward => reward.Label).ToArray(),
            Array.Empty<string>(),
            Terminal: false,
            Metadata: metadata,
            Actions: actions,
            RunState: runStateSnapshot);
    }

    private RuntimeWindowContext BuildMapWindow(object runNode, object runState)
    {
        var textDiagnostics = new TextDiagnosticsCollector();
        var mapNodes = ExtractMapNodes(runState, textDiagnostics, out var mapNodeSource);
        var playerBuild = BuildPlayerState(runState, textDiagnostics);
        var player = playerBuild.Player;
        var metadata = CreateBaseMetadata(runNode, runState, DecisionPhase.Map);
        foreach (var pair in playerBuild.Diagnostics)
        {
            metadata[pair.Key] = pair.Value;
        }
        var runStateSnapshot = BuildRunState(runState, textDiagnostics, mapNodes, mapNodeSource);
        metadata["node_count"] = mapNodes.Count;
        metadata["window_kind"] = mapNodes.Count > 0 ? "map_ready" : "map_transition";
        metadata["map_ready"] = mapNodes.Count > 0;
        metadata["map_node_source"] = mapNodeSource;
        metadata["no_reachable_nodes"] = mapNodes.Count == 0;
        LogTextDiagnostics("map", textDiagnostics);
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
            Actions: actions,
            RunState: runStateSnapshot);
    }

    private RuntimeWindowContext BuildTerminalWindow(object runNode, object runState)
    {
        var textDiagnostics = new TextDiagnosticsCollector();
        var playerBuild = BuildPlayerState(runState, textDiagnostics);
        var player = playerBuild.Player;
        var metadata = CreateBaseMetadata(runNode, runState, DecisionPhase.Terminal);
        foreach (var pair in playerBuild.Diagnostics)
        {
            metadata[pair.Key] = pair.Value;
        }
        var runStateSnapshot = BuildRunState(runState, textDiagnostics);
        metadata["result"] = GetBoolean(runState, "IsGameOver") ? "game_over" : "terminal";
        LogTextDiagnostics("terminal", textDiagnostics);
        return new RuntimeWindowContext(
            DecisionPhase.Terminal,
            player,
            Array.Empty<RuntimeEnemyState>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Terminal: true,
            Metadata: metadata,
            Actions: Array.Empty<RuntimeActionDefinition>(),
            RunState: runStateSnapshot);
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

    private RuntimeRunState BuildRunState(
        object runState,
        TextDiagnosticsCollector textDiagnostics,
        IReadOnlyList<string>? reachableNodes = null,
        string? mapNodeSource = null)
    {
        var currentActIndex = GetNullableInt(runState, "CurrentActIndex");
        var act = GetNullableInt(runState, "Act")
                  ?? GetNullableInt(runState, "CurrentAct")
                  ?? (currentActIndex is null ? null : currentActIndex.Value + 1);
        var currentRoom = GetMemberValue(runState, "CurrentRoom");
        var currentLocation = GetMemberValue(runState, "CurrentLocation");
        var currentMapPoint = GetMemberValue(runState, "CurrentMapPoint");
        var mapNodes = reachableNodes ?? ExtractMapNodes(runState, textDiagnostics, out mapNodeSource);
        mapNodeSource ??= "unavailable";
        return new RuntimeRunState(
            Act: act,
            Floor: GetNullableInt(runState, "ActFloor"),
            CurrentRoomType: NormalizeTypeName(currentRoom),
            CurrentLocationType: NormalizeTypeName(currentLocation),
            CurrentActIndex: currentActIndex,
            AscensionLevel: GetNullableInt(runState, "AscensionLevel"),
            Map: new RuntimeRunMapState(
                CurrentCoord: currentMapPoint is null ? null : DescribeMapCoord(GetMemberValue(currentMapPoint, "coord")),
                CurrentNodeType: currentMapPoint is null
                    ? null
                    : ConvertToText(GetMemberValue(currentMapPoint, "PointType"), "run_state.map.current_node_type", textDiagnostics),
                ReachableNodes: mapNodes,
                Source: mapNodeSource));
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

    private PlayerBuildResult BuildPlayerState(object runState, TextDiagnosticsCollector textDiagnostics)
    {
        var player = GetPlayers(runState).FirstOrDefault();
        if (player is null)
        {
            return new PlayerBuildResult(null, new Dictionary<string, object?>());
        }

        var creature = GetMemberValue(player, "Creature");
        var playerCombatState = GetMemberValue(player, "PlayerCombatState");
        var drawPile = GetMemberValue(playerCombatState, "DrawPile");
        var discardPile = GetMemberValue(playerCombatState, "DiscardPile");
        var exhaustPile = GetMemberValue(playerCombatState, "ExhaustPile");
        var handCards = ExtractCards(GetMemberValue(playerCombatState, "Hand"), "player.hand", textDiagnostics);
        var drawPileCount = CountCards(drawPile);
        var discardPileCount = CountCards(discardPile);
        var exhaustPileCount = CountCards(exhaustPile);
        var drawPileCards = ExtractCardsWithSource(drawPile, "player.draw_pile_cards", textDiagnostics, "draw_pile", drawPileCount);
        var discardPileCards = ExtractCardsWithSource(discardPile, "player.discard_pile_cards", textDiagnostics, "discard_pile", discardPileCount);
        var exhaustPileCards = ExtractCardsWithSource(exhaustPile, "player.exhaust_pile_cards", textDiagnostics, "exhaust_pile", exhaustPileCount);
        var relics = ExtractLabels(GetMemberValue(player, "Relics"), "player.relics", textDiagnostics);
        var potions = ExtractLabels(GetMemberValue(player, "PotionSlots"), "player.potions", textDiagnostics);
        var powers = ExtractPowers(creature ?? player, "player.powers", textDiagnostics);
        var diagnostics = new Dictionary<string, object?>
        {
            ["pile_export"] = new Dictionary<string, object?>
            {
                ["draw_pile"] = CreatePileDiagnostics(drawPileCards),
                ["discard_pile"] = CreatePileDiagnostics(discardPileCards),
                ["exhaust_pile"] = CreatePileDiagnostics(exhaustPileCards),
            }
        };

        return new PlayerBuildResult(new RuntimePlayerState(
            Hp: GetNullableInt(creature, "CurrentHp") ?? 0,
            MaxHp: GetNullableInt(creature, "MaxHp") ?? 0,
            Block: GetNullableInt(creature, "Block") ?? 0,
            Energy: GetNullableInt(playerCombatState, "Energy") ?? 0,
            Gold: GetNullableInt(player, "Gold") ?? 0,
            Hand: handCards,
            DrawPile: drawPileCount,
            DiscardPile: discardPileCount,
            ExhaustPile: exhaustPileCount,
            Relics: relics,
            Potions: potions,
            Powers: powers,
            DrawPileCards: drawPileCards.Cards,
            DiscardPileCards: discardPileCards.Cards,
            ExhaustPileCards: exhaustPileCards.Cards), diagnostics);
    }

    private IReadOnlyDictionary<string, object?> CreatePileDiagnostics(PileCardExtraction extraction)
    {
        var diagnostics = new Dictionary<string, object?>
        {
            ["source"] = extraction.Source,
            ["expected_count"] = extraction.ExpectedCount,
            ["exported_count"] = extraction.Cards.Count,
            ["degraded"] = extraction.IsDegraded,
        };

        if (!string.IsNullOrWhiteSpace(extraction.FallbackReason))
        {
            diagnostics["fallback_reason"] = extraction.FallbackReason;
        }

        return diagnostics;
    }

    private PileCardExtraction ExtractCardsWithSource(object? pile, string path, TextDiagnosticsCollector textDiagnostics, string pileName, int expectedCount)
    {
        if (pile is null)
        {
            return new PileCardExtraction(Array.Empty<RuntimeCard>(), $"{pileName}_missing", expectedCount, "pile_missing");
        }

        var cards = GetMemberValue(pile, "Cards");
        if (cards is null)
        {
            return new PileCardExtraction(Array.Empty<RuntimeCard>(), $"{pileName}_cards_missing", expectedCount, "cards_collection_missing");
        }

        try
        {
            return new PileCardExtraction(ExtractCards(pile, path, textDiagnostics), $"{pileName}_cards", expectedCount);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Pile extraction degraded for {pileName}: {ex.GetBaseException().Message}");
            return new PileCardExtraction(Array.Empty<RuntimeCard>(), $"{pileName}_extract_failed", expectedCount, "pile_extract_failed");
        }
    }

    private EnemyBuildResult BuildEnemies(object runState, TextDiagnosticsCollector textDiagnostics)
    {
        var combatState = GetCombatState(runState);
        if (combatState is null)
        {
            return new EnemyBuildResult(Array.Empty<RuntimeEnemyState>(), Array.Empty<IReadOnlyDictionary<string, object?>>());
        }

        var playerTargets = BuildCreatureTargetCollection(runState);
        var enemies = new List<RuntimeEnemyState>();
        var diagnostics = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var (enemy, index) in EnumerateObjects(GetMemberValue(combatState, "Enemies")).Select((enemy, index) => (enemy, index)))
        {
            try
            {
                enemies.Add(BuildEnemyState(enemy, index, playerTargets, textDiagnostics, diagnostics));
            }
            catch (Exception ex)
            {
                var enemyId = ResolveEnemyId(enemy, index);
                diagnostics.Add(CreateEnemyExportDiagnostic(
                    index,
                    enemyId,
                    "enemy",
                    "exception",
                    ex.GetBaseException().Message));
                _logger?.Warn($"Enemy extraction degraded for enemies[{index}] ({enemyId}): {ex.GetBaseException().Message}");
                enemies.Add(BuildFallbackEnemyState(enemy, index, playerTargets, textDiagnostics));
            }
        }

        return new EnemyBuildResult(enemies.ToArray(), diagnostics.ToArray());
    }

    private RuntimeEnemyState BuildEnemyState(
        object enemy,
        int index,
        object? playerTargets,
        TextDiagnosticsCollector textDiagnostics,
        List<IReadOnlyDictionary<string, object?>> diagnostics)
    {
        var path = $"enemies[{index}]";
        var enemyId = ResolveEnemyId(enemy, index);
        var monster = GetMemberValue(enemy, "Monster");
        var intent = ResolveEnemyIntent(enemy, playerTargets, $"{path}.intent", textDiagnostics);
        var powers = ExtractPowers(monster ?? enemy, $"{path}.powers", textDiagnostics);
        var enrichment = DescribeEnemyEnrichment(enemy, enemyId, index, playerTargets, intent, powers, textDiagnostics, diagnostics);
        return new RuntimeEnemyState(
            EnemyId: enemyId,
            Name: ConvertToText(GetMemberValue(enemy, "Name"), $"{path}.name", textDiagnostics) ?? $"enemy_{index}",
            Hp: GetNullableInt(enemy, "CurrentHp") ?? 0,
            MaxHp: GetNullableInt(enemy, "MaxHp") ?? 0,
            Block: GetNullableInt(enemy, "Block") ?? 0,
            Intent: intent.Display,
            IsAlive: GetBoolean(enemy, "IsAlive", defaultValue: true),
            InstanceEnemyId: enemyId,
            CanonicalEnemyId: ResolveEnemyCanonicalId(enemy),
            IntentRaw: intent.Raw,
            IntentType: intent.Type,
            IntentDamage: intent.Damage,
            IntentHits: intent.Hits,
            IntentBlock: intent.Block,
            IntentEffects: intent.Effects,
            Powers: powers,
            MoveName: enrichment.MoveName,
            MoveDescription: enrichment.MoveDescription,
            MoveGlossary: enrichment.MoveGlossary,
            Traits: enrichment.Traits,
            Keywords: enrichment.Keywords);
    }

    private RuntimeEnemyState BuildFallbackEnemyState(object enemy, int index, object? playerTargets, TextDiagnosticsCollector textDiagnostics)
    {
        var path = $"enemies[{index}]";
        var enemyId = ResolveEnemyId(enemy, index);
        var intent = ResolveEnemyIntent(enemy, playerTargets, $"{path}.intent", textDiagnostics);
        return new RuntimeEnemyState(
            EnemyId: enemyId,
            Name: ConvertToText(GetMemberValue(enemy, "Name"), $"{path}.name", textDiagnostics) ?? $"enemy_{index}",
            Hp: GetNullableInt(enemy, "CurrentHp") ?? 0,
            MaxHp: GetNullableInt(enemy, "MaxHp") ?? 0,
            Block: GetNullableInt(enemy, "Block") ?? 0,
            Intent: intent.Display,
            IsAlive: GetBoolean(enemy, "IsAlive", defaultValue: true),
            InstanceEnemyId: enemyId,
            CanonicalEnemyId: ResolveEnemyCanonicalId(enemy),
            IntentRaw: intent.Raw,
            IntentType: intent.Type,
            IntentDamage: intent.Damage,
            IntentHits: intent.Hits,
            IntentBlock: intent.Block,
            IntentEffects: intent.Effects,
            Powers: ExtractPowers(GetMemberValue(enemy, "Monster") ?? enemy, $"{path}.powers", textDiagnostics),
            MoveGlossary: Array.Empty<GlossaryAnchor>(),
            Traits: Array.Empty<string>(),
            Keywords: Array.Empty<string>());
    }

    private EnemyEnrichmentDescriptor DescribeEnemyEnrichment(
        object enemy,
        string enemyId,
        int index,
        object? playerTargets,
        EnemyIntentDescriptor intent,
        IReadOnlyList<RuntimePowerState> powers,
        TextDiagnosticsCollector textDiagnostics,
        List<IReadOnlyDictionary<string, object?>> diagnostics)
    {
        var path = $"enemies[{index}]";
        var monster = GetMemberValue(enemy, "Monster");
        var moveSource = ResolveEnemyMoveSource(enemy);

        T SafeRead<T>(string field, Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                diagnostics.Add(CreateEnemyExportDiagnostic(
                    index,
                    enemyId,
                    field,
                    "exception",
                    ex.GetBaseException().Message));
                _logger?.Warn($"Enemy enrich field degraded for {path}.{field}: {ex.GetBaseException().Message}");
                return fallback;
            }
        }

        var moveName = SafeRead(
            "move_name",
            () => ResolveEnemyMoveName(enemy, moveSource, playerTargets, intent, path, textDiagnostics),
            fallback: null as string);
        var traits = SafeRead(
            "traits",
            () => ExtractEnemyTraits(enemy, monster, moveSource, $"{path}.traits", textDiagnostics),
            fallback: Array.Empty<string>());
        var placeholderKeywords = Array.Empty<string>();
        var moveDescription = SafeRead(
            "move_description",
            () => ResolveEnemyMoveDescription(enemy, moveSource, playerTargets, path, textDiagnostics, moveName, traits, placeholderKeywords),
            fallback: new DescriptionExtraction(null, null, null, null, null, Array.Empty<DescriptionVariable>(), Array.Empty<GlossaryAnchor>()));
        var keywords = SafeRead(
            "keywords",
            () => ExtractEnemyKeywords(intent, moveDescription.Glossary, traits, powers, enemy, monster, moveSource, $"{path}.keywords", textDiagnostics),
            fallback: Array.Empty<string>());

        if (string.IsNullOrWhiteSpace(moveName) && !string.Equals(intent.Display, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateEnemyExportDiagnostic(index, enemyId, "move_name", "not_found", "no readable move name found", "unresolved"));
        }

        if (string.IsNullOrWhiteSpace(moveDescription.Description) && !string.Equals(intent.Display, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateEnemyExportDiagnostic(index, enemyId, "move_description", "not_found", "no readable move description found", "unresolved"));
        }

        return new EnemyEnrichmentDescriptor(
            moveName,
            moveDescription.Description,
            moveDescription.Glossary,
            traits,
            keywords);
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
                var traits = ExtractTextList(
                    GetMemberValue(card, "Traits") ?? GetMemberValue(card, "Tags"),
                    $"player.hand[{index}].traits",
                    textDiagnostics);
                var keywords = ExtractTextList(
                    GetMemberValue(card, "Keywords") ?? GetMemberValue(card, "KeywordIds"),
                    $"player.hand[{index}].keywords",
                    textDiagnostics);
                var description = ResolveCardDescription(card, $"player.hand[{index}].description", textDiagnostics, traits, keywords);
                return new HandCardDescriptor(
                    RuntimeCardIdentity.CreateCardId(card, index),
                    nameResolution.Text ?? $"card_{index}",
                    nameResolution,
                    ConvertToText(GetMemberValue(card, "TargetType")),
                    GetBoolean(card, "IsPlayable", defaultValue: true),
                    ResolveCardCanonicalId(card),
                    description.Description,
                    description.Glossary,
                    ResolveCardUpgraded(card),
                    traits,
                    keywords);
            })
            .ToArray();
    }

    private IReadOnlyList<RuntimeCard> ExtractCards(object? pile, string path, TextDiagnosticsCollector textDiagnostics)
    {
        var results = new List<RuntimeCard>();
        foreach (var (card, index) in EnumerateObjects(GetMemberValue(pile, "Cards")).Select((card, index) => (card, index)))
        {
            try
            {
                results.Add(BuildRuntimeCard(card, index, path, textDiagnostics));
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Card extraction degraded for {path}[{index}]: {ex.GetBaseException().Message}");
                results.Add(BuildFallbackRuntimeCard(card, index));
            }
        }

        return results;
    }

    private RuntimeCard BuildRuntimeCard(object card, int index, string path, TextDiagnosticsCollector textDiagnostics)
    {
        var traits = ExtractTextList(
            GetMemberValue(card, "Traits") ?? GetMemberValue(card, "Tags"),
            $"{path}[{index}].traits",
            textDiagnostics);
        var keywords = ExtractTextList(
            GetMemberValue(card, "Keywords") ?? GetMemberValue(card, "KeywordIds"),
            $"{path}[{index}].keywords",
            textDiagnostics);
        var description = ResolveCardDescription(card, $"{path}[{index}].description", textDiagnostics, traits, keywords);
        var instanceCardId = RuntimeCardIdentity.CreateCardId(card, index);
        return new RuntimeCard(
            CardId: instanceCardId,
            Name: ConvertToText(GetMemberValue(card, "Title") ?? GetMemberValue(card, "Name") ?? card, $"{path}[{index}].name", textDiagnostics, "Title", "Name")
                  ?? $"card_{index}",
            Cost: ResolveCardCost(card),
            Playable: GetBoolean(card, "IsPlayable", defaultValue: true),
            InstanceCardId: instanceCardId,
            CanonicalCardId: ResolveCardCanonicalId(card),
            Description: description.Description,
            CostForTurn: ResolveCardCostForTurn(card),
            Upgraded: ResolveCardUpgraded(card),
            TargetType: ConvertToText(GetMemberValue(card, "TargetType"), $"{path}[{index}].target_type", textDiagnostics),
            CardType: ConvertToText(GetMemberValue(card, "CardType") ?? GetMemberValue(card, "Type"), $"{path}[{index}].card_type", textDiagnostics),
            Rarity: ConvertToText(GetMemberValue(card, "Rarity") ?? GetMemberValue(card, "CardRarity"), $"{path}[{index}].rarity", textDiagnostics),
            Traits: traits,
            Keywords: keywords,
            Glossary: description.Glossary);
    }

    private RuntimeCard BuildFallbackRuntimeCard(object? card, int index)
    {
        var instanceCardId = RuntimeCardIdentity.CreateCardId(card, index);
        return new RuntimeCard(
            CardId: instanceCardId,
            Name: ConvertToText(GetMemberValue(card, "Title") ?? GetMemberValue(card, "Name") ?? card) ?? $"card_{index}",
            Cost: ResolveCardCost(card),
            Playable: GetBoolean(card, "IsPlayable", defaultValue: true),
            InstanceCardId: instanceCardId,
            CanonicalCardId: ResolveCardCanonicalId(card),
            Description: null,
            CostForTurn: ResolveCardCostForTurn(card),
            Upgraded: ResolveCardUpgraded(card),
            TargetType: ConvertToText(GetMemberValue(card, "TargetType")),
            CardType: ConvertToText(GetMemberValue(card, "CardType") ?? GetMemberValue(card, "Type")),
            Rarity: ConvertToText(GetMemberValue(card, "Rarity") ?? GetMemberValue(card, "CardRarity")),
            Traits: Array.Empty<string>(),
            Keywords: Array.Empty<string>(),
            Glossary: Array.Empty<GlossaryAnchor>());
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
        var choices = FilterCardRewardSelectionChoices(ExtractCardRewardChoiceItems(cardRewardScreen));
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

    private List<object> FilterCardRewardSelectionChoices(List<object> choices)
    {
        if (choices.Count == 0)
        {
            return choices;
        }

        // If we already discovered holder-like nodes, drop entries that do not actually carry a card model/node.
        if (choices.Any(choice => GetMemberValue(choice, "CardModel") is not null || GetMemberValue(choice, "CardNode") is not null))
        {
            return choices
                .Where(choice => GetMemberValue(choice, "CardModel") is not null || GetMemberValue(choice, "CardNode") is not null)
                .ToList();
        }

        return choices;
    }

    private List<string> ExtractMapNodes(object runState, TextDiagnosticsCollector textDiagnostics, out string source)
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
            source = "current_map_point";
            return nodes;
        }

        var map = GetMemberValue(runState, "Map");
        var startingPoint = GetMemberValue(map, "StartingMapPoint");
        nodes = EnumerateObjects(GetMemberValue(startingPoint, "Children"))
            .Select((node, index) => DescribeMapNode(node, $"map_nodes[{index}]", textDiagnostics))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        source = nodes.Count > 0 ? "starting_map_point_fallback" : "no_reachable_nodes";
        return nodes;
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
            root = new RuntimeRoot(gameInstance, RunNode: null, RunState: null);
            status = "game runtime attached; no active run detected (exporting menu phase).";
            return true;
        }

        var runState = GetMemberValue(runNode, "_state");
        if (runState is null)
        {
            root = new RuntimeRoot(gameInstance, runNode, RunState: null);
            status = "run node found, but RunState is not available yet (exporting menu phase).";
            return true;
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
        var hasLiveEnemies = BuildEnemies(runState, new TextDiagnosticsCollector()).Enemies.Any(enemy => enemy.IsAlive);
        var cardRewardSelectionDetected = GetCardRewardSelectionScreen(runNode, rewardScreen) is not null;
        var rewardAdvanceDetected = hasRewardScreen &&
                                    rewardButtons.Length == 0 &&
                                    rewardScreenVisible &&
                                    TryFindRewardAdvanceButton(runNode, rewardScreen, textDiagnostics: null, out _, out _);
        var treatAsReward = hasRewardScreen &&
                            (!rewardScreenComplete ||
                             rewardButtons.Length > 0 ||
                             rewardScreenVisible ||
                             !hasLiveEnemies);
        if (cardRewardSelectionDetected)
        {
            treatAsReward = true;
        }

        var rewardSubphase = "none";
        if (cardRewardSelectionDetected)
        {
            rewardSubphase = "card_reward_selection";
        }
        else if (rewardAdvanceDetected)
        {
            rewardSubphase = "reward_advance";
        }
        else if (hasRewardScreen && rewardButtons.Length == 0 && rewardScreenComplete && rewardScreenVisible)
        {
            rewardSubphase = "reward_transition";
        }
        else if (hasRewardScreen)
        {
            rewardSubphase = "reward_choice";
        }
        var detectionSource = cardRewardSelectionDetected
            ? "overlay_stack.card_reward_selection"
            : rewardAdvanceDetected ? "reward_screen.advance_button"
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
            AdvanceButtonDetected: rewardAdvanceDetected,
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

    private bool TryBuildRewardAdvanceAction(
        object runNode,
        TextDiagnosticsCollector textDiagnostics,
        out RuntimeActionDefinition action,
        out IReadOnlyDictionary<string, object?> metadata)
    {
        if (!TryFindRewardAdvanceButton(runNode, GetRewardScreen(runNode), textDiagnostics, out var buttonNode, out var buttonLabel))
        {
            action = default!;
            metadata = new Dictionary<string, object?>
            {
                ["reward_advance_available"] = false,
            };
            return false;
        }

        action = new RuntimeActionDefinition(
            "advance_reward",
            buttonLabel,
            new Dictionary<string, object?>
            {
                ["button_label"] = buttonLabel,
            });
        metadata = new Dictionary<string, object?>
        {
            ["reward_advance_available"] = true,
            ["reward_advance_label"] = buttonLabel,
            ["reward_advance_target_type"] = GetTypeName(buttonNode),
        };
        return true;
    }

    private bool TryFindRewardAdvanceButton(
        object runNode,
        object? rewardScreen,
        TextDiagnosticsCollector? textDiagnostics,
        out object buttonNode,
        out string buttonLabel)
    {
        if (rewardScreen is null)
        {
            buttonNode = default!;
            buttonLabel = string.Empty;
            return false;
        }

        foreach (var candidate in EnumerateNodeDescendants(rewardScreen, maxDepth: 7).Prepend(rewardScreen))
        {
            if (!IsPotentialMenuButton(candidate))
            {
                continue;
            }

            if (!IsMenuNodeInteractable(candidate))
            {
                continue;
            }

            var label = GetMenuNodeLabel(candidate, textDiagnostics);
            if (string.IsNullOrWhiteSpace(label) || !IsRewardAdvanceLabel(label))
            {
                continue;
            }

            buttonNode = candidate;
            buttonLabel = label.Trim();
            return true;
        }

        buttonNode = default!;
        buttonLabel = string.Empty;
        return false;
    }

    private static bool IsRewardAdvanceLabel(string label)
    {
        var normalized = NormalizeLabel(label);
        return RewardAdvanceLabelHints.Any(hint => normalized.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private List<object> ExtractCardRewardChoiceItems(object cardRewardScreen)
    {
        var holders = ExtractCardRewardChoiceHoldersFromRow(cardRewardScreen);
        if (holders.Count > 0)
        {
            return holders;
        }

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
            holders = ExtractCardRewardChoiceHoldersFromContainer(container!);
            if (holders.Count > 0)
            {
                return holders;
            }

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

    private List<object> ExtractCardRewardChoiceHoldersFromRow(object cardRewardScreen)
    {
        var cardRow = GetMemberValue(cardRewardScreen, "_cardRow") ?? GetMemberValue(cardRewardScreen, "CardRow");
        if (cardRow is not null)
        {
            var holders = ExtractCardRewardChoiceHoldersFromContainer(cardRow);
            if (holders.Count > 0)
            {
                return holders;
            }
        }

        return ExtractCardRewardChoiceHoldersFromContainer(cardRewardScreen);
    }

    private List<object> ExtractCardRewardChoiceHoldersFromContainer(object container)
    {
        var cardHolderType = FindSts2Assembly()?.GetType("MegaCrit.Sts2.Core.Nodes.Cards.Holders.NCardHolder");
        return EnumerateNodeDescendants(container, maxDepth: 6)
            .Where(child =>
            {
                var typeName = GetTypeName(child) ?? string.Empty;
                if (cardHolderType is not null && cardHolderType.IsInstanceOfType(child))
                {
                    // Filter out non-card holders (e.g. hitboxes) that still derive from NCardHolder.
                    return GetMemberValue(child, "CardModel") is not null || GetMemberValue(child, "CardNode") is not null;
                }

                if (typeName.Contains("NCardHolder", StringComparison.Ordinal) ||
                    string.Equals(child.GetType().Name, "NCardHolder", StringComparison.Ordinal))
                {
                    return true;
                }

                return false;
            })
            .ToList();
    }

    private IEnumerable<object> EnumerateNodeDescendants(object root, int maxDepth)
    {
        if (maxDepth <= 0)
        {
            yield break;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object Node, int Depth)>();
        queue.Enqueue((root, 0));
        visited.Add(root);

        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (var child in EnumerateNodeChildren(node))
            {
                if (child is null)
                {
                    continue;
                }

                yield return child;
                if (!child.GetType().IsValueType && visited.Add(child))
                {
                    queue.Enqueue((child, depth + 1));
                }
            }
        }
    }

    private IEnumerable<object> EnumerateNodeChildren(object node)
    {
        var type = node.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // Prefer GetChildren overloads when available.
        var method = type.GetMethod("GetChildren", flags, null, Type.EmptyTypes, null);
        if (method is not null)
        {
            object? value = null;
            try
            {
                value = method.Invoke(node, null);
            }
            catch
            {
                value = null;
            }

            foreach (var child in EnumerateObjects(value))
            {
                yield return child;
            }
            yield break;
        }

        method = type.GetMethod("GetChildren", flags, null, new[] { typeof(bool) }, null);
        if (method is not null)
        {
            object? value = null;
            try
            {
                // STS2 uses Godot 4 which may place important UI nodes under internal children.
                value = method.Invoke(node, new object?[] { true });
            }
            catch
            {
                value = null;
            }

            foreach (var child in EnumerateObjects(value))
            {
                yield return child;
            }
            yield break;
        }

        // Fallback: enumerate GetChildCount/GetChild.
        var getChildCount = type.GetMethod("GetChildCount", flags, null, Type.EmptyTypes, null)
                           ?? type.GetMethod("GetChildCount", flags, null, new[] { typeof(bool) }, null);
        var getChild = type.GetMethod("GetChild", flags, null, new[] { typeof(int), typeof(bool) }, null)
                     ?? type.GetMethod("GetChild", flags, null, new[] { typeof(int) }, null);
        if (getChildCount is null || getChild is null)
        {
            yield break;
        }

        int count;
        try
        {
            count = getChildCount.GetParameters().Length == 0
                ? Convert.ToInt32(getChildCount.Invoke(node, null))
                : Convert.ToInt32(getChildCount.Invoke(node, new object?[] { true }));
        }
        catch
        {
            yield break;
        }

        for (var index = 0; index < count; index++)
        {
            object? child = null;
            try
            {
                child = getChild.GetParameters().Length == 1
                    ? getChild.Invoke(node, new object?[] { index })
                    : getChild.Invoke(node, new object?[] { index, true });
            }
            catch
            {
                child = null;
            }

            if (child is not null)
            {
                yield return child;
            }
        }
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

    private static int? ResolveCardCostForTurn(object? card)
    {
        if (GetBoolean(card, "HasEnergyCostX"))
        {
            return -1;
        }

        return GetNullableInt(card, "CurrentStarCost")
               ?? GetNullableInt(card, "CurrentEnergyCost")
               ?? GetNullableInt(card, "StarCostForTurn")
               ?? GetNullableInt(card, "EnergyCost");
    }

    private static bool? ResolveCardUpgraded(object? card)
    {
        if (GetMemberValue(card, "IsUpgraded") is bool isUpgraded)
        {
            return isUpgraded;
        }

        if (GetMemberValue(card, "Upgraded") is bool upgraded)
        {
            return upgraded;
        }

        var upgradeCount = GetNullableInt(card, "UpgradeCount");
        return upgradeCount is null ? null : upgradeCount.Value > 0;
    }

    private static string? ResolveCardCanonicalId(object? card)
    {
        return ConvertToText(
                   GetMemberValue(card, "CardId")
                   ?? GetMemberValue(card, "CardKey")
                   ?? GetMemberValue(card, "Key")
                   ?? GetMemberValue(card, "Id")
                   ?? GetMemberValue(card, "DataId")
                   ?? GetMemberValue(card, "TemplateId")
                   ?? GetMemberValue(card, "CanonicalId")
                   ?? GetMemberValue(GetMemberValue(card, "Data"), "Id")
                   ?? GetMemberValue(GetMemberValue(card, "Definition"), "Id"))
               ?? ConvertToText(GetMemberValue(card, "InternalName"));
    }

    private DescriptionExtraction ResolveCardDescription(
        object? card,
        string path,
        TextDiagnosticsCollector textDiagnostics,
        IReadOnlyList<string>? traits = null,
        IReadOnlyList<string>? keywords = null)
    {
        var source = ResolveCardDescriptionSource(card);
        var descriptionValue =
            GetMemberValue(card, "Description")
            ?? GetMemberValue(source, "Description")
            ?? GetMemberValue(card, "RulesText")
            ?? GetMemberValue(source, "RulesText")
            ?? GetMemberValue(card, "CardText")
            ?? GetMemberValue(source, "CardText")
            ?? GetMemberValue(card, "BodyText")
            ?? GetMemberValue(source, "BodyText")
            ?? GetMemberValue(card, "Text");
        var boundDescriptionValue = TryBindLocStringWithDynamicVars(descriptionValue, source ?? card);
        var raw = ConvertDescriptionTemplateToText(
            descriptionValue,
            path,
            textDiagnostics,
            "Description",
            "RulesText",
            "CardText",
            "BodyText",
            "Text");
        var rendered = ConvertToText(
            GetMemberValue(card, "RenderedDescription")
            ?? GetMemberValue(source, "RenderedDescription")
            ?? GetMemberValue(card, "RenderedText")
            ?? GetMemberValue(source, "RenderedText")
            ?? GetMemberValue(card, "DisplayDescription")
            ?? GetMemberValue(source, "DisplayDescription")
            ?? GetMemberValue(card, "DescriptionRendered")
            ?? GetMemberValue(source, "DescriptionRendered")
            ?? GetMemberValue(card, "ResolvedDescription")
            ?? GetMemberValue(source, "ResolvedDescription")
            ?? GetMemberValue(card, "CurrentDescription")
            ?? GetMemberValue(source, "CurrentDescription")
            ?? GetMemberValue(card, "DescriptionText")
            ?? GetMemberValue(source, "DescriptionText")
            ?? GetMemberValue(GetMemberValue(card, "Description"), "RenderedText")
            ?? GetMemberValue(GetMemberValue(card, "Description"), "Text")
            ?? boundDescriptionValue,
            $"{path}.rendered",
            textDiagnostics,
            "RenderedDescription",
            "RenderedText",
            "DisplayDescription",
            "DescriptionRendered",
            "ResolvedDescription",
            "CurrentDescription");
        var seedVariables = ResolveCardDescriptionSeedVariables(source ?? card, raw, keywords)
            .Concat(ExtractDescriptionVariablesFromLocString(descriptionValue, "loc_string"))
            .Concat(ExtractDescriptionVariablesFromLocString(boundDescriptionValue, "bound_loc_string"))
            .ToArray();
        var vars = ExtractDescriptionVariables(source ?? card, raw, seedVariables);
        var renderOutcome = RenderDescription(raw, rendered, vars);
        var glossary = ExtractGlossaryAnchors(
            canonicalId: null,
            displayName: ConvertToText(GetMemberValue(card, "Title") ?? GetMemberValue(card, "Name") ?? card),
            texts: new[] { renderOutcome.Text, raw },
            keywords: keywords,
            traits: traits);
        var canonicalDescription = ChooseCanonicalDescription(renderOutcome.Text, raw);
        LogDescriptionDiagnostics(
            kind: "card",
            identifier: ResolveCardCanonicalId(card) ?? ConvertToText(GetMemberValue(card, "Title") ?? GetMemberValue(card, "Name") ?? card) ?? path,
            path: path,
            raw: raw,
            rendered: renderOutcome.Text,
            quality: renderOutcome.Quality,
            source: renderOutcome.Source,
            variables: vars,
            glossary: glossary);
        return new DescriptionExtraction(raw, renderOutcome.Text, canonicalDescription, renderOutcome.Quality, renderOutcome.Source, vars, glossary);
    }

    private static object? ResolveCardDescriptionSource(object? card)
    {
        return GetMemberValue(card, "CardModel")
            ?? GetMemberValue(card, "Model")
            ?? GetMemberValue(card, "Card")
            ?? card;
    }

    private static IReadOnlyList<DescriptionVariable> ExtractDescriptionVariablesFromLocString(object? descriptionValue, string sourceLabel)
    {
        if (descriptionValue is null)
        {
            return Array.Empty<DescriptionVariable>();
        }

        var variables = GetMemberValue(descriptionValue, "Variables");
        if (variables is not IDictionary dictionary || dictionary.Count == 0)
        {
            return Array.Empty<DescriptionVariable>();
        }

        var results = new List<DescriptionVariable>();
        foreach (DictionaryEntry entry in dictionary)
        {
            var rawKey = ConvertToText(entry.Key);
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                continue;
            }

            var key = NormalizeDescriptionVariableKey(rawKey);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var resolution = ResolveLocStringVariableValue(entry.Value);
            results.Add(new DescriptionVariable(
                key,
                resolution.Value,
                resolution.Source ?? sourceLabel,
                rawKey));
        }

        return DeduplicateVariables(results);
    }

    private static object? TryBindLocStringWithDynamicVars(object? descriptionValue, object? source)
    {
        if (descriptionValue is null || source is null)
        {
            return null;
        }

        var typeName = GetTypeName(descriptionValue) ?? string.Empty;
        if (!typeName.Contains("LocString", StringComparison.Ordinal))
        {
            return null;
        }

        var dynamicVars = GetMemberValue(source, "DynamicVars");
        if (dynamicVars is null)
        {
            return null;
        }

        var bound = CloneLocString(descriptionValue) ?? descriptionValue;
        TryInvokeSingleParameterMethod(bound, "AddVariablesFrom", descriptionValue);
        if (!TryInvokeSingleParameterMethod(dynamicVars, "AddTo", bound))
        {
            return null;
        }

        return bound;
    }

    private static object? CloneLocString(object descriptionValue)
    {
        var type = descriptionValue.GetType();
        var locTable = ConvertToText(GetMemberValue(descriptionValue, "LocTable"));
        var locEntryKey = ConvertToText(GetMemberValue(descriptionValue, "LocEntryKey"));
        if (string.IsNullOrWhiteSpace(locTable) || string.IsNullOrWhiteSpace(locEntryKey))
        {
            return null;
        }

        var ctor = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string), typeof(string) }, null);
        if (ctor is null)
        {
            return null;
        }

        try
        {
            return ctor.Invoke(new object?[] { locTable, locEntryKey });
        }
        catch
        {
            return null;
        }
    }

    private static bool TryInvokeSingleParameterMethod(object target, string methodName, object argument)
    {
        var methods = target.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .ToArray();
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || !parameters[0].ParameterType.IsInstanceOfType(argument))
            {
                continue;
            }

            try
            {
                method.Invoke(target, new[] { argument });
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static VariableResolution ResolveLocStringVariableValue(object? value)
    {
        if (value is null)
        {
            return new VariableResolution(null, null);
        }

        try
        {
            return new VariableResolution(Convert.ToInt32(value), "loc_string.value");
        }
        catch
        {
            // fall through
        }

        foreach (var memberName in new[] { "IntValue", "PreviewValue", "BaseValue", "EnchantedValue", "Value" })
        {
            var numeric = GetNullableInt(value, memberName);
            if (numeric is not null)
            {
                return new VariableResolution(numeric, $"loc_string.{memberName}");
            }
        }

        return new VariableResolution(null, null);
    }

    private static string ResolveEnemyId(object enemy, int index)
    {
        return ConvertToText(GetMemberValue(enemy, "CombatId"))
               ?? ConvertToText(GetMemberValue(enemy, "SlotName"))
               ?? ConvertToText(GetMemberValue(enemy, "Name"))
               ?? $"enemy_{index}";
    }

    private static string? ResolveEnemyCanonicalId(object? enemy)
    {
        return ConvertToText(
            GetMemberValue(enemy, "EnemyId")
            ?? GetMemberValue(enemy, "MonsterId")
            ?? GetMemberValue(enemy, "Id")
            ?? GetMemberValue(enemy, "Key")
            ?? GetMemberValue(enemy, "TemplateId")
            ?? GetMemberValue(GetMemberValue(enemy, "Monster"), "EnemyId")
            ?? GetMemberValue(GetMemberValue(enemy, "Monster"), "Id"));
    }

    private static string? ResolvePowerCanonicalId(object? power)
    {
        return ConvertToText(
            GetMemberValue(power, "PowerId")
            ?? GetMemberValue(power, "Id")
            ?? GetMemberValue(power, "Key")
            ?? GetMemberValue(power, "TemplateId")
            ?? GetMemberValue(power, "CanonicalId"));
    }

    private static EnemyIntentDescriptor ResolveEnemyIntent(object enemy, object? playerTargets, string path, TextDiagnosticsCollector textDiagnostics)
    {
        var raw = ConvertToText(
            GetMemberValue(enemy, "Intent")
            ?? GetMemberValue(GetMemberValue(enemy, "Monster"), "Intent"),
            path,
            textDiagnostics,
            "Intent");
        var intentObject = ResolvePrimaryEnemyIntentObject(enemy);
        var owner = ResolveEnemyOwnerCreature(enemy);
        var hoverTip = intentObject is not null && owner is not null
            ? TryInvokeCompatibleMethod(intentObject, "GetHoverTip", playerTargets, owner)
            : null;
        raw ??= ConvertToText(
            intentObject is not null && owner is not null
                ? TryInvokeCompatibleMethod(intentObject, "GetIntentLabel", playerTargets, owner)
                : null,
            $"{path}.label",
            textDiagnostics,
            "Title",
            "Text",
            "Label");
        var hoverTipTitle = ConvertToText(GetMemberValue(hoverTip, "Title"), $"{path}.hover_tip_title", textDiagnostics, "Title");
        if (IsLowQualityIntentLabel(raw) && !string.IsNullOrWhiteSpace(hoverTipTitle))
        {
            raw = hoverTipTitle;
        }
        else
        {
            raw ??= hoverTipTitle;
        }

        var intentTypeText = ConvertToText(GetMemberValue(intentObject, "IntentType"));
        var type = NormalizeIntentType(raw ?? intentTypeText, enemy);
        var hoverDescription = ConvertToText(GetMemberValue(hoverTip, "Description"), $"{path}.hover_tip_description", textDiagnostics, "Description", "Text");
        var damage = GetNullableInt(enemy, "IntentDamage")
                     ?? GetNullableInt(enemy, "DisplayedIntentDamage")
                     ?? GetNullableInt(enemy, "AttackDamage")
                     ?? GetNullableInt(GetMemberValue(enemy, "Monster"), "IntentDamage")
                     ?? GetNullableInt(intentObject, "Damage")
                     ?? ConvertToInt32(TryInvokeCompatibleMethod(intentObject, "GetTotalDamage", playerTargets, owner))
                     ?? ConvertToInt32(TryInvokeCompatibleMethod(intentObject, "GetSingleDamage", playerTargets, owner));
        var hits = GetNullableInt(enemy, "IntentHits")
                   ?? GetNullableInt(enemy, "AttackCount")
                   ?? GetNullableInt(enemy, "HitCount")
                   ?? GetNullableInt(GetMemberValue(enemy, "Monster"), "IntentHits")
                   ?? GetNullableInt(intentObject, "Repeats");
        var block = GetNullableInt(enemy, "IntentBlock")
                    ?? GetNullableInt(enemy, "BlockAmount")
                    ?? GetNullableInt(GetMemberValue(enemy, "Monster"), "IntentBlock");
        var display = raw ?? type ?? "unknown";
        if ((string.IsNullOrWhiteSpace(raw) || string.Equals(display, "unknown", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(type) &&
            damage is not null)
        {
            display = hits is > 1
                ? $"{type}_{damage}x{hits}"
                : $"{type}_{damage}";
        }

        return new EnemyIntentDescriptor(
            Display: display,
            Raw: raw,
            Type: type,
            Damage: damage,
            Hits: hits,
            Block: block,
            Effects: ExtractIntentEffects(enemy, raw ?? hoverDescription));
    }

    private string? ResolveEnemyMoveName(
        object enemy,
        object? moveSource,
        object? playerTargets,
        EnemyIntentDescriptor intent,
        string path,
        TextDiagnosticsCollector textDiagnostics)
    {
        var intentObject = ResolvePrimaryEnemyIntentObject(enemy);
        var owner = ResolveEnemyOwnerCreature(enemy);
        var hoverTip = intentObject is not null && owner is not null
            ? TryInvokeCompatibleMethod(intentObject, "GetHoverTip", playerTargets, owner)
            : null;
        var moveName = ConvertToText(
            GetFirstMemberValue(
                moveSource,
                "Name",
                "Title",
                "DisplayName",
                "Label",
                "IntentName",
                "MoveName"),
            $"{path}.move_name",
            textDiagnostics,
            "Name",
            "Title",
            "DisplayName",
            "Label");
        if (!string.IsNullOrWhiteSpace(moveName))
        {
            return moveName;
        }

        moveName = ConvertToText(
            intentObject is not null && owner is not null
                ? TryInvokeCompatibleMethod(intentObject, "GetIntentLabel", playerTargets, owner)
                : null,
            $"{path}.move_name_intent_label",
            textDiagnostics,
            "Title",
            "Text",
            "Label");
        if (!string.IsNullOrWhiteSpace(moveName))
        {
            var hoverTitle = ConvertToText(GetMemberValue(hoverTip, "Title"), $"{path}.move_name_hover_tip", textDiagnostics, "Title");
            if (IsLowQualityIntentLabel(moveName) && !string.IsNullOrWhiteSpace(hoverTitle))
            {
                return hoverTitle;
            }

            return moveName;
        }

        moveName = ConvertToText(GetMemberValue(hoverTip, "Title"), $"{path}.move_name_hover_tip", textDiagnostics, "Title");
        if (!string.IsNullOrWhiteSpace(moveName))
        {
            return moveName;
        }

        moveName = ConvertToText(
            GetFirstMemberValue(
                enemy,
                "IntentName",
                "MoveName",
                "ActionName",
                "CurrentMoveName",
                "NextMoveName"),
            $"{path}.move_name_fallback",
            textDiagnostics,
            "IntentName",
            "MoveName",
            "ActionName");
        if (!string.IsNullOrWhiteSpace(moveName))
        {
            return moveName;
        }

        if (!string.IsNullOrWhiteSpace(intent.Raw) &&
            !string.Equals(intent.Raw, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return intent.Raw;
        }

        return !string.IsNullOrWhiteSpace(intent.Display) &&
               !string.Equals(intent.Display, "unknown", StringComparison.OrdinalIgnoreCase)
            ? intent.Display
            : null;
    }

    private DescriptionExtraction ResolveEnemyMoveDescription(
        object enemy,
        object? moveSource,
        object? playerTargets,
        string path,
        TextDiagnosticsCollector textDiagnostics,
        string? moveName,
        IReadOnlyList<string> traits,
        IReadOnlyList<string> keywords)
    {
        var monster = GetMemberValue(enemy, "Monster");
        var source = moveSource ?? enemy;
        var intentObject = ResolvePrimaryEnemyIntentObject(enemy);
        var owner = ResolveEnemyOwnerCreature(enemy);
        var hoverTip = intentObject is not null && owner is not null
            ? TryInvokeCompatibleMethod(intentObject, "GetHoverTip", playerTargets, owner)
            : null;
        var rawDescriptionValue =
            GetFirstMemberValue(
                moveSource,
                "Description",
                "RulesText",
                "Text",
                "TooltipText",
                "IntentDescription",
                "IntentText",
                "EffectText",
                "DescriptionTemplate",
                "LocString")
            ?? GetFirstMemberValue(
                enemy,
                "IntentDescription",
                "IntentText",
                "IntentTooltip",
                "IntentTooltipText",
                "MoveDescription",
                "CurrentMoveDescription")
            ?? GetFirstMemberValue(
                monster,
                "IntentDescription",
                "IntentText",
                "MoveDescription")
            ?? GetMemberValue(hoverTip, "Description")
            ?? (intentObject is not null && owner is not null
                ? TryInvokeCompatibleMethod(intentObject, "GetIntentDescription", playerTargets, owner)
                : null);
        var renderedDescriptionValue =
            GetFirstMemberValue(
                moveSource,
                "RenderedDescription",
                "RenderedText",
                "DisplayDescription",
                "DescriptionRendered",
                "Tooltip",
                "TooltipText")
            ?? GetFirstMemberValue(
                enemy,
                "RenderedIntentDescription",
                "IntentDescriptionRendered",
                "MoveDescriptionRendered")
            ?? GetFirstMemberValue(
                monster,
                "RenderedIntentDescription",
                "MoveDescriptionRendered")
            ?? GetMemberValue(hoverTip, "Description");

        var raw = ConvertDescriptionTemplateToText(
            rawDescriptionValue,
            $"{path}.move_description",
            textDiagnostics,
            "Description",
            "RulesText",
            "Text",
            "TooltipText",
            "IntentDescription",
            "IntentText");
        var rendered = ConvertToText(
            renderedDescriptionValue,
            $"{path}.move_description_rendered",
            textDiagnostics,
            "RenderedDescription",
            "RenderedText",
            "DisplayDescription",
            "DescriptionRendered");

        var vars = ExtractDescriptionVariables(
            source,
            raw,
            ResolveEnemyMoveDescriptionSeedVariables(moveSource, enemy, raw, traits, keywords));
        var renderOutcome = RenderDescription(raw, rendered, vars);
        var glossary = ExtractGlossaryAnchors(
            canonicalId: null,
            displayName: moveName,
            texts: new[] { renderOutcome.Text, raw, moveName },
            keywords: keywords,
            traits: traits);
        var canonicalDescription = ChooseCanonicalDescription(renderOutcome.Text, raw);

        if (!string.IsNullOrWhiteSpace(raw) || !string.IsNullOrWhiteSpace(rendered))
        {
            LogDescriptionDiagnostics(
                kind: "enemy_move",
                identifier: ResolveEnemyCanonicalId(enemy) ?? moveName ?? path,
                path: $"{path}.move_description",
                raw: raw,
                rendered: renderOutcome.Text,
                quality: renderOutcome.Quality,
                source: renderOutcome.Source,
                variables: vars,
                glossary: glossary);
        }

        return new DescriptionExtraction(raw, renderOutcome.Text, canonicalDescription, renderOutcome.Quality, renderOutcome.Source, vars, glossary);
    }

    private static IReadOnlyList<DescriptionVariable> ResolveEnemyMoveDescriptionSeedVariables(
        object? moveSource,
        object enemy,
        string? raw,
        IReadOnlyList<string> traits,
        IReadOnlyList<string> keywords)
    {
        var seedHints = (keywords ?? Array.Empty<string>())
            .Concat(traits ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var variables = new List<DescriptionVariable>();
        foreach (var key in new[] { "damage", "block", "draw", "strength", "weak", "vulnerable", "frail", "amount" })
        {
            if (!ShouldSeedDescriptionVariable(key, raw, seedHints))
            {
                continue;
            }

            if (moveSource is not null)
            {
                AddSeedVariable(variables, key, moveSource, "enemy_move_seed", null);
            }

            AddSeedVariable(variables, key, enemy, "enemy_seed", null);
        }

        return DeduplicateVariables(variables);
    }

    private IReadOnlyList<string> ExtractEnemyTraits(
        object enemy,
        object? monster,
        object? moveSource,
        string path,
        TextDiagnosticsCollector textDiagnostics)
    {
        return ExtractCombinedTextList(
            new object?[]
            {
                GetFirstMemberValue(enemy, "Traits", "Tags", "EnemyTags", "TraitIds", "TypeTags"),
                GetFirstMemberValue(monster, "Traits", "Tags", "EnemyTags", "TraitIds", "TypeTags"),
                GetFirstMemberValue(moveSource, "Traits", "Tags", "EnemyTags"),
            },
            path,
            textDiagnostics);
    }

    private IReadOnlyList<string> ExtractEnemyKeywords(
        EnemyIntentDescriptor intent,
        IReadOnlyList<GlossaryAnchor> moveGlossary,
        IReadOnlyList<string> traits,
        IReadOnlyList<RuntimePowerState> powers,
        object enemy,
        object? monster,
        object? moveSource,
        string path,
        TextDiagnosticsCollector textDiagnostics)
    {
        var keywords = new List<string>();
        keywords.AddRange(ExtractCombinedTextList(
            new object?[]
            {
                GetFirstMemberValue(enemy, "Keywords", "KeywordIds", "IntentKeywords"),
                GetFirstMemberValue(monster, "Keywords", "KeywordIds", "IntentKeywords"),
                GetFirstMemberValue(moveSource, "Keywords", "KeywordIds", "IntentKeywords"),
            },
            path,
            textDiagnostics));
        keywords.AddRange(intent.Effects ?? Array.Empty<string>());
        keywords.AddRange(moveGlossary.Select(anchor => anchor.GlossaryId));
        keywords.AddRange(traits.Select(trait => NormalizeGlossaryId(trait) ?? trait));
        keywords.AddRange(
            powers.Select(power => !string.IsNullOrWhiteSpace(power.CanonicalPowerId)
                ? power.CanonicalPowerId!
                : power.PowerId));
        return keywords
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static object? ResolveEnemyMoveSource(object enemy)
    {
        var monster = GetMemberValue(enemy, "Monster");
        var candidates = new[]
        {
            GetFirstMemberValue(enemy, "CurrentMove", "Move", "MoveData", "IntentData", "PlannedMove", "SelectedMove", "CurrentAction", "CurrentIntent", "NextMove"),
            GetFirstMemberValue(monster, "CurrentMove", "Move", "MoveData", "IntentData", "PlannedMove", "SelectedMove", "CurrentAction", "CurrentIntent", "NextMove"),
            GetMemberValue(enemy, "Intent"),
            GetMemberValue(monster, "Intent"),
        };

        foreach (var candidate in candidates)
        {
            if (candidate is null || candidate is string || candidate.GetType().IsValueType)
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static object? ResolvePrimaryEnemyIntentObject(object enemy)
    {
        var moveSource = ResolveEnemyMoveSource(enemy);
        var directIntent = EnumerateObjects(GetMemberValue(moveSource, "Intents")).FirstOrDefault();
        if (directIntent is not null)
        {
            return directIntent;
        }

        var monster = GetMemberValue(enemy, "Monster");
        return EnumerateObjects(GetMemberValue(GetMemberValue(monster, "NextMove"), "Intents")).FirstOrDefault();
    }

    private static object? ResolveEnemyOwnerCreature(object enemy)
    {
        return GetMemberValue(enemy, "Creature")
               ?? GetMemberValue(GetMemberValue(enemy, "Monster"), "Creature");
    }

    private object? BuildCreatureTargetCollection(object runState)
    {
        var creatures = GetPlayers(runState)
            .Select(player => GetMemberValue(player, "Creature"))
            .Where(creature => creature is not null)
            .Cast<object>()
            .ToArray();
        if (creatures.Length == 0)
        {
            return null;
        }

        var elementType = creatures[0].GetType();
        var typedArray = Array.CreateInstance(elementType, creatures.Length);
        for (var index = 0; index < creatures.Length; index += 1)
        {
            typedArray.SetValue(creatures[index], index);
        }

        return typedArray;
    }

    private static IReadOnlyDictionary<string, object?> CreateEnemyExportDiagnostic(
        int index,
        string enemyId,
        string field,
        string source,
        string detail,
        string status = "fallback")
    {
        return new Dictionary<string, object?>
        {
            ["enemy_index"] = index,
            ["enemy_id"] = enemyId,
            ["field"] = field,
            ["status"] = status,
            ["source"] = source,
            ["detail"] = detail,
        };
    }

    private static object? GetFirstMemberValue(object? target, params string[] memberNames)
    {
        foreach (var memberName in memberNames)
        {
            var value = GetMemberValue(target, memberName);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractCombinedTextList(
        IEnumerable<object?> collections,
        string path,
        TextDiagnosticsCollector textDiagnostics)
    {
        var results = new List<string>();
        var index = 0;
        foreach (var collection in collections)
        {
            results.AddRange(ExtractTextList(collection, $"{path}[{index}]", textDiagnostics));
            index += 1;
        }

        return results
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static object? TryInvokeCompatibleMethod(object? target, string methodName, params object?[] args)
    {
        if (target is null)
        {
            return null;
        }

        var methods = target.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .ToArray();
        foreach (var method in methods)
        {
            if (method.GetParameters().Length != args.Length)
            {
                continue;
            }

            try
            {
                return method.Invoke(target, args);
            }
            catch
            {
                // Continue probing other overloads.
            }
        }

        return null;
    }

    private static int? ConvertToInt32(object? value)
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

    private static bool IsLowQualityIntentLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        return trimmed.All(char.IsDigit) ||
               trimmed.StartsWith("FORMAT_", StringComparison.OrdinalIgnoreCase);
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

    private IReadOnlyList<RuntimePowerState> ExtractPowers(object? source, string path, TextDiagnosticsCollector textDiagnostics)
    {
        var collection = ResolvePowerCollection(source);
        return EnumerateObjects(collection)
            .Select((power, index) => DescribePower(power, $"{path}[{index}]", textDiagnostics))
            .Where(power => power is not null)
            .Cast<RuntimePowerState>()
            .ToArray();
    }

    private static object? ResolvePowerCollection(object? source)
    {
        if (source is null)
        {
            return null;
        }

        foreach (var memberName in new[] { "Powers", "StatusEffects", "Buffs", "Debuffs" })
        {
            var collection = GetMemberValue(source, memberName);
            if (collection is not null)
            {
                return collection;
            }
        }

        foreach (var nestedName in new[] { "Creature", "Monster" })
        {
            var nested = GetMemberValue(source, nestedName);
            if (nested is null || ReferenceEquals(nested, source))
            {
                continue;
            }

            var collection = ResolvePowerCollection(nested);
            if (collection is not null)
            {
                return collection;
            }
        }

        return null;
    }

    private RuntimePowerState? DescribePower(object? power, string path, TextDiagnosticsCollector textDiagnostics)
    {
        var name = ConvertToText(
            GetMemberValue(power, "Name") ?? GetMemberValue(power, "DisplayName") ?? power,
            $"{path}.name",
            textDiagnostics,
            "Name",
            "DisplayName");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var raw = ConvertDescriptionTemplateToText(
            GetMemberValue(power, "Description")
            ?? GetMemberValue(power, "RulesText")
            ?? GetMemberValue(power, "Text"),
            $"{path}.description",
            textDiagnostics,
            "Description",
            "RulesText",
            "Text");
        var rendered = ConvertToText(
            GetMemberValue(power, "RenderedDescription")
            ?? GetMemberValue(power, "RenderedText")
            ?? GetMemberValue(power, "DisplayDescription")
            ?? GetMemberValue(power, "DescriptionRendered"),
            $"{path}.description_rendered",
            textDiagnostics,
            "RenderedDescription",
            "RenderedText",
            "DisplayDescription",
            "DescriptionRendered");
        var canonicalPowerId = ResolvePowerCanonicalId(power);
        var vars = ExtractDescriptionVariables(power, raw, ResolvePowerDescriptionSeedVariables(power, canonicalPowerId));
        var renderOutcome = RenderDescription(raw, rendered, vars);
        var glossary = ExtractGlossaryAnchors(
            canonicalPowerId,
            name,
            new[] { renderOutcome.Text, raw },
            keywords: null,
            traits: null);
        LogDescriptionDiagnostics(
            kind: "power",
            identifier: canonicalPowerId ?? name,
            path: path,
            raw: raw,
            rendered: ChooseCanonicalDescription(renderOutcome.Text, raw),
            quality: renderOutcome.Quality,
            source: renderOutcome.Source,
            variables: vars,
            glossary: glossary);

        return new RuntimePowerState(
            PowerId: canonicalPowerId ?? name,
            Name: name,
            Amount: GetNullableInt(power, "Amount")
                    ?? GetNullableInt(power, "Stacks")
                    ?? GetNullableInt(power, "Value"),
            Description: ChooseCanonicalDescription(renderOutcome.Text, raw),
            CanonicalPowerId: canonicalPowerId,
            Glossary: glossary);
    }

    private static IReadOnlyList<DescriptionVariable> ResolveCardDescriptionSeedVariables(object? card, string? raw, IReadOnlyList<string>? keywords)
    {
        var variables = new List<DescriptionVariable>();
        foreach (var key in new[] { "damage", "block", "draw", "strength", "magic" })
        {
            if (ShouldSeedDescriptionVariable(key, raw, keywords))
            {
                AddSeedVariable(variables, key, card, "member_alias", null);
            }
        }

        if (keywords is not null)
        {
            foreach (var keyword in keywords)
            {
                var normalized = NormalizeGlossaryId(keyword);
                if (normalized is not null &&
                    (string.Equals(normalized, "damage", StringComparison.Ordinal) ||
                     string.Equals(normalized, "block", StringComparison.Ordinal) ||
                     string.Equals(normalized, "draw", StringComparison.Ordinal) ||
                     string.Equals(normalized, "strength", StringComparison.Ordinal)))
                {
                    AddSeedVariable(variables, normalized, card, "keyword_seed", keyword);
                }
            }
        }

        return DeduplicateVariables(variables);
    }

    private static bool ShouldSeedDescriptionVariable(string key, string? raw, IReadOnlyList<string>? keywords)
    {
        if (keywords is not null && keywords.Any(keyword => string.Equals(NormalizeGlossaryId(keyword), key, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (KnownGlossarySpecs.TryGetValue(key, out var spec))
        {
            return spec.Terms.Any(term => raw.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return raw.Contains(key, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<DescriptionVariable> ResolvePowerDescriptionSeedVariables(object? power, string? canonicalPowerId)
    {
        var variables = new List<DescriptionVariable>();
        AddSeedVariable(variables, "amount", power, "member_alias", null);
        var normalizedPowerId = NormalizeGlossaryId(canonicalPowerId);
        if (normalizedPowerId is not null &&
            DescriptionVariableMemberAliases.ContainsKey(normalizedPowerId))
        {
            AddSeedVariable(variables, normalizedPowerId, power, "power_id", canonicalPowerId);
        }

        return DeduplicateVariables(variables);
    }

    private static void AddSeedVariable(List<DescriptionVariable> variables, string key, object? source, string sourceLabel, string? placeholder)
    {
        var resolution = ResolveDescriptionVariableValue(source, key);
        if (resolution.Value is null)
        {
            return;
        }

        variables.Add(new DescriptionVariable(key, resolution.Value, resolution.Source ?? sourceLabel, placeholder));
    }

    private static IReadOnlyList<DescriptionVariable> ExtractDescriptionVariables(
        object? source,
        string? raw,
        IReadOnlyList<DescriptionVariable>? seedVariables = null)
    {
        var variables = new List<DescriptionVariable>();
        if (seedVariables is not null)
        {
            variables.AddRange(seedVariables);
        }

        if (!string.IsNullOrWhiteSpace(raw))
        {
            foreach (Match match in PlaceholderRegex.Matches(raw))
            {
                var placeholder = match.Groups["name"].Value;
                if (string.IsNullOrWhiteSpace(placeholder))
                {
                    continue;
                }

                var key = NormalizeDescriptionVariableKey(placeholder);
                var resolution = ResolveDescriptionVariableValue(source, key);
                variables.Add(new DescriptionVariable(
                    key,
                    resolution.Value,
                    resolution.Source ?? "description_placeholder",
                    placeholder));
            }
        }

        return DeduplicateVariables(variables);
    }

    private static IReadOnlyList<DescriptionVariable> DeduplicateVariables(IEnumerable<DescriptionVariable> variables)
    {
        return variables
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Key))
            .GroupBy(variable => variable.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var preferred = group.FirstOrDefault(variable => variable.Value is not null);
                if (preferred is not null)
                {
                    return preferred;
                }

                return group.First();
            })
            .ToArray();
    }

    private static VariableResolution ResolveDescriptionVariableValue(object? source, string? key)
    {
        if (source is null || string.IsNullOrWhiteSpace(key))
        {
            return new VariableResolution(null, null);
        }

        if (!DescriptionVariableMemberAliases.TryGetValue(key, out var aliases))
        {
            aliases = new[] { key };
        }

        foreach (var match in EnumerateDescriptionVariableMatches(source, aliases))
        {
            return match;
        }

        return new VariableResolution(null, null);
    }

    private static string NormalizeDescriptionVariableKey(string? rawKey)
    {
        var normalized = NormalizeGlossaryId(rawKey);
        return normalized switch
        {
            "attackdamage" => "damage",
            "currentdamage" => "damage",
            "basedamage" => "damage",
            "damageamount" => "damage",
            "currentblock" => "block",
            "baseblock" => "block",
            "blockamount" => "block",
            "drawamount" => "draw",
            "drawcount" => "draw",
            "carddraw" => "draw",
            "magicnumber" => "magic",
            "magicvalue" => "magic",
            "strengthamount" => "strength",
            _ => normalized ?? string.Empty,
        };
    }

    private static RenderOutcome RenderDescription(
        string? raw,
        string? runtimeRendered,
        IReadOnlyList<DescriptionVariable>? variables)
    {
        var preferred = NormalizeDescriptionText(runtimeRendered);
        var template = RenderTemplateDescription(raw, variables);
        if (string.IsNullOrWhiteSpace(template) && string.IsNullOrWhiteSpace(preferred))
        {
            return new RenderOutcome(null, null, null);
        }

        if (!string.IsNullOrWhiteSpace(template) && !ContainsDescriptionPlaceholder(template))
        {
            return new RenderOutcome(
                template,
                "resolved",
                preferred is not null ? "runtime_rendered_with_markdown_glossary" : "rendered_from_vars");
        }

        if (!string.IsNullOrWhiteSpace(preferred) && !ContainsDescriptionPlaceholder(preferred))
        {
            return new RenderOutcome(preferred, "resolved", "runtime_rendered");
        }

        var hasResolvedVariables = (variables ?? Array.Empty<DescriptionVariable>()).Any(variable => variable.Value is not null);
        return new RenderOutcome(
            template ?? preferred,
            hasResolvedVariables ? "partial" : "template_fallback",
            preferred is not null ? "runtime_template_fallback" : "raw_template");
    }

    private static string? RenderTemplateDescription(string? raw, IReadOnlyList<DescriptionVariable>? variables)
    {
        var template = raw?.Trim();
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        var variableMap = (variables ?? Array.Empty<DescriptionVariable>())
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Key))
            .GroupBy(variable => variable.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);

        var rendered = PlaceholderRegex.Replace(template, match =>
        {
            var key = NormalizeDescriptionVariableKey(match.Groups["name"].Value);
            if (variableMap.TryGetValue(key, out var value) && value is not null)
            {
                return value.Value.ToString();
            }

            return match.Value;
        });

        return NormalizeDescriptionText(rendered);
    }

    private static string? NormalizeDescriptionText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = NormalizeDescriptionRichText(text).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeDescriptionRichText(string text)
    {
        var normalized = text;
        for (var pass = 0; pass < 4; pass += 1)
        {
            var replaced = RichTextPairRegex.Replace(normalized, match =>
            {
                var tag = match.Groups["tag"].Value;
                var content = match.Groups["content"].Value;
                return string.Equals(tag, "gold", StringComparison.OrdinalIgnoreCase)
                    ? $"**{content.Trim()}**"
                    : content;
            });
            if (string.Equals(replaced, normalized, StringComparison.Ordinal))
            {
                break;
            }

            normalized = replaced;
        }

        return RichTextTagRegex.Replace(normalized, string.Empty);
    }

    private static string? ChooseCanonicalDescription(string? rendered, string? raw)
    {
        return NormalizeDescriptionText(rendered) ?? NormalizeDescriptionText(raw);
    }

    private static bool ContainsDescriptionPlaceholder(string? text)
    {
        return !string.IsNullOrWhiteSpace(text) && PlaceholderRegex.IsMatch(text);
    }

    private static IEnumerable<VariableResolution> EnumerateDescriptionVariableMatches(object source, IReadOnlyList<string> aliases)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object Node, string Path, int Depth)>();
        queue.Enqueue((source, "card", 0));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current.Node))
            {
                continue;
            }

            foreach (var alias in aliases)
            {
                var direct = GetNullableInt(current.Node, alias);
                if (direct is not null)
                {
                    yield return new VariableResolution(direct, $"member:{current.Path}.{alias}");
                }

                var method = InvokeNullableIntMethod(current.Node, alias);
                if (method is not null)
                {
                    yield return new VariableResolution(method, $"method:{current.Path}.{alias}()");
                }
            }

            foreach (var fuzzy in EnumerateNumericMembersByHint(current.Node, aliases, current.Path))
            {
                yield return fuzzy;
            }

            if (current.Depth >= 2)
            {
                continue;
            }

            foreach (var nestedName in DescriptionSearchNestedMembers)
            {
                var nested = GetMemberValue(current.Node, nestedName);
                if (nested is null || nested is string || nested is ValueType)
                {
                    continue;
                }

                if (nested is IEnumerable enumerable && nested is not IDictionary)
                {
                    var nestedIndex = 0;
                    foreach (var item in enumerable)
                    {
                        if (item is null || item is string || item is ValueType)
                        {
                            nestedIndex += 1;
                            continue;
                        }

                        queue.Enqueue((item, $"{current.Path}.{nestedName}[{nestedIndex}]", current.Depth + 1));
                        nestedIndex += 1;
                    }

                    continue;
                }

                queue.Enqueue((nested, $"{current.Path}.{nestedName}", current.Depth + 1));
            }
        }
    }

    private static IEnumerable<VariableResolution> EnumerateNumericMembersByHint(object source, IReadOnlyList<string> aliases, string path)
    {
        var type = source.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var aliasSet = new HashSet<string>(aliases, StringComparer.OrdinalIgnoreCase);
        var tokenSet = aliases
            .SelectMany(alias => SplitIdentifierTokens(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var property in type.GetProperties(flags).Where(property => property.GetIndexParameters().Length == 0))
        {
            if (!IsNumericType(property.PropertyType))
            {
                continue;
            }

            if (!NameMatchesDescriptionHint(property.Name, aliasSet, tokenSet))
            {
                continue;
            }

            var value = GetNullableInt(source, property.Name);
            if (value is not null)
            {
                yield return new VariableResolution(value, $"property_hint:{path}.{property.Name}");
            }
        }

        foreach (var field in type.GetFields(flags))
        {
            if (!IsNumericType(field.FieldType))
            {
                continue;
            }

            if (!NameMatchesDescriptionHint(field.Name, aliasSet, tokenSet))
            {
                continue;
            }

            var value = GetNullableInt(source, field.Name);
            if (value is not null)
            {
                yield return new VariableResolution(value, $"field_hint:{path}.{field.Name}");
            }
        }
    }

    private static int? InvokeNullableIntMethod(object source, string alias)
    {
        foreach (var methodName in new[] { alias, $"Get{alias}", $"Compute{alias}", $"Resolve{alias}", $"GetCurrent{alias}" })
        {
            var method = source.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (method is null || !IsNumericType(method.ReturnType))
            {
                continue;
            }

            try
            {
                var value = method.Invoke(source, null);
                if (value is not null)
                {
                    return Convert.ToInt32(value);
                }
            }
            catch
            {
                // ignore dynamic runtime invocation failures
            }
        }

        return null;
    }

    private static bool NameMatchesDescriptionHint(string name, ISet<string> aliasSet, IReadOnlyList<string> tokenSet)
    {
        if (aliasSet.Contains(name))
        {
            return true;
        }

        return tokenSet.All(token => name.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> SplitIdentifierTokens(string value)
    {
        return Regex.Matches(value, "[A-Z]?[a-z]+|[0-9]+|[A-Z]+(?![a-z])")
            .Select(match => match.Value)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
    }

    private static bool IsNumericType(Type type)
    {
        var candidate = Nullable.GetUnderlyingType(type) ?? type;
        return candidate == typeof(int) ||
               candidate == typeof(short) ||
               candidate == typeof(long) ||
               candidate == typeof(byte);
    }

    private static IReadOnlyList<GlossaryAnchor> ExtractGlossaryAnchors(
        string? canonicalId,
        string? displayName,
        IEnumerable<string?> texts,
        IReadOnlyList<string>? keywords,
        IReadOnlyList<string>? traits)
    {
        var anchors = new List<GlossaryAnchor>();
        AddGlossaryAnchorFromId(anchors, canonicalId, displayName, "canonical_id");

        foreach (var keyword in keywords ?? Array.Empty<string>())
        {
            AddGlossaryAnchorFromId(anchors, keyword, keyword, "keyword");
        }

        foreach (var trait in traits ?? Array.Empty<string>())
        {
            AddGlossaryAnchorFromId(anchors, trait, trait, "trait");
        }

        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (var spec in KnownGlossarySpecs.Values)
            {
                if (spec.Terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)))
                {
                    AddGlossaryAnchor(anchors, spec.GlossaryId, spec.DisplayText, spec.Hint, "description_text");
                }
            }
        }

        return anchors
            .GroupBy(anchor => anchor.GlossaryId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static void AddGlossaryAnchorFromId(List<GlossaryAnchor> anchors, string? rawId, string? displayText, string source)
    {
        var normalizedId = NormalizeGlossaryId(rawId);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return;
        }

        if (KnownGlossarySpecs.TryGetValue(normalizedId, out var spec))
        {
            AddGlossaryAnchor(anchors, spec.GlossaryId, spec.DisplayText, spec.Hint, source);
            return;
        }

        var normalizedDisplayText = NormalizeDescriptionText(displayText) ?? displayText?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedDisplayText))
        {
            return;
        }

        AddGlossaryAnchor(anchors, normalizedId, normalizedDisplayText!, null, source);
    }

    private static void AddGlossaryAnchor(List<GlossaryAnchor> anchors, string glossaryId, string displayText, string? hint, string source)
    {
        if (anchors.Any(anchor => string.Equals(anchor.GlossaryId, glossaryId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        anchors.Add(new GlossaryAnchor(glossaryId, displayText, hint, source));
    }

    private static string? NormalizeGlossaryId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character))
            .ToArray());
    }

    private static IReadOnlyList<string> ExtractTextList(object? collection, string path, TextDiagnosticsCollector textDiagnostics)
    {
        return EnumerateObjects(collection)
            .Select((item, index) => ConvertToText(item, $"{path}[{index}]", textDiagnostics, "Name", "Label", "Title", "Text", "Id", "Key"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? NormalizeIntentType(string? raw, object enemy)
    {
        var direct = ConvertToText(
            GetMemberValue(enemy, "IntentType")
            ?? GetMemberValue(enemy, "IntentKind")
            ?? GetMemberValue(GetMemberValue(enemy, "Monster"), "IntentType"));
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var normalized = raw.ToLowerInvariant();
        var hasAttack = normalized.Contains("attack", StringComparison.Ordinal);
        var hasBlock = normalized.Contains("block", StringComparison.Ordinal) || normalized.Contains("defend", StringComparison.Ordinal);
        var hasBuff = normalized.Contains("buff", StringComparison.Ordinal);
        var hasDebuff = normalized.Contains("debuff", StringComparison.Ordinal) ||
                        normalized.Contains("weak", StringComparison.Ordinal) ||
                        normalized.Contains("frail", StringComparison.Ordinal) ||
                        normalized.Contains("vulnerable", StringComparison.Ordinal);

        if (hasAttack && hasBlock)
        {
            return "attack_block";
        }

        if (hasAttack && hasDebuff)
        {
            return "attack_debuff";
        }

        if (hasAttack && hasBuff)
        {
            return "attack_buff";
        }

        if (hasAttack)
        {
            return "attack";
        }

        if (hasBlock)
        {
            return "block";
        }

        if (hasDebuff)
        {
            return "debuff";
        }

        if (hasBuff)
        {
            return "buff";
        }

        return normalized.Replace(' ', '_');
    }

    private static IReadOnlyList<string> ExtractIntentEffects(object enemy, string? raw)
    {
        var effects = new List<string>();
        foreach (var item in EnumerateObjects(GetMemberValue(enemy, "IntentEffects") ?? GetMemberValue(enemy, "IntentKeywords")))
        {
            var text = ConvertToText(item);
            if (!string.IsNullOrWhiteSpace(text))
            {
                effects.Add(text!);
            }
        }

        if (!string.IsNullOrWhiteSpace(raw))
        {
            var normalized = raw.ToLowerInvariant();
            if (normalized.Contains("weak", StringComparison.Ordinal))
            {
                effects.Add("weak");
            }

            if (normalized.Contains("frail", StringComparison.Ordinal))
            {
                effects.Add("frail");
            }

            if (normalized.Contains("vulnerable", StringComparison.Ordinal))
            {
                effects.Add("vulnerable");
            }
        }

        return effects
            .Where(effect => !string.IsNullOrWhiteSpace(effect))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, object?> BuildCardPreview(HandCardDescriptor card)
    {
        var preview = new Dictionary<string, object?>
        {
            ["card_id"] = card.CardId,
            ["card_name"] = card.Name,
            ["target_type"] = card.TargetType,
            ["canonical_card_id"] = card.CanonicalCardId,
            ["description"] = card.Description,
            ["glossary"] = card.Glossary,
            ["upgraded"] = card.Upgraded,
            ["traits"] = card.Traits,
            ["keywords"] = card.Keywords,
        };
        return preview
            .Where(pair => pair.Value is not null &&
                           (!(pair.Value is string text) || !string.IsNullOrWhiteSpace(text)) &&
                           (!(pair.Value is IReadOnlyCollection<string> values) || values.Count > 0))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private void LogTextDiagnostics(string windowKind, TextDiagnosticsCollector collector)
    {
        var metadata = collector.ToMetadata();
        var resolved = Convert.ToInt32(metadata["resolved"]);
        var fallback = Convert.ToInt32(metadata["fallback"]);
        var unresolved = Convert.ToInt32(metadata["unresolved"]);
        if (fallback == 0 && unresolved == 0)
        {
            if (_options.LogDescriptionSuccesses && resolved > 0)
            {
                _logger?.Info($"Text resolution window={windowKind} resolved={resolved} fallback=0 unresolved=0");
            }

            return;
        }

        var entries = metadata.TryGetValue("entries", out var rawEntries) && rawEntries is IReadOnlyCollection<IReadOnlyDictionary<string, object?>> diagnostics
            ? diagnostics.Take(5).Select(FormatTextDiagnosticEntry).ToArray()
            : Array.Empty<string>();
        var entrySummary = entries.Length == 0 ? "none" : string.Join(" | ", entries);
        _logger?.Warn($"Text resolution issues window={windowKind} resolved={resolved} fallback={fallback} unresolved={unresolved} entries={entrySummary}");
    }

    private void LogDescriptionDiagnostics(
        string kind,
        string identifier,
        string path,
        string? raw,
        string? rendered,
        string? quality,
        string? source,
        IReadOnlyList<DescriptionVariable> variables,
        IReadOnlyList<GlossaryAnchor> glossary)
    {
        var unresolvedVariables = variables.Where(variable => variable.Value is null).Select(variable => variable.Key).Distinct(StringComparer.Ordinal).ToArray();
        var expectedGlossaryNormalization = !string.IsNullOrWhiteSpace(raw) && RichTextPairRegex.IsMatch(raw);
        var glossaryNormalizationMissing = expectedGlossaryNormalization &&
                                          (!string.IsNullOrWhiteSpace(rendered) && rendered.Contains('[', StringComparison.Ordinal));
        var requiresWarning =
            string.Equals(quality, "template_fallback", StringComparison.Ordinal) ||
            unresolvedVariables.Length > 0 ||
            glossaryNormalizationMissing;

        if (!requiresWarning && !_options.LogDescriptionSuccesses)
        {
            return;
        }

        var message =
            $"Description {kind}={identifier} path={path} quality={quality ?? "unknown"} source={source ?? "unknown"} " +
            $"unresolved_vars={(unresolvedVariables.Length == 0 ? "-" : string.Join(",", unresolvedVariables))} " +
            $"glossary={glossary.Count} raw=\"{AbbreviateForLog(raw)}\" rendered=\"{AbbreviateForLog(rendered)}\"";
        if (requiresWarning)
        {
            _logger?.Warn(message);
            return;
        }

        _logger?.Info(message);
    }

    private static string FormatTextDiagnosticEntry(IReadOnlyDictionary<string, object?> entry)
    {
        var path = ConvertToText(GetDictionaryValue(entry, "path")) ?? "?";
        var status = ConvertToText(GetDictionaryValue(entry, "status")) ?? "?";
        var source = ConvertToText(GetDictionaryValue(entry, "source")) ?? "?";
        return $"{path}:{status}:{source}";
    }

    private static string AbbreviateForLog(string? text, int maxLength = 160)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
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

    private static string? ConvertDescriptionTemplateToText(object? value, string path, TextDiagnosticsCollector? textDiagnostics = null, params string[] preferredMembers)
    {
        if (value is not null)
        {
            var typeName = GetTypeName(value) ?? string.Empty;
            if (typeName.Contains("LocString", StringComparison.Ordinal))
            {
                if (TryInvokeParameterlessMethod(value, "GetRawText") is string rawText && !string.IsNullOrWhiteSpace(rawText))
                {
                    textDiagnostics?.Record(path, new TextResolutionResult(rawText, "fallback", "loc_string.raw"));
                    return rawText;
                }

                if (GetMemberValue(value, "LocEntryKey") is string entryKey && !string.IsNullOrWhiteSpace(entryKey))
                {
                    textDiagnostics?.Record(path, new TextResolutionResult(entryKey, "fallback", "loc_string.key"));
                    return entryKey;
                }
            }
        }

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

    private static string? NormalizeTypeName(object? target)
    {
        var type = target as Type ?? target?.GetType();
        if (type is null)
        {
            return null;
        }

        return type.Name;
    }

    private readonly record struct PlayerBuildResult(
        RuntimePlayerState? Player,
        IReadOnlyDictionary<string, object?> Diagnostics);

    private readonly record struct PileCardExtraction(
        IReadOnlyList<RuntimeCard> Cards,
        string Source,
        int ExpectedCount,
        string? FallbackReason = null)
    {
        public bool IsDegraded => !string.IsNullOrWhiteSpace(FallbackReason) || Cards.Count != ExpectedCount;
    }

    private readonly record struct RuntimeRoot(object GameInstance, object? RunNode, object? RunState);

    private sealed record GlossarySpec(
        string GlossaryId,
        string DisplayText,
        string Hint,
        IReadOnlyList<string> Terms);

    private readonly record struct VariableResolution(int? Value, string? Source);

    private readonly record struct RenderOutcome(string? Text, string? Quality, string? Source);

    private readonly record struct DescriptionExtraction(
        string? Raw,
        string? Rendered,
        string? Description,
        string? Quality,
        string? Source,
        IReadOnlyList<DescriptionVariable> Vars,
        IReadOnlyList<GlossaryAnchor> Glossary);

    private readonly record struct HandCardDescriptor(
        string CardId,
        string Name,
        TextResolutionResult NameResolution,
        string? TargetType,
        bool Playable,
        string? CanonicalCardId,
        string? Description,
        IReadOnlyList<GlossaryAnchor> Glossary,
        bool? Upgraded,
        IReadOnlyList<string> Traits,
        IReadOnlyList<string> Keywords);
    private readonly record struct EnemyBuildResult(
        IReadOnlyList<RuntimeEnemyState> Enemies,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Diagnostics);
    private readonly record struct EnemyEnrichmentDescriptor(
        string? MoveName,
        string? MoveDescription,
        IReadOnlyList<GlossaryAnchor> MoveGlossary,
        IReadOnlyList<string> Traits,
        IReadOnlyList<string> Keywords);
    private readonly record struct EnemyIntentDescriptor(
        string Display,
        string? Raw,
        string? Type,
        int? Damage,
        int? Hits,
        int? Block,
        IReadOnlyList<string> Effects);
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
        bool AdvanceButtonDetected,
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
                ["advance_button_detected"] = AdvanceButtonDetected,
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
            var choices = FilterCardRewardSelectionChoices(ExtractCardRewardChoiceItems(cardRewardScreen));
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

            var choiceTypeName = GetTypeName(choice) ?? string.Empty;
            var directSelect = cardRewardScreen.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(candidate =>
                {
                    if (!string.Equals(candidate.Name, "SelectCard", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var parameters = candidate.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(choice);
                });

            if (directSelect is not null)
            {
                try
                {
                    directSelect.Invoke(cardRewardScreen, new[] { choice });
                    return new RuntimeActionResult(true, "Selected card reward.", metadata: new Dictionary<string, object?>
                    {
                        ["reward"] = ConvertToText(GetDictionaryValue(action.Params, "reward")),
                        ["reward_index"] = selectedIndex,
                        ["runtime_handler"] = $"card_reward_screen.{directSelect.Name}",
                        ["choice_type"] = choiceTypeName,
                    });
                }
                catch (Exception ex)
                {
                    return new RuntimeActionResult(false, $"Card reward selection failed: {ex.GetBaseException().Message}", "runtime_incompatible");
                }
            }
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

    private RuntimeActionResult ExecuteAdvanceReward(object runNode, ActionRequest request, LegalAction action)
    {
        var rewardScreen = GetRewardScreen(runNode);
        if (rewardScreen is null)
        {
            return new RuntimeActionResult(false, "Rewards screen is not available.", "runtime_not_ready");
        }

        var requestedLabel = ConvertToText(GetDictionaryValue(action.Params, "button_label"));
        var textDiagnostics = new TextDiagnosticsCollector();
        if (!TryFindRewardAdvanceButton(runNode, rewardScreen, textDiagnostics, out var buttonNode, out var buttonLabel))
        {
            return new RuntimeActionResult(false, "Reward advance button is not currently available.", "stale_action", new Dictionary<string, object?>
            {
                ["action_type"] = "advance_reward",
                ["button_label"] = requestedLabel,
            });
        }

        if (!string.IsNullOrWhiteSpace(requestedLabel) &&
            !string.Equals(buttonLabel, requestedLabel, StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeActionResult(false, "Reward advance button changed since the action was generated.", "stale_action", new Dictionary<string, object?>
            {
                ["action_type"] = "advance_reward",
                ["button_label"] = requestedLabel,
                ["resolved_button_label"] = buttonLabel,
            });
        }

        if (TryInvokeFirstCompatibleMethod(rewardScreen, RewardAdvanceMethodNames, new[] { new object?[] { buttonNode }, Array.Empty<object?>() }, out var rewardScreenMethod))
        {
            return new RuntimeActionResult(true, "Advanced reward screen.", metadata: new Dictionary<string, object?>
            {
                ["action_type"] = "advance_reward",
                ["button_label"] = buttonLabel,
                ["runtime_handler"] = $"reward_screen.{rewardScreenMethod}",
                ["next_window_expected"] = "map",
            });
        }

        if (TryActivateMenuNode(buttonNode, out var buttonHandler))
        {
            return new RuntimeActionResult(true, "Advanced reward screen.", metadata: new Dictionary<string, object?>
            {
                ["action_type"] = "advance_reward",
                ["button_label"] = buttonLabel,
                ["runtime_handler"] = buttonHandler,
                ["next_window_expected"] = "map",
            });
        }

        return new RuntimeActionResult(false, "Reward advance button is not clickable.", "not_clickable", new Dictionary<string, object?>
        {
            ["action_type"] = "advance_reward",
            ["button_label"] = buttonLabel,
            ["target_type"] = GetTypeName(buttonNode),
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
            ["action_type"] = "choose_map_node",
            ["node"] = node,
            ["coord"] = $"{coord.Value.Col},{coord.Value.Row}",
            ["next_window_expected"] = "room_transition",
            ["transition_kind"] = "map_travel_submitted",
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
