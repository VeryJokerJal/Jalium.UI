using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSModalCommandRouteTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BuiltInGesturesNeverQueryOrExecuteTheBackgroundWindow(bool focusInside)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(focusInside);
        int queries = 0, executed = 0;
        h.Window.CommandBindings.Add(new CommandBinding(ApplicationCommands.Find,
            (_, _) => executed++, (_, e) => { queries++; e.CanExecute = true; }));
        h.Send(Key.F);
        Assert.Equal((0, 0), (queries, executed));
    }

    [Theory]
    [InlineData("preview-query")] [InlineData("query")]
    [InlineData("preview-execute")] [InlineData("execute")]
    public void BackgroundCommandEventHandlersCannotObserveModalCommands(string phase)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        var command = h.BindDialog(); int leaked = 0, executed = 0;
        h.Dialog.CommandBindings.Add(new CommandBinding(command, (_, _) => executed++, (_, e) => e.CanExecute = true));
        if (phase == "preview-query" || phase == "query")
            h.Window.AddHandler(phase == "preview-query" ? CommandManager.PreviewCanExecuteEvent : CommandManager.CanExecuteEvent,
                new CanExecuteRoutedEventHandler((_, _) => leaked++), handledEventsToo: true);
        else h.Window.AddHandler(phase == "preview-execute" ? CommandManager.PreviewExecutedEvent : CommandManager.ExecutedEvent,
            new ExecutedRoutedEventHandler((_, _) => leaked++), handledEventsToo: true);
        h.Send(); Assert.Equal(1, executed); Assert.Equal(0, leaked);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DialogCommandBindingsStillRunForFocusedAndFallbackTargets(bool focusInside)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(focusInside); var command = h.BindDialog(); int executed = 0;
        h.Dialog.CommandBindings.Add(new CommandBinding(command, (_, _) => executed++, (_, e) => e.CanExecute = true));
        h.Send(); Assert.Equal(1, executed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AnExplicitBackgroundTargetIsRejectedOnlyForPhysicalModalInput(bool programmatic)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var command = h.BindDialog(h.Background); int executed = 0;
        h.Background.CommandBindings.Add(new CommandBinding(command, (_, _) => executed++, (_, e) => e.CanExecute = true));
        if (programmatic) command.Execute(null, h.Background); else h.Send();
        Assert.Equal(programmatic ? 1 : 0, executed);
    }

    [Fact]
    public void ACommandTargetInsideTheDialogCanBeExplicitlySelected()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var command = h.BindDialog(h.Other); int executed = 0;
        h.Other.CommandBindings.Add(new CommandBinding(command, (_, _) => executed++, (_, e) => e.CanExecute = true));
        h.Send(); Assert.Equal(1, executed);
    }

    [Theory]
    [InlineData("leave")] [InlineData("reparent")]
    [InlineData("hide")] [InlineData("disable")]
    public void AnExplicitTargetChangedDuringQueryDoesNotExecuteTheOldModalRoute(string change)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var command = h.BindDialog(h.Other); int executed = 0, queries = 0;
        h.Other.CommandBindings.Add(new CommandBinding(command, (_, _) => executed++, (_, e) =>
        {
            queries++; e.CanExecute = true;
            if (queries != 1) return;
            if (change == "leave") { h.Inside.Children.Remove(h.Other); h.Root.Children.Add(h.Other); }
            else if (change == "reparent") { h.Inside.Children.Remove(h.Other); h.Replacement.Children.Add(h.Other); }
            else if (change == "hide") h.Other.Visibility = Visibility.Collapsed;
            else h.Other.IsEnabled = false;
        }));
        h.Send(); Assert.Equal(1, queries); Assert.Equal(0, executed);
    }

    [Fact]
    public void ADeclinedModalCommandCannotContinueToAnAncestorOutsideTheDialog()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var command = h.BindDialog(); int executed = 0;
        h.Dialog.CommandBindings.Add(new CommandBinding(command, (_, _) => executed++, (_, e) => { e.CanExecute = false; e.ContinueRouting = true; }));
        h.Window.CommandBindings.Add(new CommandBinding(command, (_, _) => executed++, (_, e) => e.CanExecute = true));
        h.Send(); Assert.Equal(0, executed);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void HiddenOrDisabledDialogScopesDoNotReceivePhysicalCommands(bool disabled, bool rawHandlers)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int queries = 0, executed = 0;
        if (rawHandlers)
        {
            CommandManager.AddCanExecuteHandler(h.Dialog, (_, e) => { queries++; e.CanExecute = true; });
            CommandManager.AddExecutedHandler(h.Dialog, (_, _) => executed++);
        }
        else h.Dialog.CommandBindings.Add(new CommandBinding(ApplicationCommands.Find,
            (_, _) => executed++, (_, e) => { queries++; e.CanExecute = true; }));
        if (disabled) h.Dialog.IsEnabled = false; else h.Dialog.Visibility = Visibility.Collapsed;
        h.Send(Key.F); Assert.Equal((0, 0), (queries, executed));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ProgrammaticCommandsRetainTheOrdinaryAncestorRoute(bool useFocusedTarget)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var command = new RoutedCommand("Programmatic", typeof(MacOSModalCommandRouteTests)); int executed = 0;
        h.Window.CommandBindings.Add(new CommandBinding(command, (_, _) => executed++, (_, e) => e.CanExecute = true));
        if (useFocusedTarget) command.Execute(null); else command.Execute(null, h.Target);
        Assert.Equal(1, executed);
    }

    [Fact]
    public void APhysicalBindingOutsideAModalRetainsItsExplicitForeignWindowTarget()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); using var foreign = new Host();
        Host.Active.SetValue(h.Window, null); Assert.True(h.Target.Focus());
        var command = h.BindDialog(foreign.Target); int executed = 0;
        foreign.Target.CommandBindings.Add(new CommandBinding(command, (_, _) => executed++, (_, e) => e.CanExecute = true));
        h.Send(); Assert.Equal(1, executed);
    }

    [Fact]
    public void NestedModalCommandsStopBeforeTheOuterDialogHandlers()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Root.Children.Remove(h.Dialog);
        var outer = new DialogWithContent(h.Dialog); h.Root.Children.Add(outer); Assert.True(h.Target.Focus());
        var command = h.BindDialog(); int executed = 0, outerCalls = 0;
        h.Dialog.CommandBindings.Add(new CommandBinding(command, (_, _) => executed++, (_, e) => e.CanExecute = true));
        outer.AddHandler(CommandManager.PreviewCanExecuteEvent, new CanExecuteRoutedEventHandler((_, _) => outerCalls++), true);
        outer.AddHandler(CommandManager.CanExecuteEvent, new CanExecuteRoutedEventHandler((_, _) => outerCalls++), true);
        outer.AddHandler(CommandManager.PreviewExecutedEvent, new ExecutedRoutedEventHandler((_, _) => outerCalls++), true);
        outer.AddHandler(CommandManager.ExecutedEvent, new ExecutedRoutedEventHandler((_, _) => outerCalls++), true);
        h.Send(); Assert.Equal(1, executed); Assert.Equal(0, outerCalls);
    }

    private sealed class DialogWithContent : ContentDialog
    {
        internal DialogWithContent(UIElement child) { Visibility = Visibility.Visible; AddVisualChild(child); }
    }

    private sealed class Host : IDisposable
    {
        internal readonly DisplayedTestWindow Window;
        internal readonly StackPanel Root = new(), Inside = new(), Replacement = new();
        internal readonly ContentDialog Dialog;
        internal readonly Control Target = new() { Focusable = true }, Other = new() { Focusable = true }, Background = new() { Focusable = true };
        internal static readonly PropertyInfo Active = typeof(Window).GetProperty("ActiveContentDialog", BindingFlags.Instance | BindingFlags.NonPublic)!;
        internal Host(bool focusInside = true)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus();
            Inside.Children.Add(Target); Inside.Children.Add(Other); Inside.Children.Add(Replacement);
            Dialog = new DialogWithContent(Inside); Root.Children.Add(Dialog); Root.Children.Add(Background);
            Window = new DisplayedTestWindow { Width = 360, Height = 300, TitleBarStyle = WindowTitleBarStyle.Native, Content = Root };
            Window.Measure(new Size(360,300)); Window.Arrange(new Rect(0,0,360,300));
            Assert.Same(Dialog, Inside.VisualParent); Assert.True(Target.IsVisible && Other.IsVisible);
            Assert.True((focusInside ? Target : Background).Focus()); Active.SetValue(Window, Dialog);
        }
        internal RoutedCommand BindDialog(UIElement? explicitTarget = null)
        {
            var command = new RoutedCommand("ModalRoute", typeof(MacOSModalCommandRouteTests));
            Dialog.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control)) { CommandTarget = explicitTarget });
            return command;
        }
        internal void Send(Key key = Key.K) => typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window,
            [new PlatformEvent { Type = PlatformEventType.KeyDown, KeyCode = KeyInterop.VirtualKeyFromKey(key), Modifiers = 8 }]);
        public void Dispose() { Active.SetValue(Window,null); Keyboard.ClearFocus(); Window.Close(); }
    }
}
