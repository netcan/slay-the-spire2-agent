using Sts2Mod.StateBridge.Contracts;

namespace Sts2Mod.StateBridge.Providers;

public interface IGameStateProvider
{
    HealthResponse GetHealth();

    DecisionSnapshot GetSnapshot(string? requestedPhase = null);

    IReadOnlyList<LegalAction> GetActions(string? requestedPhase = null);
}
