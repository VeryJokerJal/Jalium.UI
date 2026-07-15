using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using Jalium.UI.Diagnostics;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class StartupDiagnosticsTests
{
    [Fact]
    public void ScopeAndMilestone_EmitStructuredMonotonicEvents()
    {
        using var listener = new StartupEventListener();
        const string stageName = "StartupDiagnosticsTests.Scope";
        const string milestoneName = "StartupDiagnosticsTests.Milestone";

        using (StartupDiagnostics.Begin(stageName, blocksUiThread: true))
        {
            Thread.SpinWait(50_000);
        }
        StartupDiagnostics.Mark(milestoneName, blocksUiThread: false);

        var started = Assert.Single(listener.Events,
            e => e.EventName == "StageStarted" && e.StageName == stageName);
        var completed = Assert.Single(listener.Events,
            e => e.EventName == "StageCompleted" && e.StageName == stageName);
        var milestone = Assert.Single(listener.Events,
            e => e.EventName == "Milestone" && e.StageName == milestoneName);

        Assert.True(started.ProcessElapsedMs >= 0);
        Assert.True(completed.ProcessElapsedMs >= started.ProcessElapsedMs);
        Assert.True(completed.DurationMs > 0);
        Assert.True(milestone.ProcessElapsedMs >= completed.ProcessElapsedMs);
        Assert.True(started.BlocksUiThread);
        Assert.False(milestone.BlocksUiThread);
        Assert.Equal(Environment.CurrentManagedThreadId, started.ThreadId);
    }

    [Fact]
    public void RegisteredUiThread_IsIncludedInMilestone()
    {
        using var listener = new StartupEventListener();
        const string milestoneName = "StartupDiagnosticsTests.UiThread";

        int previousUiThreadId = StartupDiagnostics.RegisteredUiThreadIdForTesting;
        try
        {
            StartupDiagnostics.NotifyUiThreadRegistered();
            bool expectedThreadPoolState = Thread.CurrentThread.IsThreadPoolThread;
            StartupDiagnostics.Mark(milestoneName, blocksUiThread: false);

            var milestone = Assert.Single(listener.Events,
                e => e.EventName == "Milestone" && e.StageName == milestoneName);
            Assert.Equal(1, milestone.UiThreadState);
            Assert.Equal(Environment.CurrentManagedThreadId, milestone.ThreadId);
            Assert.Equal(expectedThreadPoolState, milestone.IsThreadPoolThread);
        }
        finally
        {
            StartupDiagnostics.RegisteredUiThreadIdForTesting = previousUiThreadId;
        }
    }

    private sealed class StartupEventListener : EventListener
    {
        public ConcurrentQueue<CapturedStartupEvent> Events { get; } = new();

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Jalium-UI-Startup")
            {
                EnableEvents(eventSource, EventLevel.Informational);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventSource.Name != "Jalium-UI-Startup" ||
                eventData.Payload == null ||
                eventData.EventId is < 1 or > 3)
            {
                return;
            }

            object?[] payload = eventData.Payload.ToArray();
            string eventName = eventData.EventName ?? string.Empty;
            int expectedPayloadCount = eventData.EventId == 2 ? 9 : 8;
            if (payload.Length < expectedPayloadCount)
            {
                return;
            }

            int durationIndex = eventName == "StageCompleted" ? 3 : -1;
            int threadIdIndex = eventName == "StageCompleted" ? 4 : 3;
            int threadPoolIndex = eventName == "StageCompleted" ? 6 : 5;
            int uiThreadIndex = eventName == "StageCompleted" ? 7 : 6;
            int blocksUiIndex = eventName == "StageCompleted" ? 8 : 7;

            Events.Enqueue(new CapturedStartupEvent(
                eventName,
                (string)payload[0]!,
                Convert.ToDouble(payload[2], System.Globalization.CultureInfo.InvariantCulture),
                durationIndex >= 0
                    ? Convert.ToDouble(payload[durationIndex], System.Globalization.CultureInfo.InvariantCulture)
                    : 0,
                Convert.ToInt32(payload[threadIdIndex], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToBoolean(payload[threadPoolIndex], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToInt32(payload[uiThreadIndex], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToBoolean(payload[blocksUiIndex], System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    private sealed record CapturedStartupEvent(
        string EventName,
        string StageName,
        double ProcessElapsedMs,
        double DurationMs,
        int ThreadId,
        bool IsThreadPoolThread,
        int UiThreadState,
        bool BlocksUiThread);
}
