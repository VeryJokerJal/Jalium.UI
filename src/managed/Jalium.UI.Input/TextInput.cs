using Jalium.UI;

using Jalium.UI.Threading;

namespace Jalium.UI.Input;

/// <summary>
/// IME conversion mode flags.
/// </summary>
[Flags]
public enum ImeConversionModeValues
{
    Native = 0x1,
    Katakana = 0x2,
    FullShape = 0x4,
    Roman = 0x8,
    CharCode = 0x10,
    NoConversion = 0x20,
    Eudc = 0x40,
    Symbol = 0x80,
    Fixed = 0x100,
    Alphanumeric = 0x200,
    DoNotCare = int.MinValue,
}

/// <summary>
/// IME sentence mode flags.
/// </summary>
[Flags]
public enum ImeSentenceModeValues
{
    None = 0,
    PluralClause = 0x1,
    SingleConversion = 0x2,
    Automatic = 0x4,
    PhrasePrediction = 0x8,
    Conversation = 0x10,
    DoNotCare = int.MinValue,
}

/// <summary>Specifies the speech recognizer's current operating mode.</summary>
public enum SpeechMode
{
    Dictation = 0,
    Command = 1,
    Indeterminate = 2,
}

/// <summary>
/// Provides data for InputMethod state changes.
/// </summary>
public sealed class InputMethodStateChangedEventArgs : EventArgs
{
    public InputMethodStateChangedEventArgs(InputMethodStateType stateType)
    {
        StateType = stateType;
    }
    public InputMethodStateType StateType { get; }
    public bool IsImeStateChanged => StateType == InputMethodStateType.ImeState;
    public bool IsImeConversionModeChanged => StateType == InputMethodStateType.ImeConversionMode;
    public bool IsImeSentenceModeChanged => StateType == InputMethodStateType.ImeSentenceMode;
    public bool IsHandwritingStateChanged => StateType == InputMethodStateType.HandwritingState;
    public bool IsSpeechModeChanged => StateType == InputMethodStateType.SpeechMode;
    public bool IsMicrophoneStateChanged => StateType == InputMethodStateType.MicrophoneState;
}

/// <summary>
/// Specifies the type of InputMethod state that changed.
/// </summary>
public enum InputMethodStateType
{
    ImeState,
    ImeConversionMode,
    ImeSentenceMode,
    HandwritingState,
    SpeechMode,
    MicrophoneState
}

/// <summary>
/// Delegate for InputMethod state changed events.
/// </summary>
public delegate void InputMethodStateChangedEventHandler(object sender, InputMethodStateChangedEventArgs e);

/// <summary>
/// Manages input language for the application.
/// </summary>
public sealed class InputLanguageManager : DispatcherObject
{
    private static readonly InputLanguageManager _current = new();
    private IInputLanguageSource? _source;
    private System.Globalization.CultureInfo _currentInputLanguage =
        System.Globalization.CultureInfo.CurrentCulture;

    private InputLanguageManager()
    {
    }

    public static InputLanguageManager Current => _current;

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty InputLanguageProperty =
        DependencyProperty.RegisterAttached("InputLanguage", typeof(System.Globalization.CultureInfo), typeof(InputLanguageManager), new PropertyMetadata(null));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty RestoreInputLanguageProperty =
        DependencyProperty.RegisterAttached("RestoreInputLanguage", typeof(bool), typeof(InputLanguageManager), new PropertyMetadata(false));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static System.Globalization.CultureInfo? GetInputLanguage(DependencyObject element) => (System.Globalization.CultureInfo?)element.GetValue(InputLanguageProperty);
    public static void SetInputLanguage(DependencyObject element, System.Globalization.CultureInfo? value) => element.SetValue(InputLanguageProperty, value);
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static bool GetRestoreInputLanguage(DependencyObject element) => (bool)(element.GetValue(RestoreInputLanguageProperty) ?? false);
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static void SetRestoreInputLanguage(DependencyObject element, bool value) => element.SetValue(RestoreInputLanguageProperty, value);

    public System.Globalization.CultureInfo CurrentInputLanguage
    {
        get => _source?.CurrentInputLanguage ?? _currentInputLanguage;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var previous = CurrentInputLanguage;
            if (Equals(previous, value))
                return;
            if (!ReportInputLanguageChanging(value, previous))
                return;

            if (_source is not null)
                _source.CurrentInputLanguage = value;
            _currentInputLanguage = value;
            ReportInputLanguageChanged(value, previous);
        }
    }

    public System.Collections.IEnumerable AvailableInputLanguages =>
        _source?.InputLanguageList ?? new[] { CurrentInputLanguage };

    public event InputLanguageEventHandler? InputLanguageChanged;
    public event InputLanguageEventHandler? InputLanguageChanging;

    /// <summary>Registers and initializes the platform input-language source.</summary>
    public void RegisterInputLanguageSource(IInputLanguageSource inputLanguageSource)
    {
        ArgumentNullException.ThrowIfNull(inputLanguageSource);
        if (ReferenceEquals(_source, inputLanguageSource))
            return;

        _source?.Uninitialize();
        _source = inputLanguageSource;
        _source.Initialize();
        _currentInputLanguage = _source.CurrentInputLanguage;
    }

    /// <summary>Reports a proposed language change and returns whether it was accepted.</summary>
    public bool ReportInputLanguageChanging(
        System.Globalization.CultureInfo newLanguageId,
        System.Globalization.CultureInfo previousLanguageId)
    {
        ArgumentNullException.ThrowIfNull(newLanguageId);
        ArgumentNullException.ThrowIfNull(previousLanguageId);
        var args = new InputLanguageChangingEventArgs(newLanguageId, previousLanguageId);
        InputLanguageChanging?.Invoke(this, args);
        return !args.Rejected;
    }

    /// <summary>Reports that a language change has completed.</summary>
    public void ReportInputLanguageChanged(
        System.Globalization.CultureInfo newLanguageId,
        System.Globalization.CultureInfo previousLanguageId)
    {
        ArgumentNullException.ThrowIfNull(newLanguageId);
        ArgumentNullException.ThrowIfNull(previousLanguageId);
        _currentInputLanguage = newLanguageId;
        InputLanguageChanged?.Invoke(
            this,
            new InputLanguageChangedEventArgs(newLanguageId, previousLanguageId));
    }
}

/// <summary>Defines a platform source of available and current input languages.</summary>
public interface IInputLanguageSource
{
    System.Globalization.CultureInfo CurrentInputLanguage { get; set; }
    System.Collections.IEnumerable InputLanguageList { get; }
    void Initialize();
    void Uninitialize();
}

/// <summary>Handles input-language transition notifications.</summary>
public delegate void InputLanguageEventHandler(object sender, InputLanguageEventArgs e);

/// <summary>
/// Provides data for input language change events.
/// </summary>
public abstract partial class InputLanguageEventArgs : EventArgs
{
    protected InputLanguageEventArgs(
        System.Globalization.CultureInfo newLanguageId,
        System.Globalization.CultureInfo previousLanguageId)
    {
        NewLanguage = newLanguageId ?? throw new ArgumentNullException(nameof(newLanguageId));
        PreviousLanguage = previousLanguageId ?? throw new ArgumentNullException(nameof(previousLanguageId));
    }

    public virtual System.Globalization.CultureInfo NewLanguage { get; }
    public virtual System.Globalization.CultureInfo PreviousLanguage { get; }
}

/// <summary>Provides data before an input-language transition is committed.</summary>
public sealed class InputLanguageChangingEventArgs : InputLanguageEventArgs
{
    public InputLanguageChangingEventArgs(
        System.Globalization.CultureInfo newLanguageId,
        System.Globalization.CultureInfo previousLanguageId)
        : base(newLanguageId, previousLanguageId)
    {
    }

    public bool Rejected { get; set; }
}

/// <summary>Provides data after an input-language transition completes.</summary>
public sealed class InputLanguageChangedEventArgs : InputLanguageEventArgs
{
    public InputLanguageChangedEventArgs(
        System.Globalization.CultureInfo newLanguageId,
        System.Globalization.CultureInfo previousLanguageId)
        : base(newLanguageId, previousLanguageId)
    {
    }
}
