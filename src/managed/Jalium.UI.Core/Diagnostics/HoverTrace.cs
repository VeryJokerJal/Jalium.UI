using System.Text;

namespace Jalium.UI.Diagnostics;

/// <summary>
/// TEMPORARY diagnostic instrumentation for the "title-bar hover freezes rendering"
/// investigation. Enabled with JALIUM_HOVER_TRACE=1; zero overhead otherwise
/// (single static readonly bool branch). Dumps one line of per-second counter
/// deltas + gauges to stderr from a dedicated background thread, so it keeps
/// reporting even when the UI thread / frame chain is stalled.
/// REMOVE AFTER INVESTIGATION.
/// </summary>
public static class HoverTrace
{
    public const int TICK = 0;          // CompositionTarget.OnFrameTick (HRT thread)
    public const int RAISE = 1;         // CompositionTarget.RaiseRendering entered (UI thread)
    public const int REARM = 2;         // CompositionTarget.RearmTimer entered
    public const int REARM_SKIP = 3;    // RearmTimer early-out (!_timerRunning)
    public const int ENQ = 4;           // Dispatcher.EnqueueOperation queued
    public const int POST_FAIL = 5;     // Dispatcher wake PostMessageW returned false
    public const int WAKE = 6;          // Dispatcher.MessageWindowProc WM_DISPATCHER_INVOKE
    public const int PQ = 7;            // Dispatcher.ProcessQueue entered
    public const int PQ_DISABLED = 8;   // ProcessQueue early-out (processing disabled)
    public const int OP = 9;            // Dispatcher operation dispatched
    public const int PR = 10;           // Window.ProcessRender
    public const int RF = 11;           // Window.RenderFrame entered
    public const int SKIP_NODIRTY = 12; // RenderFrame idle skip (no dirty)
    public const int PRESENT = 13;      // successful present
    public const int CREDIT_MISS = 14;  // TryBeginDraw deferred: no present credit
    public const int BEGIN_FAIL = 15;   // TryBeginDraw fence/device fail
    public const int NCMM = 16;         // WM_NCMOUSEMOVE
    public const int NCHT = 17;         // WM_NCHITTEST
    public const int SETCUR = 18;       // WM_SETCURSOR
    public const int MM = 19;           // WM_MOUSEMOVE
    public const int NCHOVER = 20;      // WM_NCMOUSEHOVER
    public const int PAINT = 21;        // WM_PAINT
    public const int POP_INVAL = 22;    // PopupWindow.InvalidateWindow scheduled a ProcessRender
    public const int POP_PR = 23;       // PopupWindow.ProcessRender entered
    public const int POP_RF = 24;       // PopupWindow.RenderFrame body reached (past render-state guard)
    public const int POP_BEGINFAIL = 25;// PopupWindow.TryBeginDraw returned false (present back-pressure)
    public const int POP_PRESENT = 26;  // PopupWindow EndDraw (present) succeeded
    public const int FULL_FRAME = 27;   // Window.RenderFrame ran with fullInvalidation (whole-scene re-raster)
    public const int LAYER_SEEN = 28;   // TryCompositeChildLayer reached a _isCompositorBoundary child
    public const int LAYER_OK = 29;     // boundary composited from cached layer (returned true)
    public const int LAYER_DIRTY = 30;  // boundary bailed at the dirty gate (re-record this frame)
    public const int LAYER_INELIG = 31; // boundary bailed at eligibility/effect/capture-refused
    public const int DRAW_GEO = 32;     // DrawGeometry emitted (→ native FillPath / stencil-then-cover)
    public const int DRAW_TEXT2 = 33;   // DrawText emitted
    public const int DRAW_RECT2 = 34;   // DrawRectangle emitted
    public const int DRAW_IMG = 35;     // DrawImage emitted
    public const int DRAW_COMPOSITE = 36; // CompositeLayer / CompositeLayerClipped emitted (cached quad)
    public const int CB_NOCACHE = 37;   // boundary bailed: !ParticipatesInRenderCache or self has Effect
    public const int CB_EFFECT = 38;    // boundary bailed: SubtreeHasEffect (a descendant carries an effect)
    public const int CB_REFUSED = 39;   // boundary bailed: BeginLayerCapture refused (realized==0)
    public const int CAP_DEFER = 40;    // InvalidateWindow deferred a render by the refresh-rate cap
    private const int SlotCount = 41;

    private static readonly string[] Names =
    {
        "tick", "raise", "rearm", "rearmSkip", "enq", "postFail", "wake", "pq",
        "pqDis", "op", "pr", "rf", "skipND", "present", "creditMiss", "beginFail",
        "ncMM", "ncHT", "setCur", "MM", "ncHover", "paint",
        "popInval", "popPR", "popRF", "popBeginFail", "popPresent", "FULL",
        "lSeen", "lOK", "lDirty", "lInelig",
        "dGeo", "dText", "dRect", "dImg", "dComposite",
        "cbNoCache", "cbEffect", "cbRefused", "capDefer"
    };

    // Gauges (latest value wins)
    public const int G_QDEPTH = 0;      // dispatcher queue depth after ProcessQueue exit
    public const int G_CREDIT = 1;      // _swapCredit at TryBegin
    public const int G_PENDING = 2;     // _renderPendingOnSwap
    public const int G_WAITREG = 3;     // _swapWaitRegistered
    public const int G_TIMER_RUNNING = 4; // CompositionTarget._timerRunning
    public const int G_SUBS = 5;        // CompositionTarget._subscriberCount
    // Per-category GPU time of the last completed frame (microseconds), from the
    // native hardware-timestamp breakdown (GpuTimingStats). Lets a hover trace pin
    // WHICH phase eats the per-present GPU: Other≈clear+barriers+MSAA-resolve,
    // Path≈8×MSAA stencil paths (nav icons), Sdf≈rects, Text≈glyphs.
    public const int G_GPU_TOTAL_US = 6;
    public const int G_GPU_OTHER_US = 7;
    public const int G_GPU_PATH_US = 8;
    public const int G_GPU_SDF_US = 9;
    public const int G_GPU_TEXT_US = 10;
    public const int G_GPU_BITMAP_US = 11;
    public const int G_FRAME_INTERVAL_MS = 12;  // CompositionTarget.FrameIntervalMs (render cap interval)
    private const int GaugeCount = 13;

    private static readonly string[] GaugeNames =
    {
        "qDepth", "credit", "pend", "waitReg", "timerRun", "subs",
        "gpuTotUs", "gpuOtherUs", "gpuPathUs", "gpuSdfUs", "gpuTextUs", "gpuBmpUs",
        "frameIntMs"
    };

    public static readonly bool Enabled = ResolveEnabled();

    private static bool ResolveEnabled()
    {
        var env = Environment.GetEnvironmentVariable("JALIUM_HOVER_TRACE");
        if (env is "1" or "true" or "True")
            return true;

        // Fallback for windowed (WinExe) apps where wiring an env var into the
        // launched process is awkward (VS launch, double-click): enable when a
        // sentinel file named "jalium_hover_trace.on" sits next to the exe or in
        // the working directory. Contents are irrelevant — presence is enough.
        try
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "jalium_hover_trace.on")))
                return true;
            if (System.IO.File.Exists("jalium_hover_trace.on"))
                return true;
        }
        catch
        {
        }

        return false;
    }

    private static readonly long[] s_counters = new long[SlotCount];
    private static readonly long[] s_gauges = new long[GaugeCount];
    private static int s_reporterStarted;

    // stderr is invisible in a windowed (WinExe) app, so mirror every line to a
    // file. Override the path with JALIUM_HOVER_TRACE_FILE; default is the temp dir.
    private static readonly string s_filePath = ResolveFilePath();

    private static string ResolveFilePath()
    {
        var custom = Environment.GetEnvironmentVariable("JALIUM_HOVER_TRACE_FILE");
        if (!string.IsNullOrWhiteSpace(custom))
            return custom;
        return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jalium_hover_trace.log");
    }

    public static void Bump(int slot)
    {
        if (!Enabled) return;
        Interlocked.Increment(ref s_counters[slot]);
        EnsureReporter();
    }

    public static void Gauge(int gauge, long value)
    {
        if (!Enabled) return;
        Interlocked.Exchange(ref s_gauges[gauge], value);
        EnsureReporter();
    }

    private static void EnsureReporter()
    {
        if (Interlocked.CompareExchange(ref s_reporterStarted, 1, 0) != 0) return;
        var t = new Thread(ReportLoop) { IsBackground = true, Name = "Jalium.HoverTrace" };
        t.Start();
    }

    private static void ReportLoop()
    {
        // Emit a header immediately so the file exists and proves the trace is live
        // even before the first per-second dump — critical in a windowed app where
        // stderr goes nowhere visible.
        WriteLine($"[hover-trace] STARTED pid={Environment.ProcessId} file={s_filePath} — per-second counter deltas + gauges follow");

        var prev = new long[SlotCount];
        var sb = new StringBuilder(256);
        while (true)
        {
            Thread.Sleep(1000);
            sb.Clear();
            sb.Append("[hover-trace] ");
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"));
            for (int i = 0; i < SlotCount; i++)
            {
                long cur = Interlocked.Read(ref s_counters[i]);
                long delta = cur - prev[i];
                prev[i] = cur;
                if (delta != 0)
                {
                    sb.Append(' ').Append(Names[i]).Append('=').Append(delta);
                }
            }
            sb.Append(" |");
            for (int g = 0; g < GaugeCount; g++)
            {
                sb.Append(' ').Append(GaugeNames[g]).Append('=').Append(Interlocked.Read(ref s_gauges[g]));
            }
            WriteLine(sb.ToString());
        }
    }

    // Mirror to stderr (visible when launched from a console) AND append to the
    // trace file (the reliable sink for windowed apps). Both are best-effort.
    private static void WriteLine(string line)
    {
        try { Console.Error.WriteLine(line); } catch { }
        try { System.IO.File.AppendAllText(s_filePath, line + Environment.NewLine); } catch { }
    }
}
