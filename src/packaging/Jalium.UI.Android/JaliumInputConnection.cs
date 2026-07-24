using System.Runtime.Versioning;
using System.Text;
using Android.Runtime;
using Android.Views;
using Android.Views.InputMethods;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI;

/// <summary>
/// A virtual <see cref="InputConnection"/> for Jalium's self-drawn editors. It
/// keeps a local mirror of the focused editor's text/selection so the IME can
/// query surrounding text, and forwards composition, commit, delete and editor
/// actions to the Jalium UI thread through <see cref="AndroidActivityBridge"/>.
/// This is what makes Chinese/CJK predictive input, candidate selection, copy,
/// paste, and Next/Done Return-key actions work with the system keyboard.
/// </summary>
/// <remarks>
/// The mirror is authoritative only between an IME edit and the asynchronous
/// re-sync that arrives via <see cref="SetSurroundingText"/> once the editor has
/// applied the change. Method names follow the .NET for Android binding: text
/// <em>parameters</em> use <c>ICharSequence</c> directly, while text
/// <em>results</em> use the <c>*Formatted</c> variants.
/// </remarks>
[SupportedOSPlatform("android24.0")]
internal sealed class JaliumInputConnection : BaseInputConnection
{
    private readonly JaliumTextInputView _owner;
    private readonly StringBuilder _text = new();
    private int _selStart;
    private int _selEnd;
    private int _composingStart = -1;
    private int _composingEnd = -1;

    public JaliumInputConnection(JaliumTextInputView owner)
        : base(owner, fullEditor: true)
    {
        _owner = owner;
        AndroidImeState state = owner.CurrentState;
        SetSurroundingText(state.SurroundingText, state.SelectionStart, state.SelectionEnd);
    }

    /// <summary>Re-syncs the mirror from the authoritative Jalium editor state.</summary>
    public void SetSurroundingText(string? text, int selStart, int selEnd)
    {
        _text.Clear();
        _text.Append(text ?? string.Empty);
        int len = _text.Length;
        _selStart = Clamp(selStart, len);
        _selEnd = Clamp(selEnd, len);
        _composingStart = -1;
        _composingEnd = -1;
    }

    private static int Clamp(int value, int len) => value < 0 ? 0 : (value > len ? len : value);

    private int SelMin => Math.Min(_selStart, _selEnd);
    private int SelMax => Math.Max(_selStart, _selEnd);

    public override bool SetComposingText(Java.Lang.ICharSequence? text, int newCursorPosition)
    {
        string ins = text?.ToString() ?? string.Empty;
        int start = _composingStart >= 0 ? _composingStart : SelMin;
        int end = _composingStart >= 0 ? _composingEnd : SelMax;
        start = Clamp(start, _text.Length);
        end = Clamp(end, _text.Length);

        _text.Remove(start, end - start);
        _text.Insert(start, ins);
        _composingStart = start;
        _composingEnd = start + ins.Length;
        int caret = newCursorPosition > 0 ? _composingEnd : start;
        _selStart = _selEnd = Clamp(caret, _text.Length);

        AndroidActivityBridge.InjectImeComposition(ins, ins.Length);
        return true;
    }

    public override bool CommitText(Java.Lang.ICharSequence? text, int newCursorPosition)
    {
        string ins = text?.ToString() ?? string.Empty;
        int start = _composingStart >= 0 ? _composingStart : SelMin;
        int end = _composingStart >= 0 ? _composingEnd : SelMax;
        start = Clamp(start, _text.Length);
        end = Clamp(end, _text.Length);

        _text.Remove(start, end - start);
        _text.Insert(start, ins);
        _composingStart = -1;
        _composingEnd = -1;
        int caret = newCursorPosition > 0 ? start + ins.Length : start;
        _selStart = _selEnd = Clamp(caret, _text.Length);

        AndroidActivityBridge.InjectImeCommit(ins);
        return true;
    }

    public override bool FinishComposingText()
    {
        _composingStart = -1;
        _composingEnd = -1;
        AndroidActivityBridge.InjectImeFinishComposing();
        return true;
    }

    public override bool DeleteSurroundingText(int beforeLength, int afterLength)
    {
        // Match the editor's collapsed-caret-only delete policy: with an active
        // selection the managed side keeps the selection (it deletes via key
        // events), so mutating the mirror here would desync it from the editor.
        if (SelMin != SelMax)
            return true;

        int before = Math.Max(0, beforeLength);
        int after = Math.Max(0, afterLength);
        int caret = SelMin;

        int afterEnd = Math.Min(_text.Length, caret + after);
        if (afterEnd > caret)
            _text.Remove(caret, afterEnd - caret);

        int beforeStart = Math.Max(0, caret - before);
        int removedBefore = caret - beforeStart;
        if (removedBefore > 0)
            _text.Remove(beforeStart, removedBefore);

        _selStart = _selEnd = Clamp(caret - removedBefore, _text.Length);
        _composingStart = -1;
        _composingEnd = -1;

        AndroidActivityBridge.InjectImeDeleteSurrounding(before, after);
        return true;
    }

    public override bool SendKeyEvent(KeyEvent? e)
    {
        if (e == null)
            return true;

        int action = e.Action switch
        {
            KeyEventActions.Down => 0,
            KeyEventActions.Up => 1,
            _ => -1,
        };

        if (action >= 0)
        {
            AndroidActivityBridge.InjectKey(
                (int)e.KeyCode, e.ScanCode, action, (int)e.MetaState, e.RepeatCount);

            if (e.Action == KeyEventActions.Down)
            {
                int unicode = e.GetUnicodeChar((MetaKeyStates)e.MetaState);
                if (unicode > 0)
                    AndroidActivityBridge.InjectChar((uint)unicode);
            }
        }

        return true;
    }

    public override bool SetSelection(int start, int end)
    {
        _selStart = Clamp(start, _text.Length);
        _selEnd = Clamp(end, _text.Length);
        return true;
    }

    public override bool SetComposingRegion(int start, int end)
    {
        _composingStart = Clamp(Math.Min(start, end), _text.Length);
        _composingEnd = Clamp(Math.Max(start, end), _text.Length);
        return true;
    }

    public override bool PerformEditorAction([GeneratedEnum] ImeAction actionCode)
    {
        _owner.PerformImeAction(actionCode);
        return true;
    }

    public override Java.Lang.ICharSequence? GetTextBeforeCursorFormatted(int length, [GeneratedEnum] GetTextFlags flags)
    {
        int selMin = SelMin;
        int start = Math.Max(0, selMin - Math.Max(0, length));
        return new Java.Lang.String(_text.ToString(start, selMin - start));
    }

    public override Java.Lang.ICharSequence? GetTextAfterCursorFormatted(int length, [GeneratedEnum] GetTextFlags flags)
    {
        int selMax = SelMax;
        // Clamp the request to available length before adding, so a very large
        // `length` cannot overflow selMax + length into a negative substring.
        int take = Math.Min(Math.Max(0, length), _text.Length - selMax);
        return new Java.Lang.String(_text.ToString(selMax, take));
    }

    public override Java.Lang.ICharSequence? GetSelectedTextFormatted([GeneratedEnum] GetTextFlags flags)
    {
        if (SelMax <= SelMin)
            return null;
        return new Java.Lang.String(_text.ToString(SelMin, SelMax - SelMin));
    }

    public override ExtractedText? GetExtractedText(ExtractedTextRequest? request, [GeneratedEnum] GetTextFlags flags)
    {
        return new ExtractedText
        {
            Text = new Java.Lang.String(_text.ToString()),
            StartOffset = 0,
            SelectionStart = SelMin,
            SelectionEnd = SelMax,
            PartialStartOffset = -1,
            PartialEndOffset = -1,
            Flags = 0,
        };
    }
}
