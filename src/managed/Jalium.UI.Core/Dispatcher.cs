using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Jalium.UI.Core.Platform;
using Jalium.UI.Threading;

namespace Jalium.UI;

/// <summary>
/// Provides services for managing the queue of work items for a thread.
/// </summary>
public sealed partial class Dispatcher : IDisposable
{
    private static readonly ThreadLocal<Dispatcher?> _currentDispatcher = new();
    private static Dispatcher? _mainDispatcher;
    private static readonly object _lock = new();
    private static readonly ConcurrentDictionary<Thread, Dispatcher> _dispatchers = new();

    private readonly Thread _thread;
    private readonly uint _threadId;

    // WPF 语义：调度队列按优先级出队（高优先级先执行），同优先级内 FIFO。
    // 用一个插入有序的链表 + 出队时扫描最高优先级实现：节点小、改优先级/中止天然支持。
    // 现有所有 enqueue 一律走 DispatcherPriority.Normal，因此对既有调用者而言出队顺序与
    // 旧的单一 FIFO 队列完全一致（优先级排序是其严格超集）。
    private readonly LinkedList<DispatcherOperation> _queue = new();
    private readonly object _instanceLock = new();
    private readonly ManualResetEventSlim _workAvailable = new(false);

    private volatile bool _isShutdown;
    private bool _disposed;
    private int _disableProcessingCount;
    private DispatcherHooks? _hooks;

    // Re-entrancy depth of the same-thread inline InvokeAsync fast path (see TryInvokeInline).
    // Bounds stack growth when UI-thread continuations resume synchronously through
    // DispatcherSynchronizationContext.Post (e.g. an `await Task.Yield()` storm): above the limit
    // InvokeAsync falls back to the queue so the chain breaks to the message pump instead of
    // overflowing the stack. Thread-static because the inline path only ever runs on the dispatcher
    // thread (it is gated by CheckAccess()).
    [ThreadStatic] private static int t_inlineInvokeDepth;
    private const int MaxInlineInvokeDepth = 32;

    // Platform-abstracted dispatcher wake mechanism
    private IDispatcherWake? _dispatcherWake;

    // Win32 message window (Windows only, kept for backward compat)
    private nint _messageWindow;
    private WndProcDelegate? _wndProcDelegate;
    private const string MessageWindowClassName = "JaliumDispatcherMessageWindow";
    private const uint WM_DISPATCHER_INVOKE = 0x0400 + 1; // WM_USER + 1

    // Input message ranges drained by DrainPendingInputMessages (client-area input
    // only; non-client messages stay with the regular pump).
    private const uint WM_QUIT = 0x0012;
    private const uint WM_KEYFIRST = 0x0100;   // WM_KEYDOWN
    private const uint WM_KEYLAST = 0x0109;    // WM_UNICHAR
    private const uint WM_MOUSEFIRST = 0x0200; // WM_MOUSEMOVE
    private const uint WM_MOUSELAST = 0x020E;  // WM_MOUSEHWHEEL
    private const uint WM_TOUCHFIRST = 0x0240; // WM_TOUCH
    private const uint WM_POINTERLAST = 0x0253; // WM_POINTERROUTEDRELEASED
    private const uint PM_REMOVE = 0x0001;

    // MsgWaitForMultipleObjectsEx: the primary Windows pump (RunWindowsMessageLoop)
    // blocks on the work-available kernel event OR any incoming input/message, so a
    // posted dispatcher wake no longer outranks hardware input the way a bare
    // GetMessage loop does (Win32 retrieval order is posted > input).
    private const uint QS_ALLINPUT = 0x04FF;         // input | posted | timer | paint | hotkey | send
    private const uint MWMO_INPUTAVAILABLE = 0x0004; // return even if a message is already queued
    private const uint INFINITE = 0xFFFFFFFF;
    private const uint WAIT_FAILED = 0xFFFFFFFF;

    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private const int MinDispatchPriority = (int)DispatcherPriority.SystemIdle; // 1; Inactive(0)/Invalid(-1) never run

    /// <summary>
    /// Gets the <see cref="Dispatcher"/> for the thread currently executing.
    /// </summary>
    public static Dispatcher? CurrentDispatcher => _currentDispatcher.Value;

    /// <summary>
    /// Gets the <see cref="Dispatcher"/> for the main UI thread.
    /// </summary>
    public static Dispatcher? MainDispatcher => _mainDispatcher;

    /// <summary>
    /// Gets the thread this <see cref="Dispatcher"/> is associated with.
    /// </summary>
    public Thread Thread => _thread;

    /// <summary>
    /// Gets a value indicating whether the dispatcher has been shut down.
    /// </summary>
    public bool HasShutdownStarted => _isShutdown;

    /// <summary>
    /// Gets a value indicating whether the dispatcher has finished shutting down.
    /// </summary>
    public bool HasShutdownFinished
    {
        get { lock (_instanceLock) { return _isShutdown && _queue.Count == 0; } }
    }

    /// <summary>
    /// Gets the collection of hooks that provide additional event information about the <see cref="Dispatcher"/>.
    /// </summary>
    public DispatcherHooks Hooks => _hooks ??= new DispatcherHooks();

    internal DispatcherHooks? HooksInternal => _hooks;

    /// <summary>
    /// Occurs when a thread exception is thrown and uncaught during execution of a delegate by way of Invoke or BeginInvoke.
    /// </summary>
    public event DispatcherUnhandledExceptionEventHandler? UnhandledException;

    /// <summary>
    /// Occurs when a thread exception is thrown and uncaught during execution of a delegate, before the
    /// <see cref="UnhandledException"/> event, to determine whether the exception should be caught.
    /// </summary>
    public event DispatcherUnhandledExceptionFilterEventHandler? UnhandledExceptionFilter;

    /// <summary>
    /// Initializes a new instance of the <see cref="Dispatcher"/> class for the current thread.
    /// </summary>
    private Dispatcher()
    {
        _thread = Thread.CurrentThread;
        _threadId = s_isWindows ? GetCurrentThreadId() : 0;

        // Create platform-specific dispatch wake mechanism
        if (s_isWindows)
        {
            CreateMessageWindow();
        }
        else
        {
            try
            {
                _dispatcherWake = new NativeDispatcherWake();
                _dispatcherWake.SetCallback(ProcessQueue);
            }
            catch
            {
                // Fallback: no wake mechanism, rely on ManualResetEventSlim polling
            }
        }
    }

    /// <summary>
    /// Gets or creates a <see cref="Dispatcher"/> for the current thread.
    /// </summary>
    /// <returns>The dispatcher for the current thread.</returns>
    public static Dispatcher GetForCurrentThread()
    {
        if (_currentDispatcher.Value == null)
        {
            var dispatcher = new Dispatcher();
            _currentDispatcher.Value = dispatcher;
            _dispatchers[dispatcher._thread] = dispatcher;

            // First dispatcher becomes the main dispatcher
            lock (_lock)
            {
                _mainDispatcher ??= dispatcher;
            }
        }

        return _currentDispatcher.Value;
    }

    /// <summary>
    /// Returns the <see cref="Dispatcher"/> for the specified thread, or <see langword="null"/> if none exists.
    /// </summary>
    /// <param name="thread">The thread whose dispatcher is requested.</param>
    public static Dispatcher? FromThread(Thread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        return _dispatchers.TryGetValue(thread, out var dispatcher) ? dispatcher : null;
    }

    /// <summary>
    /// Sets the current thread's dispatcher as the main UI thread dispatcher.
    /// </summary>
    public static void SetAsMainThread()
    {
        var dispatcher = GetForCurrentThread();
        lock (_lock)
        {
            _mainDispatcher = dispatcher;
        }
    }

    /// <summary>
    /// Determines whether the calling thread is the thread associated with this <see cref="Dispatcher"/>.
    /// </summary>
    /// <returns>true if the calling thread is the thread associated with this dispatcher; otherwise, false.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool CheckAccess()
    {
        return Thread.CurrentThread == _thread;
    }

    /// <summary>
    /// Determines whether the calling thread has access to this <see cref="Dispatcher"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread does not have access to this <see cref="Dispatcher"/>.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void VerifyAccess()
    {
        if (!CheckAccess())
        {
            throw new InvalidOperationException(
                "The calling thread cannot access this object because a different thread owns it.");
        }
    }

    #region Invoke (synchronous)

    /// <summary>
    /// Executes the specified <see cref="Action"/> synchronously on the dispatcher thread at <see cref="DispatcherPriority.Send"/>.
    /// </summary>
    public void Invoke(Action callback) => Invoke(callback, DispatcherPriority.Send);

    /// <summary>
    /// Executes the specified <see cref="Action"/> synchronously on the dispatcher thread at the given priority.
    /// </summary>
    public void Invoke(Action callback, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidatePriority(priority);

        if (CheckAccess() && priority == DispatcherPriority.Send)
        {
            callback();
            return;
        }

        var op = CreateOperation(callback, priority, critical: false, observed: true);
        EnqueueOperation(op);
        op.Wait();

        if (op.OperationException != null)
        {
            throw new InvalidOperationException(
                "An error occurred while executing on the dispatcher thread.", op.OperationException);
        }
    }

    /// <summary>
    /// Executes the specified <see cref="Func{TResult}"/> synchronously on the dispatcher thread at <see cref="DispatcherPriority.Send"/>.
    /// </summary>
    public TResult Invoke<TResult>(Func<TResult> callback) => Invoke(callback, DispatcherPriority.Send);

    /// <summary>
    /// Executes the specified <see cref="Func{TResult}"/> synchronously on the dispatcher thread at the given priority.
    /// </summary>
    public TResult Invoke<TResult>(Func<TResult> callback, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidatePriority(priority);

        if (CheckAccess() && priority == DispatcherPriority.Send)
        {
            return callback();
        }

        TResult result = default!;
        var op = CreateOperation(() => result = callback(), priority, critical: false, observed: true);
        EnqueueOperation(op);
        op.Wait();

        if (op.OperationException != null)
        {
            throw new InvalidOperationException(
                "An error occurred while executing on the dispatcher thread.", op.OperationException);
        }

        return result;
    }

    #endregion

    #region BeginInvoke (asynchronous, returns DispatcherOperation)

    /// <summary>
    /// Schedules the specified <see cref="Action"/> for asynchronous execution at <see cref="DispatcherPriority.Normal"/>.
    /// </summary>
    /// <returns>A <see cref="DispatcherOperation"/> tracking the scheduled work.</returns>
    public DispatcherOperation BeginInvoke(Action callback) => BeginInvoke(DispatcherPriority.Normal, callback);

    /// <summary>
    /// Schedules the specified <see cref="Action"/> for asynchronous execution at the given priority.
    /// </summary>
    public DispatcherOperation BeginInvoke(DispatcherPriority priority, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidatePriority(priority);
        var op = CreateOperation(callback, priority, critical: false, observed: false);
        EnqueueOperation(op);
        return op;
    }

    /// <summary>
    /// Schedules a delegate for asynchronous execution at <see cref="DispatcherPriority.Normal"/>.
    /// </summary>
    public DispatcherOperation BeginInvoke(Delegate method, params object?[]? args)
        => BeginInvoke(DispatcherPriority.Normal, method, args);

    /// <summary>
    /// Schedules a delegate for asynchronous execution at the given priority.
    /// </summary>
    public DispatcherOperation BeginInvoke(DispatcherPriority priority, Delegate method, params object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        ValidatePriority(priority);
        var op = CreateOperation(() => method.DynamicInvoke(args), priority, critical: false, observed: false);
        EnqueueOperation(op);
        return op;
    }

    /// <summary>
    /// Schedules the specified <see cref="Action"/> for asynchronous execution. Exceptions from critical
    /// callbacks are rethrown by <see cref="ProcessQueue"/> instead of being routed to <see cref="UnhandledException"/>.
    /// </summary>
    public DispatcherOperation BeginInvokeCritical(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var op = CreateOperation(callback, DispatcherPriority.Normal, critical: true, observed: false);
        EnqueueOperation(op);
        return op;
    }

    #endregion

    #region InvokeAsync (Task-returning, Jalium convention)

    /// <summary>
    /// Executes the specified <see cref="Action"/> asynchronously on the dispatcher thread.
    /// </summary>
    public Task InvokeAsync(Action callback) => InvokeAsync(callback, DispatcherPriority.Normal);

    // Same-thread inline fast-path predicate shared by both InvokeAsync overloads. When true, the
    // callback may run synchronously on the calling (dispatcher) thread and the returned Task is
    // already completed — so a continuation posted at Normal/Send priority via
    // DispatcherSynchronizationContext.Post resumes promptly even when the message pump is not yet
    // running (the sync-over-async case that otherwise deadlocks the UI thread). Cases that fall
    // through to the queued path:
    //   * priority < Normal (Background/Input/Render/idle): explicit deferral is honored.
    //   * not on the dispatcher thread: cross-thread work must be marshaled via the queue.
    //   * DisableProcessing in effect: the caller fenced off a re-entrancy-free critical region,
    //     so deferred continuation work is held back (EnableProcessing re-posts a wake on exit).
    //   * inline re-entrancy depth at the cap: a same-thread continuation storm (e.g. an
    //     `await Task.Yield()` loop) is broken back onto the queue so it cannot overflow the stack.
    private bool TryInvokeInline(DispatcherPriority priority)
        => priority >= DispatcherPriority.Normal
           && CheckAccess()
           && Volatile.Read(ref _disableProcessingCount) == 0
           && t_inlineInvokeDepth < MaxInlineInvokeDepth;

    /// <summary>
    /// Executes the specified <see cref="Action"/> asynchronously on the dispatcher thread at the given priority.
    /// </summary>
    public Task InvokeAsync(Action callback, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidatePriority(priority);

        // Inline fast path: run a same-thread Normal/Send callback synchronously and hand back an
        // already-completed Task. This keeps UI-thread await continuations (forwarded here by
        // DispatcherSynchronizationContext.Post at Normal priority) from being parked until the pump
        // runs, which is what otherwise deadlocks sync-over-async on the UI thread. No
        // DispatcherOperation is created, so OperationPosted/Completed hooks intentionally do not
        // fire for this path (matching the synchronous Invoke(Send) fast path).
        if (TryInvokeInline(priority))
        {
            t_inlineInvokeDepth++;
            try
            {
                callback();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                // Faulted Task mirrors the queued path (op exception -> tcs.TrySetException). These
                // ops are observed, so the queued path does not route to UnhandledException either.
                return Task.FromException(ex);
            }
            finally
            {
                t_inlineInvokeDepth--;
            }
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var op = CreateOperation(callback, priority, critical: false, observed: true);
        op.Completed += (_, _) =>
        {
            if (op.OperationException != null) tcs.TrySetException(op.OperationException);
            else tcs.TrySetResult();
        };
        op.Aborted += (_, _) => tcs.TrySetCanceled();
        EnqueueOperation(op);
        return tcs.Task;
    }

    /// <summary>
    /// Executes the specified <see cref="Func{TResult}"/> asynchronously on the dispatcher thread.
    /// </summary>
    public Task<TResult> InvokeAsync<TResult>(Func<TResult> callback) => InvokeAsync(callback, DispatcherPriority.Normal);

    /// <summary>
    /// Executes the specified <see cref="Func{TResult}"/> asynchronously on the dispatcher thread at the given priority.
    /// </summary>
    public Task<TResult> InvokeAsync<TResult>(Func<TResult> callback, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidatePriority(priority);

        // Inline fast path (see the Action overload for rationale): run the same-thread Normal/Send
        // callback synchronously and return an already-completed Task carrying its result.
        if (TryInvokeInline(priority))
        {
            t_inlineInvokeDepth++;
            try
            {
                return Task.FromResult(callback());
            }
            catch (Exception ex)
            {
                return Task.FromException<TResult>(ex);
            }
            finally
            {
                t_inlineInvokeDepth--;
            }
        }

        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        TResult result = default!;
        var op = CreateOperation(() => result = callback(), priority, critical: false, observed: true);
        op.Completed += (_, _) =>
        {
            if (op.OperationException != null) tcs.TrySetException(op.OperationException);
            else tcs.TrySetResult(result);
        };
        op.Aborted += (_, _) => tcs.TrySetCanceled();
        EnqueueOperation(op);
        return tcs.Task;
    }

    #endregion

    #region Queue engine

    private DispatcherOperation CreateOperation(Action action, DispatcherPriority priority, bool critical, bool observed)
        => new(this, priority, action, critical, observed);

    private void EnqueueOperation(DispatcherOperation op)
    {
        bool wake;
        lock (_instanceLock)
        {
            if (_isShutdown)
            {
                // Cannot queue work after shutdown has begun.
                op.AbortInternal();
                return;
            }
            op.Node = _queue.AddLast(op);
            wake = true;
        }

        Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.ENQ);
        _hooks?.RaiseOperationPosted(op, EventArgs.Empty);
        if (wake)
            NotifyDispatcherThread();
    }

    // 出队最高优先级的待执行操作（同优先级 FIFO）；顺带剔除已中止节点；Inactive/Invalid 不出队。
    private DispatcherOperation? DequeueNextOperation()
    {
        lock (_instanceLock)
        {
            LinkedListNode<DispatcherOperation>? best = null;
            var node = _queue.First;
            while (node != null)
            {
                var next = node.Next;
                var op = node.Value;

                if (op.Status == DispatcherOperationStatus.Aborted)
                {
                    _queue.Remove(node);
                    op.Node = null;
                }
                else if ((int)op.Priority >= MinDispatchPriority &&
                         (best == null || (int)op.Priority > (int)best.Value.Priority))
                {
                    best = node;
                }

                node = next;
            }

            if (best == null)
                return null;

            _queue.Remove(best);
            best.Value.Node = null;
            return best.Value;
        }
    }

    internal void RemoveOperation(DispatcherOperation op)
    {
        lock (_instanceLock)
        {
            if (op.Node != null)
            {
                _queue.Remove(op.Node);
                op.Node = null;
            }
        }
    }

    internal void OnOperationPriorityChanged(DispatcherOperation op)
    {
        // 链表出队时按 op.Priority 实时判定，无需重排；仅唤醒一次以便重新评估。
        _hooks?.RaiseOperationPriorityChanged(op, EventArgs.Empty);
        NotifyDispatcherThread();
    }

    /// <summary>
    /// Processes pending work items in the dispatcher queue in priority order.
    /// This is invoked from the dispatcher thread by the platform message pump.
    /// </summary>
    public void ProcessQueue()
    {
        VerifyAccess();
        Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.PQ);

        if (Volatile.Read(ref _disableProcessingCount) > 0)
        {
            Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.PQ_DISABLED);
            return; // processing disabled; work stays queued until re-enabled re-posts a wake
        }

        // Bounded batch: process only the operations that were already queued when
        // this dispatch started. Draining until the queue is empty starves the
        // Win32 message pump during continuous animation — every batch re-enqueues
        // the next frame's work (RaiseRendering → ProcessRender → RearmTimer, flush
        // and retry timers firing mid-render), so the queue never empties, GetMessage
        // is never called, and input (mouse wheel, moves) queues up for the whole
        // animation ("one scroll animation must finish before the next begins").
        // No wake-up is lost by returning early: NotifyDispatcherThread posts one
        // WM_DISPATCHER_INVOKE per enqueue, so the remaining operations always have
        // at least as many wake messages still sitting in the OS queue — behind any
        // input messages that arrived first, which is exactly the point.
        int budget;
        lock (_instanceLock)
        {
            budget = _queue.Count;
        }

        while (budget-- > 0)
        {
            if (Volatile.Read(ref _disableProcessingCount) > 0)
                break;

            var op = DequeueNextOperation();
            if (op == null)
                break;

            Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.OP);
            DispatchOperation(op);
        }

        lock (_instanceLock)
        {
            Jalium.UI.Diagnostics.HoverTrace.Gauge(Jalium.UI.Diagnostics.HoverTrace.G_QDEPTH, _queue.Count);
            if (_queue.Count == 0)
                _workAvailable.Reset();
            // Non-empty: keep _workAvailable set so the non-Windows PushFrame loop
            // (ProcessQueue(); Wait(...)) starts the next batch immediately.
        }
    }

    private void DispatchOperation(DispatcherOperation op)
    {
        try
        {
            op.InvokeInternal();
        }
        catch (Exception ex)
        {
            if (op.IsCritical)
                throw; // critical: propagate out of the pump (legacy BeginInvokeCritical contract)

            if (!RaiseUnhandledException(ex))
            {
                // Unhandled and not requested-catch: rethrow to crash like WPF would.
                throw;
            }
            // else: handled / requested-catch -> swallow
        }
    }

    // Returns true if the exception was handled (or a filter requested catching it and an
    // UnhandledException handler marked it Handled). Returns false to let it propagate.
    private bool RaiseUnhandledException(Exception ex)
    {
        bool requestCatch = true;
        var filter = UnhandledExceptionFilter;
        if (filter != null)
        {
            var filterArgs = new DispatcherUnhandledExceptionFilterEventArgs(this, ex);
            filter(this, filterArgs);
            requestCatch = filterArgs.RequestCatch;
        }

        if (!requestCatch)
            return false; // a filter explicitly asked us NOT to catch -> let it propagate

        var handler = UnhandledException;
        if (handler != null)
        {
            var args = new DispatcherUnhandledExceptionEventArgs(this, ex);
            handler(this, args);
        }

        // Jalium historically swallowed non-critical dispatcher exceptions to keep the pump alive;
        // preserve that default once a filter/handler has had a chance to observe it.
        return true;
    }

    /// <summary>
    /// Initiates shutdown of the dispatcher.
    /// </summary>
    public void BeginInvokeShutdown()
    {
        _isShutdown = true;
        ShutdownStarted?.Invoke(this, EventArgs.Empty);
        NotifyDispatcherThread();
    }

    private static void ValidatePriority(DispatcherPriority priority)
    {
        if (priority < DispatcherPriority.Inactive || priority > DispatcherPriority.Send)
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Invalid DispatcherPriority.");
    }

    #endregion

    #region Frames / pumping

    private static int s_frameDepth;

    /// <summary>
    /// Enters an execute loop, pumping the platform message queue (and dispatcher work) until the
    /// specified <see cref="DispatcherFrame"/> is instructed to stop.
    /// </summary>
    public void PushFrame(DispatcherFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        VerifyAccess();
        if (_isShutdown)
            throw new InvalidOperationException("Cannot perform this operation while dispatcher processing is suspended.");
        if (Volatile.Read(ref _disableProcessingCount) > 0)
            throw new InvalidOperationException(
                "Cannot pump a dispatcher frame (PushFrame / DispatcherOperation.Wait) while processing is disabled " +
                "via DisableProcessing: dispatcher work cannot run, so the frame would never complete. Close the " +
                "DisableProcessing scope before pumping.");

        Interlocked.Increment(ref s_frameDepth);
        try
        {
            if (s_isWindows)
            {
                RunWindowsMessageLoop(frame);
            }
            else
            {
                // Cross-platform: drain dispatcher work then block until more arrives.
                while (frame.Continue)
                {
                    ProcessQueue();
                    if (!frame.Continue)
                        break;
                    _workAvailable.Wait(15);
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref s_frameDepth);
        }
    }

    /// <summary>
    /// Primary Windows pump. Owns its wake (the work-available kernel event) and
    /// orders the turn input-first, instead of letting <c>GetMessage</c> impose
    /// Win32's fixed retrieval priority.
    /// </summary>
    /// <remarks>
    /// A classic <c>while (GetMessage) DispatchMessage</c> loop retrieves messages
    /// in the order sent &gt; <b>posted</b> &gt; input(hardware) &gt; WM_PAINT &gt;
    /// WM_TIMER. The render/animation wake is a <b>posted</b> message
    /// (<see cref="WM_DISPATCHER_INVOKE"/>), so a continuously-rendering UI always
    /// keeps a wake sitting ahead of pending mouse/keyboard input and starves it
    /// for the whole animation ("the current scroll must finish before the next
    /// notch lands"). The bounded <see cref="ProcessQueue"/> batch and
    /// <see cref="DrainPendingInputMessages"/> only blunt this; the pump's own
    /// <c>GetMessage</c> still picks the next posted wake ahead of input.
    ///
    /// This loop removes the inversion by not letting <c>GetMessage</c> choose the
    /// order. Each turn: (1) drain pending hardware input FIRST; (2) run a bounded
    /// dispatcher batch directly — this also <see cref="ManualResetEventSlim.Reset"/>s
    /// the work-available event when the queue empties, so the primary pump does
    /// not depend on the posted wake and cannot busy-spin at idle; (3) pump at most
    /// one other OS message (WM_PAINT / non-client / the modal-loop fallback wake /
    /// WM_QUIT) then loop back to re-drain input; (4) when genuinely idle, block on
    /// the work-available event OR any incoming input via
    /// <see cref="MsgWaitForMultipleObjectsEx"/>.
    ///
    /// <see cref="NotifyDispatcherThread"/> still posts <see cref="WM_DISPATCHER_INVOKE"/>
    /// (idempotent through the wake latch) purely as a fallback so OS-driven nested
    /// modal loops we do NOT own — menu tracking, DefWindowProc move/size, MessageBox,
    /// OLE drag — keep servicing dispatcher work. In this loop that posted wake is
    /// just another message drained in step (3), always after input.
    /// </remarks>
    private void RunWindowsMessageLoop(DispatcherFrame frame)
        => RunWindowsMessageLoopCore(frame, outermost: false);

    /// <summary>
    /// Runs the top-level Windows message loop with the same input-first ordering as
    /// nested frames, returning the WM_QUIT exit code. Application.Run routes through
    /// this so the PRIMARY pump — not only nested frames driven by PushFrame /
    /// DispatcherOperation.Wait — stops a posted dispatcher wake from outranking
    /// hardware input. Windows only; cross-platform hosts keep their own loop.
    /// </summary>
    public int RunMainMessageLoop()
    {
        VerifyAccess();
        if (!s_isWindows)
            throw new PlatformNotSupportedException(
                "RunMainMessageLoop is the Windows pump; use PushFrame/Run on other platforms.");
        return RunWindowsMessageLoopCore(new DispatcherFrame(), outermost: true);
    }

    /// <summary>
    /// Runs a nested, input-first modal message loop that pumps while
    /// <paramref name="keepRunning"/> returns true (re-checked each turn and before
    /// each idle block). Window.ShowDialog uses this so a modal dialog gets the same
    /// input-first ordering as the main pump, and a WM_QUIT is re-posted so the
    /// application's main loop still exits. Windows only; a no-op elsewhere.
    /// </summary>
    public void RunModalLoop(Func<bool> keepRunning)
    {
        ArgumentNullException.ThrowIfNull(keepRunning);
        VerifyAccess();
        if (!s_isWindows)
            return;
        Interlocked.Increment(ref s_frameDepth);
        try { RunWindowsMessageLoopCore(new DispatcherFrame(), outermost: false, keepRunning); }
        finally { Interlocked.Decrement(ref s_frameDepth); }
    }

    private int RunWindowsMessageLoopCore(DispatcherFrame frame, bool outermost, Func<bool>? keepRunning = null)
    {
        int exitCode = 0;

        // Materialize the kernel HANDLE backing the work-available event once and
        // ref-count it for the loop's lifetime, so a concurrent Dispatcher.Dispose
        // (which closes _workAvailable's SafeWaitHandle) cannot close the HANDLE out
        // from under an in-flight MsgWaitForMultipleObjectsEx. ManualResetEventSlim
        // lazily creates the underlying event on first WaitHandle access; Set()
        // (NotifyDispatcherThread) and Reset() (ProcessQueue, when the queue drains
        // empty) drive it.
        WaitHandle waitHandle = _workAvailable.WaitHandle;
        var safeWaitHandle = waitHandle.SafeWaitHandle;
        bool handleRefAdded = false;
        try
        {
            safeWaitHandle.DangerousAddRef(ref handleRefAdded);
            nint wakeHandle = safeWaitHandle.DangerousGetHandle();

            // keepRunning: nested modal pumps (Window.ShowDialog) stop when it
            // returns false (re-checked each turn AND before the idle block so a
            // dialog closed mid-turn does not block until the next message); the
            // main / nested-frame pumps pass null and stop on frame.Continue.
            while (frame.Continue && (keepRunning is null || keepRunning()))
            {
                // (1) Input first — cut hardware input ahead of any posted wake.
                DrainPendingInputMessages();
                if (!frame.Continue)
                    break;

                // (2) Dispatcher work directly. The bounded batch resets the wake
                //     event when the queue empties, so idle in step (4) does not
                //     busy-spin on a still-signaled event.
                ProcessQueue();
                if (!frame.Continue)
                    break;

                // (3) At most one non-input OS message per turn, then loop back to
                //     re-drain input — keeps input strictly ahead under a message
                //     burst (including a flood of posted dispatcher wakes).
                if (PumpOnePendingMessage(outermost, out bool quit, out int code))
                {
                    if (quit)
                    {
                        exitCode = code;
                        break;
                    }
                    continue;
                }
                if (!frame.Continue || (keepRunning is not null && !keepRunning()))
                    break;

                // (4) Idle: nothing queued, no message waiting. Block until woken.
                //     Normally wait on the work-available event OR any incoming
                //     input/message (MWMO_INPUTAVAILABLE returns immediately if a
                //     message is already queued, closing the enqueue/wait race).
                //     While processing is disabled the event can be stuck signaled
                //     over work we are not allowed to drain (ProcessQueue early-
                //     returns and never Resets it) — waiting on the event there would
                //     busy-spin, so wait on messages ONLY (nCount 0, MSDN "waits only
                //     for an input event"); EnableProcessing posts a wake message.
                uint wakeCount = Volatile.Read(ref _disableProcessingCount) > 0 ? 0u : 1u;
                uint r = MsgWaitForMultipleObjectsEx(
                    wakeCount, ref wakeHandle, INFINITE, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
                if (r == WAIT_FAILED)
                    break;
            }
        }
        finally
        {
            if (handleRefAdded)
                safeWaitHandle.DangerousRelease();
        }

        return exitCode;
    }

    /// <summary>
    /// Retrieves and dispatches at most one pending message of any class.
    /// Returns <see langword="true"/> if a message was handled (the caller loops
    /// back to re-drain input first); sets <paramref name="quit"/> when WM_QUIT was
    /// seen (re-posted for outer pumps, caller stops). Returns <see langword="false"/>
    /// when the queue holds no message.
    /// </summary>
    private bool PumpOnePendingMessage(bool outermost, out bool quit, out int exitCode)
    {
        quit = false;
        exitCode = 0;
        if (!PeekMessageW(out var msg, nint.Zero, 0, 0, PM_REMOVE))
            return false;

        if (msg.message == WM_QUIT)
        {
            exitCode = unchecked((int)msg.wParam);
            quit = true;
            // Nested frame: re-post so the OUTER pump also observes the quit.
            // Outermost (Application.Run): consume it and return the exit code —
            // there is no pump above us to see a re-posted WM_QUIT.
            if (!outermost)
                PostQuitMessage(exitCode);
            return true;
        }

        TranslateMessageW(ref msg);
        DispatchMessageW(ref msg);
        return true;
    }

    /// <summary>
    /// Enters an execute loop using a default <see cref="DispatcherFrame"/>.
    /// </summary>
    public void Run() => PushFrame(new DispatcherFrame());

    /// <summary>
    /// Requests that all frames exit, including nested frames.
    /// </summary>
    public void ExitAllFrames()
    {
        // Frames observe HasShutdownStarted via DispatcherFrame.Continue when exitWhenRequested;
        // explicit exit is signalled by marking shutdown-pending semantics. We post a wake so any
        // pumping frame re-evaluates Continue.
        NotifyDispatcherThread();
    }

    /// <summary>
    /// Disables processing of the queue until the returned structure is disposed.
    /// </summary>
    public DispatcherProcessingDisabled DisableProcessing()
    {
        VerifyAccess();
        Interlocked.Increment(ref _disableProcessingCount);
        return new DispatcherProcessingDisabled(this);
    }

    internal void EnableProcessing()
    {
        if (Interlocked.Decrement(ref _disableProcessingCount) == 0)
        {
            // Re-evaluate the queue now that processing is allowed again.
            NotifyDispatcherThread();
        }
    }

    /// <summary>
    /// Yields control to other pending operations on the dispatcher (await-able).
    /// </summary>
    public static DispatcherPriorityAwaitable Yield() => Yield(DispatcherPriority.Background);

    /// <summary>
    /// Yields control to operations at or above the specified priority on the current dispatcher.
    /// </summary>
    public static DispatcherPriorityAwaitable Yield(DispatcherPriority priority)
    {
        var dispatcher = CurrentDispatcher
            ?? throw new InvalidOperationException("Dispatcher.Yield requires a dispatcher on the current thread.");
        return new DispatcherPriorityAwaitable(dispatcher, priority);
    }

    #endregion

#pragma warning disable CS0067
    /// <summary>
    /// Occurs when the dispatcher begins to shut down.
    /// </summary>
    public event EventHandler? ShutdownStarted;

    /// <summary>
    /// Occurs when the dispatcher finishes shutting down.
    /// </summary>
    public event EventHandler? ShutdownFinished;
#pragma warning restore CS0067

    #region Message Window

    private void CreateMessageWindow()
    {
        // Keep delegate alive
        _wndProcDelegate = MessageWindowProc;

        var wndClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandle(null),
            lpszClassName = MessageWindowClassName + _threadId
        };

        var atom = RegisterClassExW(ref wndClass);
        if (atom == 0)
        {
            // Class might already exist, try to create window anyway
        }

        _messageWindow = CreateWindowExW(
            0,
            MessageWindowClassName + _threadId,
            string.Empty,
            0,
            0, 0, 0, 0,
            HWND_MESSAGE,
            nint.Zero,
            GetModuleHandle(null),
            nint.Zero);
    }

    private void DestroyMessageWindow()
    {
        if (_messageWindow != nint.Zero)
        {
            DestroyWindow(_messageWindow);
            _messageWindow = nint.Zero;
        }

        UnregisterClassW(MessageWindowClassName + _threadId, GetModuleHandle(null));
    }

    private nint MessageWindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_DISPATCHER_INVOKE)
        {
            Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.WAKE);
            // Release the wake latch BEFORE the batch: operations enqueued while
            // the batch runs must be able to post the next wake themselves.
            Interlocked.Exchange(ref _wakeInFlight, 0);
            ProcessQueue();
            // Input fairness: Win32 GetMessage retrieval priority is sent > posted >
            // input (hardware). During continuous animation every frame re-enqueues
            // dispatcher work, and every enqueue posts one WM_DISPATCHER_INVOKE, so
            // the posted queue is effectively never empty at the moment GetMessage
            // runs — hardware input (mouse wheel, keys) is starved for the whole
            // animation and then flushes as one OS-coalesced lump when the frame
            // chain finally idles ("scrolling queues up: the current scroll animation
            // must finish before the next notch lands"). The bounded ProcessQueue
            // batch alone cannot fix this: it returns to the pump, but the pump's
            // GetMessage still picks the next posted wake ahead of any input.
            // Remedy: after each dispatcher batch, explicitly pull pending INPUT
            // messages out of the queue (range-filtered PeekMessage ignores posted
            // wakes) and dispatch them, so input cuts ahead of the wake flood.
            DrainPendingInputMessages();
            // Wake coalescing invariant: "queue non-empty ⇒ at least one wake in
            // flight". Work left by the bounded batch (or enqueued during it whose
            // Notify lost the latch race against a wake that is being consumed
            // right now) gets its follow-up wake here; NotifyDispatcherThread is
            // idempotent through the latch, so this never stacks duplicates.
            // Skipped while processing is disabled — re-notifying there would spin
            // the pump (ProcessQueue returns without draining); EnableProcessing
            // re-posts the wake that resumes the queue.
            if (Volatile.Read(ref _disableProcessingCount) == 0)
            {
                bool hasWork;
                lock (_instanceLock)
                {
                    hasWork = _queue.Count > 0;
                }
                if (hasWork)
                {
                    NotifyDispatcherThread();
                }
            }
            return nint.Zero;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // True while DrainPendingInputMessages is dispatching input on this dispatcher's
    // thread. An input handler may run a nested pump (modal dialog, drag loop, menu);
    // wake messages dispatched there re-enter MessageWindowProc — the flag keeps the
    // nested level from starting a second drain on top of the first (bounding stack
    // growth to what nested frames already introduce by design).
    private bool _drainingInput;

    // Upper bound of input messages dispatched per drain call. Keeps an input storm
    // (e.g. autorepeat, high-rate wheel) from reverse-starving dispatcher operations:
    // at most this many input dispatches run between two dispatcher batches.
    private const int MaxInputMessagesPerDrain = 16;

    /// <summary>
    /// Dispatches pending hardware/input messages (mouse, keyboard, touch/pointer)
    /// for this thread so they are not starved behind posted dispatcher wake
    /// messages. Windows only; called at the tail of each WM_DISPATCHER_INVOKE batch.
    /// </summary>
    private void DrainPendingInputMessages()
    {
        // Respect the same fences as ProcessQueue: no re-entrant drains, nothing
        // after shutdown, and nothing inside a DisableProcessing critical region
        // (dispatching input there could re-enter arbitrary user code).
        if (_drainingInput || _isShutdown || Volatile.Read(ref _disableProcessingCount) > 0)
            return;

        _drainingInput = true;
        try
        {
            int budget = MaxInputMessagesPerDrain;
            bool retrievedAny = true;
            while (retrievedAny && budget > 0)
            {
                retrievedAny = false;
                // One message per class per pass: PeekMessage range filters preserve
                // FIFO order within each class, and the round-robin interleaves the
                // classes instead of draining all of one before the others.
                if (!TryDispatchOneInputMessage(WM_MOUSEFIRST, WM_MOUSELAST, ref budget, ref retrievedAny))
                    return;
                if (!TryDispatchOneInputMessage(WM_KEYFIRST, WM_KEYLAST, ref budget, ref retrievedAny))
                    return;
                if (!TryDispatchOneInputMessage(WM_TOUCHFIRST, WM_POINTERLAST, ref budget, ref retrievedAny))
                    return;
            }
        }
        finally
        {
            _drainingInput = false;
        }
    }

    // Retrieves and dispatches at most one pending message in [first, last].
    // Returns false when the drain must stop immediately (a WM_QUIT was synthesized —
    // PeekMessage returns WM_QUIT regardless of the range filter once PostQuitMessage
    // has been called and the queue runs dry; re-post it for the outer pump and bail).
    private bool TryDispatchOneInputMessage(uint first, uint last, ref int budget, ref bool retrievedAny)
    {
        if (budget <= 0)
            return true;

        if (!PeekMessageW(out var msg, nint.Zero, first, last, PM_REMOVE))
            return true;

        if (msg.message == WM_QUIT)
        {
            PostQuitMessage((int)msg.wParam);
            return false;
        }

        budget--;
        retrievedAny = true;
        TranslateMessageW(ref msg);
        DispatchMessageW(ref msg);
        return true;
    }

    // Wake coalescing latch: 1 while a WM_DISPATCHER_INVOKE is posted but not yet
    // consumed. One-wake-per-enqueue used to flood the thread's posted queue during
    // continuous animation (each frame enqueues ~4 ops, each pump pass consumes ONE
    // wake but batches N ops → the surplus grew unbounded) and hit the Win32
    // per-thread limit of 10,000 posted messages after ~2 minutes — from then on
    // EVERY PostMessage on the thread (including TrackMouseEvent's leave messages
    // and our own wakes) failed randomly and silently. With the latch there is at
    // most one wake in flight, so the quota can never fill from here.
    private int _wakeInFlight;

    private void NotifyDispatcherThread()
    {
        _workAvailable.Set();

        if (s_isWindows)
        {
            // Post ONE wake per idle→pending transition (see _wakeInFlight). The
            // consumer releases the latch before running its batch, so an enqueue
            // racing the batch posts the next wake; MessageWindowProc re-notifies
            // after the batch when work remains ("queue non-empty ⇒ wake in flight").
            if (_messageWindow != nint.Zero &&
                Interlocked.CompareExchange(ref _wakeInFlight, 1, 0) == 0)
            {
                if (!PostMessageW(_messageWindow, WM_DISPATCHER_INVOKE, nint.Zero, nint.Zero))
                {
                    // Roll the latch back so a later enqueue retries. A failure here
                    // means some OTHER code filled the thread quota — count it, don't
                    // hide it (19k silent failures once cost a day of diagnosis).
                    Interlocked.Exchange(ref _wakeInFlight, 0);
                    Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.POST_FAIL);
                }
            }
        }
        else
        {
            // Wake via platform-native mechanism (eventfd on Linux, ALooper on Android)
            _dispatcherWake?.Wake();
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _dispatchers.TryRemove(_thread, out _);

        if (s_isWindows)
        {
            DestroyMessageWindow();
        }

        _dispatcherWake?.Dispose();
        _dispatcherWake = null;
        _workAvailable.Dispose();
    }

    #endregion

    #region Win32 Interop

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    private static readonly nint HWND_MESSAGE = new(-3);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClassW(string lpClassName, nint hInstance);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hWnd, uint Msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProcW(nint hWnd, uint Msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int nExitCode);

    // Single-handle overload: pHandles marshals as a pointer to the one HANDLE
    // (nCount == 1). Returns WAIT_OBJECT_0 for the handle, WAIT_OBJECT_0+1 for a
    // waiting message, WAIT_FAILED on error.
    [LibraryImport("user32.dll", EntryPoint = "MsgWaitForMultipleObjectsEx")]
    private static partial uint MsgWaitForMultipleObjectsEx(uint nCount, ref nint pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);

    #endregion
}

/// <summary>
/// Represents the method that handles the <see cref="Dispatcher.UnhandledException"/> event.
/// </summary>
public delegate void DispatcherUnhandledExceptionEventHandler(object sender, DispatcherUnhandledExceptionEventArgs e);

/// <summary>
/// Represents the method that handles the <see cref="Dispatcher.UnhandledExceptionFilter"/> event.
/// </summary>
public delegate void DispatcherUnhandledExceptionFilterEventHandler(object sender, DispatcherUnhandledExceptionFilterEventArgs e);

/// <summary>
/// Specifies the priority at which operations can be invoked via the <see cref="Dispatcher"/>.
/// </summary>
public enum DispatcherPriority
{
    /// <summary>
    /// The enumeration value is -1. This is an invalid priority.
    /// </summary>
    Invalid = -1,

    /// <summary>
    /// The operation will not be processed.
    /// </summary>
    Inactive = 0,

    /// <summary>
    /// The operation is processed when the system is idle.
    /// </summary>
    SystemIdle = 1,

    /// <summary>
    /// The operation is processed when the application is idle.
    /// </summary>
    ApplicationIdle = 2,

    /// <summary>
    /// The operation is processed after background operations have completed.
    /// </summary>
    ContextIdle = 3,

    /// <summary>
    /// The operation is processed after all other non-idle operations have completed.
    /// </summary>
    Background = 4,

    /// <summary>
    /// The operation is processed at the same priority as input.
    /// </summary>
    Input = 5,

    /// <summary>
    /// The operation is processed when layout and render operations have completed.
    /// </summary>
    Loaded = 6,

    /// <summary>
    /// The operation is processed at the same priority as rendering.
    /// </summary>
    Render = 7,

    /// <summary>
    /// The operation is processed at the same priority as data binding.
    /// </summary>
    DataBind = 8,

    /// <summary>
    /// The operation is processed at normal priority.
    /// </summary>
    Normal = 9,

    /// <summary>
    /// The operation is processed before other asynchronous operations.
    /// </summary>
    Send = 10
}
