using System.Runtime.Versioning;
using Android.Content;
using Android.Runtime;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input.TextInput;

namespace Jalium.UI;

/// <summary>
/// A non-drawing, focusable <see cref="View"/> that serves as the Android IME
/// anchor for Jalium's self-drawn editors. It reports itself as a text editor,
/// builds an <see cref="EditorInfo"/> (keyboard type + Return key) from the
/// current <see cref="AndroidImeState"/>, and hands the IME a
/// <see cref="JaliumInputConnection"/>. Touches never reach it — the Activity
/// consumes them at dispatch — so it exists purely to host the system keyboard.
/// </summary>
[SupportedOSPlatform("android24.0")]
internal sealed class JaliumTextInputView : View
{
    private JaliumInputConnection? _connection;

    public JaliumTextInputView(Context context) : base(context)
    {
        Focusable = true;
        FocusableInTouchMode = true;
        SetWillNotDraw(true);
    }

    /// <summary>The IME state currently applied to this view.</summary>
    public AndroidImeState CurrentState { get; private set; } = AndroidImeState.Disabled;

    /// <summary>
    /// Raised on the UI thread after the on-screen keyboard's action key was
    /// pressed, letting the host decide whether to hide the keyboard.
    /// </summary>
    public event Action<TextInputReturnKeyType>? EditorAction;

    public override bool OnCheckIsTextEditor() => CurrentState.Enabled;

    /// <summary>Stores a new IME state and re-syncs the live input connection's mirror.</summary>
    public void ApplyState(AndroidImeState state)
    {
        CurrentState = state;
        _connection?.SetSurroundingText(state.SurroundingText, state.SelectionStart, state.SelectionEnd);
    }

    public override IInputConnection? OnCreateInputConnection(EditorInfo? outAttrs)
    {
        if (outAttrs == null || !CurrentState.Enabled)
            return null;

        ConfigureEditorInfo(outAttrs, CurrentState);
        _connection = new JaliumInputConnection(this);
        return _connection;
    }

    internal void PerformImeAction(ImeAction actionCode)
    {
        TextInputReturnKeyType action = MapFromImeAction(actionCode, CurrentState.ReturnKeyType);
        AndroidActivityBridge.InjectImeEditorAction(action);
        EditorAction?.Invoke(action);
    }

    /// <summary>Computes the Android input type bits for a state (also used to detect shape changes).</summary>
    internal static InputTypes ComputeInputType(AndroidImeState state)
    {
        switch (state.ContentType)
        {
            case TextInputContentType.Telephone:
                return InputTypes.ClassPhone;

            case TextInputContentType.Digits:
                return InputTypes.ClassNumber;

            case TextInputContentType.Pin:
                return InputTypes.ClassNumber | InputTypes.NumberVariationPassword;

            case TextInputContentType.Number:
                return InputTypes.ClassNumber | InputTypes.NumberFlagDecimal | InputTypes.NumberFlagSigned;
        }

        InputTypes type = state.ContentType switch
        {
            TextInputContentType.Email => InputTypes.ClassText | InputTypes.TextVariationEmailAddress,
            TextInputContentType.Url => InputTypes.ClassText | InputTypes.TextVariationUri,
            TextInputContentType.Password => InputTypes.ClassText | InputTypes.TextVariationPassword,
            TextInputContentType.Name => InputTypes.ClassText | InputTypes.TextVariationPersonName,
            _ => InputTypes.ClassText,
        };

        if (state.Multiline)
            type |= InputTypes.TextFlagMultiLine;

        // Password variations already suppress suggestions; only add the flag for
        // ordinary text so Chinese prediction stays available unless opted out.
        if (!state.ShowSuggestions && state.ContentType != TextInputContentType.Password)
            type |= InputTypes.TextFlagNoSuggestions;

        if (state.Uppercase)
            type |= InputTypes.TextFlagCapCharacters;
        else if (state.AutoCapitalization)
            type |= state.ContentType == TextInputContentType.Name
                ? InputTypes.TextFlagCapWords
                : InputTypes.TextFlagCapSentences;

        return type;
    }

    private static void ConfigureEditorInfo(EditorInfo outAttrs, AndroidImeState state)
    {
        outAttrs.InputType = ComputeInputType(state);
        outAttrs.ImeOptions = MapToImeAction(state.ReturnKeyType)
            | ImeFlags.NoFullscreen
            | ImeFlags.NoExtractUi;
        outAttrs.InitialSelStart = state.SelectionStart;
        outAttrs.InitialSelEnd = state.SelectionEnd;
    }

    private static ImeFlags MapToImeAction(TextInputReturnKeyType returnKey)
    {
        ImeAction action = returnKey switch
        {
            TextInputReturnKeyType.Done => ImeAction.Done,
            TextInputReturnKeyType.Go => ImeAction.Go,
            TextInputReturnKeyType.Send => ImeAction.Send,
            TextInputReturnKeyType.Search => ImeAction.Search,
            TextInputReturnKeyType.Next => ImeAction.Next,
            TextInputReturnKeyType.Previous => ImeAction.Previous,
            TextInputReturnKeyType.Return => ImeAction.None,
            _ => ImeAction.Unspecified,
        };

        return (ImeFlags)(int)action;
    }

    private static TextInputReturnKeyType MapFromImeAction(ImeAction action, TextInputReturnKeyType fallback)
    {
        return action switch
        {
            ImeAction.Next => TextInputReturnKeyType.Next,
            ImeAction.Previous => TextInputReturnKeyType.Previous,
            ImeAction.Done => TextInputReturnKeyType.Done,
            ImeAction.Go => TextInputReturnKeyType.Go,
            ImeAction.Send => TextInputReturnKeyType.Send,
            ImeAction.Search => TextInputReturnKeyType.Search,
            _ => fallback == TextInputReturnKeyType.Default ? TextInputReturnKeyType.Done : fallback,
        };
    }
}
