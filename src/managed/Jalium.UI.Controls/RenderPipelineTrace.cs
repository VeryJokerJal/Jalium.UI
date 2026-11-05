using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Jalium.UI.Rendering;

// Explicit diagnostic capture only. No buffer, file, or per-event allocation
// exists on the normal rendering path. Export runs after the worker stops.
internal sealed class RenderPipelineTrace
{
    private static int _nextId;
    private readonly object _gate = new();
    private readonly Entry[] _entries;
    private readonly string _path;
    private long _count;

    internal RenderPipelineTrace(string path, int capacity = 262144)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _path = path;
        _entries = new Entry[capacity];
    }

    internal static RenderPipelineTrace? TryCreate()
    {
        string? path = Environment.GetEnvironmentVariable("JALIUM_RENDER_PIPELINE_TRACE");
        if (string.IsNullOrWhiteSpace(path)) return null;
        return new RenderPipelineTrace($"{path}.{Environment.ProcessId}.{Interlocked.Increment(ref _nextId)}.csv");
    }

    internal void Mark(RenderPipelineStage stage, long ticket = 0, int detail = 0, long timestampTicks = 0)
    {
        long timestamp = timestampTicks != 0 ? timestampTicks : Stopwatch.GetTimestamp();
        lock (_gate)
        {
            _entries[_count % _entries.Length] = new Entry(timestamp, ticket, stage, detail);
            _count++;
        }
    }

    internal void Export()
    {
        try
        {
            Entry[] entries;
            long total;
            lock (_gate)
            {
                total = _count;
                entries = new Entry[(int)Math.Min(total, _entries.Length)];
                long start = total - entries.Length;
                for (int i = 0; i < entries.Length; i++)
                    entries[i] = _entries[(start + i) % _entries.Length];
            }
            using var writer = new StreamWriter(_path);
            writer.WriteLine(FormattableString.Invariant($"# frequency={Stopwatch.Frequency},total={total},dropped={total - entries.Length}"));
            writer.WriteLine("timestamp,ticket,stage,detail");
            foreach (var entry in entries)
            {
                writer.Write(entry.Timestamp.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(entry.Ticket.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(entry.Stage.ToString());
                writer.Write(',');
                writer.WriteLine(entry.Detail.ToString(CultureInfo.InvariantCulture));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Jalium.Render] Pipeline trace export failed: {ex.Message}");
        }
    }

    private readonly record struct Entry(long Timestamp, long Ticket, RenderPipelineStage Stage, int Detail);
}

internal enum RenderPipelineStage
{
    FrameClock,
    ProcessRender,
    RenderFrame,
    CaptureRequested,
    CaptureCoalesced,
    CaptureAcquired,
    RecordStart,
    RecordEnd,
    Published,
    WorkerClaim,
    SlotCompleted,
    RefreshPosted,
    RefreshDispatch,
    BeforeBegin,
    AfterBegin,
    ReplayStart,
    ReplayEnd,
    BeforeEnd,
    Submitted,
    CaptureDelayStart,
    CaptureDelayEnd,
    CaptureTimerStopped,
}
