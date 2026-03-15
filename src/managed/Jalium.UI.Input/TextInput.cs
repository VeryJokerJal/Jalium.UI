using Jalium.UI;

namespace Jalium.UI.Input;

/// <summary>
/// IME conversion mode flags.
/// </summary>
[Flags]
public enum ImeConversionModeValues
{
    DoNotCare = 0,
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
}

/// <summary>
/// IME sentence mode flags.
/// </summary>
[Flags]
public enum ImeSentenceModeValues
{
    DoNotCare = 0,
    None = 0x1,
    PluralClause = 0x2,
    SingleConversion = 0x4,
    Automatic = 0x8,
    Conversation = 0x10,
    PhrasePrediction = 0x20,
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
public sealed class InputLanguageManager
{
    private static readonly InputLanguageManager _current = new();
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

    public System.Globalization.CultureInfo? CurrentInputLanguage { get; set; }
    public IEnumerable<System.Globalization.CultureInfo> AvailableInputLanguages => Array.Empty<System.Globalization.CultureInfo>();

    public event EventHandler<InputLanguageEventArgs>? InputLanguageChanged;
    public event EventHandler<InputLanguageEventArgs>? InputLanguageChanging;
}

/// <summary>
/// Provides data for input language change events.
/// </summary>
public sealed class InputLanguageEventArgs : EventArgs
{
    public InputLanguageEventArgs(System.Globalization.CultureInfo newLanguage, System.Globalization.CultureInfo previousLanguage)
    {
        NewLanguage = newLanguage;
        PreviousLanguage = previousLanguage;
    }
    public System.Globalization.CultureInfo NewLanguage { get; }
    public System.Globalization.CultureInfo PreviousLanguage { get; }
    public bool Rejected { get; set; }
}
