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

    private sealed class FakeRewardScreen(bool isComplete, bool visible, params FakeRewardButton[] buttons)
    {
        public bool IsComplete { get; } = isComplete;
        public bool Visible { get; } = visible;
        public List<FakeRewardButton> _rewardButtons { get; } = new(buttons);
    }

    private sealed class FakeRewardButton(FakeReward reward)
    {
        public FakeReward Reward { get; } = reward;
    }

    private sealed class FakeReward(string description)
    {
        public string Description { get; } = description;
    }

    private sealed class FakeCard(string title)
    {
        public string Title { get; } = title;
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

    private sealed class FakeRunState(IEnumerable<FakeEnemy> enemies)
    {
        public object CurrentRoom { get; } = new FakeCombatRoom();
        public List<FakePlayer> Players { get; } = new() { new FakePlayer(enemies.ToArray()) };
    }

    private sealed class FakeCombatRoom;

    private sealed class FakePlayer(FakeEnemy[] enemies)
    {
        public int Gold { get; } = 99;
        public FakeCreature Creature { get; } = new(enemies);
        public FakePlayerCombatState PlayerCombatState { get; } = new();
        public List<object> Relics { get; } = new();
        public List<object> PotionSlots { get; } = new();
    }

    private sealed class FakeCreature(FakeEnemy[] enemies)
    {
        public int CurrentHp { get; } = 80;
        public int MaxHp { get; } = 80;
        public int Block { get; } = 0;
        public FakeCombatState CombatState { get; } = new(enemies);
    }

    private sealed class FakeCombatState(FakeEnemy[] enemies)
    {
        public int RoundNumber { get; } = 3;
        public string CurrentSide { get; } = "Player";
        public List<FakeEnemy> Enemies { get; } = new(enemies);
    }

    private sealed class FakeEnemy(string combatId, bool isAlive)
    {
        public string CombatId { get; } = combatId;
        public string Name { get; } = "Louse";
        public int CurrentHp { get; } = isAlive ? 10 : 0;
        public int MaxHp { get; } = 10;
        public int Block { get; } = 0;
        public string Intent { get; } = "Attack";
        public bool IsAlive { get; } = isAlive;
    }

    private sealed class FakePlayerCombatState
    {
        public int Energy { get; } = 3;
        public int Stars { get; } = 0;
        public int MaxEnergy { get; } = 3;
        public FakePile Hand { get; } = new();
        public FakePile DrawPile { get; } = new();
        public FakePile DiscardPile { get; } = new();
        public FakePile ExhaustPile { get; } = new();
    }

    private sealed class FakePile
    {
        public List<object> Cards { get; } = new();
    }
}
