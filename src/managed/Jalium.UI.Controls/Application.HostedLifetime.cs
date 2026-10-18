using Jalium.UI.Threading;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Jalium.UI;

public partial class Application
{
    private ConditionalWeakTable<Window, object>? _hostedWindows;
    private static readonly object HostedWindowMarker = new();
    private Window[]? _hostedPendingWindows;
    private DispatcherTimer? _hostedStopTimer;
    private Action? _hostedCleanupCompleted;
    private int _hostedCleanupFinished;

    internal bool IsHostedStopping => Volatile.Read(ref _hostedStopState) != 0 || Volatile.Read(ref _cleanupStarted) != 0;

    internal void TrackHostedWindow(Window window)
    {
        if (!OperatingSystem.IsMacOS() || !ReferenceEquals(_current, this)) return;
        if (IsHostedStopping)
            throw new InvalidOperationException("A stopping application cannot create or adopt a window.");
        window.SetHostedApplicationOwner(this);
        (_hostedWindows ??= new()).GetValue(window, static _ => HostedWindowMarker);
    }

    private bool HasPendingHostedWindowTeardown => _hostedPendingWindows?.Any(window =>
        window.IsOwnedByHostedApplication(this) && window.HasPendingHostedTeardown) == true;

    internal bool IsHostedCleanupPending => IsHostedStopping && Volatile.Read(ref _hostedCleanupFinished) == 0;

    internal bool DeferUntilHostedCleanupCompleted(Action completed)
    {
        if (!IsHostedCleanupPending) return false;
        _hostedCleanupCompleted += completed;
        return true;
    }

    private void ScheduleHostedCleanup()
    {
        _hostedStopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _hostedStopTimer.Tick += (_, _) =>
        {
            if (HasPendingHostedWindowTeardown) return;
            ExceptionDispatchInfo? failure = null;
            FinishHostedCleanup(ref failure);
            if (failure != null)
                System.Diagnostics.Debug.WriteLine($"Jalium.UI: deferred hosted cleanup failed: {failure.SourceException}");
        };
        _hostedStopTimer.Start();
    }

    private void FinishHostedCleanup(ref ExceptionDispatchInfo? failure)
    {
        _hostedStopTimer?.Stop();
        _hostedStopTimer = null;
        _hostedPendingWindows = null;
        HostedLifecycleCleanup.Attempt(Cleanup, ref failure);
        Volatile.Write(ref _hostedCleanupFinished, 1);
        var completed = _hostedCleanupCompleted;
        _hostedCleanupCompleted = null;
        if (completed != null) HostedLifecycleCleanup.Attempt(completed, ref failure);
    }

    private void CloseHostedWindows()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var membership = Interlocked.Exchange(ref _hostedWindows, null);
        if (membership == null) return;
        ExceptionDispatchInfo? failure = null;
        // Weak membership covers unshown/pending windows without retaining views
        // otherwise kept only by application code. An explicit new owner wins.
        var windows = membership.Select(pair => pair.Key)
            .Where(window => window.IsOwnedByHostedApplication(this))
            .OrderByDescending(Depth).ToArray();
        _hostedPendingWindows = windows;
        try
        {
            foreach (var window in windows)
                if (window.IsOwnedByHostedApplication(this))
                    HostedLifecycleCleanup.Attempt(window.CloseForHostedApplicationShutdown, ref failure);
        }
        finally { membership.Clear(); }
        failure?.Throw();

        static int Depth(Window window)
        {
            int depth = 0;
            for (var owner = window.Owner; owner != null && depth < 2048; owner = owner.Owner) depth++;
            return depth;
        }
    }
}

internal static class HostedLifecycleCleanup
{
    internal static void Attempt(Action action, ref ExceptionDispatchInfo? failure, bool suppress = false)
    {
        try { action(); }
        catch (Exception error)
        {
            if (failure == null && !suppress) failure = ExceptionDispatchInfo.Capture(error);
            else System.Diagnostics.Debug.WriteLine($"Jalium.UI: hosted cleanup failed: {error}");
        }
    }
}
