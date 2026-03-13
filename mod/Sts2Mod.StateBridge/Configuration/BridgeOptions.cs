namespace Sts2Mod.StateBridge.Configuration;

public sealed class BridgeOptions
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 17654;
    public string ProtocolVersion { get; init; } = "0.1.0";
    public string ModVersion { get; init; } = "0.1.0";
    public string GameVersion { get; init; } = "prototype";
    public string ProviderMode { get; init; } = "fixture";
    public bool AllowDebugPhaseOverride { get; init; } = true;
    public bool ReadOnly { get; init; } = true;

    public string Prefix => $"http://{Host}:{Port}/";
}
