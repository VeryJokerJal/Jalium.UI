using Jalium.UI.Input.TextInput;

namespace Jalium.UI.Controls.Platform;

/// <summary>
/// Immutable snapshot of the managed IME state that the Jalium UI thread pushes
/// toward the Android system soft keyboard. It is produced on the Jalium UI
/// thread by the focused editor's context and consumed on the Android main
/// thread by an <see cref="IAndroidSoftKeyboardController"/> implementation.
/// </summary>
/// <remarks>
/// Text offsets are managed UTF-16 indices into <see cref="SurroundingText"/>.
/// Unlike the Wayland transport, the Android input connection operates on
/// UTF-16 directly, so no UTF-8 byte conversion is required here.
/// </remarks>
public readonly record struct AndroidImeState(
    bool Enabled,
    TextInputContentType ContentType,
    TextInputReturnKeyType ReturnKeyType,
    bool Multiline,
    bool ShowSuggestions,
    bool AutoCapitalization,
    bool Lowercase,
    bool Uppercase,
    string SurroundingText,
    int SelectionStart,
    int SelectionEnd)
{
    /// <summary>A disabled state instructing the controller to hide the keyboard.</summary>
    public static AndroidImeState Disabled { get; } = new(
        Enabled: false,
        ContentType: TextInputContentType.Normal,
        ReturnKeyType: TextInputReturnKeyType.Default,
        Multiline: false,
        ShowSuggestions: true,
        AutoCapitalization: false,
        Lowercase: false,
        Uppercase: false,
        SurroundingText: string.Empty,
        SelectionStart: 0,
        SelectionEnd: 0);
}

/// <summary>
/// Bridges the managed IME state to the Android <c>InputMethodManager</c>. The
/// Android entry package (<c>JaliumActivity</c>) registers an implementation via
/// <see cref="AndroidActivityBridge.SetSoftKeyboardController"/>. Implementations
/// receive <see cref="UpdateImeState"/> on the Jalium UI thread and are
/// responsible for marshalling onto the Android main thread before touching any
/// <c>View</c> / <c>InputMethodManager</c> state.
/// </summary>
public interface IAndroidSoftKeyboardController
{
    /// <summary>
    /// Applies a new IME state: shows, hides, or reconfigures the system soft
    /// keyboard so it matches the currently focused Jalium editor.
    /// </summary>
    void UpdateImeState(AndroidImeState state);
}
