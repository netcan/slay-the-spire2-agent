using System.Diagnostics;

namespace Sts2Mod.StateBridge.Providers;

internal sealed class InGameActionTrace
{
    private readonly object _gate = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly string _requestId;
    private readonly DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private string _stage = "created";
    private DateTimeOffset? _enqueuedAt;
    private DateTimeOffset? _dequeuedAt;
    private DateTimeOffset? _executionStartedAt;
    private DateTimeOffset? _completedAt;
    private int? _enqueuedTick;
    private int? _dequeuedTick;
    private int? _executionStartedTick;
    private int? _completedTick;
    private int? _enqueuedThreadId;
    private int? _dequeuedThreadId;
    private int? _executionThreadId;
    private string? _errorCode;
    private string? _detail;

    public InGameActionTrace(string requestId)
    {
        _requestId = requestId;
    }

    public void MarkEnqueued(int tickCount, int threadId)
    {
        lock (_gate)
        {
            _stage = "enqueued";
            _enqueuedAt = DateTimeOffset.UtcNow;
            _enqueuedTick = tickCount;
            _enqueuedThreadId = threadId;
        }
    }

    public void MarkDequeued(int tickCount, int threadId)
    {
        lock (_gate)
        {
            _stage = "dequeued";
            _dequeuedAt = DateTimeOffset.UtcNow;
            _dequeuedTick = tickCount;
            _dequeuedThreadId = threadId;
        }
    }

    public void MarkExecuting(int tickCount, int threadId)
    {
        lock (_gate)
        {
            _stage = "executing";
            _executionStartedAt = DateTimeOffset.UtcNow;
            _executionStartedTick = tickCount;
            _executionThreadId = threadId;
        }
    }

    public void MarkCompleted(int tickCount, string stage, string? detail = null)
    {
        lock (_gate)
        {
            _stage = stage;
            _completedAt = DateTimeOffset.UtcNow;
            _completedTick = tickCount;
            _detail = detail;
        }
    }

    public void MarkFailed(int tickCount, string errorCode, string detail)
    {
        lock (_gate)
        {
            _stage = "failed";
            _completedAt = DateTimeOffset.UtcNow;
            _completedTick = tickCount;
            _errorCode = errorCode;
            _detail = detail;
        }
    }

    public IReadOnlyDictionary<string, object?> ToMetadata(
        int lastTickCount,
        DateTimeOffset? lastTickAt,
        int pendingQueueCount,
        bool currentWindowReady)
    {
        lock (_gate)
        {
            return new Dictionary<string, object?>
            {
                ["request_id"] = _requestId,
                ["queue_stage"] = _stage,
                ["elapsed_ms"] = _stopwatch.ElapsedMilliseconds,
                ["current_window_ready"] = currentWindowReady,
                ["pending_queue_count"] = pendingQueueCount,
                ["last_tick_count"] = lastTickCount,
                ["last_tick_at"] = lastTickAt?.ToString("O"),
                ["created_at"] = _createdAt.ToString("O"),
                ["enqueued_at"] = _enqueuedAt?.ToString("O"),
                ["dequeued_at"] = _dequeuedAt?.ToString("O"),
                ["execution_started_at"] = _executionStartedAt?.ToString("O"),
                ["completed_at"] = _completedAt?.ToString("O"),
                ["enqueued_tick"] = _enqueuedTick,
                ["dequeued_tick"] = _dequeuedTick,
                ["execution_started_tick"] = _executionStartedTick,
                ["completed_tick"] = _completedTick,
                ["enqueued_thread_id"] = _enqueuedThreadId,
                ["dequeued_thread_id"] = _dequeuedThreadId,
                ["execution_thread_id"] = _executionThreadId,
                ["error_code"] = _errorCode,
                ["detail"] = _detail,
            };
        }
    }
}
