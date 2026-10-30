using System.Runtime.CompilerServices;

namespace Jalium.UI.MemoryProbe;

internal static partial class Program
{
    // Created only for explicitly requested diagnostic runs. These weak references
    // cannot keep closed windows or their visual trees alive.
    private static List<WeakReference<Window>>? _diagnosticWindows;
    private static long _nextLifetimeDiagnosticMilliseconds = 1_000;

    private static void TrackLifetimeDiagnostics(Window window)
    {
        if (!_options.LifetimeDiagnostics)
            return;

        (_diagnosticWindows ??= []).Add(new WeakReference<Window>(window));
    }

    private static void ReportPeriodicLifetimeDiagnostics(long elapsedMilliseconds)
    {
        if (!_options.LifetimeDiagnostics || elapsedMilliseconds < _nextLifetimeDiagnosticMilliseconds)
            return;

        _nextLifetimeDiagnosticMilliseconds = elapsedMilliseconds + 1_000;
        ReportLifetimeDiagnostics("tick");
    }

    private static void ReportLifetimeDiagnostics(string reason)
    {
        if (!_options.LifetimeDiagnostics)
            return;

        var info = GC.GetGCMemoryInfo();
        var counts = CountDiagnosticWindows();
        long lastPauseTicks = 0;
        foreach (var pause in info.PauseDurations)
            lastPauseTicks += pause.Ticks;
        Marker($"LIFETIME reason={reason} elapsedMs={_actionClock?.ElapsedMilliseconds ?? 0} " +
            $"created={_diagnosticWindows?.Count ?? 0} open={OpenWindows.Count} " +
            $"alive={counts.Alive} closedAlive={counts.ClosedAlive} " +
            $"gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)} " +
            $"managedBytes={GC.GetTotalMemory(forceFullCollection: false)} " +
            $"allocatedBytes={GC.GetTotalAllocatedBytes(precise: false)} " +
            $"lastGcIndex={info.Index} lastGcHeapBytes={info.HeapSizeBytes} " +
            $"lastGcCommittedBytes={info.TotalCommittedBytes} lastGcFragmentedBytes={info.FragmentedBytes} " +
            $"lastGcPauseMicroseconds={lastPauseTicks / 10} " +
            $"gcPauseTimePercentage={info.PauseTimePercentage.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (int Alive, int ClosedAlive) CountDiagnosticWindows()
    {
        int alive = 0;
        int closedAlive = 0;
        if (_diagnosticWindows != null)
        {
            foreach (var reference in _diagnosticWindows)
            {
                if (reference.TryGetTarget(out var window))
                {
                    alive++;
                    if (!OpenWindows.Contains(window))
                        closedAlive++;
                }
            }
        }

        return (alive, closedAlive);
    }
}
