using Jalium.UI.Input;
using Jalium.UI.Threading;

namespace Jalium.UI;

public partial class Window
{
    private sealed class InputDetachAttempt(Window owner, long generation)
    {
        internal long Generation { get; } = generation;
        internal DispatcherOperation? Operation;
        internal bool PostInProgress = true;
        internal bool CallbackRanDuringPost;

        internal void Track(DispatcherOperation operation)
        {
            operation.Aborted += OnFinished;
            operation.Completed += OnFinished;
        }

        internal void StopTracking(DispatcherOperation operation)
        {
            operation.Aborted -= OnFinished;
            operation.Completed -= OnFinished;
        }

        private void OnFinished(object? sender, EventArgs e)
        {
            if (sender is DispatcherOperation operation)
                owner.OnInputDetachOperationFinished(this, operation);
        }
    }

    private readonly object _inputDetachLifetimeGate = new();
    private HashSet<UIElement>? _pendingInputDetachRoots;
    private DispatcherOperation? _inputDetachOperation;
    private InputDetachAttempt? _inputDetachAttempt;
    private long _inputDetachGeneration;
    private int _inputDetachPostDepth;
    private volatile bool _inputDetachFrameFallbackPending;

    void IInputTreeLifetimeHost.OnInputSubtreeDetached(UIElement subtree)
    {
        if (_isClosing || _managedTeardownStarted) return;
        if (_hitMemoElement != null && Controls.WindowInputDispatcher.IsDescendantOf(_hitMemoElement, subtree))
        {
            _hitMemoElement = null;
            _hitMemoLayoutGeneration = -1;
        }
        if (!_inputDispatcher.HasInputStateInSubtree(subtree)) return;

        InputDetachAttempt attempt;
        lock (_inputDetachLifetimeGate)
        {
            (_pendingInputDetachRoots ??= []).Add(subtree);
            if (_inputDetachAttempt != null || _inputDetachFrameFallbackPending) return;
            attempt = new InputDetachAttempt(this, unchecked(++_inputDetachGeneration));
            _inputDetachAttempt = attempt;
            _inputDetachPostDepth++;
        }

        DispatcherOperation? operation = null;
        try
        {
            // OperationPosted may synchronously pump or abort the operation.
            // Publish only a pending handle, and keep user hooks outside the gate.
            operation = Dispatcher.BeginInvoke(DispatcherPriority.Input,
                () => ProcessInputDetachChecks(attempt));
            attempt.Operation = operation;
            attempt.Track(operation);
            bool obsolete;
            bool requestFrame = false;
            lock (_inputDetachLifetimeGate)
            {
                obsolete = !IsCurrentInputDetachAttemptLocked(attempt);
                attempt.PostInProgress = false;
                if (!obsolete)
                {
                    if (attempt.CallbackRanDuringPost || operation.Task.IsCompleted ||
                        operation.Status != DispatcherOperationStatus.Pending)
                    {
                        RetireInputDetachAttemptLocked(attempt);
                        requestFrame = ArmInputDetachFrameFallbackLocked();
                    }
                    else
                    {
                        _inputDetachOperation = operation;
                    }
                }
            }

            if (obsolete)
            {
                attempt.StopTracking(operation);
                TryAbortInputDetachOperation(operation);
            }
            else if (requestFrame)
            {
                attempt.StopTracking(operation);
                RequestInputDetachFrameFallback();
            }
            else if (operation.Task.IsCompleted)
            {
                OnInputDetachOperationFinished(attempt, operation);
            }
        }
        catch
        {
            // A throwing OperationPosted hook can leave an inserted node whose
            // handle never reached this caller. Its invalidated attempt is inert.
            if (operation != null) attempt.StopTracking(operation);
            bool requestFrame = false;
            lock (_inputDetachLifetimeGate)
            {
                if (IsCurrentInputDetachAttemptLocked(attempt))
                {
                    RetireInputDetachAttemptLocked(attempt);
                    requestFrame = ArmInputDetachFrameFallbackLocked();
                }
            }
            if (operation != null) TryAbortInputDetachOperation(operation);
            if (requestFrame) RequestInputDetachFrameFallback();
        }
        finally
        {
            lock (_inputDetachLifetimeGate) _inputDetachPostDepth--;
        }
    }

    private void ProcessInputDetachChecks(InputDetachAttempt attempt)
    {
        HashSet<UIElement>? roots;
        lock (_inputDetachLifetimeGate)
        {
            if (!IsCurrentInputDetachAttemptLocked(attempt)) return;
            if (attempt.PostInProgress || _inputDetachPostDepth != 0)
            {
                // Same-stack pumping is too early to decide whether a subtree
                // was moved within this window. The next frame rechecks it.
                attempt.CallbackRanDuringPost = true;
                return;
            }
            roots = _pendingInputDetachRoots;
            _pendingInputDetachRoots = null;
            RetireInputDetachAttemptLocked(attempt);
        }
        if (attempt.Operation is { } operation) attempt.StopTracking(operation);
        ProcessDetachedInputRoots(roots);
    }

    private void OnInputDetachOperationFinished(InputDetachAttempt attempt, DispatcherOperation operation)
    {
        attempt.StopTracking(operation);
        bool requestFrame;
        lock (_inputDetachLifetimeGate)
        {
            if (attempt.PostInProgress || !IsCurrentInputDetachAttemptLocked(attempt) ||
                !ReferenceEquals(_inputDetachOperation, operation)) return;
            RetireInputDetachAttemptLocked(attempt);
            requestFrame = ArmInputDetachFrameFallbackLocked();
        }
        if (requestFrame) RequestInputDetachFrameFallback();
    }

    private bool ArmInputDetachFrameFallbackLocked()
    {
        if (_isClosing || _managedTeardownStarted || _inputDetachAttempt != null ||
            _pendingInputDetachRoots is not { Count: > 0 } || _inputDetachFrameFallbackPending)
            return false;
        _inputDetachFrameFallbackPending = true;
        return true;
    }

    private void RequestInputDetachFrameFallback()
    {
        // RequestFrame can also run a synchronous dispatcher hook. A nested
        // frame must not retire input before same-batch reparenting can finish.
        lock (_inputDetachLifetimeGate) _inputDetachPostDepth++;
        try { Media.CompositionTarget.RequestFrame(); }
        finally { lock (_inputDetachLifetimeGate) _inputDetachPostDepth--; }
    }

    private void ProcessPendingInputDetachesForFrame()
    {
        if (!_inputDetachFrameFallbackPending) return;
        HashSet<UIElement>? roots;
        lock (_inputDetachLifetimeGate)
        {
            if (!_inputDetachFrameFallbackPending || _isClosing || _managedTeardownStarted ||
                _inputDetachAttempt != null || _inputDetachPostDepth != 0) return;
            _inputDetachFrameFallbackPending = false;
            roots = _pendingInputDetachRoots;
            _pendingInputDetachRoots = null;
            unchecked { _inputDetachGeneration++; }
        }
        ProcessDetachedInputRoots(roots);
    }

    private void ProcessDetachedInputRoots(HashSet<UIElement>? roots)
    {
        if (roots == null) return;
        foreach (var root in roots)
        {
            if (_isClosing || _managedTeardownStarted) return;
            if (!IsElementAttachedToThisWindow(root))
                _inputDispatcher.HandleSubtreeDetached(root);
        }
    }

    private bool IsCurrentInputDetachAttemptLocked(InputDetachAttempt attempt) =>
        ReferenceEquals(_inputDetachAttempt, attempt) && attempt.Generation == _inputDetachGeneration;

    private void RetireInputDetachAttemptLocked(InputDetachAttempt attempt)
    {
        if (!IsCurrentInputDetachAttemptLocked(attempt)) return;
        _inputDetachAttempt = null;
        _inputDetachOperation = null;
        unchecked { _inputDetachGeneration++; }
    }

    private static void TryAbortInputDetachOperation(DispatcherOperation operation)
    {
        try { operation.Abort(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Input detach cancellation hook failed: {ex.Message}"); }
    }

    private void CancelPendingInputDetachChecks()
    {
        DispatcherOperation? operation;
        InputDetachAttempt? attempt;
        lock (_inputDetachLifetimeGate)
        {
            unchecked { _inputDetachGeneration++; }
            attempt = _inputDetachAttempt;
            operation = _inputDetachOperation ?? attempt?.Operation;
            _inputDetachAttempt = null;
            _inputDetachOperation = null;
            _pendingInputDetachRoots = null;
            _inputDetachFrameFallbackPending = false;
        }
        if (operation != null)
        {
            attempt?.StopTracking(operation);
            TryAbortInputDetachOperation(operation);
        }
    }
}
