using Sts2Mod.StateBridge.Configuration;
using Sts2Mod.StateBridge.Contracts;
using Sts2Mod.StateBridge.Core;
using Sts2Mod.StateBridge.Extraction;
using Sts2Mod.StateBridge.Logging;

namespace Sts2Mod.StateBridge.Providers;

internal static class InGameRuntimeCoordinator
{
    private static readonly object Gate = new();
    private static readonly Queue<PendingAction> PendingActions = new();
    private static Sts2RuntimeReflectionReader? _reader;
    private static BridgeSessionState? _sessionState;
    private static Dictionary<string, IWindowExtractor>? _extractors;
    private static IBridgeLogger? _logger;
    private static ExportedWindow? _currentWindow;
    private static string? _lastTickError;
    private static int _tickCount;
    private static DateTimeOffset? _lastTickAt;
    private static bool _initialized;

    public static bool IsInitialized
    {
        get
        {
            lock (Gate)
            {
                return _initialized;
            }
        }
    }

    public static void Initialize(Sts2RuntimeReflectionReader reader, BridgeOptions options, IBridgeLogger logger)
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            _reader = reader;
            _logger = logger;
            _sessionState = new BridgeSessionState(options);
            _extractors = new IWindowExtractor[]
            {
                new CombatWindowExtractor(),
                new RewardWindowExtractor(),
                new MapWindowExtractor(),
                new TerminalWindowExtractor(),
            }.ToDictionary(extractor => extractor.Phase, StringComparer.OrdinalIgnoreCase);
            _initialized = true;
            _lastTickError = null;
            _currentWindow = null;
            _tickCount = 0;
            _lastTickAt = null;
            logger.Info("Initialized in-game runtime coordinator");
        }
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            while (PendingActions.Count > 0)
            {
                var pending = PendingActions.Dequeue();
                pending.Completion.TrySetResult(CreateFailedResponse(
                    pending.Request,
                    pending.Request.ActionId,
                    "bridge_shutdown",
                    "In-game runtime coordinator is shutting down."));
            }

            _currentWindow = null;
            _lastTickError = null;
            _tickCount = 0;
            _lastTickAt = null;
            _extractors = null;
            _reader = null;
            _sessionState = null;
            _initialized = false;
        }
    }

    public static void Tick(string source)
    {
        Sts2RuntimeReflectionReader? reader;
        BridgeSessionState? sessionState;
        Dictionary<string, IWindowExtractor>? extractors;
        IBridgeLogger? logger;
        int tickCount;
        lock (Gate)
        {
            if (!_initialized)
            {
                return;
            }

            _tickCount++;
            _lastTickAt = DateTimeOffset.UtcNow;
            reader = _reader;
            sessionState = _sessionState;
            extractors = _extractors;
            logger = _logger;
            tickCount = _tickCount;
        }

        if (reader is null || sessionState is null || extractors is null)
        {
            return;
        }

        try
        {
            var context = reader.CaptureWindow();
            var window = extractors[context.Phase].Export(context, sessionState);
            lock (Gate)
            {
                _currentWindow = window;
                _lastTickError = null;
            }
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                _lastTickError = ex.Message;
            }

            logger?.Warn($"In-game tick could not capture runtime window: {ex.Message}");
        }

        ProcessPendingActions(reader, logger, tickCount, source);
    }

    public static bool TryGetCurrentWindow(out ExportedWindow window, out string? error)
    {
        lock (Gate)
        {
            if (_currentWindow is not null)
            {
                window = _currentWindow;
                error = null;
                return true;
            }

            window = default!;
            error = _lastTickError ?? "In-game runtime window is not ready yet.";
            return false;
        }
    }

    public static ActionResponse ApplyAction(ActionRequest request, bool readOnly)
    {
        request = EnsureRequestId(request);
        if (readOnly)
        {
            return CreateRejectedResponse(request, request.ActionId, "read_only", "Bridge is running in read-only mode.");
        }

        PendingAction pending;
        int tickCount;
        int queueDepth;
        lock (Gate)
        {
            if (!_initialized)
            {
                return CreateRejectedResponse(request, request.ActionId, "not_in_game_runtime", "Bridge is not running inside the STS2 process.");
            }

            pending = new PendingAction(request);
            PendingActions.Enqueue(pending);
            tickCount = _tickCount;
            queueDepth = PendingActions.Count;
        }
        pending.Trace.MarkEnqueued(tickCount, Environment.CurrentManagedThreadId);
        _logger?.Info($"Enqueued in-game action request_id={request.RequestId} action_id={request.ActionId} decision_id={request.DecisionId} queue_depth={queueDepth}");

        if (!pending.Completion.Task.Wait(TimeSpan.FromSeconds(3)))
        {
            var metadata = CreateTraceMetadata(pending);
            _logger?.Warn($"Timed out waiting for in-game action request_id={request.RequestId} action_id={request.ActionId} stage={metadata["queue_stage"]}");
            return CreateFailedResponse(
                request,
                request.ActionId,
                "action_timeout",
                "Timed out waiting for the game thread to process the action.",
                metadata);
        }

        return pending.Completion.Task.GetAwaiter().GetResult();
    }

    private static void ProcessPendingActions(Sts2RuntimeReflectionReader reader, IBridgeLogger? logger, int tickCount, string source)
    {
        while (true)
        {
            PendingAction? pending;
            ExportedWindow? window;
            int queueDepth;
            lock (Gate)
            {
                if (PendingActions.Count == 0)
                {
                    return;
                }

                pending = PendingActions.Dequeue();
                window = _currentWindow;
                queueDepth = PendingActions.Count;
            }

            try
            {
                pending.Trace.MarkDequeued(tickCount, Environment.CurrentManagedThreadId);
                logger?.Info($"Dequeued in-game action request_id={pending.Request.RequestId} source={source} queue_depth={queueDepth}");
                pending.Trace.MarkExecuting(tickCount, Environment.CurrentManagedThreadId);
                var response = ExecutePendingAction(reader, pending.Request, window, tickCount);
                response = MergeTraceMetadata(response, pending, tickCount, response.Status);
                pending.Completion.TrySetResult(response);
            }
            catch (Exception ex)
            {
                pending.Trace.MarkFailed(tickCount, "action_execution_failed", ex.Message);
                logger?.Error("Failed to process queued in-game action", ex);
                pending.Completion.TrySetResult(CreateFailedResponse(
                    pending.Request,
                    pending.Request.ActionId,
                    "action_execution_failed",
                    ex.Message,
                    CreateTraceMetadata(pending)));
            }
        }
    }

    private static ActionResponse ExecutePendingAction(
        Sts2RuntimeReflectionReader reader,
        ActionRequest request,
        ExportedWindow? currentWindow,
        int tickCount)
    {
        if (currentWindow is null)
        {
            return CreateRejectedResponse(request, request.ActionId, "runtime_not_ready", "No live decision window is available yet.");
        }

        if (!string.Equals(request.DecisionId, currentWindow.Snapshot.DecisionId, StringComparison.Ordinal))
        {
            return CreateRejectedResponse(request, request.ActionId, "stale_decision", "Requested decision_id is no longer current.");
        }

        var action = ResolveAction(currentWindow.Actions, request);
        if (action is null)
        {
            return CreateRejectedResponse(request, request.ActionId, "illegal_action", "Requested action is not part of the current legal action set.");
        }

        var result = reader.ExecuteAction(request, action);
        var responseMetadata = new Dictionary<string, object?>(result.Metadata)
        {
            ["phase"] = currentWindow.Snapshot.Phase,
            ["state_version"] = currentWindow.Snapshot.StateVersion,
            ["tick_count"] = tickCount,
        };
        if (!result.Accepted)
        {
            return CreateRejectedResponse(request, action.ActionId, result.ErrorCode ?? "action_rejected", result.Message, responseMetadata);
        }

        return CreateAcceptedResponse(request, action.ActionId, result.Message, responseMetadata);
    }

    private static LegalAction? ResolveAction(IEnumerable<LegalAction> actions, ActionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ActionId))
        {
            return actions.FirstOrDefault(action => string.Equals(action.ActionId, request.ActionId, StringComparison.Ordinal));
        }

        return actions.FirstOrDefault(action =>
            string.Equals(action.Type, request.ActionType, StringComparison.OrdinalIgnoreCase) &&
            request.Params.All(pair => action.Params.TryGetValue(pair.Key, out var value) && Equals(value, pair.Value)));
    }

    private static ActionResponse CreateAcceptedResponse(
        ActionRequest request,
        string? actionId,
        string message,
        IReadOnlyDictionary<string, object?> metadata)
    {
        return new ActionResponse(
            RequestId: request.RequestId ?? Guid.NewGuid().ToString("N"),
            DecisionId: request.DecisionId,
            ActionId: actionId,
            Status: "accepted",
            ErrorCode: null,
            Message: message,
            Metadata: metadata);
    }

    private static ActionResponse CreateRejectedResponse(
        ActionRequest request,
        string? actionId,
        string errorCode,
        string message,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        return new ActionResponse(
            RequestId: request.RequestId ?? Guid.NewGuid().ToString("N"),
            DecisionId: request.DecisionId,
            ActionId: actionId,
            Status: "rejected",
            ErrorCode: errorCode,
            Message: message,
            Metadata: metadata ?? new Dictionary<string, object?>());
    }

    private static ActionResponse CreateFailedResponse(
        ActionRequest request,
        string? actionId,
        string errorCode,
        string message,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        return new ActionResponse(
            RequestId: request.RequestId ?? Guid.NewGuid().ToString("N"),
            DecisionId: request.DecisionId,
            ActionId: actionId,
            Status: "failed",
            ErrorCode: errorCode,
            Message: message,
            Metadata: metadata ?? new Dictionary<string, object?>());
    }

    private static ActionRequest EnsureRequestId(ActionRequest request)
    {
        return string.IsNullOrWhiteSpace(request.RequestId)
            ? request with { RequestId = Guid.NewGuid().ToString("N") }
            : request;
    }

    private static IReadOnlyDictionary<string, object?> CreateTraceMetadata(PendingAction pending)
    {
        int lastTickCount;
        DateTimeOffset? lastTickAt;
        int pendingQueueCount;
        bool currentWindowReady;
        lock (Gate)
        {
            lastTickCount = _tickCount;
            lastTickAt = _lastTickAt;
            pendingQueueCount = PendingActions.Count;
            currentWindowReady = _currentWindow is not null;
        }

        return pending.Trace.ToMetadata(lastTickCount, lastTickAt, pendingQueueCount, currentWindowReady);
    }

    private static ActionResponse MergeTraceMetadata(ActionResponse response, PendingAction pending, int tickCount, string status)
    {
        if (string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase))
        {
            pending.Trace.MarkCompleted(tickCount, "completed", response.Message);
        }
        else if (string.Equals(status, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            pending.Trace.MarkCompleted(tickCount, "rejected", response.ErrorCode ?? response.Message);
        }

        var metadata = new Dictionary<string, object?>(response.Metadata);
        foreach (var pair in CreateTraceMetadata(pending))
        {
            metadata[pair.Key] = pair.Value;
        }

        return response with { Metadata = metadata };
    }

    private sealed class PendingAction
    {
        public PendingAction(ActionRequest request)
        {
            Request = request;
            Trace = new InGameActionTrace(request.RequestId ?? Guid.NewGuid().ToString("N"));
            Completion = new TaskCompletionSource<ActionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public ActionRequest Request { get; }

        public InGameActionTrace Trace { get; }

        public TaskCompletionSource<ActionResponse> Completion { get; }
    }
}
