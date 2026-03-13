using Sts2Mod.StateBridge.Configuration;
using Sts2Mod.StateBridge.Contracts;

namespace Sts2Mod.StateBridge.Core;

public sealed class BridgeSessionState
{
    private readonly BridgeOptions _options;
    private int _stateVersion;
    private string _phase;
    private string? _fingerprint;

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
        Notes: _options.ProviderMode == "runtime" ? "runtime bridge" : "prototype bridge");

    public void AdvanceIfNeeded(string phase, string? fingerprint = null)
    {
        var normalizedFingerprint = string.IsNullOrWhiteSpace(fingerprint) ? null : fingerprint;
        var phaseChanged = !string.Equals(_phase, phase, StringComparison.OrdinalIgnoreCase);
        var fingerprintChanged = !string.Equals(_fingerprint, normalizedFingerprint, StringComparison.Ordinal);
        if (!phaseChanged && !fingerprintChanged)
        {
            return;
        }

        _phase = phase;
        _fingerprint = normalizedFingerprint;
        _stateVersion += 1;
        DecisionId = BridgeIds.CreateDecisionId(SessionId, _stateVersion, _phase);
    }

    public string CreateActionId(string actionType, IReadOnlyDictionary<string, object?> parameters)
    {
        return BridgeIds.CreateActionId(DecisionId, actionType, parameters);
    }
}
