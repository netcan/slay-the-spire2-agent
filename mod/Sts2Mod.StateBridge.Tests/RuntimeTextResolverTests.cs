using Sts2Mod.StateBridge.Configuration;
using Sts2Mod.StateBridge.Contracts;
using Sts2Mod.StateBridge.Core;
using Sts2Mod.StateBridge.Extraction;
using Sts2Mod.StateBridge.Providers;
using Xunit;

namespace Sts2Mod.StateBridge.Tests;

public sealed class RuntimeTextResolverTests
{
    [Fact]
    public void Resolve_UsesFormattedTextForLocString()
    {
        var collector = new TextDiagnosticsCollector();

        var result = RuntimeTextResolver.Resolve(new FakeLocString("Burning Blood", "burning_blood"), "player.relics[0]", collector, "Name");

        Assert.Equal("Burning Blood", result.Text);
        Assert.Equal("resolved", result.Status);
        Assert.Equal("loc_string.formatted", result.Source);

        var metadata = collector.ToMetadata();
        Assert.Equal(1, metadata["resolved"]);
        Assert.Equal(0, metadata["fallback"]);
        Assert.Equal(0, metadata["unresolved"]);
    }

    [Fact]
    public void Resolve_FallsBackThroughNestedRelicMember()
    {
        var collector = new TextDiagnosticsCollector();
        var wrapper = new InventoryWrapper(new RelicModel(new FakeLocString("Lantern", "lantern")));

        var result = RuntimeTextResolver.Resolve(wrapper, "player.relics[0]", collector, "Potion", "Relic", "Name");

        Assert.Equal("Lantern", result.Text);
        Assert.Equal("resolved", result.Status);
        Assert.Contains("Relic", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RecordsFallbackDiagnosticsWhenTextIsUnavailable()
    {
        var collector = new TextDiagnosticsCollector();

        var result = RuntimeTextResolver.Resolve(new UnsupportedTextValue(), "rewards[0]", collector, "Description", "Name");

        Assert.Null(result.Text);
        Assert.Equal("unresolved", result.Status);

        var metadata = collector.ToMetadata();
        Assert.Equal(1, metadata["unresolved"]);
        var entries = Assert.IsAssignableFrom<IReadOnlyCollection<IReadOnlyDictionary<string, object?>>>(metadata["entries"]);
        var entry = Assert.Single(entries);
        Assert.Equal("rewards[0]", entry["path"]);
        Assert.Equal("unresolved", entry["status"]);
    }

    [Fact]
    public void Export_IgnoresDiagnosticsMetadataInFingerprint()
    {
        var extractor = new CombatWindowExtractor();
        var session = new BridgeSessionState(new BridgeOptions());

        var first = CreateContext(
            windowDiagnostics: new Dictionary<string, object?>
            {
                ["resolved"] = 3,
                ["fallback"] = 1,
                ["unresolved"] = 0,
            },
            actionDiagnostics: new Dictionary<string, object?>
            {
                ["path"] = "actions.play_card[0].label",
                ["status"] = "fallback",
            });
        extractor.Export(first, session);

        var second = CreateContext(
            windowDiagnostics: new Dictionary<string, object?>
            {
                ["resolved"] = 9,
                ["fallback"] = 0,
                ["unresolved"] = 2,
            },
            actionDiagnostics: new Dictionary<string, object?>
            {
                ["path"] = "actions.play_card[0].label",
                ["status"] = "resolved",
            });
        extractor.Export(second, session);

        Assert.Equal(1, session.StateVersion);
    }

    private static RuntimeWindowContext CreateContext(
        IReadOnlyDictionary<string, object?> windowDiagnostics,
        IReadOnlyDictionary<string, object?> actionDiagnostics)
    {
        return new RuntimeWindowContext(
            DecisionPhase.Combat,
            new RuntimePlayerState(
                80,
                80,
                0,
                3,
                99,
                new[] { new RuntimeCard("card_0", "Strike", 1, true) },
                10,
                0,
                0,
                new[] { "Burning Blood" },
                Array.Empty<string>()),
            new[] { new RuntimeEnemyState("enemy_0", "Slime", 12, 12, 0, "Attack", true) },
            Array.Empty<string>(),
            Array.Empty<string>(),
            false,
            new Dictionary<string, object?>
            {
                ["source"] = "sts2_runtime",
                ["window_kind"] = "player_turn",
                ["text_diagnostics"] = windowDiagnostics,
            },
            new[]
            {
                new RuntimeActionDefinition(
                    "play_card",
                    "Play Strike",
                    new Dictionary<string, object?>
                    {
                        ["card_id"] = "card_0",
                        ["card_name"] = "Strike",
                    },
                    Array.Empty<string>(),
                    new Dictionary<string, object?>
                    {
                        ["playable"] = true,
                        ["diagnostics"] = actionDiagnostics,
                    }),
            });
    }

    private sealed class InventoryWrapper(RelicModel relic)
    {
        public RelicModel Relic { get; } = relic;
    }

    private sealed class RelicModel(FakeLocString name)
    {
        public FakeLocString Name { get; } = name;
    }

    private sealed class FakeLocString(string formattedText, string locEntryKey)
    {
        public string LocEntryKey { get; } = locEntryKey;

        public string GetFormattedText() => formattedText;

        public string GetRawText() => locEntryKey;
    }

    private sealed class UnsupportedTextValue;
}
