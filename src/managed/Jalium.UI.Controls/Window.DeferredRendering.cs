using Jalium.UI.Threading;

namespace Jalium.UI;

public partial class Window
{
    private long _deferredRenderGeneration;
    private DispatcherOperation? _deferredRenderOperation;
    private bool _deferredRenderPosted;

    private (Timer? Timer, DispatcherOperation? Operation) DetachDeferredRenderLocked()
    {
        unchecked { _deferredRenderGeneration++; }
        var previous = (_renderThrottleTimer, _deferredRenderOperation);
        _renderThrottleTimer = null;
        _deferredRenderOperation = null;
        _deferredRenderPosted = false;
        return previous;
    }

    private static void ReleaseDeferredRender((Timer? Timer, DispatcherOperation? Operation) previous)
    {
        previous.Timer?.Dispose();
        // Abort removes queued work from Dispatcher instead of retaining a closed
        // window until a later pump. Hooks may re-enter, so call it outside the gate.
        try { previous.Operation?.Abort(); }
        catch (Exception ex)
        {
            // The operation is already removed before abort hooks run. A failing
            // diagnostic hook must not interrupt native window/resource teardown.
            System.Diagnostics.Debug.WriteLine($"Deferred render cancellation hook failed: {ex.Message}");
        }
    }

    private void CancelDeferredRender()
    {
        (Timer? Timer, DispatcherOperation? Operation) previous;
        lock (_renderLifecycleGate) previous = DetachDeferredRenderLocked();
        ReleaseDeferredRender(previous);
    }

    private void OnDeferredRenderTimer(long generation)
    {
        Dispatcher dispatcher;
        lock (_renderLifecycleGate)
        {
            if (generation != _deferredRenderGeneration || _isClosing ||
                _managedTeardownStarted || Handle == nint.Zero || _dispatcher == null || _deferredRenderPosted)
                return;
            _deferredRenderPosted = true;
            dispatcher = _dispatcher;
        }

        DispatcherOperation? operation = null;
        try
        {
            // Posting runs application dispatcher hooks synchronously. Never hold
            // the lifecycle gate here: a hook may marshal back to the UI thread.
            operation = dispatcher.BeginInvokeCritical(() => ProcessDeferredRender(generation));
            operation.Aborted += (_, _) => OnDeferredRenderAborted(generation);
            bool obsolete;
            lock (_renderLifecycleGate)
            {
                obsolete = generation != _deferredRenderGeneration || _isClosing || _managedTeardownStarted;
                if (!obsolete && operation.Status == DispatcherOperationStatus.Pending)
                    _deferredRenderOperation = operation;
            }
            if (obsolete) ReleaseDeferredRender((null, operation));
            else if (operation.Status == DispatcherOperationStatus.Aborted)
                OnDeferredRenderAborted(generation);
        }
        catch
        {
            ReleaseDeferredRender((null, operation));
            OnDeferredRenderAborted(generation);
        }
    }

    private void ProcessDeferredRender(long generation)
    {
        lock (_renderLifecycleGate)
        {
            if (generation != _deferredRenderGeneration || _isClosing || _managedTeardownStarted)
                return;
            _deferredRenderOperation = null;
            _deferredRenderPosted = false;
        }
        ProcessRender();
    }

    private void OnDeferredRenderAborted(long generation)
    {
        lock (_renderLifecycleGate)
        {
            if (generation != _deferredRenderGeneration) return;
            _deferredRenderOperation = null;
            _deferredRenderPosted = false;
            if (_isClosing || _managedTeardownStarted || Handle == nint.Zero ||
                _dispatcher == null || _dispatcher.HasShutdownStarted)
                return;
        }
        // An externally aborted retry must not strand dirty state. Deliberate
        // cancellation/replacement advances the generation and never reaches here.
        InvalidateWindow();
    }
}
