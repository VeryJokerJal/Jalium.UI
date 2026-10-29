using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Threading;

namespace Jalium.UI.StartupAllocationProbe;

internal static partial class Program
{
    private const int ExitFailure = 1;
    private static readonly TimeSpan StableInterval = TimeSpan.FromSeconds(10);

    private static Application? s_application;
    private static Window? s_window;
    private static DispatcherTimer? s_stableTimer;
    private static Exception? s_callbackFailure;
    private static string s_phase = "entry";

    private static GcSnapshot s_windowBefore;
    private static GcSnapshot s_windowAfter;
    private static GcSnapshot s_windowShown;
    private static GcSnapshot s_stable;
    private static bool s_shownReached;
    private static bool s_stableReached;

    [STAThread]
    private static int Main(string[] args)
    {
        NativeShellMode nativeShellMode = GetNativeShellMode(args);
        if (nativeShellMode == NativeShellMode.Conflict)
        {
            WriteNativeModeConflict();
            return ExitUsage;
        }

        if (nativeShellMode != NativeShellMode.None)
        {
            return RunNativeShellMode(nativeShellMode);
        }

        GcSnapshot entry = Capture();

        try
        {
            GcSnapshot applicationBefore = Capture();
            s_phase = "application_construct";
            var application = new Application();
            s_application = application;
            GcSnapshot applicationAfter = Capture();

            s_phase = "theme_verify";
            int themeDictionaryCount = application.Resources.MergedDictionaries.Count;
            bool hasButtonResource = application.Resources.Contains(typeof(Button));
            if (themeDictionaryCount < 3 || !hasButtonResource)
            {
                throw new InvalidOperationException(
                    "The complete desktop theme was not loaded, including the Button resource.");
            }

            GcSnapshot themeVerified = Capture();

            // Emit only after all application/theme snapshots are captured so the
            // marker formatting is not charged to those diagnostic intervals.
            WriteEntry(entry);
            WriteStage("application_before", applicationBefore, entry);
            WriteStage("application_after", applicationAfter, applicationBefore);
            WriteThemeStage(themeVerified, applicationAfter, themeDictionaryCount, hasButtonResource);

            s_phase = "window_construct";
            s_windowBefore = Capture();
            var window = new Window
            {
                Title = "Jalium.UI Startup Allocation Probe",
                Width = 800,
                Height = 600,
                Content = null,
            };
            s_window = window;
            s_windowAfter = Capture();

            window.Shown += OnWindowShown;

            s_phase = "application_run";
            int runCode = application.Run(window, args);
            s_phase = "run_return";
            s_stableTimer?.Stop();

            GcSnapshot runReturned = Capture();
            if (!s_shownReached)
            {
                // Preserve constructor evidence even when the native show path exits early.
                WriteStage("window_before", s_windowBefore, previous: null);
                WriteStage("window_after", s_windowAfter, s_windowBefore);
            }

            GcSnapshot previous = s_stableReached
                ? s_stable
                : s_shownReached
                    ? s_windowShown
                    : s_windowAfter;
            WriteRunReturnStage(runReturned, previous, runCode);

            if (s_callbackFailure is not null)
            {
                WriteError("callback", s_callbackFailure);
                return ExitFailure;
            }

            if (!s_stableReached)
            {
                WriteError(
                    "early_exit",
                    new InvalidOperationException(
                        "Application.Run returned before the stable_10s stage completed."));
                return ExitFailure;
            }

            return runCode;
        }
        catch (Exception ex)
        {
            s_stableTimer?.Stop();
            WriteFailureStage(Capture(), s_phase);
            WriteError("unhandled", ex);
            return ExitFailure;
        }
    }

    private static void OnWindowShown(object? sender, EventArgs e)
    {
        if (s_shownReached)
        {
            return;
        }

        try
        {
            s_phase = "window_shown";
            s_windowShown = Capture();
            s_shownReached = true;

            // These three snapshots were taken before any of these markers were
            // formatted, keeping the constructor and first-show deltas isolated.
            WriteStage("window_before", s_windowBefore, previous: null);
            WriteStage("window_after", s_windowAfter, s_windowBefore);
            WriteStage("window_shown", s_windowShown, s_windowAfter);

            s_stableTimer = new DispatcherTimer
            {
                Interval = StableInterval,
            };
            s_stableTimer.Tick += OnStableTimerTick;
            s_stableTimer.Start();
            s_phase = "stable_wait";
        }
        catch (Exception ex)
        {
            FailFromCallback("shown", ex);
        }
    }

    private static void OnStableTimerTick(object? sender, EventArgs e)
    {
        s_stableTimer?.Stop();

        try
        {
            s_phase = "stable_10s";
            s_stable = Capture();
            s_stableReached = true;
            WriteStage("stable_10s", s_stable, s_windowShown);

            s_phase = "normal_close";
            (s_window ?? throw new InvalidOperationException("The probe window is unavailable.")).Close();
        }
        catch (Exception ex)
        {
            FailFromCallback("stable", ex);
        }
    }

    private static void FailFromCallback(string category, Exception exception)
    {
        s_stableTimer?.Stop();
        s_callbackFailure ??= exception;
        WriteFailureStage(Capture(), s_phase);
        WriteError(category, exception);

        try
        {
            s_window?.Close();
        }
        catch (Exception closeException)
        {
            WriteError("close_after_failure", closeException);
            s_application?.Shutdown(ExitFailure);
        }
    }

    private static GcSnapshot Capture()
    {
        return new GcSnapshot(
            GC.GetTotalAllocatedBytes(precise: false),
            GC.GetTotalMemory(forceFullCollection: false),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }

    private static void WriteEntry(GcSnapshot snapshot)
    {
        Console.WriteLine(
            $"## STARTUP_ALLOC stage=entry pid={Environment.ProcessId} size=800x600 content=null stableMs=10000 " +
            $"diagnosticOnly=true samplingOverhead=true budgetProbe=false " +
            $"allocatedBytes={snapshot.AllocatedBytes} deltaAllocatedBytes=0 " +
            $"heapBytes={snapshot.HeapBytes} deltaHeapBytes=0 " +
            $"gen0={snapshot.Gen0Collections} gen1={snapshot.Gen1Collections} gen2={snapshot.Gen2Collections}");
        Console.Out.Flush();
    }

    private static void WriteThemeStage(
        GcSnapshot snapshot,
        GcSnapshot previous,
        int dictionaryCount,
        bool hasButtonResource)
    {
        Console.WriteLine(
            $"## STARTUP_ALLOC stage=theme_verified dictionaries={dictionaryCount} " +
            $"buttonResource={hasButtonResource.ToString().ToLowerInvariant()} " +
            $"allocatedBytes={snapshot.AllocatedBytes} " +
            $"deltaAllocatedBytes={snapshot.AllocatedBytes - previous.AllocatedBytes} " +
            $"heapBytes={snapshot.HeapBytes} deltaHeapBytes={snapshot.HeapBytes - previous.HeapBytes} " +
            $"gen0={snapshot.Gen0Collections} gen1={snapshot.Gen1Collections} gen2={snapshot.Gen2Collections}");
        Console.Out.Flush();
    }

    private static void WriteRunReturnStage(GcSnapshot snapshot, GcSnapshot previous, int runCode)
    {
        Console.WriteLine(
            $"## STARTUP_ALLOC stage=run_return runCode={runCode} shown={s_shownReached.ToString().ToLowerInvariant()} " +
            $"stable={s_stableReached.ToString().ToLowerInvariant()} " +
            $"allocatedBytes={snapshot.AllocatedBytes} " +
            $"deltaAllocatedBytes={snapshot.AllocatedBytes - previous.AllocatedBytes} " +
            $"heapBytes={snapshot.HeapBytes} deltaHeapBytes={snapshot.HeapBytes - previous.HeapBytes} " +
            $"gen0={snapshot.Gen0Collections} gen1={snapshot.Gen1Collections} gen2={snapshot.Gen2Collections}");
        Console.Out.Flush();
    }

    private static void WriteFailureStage(GcSnapshot snapshot, string phase)
    {
        Console.Error.WriteLine(
            $"## STARTUP_ALLOC stage=failure_exit phase={phase} " +
            $"allocatedBytes={snapshot.AllocatedBytes} heapBytes={snapshot.HeapBytes} " +
            $"gen0={snapshot.Gen0Collections} gen1={snapshot.Gen1Collections} gen2={snapshot.Gen2Collections}");
        Console.Error.Flush();
    }

    private static void WriteStage(string stage, GcSnapshot snapshot, GcSnapshot? previous)
    {
        long deltaAllocatedBytes = previous is { } prior
            ? snapshot.AllocatedBytes - prior.AllocatedBytes
            : 0;
        long deltaHeapBytes = previous is { } priorHeap
            ? snapshot.HeapBytes - priorHeap.HeapBytes
            : 0;

        Console.WriteLine(
            $"## STARTUP_ALLOC stage={stage} allocatedBytes={snapshot.AllocatedBytes} " +
            $"deltaAllocatedBytes={deltaAllocatedBytes} heapBytes={snapshot.HeapBytes} " +
            $"deltaHeapBytes={deltaHeapBytes} gen0={snapshot.Gen0Collections} " +
            $"gen1={snapshot.Gen1Collections} gen2={snapshot.Gen2Collections}");
        Console.Out.Flush();
    }

    private static void WriteError(string category, Exception exception)
    {
        Console.Error.WriteLine($"## STARTUP_ALLOC ERROR category={category} phase={s_phase} {exception}");
        Console.Error.Flush();
    }

    private readonly record struct GcSnapshot(
        long AllocatedBytes,
        long HeapBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections);
}
