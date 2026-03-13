using Sts2Mod.StateBridge.Configuration;
using Sts2Mod.StateBridge.Contracts;

namespace Sts2Mod.StateBridge.Core;

public sealed class BridgeSessionState
{
    private readonly BridgeOptions _options;
    private int _stateVersion;
    private string _phase;

    public BridgeSessionState(BridgeOptions options)
    {
        _options = options;
        SessionId = BridgeIds.CreateSessionId("sts2-mod-state-bridge");
        _phase = DecisionPhase.Combat;
        DecisionId = BridgeIds.CreateDecisionId(SessionId, _stateVersion, _phase);
    }

    public string SessionId { get; }

    public int StateVersion => _stateVersion;

    public string DecisionId { get; private set; }

    public CompatibilityMetadata Compatibility => new(
        _options.ProtocolVersion,
        _options.ModVersion,
        _options.GameVersion,
        _options.ProviderMode,
        _options.ReadOnly,
        Ready: true,
        Notes: "prototype bridge");

    public void AdvanceIfNeeded(string phase)
    {
        if (string.Equals(_phase, phase, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _phase = phase;
        _stateVersion += 1;
        DecisionId = BridgeIds.CreateDecisionId(SessionId, _stateVersion, _phase);
    }

    public string CreateActionId(string actionType, IReadOnlyDictionary<string, object?> parameters)
    {
        return BridgeIds.CreateActionId(DecisionId, actionType, parameters);
    }
}
