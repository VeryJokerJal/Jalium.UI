using System.ComponentModel;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class RadioButtonKeyboardNavigationTests : IDisposable
{
    public RadioButtonKeyboardNavigationTests() { Keyboard.Initialize(); Keyboard.ClearFocus(); }
    public void Dispose() => Keyboard.ClearFocus();

    [Theory]
    [InlineData(Key.Right, 2)]
    [InlineData(Key.Down, 2)]
    [InlineData(Key.Left, 0)]
    [InlineData(Key.Up, 0)]
    public void ArrowsMoveFocusAndSelectionAndExecuteTheDestinationCommand(Key key, int expected)
    {
        var (root, buttons) = Group(); var clicks = 0; var executions = 0;
        buttons[expected].Click += (_, _) => clicks++;
        buttons[expected].Command = new ActionCommand(() => executions++);
        buttons[1].IsChecked = true; Layout(root); Assert.True(buttons[1].Focus());
        var args = Press(buttons[1], key);
        Assert.True(args.Handled); Assert.Same(buttons[expected], Keyboard.FocusedElement);
        Assert.True(buttons[expected].IsChecked); Assert.Single(buttons, x => x.IsChecked == true);
        Assert.Equal(1, clicks); Assert.Equal(1, executions);
    }

    [Theory]
    [InlineData(Key.Right, 2, 0)]
    [InlineData(Key.Left, 0, 2)]
    public void ArrowsWrapAtTheGroupBoundary(Key key, int initial, int expected)
    {
        var (root, buttons) = Group(); Layout(root); buttons[initial].IsChecked = true; buttons[initial].Focus();
        Assert.True(Press(buttons[initial], key).Handled);
        Assert.Same(buttons[expected], Keyboard.FocusedElement); Assert.True(buttons[expected].IsChecked);
    }

    [Fact]
    public void NavigationSkipsDisabledHiddenAndUnfocusableMembers()
    {
        var root = new StackPanel(); var buttons = Enumerable.Range(0, 5).Select(_ => Radio("skip")).ToArray();
        foreach (var button in buttons) root.Children.Add(button);
        buttons[1].IsEnabled = false; buttons[2].Visibility = Visibility.Hidden; buttons[3].Focusable = false;
        Layout(root); buttons[0].Focus(); Press(buttons[0], Key.Right);
        Assert.Same(buttons[4], Keyboard.FocusedElement); Assert.True(buttons[4].IsChecked);
    }

    [Fact]
    public void NavigationSkipsHiddenAncestorsAndUnavailableCommands()
    {
        var (root, buttons) = Group(); var hidden = new StackPanel { Visibility = Visibility.Collapsed };
        var nested = Radio("navigation"); hidden.Children.Add(nested); root.Children.Insert(1, hidden);
        buttons[1].Command = new DisabledCommand(); Layout(root); buttons[0].Focus();
        Press(buttons[0], Key.Right);
        Assert.Same(buttons[2], Keyboard.FocusedElement); Assert.False(nested.IsChecked == true);
    }

    [Fact]
    public void AttachingACheckedMemberUpdatesOnlyItsDestinationGroup()
    {
        var (root, buttons) = Group(); buttons[0].IsChecked = true;
        var incoming = Radio("navigation"); incoming.IsChecked = true;
        Assert.True(buttons[0].IsChecked);
        root.Children.Add(incoming); Layout(root);
        Assert.False(buttons[0].IsChecked); Assert.True(incoming.IsChecked);
    }

    [Fact]
    public void ToolbarRadioButtonsLeaveArrowsToToolbarNavigation()
    {
        var toolbar = new ToolbarHost(); var button = Radio("toolbar"); toolbar.Attach(button); Layout(toolbar);
        Assert.False(Press(button, Key.Right).Handled);
    }

    [Fact]
    public void NamedGroupsFollowVisualOrderAcrossParentsAndIgnoreOtherRoots()
    {
        var root = new StackPanel(); var a = new StackPanel(); var b = new StackPanel();
        var first = Radio("shared"); var second = Radio("shared"); var other = Radio("other");
        a.Children.Add(first); a.Children.Add(other); b.Children.Add(second); root.Children.Add(a); root.Children.Add(b);
        var separate = new StackPanel(); var isolated = Radio("shared"); separate.Children.Add(isolated);
        Layout(root); Layout(separate); isolated.IsChecked = true; first.IsChecked = true; first.Focus();
        Press(first, Key.Right);
        Assert.Same(second, Keyboard.FocusedElement); Assert.True(second.IsChecked); Assert.True(isolated.IsChecked);
        Assert.False(other.IsChecked == true);
    }

    [Fact]
    public void UnnamedGroupsStayWithImmediateSiblings()
    {
        var (root, buttons) = Group(""); var nested = new StackPanel(); var unrelated = Radio("");
        nested.Children.Add(unrelated); root.Children.Add(nested); Layout(root);
        buttons[2].Focus(); Press(buttons[2], Key.Right);
        Assert.Same(buttons[0], Keyboard.FocusedElement); Assert.False(unrelated.IsChecked == true);
    }

    [Fact]
    public void SelectionPreservesBoundCheckedProperties()
    {
        var (root, buttons) = Group(); var models = buttons.Select(_ => new CheckedModel()).ToArray();
        for (var i = 0; i < buttons.Length; i++)
            buttons[i].SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(CheckedModel.Checked)) { Source = models[i], Mode = BindingMode.OneWay });
        models[0].Checked = true; Layout(root); buttons[0].Focus(); Press(buttons[0], Key.Right);
        Assert.True(buttons[1].IsChecked); Assert.False(buttons[0].IsChecked);
        Assert.All(buttons, x => Assert.NotNull(BindingOperations.GetBindingExpression(x, ToggleButton.IsCheckedProperty)));
        models[2].Checked = true;
        Assert.True(buttons[2].IsChecked); Assert.Single(buttons, x => x.IsChecked == true);
    }

    [Fact]
    public void ModifiedArrowsAndDisabledInputAreNotConsumed()
    {
        var (root, buttons) = Group(); Layout(root); buttons[0].Focus();
        Assert.False(Press(buttons[0], Key.Right, ModifierKeys.Control).Handled);
        Assert.Same(buttons[0], Keyboard.FocusedElement);
        buttons[0].IsEnabled = false; Assert.False(Press(buttons[0], Key.Right).Handled);
    }

    [Fact]
    public void HorizontalArrowsFollowRightToLeftLayout()
    {
        var (root, buttons) = Group(); root.FlowDirection = FlowDirection.RightToLeft; Layout(root);
        buttons[1].Focus(); Press(buttons[1], Key.Left);
        Assert.Same(buttons[2], Keyboard.FocusedElement);
    }

    private static (StackPanel Root, RadioButton[] Buttons) Group(string? name = null)
    {
        var root = new StackPanel(); var buttons = Enumerable.Range(0, 3).Select(_ => Radio(name ?? Guid.NewGuid().ToString())).ToArray();
        if (name is null) foreach (var button in buttons) button.GroupName = "navigation";
        foreach (var button in buttons) root.Children.Add(button); return (root, buttons);
    }
    private static RadioButton Radio(string group)
    {
        var template = new ControlTemplate(typeof(RadioButton)); template.SetVisualTree(() => new Border());
        return new RadioButton { GroupName = group, Width = 80, Height = 24, Template = template };
    }
    private static void Layout(FrameworkElement root) { root.Measure(new Size(400, 400)); root.Arrange(new Rect(0, 0, 400, 400)); root.UpdateLayout(); }
    private static KeyEventArgs Press(RadioButton target, Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        var args = new KeyEventArgs(new TestKeyboard(modifiers), new Source { RootVisual = target }, 1, key) { RoutedEvent = UIElement.KeyDownEvent, Source = target };
        target.RaiseEvent(args); return args;
    }
    private sealed class TestKeyboard(ModifierKeys modifiers) : KeyboardDevice
    {
        public override IInputElement? Target => null;
        public override PresentationSource? ActiveSource => null;
        protected override KeyStates GetKeyStatesFromSystem(Key key) =>
            modifiers.HasFlag(ModifierKeys.Control) && key == Key.LeftCtrl ? KeyStates.Down : KeyStates.None;
    }
    private sealed class Source : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
    private sealed class ActionCommand(Action action) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
    private sealed class DisabledCommand : ICommand
    {
        public bool CanExecute(object? parameter) => false;
        public void Execute(object? parameter) => throw new InvalidOperationException("Disabled command executed");
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
    private sealed class ToolbarHost : ToolBar
    {
        public void Attach(Visual child) => AddVisualChild(child);
    }
    private sealed class CheckedModel : INotifyPropertyChanged
    {
        private bool _checked;
        public bool Checked { get => _checked; set { _checked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
