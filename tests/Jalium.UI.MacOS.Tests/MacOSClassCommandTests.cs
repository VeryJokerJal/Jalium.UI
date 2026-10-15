using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSClassCommandTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ClassInputAppliesToItsOwnerAndDerivedWindows(bool derived)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<InputOwner>(derived);
        var command = new Counter();
        CommandManager.RegisterClassInputBinding(typeof(ClassWindow<InputOwner>), KeyBinding(command));
        host.Key(); Assert.Equal(1, command.Executions); Assert.Equal(1, command.Queries);
    }

    [Fact]
    public void ClassInputNeverAppliesToAnUnrelatedWindow()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<InputOther>(); var command = new Counter();
        CommandManager.RegisterClassInputBinding(typeof(ClassWindow<Unrelated>), KeyBinding(command));
        host.Key(); Assert.Equal(0, command.Queries);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void InstanceAndDerivedClassInputOverrideBaseClassInput(bool instance)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<InputPriority>(true); var older = new Counter(); var derived = new Counter(); var local = new Counter();
        CommandManager.RegisterClassInputBinding(typeof(ClassWindow<InputPriority>), KeyBinding(older));
        CommandManager.RegisterClassInputBinding(typeof(DerivedWindow<InputPriority>), KeyBinding(derived));
        if (instance) host.Window.InputBindings.Add(KeyBinding(local));
        host.Key(); Assert.Equal((0, instance ? 0 : 1, instance ? 1 : 0), (older.Executions, derived.Executions, local.Executions));
    }

    [Fact]
    public void LaterClassInputReplacesAnEarlierMatchingRegistration()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<InputLatest>(); var older = new Counter(); var newer = new Counter();
        CommandManager.RegisterClassInputBinding(typeof(ClassWindow<InputLatest>), KeyBinding(older));
        CommandManager.RegisterClassInputBinding(typeof(ClassWindow<InputLatest>), KeyBinding(newer));
        host.Key(); Assert.Equal((0, 1), (older.Executions, newer.Executions));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void RegistrationOwnsItsBindingForTheTypeLifetime(bool commandBinding)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<Retained>(); var count = new Counter(); var routed = NewCommand();
        WeakReference reference = commandBinding
            ? RegisterCommandForGc(typeof(ClassWindow<Retained>), routed, count)
            : RegisterInputForGc(typeof(ClassWindow<Retained>), count);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.True(reference.IsAlive);
        if (commandBinding) routed.Execute(null, host.Window); else host.Key();
        Assert.Equal(1, count.Executions);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ClassMouseBindingsUseTheNativeWindowRoute(bool wheel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<MouseOwner>(); var count = new Counter();
        var binding = new MouseBinding(count, new MouseGesture(wheel ? MouseAction.WheelClick : MouseAction.LeftClick, ModifierKeys.Control));
        CommandManager.RegisterClassInputBinding(typeof(ClassControl<MouseOwner>), binding);
        host.Mouse(wheel); Assert.Equal(1, count.Executions);
    }

    [Fact]
    public void ClassInputStopsAtTheActiveModalScope()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<ModalOwner>(); var background = new Counter(); var dialogCommand = new Counter();
        CommandManager.RegisterClassInputBinding(typeof(ClassWindow<ModalOwner>), KeyBinding(background));
        CommandManager.RegisterClassInputBinding(typeof(ClassDialog), KeyBinding(dialogCommand));
        var dialog = new ClassDialog { Visibility = Visibility.Visible }; host.Root.Children.Add(dialog);
        var property = typeof(Window).GetProperty("ActiveContentDialog", BindingFlags.Instance | BindingFlags.NonPublic)!;
        property.SetValue(host.Window, dialog);
        try { host.Key(); Assert.Equal((0, 1), (background.Executions, dialogCommand.Executions)); }
        finally { property.SetValue(host.Window, null); }
    }

    [Theory] [InlineData("close")] [InlineData("focus")] [InlineData("move")] [InlineData("hide")]
    public void ClassInputCannotExecuteAfterItsQueryChangesTheInputRoute(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<InputMutation>(); var count = new Counter { Query = () => { host.Mutate(mutation); return true; } };
        CommandManager.RegisterClassInputBinding(typeof(ClassWindow<InputMutation>), KeyBinding(count));
        host.Key(); Assert.Equal(1, count.Queries); Assert.Equal(0, count.Executions);
    }

    [Fact]
    public void ClassGestureCallbacksDoNotHoldTheRegistrationLock()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<LockOwner>(); var count = new Counter(); int matches = 0;
        var other = KeyBinding(new Counter());
        CommandManager.RegisterClassInputBinding(typeof(ClassWindow<LockOwner>), new InputBinding(count, new CallbackGesture(() =>
        {
            matches++;
            Assert.True(Task.Run(() => CommandManager.RegisterClassInputBinding(typeof(ClassWindow<LockOther>), other))
                .Wait(TimeSpan.FromSeconds(2)));
            return true;
        })));
        host.Key(); Assert.Equal(1, matches); Assert.Equal(1, count.Executions);
    }

    [Fact]
    public void RegistrationDuringClassMatchingAffectsTheNextInput()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<InputSnapshot>(); var count = new Counter(); int matches = 0;
        CommandManager.RegisterClassInputBinding(typeof(ClassWindow<InputSnapshot>), new InputBinding(new Counter(), new CallbackGesture(() =>
        {
            matches++; CommandManager.RegisterClassInputBinding(typeof(ClassWindow<InputSnapshot>), KeyBinding(count)); return false;
        })));
        host.Key(); Assert.Equal(1, matches); Assert.Equal(0, count.Executions);
        host.Key(); Assert.Equal(1, count.Executions);
    }

    [Fact]
    public void ClassCommandsWithTheSameCommandStayBoundToTheirOwnTypes()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var a = new Host<CommandA>(); using var b = new Host<CommandB>(); var command = NewCommand(); int first = 0, second = 0;
        CommandManager.RegisterClassCommandBinding(typeof(ClassControl<CommandA>), Binding(command, () => first++));
        CommandManager.RegisterClassCommandBinding(typeof(ClassControl<CommandB>), Binding(command, () => second++));
        command.Execute(null, a.Target); command.Execute(null, b.Target);
        Assert.Equal((1, 1), (first, second));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void CommandBindingResolutionUsesTheMostDerivedApplicableType(bool derived)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<CommandInheritance>(true); var command = NewCommand(); int baseCalls = 0, derivedCalls = 0;
        CommandManager.RegisterClassCommandBinding(typeof(ClassWindow<CommandInheritance>), Binding(command, () => baseCalls++));
        if (derived) CommandManager.RegisterClassCommandBinding(typeof(DerivedWindow<CommandInheritance>), Binding(command, () => derivedCalls++));
        command.Execute(null, host.Window); Assert.Equal((derived ? 0 : 1, derived ? 1 : 0), (baseCalls, derivedCalls));
    }

    [Fact]
    public void AnInstanceCommandOverridesClassBindingsWithoutBeingInvokedTwice()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<InstanceCommand>(); var command = NewCommand(); int local = 0, queries = 0, type = 0;
        host.Target.CommandBindings.Add(new CommandBinding(command, (_, _) => local++, (_, e) => { queries++; e.CanExecute = true; }));
        CommandManager.RegisterClassCommandBinding(typeof(ClassControl<InstanceCommand>), Binding(command, () => type++));
        command.Execute(null, host.Target); Assert.Equal((1, 1, 0), (local, queries, type));
    }

    [Fact]
    public void RegisteringAnotherCommandDoesNotDuplicateClassPreviewHandlers()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<PreviewOnce>(); var command = NewCommand(); int previews = 0, executions = 0;
        var binding = Binding(command, () => executions++); binding.PreviewExecuted += (_, _) => previews++;
        CommandManager.RegisterClassCommandBinding(typeof(ClassControl<PreviewOnce>), binding);
        CommandManager.RegisterClassCommandBinding(typeof(ClassControl<PreviewOnce>), Binding(NewCommand(), () => { }));
        command.Execute(null, host.Target); Assert.Equal((1, 1), (previews, executions));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AnExecutedOnlyBindingIsEnabledByDefault(bool onClass)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<ExecutedOnly>(); var command = NewCommand(); int executions = 0;
        var binding = new CommandBinding(command, (_, _) => executions++);
        if (onClass) CommandManager.RegisterClassCommandBinding(typeof(ClassControl<ExecutedOnly>), binding);
        else host.Target.CommandBindings.Add(binding);
        Assert.True(command.CanExecute(null, host.Target)); command.Execute(null, host.Target); Assert.Equal(1, executions);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void PreviewCanExecuteCanApproveOrRejectBeforeTheBubbleQuery(bool allow)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<PreviewQuery>(); var command = NewCommand(); int previews = 0, queries = 0;
        var binding = new CommandBinding(command, (_, _) => { }, (_, e) => { queries++; e.CanExecute = !allow; });
        binding.PreviewCanExecute += (_, e) => { previews++; e.CanExecute = allow; e.Handled = true; };
        CommandManager.RegisterClassCommandBinding(typeof(ClassControl<PreviewQuery>), binding);
        Assert.Equal(allow, command.CanExecute(null, host.Target)); Assert.Equal((1, 0), (previews, queries));
    }

    [Theory] [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void CommandBindingsContributeCustomDefaultGestures(bool onClass, bool mouse)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<DefaultGesture>(); var command = NewCommand(); int executions = 0;
        command.InputGestures.Add(mouse ? new MouseGesture(MouseAction.WheelClick, ModifierKeys.Control) : new KeyGesture(Key.K, ModifierKeys.Control));
        var binding = Binding(command, () => executions++);
        if (onClass) CommandManager.RegisterClassCommandBinding(typeof(ClassControl<DefaultGesture>), binding);
        else host.Target.CommandBindings.Add(binding);
        if (mouse) host.Mouse(true); else host.Key(); Assert.Equal(1, executions);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AlreadyHandledCommandEventsDoNotInvokeBindings(bool query)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<HandledCommand>(); var command = NewCommand(); int callbacks = 0;
        host.Target.CommandBindings.Add(new CommandBinding(command, (_, _) => callbacks++, (_, e) => { callbacks++; e.CanExecute = true; }));
        if (query)
        {
            CommandManager.AddCanExecuteHandler(host.Target, (_, e) => e.Handled = true);
            host.Target.RaiseEvent(new CanExecuteRoutedEventArgs(command, null) { RoutedEvent = CommandManager.CanExecuteEvent });
        }
        else
        {
            CommandManager.AddExecutedHandler(host.Target, (_, e) => e.Handled = true);
            host.Target.RaiseEvent(new ExecutedRoutedEventArgs(command, null) { RoutedEvent = CommandManager.ExecutedEvent });
        }
        Assert.Equal(0, callbacks);
    }

    [Fact]
    public void ADecliningClassQueryCanReachTheNextApplicableBinding()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<QueryFallback>(); var command = NewCommand(); int first = 0, second = 0;
        CommandManager.RegisterClassCommandBinding(typeof(ClassControl<QueryFallback>), new CommandBinding(command, (_, _) => { }, (_, e) => { first++; e.CanExecute = false; }));
        CommandManager.RegisterClassCommandBinding(typeof(ClassControl<QueryFallback>), new CommandBinding(command, (_, _) => { }, (_, e) => { second++; e.CanExecute = true; }));
        Assert.True(command.CanExecute(null, host.Target)); Assert.Equal((1, 1), (first, second));
    }

    [Fact]
    public void ClassCommandQueriesUseAStableSnapshotDuringRegistration()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<CommandSnapshot>(); var command = NewCommand(); int later = 0;
        CommandManager.RegisterClassCommandBinding(typeof(ClassControl<CommandSnapshot>), new CommandBinding(command, (_, _) => { }, (_, e) =>
        {
            CommandManager.RegisterClassCommandBinding(typeof(ClassControl<CommandSnapshot>), new CommandBinding(command, (_, _) => { }, (_, args) => { later++; args.CanExecute = true; }));
            e.CanExecute = false;
        }));
        Assert.False(command.CanExecute(null, host.Target)); Assert.Equal(0, later);
        Assert.True(command.CanExecute(null, host.Target)); Assert.Equal(1, later);
    }

    [Fact]
    public void ProgrammaticCommandsWithoutAnExplicitTargetCaptureTheKeyboardFocus()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<ImplicitFocus>(); var command = NewCommand(); int executions = 0;
        host.Target.CommandBindings.Add(Binding(command, () => executions++));
        Assert.True(command.CanExecute(null)); command.Execute(null); Assert.Equal(1, executions);
        Keyboard.ClearFocus(); Assert.False(command.CanExecute(null));
    }

    [Theory] [InlineData("close")] [InlineData("focus")] [InlineData("move")] [InlineData("hide")]
    public void PreviewQueryMutationStopsLaterPhysicalInputCallbacks(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<PreviewMutation>(); var command = NewCommand(); int previews = 0, queries = 0, executions = 0;
        CommandManager.AddPreviewCanExecuteHandler(host.Target, (_, _) => { previews++; host.Mutate(mutation); });
        CommandManager.RegisterClassCommandBinding(typeof(ClassControl<PreviewMutation>), new CommandBinding(command, (_, _) => executions++, (_, e) => { queries++; e.CanExecute = true; }));
        host.Target.InputBindings.Add(KeyBinding(command)); host.Key();
        Assert.Equal((1, 0, 0), (previews, queries, executions));
    }

    [Fact]
    public void ADecliningPhysicalQueryCannotContinueAfterItMovesFocus()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<QueryMutation>(); var command = NewCommand(); int first = 0, later = 0;
        CommandManager.RegisterClassCommandBinding(typeof(ClassControl<QueryMutation>), new CommandBinding(command, (_, _) => { }, (_, e) => { first++; Assert.True(host.Other.Focus()); e.CanExecute = false; }));
        CommandManager.RegisterClassCommandBinding(typeof(ClassControl<QueryMutation>), new CommandBinding(command, (_, _) => { }, (_, e) => { later++; e.CanExecute = true; }));
        host.Target.InputBindings.Add(KeyBinding(command)); host.Key(); Assert.Equal((1, 0), (first, later));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void DeclinedRoutedInputCanExplicitlyContinueToTheAncestor(bool continueRouting)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<ContinueInput>(); var command = NewCommand(); var parent = new Counter();
        host.Target.CommandBindings.Add(new CommandBinding(command, (_, _) => { }, (_, e) =>
            { e.CanExecute = false; e.ContinueRouting = continueRouting; }));
        host.Target.InputBindings.Add(KeyBinding(command)); host.Window.InputBindings.Add(KeyBinding(parent));
        host.Key(); Assert.Equal(continueRouting ? 1 : 0, parent.Executions);
    }

    [Fact]
    public void InstanceInputMatchingUsesItsCapturedBindingSnapshot()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<LocalInputSnapshot>(); var count = new Counter(); int matches = 0;
        host.Target.InputBindings.Add(new InputBinding(new Counter(), new CallbackGesture(() =>
            { matches++; host.Target.InputBindings.Clear(); return false; })));
        host.Target.InputBindings.Add(KeyBinding(count));
        host.Key(); Assert.Equal((1, 1), (matches, count.Executions));
        host.Key(); Assert.Equal(1, count.Executions);
    }

    [Fact]
    public void InstanceCommandQueriesUseTheirCapturedBindingSnapshot()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<LocalCommandSnapshot>(); var command = NewCommand(); int first = 0, later = 0;
        host.Target.CommandBindings.Add(new CommandBinding(command, (_, _) => { }, (_, e) =>
            { first++; host.Target.CommandBindings.Clear(); e.CanExecute = false; }));
        host.Target.CommandBindings.Add(new CommandBinding(command, (_, _) => { }, (_, e) =>
            { later++; e.CanExecute = true; }));
        Assert.True(command.CanExecute(null, host.Target)); Assert.Equal((1, 1), (first, later));
        Assert.False(command.CanExecute(null, host.Target));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ABindingWithoutAPreviewHandlerDoesNotConsumeAnUnhandledPreview(bool onClass)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host<UnhandledPreview>(); var command = NewCommand(); int previews = 0, queries = 0;
        CommandManager.AddPreviewCanExecuteHandler(host.Window, (_, e) => { previews++; e.CanExecute = true; });
        var binding = new CommandBinding(command, (_, _) => { }, (_, e) => { queries++; e.CanExecute = false; });
        if (onClass) CommandManager.RegisterClassCommandBinding(typeof(ClassControl<UnhandledPreview>), binding);
        else host.Target.CommandBindings.Add(binding);
        Assert.False(command.CanExecute(null, host.Target)); Assert.Equal((1, 1), (previews, queries));
    }

    private static KeyBinding KeyBinding(ICommand command) => new(command, new KeyGesture(Key.K, ModifierKeys.Control));
    private static RoutedCommand NewCommand() => new(Guid.NewGuid().ToString("N"), typeof(MacOSClassCommandTests));
    private static CommandBinding Binding(RoutedCommand command, Action execute) => new(command, (_, _) => execute(), (_, e) => e.CanExecute = true);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterInputForGc(Type type, Counter count)
    { var b = KeyBinding(count); CommandManager.RegisterClassInputBinding(type, b); return new WeakReference(b); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterCommandForGc(Type type, RoutedCommand command, Counter count)
    { var b = Binding(command, () => count.Executions++); CommandManager.RegisterClassCommandBinding(type, b); return new WeakReference(b); }
    private sealed class Counter : ICommand
    {
        internal int Queries, Executions; internal Func<bool>? Query;
        public bool CanExecute(object? p) { Queries++; return Query?.Invoke() ?? true; }
        public void Execute(object? p) => Executions++;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
    private sealed class CallbackGesture(Func<bool> match) : InputGesture
    { public override bool Matches(object target, InputEventArgs args) => args is KeyEventArgs { Key: Key.K } && match(); }
    private class ClassWindow<T> : Window
    {
        public ClassWindow()
        { TitleBarStyle = WindowTitleBarStyle.Native; if (OperatingSystem.IsMacOS()) typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, [Visibility.Visible]); }
    }
    private sealed class DerivedWindow<T> : ClassWindow<T> { }
    private sealed class ClassControl<T> : Control { }
    private sealed class ClassDialog : ContentDialog { }
    private sealed class Host<T> : IDisposable
    {
        internal readonly ClassWindow<T> Window;
        internal readonly StackPanel Root = new(), Panel = new(), Replacement = new();
        internal readonly ClassControl<T> Target = new() { Focusable = true, Width = 200, Height = 60, Background = Brushes.White };
        internal readonly Control Other = new() { Focusable = true, Width = 200, Height = 60 };
        internal Host(bool derived = false)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus(); Jalium.UI.Input.Mouse.Capture(null);
            Root.Children.Add(Panel); Root.Children.Add(Replacement); Panel.Children.Add(Target); Panel.Children.Add(Other);
            Window = derived ? new DerivedWindow<T>() : new ClassWindow<T>(); Window.Width = 300; Window.Height = 240; Window.Content = Root;
            Window.Measure(new Size(300, 240)); Window.Arrange(new Rect(0, 0, 300, 240)); Assert.True(Target.Focus());
        }
        internal void Key() => Send(new PlatformEvent { Type = PlatformEventType.KeyDown, KeyCode = KeyInterop.VirtualKeyFromKey(Jalium.UI.Input.Key.K), Modifiers = 8 });
        internal void Mouse(bool wheel)
        {
            var point = Target.TranslatePoint(new Point(20, 20), Window);
            Assert.Same(Target, Window.HitTest(point)?.VisualHit);
            Send(new PlatformEvent { Type = wheel ? PlatformEventType.MouseWheel : PlatformEventType.MouseDown,
                MouseX = (int)Math.Round(point.X * Window.DpiScale), MouseY = (int)Math.Round(point.Y * Window.DpiScale),
                Modifiers = 8, Button = 0, ClickCount = 1, WheelDeltaY = 1,
                HasMouseButtonStates = true, MouseButtons = wheel ? 0u : 1u });
        }
        private void Send(PlatformEvent e) => typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, [e]);
        internal void Mutate(string mutation)
        {
            if (mutation == "close") Window.Close();
            else if (mutation == "focus") Assert.True(Other.Focus());
            else if (mutation == "hide") Target.Visibility = Visibility.Hidden;
            else { Panel.Children.Remove(Target); Replacement.Children.Add(Target); }
        }
        public void Dispose()
        { Jalium.UI.Input.Mouse.Capture(null); Jalium.UI.Input.Mouse.UpdateState(default, null, MouseButtonStates.AllReleased); Keyboard.ClearFocus(); Window.Close(); }
    }
    private sealed class ContinueInput { } private sealed class LocalInputSnapshot { } private sealed class LocalCommandSnapshot { }
    private sealed class UnhandledPreview { }
    private sealed class InputOwner { } private sealed class InputOther { } private sealed class Unrelated { }
    private sealed class InputPriority { } private sealed class InputLatest { } private sealed class Retained { }
    private sealed class MouseOwner { } private sealed class ModalOwner { } private sealed class InputMutation { }
    private sealed class LockOwner { } private sealed class LockOther { } private sealed class InputSnapshot { }
    private sealed class CommandA { } private sealed class CommandB { } private sealed class CommandInheritance { }
    private sealed class InstanceCommand { } private sealed class PreviewOnce { } private sealed class ExecutedOnly { }
    private sealed class PreviewQuery { } private sealed class DefaultGesture { } private sealed class HandledCommand { }
    private sealed class QueryFallback { } private sealed class CommandSnapshot { } private sealed class ImplicitFocus { }
    private sealed class PreviewMutation { } private sealed class QueryMutation { }
}
