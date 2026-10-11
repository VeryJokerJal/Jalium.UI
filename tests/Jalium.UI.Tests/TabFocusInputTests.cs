using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class TabFocusInputTests : IDisposable
{
    public TabFocusInputTests()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
    }

    public void Dispose() => Keyboard.ClearFocus();

    public static IEnumerable<object[]> NavigationCases()
    {
        foreach (bool rich in new[] { false, true })
        foreach (bool reverse in new[] { false, true })
        foreach (bool destinationAcceptsTab in new[] { false, true })
            yield return [rich, reverse, destinationAcceptsTab];
    }

    [Theory]
    [MemberData(nameof(NavigationCases))]
    public void TabThenChar_MovesFocusWithoutEditingEitherControl(
        bool rich, bool reverse, bool destinationAcceptsTab)
    {
        var source = Editor(rich, "source");
        var destination = Editor(rich, "destination");
        destination.AcceptsTab = destinationAcceptsTab;
        using var fixture = reverse ? new InputFixture(destination, source) : new InputFixture(source, destination);
        Select(destination, 1, 3);
        string sourceText = Text(source), destinationText = Text(destination);
        bool sourceUndo = source.CanUndo, destinationUndo = destination.CanUndo;
        int changes = 0, textInputs = 0;
        source.TextChanged += (_, _) => changes++;
        destination.TextChanged += (_, _) => changes++;
        destination.PreviewTextInput += (_, _) => textInputs++;
        destination.TextInput += (_, _) => textInputs++;
        Assert.True(source.Focus());

        fixture.Tab(reverse);

        Assert.Same(destination, Keyboard.FocusedElement);
        Assert.False(source.IsKeyboardFocused);
        Assert.True(destination.IsKeyboardFocused);
        Assert.True(FocusVisualManager.ShowFocusCues);
        Assert.Equal(sourceText, Text(source));
        Assert.Equal(destinationText, Text(destination));
        Assert.Equal(sourceUndo, source.CanUndo);
        Assert.Equal(destinationUndo, destination.CanUndo);
        Assert.Equal(0, changes);
        Assert.Equal(0, textInputs);
        Assert.True(((IImeSupport)destination).TryGetImeSurroundingText(out var selection));
        Assert.Equal(1, Math.Min(selection.AnchorIndex, selection.CursorIndex));
        Assert.Equal(4, Math.Max(selection.AnchorIndex, selection.CursorIndex));

        // Ordinary typing must still reach the new target and replace its selection.
        fixture.Dispatcher.HandleCharInput("中🙂", 4);
        Assert.Equal(destinationText.Remove(1, 3).Insert(1, "中🙂"), Text(destination));
        Assert.Equal(sourceText, Text(source));
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AcceptsTab_KeyAndCharInsertExactlyOnceAndRespectReadOnly(bool rich, bool readOnly)
    {
        var editor = Editor(rich, "alpha");
        editor.AcceptsTab = true;
        editor.IsReadOnly = readOnly;
        var next = new Button();
        using var fixture = new InputFixture(editor, next);
        Select(editor, 0, 0);
        string before = Text(editor);
        int changes = 0;
        editor.TextChanged += (_, _) => changes++;
        Assert.True(editor.Focus());

        fixture.Tab();

        Assert.Same(editor, Keyboard.FocusedElement);
        Assert.Equal(readOnly ? before : "\t" + before, Text(editor));
        Assert.Equal(readOnly ? 0 : 1, changes);
        if (!readOnly)
        {
            editor.Undo();
            Assert.Equal(before, Text(editor));
            editor.Redo();
            Assert.Equal("\t" + before, Text(editor));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RoutedTabTextInput_DoesNotBypassKeyHandling(bool rich, bool acceptsTab)
    {
        var editor = Editor(rich, "alpha");
        editor.AcceptsTab = acceptsTab;
        using var fixture = new InputFixture(editor);
        Select(editor, 1, 3);
        string before = Text(editor);
        int changes = 0;
        editor.TextChanged += (_, _) => changes++;

        editor.RaiseEvent(new TextCompositionEventArgs(UIElement.TextInputEvent, "\t", 1));

        Assert.Equal(before, Text(editor));
        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommittedTextContainingTabs_RemainsEditable(bool rich)
    {
        var editor = Editor(rich, "alpha");
        using var fixture = new InputFixture(editor);
        Select(editor, 0, 0);
        string before = Text(editor);
        Assert.True(editor.Focus());

        fixture.Dispatcher.HandleCharInput("left\tright中文🙂", 1);

        Assert.Equal("left\tright中文🙂" + before, Text(editor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewHandledTab_DoesNotInsertOrMoveFocus(bool rich)
    {
        var editor = Editor(rich, "alpha");
        editor.AcceptsTab = true;
        using var fixture = new InputFixture(editor, new Button());
        editor.PreviewKeyDown += (_, e) => e.Handled = e.Key == Key.Tab;
        string before = Text(editor);
        Assert.True(editor.Focus());

        fixture.Tab();

        Assert.Same(editor, Keyboard.FocusedElement);
        Assert.Equal(before, Text(editor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TabNavigation_SkipsDisabledAndHiddenTargetsAndSupportsRepeatedReverse(bool rich)
    {
        var first = Editor(rich, "first");
        var disabled = new TextBox { Text = "disabled", IsEnabled = false };
        var hidden = new TextBox { Text = "hidden", Visibility = Visibility.Collapsed };
        var last = Editor(rich, "last");
        using var fixture = new InputFixture(first, disabled, hidden, last);
        string firstText = Text(first), lastText = Text(last);
        Assert.True(first.Focus());

        for (int i = 0; i < 3; i++)
        {
            fixture.Tab();
            Assert.Same(last, Keyboard.FocusedElement);
            fixture.Tab(reverse: true);
            Assert.Same(first, Keyboard.FocusedElement);
        }

        Assert.Equal(firstText, Text(first));
        Assert.Equal(lastText, Text(last));
        Assert.Equal("disabled", disabled.Text);
        Assert.Equal("hidden", hidden.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TabAtContainedBoundary_DoesNotEditTheRemainingTarget(bool rich)
    {
        var editor = Editor(rich, "alpha");
        using var fixture = new InputFixture(editor);
        KeyboardNavigation.SetTabNavigation(fixture.Panel, KeyboardNavigationMode.Contained);
        string before = Text(editor);
        Assert.True(editor.Focus());

        fixture.Tab();
        fixture.Tab(reverse: true);

        Assert.Same(editor, Keyboard.FocusedElement);
        Assert.Equal(before, Text(editor));
    }

    private static TextBoxBase Editor(bool rich, string text) => rich
        ? new RichTextBox(FlowDocument.FromText(text))
        : new TextBox { Text = text, AcceptsReturn = true };

    private static string Text(TextBoxBase editor) => editor is RichTextBox rich
        ? rich.Document.GetText() : ((TextBox)editor).Text;

    private static void Select(TextBoxBase editor, int start, int length)
    {
        if (editor is RichTextBox rich)
            rich.Selection.Select(rich.Document.ContentStart.GetPositionAtOffset(start)!,
                rich.Document.ContentStart.GetPositionAtOffset(start + length)!);
        else
            ((TextBox)editor).Select(start, length);
    }

    private sealed class InputFixture : IDisposable
    {
        private readonly Window _window;
        internal StackPanel Panel { get; } = new();
        internal WindowInputDispatcher Dispatcher { get; }

        internal InputFixture(params UIElement[] children)
        {
            foreach (var child in children) Panel.Children.Add(child);
            _window = new DisplayedTestWindow { Content = Panel, TitleBarStyle = WindowTitleBarStyle.Native };
            _window.Measure(new Size(480, 400));
            _window.Arrange(new Rect(0, 0, 480, 400));
            Dispatcher = new WindowInputDispatcher(_window);
        }

        internal void Tab(bool reverse = false)
        {
            var modifiers = reverse ? ModifierKeys.Shift : ModifierKeys.None;
            Assert.True(Dispatcher.HandleKeyDown(Key.Tab, modifiers, false, 1));
            // Win32 translates a Tab press into WM_KEYDOWN followed by WM_CHAR.
            // The character arrives after focus has potentially changed.
            Dispatcher.HandleCharInput("\t", 2);
            Dispatcher.HandleKeyUp(Key.Tab, modifiers, 3);
        }

        public void Dispose()
        {
            Keyboard.ClearFocus();
            _window.Close();
        }
    }
}
