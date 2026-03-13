using Sts2Mod.StateBridge.Contracts;

namespace Sts2Mod.StateBridge.Providers;

public sealed class Sts2RuntimeStateProvider : IGameStateProvider
{
    public HealthResponse GetHealth()
    {
        throw new NotImplementedException("Wire this provider to real STS2 runtime assemblies once Sts2ManagedDir is configured.");
    }

    public DecisionSnapshot GetSnapshot(string? requestedPhase = null)
    {
        throw new NotImplementedException("Real STS2 runtime extraction is not wired in this prototype yet.");
    }

    public IReadOnlyList<LegalAction> GetActions(string? requestedPhase = null)
    {
        throw new NotImplementedException("Real STS2 runtime extraction is not wired in this prototype yet.");
    }
}
