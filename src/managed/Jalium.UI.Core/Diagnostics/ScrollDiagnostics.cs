using System.Globalization;
using System.Text;

namespace Jalium.UI.Diagnostics;

/// <summary>
/// Opt-in trace of what actually moves a <c>ScrollViewer</c>'s content: the scroll
/// offsets, the rubber-band overscroll, and the rectangle the content is finally
/// arranged at.
/// </summary>
/// <remarks>
/// Exists because "the content sits a few dozen pixels too low" is invisible in
/// every other instrument — the layout looks correct, sizes are right, and only the
/// arrange origin is wrong. Recording origin changes together with the values that
/// feed them shows whether an offset, a bounce animation, or a stale viewport put
/// the content there.
/// <para>
/// Costs one boolean test when disabled. Enable with the
/// <c>JALIUM_SCROLL_TRACE=1</c> environment variable, or by dropping a file named
/// <c>jalium_scroll_trace.on</c> next to the executable — windowed apps launched
/// from Explorer or the debugger cannot easily be given an environment variable.
/// Output goes to <c>%TEMP%\jalium_scroll_trace.log</c> unless
/// <c>JALIUM_SCROLL_TRACE_FILE</c> says otherwise.
/// </para>
/// </remarks>
public static class ScrollDiagnostics
{
    public static readonly bool Enabled = ResolveEnabled();

    private static readonly string s_filePath = ResolveFilePath();
    private static readonly object s_gate = new();
    private static readonly StringBuilder s_pending = new();
    private static long s_sequence;

    private static bool ResolveEnabled()
    {
        var env = Environment.GetEnvironmentVariable("JALIUM_SCROLL_TRACE");
        if (env is "1" or "true" or "True")
        {
            return true;
        }

        try
        {
            if (System.IO.File.Exists(
                System.IO.Path.Combine(AppContext.BaseDirectory, "jalium_scroll_trace.on")))
            {
                return true;
            }

            if (System.IO.File.Exists("jalium_scroll_trace.on"))
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string ResolveFilePath()
    {
        var custom = Environment.GetEnvironmentVariable("JALIUM_SCROLL_TRACE_FILE");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return custom;
        }

        return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jalium_scroll_trace.log");
    }

    /// <summary>
    /// Records where a scroll viewer is about to place its content, alongside the
    /// values that decided it. <paramref name="originY"/> is the interesting one:
    /// anything other than <c>-offsetY</c> means something else moved the content.
    /// </summary>
    public static void RecordArrange(
        string viewerName,
        double finalHeight,
        double viewportHeight,
        double extentHeight,
        double offsetY,
        double overscrollY,
        double originY)
    {
        if (!Enabled)
        {
            return;
        }

        Append(
            $"arrange name={viewerName} final={F(finalHeight)} vp={F(viewportHeight)} " +
            $"ext={F(extentHeight)} off={F(offsetY)} over={F(overscrollY)} originY={F(originY)}");
    }

    /// <summary>
    /// Records a change to the rubber-band overscroll, which offsets the content
    /// independently of the scroll offset. <paramref name="phase"/> names what drove
    /// it — a gesture, a bounce-animation frame, or the animation settling.
    /// </summary>
    public static void RecordOverscroll(
        string viewerName,
        string phase,
        double overscrollX,
        double overscrollY)
    {
        if (!Enabled)
        {
            return;
        }

        Append($"overscroll name={viewerName} phase={phase} x={F(overscrollX)} y={F(overscrollY)}");
    }

    /// <summary>Records a scroll offset change and what asked for it.</summary>
    public static void RecordOffset(
        string viewerName,
        string source,
        double offsetX,
        double offsetY)
    {
        if (!Enabled)
        {
            return;
        }

        Append($"offset name={viewerName} src={source} x={F(offsetX)} y={F(offsetY)}");
    }

    /// <summary>
    /// Records a detailed phase in the input/metrics/provider pipeline. Callers should
    /// guard expensive detail construction with <see cref="Enabled"/>.
    /// </summary>
    public static void RecordEvent(
        string elementName,
        int elementId,
        string phase,
        string details)
    {
        if (!Enabled)
        {
            return;
        }

        var safeName = string.IsNullOrEmpty(elementName) ? "<unnamed>" : elementName;
        Append(
            $"event tick={Environment.TickCount64} thread={Environment.CurrentManagedThreadId} " +
            $"element={safeName} id={elementId:x8} phase={phase} {details}");
    }

    private static string F(double value)
        => value.ToString("F1", CultureInfo.InvariantCulture);

    private static void Append(string line)
    {
        // Buffered so a drag storm does not turn into thousands of file opens; the
        // sequence number keeps ordering readable across flushes.
        string? toFlush = null;
        lock (s_gate)
        {
            s_pending.Append(System.Threading.Interlocked.Increment(ref s_sequence))
                     .Append(' ')
                     .AppendLine(line);

            // Small on purpose: a trace is usually read after killing the process,
            // and the frames right before the kill are the interesting ones.
            if (s_pending.Length >= 512)
            {
                toFlush = s_pending.ToString();
                s_pending.Clear();
            }
        }

        if (toFlush != null)
        {
            TryWrite(toFlush);
        }
    }

    /// <summary>
    /// Writes anything still buffered. Call before inspecting the log — a trace that
    /// ends mid-drag would otherwise be missing exactly the frames of interest.
    /// </summary>
    public static void Flush()
    {
        if (!Enabled)
        {
            return;
        }

        string toFlush;
        lock (s_gate)
        {
            if (s_pending.Length == 0)
            {
                return;
            }

            toFlush = s_pending.ToString();
            s_pending.Clear();
        }

        TryWrite(toFlush);
    }

    private static void TryWrite(string text)
    {
        try
        {
            System.IO.File.AppendAllText(s_filePath, text);
        }
        catch
        {
            // Diagnostics must never take the app down.
        }
    }
}
