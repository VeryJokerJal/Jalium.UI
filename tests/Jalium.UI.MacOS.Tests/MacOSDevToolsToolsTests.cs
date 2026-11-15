using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Diagnostics;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsToolsTests
{
    [Fact]
    public void WorkspaceCommandsRequireActivePageAndKeepPickingModesExclusive()
    {
        using var host = new ToolsHost();
        Assert.False(host.View.RulerCommand.CanExecute(null));
        host.View.RulerCommand.Execute(null);
        Assert.Equal(DevToolsToolMode.None, host.View.ToolMode);
        host.Activate();
        Assert.True(host.View.RulerCommand.CanExecute(null));
        host.View.RulerCommand.Execute(null);
        Assert.Equal(DevToolsToolMode.Ruler, host.View.ToolMode);
        Assert.Equal(host.MouseDownHandlers + 1, Handlers(host.Target, UIElement.PreviewMouseDownEvent));
        Assert.Equal(host.MouseMoveHandlers + 1, Handlers(host.Target, UIElement.PreviewMouseMoveEvent));
        host.View.ColorPickerCommand.Execute(null);
        Assert.Equal(DevToolsToolMode.ColorPicker, host.View.ToolMode);
        Assert.Equal(host.MouseDownHandlers + 1, Handlers(host.Target, UIElement.PreviewMouseDownEvent));
        Assert.Equal(host.MouseMoveHandlers, Handlers(host.Target, UIElement.PreviewMouseMoveEvent));
        host.Window.ActivatePicker();
        Assert.Equal(DevToolsToolMode.None, host.View.ToolMode);
        Assert.True((bool)Field(typeof(DevToolsWindow), "_isPickerActive").GetValue(host.Window)!);
        host.View.RulerCommand.Execute(null);
        Assert.Equal(DevToolsToolMode.Ruler, host.View.ToolMode);
        Assert.False((bool)Field(typeof(DevToolsWindow), "_isPickerActive").GetValue(host.Window)!);
        Assert.Equal(host.KeyHandlers + 1, Handlers(host.Target, UIElement.PreviewKeyDownEvent));
        Assert.Equal(host.MouseMoveHandlers + 1, Handlers(host.Target, UIElement.PreviewMouseMoveEvent));
    }

    [Fact]
    public void RulerUsesWindowCoordinatesCommitsSnapsRestartsAndRecoversFromInvalidInput()
    {
        using var host = new ToolsHost(); host.Activate(); host.View.RulerCommand.Execute(null);
        Assert.False(Click(host.Button, new Point(8, 12), MouseButton.Right).Handled);
        Assert.Null(host.View.Measurement);
        Assert.True(Click(host.Button, new Point(8, 12)).Handled);
        Assert.True(Click(host.Button, new Point(11, 16)).Handled);
        var measurement = Assert.IsType<DevToolsRulerMeasurement>(host.View.Measurement);
        Assert.Equal(new Point(8, 12), measurement.Start); Assert.Equal(new Point(11, 16), measurement.End);
        Assert.Equal(5, measurement.Distance); Assert.False(measurement.IsSnapped);
        Click(host.Button, new Point(20, 30)); Assert.Null(host.View.Measurement);
        Click(host.Button, new Point(40, 31), modifiers: ModifierKeys.Shift);
        measurement = Assert.IsType<DevToolsRulerMeasurement>(host.View.Measurement);
        Assert.True(measurement.IsSnapped); Assert.Equal(30, measurement.End.Y);
        Assert.True(Click(host.Button, new Point(double.NaN, 4)).Handled);
        Assert.True(host.View.HasRulerError); Assert.Same(measurement, host.View.Measurement);
        Click(host.Button, new Point(1, 2)); Assert.False(host.View.HasRulerError); Assert.Null(host.View.Measurement);
        Click(host.Button, new Point(1, 2)); Assert.Equal(0, host.View.Measurement!.Distance);
    }

    [Theory]
    [InlineData(DevToolsToolMode.Ruler)]
    [InlineData(DevToolsToolMode.ColorPicker)]
    public void EscapeCancelsOnlyAnActiveGestureAndReleasesTargetInput(DevToolsToolMode mode)
    {
        using var host = new ToolsHost(); host.Activate();
        (mode == DevToolsToolMode.Ruler ? host.View.RulerCommand : host.View.ColorPickerCommand).Execute(null);
        var escape = KeyArgs(host.Button, Key.Escape); host.Button.RaiseEvent(escape);
        Assert.True(escape.Handled); Assert.Equal(DevToolsToolMode.None, host.View.ToolMode);
        host.AssertNoToolHandlers();
        escape = KeyArgs(host.Button, Key.Escape); host.Button.RaiseEvent(escape);
        Assert.False(escape.Handled);
    }

    [Fact]
    public void PickerReportsInvalidCoordinatesAndUnavailableReadbackWithoutEndingTheGesture()
    {
        using var host = new ToolsHost(); host.Activate(); host.View.ColorPickerCommand.Execute(null);
        Assert.False(Click(host.Button, new Point(10, 20), MouseButton.Middle).Handled);
        Assert.False(host.View.HasPickerError);
        Assert.True(Click(host.Button, new Point(double.PositiveInfinity, 20)).Handled);
        Assert.True(host.View.HasPickerError); Assert.Equal("Invalid coordinates", host.View.PickerStatusText);
        Assert.True(Click(host.Button, new Point(10, 20)).Handled);
        Assert.True(host.View.HasPickerError); Assert.Contains("render target", host.View.PickerStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DevToolsToolMode.ColorPicker, host.View.ToolMode); Assert.Null(host.View.ColorSample);
        Assert.False(host.View.CopyArgbCommand.CanExecute(null));
        host.View.ColorPickerCommand.Execute(null); host.AssertNoToolHandlers(); Assert.False(host.View.HasPickerError);
        host.View.ColorPickerCommand.Execute(null); Assert.False(host.View.HasPickerError);
    }

    [Fact]
    public void FocusTrackingObservesHandledTransitionsAndClearsOnOutsideFocusDeactivationAndLeave()
    {
        using var host = new ToolsHost(); host.Activate(); host.View.FocusCommand.Execute(null);
        Assert.True(host.View.IsFocusTracking);
        var got = new KeyboardFocusChangedEventArgs(FocusService.GotKeyboardFocusEvent, null, host.Button) { Handled = true };
        host.Button.RaiseEvent(got);
        Assert.Equal("Button · TargetAction", host.View.FocusedElementText);
        using var outside = new OutsideWindow();
        host.Button.RaiseEvent(new KeyboardFocusChangedEventArgs(FocusService.LostKeyboardFocusEvent, host.Button, outside.Button));
        Assert.Equal("No focused element", host.View.FocusedElementText);
        host.Button.RaiseEvent(new KeyboardFocusChangedEventArgs(FocusService.GotKeyboardFocusEvent, null, host.Button));
        host.Target.DeactivateForTest(); Assert.Equal("No focused element", host.View.FocusedElementText);
        host.Button.RaiseEvent(new KeyboardFocusChangedEventArgs(FocusService.GotKeyboardFocusEvent, null, host.Button));
        host.Workspace.SelectedPage = DevToolsPage.Inspector;
        Assert.False(host.View.IsFocusTracking); Assert.Equal("No focused element", host.View.FocusedElementText);
        host.Button.RaiseEvent(new KeyboardFocusChangedEventArgs(FocusService.GotKeyboardFocusEvent, null, host.Button));
        Assert.Equal("No focused element", host.View.FocusedElementText); host.AssertNoToolHandlers();
    }

    [Fact]
    public void RepeatedPageChangesAndCloseReleaseEveryToolSubscription()
    {
        using var host = new ToolsHost();
        for (int round = 0; round < 30; round++)
        {
            host.Activate(); host.View.RulerCommand.Execute(null); host.View.FocusCommand.Execute(null);
            host.View.ColorPickerCommand.Execute(null);
            host.Workspace.SelectedPage = DevToolsPage.Inspector;
            Assert.Equal(DevToolsToolMode.None, host.View.ToolMode); Assert.False(host.View.IsFocusTracking);
            Assert.False(host.View.FocusCommand.CanExecute(null)); host.AssertNoToolHandlers();
        }
        host.Activate(); host.View.RulerCommand.Execute(null); host.View.FocusCommand.Execute(null);
        host.Window.CloseDevTools(); host.AssertNoToolHandlers();
        Assert.Equal(DevToolsToolMode.None, host.View.ToolMode); Assert.False(host.View.IsFocusTracking);
        Assert.False(host.View.RulerCommand.CanExecute(null)); Assert.False(host.View.FocusCommand.CanExecute(null));
        host.View.RulerCommand.Execute(null); host.View.FocusCommand.Execute(null); host.AssertNoToolHandlers();
    }

    [Fact]
    public void EscapeInDevToolsCancelsTheToolBeforeClosingTheWindow()
    {
        using var host = new ToolsHost(); host.Activate(); host.View.RulerCommand.Execute(null);
        var escape = KeyArgs(host.View, Key.Escape); escape.RoutedEvent = UIElement.KeyDownEvent;
        host.View.RaiseEvent(escape);
        Assert.True(escape.Handled); Assert.Equal(DevToolsToolMode.None, host.View.ToolMode);
        Assert.True(host.View.RulerCommand.CanExecute(null)); host.AssertNoToolHandlers();
        escape = KeyArgs(host.View, Key.Escape); escape.RoutedEvent = UIElement.KeyDownEvent;
        host.View.RaiseEvent(escape);
        Assert.True(escape.Handled); Assert.False(host.View.RulerCommand.CanExecute(null));
    }

    [Fact]
    public void NativeCloseReleasesTheSessionAndDisablesCommands()
    {
        using var host = new ToolsHost(); host.Activate();
        host.View.ColorPickerCommand.Execute(null); host.View.FocusCommand.Execute(null);
        host.Window.Close(); host.AssertNoToolHandlers();
        Assert.Equal(DevToolsToolMode.None, host.View.ToolMode); Assert.False(host.View.IsFocusTracking);
        Assert.False(host.View.ColorPickerCommand.CanExecute(null)); Assert.False(host.View.FocusCommand.CanExecute(null));
    }

    [Fact]
    public void CancelledCloseKeepsTheSessionAndOverlayUntilAnAcceptedClose()
    {
        using var host = new ToolsHost(); host.Activate();
        host.View.RulerCommand.Execute(null); host.View.FocusCommand.Execute(null);
        var overlay = Field(typeof(DevToolsWindow), "_overlay").GetValue(host.Window);
        int closing = 0;
        host.Window.Closing += (_, args) => args.Cancel = ++closing == 1;
        host.Window.CloseDevTools();
        Assert.Equal(DevToolsToolMode.Ruler, host.View.ToolMode); Assert.True(host.View.IsFocusTracking);
        Assert.True(host.View.RulerCommand.CanExecute(null)); Assert.Same(overlay, Field(typeof(DevToolsWindow), "_overlay").GetValue(host.Window));
        Click(host.Button, new Point(1, 1)); Click(host.Button, new Point(4, 5)); Assert.Equal(5, host.View.Measurement!.Distance);
        host.View.ColorPickerCommand.Execute(null); Assert.Equal(DevToolsToolMode.ColorPicker, host.View.ToolMode);
        host.Window.CloseDevTools(); Assert.Equal(2, closing); host.AssertNoToolHandlers();
        Assert.False(host.View.RulerCommand.CanExecute(null)); Assert.Null(Field(typeof(DevToolsWindow), "_overlay").GetValue(host.Window));
    }

    [Fact]
    public void OverlayCommandsReflectGlobalRendererStateAndRefreshOnReturn()
    {
        using var host = new ToolsHost(); RenderDiagnostics.Mode = RenderDiagnostics.OverlayMode.None;
        host.Activate(); host.View.OverdrawCommand.Execute(null);
        Assert.Equal(RenderDiagnostics.OverlayMode.Overdraw, RenderDiagnostics.Mode); Assert.Equal(RenderDiagnostics.Mode, host.View.OverlayMode);
        host.View.DirtyRegionsCommand.Execute(null);
        Assert.Equal(RenderDiagnostics.OverlayMode.DirtyRegions, host.View.OverlayMode);
        host.View.DirtyRegionsCommand.Execute(null); Assert.Equal(RenderDiagnostics.OverlayMode.None, host.View.OverlayMode);
        host.Workspace.SelectedPage = DevToolsPage.Inspector; RenderDiagnostics.Mode = RenderDiagnostics.OverlayMode.Overdraw;
        host.Activate(); Assert.Equal(RenderDiagnostics.OverlayMode.Overdraw, host.View.OverlayMode);
    }

    private static MouseButtonEventArgs Click(UIElement target, Point point, MouseButton button = MouseButton.Left, ModifierKeys modifiers = ModifierKeys.None)
    {
        var args = new MouseButtonEventArgs(UIElement.PreviewMouseDownEvent, point, button, MouseButtonState.Pressed, 1,
            button == MouseButton.Left ? MouseButtonState.Pressed : MouseButtonState.Released,
            button == MouseButton.Middle ? MouseButtonState.Pressed : MouseButtonState.Released,
            button == MouseButton.Right ? MouseButtonState.Pressed : MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, modifiers, 1) { Source = target };
        target.RaiseEvent(args); return args;
    }

    private static KeyEventArgs KeyArgs(UIElement target, Key key) => new(Keyboard.PrimaryDevice, new TestKeySource { RootVisual = target }, 1, key)
        { RoutedEvent = UIElement.PreviewKeyDownEvent, Source = target };
    private static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static int Handlers(UIElement target, RoutedEvent routedEvent)
    {
        var handlers = (IDictionary?)Field(typeof(UIElement), "_eventHandlers").GetValue(target);
        return handlers?[routedEvent] is ICollection list ? list.Count : 0;
    }

    private sealed class TestKeySource : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
    private sealed class TestWindow : Window
    {
        public TestWindow()
        {
            // Match DisplayedTestWindow: exercise a visible logical root without
            // opening an AppKit surface from an xUnit worker thread.
            if (OperatingSystem.IsMacOS())
                typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(this, [Visibility.Visible]);
        }
        public void DeactivateForTest() => OnDeactivated(EventArgs.Empty);
    }
    private sealed class OutsideWindow : IDisposable
    {
        public Button Button { get; } = new();
        private readonly Window _window;
        public OutsideWindow() => _window = new Window { Content = Button };
        public void Dispose() => _window.Close();
    }
    private sealed class ToolsHost : IDisposable
    {
        public TestWindow Target { get; }
        public Button Button { get; } = new() { Name = "TargetAction", Content = "Target", Margin = new Thickness(20, 30, 0, 0) };
        public DevToolsWindow Window { get; }
        public DevToolsWorkspace Workspace { get; }
        public DevToolsToolsView View { get; }
        public int MouseDownHandlers { get; }
        public int MouseMoveHandlers { get; }
        public int KeyHandlers { get; }
        private readonly int _gotHandlers, _lostHandlers;
        private readonly RenderDiagnostics.OverlayMode _overlayMode = RenderDiagnostics.Mode;
        public ToolsHost()
        {
            Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
            Target = new TestWindow { Content = Button };
            Target.Measure(new Size(640, 480)); Target.Arrange(new Rect(0, 0, 640, 480));
            Window = new DevToolsWindow(Target); Workspace = Assert.IsType<DevToolsWorkspace>(Window.Content);
            View = Assert.IsType<DevToolsToolsView>(Workspace.ToolsContent);
            MouseDownHandlers = Handlers(Target, UIElement.PreviewMouseDownEvent);
            MouseMoveHandlers = Handlers(Target, UIElement.PreviewMouseMoveEvent);
            KeyHandlers = Handlers(Target, UIElement.PreviewKeyDownEvent);
            _gotHandlers = Handlers(Target, FocusService.GotKeyboardFocusEvent);
            _lostHandlers = Handlers(Target, FocusService.LostKeyboardFocusEvent);
        }
        public void Activate() => Workspace.SelectedPage = DevToolsPage.Tools;
        public void AssertNoToolHandlers()
        {
            Assert.Equal(MouseDownHandlers, Handlers(Target, UIElement.PreviewMouseDownEvent));
            Assert.Equal(MouseMoveHandlers, Handlers(Target, UIElement.PreviewMouseMoveEvent));
            Assert.Equal(KeyHandlers, Handlers(Target, UIElement.PreviewKeyDownEvent));
            Assert.Equal(_gotHandlers, Handlers(Target, FocusService.GotKeyboardFocusEvent));
            Assert.Equal(_lostHandlers, Handlers(Target, FocusService.LostKeyboardFocusEvent));
        }
        public void Dispose() { Window.CloseDevTools(); Target.Close(); RenderDiagnostics.Mode = _overlayMode; }
    }
}
