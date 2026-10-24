using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSKeyCommandLifetimeTests
{
    [Theory]
    [InlineData("close")]
    [InlineData("hide-window")]
    [InlineData("disable-window")]
    [InlineData("hide-target")]
    [InlineData("disable-target")]
    [InlineData("change-focus")]
    [InlineData("clear-focus")]
    [InlineData("detach-target")]
    [InlineData("reparent-target")]
    public void CanExecuteCannotRunAnOldKeyboardTargetAfterMutation(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        using var destination = new Host();
        Assert.True(host.Target.Focus());
        var command = new Counter { Query = () => { Mutate(host, destination, mutation); return true; } };
        host.Target.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control)));
        host.Send();
        Assert.Equal(1, command.Queries);
        Assert.Equal(0, command.Executions);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("change-focus")]
    [InlineData("reparent-target")]
    public void GestureMatchingCannotRunAnOldKeyboardTargetAfterMutation(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        using var destination = new Host();
        Assert.True(host.Target.Focus());
        var command = new Counter();
        host.Target.InputBindings.Add(new InputBinding(command,
            new MatchingGesture(() => Mutate(host, destination, mutation))));
        host.Send();
        Assert.Equal(0, command.Queries);
        Assert.Equal(0, command.Executions);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("change-focus")]
    [InlineData("reparent-target")]
    public void RoutedCanExecuteCannotExecuteAfterTheKeyboardTargetChanges(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        using var destination = new Host();
        Assert.True(host.Target.Focus());
        var command = new RoutedCommand();
        int executions = 0;
        host.Target.CommandBindings.Add(new CommandBinding(command,
            (_, _) => executions++, (_, e) => { Mutate(host, destination, mutation); e.CanExecute = true; }));
        host.Target.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control)));
        host.Send();
        Assert.Equal(0, executions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnchangedKeyboardCommandRunsOnceWithItsOriginalParameter(bool routed)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var parameter = new object();
        int executions = 0;
        ICommand command;
        if (routed)
        {
            var route = new RoutedCommand(); command = route;
            host.Target.CommandBindings.Add(new CommandBinding(route,
                (_, e) => { Assert.Same(parameter, e.Parameter); executions++; }, (_, e) => e.CanExecute = true));
        }
        else command = new Counter { Execute = value => { Assert.Same(parameter, value); executions++; } };
        host.Target.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control))
            { CommandParameter = parameter });
        host.Send();
        Assert.Equal(1, executions);
    }

    [Fact]
    public void ACancelledCloseDoesNotRetireTheKeyboardCommand()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        EventHandler<System.ComponentModel.CancelEventArgs> cancel = (_, e) => e.Cancel = true;
        host.Window.Closing += cancel;
        var command = new Counter { Query = () => { host.Window.Close(); return true; } };
        host.Target.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control)));
        try
        {
            host.Send();
            Assert.Equal(1, command.Executions);
        }
        finally { host.Window.Closing -= cancel; }
    }

    [Fact]
    public void HandledPreviewDoesNotQueryTheCommand()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var command = new Counter();
        host.Target.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control)));
        host.Target.PreviewKeyDown += (_, e) => e.Handled = true;
        host.Send();
        Assert.Equal(0, command.Queries);
        Assert.Equal(0, command.Executions);
    }

    [Fact]
    public void ACommandThatCannotExecuteRemainsUnexecuted()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var command = new Counter { Query = () => false };
        host.Target.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control)));
        host.Send();
        Assert.Equal(1, command.Queries);
        Assert.Equal(0, command.Executions);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("close")]
    [InlineData("change-focus")]
    [InlineData("reparent-target")]
    public void BuiltInGestureRechecksTheKeyboardTargetAfterCanExecute(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        using var destination = new Host();
        Assert.True(host.Target.Focus());
        int executions = 0;
        host.Target.CommandBindings.Add(new CommandBinding(ApplicationCommands.Find,
            (_, _) => executions++, (_, e) =>
            {
                if (mutation != "none") Mutate(host, destination, mutation);
                e.CanExecute = true;
            }));
        host.Send(Key.F);
        Assert.Equal(mutation == "none" ? 1 : 0, executions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardedPopupKeyboardInputRetainsTheOwnerCommandLifetime(bool closeDuringQuery)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var command = new Counter
        {
            Query = () => { if (closeDuringQuery) host.Window.Close(); return true; }
        };
        host.Target.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control)));
        using var popup = new PopupWindow(host.Window,
            new PopupRoot(new Popup(), new Control(), isLightDismiss: false));
        var bridge = typeof(PopupWindow).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        // Match the native callback's live-bridge contract without owning a native
        // handle. Real AppKit activation and keyboard input remain separate tests.
        bridge.SetValue(popup, RuntimeHelpers.GetUninitializedObject(typeof(NativePlatformWindow)));
        try
        {
            typeof(PopupWindow).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(popup, [new PlatformEvent { Type = PlatformEventType.KeyDown,
                    KeyCode = KeyInterop.VirtualKeyFromKey(Key.K), Modifiers = 8 }]);
            Assert.Equal(1, command.Queries);
            Assert.Equal(closeDuringQuery ? 0 : 1, command.Executions);
        }
        finally { bridge.SetValue(popup, null); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModalFallbackRetainsItsActualKeyboardFocusSnapshot(bool changeDuringQuery)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        using var outside = new Host();
        var dialog = new ContentDialog { Visibility = Visibility.Visible };
        host.Window.Content = dialog;
        host.Window.ActiveContentDialog = dialog;
        Assert.True(outside.Target.Focus());
        var command = new Counter
        {
            Query = () => { if (changeDuringQuery) Assert.True(outside.Other.Focus()); return true; }
        };
        dialog.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control)));
        try
        {
            host.Send();
            Assert.Equal(1, command.Queries);
            Assert.Equal(changeDuringQuery ? 0 : 1, command.Executions);
        }
        finally { host.Window.ActiveContentDialog = null; }
    }

    [Theory]
    [InlineData("close", false)]
    [InlineData("change-focus", false)]
    [InlineData("reparent-target", false)]
    [InlineData("close", true)]
    [InlineData("change-focus", true)]
    [InlineData("reparent-target", true)]
    public void RoutedExecutionRechecksItsOwnQueryAndPreview(string mutation, bool duringPreview)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        using var destination = new Host();
        Assert.True(host.Target.Focus());
        var command = new RoutedCommand();
        int queries = 0, executions = 0;
        host.Target.CommandBindings.Add(new CommandBinding(command,
            (_, _) => executions++, (_, e) =>
            {
                queries++;
                if (!duringPreview && queries == 2) Mutate(host, destination, mutation);
                e.CanExecute = true;
            }));
        if (duringPreview)
            host.Target.AddHandler(RoutedCommand.PreviewExecutedEvent,
                new ExecutedRoutedEventHandler((_, _) => Mutate(host, destination, mutation)));
        host.Target.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control)));
        host.Send();
        Assert.Equal(2, queries);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void AHandledRoutedPreviewStillConsumesTheCommand()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var command = new RoutedCommand();
        int executions = 0;
        host.Target.CommandBindings.Add(new CommandBinding(command,
            (_, _) => executions++, (_, e) => e.CanExecute = true));
        host.Target.AddHandler(RoutedCommand.PreviewExecutedEvent,
            new ExecutedRoutedEventHandler((_, e) => e.Handled = true));
        host.Target.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control)));
        host.Send();
        Assert.Equal(0, executions);
    }

    [Fact]
    public void ProgrammaticRoutedExecutionRetainsItsExplicitTarget()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var command = new RoutedCommand();
        int queries = 0, executions = 0;
        host.Target.CommandBindings.Add(new CommandBinding(command,
            (_, _) => executions++, (_, e) =>
            { queries++; Assert.True(host.Other.Focus()); e.CanExecute = true; }));
        command.Execute(null, host.Target);
        Assert.Equal(1, queries);
        Assert.Equal(1, executions);
    }

    [Theory]
    [InlineData("close", false)]
    [InlineData("reparent-target", false)]
    [InlineData("close", true)]
    [InlineData("reparent-target", true)]
    public void MouseRoutedExecutionRechecksItsOwnQueryAndPreview(string mutation, bool duringPreview)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        using var destination = new Host();
        var command = new RoutedCommand();
        int queries = 0, executions = 0;
        host.Target.CommandBindings.Add(new CommandBinding(command,
            (_, _) => executions++, (_, e) =>
            {
                queries++;
                if (!duringPreview && queries == 2) Mutate(host, destination, mutation);
                e.CanExecute = true;
            }));
        if (duringPreview)
            host.Target.AddHandler(RoutedCommand.PreviewExecutedEvent,
                new ExecutedRoutedEventHandler((_, _) => Mutate(host, destination, mutation)));
        host.Target.InputBindings.Add(new MouseBinding(command,
            new MouseGesture(MouseAction.LeftClick, ModifierKeys.Control)));
        var packet = new MouseButtonEventArgs(UIElement.MouseDownEvent, default, MouseButton.Left,
            MouseButtonState.Pressed, 1, MouseButtonState.Pressed, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.Control, 0);
        Assert.True(CommandManager.ProcessInput(host.Target, packet));
        Assert.Equal(2, queries);
        Assert.Equal(0, executions);
    }

    private static void Mutate(Host host, Host destination, string mutation)
    {
        switch (mutation)
        {
            case "close": host.Window.Close(); break;
            case "hide-window": host.Window.Hide(); break;
            case "disable-window": host.Window.IsEnabled = false; break;
            case "hide-target": host.Target.Visibility = Visibility.Hidden; break;
            case "disable-target": host.Target.IsEnabled = false; break;
            case "change-focus": Assert.True(host.Other.Focus()); break;
            case "clear-focus": Keyboard.ClearFocus(); break;
            case "detach-target": host.Panel.Children.Remove(host.Target); break;
            case "reparent-target": host.Panel.Children.Remove(host.Target); destination.Panel.Children.Add(host.Target); break;
            default: throw new ArgumentException(mutation);
        }
    }

    private sealed class MatchingGesture(Action mutate) : InputGesture
    {
        public override bool Matches(object targetElement, InputEventArgs inputEventArgs)
        {
            if (inputEventArgs is not KeyEventArgs { Key: Key.K }) return false;
            mutate(); return true;
        }
    }

    private sealed class Counter : ICommand
    {
        internal int Queries, Executions;
        internal Func<bool>? Query;
        internal Action<object?>? Execute;
        public bool CanExecute(object? parameter) { Queries++; return Query?.Invoke() ?? true; }
        void ICommand.Execute(object? parameter) { Executions++; Execute?.Invoke(parameter); }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    private sealed class Host : IDisposable
    {
        internal readonly DisplayedTestWindow Window;
        internal readonly StackPanel Panel = new();
        internal readonly Control Target = new() { Focusable = true };
        internal readonly Control Other = new() { Focusable = true };
        internal Host()
        {
            Keyboard.Initialize(); Keyboard.ClearFocus();
            Window = new DisplayedTestWindow { Width = 300, Height = 200,
                TitleBarStyle = WindowTitleBarStyle.Native, Content = Panel };
            Panel.Children.Add(Target); Panel.Children.Add(Other);
            Window.Measure(new Size(300, 200)); Window.Arrange(new Rect(0, 0, 300, 200));
            Assert.True(Target.Focus());
        }
        internal void Send(Key key = Key.K) => typeof(Window)
            .GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window,
                [new PlatformEvent { Type = PlatformEventType.KeyDown, KeyCode = KeyInterop.VirtualKeyFromKey(key), Modifiers = 8 }]);
        public void Dispose()
        {
            Window.IsEnabled = true;
            Keyboard.ClearFocus(); Window.Close();
        }
    }
}
