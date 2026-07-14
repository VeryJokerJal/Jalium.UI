using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Jalium.UI.Threading;

namespace Jalium.UI.Input;

/// <summary>Provides dispatcher-local input method editor services.</summary>
public partial class InputMethod : DispatcherObject
{
    private static readonly ConditionalWeakTable<Dispatcher, InputMethod> Instances = new();
    private static bool s_globallyEnabled = true;

    private IInputElement? _currentTarget;
    private bool _isComposing;
    private string _compositionString = string.Empty;
    private int _compositionCursor;
    private InputMethodState _imeState = InputMethodState.Off;
    private ImeConversionModeValues _imeConversionMode = ImeConversionModeValues.Alphanumeric;
    private ImeSentenceModeValues _imeSentenceMode = ImeSentenceModeValues.None;
    private InputMethodState _handwritingState = InputMethodState.Off;
    private SpeechMode _speechMode = SpeechMode.Indeterminate;
    private InputMethodState _microphoneState = InputMethodState.Off;
    private InputMethodStateChangedEventHandler? _stateChanged;

    static InputMethod()
    {
        InputMethodService.IsInputMethodEnabledResolver = static element =>
            s_globallyEnabled &&
            (element is not DependencyObject dependencyObject || GetIsInputMethodEnabled(dependencyObject));
    }

    private InputMethod()
    {
    }

    /// <summary>Gets the input method associated with the current dispatcher.</summary>
    public static InputMethod Current
    {
        get
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            return Instances.GetValue(dispatcher, static _ => new InputMethod());
        }
    }

    /// <summary>Gets the element currently receiving composition updates.</summary>
    public static IInputElement? CurrentTarget => Current._currentTarget;
    public static bool IsComposing => Current._isComposing;
    public static string CompositionString => Current._compositionString;
    public static int CompositionCursor => Current._compositionCursor;

    /// <summary>Gets or sets the process-level input method switch used by native hosts.</summary>
    public static bool IsInputMethodEnabled
    {
        get => s_globallyEnabled;
        set
        {
            s_globallyEnabled = value;
            if (!value && IsComposing)
                CancelComposition();
        }
    }

    public InputMethodState ImeState
    {
        get => _imeState;
        set => SetState(ref _imeState, value, InputMethodStateType.ImeState);
    }

    public ImeConversionModeValues ImeConversionMode
    {
        get => _imeConversionMode;
        set => SetState(ref _imeConversionMode, value, InputMethodStateType.ImeConversionMode);
    }

    public ImeSentenceModeValues ImeSentenceMode
    {
        get => _imeSentenceMode;
        set => SetState(ref _imeSentenceMode, value, InputMethodStateType.ImeSentenceMode);
    }

    public InputMethodState HandwritingState
    {
        get => _handwritingState;
        set => SetState(ref _handwritingState, value, InputMethodStateType.HandwritingState);
    }

    public SpeechMode SpeechMode
    {
        get => _speechMode;
        set => SetState(ref _speechMode, value, InputMethodStateType.SpeechMode);
    }

    public InputMethodState MicrophoneState
    {
        get => _microphoneState;
        set => SetState(ref _microphoneState, value, InputMethodStateType.MicrophoneState);
    }

    /// <summary>Native configuration UI is unavailable until a platform provider advertises it.</summary>
    public bool CanShowConfigurationUI => false;
    public bool CanShowRegisterWordUI => false;

    public event InputMethodStateChangedEventHandler? StateChanged
    {
        add
        {
            ArgumentNullException.ThrowIfNull(value);
            _stateChanged += value;
        }
        remove
        {
            ArgumentNullException.ThrowIfNull(value);
            _stateChanged -= value;
        }
    }

    public static event EventHandler? CompositionStarted;
    public static event EventHandler<CompositionEventArgs>? CompositionUpdated;
    public static event EventHandler<CompositionResultEventArgs>? CompositionEnded;

    public static void SetTarget(IInputElement? element)
    {
        InputMethod inputMethod = Current;
        if (ReferenceEquals(inputMethod._currentTarget, element))
            return;
        if (inputMethod._isComposing)
            EndComposition();
        inputMethod._currentTarget = element;
        if (element is DependencyObject dependencyObject)
            inputMethod.ApplyPreferredState(dependencyObject);
    }

    public static void StartComposition()
    {
        InputMethod inputMethod = Current;
        if (!s_globallyEnabled ||
            inputMethod._currentTarget is DependencyObject target &&
            (!GetIsInputMethodEnabled(target) || GetIsInputMethodSuspended(target)))
        {
            return;
        }
        if (inputMethod._isComposing)
            return;
        inputMethod._isComposing = true;
        inputMethod._compositionString = string.Empty;
        inputMethod._compositionCursor = 0;
        CompositionStarted?.Invoke(null, EventArgs.Empty);
    }

    public static void UpdateComposition(string text, int cursor)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (cursor < 0 || cursor > text.Length)
            throw new ArgumentOutOfRangeException(nameof(cursor));
        InputMethod inputMethod = Current;
        if (!inputMethod._isComposing)
            StartComposition();
        if (!inputMethod._isComposing)
            return;
        inputMethod._compositionString = text;
        inputMethod._compositionCursor = cursor;
        CompositionUpdated?.Invoke(null, new CompositionEventArgs(text, cursor));
    }

    public static void EndComposition(string? result = null)
    {
        InputMethod inputMethod = Current;
        if (!inputMethod._isComposing)
            return;
        inputMethod._isComposing = false;
        inputMethod._compositionString = string.Empty;
        inputMethod._compositionCursor = 0;
        CompositionEnded?.Invoke(null, new CompositionResultEventArgs(result));
    }

    public static void CancelComposition() => EndComposition(null);

    public void ShowConfigureUI() => ShowConfigureUI(null!);
    public void ShowConfigureUI(UIElement element)
    {
        VerifyAccess();
        // A platform provider may implement this in the future.  The capability property
        // truthfully remains false and calling the WPF-compatible method is a no-op.
    }

    public void ShowRegisterWordUI() => ShowRegisterWordUI(string.Empty);
    public void ShowRegisterWordUI(string registeredText) => ShowRegisterWordUI(null!, registeredText);
    public void ShowRegisterWordUI(UIElement element, string registeredText)
    {
        VerifyAccess();
    }

    public static readonly DependencyProperty InputScopeProperty =
        FrameworkElement.InputScopeProperty.AddOwner(typeof(InputMethod));

    public static readonly DependencyProperty IsInputMethodEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsInputMethodEnabled",
            typeof(bool),
            typeof(InputMethod),
            new PropertyMetadata(true));

    public static readonly DependencyProperty IsInputMethodSuspendedProperty =
        DependencyProperty.RegisterAttached(
            "IsInputMethodSuspended",
            typeof(bool),
            typeof(InputMethod),
            new PropertyMetadata(false));

    public static readonly DependencyProperty PreferredImeStateProperty =
        DependencyProperty.RegisterAttached(
            "PreferredImeState",
            typeof(InputMethodState),
            typeof(InputMethod),
            new PropertyMetadata(InputMethodState.DoNotCare));

    public static readonly DependencyProperty PreferredImeConversionModeProperty =
        DependencyProperty.RegisterAttached(
            "PreferredImeConversionMode",
            typeof(ImeConversionModeValues),
            typeof(InputMethod),
            new PropertyMetadata(ImeConversionModeValues.DoNotCare));

    public static readonly DependencyProperty PreferredImeSentenceModeProperty =
        DependencyProperty.RegisterAttached(
            "PreferredImeSentenceMode",
            typeof(ImeSentenceModeValues),
            typeof(InputMethod),
            new PropertyMetadata(ImeSentenceModeValues.DoNotCare));

    public static bool GetIsInputMethodEnabled(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsInputMethodEnabledProperty) is true;
    }

    public static void SetIsInputMethodEnabled(DependencyObject target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsInputMethodEnabledProperty, value);
        if (!value && ReferenceEquals(CurrentTarget, target) && IsComposing)
            CancelComposition();
    }

    public static bool GetIsInputMethodSuspended(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsInputMethodSuspendedProperty) is true;
    }

    public static void SetIsInputMethodSuspended(DependencyObject target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsInputMethodSuspendedProperty, value);
        if (value && ReferenceEquals(CurrentTarget, target) && IsComposing)
            CancelComposition();
    }

    public static InputMethodState GetPreferredImeState(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (InputMethodState)target.GetValue(PreferredImeStateProperty)!;
    }

    public static void SetPreferredImeState(DependencyObject target, InputMethodState value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(PreferredImeStateProperty, value);
    }

    public static ImeConversionModeValues GetPreferredImeConversionMode(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (ImeConversionModeValues)target.GetValue(PreferredImeConversionModeProperty)!;
    }

    public static void SetPreferredImeConversionMode(DependencyObject target, ImeConversionModeValues value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(PreferredImeConversionModeProperty, value);
    }

    public static ImeSentenceModeValues GetPreferredImeSentenceMode(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (ImeSentenceModeValues)target.GetValue(PreferredImeSentenceModeProperty)!;
    }

    public static void SetPreferredImeSentenceMode(DependencyObject target, ImeSentenceModeValues value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(PreferredImeSentenceModeProperty, value);
    }

    public static InputScope GetInputScope(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (InputScope)target.GetValue(InputScopeProperty)!;
    }

    public static void SetInputScope(DependencyObject target, InputScope value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(value);
        target.SetValue(InputScopeProperty, value);
    }

    private void ApplyPreferredState(DependencyObject target)
    {
        InputMethodState preferredState = GetPreferredImeState(target);
        if (preferredState != InputMethodState.DoNotCare)
            ImeState = preferredState;
        ImeConversionModeValues conversion = GetPreferredImeConversionMode(target);
        if ((conversion & ImeConversionModeValues.DoNotCare) == 0)
            ImeConversionMode = conversion;
        ImeSentenceModeValues sentence = GetPreferredImeSentenceMode(target);
        if ((sentence & ImeSentenceModeValues.DoNotCare) == 0)
            ImeSentenceMode = sentence;
    }

    private void SetState<T>(ref T storage, T value, InputMethodStateType stateType) where T : struct, Enum
    {
        VerifyAccess();
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return;
        storage = value;
        _stateChanged?.Invoke(this, new InputMethodStateChangedEventArgs(stateType));
    }
}

/// <summary>
/// Specifies the preferred IME state.
/// </summary>
public enum InputMethodState
{
    Off = 0,
    On = 1,
    DoNotCare = 2,
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
