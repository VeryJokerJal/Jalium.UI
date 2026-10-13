using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSWindowTextNavigationTests : MacOSGeometryTestBase
{
    private static readonly MethodInfo PlatformEventMethod = typeof(Window).GetMethod(
        "OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static IEnumerable<object[]> NavigationCases()
    {
        foreach (int editor in new[] { 0, 1, 2 })
        foreach (bool shift in new[] { false, true })
        foreach (var (key, modifiers, destination) in new[]
        {
            (0x25, 0x08, 6), (0x27, 0x08, 18),
            (0x26, 0x08, 0), (0x28, 0x08, -1),
            (0x25, 0x04, 8), (0x27, 0x04, 18),
            (0x41, 0x02, 6), (0x45, 0x02, 18),
            (0x42, 0x02, 13), (0x46, 0x02, 15)
        })
            yield return [editor, key, modifiers | (shift ? 0x01 : 0), destination, shift];
    }

    [Theory]
    [MemberData(nameof(NavigationCases))]
    public void NativeNavigation_PreservesCaretAndSelectionAnchor(
        int editorKind, int key, int modifiers, int destination, bool shift)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var editor = CreateEditor(editorKind, "first\n  alpha beta\nlast");
        var window = new DisplayedTestWindow { Content = editor, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            // Command-arrow uses visual lines. Give CoreText the same finite
            // editing viewport that a presented Window provides.
            window.Measure(new Size(600, 400));
            window.Arrange(new Rect(0, 0, 600, 400));
            SetCaret(editor, 14);
            Assert.True(editor.Focus());
            Dispatch(window, key, modifiers);
            var snapshot = Snapshot(editor);
            int expected = destination < 0
                ? editor is RichTextBox rich ? rich.Document.ContentEnd.DocumentOffset : snapshot.Text.Length
                : destination;
            Assert.Equal(expected, snapshot.CursorIndex);
            Assert.Equal(shift ? 14 : expected, snapshot.AnchorIndex);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(0, false, false)] [InlineData(0, true, false)]
    [InlineData(1, false, false)] [InlineData(1, true, false)]
    [InlineData(2, false, false)] [InlineData(2, true, false)]
    [InlineData(0, false, true)] [InlineData(0, true, true)]
    [InlineData(1, false, true)] [InlineData(1, true, true)]
    [InlineData(2, false, true)] [InlineData(2, true, true)]
    public void OptionDelete_UsesWordBoundariesAndRespectsReadOnly(
        int editorKind, bool forward, bool readOnly)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var editor = CreateEditor(editorKind, "alpha beta gamma");
        if (editor is EditControl code) code.IsReadOnly = readOnly;
        else ((TextBoxBase)editor).IsReadOnly = readOnly;
        var window = new DisplayedTestWindow { Content = editor };
        try
        {
            SetCaret(editor, forward ? 6 : 10);
            Assert.True(editor.Focus());
            var before = ReadState(editor);
            Dispatch(window, forward ? 0x2e : 0x08, 0x04);
            var result = ReadState(editor);
            Assert.Equal(readOnly ? before.Text : "alpha  gamma" + (editorKind == 2 ? Environment.NewLine : ""), result.Text);
            Assert.Equal(readOnly ? (forward ? 6 : 10) : 6, result.Caret);
            if (readOnly)
                Assert.False(((IImeSupport)editor).TryGetImeSurroundingText(out _));
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OptionNavigation_NeverSplitsAnEmojiCluster(int editorKind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        const string emoji = "👩‍👩‍👧‍👦";
        var editor = CreateEditor(editorKind, "one " + emoji + " two");
        var window = new DisplayedTestWindow { Content = editor };
        try
        {
            SetCaret(editor, 4);
            Assert.True(editor.Focus());
            Dispatch(window, 0x27, 0x04);
            // TextKit keyboard word navigation skips emoji to the following
            // word, while native double-click selection still selects the
            // entire emoji. Every stop remains a whole composed cluster.
            Assert.Equal(4 + emoji.Length + " two".Length, Snapshot(editor).CursorIndex);
            Dispatch(window, 0x25, 0x04);
            Assert.Equal(5 + emoji.Length, Snapshot(editor).CursorIndex);
            Dispatch(window, 0x25, 0x04);
            Assert.Equal(0, Snapshot(editor).CursorIndex);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    public static IEnumerable<object[]> ModifierCases() =>
        Enumerable.Range(0, 16).Select(flags => new object[] { flags });

    [Theory]
    [MemberData(nameof(ModifierCases))]
    public void WindowKeyboardRoutes_PreservePhysicalModifiersForBothPhases(int flags)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var button = new Button();
        var window = new DisplayedTestWindow { Content = button };
        var events = new List<KeyEventArgs>();
        button.PreviewKeyDown += (_, e) => events.Add(e);
        button.KeyDown += (_, e) => events.Add(e);
        button.PreviewKeyUp += (_, e) => events.Add(e);
        button.KeyUp += (_, e) => events.Add(e);
        try
        {
            Assert.True(button.Focus());
            Dispatch(window, 0x4a, flags);
            Dispatch(window, 0x4a, flags, up: true);
            Assert.Equal(4, events.Count);
            var physical = ModifierKeys.None;
            if ((flags & 1) != 0) physical |= ModifierKeys.Shift;
            if ((flags & 2) != 0) physical |= ModifierKeys.Control;
            if ((flags & 4) != 0) physical |= ModifierKeys.Alt;
            if ((flags & 8) != 0) physical |= ModifierKeys.Windows;
            var compatible = physical & ~ModifierKeys.Windows;
            if ((flags & 8) != 0)
                compatible |= (flags & 2) == 0 ? ModifierKeys.Control : ModifierKeys.Windows;
            foreach (var e in events)
            {
                Assert.Equal(physical, Assert.IsType<ModifierKeys>(
                    typeof(KeyEventArgs).GetProperty("PhysicalModifiers")?.GetValue(e)));
                Assert.Equal(compatible, e.KeyboardModifiers);
            }
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void KeyRoutes_RejectFocusInAnotherWindow(bool up)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var otherButton = new Button();
        var source = new DisplayedTestWindow(); var other = new DisplayedTestWindow { Content = otherButton };
        int otherEvents = 0;
        otherButton.PreviewKeyDown += (_, _) => otherEvents++;
        otherButton.PreviewKeyUp += (_, _) => otherEvents++;
        try
        {
            Assert.True(otherButton.Focus());
            Dispatch(source, 0x4a, 0x08, up);
            Assert.Equal(0, otherEvents);
        }
        finally { Keyboard.ClearFocus(); source.Close(); other.Close(); }
    }

    [Theory]
    [InlineData(0x25, 0)] [InlineData(0x27, 9)]
    [InlineData(0x41, 0)] [InlineData(0x45, 9)]
    public void PasswordNavigation_KeepsSecureFieldBoundaries(int key, int expected)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var editor = new PasswordBox { Password = "secret😀x" };
        var window = new DisplayedTestWindow { Content = editor };
        try
        {
            editor.CaretIndex = 3;
            Assert.True(editor.Focus());
            Dispatch(window, key, key is 0x41 or 0x45 ? 0x02 : 0x08);
            Assert.Equal(expected, editor.CaretIndex);
            Assert.False(((IImeSupport)editor).TryGetImeSurroundingText(out _));
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void ExplicitEditorBinding_KeepsPrecedenceOverNativeControlNavigation()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var editor = new EditControl { Text = "alpha beta" };
        editor.SetUserKeyBindings([new(Key.F, ModifierKeys.Control,
            EditorCommands.SelectAll)]);
        var window = new DisplayedTestWindow { Content = editor };
        try
        {
            editor.CaretOffset = 3;
            Assert.True(editor.Focus());
            Dispatch(window, 0x46, 0x02);
            Assert.Equal("alpha beta", editor.SelectedText);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void PrimaryGesture_DoesNotSwallowPhysicalControlCommandCombination()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var editor = new TextBox(); var window = new DisplayedTestWindow { Content = editor };
        int matches = 0;
        var gesture = new KeyGesture(Key.J, ModifierKeys.Control);
        window.PreviewKeyDown += (_, e) => { if (gesture.Matches(window, e)) matches++; };
        try
        {
            Assert.True(editor.Focus());
            Dispatch(window, 0x4a, 0x08);
            Assert.Equal(1, matches);
            Dispatch(window, 0x4a, 0x0a);
            Assert.Equal(1, matches);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    private static UIElement CreateEditor(int kind, string text) => kind switch
    {
        0 => new TextBox { Text = text, AcceptsReturn = true },
        1 => new EditControl { Text = text },
        _ => new RichTextBox(FlowDocument.FromText(text))
    };

    private static void SetCaret(UIElement editor, int offset)
    {
        switch (editor)
        {
            case RichTextBox rich:
                rich.CaretPosition = rich.Document.GetPositionAtOffset(offset, LogicalDirection.Forward);
                break;
            case TextBox box: box.CaretIndex = offset; break;
            case EditControl code: code.CaretOffset = offset; break;
        }
    }

    private static ImeSurroundingTextSnapshot Snapshot(UIElement editor)
    {
        Assert.True(((IImeSupport)editor).TryGetImeSurroundingText(out var snapshot));
        return snapshot;
    }

    private static (string Text, int Caret) ReadState(UIElement editor) => editor switch
    {
        RichTextBox rich => (rich.Document.GetText(), rich.CaretPosition!.DocumentOffset),
        TextBox box => (box.Text, box.CaretIndex),
        EditControl code => (code.Text, code.CaretOffset),
        _ => throw new ArgumentOutOfRangeException(nameof(editor))
    };

    private static void Dispatch(Window window, int key, int modifiers, bool up = false) =>
        PlatformEventMethod.Invoke(window, [new PlatformEvent
        {
            Type = up ? PlatformEventType.KeyUp : PlatformEventType.KeyDown,
            KeyCode = key, Modifiers = modifiers
        }]);
}
