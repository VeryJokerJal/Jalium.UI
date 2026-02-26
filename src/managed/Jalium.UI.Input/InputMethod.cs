using System.Runtime.InteropServices;

namespace Jalium.UI.Input;

/// <summary>
/// Provides input method editor (IME) services for text input.
/// </summary>
public static class InputMethod
{
    private static IInputElement? _currentTarget;
    private static bool _isComposing;
    private static string _compositionString = string.Empty;
    private static int _compositionCursor;

    #region Public Properties

    /// <summary>
    /// Gets the current IME target element.
    /// </summary>
    public static IInputElement? Current => _currentTarget;

    /// <summary>
    /// Gets whether IME composition is currently active.
    /// </summary>
    public static bool IsComposing => _isComposing;

    /// <summary>
    /// Gets the current composition string.
    /// </summary>
    public static string CompositionString => _compositionString;

    /// <summary>
    /// Gets the cursor position within the composition string.
    /// </summary>
    public static int CompositionCursor => _compositionCursor;

    /// <summary>
    /// Gets or sets whether IME is enabled for the current element.
    /// </summary>
    public static bool IsInputMethodEnabled { get; set; } = true;

    #endregion

    #region Public Methods

    /// <summary>
    /// Sets the IME target element.
    /// </summary>
    public static void SetTarget(IInputElement? element)
    {
        if (_currentTarget != element)
        {
            // End any active composition before switching targets
            // to prevent stale composition state leaking to the new target
            if (_isComposing)
            {
                EndComposition();
            }
            _currentTarget = element;
        }
    }

    /// <summary>
    /// Starts IME composition.
    /// </summary>
    public static void StartComposition()
    {
        _isComposing = true;
        _compositionString = string.Empty;
        _compositionCursor = 0;
        CompositionStarted?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Updates the composition string.
    /// </summary>
    public static void UpdateComposition(string text, int cursor)
    {
        _compositionString = text;
        _compositionCursor = cursor;
        CompositionUpdated?.Invoke(null, new CompositionEventArgs(text, cursor));
    }

    /// <summary>
    /// Ends IME composition with the final result.
    /// </summary>
    public static void EndComposition(string? result = null)
    {
        var wasComposing = _isComposing;
        _isComposing = false;
        _compositionString = string.Empty;
        _compositionCursor = 0;

        if (wasComposing)
        {
            CompositionEnded?.Invoke(null, new CompositionResultEventArgs(result));
        }
    }

    /// <summary>
    /// Cancels the current IME composition.
    /// </summary>
    public static void CancelComposition()
    {
        if (_isComposing)
        {
            EndComposition(null);
        }
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when IME composition starts.
    /// </summary>
    public static event EventHandler? CompositionStarted;

    /// <summary>
    /// Occurs when the composition string is updated.
    /// </summary>
    public static event EventHandler<CompositionEventArgs>? CompositionUpdated;

    /// <summary>
    /// Occurs when IME composition ends.
    /// </summary>
    public static event EventHandler<CompositionResultEventArgs>? CompositionEnded;

    #endregion

    #region Attached Properties

    private static readonly Dictionary<IInputElement, bool> _imeEnabledMap = new();
    private static readonly Dictionary<IInputElement, InputMethodState> _imeStateMap = new();
    private static readonly Dictionary<IInputElement, InputScope> _inputScopeMap = new();

    /// <summary>
    /// Gets whether IME is enabled for the specified element.
    /// </summary>
    public static bool GetIsInputMethodEnabled(IInputElement element)
    {
        return _imeEnabledMap.TryGetValue(element, out var value) ? value : true;
    }

    /// <summary>
    /// Sets whether IME is enabled for the specified element.
    /// </summary>
    public static void SetIsInputMethodEnabled(IInputElement element, bool value)
    {
        _imeEnabledMap[element] = value;
    }

    /// <summary>
    /// Gets the preferred IME state for the specified element.
    /// </summary>
    public static InputMethodState GetPreferredImeState(IInputElement element)
    {
        return _imeStateMap.TryGetValue(element, out var value) ? value : InputMethodState.DoNotCare;
    }

    /// <summary>
    /// Sets the preferred IME state for the specified element.
    /// </summary>
    public static void SetPreferredImeState(IInputElement element, InputMethodState value)
    {
        _imeStateMap[element] = value;
    }

    /// <summary>
    /// Gets the input scope for the specified element.
    /// </summary>
    public static InputScope GetInputScope(IInputElement element)
    {
        return _inputScopeMap.TryGetValue(element, out var value) ? value : new InputScope();
    }

    /// <summary>
    /// Sets the input scope for the specified element.
    /// </summary>
    public static void SetInputScope(IInputElement element, InputScope value)
    {
        _inputScopeMap[element] = value;
    }

    #endregion
}

/// <summary>
/// Specifies the preferred IME state.
/// </summary>
public enum InputMethodState
{
    /// <summary>
    /// The IME state is not specified.
    /// </summary>
    DoNotCare,

    /// <summary>
    /// IME should be turned on.
    /// </summary>
    On,

    /// <summary>
    /// IME should be turned off.
    /// </summary>
    Off
}

/// <summary>
/// Provides data for IME composition events.
/// </summary>
public sealed class CompositionEventArgs : EventArgs
{
    /// <summary>
    /// Gets the composition string.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the cursor position within the composition string.
    /// </summary>
    public int CursorPosition { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositionEventArgs"/> class.
    /// </summary>
    public CompositionEventArgs(string text, int cursorPosition)
    {
        Text = text;
        CursorPosition = cursorPosition;
    }
}

/// <summary>
/// Provides data for IME composition result events.
/// </summary>
public sealed class CompositionResultEventArgs : EventArgs
{
    /// <summary>
    /// Gets the final result string, or null if composition was cancelled.
    /// </summary>
    public string? Result { get; }

    /// <summary>
    /// Gets whether the composition was cancelled.
    /// </summary>
    public bool IsCancelled => Result == null;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositionResultEventArgs"/> class.
    /// </summary>
    public CompositionResultEventArgs(string? result)
    {
        Result = result;
    }
}

/// <summary>
/// Provides native IME interop methods.
/// </summary>
public static class ImmNativeMethods
{
    public const int WM_IME_STARTCOMPOSITION = 0x010D;
    public const int WM_IME_ENDCOMPOSITION = 0x010E;
    public const int WM_IME_COMPOSITION = 0x010F;
    public const int WM_IME_SETCONTEXT = 0x0281;
    public const int WM_IME_NOTIFY = 0x0282;
    public const int WM_IME_CONTROL = 0x0283;
    public const int WM_IME_COMPOSITIONFULL = 0x0284;
    public const int WM_IME_SELECT = 0x0285;
    public const int WM_IME_CHAR = 0x0286;
    public const int WM_IME_REQUEST = 0x0288;
    public const int WM_IME_KEYDOWN = 0x0290;
    public const int WM_IME_KEYUP = 0x0291;

    // GCS flags for ImmGetCompositionString
    public const int GCS_COMPSTR = 0x0008;
    public const int GCS_COMPATTR = 0x0010;
    public const int GCS_COMPCLAUSE = 0x0020;
    public const int GCS_CURSORPOS = 0x0080;
    public const int GCS_DELTASTART = 0x0100;
    public const int GCS_RESULTSTR = 0x0800;

    // CFS flags for ImmSetCompositionWindow
    public const int CFS_DEFAULT = 0x0000;
    public const int CFS_RECT = 0x0001;
    public const int CFS_POINT = 0x0002;
    public const int CFS_FORCE_POSITION = 0x0020;
    public const int CFS_CANDIDATEPOS = 0x0040;
    public const int CFS_EXCLUDE = 0x0080;

    [StructLayout(LayoutKind.Sequential)]
    public struct COMPOSITIONFORM
    {
        public int dwStyle;
        public POINT ptCurrentPos;
        public RECT rcArea;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CANDIDATEFORM
    {
        public int dwIndex;
        public int dwStyle;
        public POINT ptCurrentPos;
        public RECT rcArea;
    }

    [DllImport("imm32.dll")]
    public static extern nint ImmGetContext(nint hWnd);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ImmReleaseContext(nint hWnd, nint hIMC);

    [DllImport("imm32.dll", EntryPoint = "ImmGetCompositionStringW")]
    public static extern int ImmGetCompositionString(nint hIMC, int dwIndex, byte[]? lpBuf, int dwBufLen);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ImmSetCompositionWindow(nint hIMC, ref COMPOSITIONFORM lpCompForm);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ImmSetCandidateWindow(nint hIMC, ref CANDIDATEFORM lpCandidate);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ImmNotifyIME(nint hIMC, int dwAction, int dwIndex, int dwValue);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ImmSetOpenStatus(nint hIMC, [MarshalAs(UnmanagedType.Bool)] bool fOpen);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ImmGetOpenStatus(nint hIMC);

    [DllImport("imm32.dll")]
    public static extern nint ImmAssociateContext(nint hWnd, nint hIMC);

    [DllImport("imm32.dll")]
    public static extern nint ImmAssociateContextEx(nint hWnd, nint hIMC, int dwFlags);

    public const int IACE_CHILDREN = 0x0001;
    public const int IACE_DEFAULT = 0x0010;
    public const int IACE_IGNORENOCONTEXT = 0x0020;

    public const int NI_COMPOSITIONSTR = 0x0015;
    public const int CPS_COMPLETE = 0x0001;
    public const int CPS_CONVERT = 0x0002;
    public const int CPS_REVERT = 0x0003;
    public const int CPS_CANCEL = 0x0004;
}
