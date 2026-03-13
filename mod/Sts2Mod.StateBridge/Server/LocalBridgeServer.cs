using System.Net;
using System.Text.Json;
using Sts2Mod.StateBridge.Configuration;
using Sts2Mod.StateBridge.Contracts;
using Sts2Mod.StateBridge.Logging;
using Sts2Mod.StateBridge.Providers;

namespace Sts2Mod.StateBridge.Server;

public sealed class LocalBridgeServer : IAsyncDisposable
{
    private readonly BridgeOptions _options;
    private readonly IGameStateProvider _provider;
    private readonly IBridgeLogger _logger;
    private readonly HttpListener _listener;
    private readonly JsonSerializerOptions _jsonOptions;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public LocalBridgeServer(BridgeOptions options, IGameStateProvider provider, IBridgeLogger logger)
    {
        _options = options;
        _provider = provider;
        _logger = logger;
        _listener = new HttpListener();
        _listener.Prefixes.Add(options.Prefix);
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
        };
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_listener.IsListening)
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
        _logger.Info($"Local bridge listening on {_options.Prefix}");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!_listener.IsListening)
        {
            return;
        }

        _cts?.Cancel();
        _listener.Stop();
        if (_loopTask is not null)
        {
            await _loopTask.ConfigureAwait(false);
        }
        _logger.Info("Local bridge stopped");
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                await HandleAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException) when (!_listener.IsListening)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error("Unhandled bridge loop failure", ex);
                if (context is not null)
                {
                    await WriteAsync(context.Response, 500, new ErrorResponse("bridge_error", ex.Message), cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        if (!string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            await WriteAsync(context.Response, 405, new ErrorResponse("method_not_allowed", "Only GET is supported."), cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var phase = request.QueryString["phase"];
            var path = request.Url?.AbsolutePath?.TrimEnd('/').ToLowerInvariant() ?? string.Empty;
            object payload = path switch
            {
                "/health" => _provider.GetHealth(),
                "/snapshot" => _provider.GetSnapshot(phase),
                "/actions" => _provider.GetActions(phase),
                _ => new ErrorResponse("not_found", $"Unknown endpoint: {path}")
            };
            var statusCode = payload is ErrorResponse ? 404 : 200;
            await WriteAsync(context.Response, statusCode, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("Request handling failed", ex);
            await WriteAsync(context.Response, 500, new ErrorResponse("state_export_failed", ex.Message), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteAsync(HttpListenerResponse response, int statusCode, object payload, CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(response.OutputStream, payload, _jsonOptions, cancellationToken).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _listener.Close();
        _cts?.Dispose();
    }
}
