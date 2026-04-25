using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;

namespace Jalium.UI.Hosting;

/// <summary>
/// Framework-owned <see cref="Meter"/> exposing frame-time, FPS, and frame-count
/// instruments for observability tools (OpenTelemetry, <c>dotnet-counters</c>,
/// Prometheus exporters, etc.).
/// </summary>
/// <remarks>
/// <para>
/// The meter is dormant by default — no handlers are attached and no per-frame
/// work is done. Call <see cref="Start"/> (typically via
/// <c>app.UseJaliumMetrics()</c> on the built <see cref="JaliumApp"/>, or
/// <c>builder.Services.AddJaliumMetrics()</c> when you want the service
/// registration without auto-starting) to begin collection. The meter attaches
/// to the existing
/// <see cref="CompositionTarget.Rendering"/> event — it does <em>not</em> force
/// the frame timer on, so an idle application with no visible window still
/// produces no samples.
/// </para>
/// <para>
/// The meter name is <see cref="MeterName"/> (<c>Jalium.UI</c>). Instruments:
/// <list type="bullet">
///   <item><c>jalium.frame.duration</c> — <see cref="Histogram{T}"/>, milliseconds between successive <c>Rendering</c> events</item>
///   <item><c>jalium.frame.count</c> — <see cref="Counter{T}"/>, total frames since <see cref="Start"/></item>
///   <item><c>jalium.fps</c> — <see cref="ObservableGauge{T}"/>, rolling FPS average</item>
///   <item><c>jalium.refresh_rate</c> — <see cref="ObservableGauge{T}"/>, detected monitor refresh rate (Hz)</item>
/// </list>
/// </para>
/// </remarks>
public static class JaliumMeter
{
    /// <summary>Name of the framework <see cref="Meter"/>.</summary>
    public const string MeterName = "Jalium.UI";

    private static readonly Meter s_meter = new(MeterName, typeof(JaliumMeter).Assembly.GetName().Version?.ToString() ?? "1.0.0");

    private static readonly Histogram<double> s_frameDuration =
        s_meter.CreateHistogram<double>("jalium.frame.duration", "ms", "Milliseconds between consecutive Rendering events.");

    private static readonly Counter<long> s_frameCount =
        s_meter.CreateCounter<long>("jalium.frame.count", "frame", "Total frames rendered since metrics were started.");

    // Observable instruments are retained so the Meter runtime can poll them;
    // they have no C# readers, which is expected for pull-mode gauges.
    [SuppressMessage("Performance", "CA1823:Avoid unused private fields", Justification = "Retained to keep ObservableGauge registered with the Meter.")]
    private static readonly ObservableGauge<double> s_fpsGauge =
        s_meter.CreateObservableGauge("jalium.fps", () => CurrentFps, "frame/s", "Rolling FPS average over the last N frames.");

    [SuppressMessage("Performance", "CA1823:Avoid unused private fields", Justification = "Retained to keep ObservableGauge registered with the Meter.")]
    private static readonly ObservableGauge<int> s_refreshRateGauge =
        s_meter.CreateObservableGauge("jalium.refresh_rate", () => CompositionTarget.RefreshRate, "Hz", "Detected monitor refresh rate.");

    private static readonly object s_lock = new();
    private static int s_started;
    private static EventHandler? s_handler;

    private static long s_lastFrameTimestamp;
    private static double[] s_fpsWindow = new double[60];
    private static int s_fpsWindowIndex;
    private static int s_fpsWindowFilled;

    /// <summary>
    /// The framework-owned <see cref="Meter"/>. Use to attach additional
    /// custom instruments tagged with the <c>Jalium.UI</c> meter name.
    /// </summary>
    public static Meter Meter => s_meter;

    /// <summary>
    /// Last measured frame interval in milliseconds. Returns <c>0</c> while the
    /// meter is stopped.
    /// </summary>
    public static double LastFrameDurationMs { get; private set; }

    /// <summary>
    /// Current rolling-average FPS computed over the most recent
    /// <see cref="JaliumMetricsOptions.FpsWindowFrames"/> frames.
    /// </summary>
    public static double CurrentFps { get; private set; }

    /// <summary>
    /// <see langword="true"/> while the meter is attached to the rendering event.
    /// </summary>
    public static bool IsRunning => Volatile.Read(ref s_started) == 1;

    /// <summary>
    /// Starts per-frame metric collection. Safe to call multiple times — the
    /// second and subsequent calls are no-ops until a matching <see cref="Stop"/>.
    /// </summary>
    /// <param name="fpsWindowFrames">
    /// Number of recent frames averaged into <see cref="CurrentFps"/>. Must be
    /// at least 1.
    /// </param>
    public static void Start(int fpsWindowFrames = 60)
    {
        if (fpsWindowFrames < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fpsWindowFrames), fpsWindowFrames, "FPS window must be at least one frame.");
        }

        lock (s_lock)
        {
            if (s_started == 1)
            {
                return;
            }

            if (s_fpsWindow.Length != fpsWindowFrames)
            {
                s_fpsWindow = new double[fpsWindowFrames];
            }
            else
            {
                Array.Clear(s_fpsWindow);
            }
            s_fpsWindowIndex = 0;
            s_fpsWindowFilled = 0;
            s_lastFrameTimestamp = 0;
            LastFrameDurationMs = 0;
            CurrentFps = 0;

            s_handler = OnRendering;
            CompositionTarget.Rendering += s_handler;
            s_started = 1;
        }
    }

    /// <summary>
    /// Stops per-frame metric collection. Safe to call when not running.
    /// </summary>
    public static void Stop()
    {
        lock (s_lock)
        {
            if (s_started == 0)
            {
                return;
            }

            if (s_handler != null)
            {
                CompositionTarget.Rendering -= s_handler;
                s_handler = null;
            }
            s_started = 0;
        }
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var previous = s_lastFrameTimestamp;
        s_lastFrameTimestamp = now;

        if (previous == 0)
        {
            // First frame after Start() — no baseline, skip sampling.
            return;
        }

        var elapsedMs = (now - previous) * 1000.0 / Stopwatch.Frequency;
        if (elapsedMs <= 0)
        {
            return;
        }

        s_frameDuration.Record(elapsedMs);
        s_frameCount.Add(1);
        LastFrameDurationMs = elapsedMs;

        // Rolling-average FPS over the configured window.
        s_fpsWindow[s_fpsWindowIndex] = elapsedMs;
        s_fpsWindowIndex = (s_fpsWindowIndex + 1) % s_fpsWindow.Length;
        if (s_fpsWindowFilled < s_fpsWindow.Length)
        {
            s_fpsWindowFilled++;
        }

        double sum = 0;
        for (int i = 0; i < s_fpsWindowFilled; i++)
        {
            sum += s_fpsWindow[i];
        }
        var averageMs = sum / s_fpsWindowFilled;
        CurrentFps = averageMs > 0 ? 1000.0 / averageMs : 0;
    }
}
