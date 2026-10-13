using System.Diagnostics;

namespace Jalium.UI.Controls.Platform;

internal enum MacOSWindowCloseResult { Cancelled, Complete, Pending }

/// <summary>Coordinates an AppKit quit request with cancellable Window closing.</summary>
internal sealed class MacOSWindowCloseRequest : IDisposable
{
    private readonly Application _application;
    private readonly Action<bool> _reply;
    private IDisposable? _shutdownDeferral;
    private Window[] _windows = [];
    private int _nextWindow;
    private Window? _waitingForCloseDecision;
    private bool _begun;
    private bool _requesting;
    private bool _disposed;

    internal MacOSWindowCloseResult Result { get; private set; } = MacOSWindowCloseResult.Pending;
    internal bool IsWaitingForCloseDecision => _waitingForCloseDecision != null;

    internal MacOSWindowCloseRequest(Application application, Action<bool> reply)
    {
        _application = application;
        _reply = reply;
    }

    internal MacOSWindowCloseResult Begin()
    {
        if (_begun || _disposed) return Result;
        _begun = true;
        // An accepted owner close force-closes its owned windows. Ask the
        // deepest children first so each can cancel the application quit
        // before that ownership rule takes effect. Keep creation order for
        // unrelated windows and include the owner inferred by ShowDialog.
        _windows = _application.Windows.Cast<Window>()
            .OrderByDescending(GetOwnershipDepth).ToArray();
        // Closing a main/last window must not stop AppKit while another Window
        // can still cancel this quit or owns a render callback being unwound.
        _shutdownDeferral = _application.DeferWindowCloseShutdown();
        foreach (var window in _windows)
        {
            window.PlatformTeardownCompleted += OnWindowCloseProgress;
            window.PlatformCloseRequestCompleted += OnWindowCloseProgress;
        }
        return AdvanceCloseRequests();
    }

    private MacOSWindowCloseResult AdvanceCloseRequests()
    {
        _requesting = true;
        try
        {
            if (_waitingForCloseDecision is { } waiting)
            {
                if (waiting.IsCloseDecisionPendingForPlatformTermination) return Result;
                _waitingForCloseDecision = null;
                if (!waiting.IsCloseRequestedForPlatformTermination)
                {
                    Result = MacOSWindowCloseResult.Cancelled;
                    DisposeWhenAcceptedWindowsFinish();
                    return Result;
                }
                _nextWindow++;
            }
            while (_nextWindow < _windows.Length)
            {
                var window = _windows[_nextWindow];
                // A normal Close may still be running the callback that started
                // this quit. Its _isClosing flag is not an accepted decision yet.
                if (window.IsCloseDecisionPendingForPlatformTermination)
                {
                    _waitingForCloseDecision = window;
                    return Result;
                }
                window.Close();
                if (!window.IsCloseRequestedForPlatformTermination)
                {
                    Result = MacOSWindowCloseResult.Cancelled;
                    DisposeWhenAcceptedWindowsFinish();
                    return Result;
                }
                _nextWindow++;
            }
            if (_application.Windows.Cast<Window>().Any(window => !_windows.Contains(window)))
            {
                Result = MacOSWindowCloseResult.Cancelled;
                DisposeWhenAcceptedWindowsFinish();
            }
            else if (_windows.All(window => window.IsClosedForPlatformTermination))
            {
                Result = MacOSWindowCloseResult.Complete;
                Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Window closing callback failed during macOS quit: {ex.Message}");
            Result = _windows.All(window => window.IsClosedForPlatformTermination) && _application.Windows.Count == 0
                ? MacOSWindowCloseResult.Complete : MacOSWindowCloseResult.Cancelled;
            DisposeWhenAcceptedWindowsFinish();
        }
        finally { _requesting = false; }
        return Result;
    }

    private static int GetOwnershipDepth(Window window)
    {
        HashSet<Window> visited = [window];
        int depth = 0;
        for (var owner = window.OwnerForPlatformTermination;
             owner != null && visited.Add(owner);
             owner = owner.OwnerForPlatformTermination)
            depth++;
        return depth;
    }

    private void OnWindowCloseProgress(object? sender, EventArgs e)
    {
        if (_requesting || _disposed) return;
        if (Result == MacOSWindowCloseResult.Cancelled)
        {
            DisposeWhenAcceptedWindowsFinish();
            return;
        }
        var result = AdvanceCloseRequests();
        if (result != MacOSWindowCloseResult.Pending)
            _reply(result == MacOSWindowCloseResult.Complete);
    }

    private void DisposeWhenAcceptedWindowsFinish()
    {
        // Cancellation does not undo closes already accepted by earlier windows.
        // Their render callbacks may still be releasing resources. Keep automatic
        // shutdown deferred through those closes so they cannot override Cancel.
        if (_windows.All(window => !window.IsCloseRequestedForPlatformTermination || window.IsClosedForPlatformTermination))
            Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var window in _windows)
        {
            window.PlatformTeardownCompleted -= OnWindowCloseProgress;
            window.PlatformCloseRequestCompleted -= OnWindowCloseProgress;
        }
        _waitingForCloseDecision = null;
        _shutdownDeferral?.Dispose();
        _shutdownDeferral = null;
    }
}
