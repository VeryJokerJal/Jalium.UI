using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;

namespace Jalium.UI.Diagnostics;

/// <summary>
/// A low-overhead scope returned by <see cref="StartupDiagnostics.Begin"/>.
/// </summary>
public readonly struct StartupTraceScope : IDisposable
{
    private readonly string? _stageName;
    private readonly long _startedAt;
    private readonly bool _blocksUiThread;

    internal StartupTraceScope(string stageName, long startedAt, bool blocksUiThread)
    {
        _stageName = stageName;
        _startedAt = startedAt;
        _blocksUiThread = blocksUiThread;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_stageName != null)
        {
            StartupDiagnostics.Complete(_stageName, _startedAt, _blocksUiThread);
        }
    }
}

/// <summary>
/// Records process-startup phases and milestones without taking a dependency on the
/// Generic Host or application logging. Text tracing is enabled by setting
/// <c>JALIUM_STARTUP_TRACE=1</c> and/or <c>JALIUM_STARTUP_TRACE_FILE</c>. EventPipe/ETW
/// consumers can instead enable the <c>Jalium-UI-Startup</c> EventSource.
/// </summary>
/// <remarks>
/// When neither output is enabled, calls perform only validation and an EventSource-enabled
/// check. Process start-time lookup, wall-clock capture, formatting, and file I/O are all
/// deferred until tracing is actually enabled.
/// </remarks>
public static class StartupDiagnostics
{
    private const string TraceEnvironmentVariable = "JALIUM_STARTUP_TRACE";
    private const string TraceFileEnvironmentVariable = "JALIUM_STARTUP_TRACE_FILE";

    private static readonly object s_gate = new();
    private static readonly bool s_consoleEnabled = IsEnabledValue(
        Environment.GetEnvironmentVariable(TraceEnvironmentVariable));
    private static readonly string? s_traceFilePath = NormalizeTraceFilePath(
        Environment.GetEnvironmentVariable(TraceFileEnvironmentVariable));

    private static StreamWriter? s_traceFileWriter;
    private static bool s_traceFileWriterInitialized;
    private static int s_clockInitialized;
    private static long s_clockOriginTimestamp;
    private static double s_processElapsedAtOriginMs;
    private static int s_uiThreadId;

    /// <summary>
    /// Gets whether structured text or EventSource startup tracing is currently enabled.
    /// </summary>
    public static bool IsEnabled =>
        s_consoleEnabled ||
        s_traceFilePath != null ||
        StartupEventSource.Log.IsEnabled();

    /// <summary>
    /// Begins a named startup stage. Disposing the returned scope records its end and duration.
    /// </summary>
    /// <param name="stageName">Stable, human-readable stage name.</param>
    /// <param name="blocksUiThread">
    /// Whether this synchronous call prevents the UI thread from processing input while it runs.
    /// </param>
    public static StartupTraceScope Begin(string stageName, bool blocksUiThread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);

        if (!IsEnabled)
        {
            return default;
        }

        EnsureClockInitialized();
        long startedAt = Stopwatch.GetTimestamp();
        WriteEvent(
            StartupTraceEventKind.Started,
            stageName,
            GetProcessElapsedMilliseconds(startedAt),
            durationMs: 0,
            blocksUiThread);
        return new StartupTraceScope(stageName, startedAt, blocksUiThread);
    }

    /// <summary>
    /// Records an instantaneous startup milestone.
    /// </summary>
    /// <param name="stageName">Stable, human-readable milestone name.</param>
    /// <param name="blocksUiThread">
    /// Whether the work immediately preceding this milestone was executing on a blocking UI path.
    /// </param>
    public static void Mark(string stageName, bool blocksUiThread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);

        if (!IsEnabled)
        {
            return;
        }

        EnsureClockInitialized();
        long timestamp = Stopwatch.GetTimestamp();
        WriteEvent(
            StartupTraceEventKind.Milestone,
            stageName,
            GetProcessElapsedMilliseconds(timestamp),
            durationMs: 0,
            blocksUiThread);
    }

    internal static void NotifyUiThreadRegistered()
    {
        Volatile.Write(ref s_uiThreadId, Environment.CurrentManagedThreadId);
        Mark("UiThreadRegistered", blocksUiThread: false);
    }

    internal static int RegisteredUiThreadIdForTesting
    {
        get => Volatile.Read(ref s_uiThreadId);
        set => Volatile.Write(ref s_uiThreadId, value);
    }

    internal static void Complete(string stageName, long startedAt, bool blocksUiThread)
    {
        long completedAt = Stopwatch.GetTimestamp();
        double durationMs = Stopwatch.GetElapsedTime(startedAt, completedAt).TotalMilliseconds;
        WriteEvent(
            StartupTraceEventKind.Completed,
            stageName,
            GetProcessElapsedMilliseconds(completedAt),
            durationMs,
            blocksUiThread);
    }

    private static void EnsureClockInitialized()
    {
        if (Volatile.Read(ref s_clockInitialized) != 0)
        {
            return;
        }

        lock (s_gate)
        {
            if (s_clockInitialized != 0)
            {
                return;
            }

            long originTimestamp = Stopwatch.GetTimestamp();
            DateTime originUtc = DateTime.UtcNow;
            double elapsedAtOriginMs = 0;
            try
            {
                using var process = Process.GetCurrentProcess();
                elapsedAtOriginMs = Math.Max(
                    0,
                    (originUtc - process.StartTime.ToUniversalTime()).TotalMilliseconds);
            }
            catch
            {
                // Process.StartTime can be unavailable in restricted containers. The
                // monotonic phase durations remain valid; process elapsed starts at zero.
            }

            s_clockOriginTimestamp = originTimestamp;
            s_processElapsedAtOriginMs = elapsedAtOriginMs;
            Volatile.Write(ref s_clockInitialized, 1);
        }
    }

    private static double GetProcessElapsedMilliseconds(long timestamp)
    {
        double elapsedSinceOriginMs =
            (timestamp - s_clockOriginTimestamp) * 1000.0 / Stopwatch.Frequency;
        return Math.Max(0, s_processElapsedAtOriginMs + elapsedSinceOriginMs);
    }

    private static void WriteEvent(
        StartupTraceEventKind eventKind,
        string stageName,
        double processElapsedMs,
        double durationMs,
        bool blocksUiThread)
    {
        DateTimeOffset utcTimestamp = DateTimeOffset.UtcNow;
        Thread thread = Thread.CurrentThread;
        int threadId = Environment.CurrentManagedThreadId;
        int registeredUiThreadId = Volatile.Read(ref s_uiThreadId);
        int uiThreadState = registeredUiThreadId == 0
            ? -1
            : registeredUiThreadId == threadId ? 1 : 0;
        string threadName = thread.Name ?? string.Empty;
        bool isThreadPoolThread = thread.IsThreadPoolThread;

        try
        {
            switch (eventKind)
            {
                case StartupTraceEventKind.Started:
                    StartupEventSource.Log.StageStarted(
                        stageName,
                        utcTimestamp.ToString("O", CultureInfo.InvariantCulture),
                        processElapsedMs,
                        threadId,
                        threadName,
                        isThreadPoolThread,
                        uiThreadState,
                        blocksUiThread);
                    break;
                case StartupTraceEventKind.Completed:
                    StartupEventSource.Log.StageCompleted(
                        stageName,
                        utcTimestamp.ToString("O", CultureInfo.InvariantCulture),
                        processElapsedMs,
                        durationMs,
                        threadId,
                        threadName,
                        isThreadPoolThread,
                        uiThreadState,
                        blocksUiThread);
                    break;
                default:
                    StartupEventSource.Log.Milestone(
                        stageName,
                        utcTimestamp.ToString("O", CultureInfo.InvariantCulture),
                        processElapsedMs,
                        threadId,
                        threadName,
                        isThreadPoolThread,
                        uiThreadState,
                        blocksUiThread);
                    break;
            }

            if (s_consoleEnabled || s_traceFilePath != null)
            {
                WriteTextRecord(
                    eventKind,
                    stageName,
                    utcTimestamp,
                    processElapsedMs,
                    durationMs,
                    threadId,
                    threadName,
                    isThreadPoolThread,
                    uiThreadState,
                    blocksUiThread);
            }
        }
        catch
        {
            // Diagnostics must never delay startup recovery or change application behavior.
        }
    }

    private static void WriteTextRecord(
        StartupTraceEventKind eventKind,
        string stageName,
        DateTimeOffset utcTimestamp,
        double processElapsedMs,
        double durationMs,
        int threadId,
        string threadName,
        bool isThreadPoolThread,
        int uiThreadState,
        bool blocksUiThread)
    {
        lock (s_gate)
        {
            if (s_consoleEnabled)
            {
                WriteTextRecordCore(
                    Console.Error,
                    eventKind,
                    stageName,
                    utcTimestamp,
                    processElapsedMs,
                    durationMs,
                    threadId,
                    threadName,
                    isThreadPoolThread,
                    uiThreadState,
                    blocksUiThread);
            }

            var fileWriter = GetTraceFileWriter();
            if (fileWriter != null)
            {
                WriteTextRecordCore(
                    fileWriter,
                    eventKind,
                    stageName,
                    utcTimestamp,
                    processElapsedMs,
                    durationMs,
                    threadId,
                    threadName,
                    isThreadPoolThread,
                    uiThreadState,
                    blocksUiThread);
                if (ShouldFlushTextRecord(eventKind, stageName))
                    fileWriter.Flush();
            }
        }
    }

    private static StreamWriter? GetTraceFileWriter()
    {
        if (s_traceFileWriterInitialized)
        {
            return s_traceFileWriter;
        }

        s_traceFileWriterInitialized = true;
        if (s_traceFilePath == null)
        {
            return null;
        }

        try
        {
            string? directory = Path.GetDirectoryName(s_traceFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            s_traceFileWriter = new StreamWriter(new FileStream(
                s_traceFilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite));
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            return s_traceFileWriter;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteTextRecordCore(
        TextWriter writer,
        StartupTraceEventKind eventKind,
        string stageName,
        DateTimeOffset utcTimestamp,
        double processElapsedMs,
        double durationMs,
        int threadId,
        string threadName,
        bool isThreadPoolThread,
        int uiThreadState,
        bool blocksUiThread)
    {
        writer.Write("[Jalium.Startup] event=");
        writer.Write(eventKind switch
        {
            StartupTraceEventKind.Started => "started",
            StartupTraceEventKind.Completed => "completed",
            _ => "milestone",
        });
        writer.Write(" stage=");
        WriteQuoted(writer, stageName);
        writer.Write(" process_id=");
        writer.Write(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        writer.Write(" utc=");
        writer.Write(utcTimestamp.ToString("O", CultureInfo.InvariantCulture));
        writer.Write(" process_ms=");
        writer.Write(processElapsedMs.ToString("F3", CultureInfo.InvariantCulture));
        writer.Write(" duration_ms=");
        writer.Write(durationMs.ToString("F3", CultureInfo.InvariantCulture));
        writer.Write(" thread_id=");
        writer.Write(threadId.ToString(CultureInfo.InvariantCulture));
        writer.Write(" thread_name=");
        WriteQuoted(writer, threadName);
        writer.Write(" thread_pool=");
        writer.Write(isThreadPoolThread ? "true" : "false");
        writer.Write(" ui_thread=");
        writer.Write(uiThreadState switch { 1 => "true", 0 => "false", _ => "unknown" });
        writer.Write(" blocks_ui=");
        writer.WriteLine(blocksUiThread ? "true" : "false");
    }

    private static void WriteQuoted(TextWriter writer, string value)
    {
        writer.Write('"');
        foreach (char character in value)
        {
            switch (character)
            {
                case '\\': writer.Write("\\\\"); break;
                case '"': writer.Write("\\\""); break;
                case '\r': writer.Write("\\r"); break;
                case '\n': writer.Write("\\n"); break;
                case '\t': writer.Write("\\t"); break;
                default: writer.Write(character); break;
            }
        }
        writer.Write('"');
    }

    private static bool IsEnabledValue(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeTraceFilePath(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ShouldFlushTextRecord(
        StartupTraceEventKind eventKind,
        string stageName) =>
        eventKind == StartupTraceEventKind.Milestone && stageName is
            "MainWindowShowReturned" or
            "MainWindowFirstInputReady" or
            "Gallery.DeferredSectionsCompleted" or
            "Gallery.DeferredSectionsCanceled";

    private static void OnProcessExit(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        lock (s_gate)
        {
            try
            {
                s_traceFileWriter?.Flush();
            }
            catch
            {
                // Process-exit diagnostics must never change shutdown behavior.
            }
        }
    }

    private enum StartupTraceEventKind
    {
        Started,
        Completed,
        Milestone,
    }
}

[EventSource(Name = "Jalium-UI-Startup")]
internal sealed class StartupEventSource : EventSource
{
    internal static readonly StartupEventSource Log = new();

    private StartupEventSource()
    {
    }

    [Event(1, Level = EventLevel.Informational)]
    public void StageStarted(
        string stageName,
        string utcTimestamp,
        double processElapsedMs,
        int threadId,
        string threadName,
        bool isThreadPoolThread,
        int uiThreadState,
        bool blocksUiThread)
    {
        if (IsEnabled())
        {
            WriteEvent(1, stageName, utcTimestamp, processElapsedMs, threadId, threadName,
                isThreadPoolThread, uiThreadState, blocksUiThread);
        }
    }

    [Event(2, Level = EventLevel.Informational)]
    public void StageCompleted(
        string stageName,
        string utcTimestamp,
        double processElapsedMs,
        double durationMs,
        int threadId,
        string threadName,
        bool isThreadPoolThread,
        int uiThreadState,
        bool blocksUiThread)
    {
        if (IsEnabled())
        {
            WriteEvent(2, stageName, utcTimestamp, processElapsedMs, durationMs, threadId,
                threadName, isThreadPoolThread, uiThreadState, blocksUiThread);
        }
    }

    [Event(3, Level = EventLevel.Informational)]
    public void Milestone(
        string stageName,
        string utcTimestamp,
        double processElapsedMs,
        int threadId,
        string threadName,
        bool isThreadPoolThread,
        int uiThreadState,
        bool blocksUiThread)
    {
        if (IsEnabled())
        {
            WriteEvent(3, stageName, utcTimestamp, processElapsedMs, threadId, threadName,
                isThreadPoolThread, uiThreadState, blocksUiThread);
        }
    }
}
