using System.Reflection;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSKeyBindingRouteTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void TheNearestBindingRunsOnceFromTheNativeWindowEntry(int depth)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var child = new Counter(); var panel = new Counter(); var window = new Counter();
        if (depth == 0) Bind(host.Target, child);
        if (depth <= 1) Bind(host.Panel, panel);
        Bind(host.Window, window);
        host.Send();
        Assert.Equal((depth == 0 ? 1 : 0, depth == 1 ? 1 : 0, depth == 2 ? 1 : 0),
            (child.Executions, panel.Executions, window.Executions));
        Assert.Equal(1, child.Queries + panel.Queries + window.Queries);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AWindowBindingRunsWithoutAKeyboardFocusedChild(bool changeFocusDuringQuery)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        Keyboard.ClearFocus();
        var command = new Counter { Query = () =>
        { if (changeFocusDuringQuery) Assert.True(host.Other.Focus()); return true; } };
        Bind(host.Window, command); host.Send();
        Assert.Equal(1, command.Queries); Assert.Equal(changeFocusDuringQuery ? 0 : 1, command.Executions);
    }

    [Fact]
    public void AForeignWindowFocusDoesNotFallBackToTheEventWindowBinding()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host(); using var other = new Host();
        var command = new Counter(); Bind(host.Window, command);
        Assert.True(other.Target.Focus());
        host.Send(); Assert.Equal(0, command.Queries);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AParentRoutedBindingUsesTheFocusedOrExplicitCommandTarget(bool explicitTarget)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var command = new RoutedCommand("ParentBinding", typeof(MacOSKeyBindingRouteTests));
        UIElement expected = explicitTarget ? host.Other : host.Target;
        int executions = 0;
        expected.CommandBindings.Add(new CommandBinding(command,
            (_, _) => executions++, (_, e) => e.CanExecute = true));
        host.Window.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control))
            { CommandTarget = explicitTarget ? host.Other : null });
        host.Send(); Assert.Equal(1, executions);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AConsumedKeyDoesNotQueryAnAncestorBinding(bool preview)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var command = new Counter(); Bind(host.Window, command);
        if (preview) host.Target.PreviewKeyDown += (_, e) => e.Handled = true;
        else host.Target.KeyDown += (_, e) => e.Handled = true;
        host.Send(); Assert.Equal(0, command.Queries);
    }

    [Fact]
    public void ATextControlRetainsItsOwnEditingShortcut()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var editor = new TextBox { Text = "alpha beta" }; host.Panel.Children.Add(editor);
        Assert.True(editor.Focus()); var command = new Counter(); Bind(host.Window, command, Key.A);
        host.Send(Key.A); Assert.Equal("alpha beta", editor.SelectedText); Assert.Equal(0, command.Queries);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AModalBindingScopeDoesNotRunWindowCommandsBehindIt(bool withDialogBinding)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var dialog = new ContentDialog(); host.Root.Children.Add(dialog);
        typeof(Window).GetProperty("ActiveContentDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host.Window, dialog);
        try
        {
            var background = new Counter(); var foreground = new Counter();
            Bind(host.Window, background); if (withDialogBinding) Bind(dialog, foreground);
            host.Send(); Assert.Equal(0, background.Queries);
            Assert.Equal(withDialogBinding ? 1 : 0, foreground.Executions);
        }
        finally { typeof(Window).GetProperty("ActiveContentDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host.Window, null); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AParentGestureCannotMoveTheTargetThenRunAnOldBinding(bool matches)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var replacementParent = new StackPanel(); host.Root.Children.Add(replacementParent);
        var command = new Counter(); int matchesCalled = 0;
        host.Panel.InputBindings.Add(new InputBinding(command, new MutatingGesture(() =>
        {
            matchesCalled++;
            host.Panel.Children.Remove(host.Target); replacementParent.Children.Add(host.Target);
        }, matches)));
        var fallback = new Counter(); Bind(host.Window, fallback);
        host.Send(); Assert.Equal(1, matchesCalled); Assert.Equal(0, command.Queries); Assert.Equal(0, fallback.Queries);
    }

    [Theory]
    [InlineData("close")] [InlineData("focus")] [InlineData("move")]
    public void AnAncestorCanExecuteCannotRunAfterTheInputRouteChanges(string change)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host(); var replacement = new StackPanel(); host.Root.Children.Add(replacement);
        var command = new Counter { Query = () =>
        {
            if (change == "close") host.Window.Close();
            else if (change == "focus") Assert.True(host.Other.Focus());
            else { host.Panel.Children.Remove(host.Target); replacement.Children.Add(host.Target); }
            return true;
        }};
        Bind(host.Panel, command); host.Send(); Assert.Equal(1, command.Queries); Assert.Equal(0, command.Executions);
    }

    [Fact]
    public void DefaultRoutedGestureIsQueriedOnceAcrossUnboundAncestors()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host(); int queries = 0;
        host.Window.CommandBindings.Add(new CommandBinding(ApplicationCommands.Find,
            (_, _) => throw new Xunit.Sdk.XunitException("Disabled Find executed"),
            (_, e) => { queries++; e.CanExecute = false; }));
        host.Send(Key.F); Assert.Equal(1, queries);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AnInheritedRoutedBindingStopsWhenItsInternalQueryOrPreviewMovesTheTarget(bool duringPreview)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host(); var replacement = new StackPanel(); host.Root.Children.Add(replacement);
        var command = new RoutedCommand("MovedRoute", typeof(MacOSKeyBindingRouteTests));
        int queries = 0, previews = 0, executions = 0;
        void Move() { host.Panel.Children.Remove(host.Target); replacement.Children.Add(host.Target); }
        var binding = new CommandBinding(command, (_, _) => executions++, (_, e) =>
        {
            queries++;
            if (!duringPreview && queries == 2) Move();
            e.CanExecute = true;
        });
        binding.PreviewExecuted += (_, _) => { previews++; if (duringPreview) Move(); };
        host.Target.CommandBindings.Add(binding); Bind(host.Window, command);
        host.Send(); Assert.Equal(2, queries); Assert.Equal(duringPreview ? 1 : 0, previews);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void ADisabledNearestBindingDoesNotRunTheAncestorBinding()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host(); var child = new Counter { Query = () => false }; var parent = new Counter();
        Bind(host.Target, child); Bind(host.Window, parent); host.Send();
        Assert.Equal(1, child.Queries); Assert.Equal(0, child.Executions); Assert.Equal(0, parent.Queries);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AnAncestorGestureRetainsTheOriginalFocusedInputSource(bool onWindow)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host(); var command = new Counter(); int matches = 0;
        UIElement owner = onWindow ? host.Window : host.Panel;
        owner.InputBindings.Add(new InputBinding(command, new InspectingGesture((target, args) =>
        {
            matches++; Assert.Same(owner, target);
            Assert.Same(host.Target, args.Source); Assert.Same(host.Target, args.OriginalSource);
            var key = Assert.IsType<KeyEventArgs>(args);
            Assert.Equal(ModifierKeys.Control, key.KeyboardModifiers);
            Assert.Equal(ModifierKeys.Windows, key.PhysicalModifiers);
        })));
        host.Send(); Assert.Equal(1, matches); Assert.Equal(1, command.Executions);
    }

    private sealed class InspectingGesture(Action<object, InputEventArgs> inspect) : InputGesture
    {
        public override bool Matches(object target, InputEventArgs args)
        { if (args is not KeyEventArgs { Key: Key.K }) return false; inspect(target, args); return true; }
    }

    private static void Bind(UIElement target, ICommand command, Key key = Key.K) =>
        target.InputBindings.Add(new KeyBinding(command, new KeyGesture(key, ModifierKeys.Control)));
    private sealed class MutatingGesture(Action change, bool result) : InputGesture
    {
        public override bool Matches(object element, InputEventArgs args)
        { if (args is not KeyEventArgs { Key: Key.K }) return false; change(); return result; }
    }
    private sealed class Counter : ICommand
    {
        internal int Queries, Executions; internal Func<bool>? Query;
        public bool CanExecute(object? parameter) { Queries++; return Query?.Invoke() ?? true; }
        public void Execute(object? parameter) => Executions++;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
    private sealed class Host : IDisposable
    {
        internal readonly DisplayedTestWindow Window;
        internal readonly StackPanel Root = new();
        internal readonly StackPanel Panel = new();
        internal readonly Control Target = new() { Focusable = true };
        internal readonly Control Other = new() { Focusable = true };
        internal Host()
        {
            Keyboard.Initialize(); Keyboard.ClearFocus(); Root.Children.Add(Panel);
            Panel.Children.Add(Target); Panel.Children.Add(Other);
            Window = new DisplayedTestWindow { Width = 300, Height = 200, TitleBarStyle = WindowTitleBarStyle.Native, Content = Root };
            Window.Measure(new Size(300, 200)); Window.Arrange(new Rect(0, 0, 300, 200)); Assert.True(Target.Focus());
        }
        internal void Send(Key key = Key.K) => typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Window, [new PlatformEvent { Type = PlatformEventType.KeyDown, KeyCode = KeyInterop.VirtualKeyFromKey(key), Modifiers = 8 }]);
        public void Dispose() { Window.IsEnabled = true; Keyboard.ClearFocus(); Window.Close(); }
    }
}
