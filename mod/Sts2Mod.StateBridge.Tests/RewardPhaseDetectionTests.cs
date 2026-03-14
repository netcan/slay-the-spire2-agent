using System.Reflection;
using Sts2Mod.StateBridge.Configuration;
using Sts2Mod.StateBridge.Contracts;
using Sts2Mod.StateBridge.Core;
using Sts2Mod.StateBridge.Extraction;
using Sts2Mod.StateBridge.Providers;
using Xunit;

namespace Sts2Mod.StateBridge.Tests;

public sealed class RewardPhaseDetectionTests
{
    [Fact]
    public void DetectPhase_ReturnsRewardWhenRewardButtonsAreVisible()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(new FakeScreenTracker(
            rewardScreen: new FakeRewardScreen(
                isComplete: true,
                visible: true,
                new FakeRewardButton(new FakeReward("Burning Pact")))));
        var runState = new FakeRunState(new[] { new FakeEnemy("enemy-1", true) });

        var phase = InvokeDetectPhase(reader, runNode, runState);

        Assert.Equal(DecisionPhase.Reward, phase);
    }

    [Fact]
    public void DetectPhase_ReturnsRewardWhenCombatIsClearedAndRewardScreenIsConnected()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(new FakeScreenTracker(
            rewardScreen: new FakeRewardScreen(isComplete: true, visible: false)));
        var runState = new FakeRunState(Array.Empty<FakeEnemy>());

        var phase = InvokeDetectPhase(reader, runNode, runState);

        Assert.Equal(DecisionPhase.Reward, phase);
    }

    [Fact]
    public void DetectPhase_FallsBackToOverlayRewardScreenInSinglePlayer()
    {
        var reader = CreateReader();
        var rewardScreen = new FakeRewardScreen(
            isComplete: false,
            visible: true,
            new FakeRewardButton(new FakeReward("Battle Trance")));
        var runNode = new FakeRunNode(
            new FakeScreenTracker(),
            new FakeGlobalUi(new FakeOverlayStack(rewardScreen)));
        var runState = new FakeRunState(new[] { new FakeEnemy("enemy-1", true) });

        var phase = InvokeDetectPhase(reader, runNode, runState);

        Assert.Equal(DecisionPhase.Reward, phase);
    }

    [Fact]
    public void BuildCombatWindow_UsesTransitionWindowAndNoActionsWhenEnemiesAreGone()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(new FakeScreenTracker());
        var runState = new FakeRunState(Array.Empty<FakeEnemy>());

        var window = InvokeBuildCombatWindow(reader, runNode, runState);
        var exported = new CombatWindowExtractor().Export(window, new BridgeSessionState(new BridgeOptions()));

        Assert.Equal(DecisionPhase.Combat, window.Phase);
        Assert.Empty(window.Actions);
        Assert.Equal("combat_transition", exported.Snapshot.Metadata["window_kind"]);
        Assert.Empty(exported.Actions);
    }

    [Fact]
    public void BuildCombatWindow_ExportsRichCardsEnemiesPowersAndRunState()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(new FakeScreenTracker());
        var currentPoint = new FakeMapPoint("Monster", new FakeMapCoord(1, 1),
            new FakeMapPoint("Elite", new FakeMapCoord(2, 2)));
        var runState = new FakeRunState(
            new[]
            {
                new FakeEnemy("enemy-1", true, intent: "Attack+Weak", intentDamage: 7, intentHits: 2)
                {
                    CurrentMove = new FakeEnemyMove("Gnaw")
                    {
                        Description = "Deal {Damage:diff()} [gold]damage[/gold]. Gain {Block:diff()} [gold]Block[/gold].",
                        Damage = 7,
                        Block = 4,
                        Keywords = new[] { "damage", "block" },
                    },
                    Traits = new[] { "beast" },
                    Keywords = new[] { "ambush" },
                },
            },
            currentMapPoint: currentPoint,
            hand: new[]
            {
                new FakeCard("Strike")
                {
                    CardId = "strike_red",
                    Description = "Deal {Damage:diff()} [gold]damage[/gold].",
                    RenderedDescription = "Deal 6 damage.",
                    Damage = 6,
                    TargetType = "AnyEnemy",
                    CardType = "Attack",
                    Rarity = "Starter",
                    Traits = new[] { "starter" },
                    Keywords = new[] { "damage" },
                },
            },
            drawPile: new[]
            {
                new FakeCard("Pommel Strike")
                {
                    CardId = "pommel_strike",
                    Description = "Deal {Damage:diff()} [gold]damage[/gold]. Draw 1 card.",
                    RenderedDescription = "Deal 9 damage. Draw 1 card.",
                    Damage = 9,
                    TargetType = "AnyEnemy",
                    CardType = "Attack",
                    Rarity = "Common",
                    Keywords = new[] { "damage", "draw" },
                },
            },
            discardPile: new[]
            {
                new FakeCard("Defend")
                {
                    CardId = "defend_red",
                    Description = "Gain {Block:diff()} [gold]Block[/gold].",
                    RenderedDescription = "Gain 5 Block.",
                    Block = 5,
                    TargetType = "Self",
                    CardType = "Skill",
                    Rarity = "Starter",
                    Keywords = new[] { "block" },
                },
            });

        var window = InvokeBuildCombatWindow(reader, runNode, runState);
        var exported = new CombatWindowExtractor().Export(window, new BridgeSessionState(new BridgeOptions()));

        var player = exported.Snapshot.Player;
        Assert.NotNull(player);
        var card = Assert.Single(player.Hand);
        Assert.Equal("strike_red", card.CanonicalCardId);
        Assert.Equal("Deal 6 **damage**.", card.Description);
        Assert.Contains(card.Glossary ?? Array.Empty<GlossaryAnchor>(), anchor => anchor.GlossaryId == "damage");
        Assert.Equal("AnyEnemy", card.TargetType);
        Assert.Contains("starter", card.Traits ?? Array.Empty<string>());
        var drawPileCard = Assert.Single(player.DrawPileCards ?? Array.Empty<CardView>());
        Assert.Equal("pommel_strike", drawPileCard.CanonicalCardId);
        Assert.Contains(drawPileCard.Glossary ?? Array.Empty<GlossaryAnchor>(), anchor => anchor.GlossaryId == "damage");
        Assert.Equal(1, player.DrawPile);
        var discardPileCard = Assert.Single(player.DiscardPileCards ?? Array.Empty<CardView>());
        Assert.Equal("defend_red", discardPileCard.CanonicalCardId);
        Assert.Contains(discardPileCard.Glossary ?? Array.Empty<GlossaryAnchor>(), anchor => anchor.GlossaryId == "block");
        Assert.Equal(1, player.DiscardPile);
        Assert.Empty(player.ExhaustPileCards ?? Array.Empty<CardView>());
        Assert.Equal(0, player.ExhaustPile);
        Assert.Contains("Metallicize", player.Powers?.Select(power => power.Name) ?? Array.Empty<string>());
        var playerPower = Assert.Single(player.Powers ?? Array.Empty<PowerView>());
        Assert.Equal("Gain 3 Block at end of turn.", playerPower.Description);
        Assert.Contains(playerPower.Glossary ?? Array.Empty<GlossaryAnchor>(), anchor => anchor.GlossaryId == "metallicize");
        Assert.Contains(playerPower.Glossary ?? Array.Empty<GlossaryAnchor>(), anchor => anchor.GlossaryId == "block");

        var enemy = Assert.Single(exported.Snapshot.Enemies);
        Assert.Equal("louse", enemy.CanonicalEnemyId);
        Assert.Equal("attack_debuff", enemy.IntentType);
        Assert.Equal(7, enemy.IntentDamage);
        Assert.Equal(2, enemy.IntentHits);
        Assert.Contains("weak", enemy.IntentEffects ?? Array.Empty<string>());
        Assert.Equal("Gnaw", enemy.MoveName);
        Assert.Equal("Deal 7 **damage**. Gain 4 **Block**.", enemy.MoveDescription);
        Assert.Contains(enemy.MoveGlossary ?? Array.Empty<GlossaryAnchor>(), anchor => anchor.GlossaryId == "damage");
        Assert.Contains(enemy.MoveGlossary ?? Array.Empty<GlossaryAnchor>(), anchor => anchor.GlossaryId == "block");
        Assert.Contains("beast", enemy.Traits ?? Array.Empty<string>());
        Assert.Contains("damage", enemy.Keywords ?? Array.Empty<string>());
        Assert.Contains("vulnerable", enemy.Keywords ?? Array.Empty<string>());
        Assert.Contains("Vulnerable", enemy.Powers?.Select(power => power.Name) ?? Array.Empty<string>());

        var runStateSnapshot = exported.Snapshot.RunState;
        Assert.NotNull(runStateSnapshot);
        Assert.Equal(1, runStateSnapshot.Act);
        Assert.Equal(1, runStateSnapshot.Floor);
        Assert.Equal("FakeCombatRoom", runStateSnapshot.CurrentRoomType);
        Assert.Equal("1,1", runStateSnapshot.Map?.CurrentCoord);
        Assert.Contains("Elite@2,2", runStateSnapshot.Map?.ReachableNodes ?? Array.Empty<string>());

        var pileExport = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(exported.Snapshot.Metadata["pile_export"]);
        var drawPileExport = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(pileExport["draw_pile"]);
        Assert.Equal(1, drawPileExport["expected_count"]);
        Assert.Equal(1, drawPileExport["exported_count"]);
        Assert.Equal(false, drawPileExport["degraded"]);
        var discardPileExport = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(pileExport["discard_pile"]);
        Assert.Equal(1, discardPileExport["expected_count"]);
        Assert.Equal(1, discardPileExport["exported_count"]);
        Assert.Equal(false, discardPileExport["degraded"]);
        var enemyExport = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(exported.Snapshot.Metadata["enemy_export"]);
        Assert.Equal(false, enemyExport["degraded"]);
    }

    [Fact]
    public void BuildCombatWindow_KeepsEnemyBaseStateWhenMoveDescriptionIsMissing()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(new FakeScreenTracker());
        var runState = new FakeRunState(
            new[] { new FakeEnemy("enemy-1", true, intent: "Attack", intentDamage: 6) });

        var window = InvokeBuildCombatWindow(reader, runNode, runState);
        var exported = new CombatWindowExtractor().Export(window, new BridgeSessionState(new BridgeOptions()));
        var enemy = Assert.Single(exported.Snapshot.Enemies);

        Assert.Equal("Louse", enemy.Name);
        Assert.Equal("attack_6", enemy.Intent);
        Assert.Equal("attack", enemy.IntentType);
        Assert.Equal(6, enemy.IntentDamage);
        Assert.Null(enemy.MoveName);
        Assert.Null(enemy.MoveDescription);
        Assert.Empty(enemy.MoveGlossary ?? Array.Empty<GlossaryAnchor>());
        var enemyExport = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(exported.Snapshot.Metadata["enemy_export"]);
        Assert.Equal(true, enemyExport["degraded"]);
        Assert.True((int)enemyExport["entry_count"] >= 1);
    }

    [Fact]
    public void BuildCombatWindow_NormalizesGenericIntentLabelsIntoStableTypeAndSuppressesGenericMoveName()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(new FakeScreenTracker());
        var runState = new FakeRunState(
            new[]
            {
                new FakeEnemy("enemy-1", true, intent: "策略", intentDamage: 0, intentType: "策略")
                {
                    CurrentMove = new FakeEnemyMove("策略")
                    {
                        Description = "这个敌人将要对你施加一个负面效果。",
                    },
                },
            });

        var window = InvokeBuildCombatWindow(reader, runNode, runState);
        var exported = new CombatWindowExtractor().Export(window, new BridgeSessionState(new BridgeOptions()));
        var enemy = Assert.Single(exported.Snapshot.Enemies);

        Assert.Equal("debuff", enemy.Intent);
        Assert.Equal("debuff", enemy.IntentType);
        Assert.Contains("debuff", enemy.IntentEffects ?? Array.Empty<string>());
        Assert.Null(enemy.MoveName);
        Assert.Equal("这个敌人将要对你施加一个负面效果。", enemy.MoveDescription);
    }

    [Fact]
    public void BuildCombatWindow_DegradesMissingPileWithoutFailingSnapshot()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(new FakeScreenTracker());
        var runState = new FakeRunState(
            new[] { new FakeEnemy("enemy-1", true) },
            hand: new[]
            {
                new FakeCard("Strike")
                {
                    CardId = "strike_red",
                    Description = "Deal {Damage:diff()} [gold]damage[/gold].",
                    RenderedDescription = "Deal 6 damage.",
                    Damage = 6,
                    TargetType = "AnyEnemy",
                    CardType = "Attack",
                    Keywords = new[] { "damage" },
                },
            },
            drawPileObject: new FakeBrokenPile(),
            discardPile: new[]
            {
                new FakeCard("Defend")
                {
                    CardId = "defend_red",
                    Description = "Gain {Block:diff()} [gold]Block[/gold].",
                    Block = 5,
                    TargetType = "Self",
                    CardType = "Skill",
                    Keywords = new[] { "block" },
                },
            });

        var window = InvokeBuildCombatWindow(reader, runNode, runState);
        var exported = new CombatWindowExtractor().Export(window, new BridgeSessionState(new BridgeOptions()));
        var player = Assert.IsType<PlayerState>(exported.Snapshot.Player);

        Assert.Empty(player.DrawPileCards ?? Array.Empty<CardView>());
        Assert.Equal(0, player.DrawPile);
        Assert.Single(player.DiscardPileCards ?? Array.Empty<CardView>());

        var pileExport = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(exported.Snapshot.Metadata["pile_export"]);
        var drawPileExport = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(pileExport["draw_pile"]);
        Assert.Equal(true, drawPileExport["degraded"]);
        Assert.Equal("cards_collection_missing", drawPileExport["fallback_reason"]);
        Assert.Equal(0, drawPileExport["expected_count"]);
        Assert.Equal(0, drawPileExport["exported_count"]);
    }

    [Fact]
    public void BuildCombatWindow_RendersTemplateFallbackAndGlossaryWithoutRuntimeRenderedText()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(new FakeScreenTracker());
        var runState = new FakeRunState(
            new[] { new FakeEnemy("enemy-1", true) },
            hand: new[]
            {
                new FakeCard("Defend")
                {
                    CardId = "defend_red",
                    Description = "Gain {Block:diff()} [gold]Block[/gold].",
                    Block = 5,
                    TargetType = "Self",
                    CardType = "Skill",
                    Keywords = new[] { "block" },
                },
            });

        var window = InvokeBuildCombatWindow(reader, runNode, runState);
        var exported = new CombatWindowExtractor().Export(window, new BridgeSessionState(new BridgeOptions()));
        var player = exported.Snapshot.Player;
        Assert.NotNull(player);
        var card = Assert.Single(player.Hand);

        Assert.Equal("Gain 5 **Block**.", card.Description);
        Assert.Contains(card.Glossary ?? Array.Empty<GlossaryAnchor>(), anchor => anchor.GlossaryId == "block");
    }

    [Fact]
    public void BuildCombatWindow_UsesDynamicVarsWhenDirectDamageMemberIsMissing()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(new FakeScreenTracker());
        var runState = new FakeRunState(
            new[] { new FakeEnemy("enemy-1", true) },
            hand: new[]
            {
                new FakeCard("Strike")
                {
                    CardId = "strike_red",
                    Description = "Deal {Damage:diff()} [gold]damage[/gold].",
                    Damage = null,
                    DynamicVars = new FakeDynamicVars(damage: 6),
                    TargetType = "AnyEnemy",
                    CardType = "Attack",
                    Rarity = "Starter",
                    Traits = new[] { "starter" },
                    Keywords = new[] { "damage" },
                },
            });

        var window = InvokeBuildCombatWindow(reader, runNode, runState);
        var exported = new CombatWindowExtractor().Export(window, new BridgeSessionState(new BridgeOptions()));
        var player = exported.Snapshot.Player;
        Assert.NotNull(player);
        var card = Assert.Single(player.Hand);

        Assert.Equal("Deal 6 **damage**.", card.Description);
        Assert.Contains(card.Glossary ?? Array.Empty<GlossaryAnchor>(), anchor => anchor.GlossaryId == "damage");
    }

    [Fact]
    public void BuildCombatWindow_KeepsTemplateFallbackWhenDynamicValueCannotBeResolved()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(new FakeScreenTracker());
        var runState = new FakeRunState(
            new[] { new FakeEnemy("enemy-1", true) },
            hand: new[]
            {
                new FakeCard("Battle Trance")
                {
                    CardId = "battle_trance",
                    Description = "Draw {Draw:diff()} cards.",
                    TargetType = "Self",
                    CardType = "Skill",
                    Keywords = new[] { "draw" },
                },
            });

        var window = InvokeBuildCombatWindow(reader, runNode, runState);
        var exported = new CombatWindowExtractor().Export(window, new BridgeSessionState(new BridgeOptions()));
        var player = exported.Snapshot.Player;
        Assert.NotNull(player);
        var card = Assert.Single(player.Hand);

        Assert.Equal("Draw {Draw:diff()} cards.", card.Description);
        Assert.Contains(card.Glossary ?? Array.Empty<GlossaryAnchor>(), anchor => anchor.GlossaryId == "draw");
    }

    [Fact]
    public void BuildRewardWindow_ExportsRewardChoicesAndDiagnostics()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(new FakeScreenTracker(
            rewardScreen: new FakeRewardScreen(
                isComplete: false,
                visible: true,
                new FakeRewardButton(new FakeReward("Inflame")),
                new FakeRewardButton(new FakeReward("Pommel Strike")))));
        var runState = new FakeRunState(Array.Empty<FakeEnemy>());

        var window = InvokeBuildRewardWindow(reader, runNode, runState);
        var exported = new RewardWindowExtractor().Export(window, new BridgeSessionState(new BridgeOptions()));

        Assert.Equal(DecisionPhase.Reward, window.Phase);
        Assert.Equal(new[] { "Inflame", "Pommel Strike" }, window.Rewards);
        Assert.Contains(window.Actions, action => action.Type == "choose_reward");
        Assert.Contains(window.Actions, action => action.Type == "skip_reward");
        Assert.Equal(DecisionPhase.Reward, exported.Snapshot.Phase);
        Assert.Equal("reward_choice", exported.Snapshot.Metadata["window_kind"]);
        Assert.True(exported.Snapshot.Metadata.ContainsKey("phase_detection"));
    }

    [Fact]
    public void DetectPhase_ReturnsRewardWhenCardRewardSelectionOverlayIsVisible()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(
            new FakeScreenTracker(),
            new FakeGlobalUi(new FakeOverlayStack(new FakeCardRewardSelectionScreen(
                new FakeCardChoice(new FakeCard("Strike")),
                new FakeCardChoice(new FakeCard("Defend"))))));
        var runState = new FakeRunState(Array.Empty<FakeEnemy>());

        var phase = InvokeDetectPhase(reader, runNode, runState);

        Assert.Equal(DecisionPhase.Reward, phase);
    }

    [Fact]
    public void BuildRewardWindow_ExportsCardRewardSelectionAsRewardWindow()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(
            new FakeScreenTracker(),
            new FakeGlobalUi(new FakeOverlayStack(new FakeCardRewardSelectionScreen(
                new FakeCardChoice(new FakeCard("Strike")),
                new FakeCardChoice(new FakeCard("Defend"))))));
        var runState = new FakeRunState(Array.Empty<FakeEnemy>());

        var window = InvokeBuildRewardWindow(reader, runNode, runState);
        var exported = new RewardWindowExtractor().Export(window, new BridgeSessionState(new BridgeOptions()));

        Assert.Equal(DecisionPhase.Reward, window.Phase);
        Assert.Equal(new[] { "Strike", "Defend" }, window.Rewards);
        Assert.Contains(window.Actions, action => action.Type == "choose_reward");
        Assert.Contains(window.Actions, action => action.Type == "skip_reward");
        Assert.Equal("reward_card_selection", exported.Snapshot.Metadata["window_kind"]);
        Assert.Equal("card_reward_selection", exported.Snapshot.Metadata["reward_subphase"]);
        Assert.True(exported.Snapshot.Metadata.ContainsKey("overlay_top_type"));
    }

    [Fact]
    public void BuildRewardWindow_DoesNotExportSkipRewardWhenCardSelectionCannotSkip()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(
            new FakeScreenTracker(),
            new FakeGlobalUi(new FakeOverlayStack(new FakeCardRewardSelectionScreenNoSkip(
                new FakeCardChoice(new FakeCard("Strike"))))));
        var runState = new FakeRunState(Array.Empty<FakeEnemy>());

        var window = InvokeBuildRewardWindow(reader, runNode, runState);
        var exported = new RewardWindowExtractor().Export(window, new BridgeSessionState(new BridgeOptions()));

        Assert.DoesNotContain(window.Actions, action => action.Type == "skip_reward");
        Assert.Equal(false, exported.Snapshot.Metadata["reward_skip_available"]);
        Assert.Equal("skip_hook_not_found", exported.Snapshot.Metadata["reward_skip_reason"]);
    }

    [Fact]
    public void BuildRewardWindow_ExportsAdvanceActionWhenRewardScreenNeedsContinue()
    {
        var reader = CreateReader();
        var rewardScreen = new FakeRewardScreen(isComplete: true, visible: true, advanceButton: new FakeAdvanceButton("前进"));
        var runNode = new FakeRunNode(new FakeScreenTracker(rewardScreen: rewardScreen));
        var runState = new FakeRunState(Array.Empty<FakeEnemy>());

        var window = InvokeBuildRewardWindow(reader, runNode, runState);
        var exported = new RewardWindowExtractor().Export(window, new BridgeSessionState(new BridgeOptions()));

        Assert.Empty(window.Rewards);
        Assert.Contains(window.Actions, action => action.Type == "advance_reward");
        Assert.Equal("reward_advance", exported.Snapshot.Metadata["window_kind"]);
        Assert.Equal("reward_advance", exported.Snapshot.Metadata["reward_subphase"]);
        Assert.Equal(true, exported.Snapshot.Metadata["reward_advance_available"]);
    }

    [Fact]
    public void ExecuteAdvanceReward_PrefersRewardScreenProceedHandler()
    {
        var reader = CreateReader();
        var button = new FakeAdvanceButton("前进");
        var rewardScreen = new FakeRewardScreen(isComplete: true, visible: true, advanceButton: button);
        var runNode = new FakeRunNode(new FakeScreenTracker(rewardScreen: rewardScreen));
        var action = new LegalAction(
            "act-advance",
            "advance_reward",
            "前进",
            new Dictionary<string, object?> { ["button_label"] = "前进" },
            Array.Empty<string>(),
            new Dictionary<string, object?>());
        var request = new ActionRequest("dec-1", "act-advance", null, action.Params, Guid.NewGuid().ToString("N"));

        var result = InvokeExecuteAdvanceReward(reader, runNode, request, action);
        var accepted = (bool)result.GetType().GetProperty("Accepted")!.GetValue(result)!;
        var metadata = (IReadOnlyDictionary<string, object?>)result.GetType().GetProperty("Metadata")!.GetValue(result)!;

        Assert.True(accepted);
        Assert.True(rewardScreen.ProceedPressed);
        Assert.False(button.Clicked);
        Assert.Equal("advance_reward", metadata["action_type"]);
        Assert.Equal("map", metadata["next_window_expected"]);
        Assert.Equal("reward_screen.OnProceedButtonPressed", metadata["runtime_handler"]);
    }

    [Fact]
    public void BuildRewardWindow_UsesTransitionWindowWhenRewardIsCompleteButAdvanceButtonIsMissing()
    {
        var reader = CreateReader();
        var rewardScreen = new FakeRewardScreen(isComplete: true, visible: true);
        var runNode = new FakeRunNode(new FakeScreenTracker(rewardScreen: rewardScreen));
        var runState = new FakeRunState(Array.Empty<FakeEnemy>());

        var window = InvokeBuildRewardWindow(reader, runNode, runState);
        var exported = new RewardWindowExtractor().Export(window, new BridgeSessionState(new BridgeOptions()));

        Assert.Empty(window.Actions);
        Assert.Equal("reward_transition", exported.Snapshot.Metadata["window_kind"]);
        Assert.Equal("reward_transition", exported.Snapshot.Metadata["reward_subphase"]);
    }

    [Fact]
    public void BuildMapWindow_ExportsReadyMetadataAndChooseMapNodeActions()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(new FakeScreenTracker(mapScreenVisible: true));
        var currentPoint = new FakeMapPoint("Current", new FakeMapCoord(0, 0),
            new FakeMapPoint("Monster", new FakeMapCoord(1, 2)),
            new FakeMapPoint("Elite", new FakeMapCoord(2, 2)));
        var runState = new FakeRunState(Array.Empty<FakeEnemy>(), currentRoom: new FakeMapRoom(), currentMapPoint: currentPoint);

        var window = InvokeBuildMapWindow(reader, runNode, runState);
        var exported = new MapWindowExtractor().Export(window, new BridgeSessionState(new BridgeOptions()));

        Assert.Equal(DecisionPhase.Map, window.Phase);
        Assert.Equal(new[] { "Monster@1,2", "Elite@2,2" }, window.MapNodes);
        Assert.Equal("map_ready", exported.Snapshot.Metadata["window_kind"]);
        Assert.Equal(true, exported.Snapshot.Metadata["map_ready"]);
        Assert.Equal("current_map_point", exported.Snapshot.Metadata["map_node_source"]);
        Assert.Contains(window.Actions, action => action.Type == "choose_map_node");
    }

    [Fact]
    public void BuildMapWindow_UsesStartingPointFallbackAndTransitionMetadata()
    {
        var reader = CreateReader();
        var runNode = new FakeRunNode(new FakeScreenTracker(mapScreenVisible: true));
        var startingPoint = new FakeMapPoint("Start", new FakeMapCoord(0, 0),
            new FakeMapPoint("Monster", new FakeMapCoord(3, 1)));
        var runState = new FakeRunState(
            Array.Empty<FakeEnemy>(),
            currentRoom: new FakeMapRoom(),
            currentMapPoint: new FakeMapPoint("Current", new FakeMapCoord(0, 0)),
            map: new FakeMap(startingPoint));

        var fallbackWindow = InvokeBuildMapWindow(reader, runNode, runState);
        var fallbackExported = new MapWindowExtractor().Export(fallbackWindow, new BridgeSessionState(new BridgeOptions()));

        Assert.Equal(new[] { "Monster@3,1" }, fallbackWindow.MapNodes);
        Assert.Equal("starting_map_point_fallback", fallbackExported.Snapshot.Metadata["map_node_source"]);
        Assert.Equal("map_ready", fallbackExported.Snapshot.Metadata["window_kind"]);

        var emptyRunState = new FakeRunState(
            Array.Empty<FakeEnemy>(),
            currentRoom: new FakeMapRoom(),
            currentMapPoint: new FakeMapPoint("Current", new FakeMapCoord(0, 0)),
            map: new FakeMap(new FakeMapPoint("Start", new FakeMapCoord(0, 0))));
        var transitionWindow = InvokeBuildMapWindow(reader, runNode, emptyRunState);
        var transitionExported = new MapWindowExtractor().Export(transitionWindow, new BridgeSessionState(new BridgeOptions()));

        Assert.Empty(transitionWindow.Actions);
        Assert.Equal("map_transition", transitionExported.Snapshot.Metadata["window_kind"]);
        Assert.Equal(true, transitionExported.Snapshot.Metadata["no_reachable_nodes"]);
        Assert.Equal("no_reachable_nodes", transitionExported.Snapshot.Metadata["map_node_source"]);
    }

    private static Sts2RuntimeReflectionReader CreateReader()
    {
        return new Sts2RuntimeReflectionReader(new BridgeOptions(), new InstallationProbeResult(true, null, null, null, null));
    }

    private static string InvokeDetectPhase(Sts2RuntimeReflectionReader reader, object runNode, object runState)
    {
        var method = typeof(Sts2RuntimeReflectionReader).GetMethod("DetectPhase", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (string)method.Invoke(reader, new[] { runNode, runState })!;
    }

    private static RuntimeWindowContext InvokeBuildCombatWindow(Sts2RuntimeReflectionReader reader, object runNode, object runState)
    {
        var method = typeof(Sts2RuntimeReflectionReader).GetMethod("BuildCombatWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (RuntimeWindowContext)method.Invoke(reader, new[] { runNode, runState })!;
    }

    private static RuntimeWindowContext InvokeBuildRewardWindow(Sts2RuntimeReflectionReader reader, object runNode, object runState)
    {
        var method = typeof(Sts2RuntimeReflectionReader).GetMethod("BuildRewardWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (RuntimeWindowContext)method.Invoke(reader, new[] { runNode, runState })!;
    }

    private static RuntimeWindowContext InvokeBuildMapWindow(Sts2RuntimeReflectionReader reader, object runNode, object runState)
    {
        var method = typeof(Sts2RuntimeReflectionReader).GetMethod("BuildMapWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (RuntimeWindowContext)method.Invoke(reader, new[] { runNode, runState })!;
    }

    private static object InvokeExecuteAdvanceReward(Sts2RuntimeReflectionReader reader, object runNode, ActionRequest request, LegalAction action)
    {
        var method = typeof(Sts2RuntimeReflectionReader).GetMethod("ExecuteAdvanceReward", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return method.Invoke(reader, new object[] { runNode, request, action })!;
    }

    private sealed class FakeRunNode(FakeScreenTracker screenStateTracker, FakeGlobalUi? globalUi = null)
    {
        public FakeScreenTracker ScreenStateTracker { get; } = screenStateTracker;
        public FakeGlobalUi GlobalUi { get; } = globalUi ?? new FakeGlobalUi(new FakeOverlayStack(null));
    }

    private sealed class FakeGlobalUi(FakeOverlayStack overlays)
    {
        public FakeOverlayStack Overlays { get; } = overlays;
    }

    private sealed class FakeOverlayStack(object? screen)
    {
        private readonly object? _screen = screen;

        public object? Peek()
        {
            return _screen;
        }
    }

    private sealed class FakeScreenTracker(FakeRewardScreen? rewardScreen = null, bool mapScreenVisible = false, bool rewardScreenVisible = false)
    {
        public FakeRewardScreen? _connectedRewardsScreen = rewardScreen;
        public bool _mapScreenVisible = mapScreenVisible;
        public bool _rewardScreenVisible = rewardScreenVisible;
    }

    private sealed class FakeRewardScreen
    {
        public FakeRewardScreen(bool isComplete, bool visible, params FakeRewardButton[] buttons)
        {
            IsComplete = isComplete;
            Visible = visible;
            _rewardButtons = new List<FakeRewardButton>(buttons);
        }

        public FakeRewardScreen(bool isComplete, bool visible, FakeAdvanceButton advanceButton, params FakeRewardButton[] buttons)
            : this(isComplete, visible, buttons)
        {
            AdvanceButton = advanceButton;
        }

        public bool IsComplete { get; }
        public bool Visible { get; }
        public List<FakeRewardButton> _rewardButtons { get; }
        public FakeAdvanceButton? AdvanceButton { get; }
        public bool ProceedPressed { get; private set; }

        public IEnumerable<object> GetChildren()
        {
            if (AdvanceButton is not null)
            {
                yield return AdvanceButton;
            }
        }

        public void OnProceedButtonPressed(FakeAdvanceButton _)
        {
            ProceedPressed = true;
        }
    }

    private sealed class FakeRewardButton(FakeReward reward)
    {
        public FakeReward Reward { get; } = reward;
    }

    private sealed class FakeReward(string description)
    {
        public string Description { get; } = description;
    }

    private sealed class FakeAdvanceButton(string text)
    {
        public string Text { get; } = text;
        public bool Visible { get; } = true;
        public bool IsEnabled { get; } = true;
        public bool Clicked { get; private set; }

        public void Click()
        {
            Clicked = true;
        }
    }

    private sealed class FakeCard(string title)
    {
        public string Title { get; } = title;
        public string CardId { get; init; } = title.ToLowerInvariant();
        public string Name => Title;
        public string Description { get; init; } = string.Empty;
        public string? RenderedDescription { get; init; }
        public bool IsUpgraded { get; init; }
        public string TargetType { get; init; } = "AnyEnemy";
        public string CardType { get; init; } = "Attack";
        public string Rarity { get; init; } = "Common";
        public int CanonicalEnergyCost { get; init; } = 1;
        public int CurrentStarCost { get; init; } = 1;
        public int? Damage { get; init; } = 6;
        public int? Block { get; init; } = 5;
        public bool IsPlayable { get; init; } = true;
        public IReadOnlyList<string> Traits { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
        public FakeDynamicVars? DynamicVars { get; init; }
    }

    private sealed class FakeDynamicVars(int? damage = null, int? block = null, int? cards = null)
    {
        public int? Damage { get; } = damage;
        public int? Block { get; } = block;
        public int? Cards { get; } = cards;
    }

    private sealed class FakeCardChoice(FakeCard card)
    {
        public FakeCard Card { get; } = card;
    }

    private sealed class FakeCardRewardSelectionScreen(params FakeCardChoice[] choices)
    {
        public List<FakeCardChoice> Cards { get; } = new(choices);

        public void SelectCard(FakeCardChoice choice)
        {
        }

        public void Skip()
        {
        }
    }

    private sealed class FakeCardRewardSelectionScreenNoSkip(params FakeCardChoice[] choices)
    {
        public List<FakeCardChoice> Cards { get; } = new(choices);

        public void SelectCard(FakeCardChoice choice)
        {
        }
    }

    private sealed class FakeRunState(
        IEnumerable<FakeEnemy> enemies,
        object? currentRoom = null,
        FakeMapPoint? currentMapPoint = null,
        FakeMap? map = null,
        IReadOnlyList<FakeCard>? hand = null,
        IReadOnlyList<FakeCard>? drawPile = null,
        IReadOnlyList<FakeCard>? discardPile = null,
        IReadOnlyList<FakeCard>? exhaustPile = null,
        object? drawPileObject = null,
        object? discardPileObject = null,
        object? exhaustPileObject = null)
    {
        public object CurrentRoom { get; } = currentRoom ?? new FakeCombatRoom();
        public object CurrentLocation { get; } = "Act1";
        public int ActFloor { get; } = 1;
        public int CurrentActIndex { get; } = 0;
        public int AscensionLevel { get; } = 0;
        public List<FakePlayer> Players { get; } = new()
        {
            new FakePlayer(
                enemies.ToArray(),
                hand?.ToArray() ?? Array.Empty<FakeCard>(),
                drawPileObject ?? new FakePile(drawPile?.ToArray() ?? Array.Empty<FakeCard>()),
                discardPileObject ?? new FakePile(discardPile?.ToArray() ?? Array.Empty<FakeCard>()),
                exhaustPileObject ?? new FakePile(exhaustPile?.ToArray() ?? Array.Empty<FakeCard>())),
        };
        public FakeMapPoint? CurrentMapPoint { get; } = currentMapPoint;
        public FakeMap? Map { get; } = map;
    }

    private sealed class FakeCombatRoom;
    private sealed class FakeMapRoom;

    private sealed class FakeMap(FakeMapPoint startingMapPoint)
    {
        public FakeMapPoint StartingMapPoint { get; } = startingMapPoint;
    }

    private sealed class FakeMapPoint(string pointType, FakeMapCoord coord, params FakeMapPoint[] children)
    {
        public string PointType { get; } = pointType;
        public FakeMapCoord coord { get; } = coord;
        public List<FakeMapPoint> Children { get; } = new(children);
    }

    private sealed class FakeMapCoord(int col, int row)
    {
        public int col { get; } = col;
        public int row { get; } = row;
    }

    private sealed class FakePlayer(
        FakeEnemy[] enemies,
        FakeCard[] handCards,
        object drawPile,
        object discardPile,
        object exhaustPile)
    {
        public int Gold { get; } = 99;
        public FakeCreature Creature { get; } = new(enemies);
        public FakePlayerCombatState PlayerCombatState { get; } = new(handCards, drawPile, discardPile, exhaustPile);
        public List<object> Relics { get; } = new();
        public List<object> PotionSlots { get; } = new();
    }

    private sealed class FakeCreature(FakeEnemy[] enemies)
    {
        public int CurrentHp { get; } = 80;
        public int MaxHp { get; } = 80;
        public int Block { get; } = 0;
        public FakeCombatState CombatState { get; } = new(enemies);
        public List<FakePower> Powers { get; } = new() { new FakePower("metallicize", "Metallicize", 3, "Gain 3 Block at end of turn.") };
    }

    private sealed class FakeCombatState(FakeEnemy[] enemies)
    {
        public int RoundNumber { get; } = 3;
        public string CurrentSide { get; } = "Player";
        public List<FakeEnemy> Enemies { get; } = new(enemies);
    }

    private sealed class FakeEnemy(string combatId, bool isAlive, string intent = "Attack", int intentDamage = 6, int intentHits = 1, string? intentType = null)
    {
        public string CombatId { get; } = combatId;
        public string Name { get; } = "Louse";
        public string EnemyId { get; } = "louse";
        public int CurrentHp { get; } = isAlive ? 10 : 0;
        public int MaxHp { get; } = 10;
        public int Block { get; } = 0;
        public string Intent { get; } = intent;
        public string? IntentType { get; } = intentType;
        public int IntentDamage { get; } = intentDamage;
        public int IntentHits { get; } = intentHits;
        public bool IsAlive { get; } = isAlive;
        public List<FakePower> Powers { get; } = new() { new FakePower("vulnerable", "Vulnerable", 1, "Receive more attack damage.") };
        public FakeEnemyMove? CurrentMove { get; init; }
        public IReadOnlyList<string> Traits { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    }

    private sealed class FakeEnemyMove(string name)
    {
        public string Name { get; } = name;
        public string Description { get; init; } = string.Empty;
        public string? RenderedDescription { get; init; }
        public int? Damage { get; init; }
        public int? Block { get; init; }
        public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    }

    private sealed class FakePlayerCombatState(
        FakeCard[] handCards,
        object drawPile,
        object discardPile,
        object exhaustPile)
    {
        public int Energy { get; } = 3;
        public int Stars { get; } = 0;
        public int MaxEnergy { get; } = 3;
        public FakePile Hand { get; } = new(handCards);
        public object DrawPile { get; } = drawPile;
        public object DiscardPile { get; } = discardPile;
        public object ExhaustPile { get; } = exhaustPile;
    }

    private sealed class FakePile(IEnumerable<object> cards)
    {
        public List<object> Cards { get; } = new(cards);
    }

    private sealed class FakeBrokenPile;

    private sealed class FakePower(string id, string name, int amount, string description)
    {
        public string PowerId { get; } = id;
        public string Name { get; } = name;
        public int Amount { get; } = amount;
        public string Description { get; } = description;
        public string RenderedDescription => description;
    }
}
