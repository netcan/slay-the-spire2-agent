using Sts2Mod.StateBridge;
using Sts2Mod.StateBridge.Configuration;
using Sts2Mod.StateBridge.Logging;
using Sts2Mod.StateBridge.Providers;

var options = ParseArgs(args);
var logger = new ConsoleBridgeLogger();
var provider = new FixtureGameStateProvider(options);
await using var bootstrap = new ModBootstrap(options, provider, logger);
await bootstrap.StartAsync();

var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    completion.TrySetResult();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => completion.TrySetResult();

logger.Info("Bridge host is running. Press Ctrl+C to stop.");
await completion.Task;
await bootstrap.StopAsync();

static BridgeOptions ParseArgs(string[] args)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < args.Length; index += 2)
    {
        var key = args[index];
        if (!key.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
        {
            continue;
        }

        values[key[2..]] = args[index + 1];
    }

    return new BridgeOptions
    {
        Host = values.GetValueOrDefault("host") ?? "127.0.0.1",
        Port = int.TryParse(values.GetValueOrDefault("port"), out var port) ? port : 17654,
        ProtocolVersion = values.GetValueOrDefault("protocol-version") ?? "0.1.0",
        ModVersion = values.GetValueOrDefault("mod-version") ?? "0.1.0",
        GameVersion = values.GetValueOrDefault("game-version") ?? "prototype",
        ProviderMode = values.GetValueOrDefault("provider-mode") ?? "fixture",
        AllowDebugPhaseOverride = !string.Equals(values.GetValueOrDefault("allow-debug-phase-override"), "false", StringComparison.OrdinalIgnoreCase),
        ReadOnly = true,
    };
}
