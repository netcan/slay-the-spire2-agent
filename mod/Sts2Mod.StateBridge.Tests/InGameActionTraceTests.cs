using Sts2Mod.StateBridge.Providers;
using Xunit;

namespace Sts2Mod.StateBridge.Tests;

public sealed class InGameActionTraceTests
{
    [Fact]
    public void ToMetadata_ReflectsQueueStagesAndTicks()
    {
        var trace = new InGameActionTrace("req-123");

        trace.MarkEnqueued(3, 10);
        trace.MarkDequeued(4, 11);
        trace.MarkExecuting(4, 11);
        trace.MarkCompleted(5, "completed", "ok");

        var metadata = trace.ToMetadata(5, DateTimeOffset.UtcNow, 0, currentWindowReady: true);

        Assert.Equal("req-123", metadata["request_id"]);
        Assert.Equal("completed", metadata["queue_stage"]);
        Assert.Equal(3, metadata["enqueued_tick"]);
        Assert.Equal(4, metadata["dequeued_tick"]);
        Assert.Equal(4, metadata["execution_started_tick"]);
        Assert.Equal(5, metadata["completed_tick"]);
        Assert.Equal(true, metadata["current_window_ready"]);
        Assert.Equal("ok", metadata["detail"]);
    }

    [Fact]
    public void ToMetadata_PreservesFailureReason()
    {
        var trace = new InGameActionTrace("req-456");

        trace.MarkEnqueued(7, 15);
        trace.MarkFailed(8, "action_timeout", "queue was never consumed");

        var metadata = trace.ToMetadata(9, DateTimeOffset.UtcNow, 1, currentWindowReady: false);

        Assert.Equal("failed", metadata["queue_stage"]);
        Assert.Equal("action_timeout", metadata["error_code"]);
        Assert.Equal("queue was never consumed", metadata["detail"]);
        Assert.Equal(1, metadata["pending_queue_count"]);
        Assert.Equal(false, metadata["current_window_ready"]);
    }
}
