using System.Windows.Input;
using System.Reflection;
using Jalium.UI.Data;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsReplTemplateTests
{
    public DevToolsReplTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Fact]
    public void DefaultTemplateUsesNativeEditorAndReadOnlyOutputWithAutomationNames()
    {
        Assert.True(Jalium.UI.Markup.XamlPrebuiltDictionaryRegistry.TryGet("Jalium.UI.Managed.Themes.Controls.DevToolsRepl.jalxaml", out _));
        var view = new DevToolsReplView(); Layout(view);
        var editor = Part<TextBox>(view, "PART_InputTextBox"); var output = Part<Terminal>(view, "PART_Terminal");
        Assert.False(editor.IsReadOnly); Assert.True(output.IsReadOnly); Assert.False(output.AutoStartShell);
        Assert.Equal("REPL command", editor.GetAutomationPeer()!.GetName()); Assert.Equal("REPL output", output.GetAutomationPeer()!.GetName());
        Assert.Equal(Visibility.Visible, Part<StackPanel>(view, "PART_Empty").Visibility);
        Assert.False(Part<DevToolsActionButton>(view, "PART_Submit").IsEnabled);
        AutomationProperties.SetName(view, "Project console"); Assert.Equal("Project console", view.GetAutomationPeer()!.GetName());
    }

    [Fact]
    public void InputBindingAndAppearancePropertiesRemainLive()
    {
        var source = new DevToolsReplView { InputText = "draft" }; var view = new DevToolsReplView { Prompt = "project>", ContextText = "Selected control" };
        view.SetBinding(DevToolsReplView.InputTextProperty, new Binding(nameof(DevToolsReplView.InputText)) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        Layout(view); var editor = Part<TextBox>(view, "PART_InputTextBox"); Assert.Equal("draft", editor.Text);
        editor.Text = "中文😀"; Assert.Equal("中文😀", view.InputText); Assert.Equal("中文😀", source.InputText);
        source.InputText = "next"; Assert.Equal("next", editor.Text);
        Assert.Equal("project>", Part<TextBlock>(view, "PART_Prompt").Text); Assert.Equal("Selected control", Part<TextBlock>(view, "PART_Context").Text);
        Assert.Contains("Menlo", view.FontFamily.Source);
        var family = new FontFamily("monospace"); view.FontFamily = family;
        Assert.Same(family, editor.FontFamily); Assert.Same(family, Part<Terminal>(view, "PART_Terminal").FontFamily);
        view.IsReadOnly = true; Assert.True(editor.IsReadOnly); Assert.False(view.CanSubmit);
        Assert.Equal(Visibility.Visible, Part<DevToolsTag>(view, "PART_ReadOnlyStatus").Visibility);
        view.IsReadOnly = false; Assert.False(editor.IsReadOnly); Assert.True(view.CanSubmit);
    }

    [Fact]
    public void SubmissionRoutesOnceAndRejectsReentryWhileExecuting()
    {
        var calls = new List<string>(); var view = new DevToolsReplView(); Layout(view);
        view.CommandSubmitted += (_, e) => { var command = Assert.IsType<DevToolsReplCommandEventArgs>(e); calls.Add("event:" + command.CommandText); Assert.True(view.IsExecuting); Assert.False(view.Submit()); };
        view.ExecuteCommand = new RelayCommand(code => { calls.Add("command:" + code); Assert.True(Part<TextBox>(view, "PART_InputTextBox").IsReadOnly); view.WriteResult("42"); });
        view.InputText = "42"; Assert.True(view.Submit()); Assert.Equal(new[] { "event:42", "command:42" }, calls);
        Assert.False(view.IsExecuting); Assert.False(Part<TextBox>(view, "PART_InputTextBox").IsReadOnly); Assert.Equal("", view.InputText);
        Assert.Equal(new[] { "42" }, view.History); Assert.Contains("=> 42", Output(view));
        Assert.Equal(Visibility.Collapsed, Part<StackPanel>(view, "PART_Empty").Visibility);
    }

    [Fact]
    public void HandledSubmissionCanReplaceExecutionAndErrorsBecomeOutputAndState()
    {
        int calls = 0; var view = new DevToolsReplView { ExecuteCommand = new RelayCommand(() => calls++) }; Layout(view);
        RoutedEventHandler handled = (_, e) => e.Handled = true; view.CommandSubmitted += handled;
        view.InputText = "handled"; view.Submit(); Assert.Equal(0, calls); view.CommandSubmitted -= handled;
        view.ExecuteCommand = new RelayCommand(() => throw new InvalidOperationException("Unavailable member")); view.InputText = "bad";
        Assert.True(view.Submit()); Assert.False(view.IsExecuting); Assert.Equal("Unavailable member", view.LastError);
        Assert.Contains("E: Unavailable member", Output(view)); Assert.Equal(Visibility.Visible, Part<DevToolsTag>(view, "PART_ErrorStatus").Visibility);
        view.IsReadOnly = true; view.ExecuteCommand = new RelayCommand(() => calls++);
        Assert.False(view.Submit()); Assert.Equal(0, calls);
        view.IsReadOnly = false; view.InputText = "next"; Assert.Equal("", view.LastError);
        view.IsEnabled = false; Assert.False(view.Submit()); Assert.False(view.SubmitCommand.CanExecute(null));
    }

    [Fact]
    public void CommandAvailabilityUpdatesAndOldObserversDetach()
    {
        var old = new ObservedCommand(); var current = new ObservedCommand(); var view = new DevToolsReplView { InputText = "test", ExecuteCommand = old }; Layout(view);
        Assert.Equal(1, old.Observers); Assert.True(view.CanSubmit);
        old.Available = false; old.Notify(); Assert.False(view.CanSubmit); Assert.False(Part<DevToolsActionButton>(view, "PART_Submit").IsEnabled);
        view.ExecuteCommand = current; Assert.Equal(0, old.Observers); Assert.Equal(1, current.Observers); Assert.True(view.CanSubmit);
        view.InputText = "  "; Assert.False(view.CanSubmit); view.ExecuteCommand = null; Assert.Equal(0, current.Observers);
    }

    [Fact]
    public void BoundedHistoryRestoresDraftAndEditingStartsANewRecall()
    {
        var view = new DevToolsReplView { HistoryCapacity = 2 };
        foreach (var text in new[] { "first", "second", "third" }) { view.InputText = text; view.Submit(); }
        Assert.Equal(new[] { "second", "third" }, view.History); Assert.Equal(2, view.HistoryCount);
        view.InputText = "unfinished"; Assert.True(view.RecallHistory(-1)); Assert.Equal("third", view.InputText);
        view.RecallHistory(-1); view.RecallHistory(-1); Assert.Equal("second", view.InputText);
        view.RecallHistory(1); view.RecallHistory(1); Assert.Equal("unfinished", view.InputText);
        view.RecallHistory(-1); view.InputText = "edited"; view.RecallHistory(-1); view.RecallHistory(1); Assert.Equal("edited", view.InputText);
        view.Clear(); Assert.Equal(2, view.HistoryCount); Assert.Equal("edited", view.InputText);
        view.HistoryCapacity = 0; Assert.Empty(view.History); view.Submit(); Assert.Empty(view.History);
    }

    [Fact]
    public void RetemplatingReplaysBoundedOutputAndRetiresInputHandlers()
    {
        var view = new DevToolsReplView { TranscriptCapacity = 2 }; Layout(view); var original = view.Template;
        var retired = Part<TextBox>(view, "PART_InputTextBox"); view.Write("first\r\n"); view.Write("second\r\n"); view.Write("third\r\n");
        var empty = new ControlTemplate(typeof(DevToolsReplView)); empty.SetVisualTree(() => new Border()); view.Template = empty; Layout(view);
        view.InputText = "draft"; Assert.False(KeyDown(retired, Key.Enter).Handled); Assert.Equal(0, view.HistoryCount);
        view.Write("fourth\r\n"); view.Template = original; Layout(view);
        Assert.DoesNotContain("first", Output(view)); Assert.DoesNotContain("second", Output(view)); Assert.Contains("third", Output(view)); Assert.Contains("fourth", Output(view));
        Assert.Equal("draft", Part<TextBox>(view, "PART_InputTextBox").Text); Assert.True(view.HasOutput);
        Part<DevToolsActionButton>(view, "PART_Clear").PerformClick(); Assert.False(view.HasOutput); Assert.DoesNotContain("third", Output(view));
    }

    [Fact]
    public void KeyboardShortcutsPreserveNativeCopyAndClearKeepsTheDraft()
    {
        var view = new DevToolsReplView(); Layout(view); var editor = Part<TextBox>(view, "PART_InputTextBox");
        editor.Text = "selected text"; editor.Select(0, 8); Assert.False(KeyDown(editor, Key.C, ModifierKeys.Control).Handled);
        editor.Select(0, 0); Assert.True(KeyDown(editor, Key.C, ModifierKeys.Control).Handled); Assert.Equal("", view.InputText);
        editor.Text = "draft"; Assert.True(KeyDown(editor, Key.L, ModifierKeys.Control).Handled); Assert.Equal("draft", view.InputText); Assert.False(view.HasOutput);
        Assert.True(KeyDown(editor, Key.Escape).Handled); Assert.Equal("", editor.Text);
        editor.Text = "run"; Assert.True(KeyDown(editor, Key.Enter).Handled); Assert.Equal(new[] { "run" }, view.History);
        editor.Text = "new draft"; KeyDown(editor, Key.Up); Assert.Equal("run", editor.Text); KeyDown(editor, Key.Down); Assert.Equal("new draft", editor.Text);
    }

    [Theory]
    [InlineData(320)] [InlineData(480)] [InlineData(1040)]
    public void EditorOutputAndToolbarFitTheViewport(double width)
    {
        var view = new DevToolsReplView(); Layout(view, width);
        foreach (string name in new[] { "PART_InputTextBox", "PART_Submit", "PART_Terminal", "PART_Clear" })
        {
            var part = Part<FrameworkElement>(view, name); var end = part.TranslatePoint(new Point(part.ActualWidth, part.ActualHeight), view);
            Assert.True(part.ActualWidth > 0); Assert.InRange(end.X, 0, width + .5); Assert.InRange(end.Y, 0, 760.5);
        }
    }

    [Fact]
    public void SingleLineInputKeepsUnicodeAndCapacityPropertiesValidateTheirBounds()
    {
        var view = new DevToolsReplView { InputText = "中文😀\r\n\tA\u0001" }; Assert.Equal("中文😀   A", view.InputText);
        Assert.Throws<ArgumentException>(() => view.HistoryCapacity = -1); Assert.Throws<ArgumentException>(() => view.TranscriptCapacity = -1); Assert.Throws<ArgumentException>(() => view.MaxScrollbackLines = -1);
        Assert.Throws<InvalidOperationException>(() => view.SetValue(DevToolsReplView.IsExecutingProperty, true));
    }

    [Fact]
    public void ImeCompositionEnterStaysWithTheNativeEditorUntilCommitted()
    {
        var view = new DevToolsReplView(); Layout(view); var editor = Part<TextBox>(view, "PART_InputTextBox");
        typeof(TextBox).GetMethod("OnImeCompositionStart", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
        typeof(TextBox).GetMethod("OnImeCompositionUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, new object[] { "中文", 2 });
        Assert.False(KeyDown(editor, Key.Enter).Handled); Assert.Empty(view.History);
        editor.RaiseEvent(new TextCompositionEventArgs(UIElement.TextInputEvent, "中文", 1));
        typeof(TextBox).GetMethod("OnImeCompositionEnd", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, new object[] { "中文" });
        Assert.Equal("中文", view.InputText); Assert.True(KeyDown(editor, Key.Enter).Handled); Assert.Equal(new[] { "中文" }, view.History);
    }

    [Fact]
    public void ReadOnlyChangedDuringExecutionSurvivesTheReadyTransition()
    {
        var view = new DevToolsReplView(); Layout(view);
        view.ExecuteCommand = new RelayCommand(() => view.IsReadOnly = true); view.InputText = "switch"; view.Submit();
        Assert.True(view.IsReadOnly); Assert.True(Part<TextBox>(view, "PART_InputTextBox").IsReadOnly); Assert.False(view.CanSubmit);
        view.IsReadOnly = false; Assert.False(Part<TextBox>(view, "PART_InputTextBox").IsReadOnly);
    }

    [Fact]
    public void ResizingReflowsCompleteWritesWithoutLosingTheEndOfALine()
    {
        var view = new DevToolsReplView(); Layout(view, 1040);
        string message = new string('a', 60) + "TAIL_MARKER";
        view.Write(message + "\r\n"); Assert.Contains("TAIL_MARKER", Output(view));
        Layout(view, 480); Pump(view); Layout(view, 480);
        Assert.Contains("TAIL_MARKER", Output(view).Replace("\r", "").Replace("\n", "").Replace(" ", ""));
        Layout(view, 1040); Pump(view); Layout(view, 1040); Assert.Contains(message, Output(view));
    }

    private static T Part<T>(Control view, string name) where T : class => Assert.IsAssignableFrom<T>(view.FindName(name));
    private static void Pump(UIElement view) => view.Dispatcher.GetType().GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view.Dispatcher, null);
    private static string Output(DevToolsReplView view) => Assert.IsAssignableFrom<ITextProvider>(Part<Terminal>(view, "PART_Terminal").GetAutomationPeer()!.GetPattern(PatternInterface.Text)).DocumentRange.GetText(-1);
    private static void Layout(UIElement element, double width = 1040) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(width, 760)); element.Arrange(new Rect(0, 0, width, 760)); element.UpdateLayout(); } }
    private static KeyEventArgs KeyDown(TextBox editor, Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        var args = (KeyEventArgs)Activator.CreateInstance(typeof(KeyEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { UIElement.PreviewKeyDownEvent, key, modifiers, true, false, 1 }, null)!;
        editor.RaiseEvent(args); return args;
    }
    private sealed class ObservedCommand : ICommand
    {
        private EventHandler? _changed; public bool Available = true; public int Observers => _changed?.GetInvocationList().Length ?? 0;
        public event EventHandler? CanExecuteChanged { add => _changed += value; remove => _changed -= value; }
        public bool CanExecute(object? parameter) => Available; public void Execute(object? parameter) { } public void Notify() => _changed?.Invoke(this, EventArgs.Empty);
    }
}
