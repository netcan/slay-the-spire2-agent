using Sts2Mod.StateBridge.Configuration;
using Sts2Mod.StateBridge.Logging;
using Sts2Mod.StateBridge.Providers;
using Sts2Mod.StateBridge.Server;

namespace Sts2Mod.StateBridge;

public sealed class ModBootstrap : IAsyncDisposable
{
    private readonly LocalBridgeServer _server;
    private readonly IBridgeLogger _logger;

    public ModBootstrap(BridgeOptions options, IGameStateProvider provider, IBridgeLogger logger)
    {
        Options = options;
        Provider = provider;
        _logger = logger;
        _server = new LocalBridgeServer(options, provider, logger);
    }

    public BridgeOptions Options { get; }

    public IGameStateProvider Provider { get; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info($"Starting STS2 mod bridge bootstrap (mode={Options.ProviderMode}, protocol={Options.ProtocolVersion})");
        return _server.StartAsync(cancellationToken);
    }

    public Task StopAsync() => _server.StopAsync();

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync().ConfigureAwait(false);
    }
}
