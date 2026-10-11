using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Controls.Primitives;
using System.Diagnostics;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSWordSegmentationTests : MacOSGeometryTestBase
{
    private static readonly MethodInfo PlatformEventMethod = typeof(Window).GetMethod(
        "OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static IEnumerable<object[]> NativeWordCases()
    {
        // Observed with NSTextView moveWordRight/Left in an unshown own-process
        // reference host. Keyboard word navigation skips non-word emoji.
        foreach (int kind in new[] { 0, 1, 2 })
        foreach (bool shift in new[] { false, true })
        foreach (var (text, initial, right, expected) in new[]
        {
            ("中文输入窗口行为验证", 0, true, 2),
            ("日本語の編集動作", 0, true, 2),
            ("ภาษาไทยยินดีต้อนรับ", 0, true, 7),
            ("don't stop foo_bar", 0, true, 5),
            ("one 👩‍👩‍👧‍👦 two", 4, true, 19),
            ("שלום עולם", 0, false, 4),
        })
            yield return [kind, text, initial, right, expected, shift];
    }

    [Theory]
    [MemberData(nameof(NativeWordCases))]
    public void OptionArrow_UsesSystemWordAndPhysicalDirection(
        int kind, string text, int initial, bool right, int expected, bool shift)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var editor = CreateEditor(kind, text);
        var window = new DisplayedTestWindow { Content = editor };
        try
        {
            var support = (IImeSupport)editor;
            Assert.True(support.TrySetImeSelection(initial, 0));
            Assert.True(editor.Focus());
            Dispatch(window, right ? 0x27 : 0x25, 4 | (shift ? 1 : 0));
            Assert.True(support.TryGetImeSurroundingText(out var after));
            Assert.Equal(expected, after.CursorIndex);
            Assert.Equal(shift ? initial : expected, after.AnchorIndex);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    public static IEnumerable<object[]> DeleteCases()
    {
        foreach (int kind in new[] { 0, 1, 2 })
        foreach (bool readOnly in new[] { false, true })
        foreach (var (text, initial, forward, expected) in new[]
        {
            ("中文输入窗口", 0, true, "输入窗口"),
            ("日本語の編集", 0, true, "語の編集"),
            ("ภาษาไทยยินดีต้อนรับ", 0, true, "ยินดีต้อนรับ"),
            ("don't stop", 0, true, " stop"),
            ("one 👩‍👩‍👧‍👦 two", 4, true, "one "),
            ("שלום עולם", 4, false, " עולם"),
        }) yield return [kind, text, initial, forward, expected, readOnly];
    }

    [Theory]
    [MemberData(nameof(DeleteCases))]
    public void OptionDelete_UsesLogicalWordsAndRestoresTextThroughUndo(
        int kind, string text, int initial, bool forward, string expected, bool readOnly)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var editor = CreateEditor(kind, text);
        var window = new DisplayedTestWindow { Content = editor };
        try
        {
            Assert.True(((IImeSupport)editor).TrySetImeSelection(initial, 0));
            string before = ReadText(editor);
            if (editor is EditControl code) code.IsReadOnly = readOnly;
            else ((TextBoxBase)editor).IsReadOnly = readOnly;
            Assert.True(editor.Focus());
            Dispatch(window, forward ? 0x2e : 0x08, 4);
            Assert.Equal(readOnly ? before : expected + (kind == 2 ? Environment.NewLine : ""), ReadText(editor));
            if (!readOnly)
            {
                Dispatch(window, 0x5a, 8);
                Assert.Equal(before, ReadText(editor));
            }
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    public static IEnumerable<object[]> SelectionCases()
    {
        foreach (int kind in new[] { 0, 1, 2 })
        foreach (bool right in new[] { false, true })
        {
            yield return [kind, "alpha beta gamma", 5, right, right ? 10 : 0];
            yield return [kind, "שלום עולם", 4, right, right ? 0 : 9];
        }
    }

    [Theory]
    [MemberData(nameof(SelectionCases))]
    public void OptionArrow_FromSelectionMovesPastTheNativeEdge(
        int kind, string text, int length, bool right, int expected)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var editor = CreateEditor(kind, text); var window = new DisplayedTestWindow { Content = editor };
        try
        {
            Assert.True(((IImeSupport)editor).TrySetImeSelection(0, length)); Assert.True(editor.Focus());
            Dispatch(window, right ? 0x27 : 0x25, 4);
            Assert.True(((IImeSupport)editor).TryGetImeSurroundingText(out var after));
            Assert.Equal(expected, after.CursorIndex); Assert.Equal(expected, after.AnchorIndex);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    public static IEnumerable<object[]> MouseWordCases()
    {
        foreach (int kind in new[] { 0, 1, 2 })
        foreach (var (text, index, start, length) in new[]
        {
            ("中文输入窗口", 3, 2, 2),
            ("日本語の編集", 1, 0, 3),
            ("ภาษาไทยยินดีต้อนรับ", 8, 7, 5),
            ("don't stop", 2, 0, 5),
            ("one 👩‍👩‍👧‍👦 two", 4, 4, 11),
        }) yield return [kind, text, index, start, length];
    }

    [Theory]
    [MemberData(nameof(MouseWordCases))]
    public void MouseWordSelection_UsesTheSystemDoubleClickUnit(
        int kind, string text, int index, int start, int length)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var editor = CreateEditor(kind, text); var window = new DisplayedTestWindow { Content = editor };
        try
        {
            Assert.True(((IImeSupport)editor).TrySetImeSelection(index, 0)); Assert.True(editor.Focus());
            if (editor is RichTextBox rich)
                typeof(RichTextBox).GetMethod("SelectWordAt", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(rich, [rich.CaretPosition!]);
            else if (editor is EditControl code)
                typeof(EditControl).GetMethod("SelectWordAt", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(code, [index]);
            else typeof(TextBoxBase).GetMethod("SelectCurrentWord", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(editor, null);
            Assert.True(((IImeSupport)editor).TryGetImeSurroundingText(out var after));
            Assert.Equal(start, after.AnchorIndex); Assert.Equal(start + length, after.CursorIndex);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void LongDocumentWordQueries_DoNotRebuildTheTextSystemForEveryKey()
    {
        if (!OperatingSystem.IsMacOS()) return;
        string text = string.Concat(Enumerable.Repeat("中文输入窗口行为验证 ", 1024));
        var watch = Stopwatch.StartNew();
        int index = 0;
        for (int step = 0; step < 256; ++step)
        {
            int next = MacOSTextKeyBehavior.FindWordBoundary(text, index, forward: true, physical: true);
            Assert.True(next > index); Assert.Equal(next, GraphemeClusters.Snap(text, next, forward: false));
            index = next;
        }
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"word navigation took {watch.Elapsed}");
        Assert.Equal(5, MacOSTextKeyBehavior.FindWordBoundary("don't stop", 0, forward: true));
        Assert.Equal(2, MacOSTextKeyBehavior.FindWordBoundary(text, 0, forward: true));
    }

    private static string ReadText(UIElement editor) => editor switch
    {
        TextBox text => text.Text,
        EditControl code => code.Text,
        RichTextBox rich => rich.Document.GetText(),
        _ => throw new ArgumentOutOfRangeException(nameof(editor))
    };

    [Fact]
    public void RichText_ChangingDocumentAndExplicitSelectionRetiresOldAnchor()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var editor = new RichTextBox(FlowDocument.FromText("中文输入窗口"));
        var window = new DisplayedTestWindow { Content = editor };
        try
        {
            Assert.True(editor.Focus());
            Assert.True(((IImeSupport)editor).TrySetImeSelection(0, 0));
            Dispatch(window, 0x27, 5);
            Assert.Equal(2, editor.Selection.End.DocumentOffset);
            editor.Document = FlowDocument.FromText("日本語の編集");
            Assert.True(((IImeSupport)editor).TrySetImeSelection(0, 0));
            Dispatch(window, 0x27, 5);
            Assert.Same(editor.Document, editor.Selection.Start.Document);
            Assert.Same(editor.Document, editor.Selection.End.Document);
            Assert.Equal(0, editor.Selection.Start.DocumentOffset);
            Assert.Equal(2, editor.Selection.End.DocumentOffset);
            Assert.True(((IImeSupport)editor).TrySetImeSelection(4, 0));
            Dispatch(window, 0x25, 5);
            Assert.Equal(3, editor.Selection.Start.DocumentOffset);
            Assert.Equal(4, editor.Selection.End.DocumentOffset);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    private static UIElement CreateEditor(int kind, string text) => kind switch
    {
        0 => new TextBox { Text = text, AcceptsReturn = true },
        1 => new EditControl { Text = text },
        _ => new RichTextBox(FlowDocument.FromText(text))
    };

    private static void Dispatch(Window window, int key, int modifiers) =>
        PlatformEventMethod.Invoke(window, [new PlatformEvent
        { Type = PlatformEventType.KeyDown, KeyCode = key, Modifiers = modifiers }]);
}
