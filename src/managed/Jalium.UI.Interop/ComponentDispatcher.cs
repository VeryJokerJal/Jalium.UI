namespace Jalium.UI.Interop;

/// <summary>Processes a Win32 thread message.</summary>
/// <param name="msg">The message to process.</param>
/// <param name="handled">Set to <see langword="true"/> when processing is complete.</param>
public delegate void ThreadMessageEventHandler(ref MSG msg, ref bool handled);

/// <summary>
/// Coordinates modal state and Win32 message preprocessing for the current UI
/// thread. Event subscriptions are isolated per thread, matching WPF's hosting
/// contract.
/// </summary>
public static class ComponentDispatcher
{
    [ThreadStatic]
    private static ThreadData? s_threadData;

    private static ThreadData CurrentThreadData => s_threadData ??= new ThreadData();

    /// <summary>Occurs when the current thread enters its outermost modal scope.</summary>
    public static event EventHandler EnterThreadModal
    {
        add => CurrentThreadData.EnterThreadModal += value;
        remove => CurrentThreadData.EnterThreadModal -= value;
    }

    /// <summary>Occurs when the current thread leaves its outermost modal scope.</summary>
    public static event EventHandler LeaveThreadModal
    {
        add => CurrentThreadData.LeaveThreadModal += value;
        remove => CurrentThreadData.LeaveThreadModal -= value;
    }

    /// <summary>Occurs before normal message preprocessing.</summary>
    public static event ThreadMessageEventHandler ThreadFilterMessage
    {
        add => CurrentThreadData.ThreadFilterMessage += value;
        remove => CurrentThreadData.ThreadFilterMessage -= value;
    }

    /// <summary>Occurs when the current thread becomes idle.</summary>
    public static event EventHandler ThreadIdle
    {
        add => CurrentThreadData.ThreadIdle += value;
        remove => CurrentThreadData.ThreadIdle -= value;
    }

    /// <summary>Occurs after filter handlers have declined a message.</summary>
    public static event ThreadMessageEventHandler ThreadPreprocessMessage
    {
        add => CurrentThreadData.ThreadPreprocessMessage += value;
        remove => CurrentThreadData.ThreadPreprocessMessage -= value;
    }

    /// <summary>Gets the message currently being preprocessed on this thread.</summary>
    public static MSG CurrentKeyboardMessage => CurrentThreadData.CurrentKeyboardMessage;

    /// <summary>Gets whether the current thread owns at least one modal scope.</summary>
    public static bool IsThreadModal => CurrentThreadData.ModalCount > 0;

    /// <summary>Enters a modal scope for the current thread.</summary>
    public static void PushModal()
    {
        ThreadData data = CurrentThreadData;
        checked
        {
            data.ModalCount++;
        }

        if (data.ModalCount == 1)
        {
            data.EnterThreadModal?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Leaves one modal scope for the current thread.</summary>
    public static void PopModal()
    {
        ThreadData data = CurrentThreadData;
        if (data.ModalCount == 0)
        {
            return;
        }

        data.ModalCount--;
        if (data.ModalCount == 0)
        {
            data.LeaveThreadModal?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Raises the idle notification on the current thread.</summary>
    public static void RaiseIdle() => CurrentThreadData.ThreadIdle?.Invoke(null, EventArgs.Empty);

    /// <summary>
    /// Runs the current thread's filter and preprocess chains and returns whether
    /// either chain handled the message.
    /// </summary>
    /// <param name="msg">The mutable Win32 message.</param>
    public static bool RaiseThreadMessage(ref MSG msg)
    {
        ThreadData data = CurrentThreadData;
        MSG previousMessage = data.CurrentKeyboardMessage;
        data.CurrentKeyboardMessage = msg;
        try
        {
            bool handled = false;
            data.ThreadFilterMessage?.Invoke(ref msg, ref handled);
            if (!handled)
            {
                data.ThreadPreprocessMessage?.Invoke(ref msg, ref handled);
            }

            data.CurrentKeyboardMessage = msg;
            return handled;
        }
        finally
        {
            data.CurrentKeyboardMessage = previousMessage;
        }
    }

    private sealed class ThreadData
    {
        internal int ModalCount;
        internal MSG CurrentKeyboardMessage;
        internal EventHandler? EnterThreadModal;
        internal EventHandler? LeaveThreadModal;
        internal ThreadMessageEventHandler? ThreadFilterMessage;
        internal EventHandler? ThreadIdle;
        internal ThreadMessageEventHandler? ThreadPreprocessMessage;
    }
}
