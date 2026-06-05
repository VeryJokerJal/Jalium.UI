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

    // Platform-abstracted dispatcher wake mechanism
    private IDispatcherWake? _dispatcherWake;

    // Win32 message window (Windows only, kept for backward compat)
    private nint _messageWindow;
    private WndProcDelegate? _wndProcDelegate;
    private const string MessageWindowClassName = "JaliumDispatcherMessageWindow";
    private const uint WM_DISPATCHER_INVOKE = 0x0400 + 1; // WM_USER + 1

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

    /// <summary>
    /// Executes the specified <see cref="Action"/> asynchronously on the dispatcher thread at the given priority.
    /// </summary>
    public Task InvokeAsync(Action callback, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidatePriority(priority);

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

        if (Volatile.Read(ref _disableProcessingCount) > 0)
            return; // processing disabled; work stays queued until re-enabled re-posts a wake

        while (true)
        {
            if (Volatile.Read(ref _disableProcessingCount) > 0)
                break;

            var op = DequeueNextOperation();
            if (op == null)
                break;

            DispatchOperation(op);
        }

        lock (_instanceLock)
        {
            if (_queue.Count == 0)
                _workAvailable.Reset();
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

        Interlocked.Increment(ref s_frameDepth);
        try
        {
            if (s_isWindows)
            {
                // Classic Win32 modal pump. The dispatcher's message-only window delivers
                // WM_DISPATCHER_INVOKE -> ProcessQueue, so queued work runs here too.
                while (frame.Continue)
                {
                    int result = GetMessageW(out var msg, nint.Zero, 0, 0);
                    if (result == 0) // WM_QUIT
                    {
                        // Re-post so the outermost loop also sees the quit, then exit this frame.
                        PostQuitMessage((int)msg.wParam);
                        break;
                    }
                    if (result == -1) // error
                        break;

                    TranslateMessageW(ref msg);
                    DispatchMessageW(ref msg);
                }
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
            ProcessQueue();
            return nint.Zero;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void NotifyDispatcherThread()
    {
        _workAvailable.Set();

        if (s_isWindows)
        {
            // Post message to wake up the Win32 message loop
            if (_messageWindow != nint.Zero)
            {
                PostMessageW(_messageWindow, WM_DISPATCHER_INVOKE, nint.Zero, nint.Zero);
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

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int nExitCode);

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
