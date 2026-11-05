using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using System.Reflection;

namespace Jalium.UI.Tests;

public sealed class LinuxWindowBehaviorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void LinuxImeContext_ChangingEditorsRetiresThePreviousNativeSession(bool customTitleBar, bool reenter)
    {
        if (!OperatingSystem.IsLinux()) return;
        Keyboard.Initialize();
        using var fixture = new LinuxPlatformFixture(customTitleBar);
        var first = new TextBox { Text = "相同文本", MinHeight = 36 };
        var second = new TextBox { Text = "相同文本", MinHeight = 36 };
        var third = new TextBox { Text = "最终编辑器", MinHeight = 36 };
        var panel = new StackPanel(); panel.Children.Add(first); panel.Children.Add(second); panel.Children.Add(third);
        fixture.Window.Content = panel;
        fixture.Window.Measure(new Size(320, 240)); fixture.Window.Arrange(new Rect(0, 0, 320, 240));
        fixture.Dispatch(new() { Type = PlatformEventType.Activate });
        try
        {
            Assert.True(first.Focus()); Invoke(fixture.Window, "UpdateLinuxImeContext");
            Assert.True(fixture.Platform.ImeContexts[^1].Enabled);
            fixture.Platform.ImeContexts.Clear();
            if (reenter) fixture.Platform.ImeAction = context =>
            {
                if (context.Enabled) return;
                fixture.Platform.ImeAction = null;
                third.Focus();
            };
            Assert.True(second.Focus()); Invoke(fixture.Window, "UpdateLinuxImeContext");
            Assert.Contains(fixture.Platform.ImeContexts, context => !context.Enabled);
            Assert.True(fixture.Platform.ImeContexts[^1].Enabled);
            Assert.Equal(reenter ? third.Text : second.Text, fixture.Platform.ImeContexts[^1].SurroundingText);
            Assert.Same(reenter ? third : second, Keyboard.FocusedElement);
        }
        finally { fixture.Platform.ImeAction = null; InputMethod.CancelComposition(); Keyboard.ClearFocus(); }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    [InlineData(false, 6)]
    [InlineData(true, 6)]
    public void LinuxCompositionCommit_RetiresInputAfterEndingCallback(bool customTitleBar, int mutation)
    {
        if (!OperatingSystem.IsLinux()) return;
        Keyboard.Initialize();
        using var fixture = new LinuxPlatformFixture(customTitleBar);
        var first = new TextBox { Text = "原文", MinHeight = 36 };
        var second = new TextBox { Text = "另一编辑器", MinHeight = 36 };
        var panel = new StackPanel(); panel.Children.Add(first); panel.Children.Add(second);
        fixture.Window.Content = panel;
        fixture.Window.Measure(new Size(320, 240)); fixture.Window.Arrange(new Rect(0, 0, 320, 240));
        EventHandler<CompositionResultEventArgs> handler = (_, args) =>
        {
            if (args.Result == null) return;
            switch (mutation)
            {
                case 1: second.Focus(); break;
                case 2: second.Focus(); first.Focus(); break;
                case 3: first.IsEnabled = false; break;
                case 4: fixture.Window.Hide(); break;
                case 5: panel.Children.Remove(first); break;
                case 6: InputMethod.StartComposition(); break;
            }
        };
        InputMethod.CompositionEnded += handler;
        try
        {
            Assert.True(first.Focus()); first.CaretIndex = first.Text.Length;
            fixture.Dispatch(new() { Type = PlatformEventType.CompositionStart });
            fixture.Dispatch(new() { Type = PlatformEventType.CompositionEnd, CompositionText = "中" });
            Assert.Equal(mutation == 0 ? "原文中" : "原文", first.Text);
            Assert.Equal("另一编辑器", second.Text);
            Assert.Equal(mutation == 6, InputMethod.IsComposing);
        }
        finally
        {
            InputMethod.CompositionEnded -= handler;
            InputMethod.CancelComposition(); Keyboard.ClearFocus();
        }
    }

    [Fact]
    public void PlatformWindowStyle_ReflectsWindowStyleAndResizeMode()
    {
        const uint Borderless = 0x01;
        const uint Resizable = 0x02;
        const uint Titlebar = 0x04;
        const uint Minimizable = 0x10;
        const uint Maximizable = 0x20;

        uint regular = Window.ComputePlatformWindowStyle(
            WindowStyle.SingleBorderWindow,
            ResizeMode.CanResize,
            WindowTitleBarStyle.Native,
            topmost: false,
            allowsTransparency: false);
        Assert.Equal(Titlebar, regular & Titlebar);
        Assert.Equal(Resizable, regular & Resizable);
        Assert.Equal(Minimizable, regular & Minimizable);
        Assert.Equal(Maximizable, regular & Maximizable);
        Assert.Equal(0u, regular & Borderless);

        uint fixedBorderless = Window.ComputePlatformWindowStyle(
            WindowStyle.None,
            ResizeMode.NoResize,
            WindowTitleBarStyle.Native,
            topmost: false,
            allowsTransparency: false);
        Assert.Equal(Borderless, fixedBorderless & Borderless);
        Assert.Equal(0u, fixedBorderless & Resizable);
        Assert.Equal(0u, fixedBorderless & Minimizable);
        Assert.Equal(0u, fixedBorderless & Maximizable);

        uint customMinimizeOnly = Window.ComputePlatformWindowStyle(
            WindowStyle.SingleBorderWindow,
            ResizeMode.CanMinimize,
            WindowTitleBarStyle.Custom,
            topmost: false,
            allowsTransparency: false);
        Assert.Equal(Borderless, customMinimizeOnly & Borderless);
        Assert.Equal(Minimizable, customMinimizeOnly & Minimizable);
        Assert.Equal(0u, customMinimizeOnly & Resizable);
        Assert.Equal(0u, customMinimizeOnly & Maximizable);
    }

    [Fact]
    public void RunModalLoop_ProcessesQueuedDispatcherWorkOnEveryPlatform()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        bool keepRunning = true;
        bool operationRan = false;

        dispatcher.BeginInvoke(() =>
        {
            operationRan = true;
            keepRunning = false;
        });

        dispatcher.RunModalLoop(() => keepRunning);

        Assert.True(operationRan);
        Assert.False(keepRunning);
    }

    [Theory]
    [InlineData(0, WindowState.Normal)]
    [InlineData(1, WindowState.Minimized)]
    [InlineData(2, WindowState.Maximized)]
    [InlineData(3, WindowState.FullScreen)]
    public void PlatformWindowState_PreservesFullscreen(int nativeState, WindowState expected)
    {
        Assert.Equal(expected, Window.MapPlatformWindowState(nativeState));
    }

    [Fact]
    public void LinuxGeometry_ProgrammaticSizeChangesReachNativeWindow()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Window.Width = 410;
        fixture.Window.Height = 270;
        Assert.Equal((820, 540), (fixture.Platform.Width, fixture.Platform.Height));
        Assert.Equal(2, fixture.Platform.ResizeCalls);
    }

    [Fact]
    public void LinuxGeometry_NativeResizePreservesBindingAndDoesNotEchoResize()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        var source = new GeometrySource { Width = 320 };
        var binding = fixture.Window.SetBinding(FrameworkElement.WidthProperty,
            new Binding(nameof(GeometrySource.Width)) { Source = source, Mode = BindingMode.TwoWay });
        int requests = fixture.Platform.ResizeCalls;
        fixture.Dispatch(new() { Type = PlatformEventType.Resize, Width = 800, Height = 500 });
        Assert.Equal(400, source.Width);
        Assert.Equal(250, fixture.Window.Height);
        Assert.Same(binding, BindingOperations.GetBindingExpressionBase(fixture.Window, FrameworkElement.WidthProperty));
        Assert.Equal(requests, fixture.Platform.ResizeCalls);
    }

    [Fact]
    public void LinuxGeometry_NativeMovePreservesPositionBinding()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        var source = new GeometrySource { Left = 12 };
        var binding = fixture.Window.SetBinding(Window.LeftProperty,
            new Binding(nameof(GeometrySource.Left)) { Source = source, Mode = BindingMode.TwoWay });
        fixture.Dispatch(new() { Type = PlatformEventType.Move, X = 200, Y = 160 });
        Assert.Equal(100, source.Left);
        Assert.Equal(80, fixture.Window.Top);
        Assert.Same(binding, BindingOperations.GetBindingExpressionBase(fixture.Window, Window.LeftProperty));
    }

    [Fact]
    public void LinuxSizeToContent_UpdatesNativeDimensionsAndUserResizeDisablesIt()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Window.Content = new Border { Width = 180, Height = 90 };
        fixture.Window.SizeToContent = SizeToContent.WidthAndHeight;
        Invoke(fixture.Window, "UpdatePlatformSizeToContent");
        Assert.Equal((360, 180), (fixture.Platform.Width, fixture.Platform.Height));
        fixture.Dispatch(new() { Type = PlatformEventType.Resize, Width = 460, Height = 300, IsUserInitiatedResize = true });
        Assert.Equal(SizeToContent.Manual, fixture.Window.SizeToContent);
        Assert.Equal((230d, 150d), (fixture.Window.Width, fixture.Window.Height));
    }

    [Fact]
    public void LinuxSizeToContent_PublishesTheAcceptedSizeOnce()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Window.Content = new Border { Width = 180, Height = 90 };
        fixture.Window.SizeToContent = SizeToContent.WidthAndHeight;
        var changes = new List<SizeChangedEventArgs>();
        fixture.Window.SizeChanged += (_, e) => changes.Add(e);
        Invoke(fixture.Window, "UpdatePlatformSizeToContent");
        var change = Assert.Single(changes);
        Assert.Equal(new Size(320, 240), change.PreviousSize);
        Assert.Equal(new Size(180, 90), change.NewSize);
        Assert.True(change.WidthChanged && change.HeightChanged);
        fixture.Dispatch(new() { Type = PlatformEventType.Resize, Width = 360, Height = 180 });
        Assert.Single(changes);
    }

    [Fact]
    public void LinuxGeometry_ProgrammaticResizePublishesTheAcceptedSizeOnce()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        var changes = new List<SizeChangedEventArgs>();
        fixture.Window.SizeChanged += (_, e) => changes.Add(e);
        fixture.Window.Width = 410;
        var change = Assert.Single(changes);
        Assert.Equal(new Size(320, 240), change.PreviousSize);
        Assert.Equal(new Size(410, 240), change.NewSize);
        Assert.True(change.WidthChanged);
        Assert.False(change.HeightChanged);
        fixture.Dispatch(new() { Type = PlatformEventType.Resize, Width = 820, Height = 480 });
        Assert.Single(changes);
        fixture.Window.Height = 270;
        Assert.Equal(2, changes.Count);
        Assert.Equal(new Size(410, 240), changes[1].PreviousSize);
        Assert.Equal(new Size(410, 270), changes[1].NewSize);
    }

    [Fact]
    public void LinuxGeometry_SizeNotificationUsesDipsAcrossScaleChanges()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Dispatch(new() { Type = PlatformEventType.Resize, Width = 640, Height = 480 });
        var changes = new List<SizeChangedEventArgs>();
        fixture.Window.SizeChanged += (_, e) => changes.Add(e);
        typeof(Window).GetField("_dpiScale", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fixture.Window, 1.25d);
        fixture.Dispatch(new() { Type = PlatformEventType.Resize, Width = 400, Height = 300 });
        Assert.Empty(changes);
        fixture.Dispatch(new() { Type = PlatformEventType.Resize, Width = 420, Height = 300 });
        var change = Assert.Single(changes);
        Assert.Equal(new Size(320, 240), change.PreviousSize);
        Assert.Equal(new Size(336, 240), change.NewSize);
        fixture.Dispatch(new() { Type = PlatformEventType.Resize, Width = 420, Height = 300 });
        Assert.Single(changes);
    }

    [Fact]
    public void LinuxGeometry_SizeNotificationUsesTheAcceptedResponseRatherThanTheRequest()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Platform.Handler = null;
        var changes = new List<SizeChangedEventArgs>();
        fixture.Window.SizeChanged += (_, e) => changes.Add(e);
        fixture.Window.Width = 410;
        Assert.Empty(changes);
        fixture.Platform.Width = 800;
        fixture.Platform.Height = 480;
        fixture.Dispatch(new() { Type = PlatformEventType.Resize, Width = 800, Height = 480 });
        var change = Assert.Single(changes);
        Assert.Equal(new Size(320, 240), change.PreviousSize);
        Assert.Equal(new Size(400, 240), change.NewSize);
    }

    [Fact]
    public void LinuxGeometry_SizeNotificationCanSynchronouslyRequestAnotherSize()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        var changes = new List<SizeChangedEventArgs>();
        fixture.Window.SizeChanged += (_, e) =>
        {
            changes.Add(e);
            if (changes.Count == 1) fixture.Window.Width = 430;
        };
        fixture.Window.Width = 410;
        Assert.Equal(2, changes.Count);
        Assert.Equal(new Size(320, 240), changes[0].PreviousSize);
        Assert.Equal(new Size(410, 240), changes[0].NewSize);
        Assert.Equal(new Size(410, 240), changes[1].PreviousSize);
        Assert.Equal(new Size(430, 240), changes[1].NewSize);
    }

    [Fact]
    public void LinuxGeometry_InitialUnspecifiedSizePublishesFiniteGeometry()
    {
        if (!OperatingSystem.IsLinux()) return;
        var window = new Window { Width = double.NaN, Height = double.NaN };
        try
        {
            var changes = new List<SizeChangedEventArgs>();
            window.SizeChanged += (_, e) => changes.Add(e);
            typeof(Window).GetMethod("OnSizeChanged", BindingFlags.Instance | BindingFlags.NonPublic,
                null, [typeof(int), typeof(int)], null)!.Invoke(window, [640, 480]);
            var change = Assert.Single(changes);
            Assert.Equal(new Size(0, 0), change.PreviousSize);
            Assert.Equal(new Size(640, 480), change.NewSize);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(SizeToContent.Width)]
    [InlineData(SizeToContent.Height)]
    [InlineData(SizeToContent.WidthAndHeight)]
    public void LinuxSizeToContent_TilingDefersAutomaticSizingAndUntileResumesIt(SizeToContent mode)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        var content = new Border { Width = 180, Height = 90 };
        fixture.Window.Content = content;
        var source = new GeometrySource { AutoSize = mode };
        var binding = fixture.Window.SetBinding(Window.SizeToContentProperty,
            new Binding(nameof(GeometrySource.AutoSize)) { Source = source, Mode = BindingMode.TwoWay });
        fixture.Platform.IsTiled = true;
        fixture.Platform.Width = 1200;
        fixture.Platform.Height = 800;
        fixture.Dispatch(new() { Type = PlatformEventType.ResizeConstraintsChanged });
        fixture.Dispatch(new() { Type = PlatformEventType.Resize, Width = 1200, Height = 800 });
        int requests = fixture.Platform.ResizeCalls;
        Invoke(fixture.Window, "UpdatePlatformSizeToContent");
        Assert.Equal(requests, fixture.Platform.ResizeCalls);
        Assert.Equal((600d, 400d), (fixture.Window.Width, fixture.Window.Height));
        content.Width = 220;
        content.Height = 110;
        Invoke(fixture.Window, "UpdatePlatformSizeToContent");
        Assert.Equal(requests, fixture.Platform.ResizeCalls);
        fixture.Window.Measure(new Size(600, 400));
        fixture.Window.Arrange(new Rect(0, 0, 600, 400));
        Assert.True(fixture.Window.IsMeasureValid);

        fixture.Platform.IsTiled = false;
        fixture.Dispatch(new() { Type = PlatformEventType.ResizeConstraintsChanged });
        Assert.False(fixture.Window.IsMeasureValid);
        Invoke(fixture.Window, "UpdatePlatformSizeToContent");
        int width = mode is SizeToContent.Width or SizeToContent.WidthAndHeight ? 440 : 1200;
        int height = mode is SizeToContent.Height or SizeToContent.WidthAndHeight ? 220 : 800;
        Assert.Equal((width, height), (fixture.Platform.Width, fixture.Platform.Height));
        Assert.Equal(mode, source.AutoSize);
        Assert.Same(binding, BindingOperations.GetBindingExpressionBase(fixture.Window, Window.SizeToContentProperty));
    }

    [Theory]
    [InlineData(SizeToContent.Width)]
    [InlineData(SizeToContent.WidthAndHeight)]
    public void LinuxSizeToContent_TextBoxContentCanShrinkAfterAWideLayout(SizeToContent mode)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        var editor = new TextBox { Text = "short", MinHeight = 36 };
        var template = new ControlTemplate(typeof(TextBox));
        template.SetVisualTree(() => new Grid { Name = "PART_ContentHost" });
        editor.Template = template;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Auto size", MaxWidth = 240 });
        panel.Children.Add(editor);
        var root = new DockPanel { Margin = new Thickness(24) };
        root.Children.Add(new ScrollViewer { Content = panel });
        fixture.Window.Content = root;
        fixture.Window.Width = 1000;
        fixture.Window.Measure(new Size(1000, 240));
        fixture.Window.Arrange(new Rect(0, 0, 1000, 240));

        fixture.Window.SizeToContent = mode;
        Invoke(fixture.Window, "UpdatePlatformSizeToContent");
        double compactWidth = fixture.Window.Width;
        Assert.InRange(compactWidth, 1, 400);

        var wideContent = new Border { Width = 900, Height = 20 };
        panel.Children.Add(wideContent);
        Invoke(fixture.Window, "UpdatePlatformSizeToContent");
        Assert.True(fixture.Window.Width >= 900);
        var expandedSize = new Size(fixture.Window.Width, fixture.Window.Height);
        fixture.Window.Measure(expandedSize);
        fixture.Window.Arrange(new Rect(new Point(), expandedSize));

        panel.Children.Remove(wideContent);
        Invoke(fixture.Window, "UpdatePlatformSizeToContent");
        Assert.Equal(compactWidth, fixture.Window.Width);
    }

    [Fact]
    public void LinuxSizeToContent_UserResizingATileDisablesAutoSizingWithoutRemovingBinding()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        var source = new GeometrySource { AutoSize = SizeToContent.WidthAndHeight };
        var binding = fixture.Window.SetBinding(Window.SizeToContentProperty,
            new Binding(nameof(GeometrySource.AutoSize)) { Source = source, Mode = BindingMode.TwoWay });
        fixture.Platform.IsTiled = true;
        fixture.Dispatch(new() { Type = PlatformEventType.ResizeConstraintsChanged });
        fixture.Dispatch(new() { Type = PlatformEventType.Resize, Width = 1000, Height = 700, IsUserInitiatedResize = true });
        Assert.Equal(SizeToContent.Manual, fixture.Window.SizeToContent);
        Assert.Equal(SizeToContent.Manual, source.AutoSize);
        Assert.Same(binding, BindingOperations.GetBindingExpressionBase(fixture.Window, Window.SizeToContentProperty));
    }

    [Theory]
    [InlineData(1, 120, 4)]
    [InlineData(319, 120, 8)]
    [InlineData(160, 1, 1)]
    [InlineData(160, 239, 2)]
    [InlineData(1, 1, 5)]
    [InlineData(319, 1, 9)]
    [InlineData(1, 239, 6)]
    [InlineData(319, 239, 10)]
    public void LinuxCustomChrome_EachEdgeStartsNativeResize(double x, double y, int edge)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture(customTitleBar: true);
        fixture.Dispatch(new() { Type = PlatformEventType.MouseDown, Button = 0, MouseX = (float)(x * 2), MouseY = (float)(y * 2), ClickCount = 1 });
        Assert.Equal(edge, fixture.Platform.ResizeEdge);
        Assert.Equal(0, fixture.Platform.MoveCalls);
    }

    [Fact]
    public void LinuxConstrainedEdges_AllCombinationsUseOnlyFreeEdgesForCursorAndResize()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture(customTitleBar: true);
        var positions = new (double X, double Y, int Edge)[]
        {
            (1, 120, 4), (319, 120, 8), (160, 1, 1), (160, 239, 2),
            (1, 1, 5), (319, 1, 9), (1, 239, 6), (319, 239, 10),
        };
        for (int allowed = 0; allowed <= 15; allowed++)
        {
            fixture.Platform.ResizableEdges = (PlatformWindowResizeEdges)allowed;
            fixture.Dispatch(new() { Type = PlatformEventType.ResizeConstraintsChanged });
            foreach (var point in positions)
            {
                int edge = point.Edge & allowed;
                var cursor = ((IInputDispatcherHost)fixture.Window).GetWindowChromeCursor(new Point(point.X, point.Y));
                var expected = edge switch
                {
                    1 or 2 => CursorType.SizeNS,
                    4 or 8 => CursorType.SizeWE,
                    5 or 10 => CursorType.SizeNWSE,
                    6 or 9 => CursorType.SizeNESW,
                    _ => (CursorType?)null,
                };
                Assert.Equal(expected, cursor?.CursorType);
                fixture.Platform.ResizeEdge = 0;
                fixture.Dispatch(new() { Type = PlatformEventType.MouseDown, Button = 0,
                    MouseX = (float)(point.X * 2), MouseY = (float)(point.Y * 2), ClickCount = 1 });
                Assert.Equal(edge, fixture.Platform.ResizeEdge);
                fixture.Dispatch(new() { Type = PlatformEventType.MouseUp, Button = 0,
                    MouseX = (float)(point.X * 2), MouseY = (float)(point.Y * 2) });
            }
        }
    }

    [Fact]
    public void LinuxResizeCursor_PointerRoutingHonorsConstraintsAndOverride()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture(customTitleBar: true);
        var previous = Mouse.OverrideCursor;
        try
        {
            Mouse.OverrideCursor = null;
            fixture.Dispatch(new() { Type = PlatformEventType.MouseMove, MouseX = 2, MouseY = 240 });
            Assert.Equal(5, fixture.Platform.CursorShape);
            fixture.Platform.ResizableEdges = PlatformWindowResizeEdges.Top;
            fixture.Dispatch(new() { Type = PlatformEventType.ResizeConstraintsChanged });
            Assert.Equal(0, fixture.Platform.CursorShape);
            fixture.Dispatch(new() { Type = PlatformEventType.MouseMove, MouseX = 2, MouseY = 240 });
            Assert.Equal(0, fixture.Platform.CursorShape);
            fixture.Dispatch(new() { Type = PlatformEventType.MouseMove, MouseX = 2, MouseY = 2 });
            Assert.Equal(4, fixture.Platform.CursorShape);
            Mouse.OverrideCursor = Cursors.Hand;
            fixture.Dispatch(new() { Type = PlatformEventType.MouseMove, MouseX = 2, MouseY = 2 });
            Assert.Equal(1, fixture.Platform.CursorShape);
            Mouse.OverrideCursor = null;
            fixture.Window.ResizeMode = ResizeMode.NoResize;
            fixture.Dispatch(new() { Type = PlatformEventType.MouseMove, MouseX = 2, MouseY = 2 });
            Assert.Equal(0, fixture.Platform.CursorShape);
        }
        finally { Mouse.OverrideCursor = previous; }
    }

    [Fact]
    public void LinuxCustomChrome_CaptionDragAndDoubleClickReachWindowManager()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture(customTitleBar: true);
        fixture.Dispatch(new() { Type = PlatformEventType.MouseDown, Button = 0, MouseX = 200, MouseY = 40, ClickCount = 1 });
        Assert.Equal(1, fixture.Platform.MoveCalls);
        fixture.Dispatch(new() { Type = PlatformEventType.MouseDown, Button = 0, MouseX = 200, MouseY = 40, ClickCount = 2 });
        Assert.Equal(WindowState.Maximized, fixture.Window.WindowState);
        Assert.Equal(1, fixture.Platform.MoveCalls);
    }

    [Fact]
    public void LinuxNativeChrome_ClientFallbackPreservesRequestedStyleAndCaptionInput()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        var window = fixture.Window;
        var binding = window.SetBinding(Window.TitleBarStyleProperty,
            new Binding(nameof(ChromeSource.Style)) { Source = new ChromeSource(), Mode = BindingMode.TwoWay });
        fixture.Platform.DecorationMode = PlatformWindowDecorationMode.Client;
        fixture.Dispatch(new() { Type = PlatformEventType.DecorationsChanged });
        window.Measure(new Size(320, 240));
        window.Arrange(new Rect(0, 0, 320, 240));
        Assert.NotNull(window.TitleBar);
        Assert.Equal(WindowTitleBarStyle.Native, window.TitleBarStyle);
        Assert.Same(binding, BindingOperations.GetBindingExpressionBase(window, Window.TitleBarStyleProperty));
        fixture.Dispatch(new() { Type = PlatformEventType.MouseDown, Button = 0, MouseX = 200, MouseY = 40, ClickCount = 1 });
        Assert.Equal(1, fixture.Platform.MoveCalls);
        fixture.Dispatch(new() { Type = PlatformEventType.MouseDown, Button = 0, MouseX = 638, MouseY = 240, ClickCount = 1 });
        Assert.Equal(8, fixture.Platform.ResizeEdge);
        fixture.Dispatch(new() { Type = PlatformEventType.MouseDown, Button = 0, MouseX = 200, MouseY = 40, ClickCount = 2 });
        Assert.Equal(WindowState.Maximized, window.WindowState);
    }

    [Fact]
    public void LinuxChrome_NegotiationSwitchesVisualsWithoutDuplicateOrPhantomChildren()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture(customTitleBar: true);
        var window = fixture.Window;
        window.Content = new Border();
        int children = window.VisualChildrenCount;
        var firstBar = window.TitleBar;
        fixture.Platform.DecorationMode = PlatformWindowDecorationMode.Server;
        fixture.Dispatch(new() { Type = PlatformEventType.DecorationsChanged });
        Assert.Null(window.TitleBar);
        Assert.Null(firstBar!.VisualParent);
        Assert.Equal(children - 1, window.VisualChildrenCount);
        fixture.Platform.DecorationMode = PlatformWindowDecorationMode.Client;
        fixture.Dispatch(new() { Type = PlatformEventType.DecorationsChanged });
        Assert.NotNull(window.TitleBar);
        Assert.Equal(children, window.VisualChildrenCount);
        var secondBar = window.TitleBar;
        fixture.Dispatch(new() { Type = PlatformEventType.DecorationsChanged });
        Assert.Same(secondBar, window.TitleBar);
        window.TitleBarStyle = WindowTitleBarStyle.Native;
        window.WindowStyle = WindowStyle.None;
        Invoke(window, "RefreshLinuxWindowChrome");
        Assert.Null(window.TitleBar);
    }

    [Fact]
    public void LinuxNativeChrome_FallbackHonorsCaptionCapabilitiesAndHiddenTitle()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Platform.DecorationMode = PlatformWindowDecorationMode.Client;
        fixture.Dispatch(new() { Type = PlatformEventType.DecorationsChanged });
        var window = fixture.Window;
        window.Title = "原生标题栏回退";
        window.ResizeMode = ResizeMode.NoResize;
        window.IsShowCloseButton = false;
        Assert.Equal(window.Title, window.TitleBar!.Title);
        Assert.False(window.TitleBar.ShowMinimizeButton);
        Assert.False(window.TitleBar.ShowMaximizeButton);
        Assert.False(window.TitleBar.ShowCloseButton);
        window.IsShowTitleBar = false;
        Assert.Equal(Visibility.Collapsed, window.TitleBar.Visibility);
        window.IsShowTitleBar = true;
        Assert.Equal(Visibility.Visible, window.TitleBar.Visibility);
        window.WindowState = WindowState.FullScreen;
        Assert.True(window.IsShowTitleBar);
        Assert.Equal(Visibility.Collapsed, window.TitleBar.Visibility);
        window.WindowState = WindowState.Normal;
        Assert.Equal(Visibility.Visible, window.TitleBar.Visibility);
    }

    private sealed class ChromeSource { public WindowTitleBarStyle Style { get; set; } = WindowTitleBarStyle.Native; }

    [Theory]
    [InlineData(WindowState.Minimized)]
    [InlineData(WindowState.Maximized)]
    [InlineData(WindowState.FullScreen)]
    public void LinuxCapabilities_UnsupportedRequestsKeepActualStateAndBinding(WindowState requested)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture(customTitleBar: true);
        fixture.Register(0x7850);
        fixture.Platform.Capabilities = PlatformWindowCapabilities.StateRequestsRestricted | PlatformWindowCapabilities.Close;
        fixture.Dispatch(new() { Type = PlatformEventType.CapabilitiesChanged });
        var source = new StateSource { State = requested };
        var binding = fixture.Window.SetBinding(Window.WindowStateProperty,
            new Binding(nameof(StateSource.State)) { Source = source });
        Assert.Equal(WindowState.Normal, fixture.Window.WindowState);
        Assert.Empty(fixture.Platform.States);
        Assert.Same(binding, BindingOperations.GetBindingExpressionBase(fixture.Window, Window.WindowStateProperty));
        fixture.Platform.Capabilities = PlatformWindowCapabilities.All | PlatformWindowCapabilities.StateRequestsRestricted;
        fixture.Dispatch(new() { Type = PlatformEventType.CapabilitiesChanged });
        Assert.Equal(requested, fixture.Window.WindowState);
        Assert.Equal(new[] { requested }, fixture.Platform.States);
        fixture.Dispatch(new() { Type = PlatformEventType.CapabilitiesChanged });
        Assert.Single(fixture.Platform.States);
    }

    [Fact]
    public void LinuxCapabilities_RestrictingPendingStatePublishesActualStateWithoutNativeEcho()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Register(0x7851);
        fixture.Window.WindowState = WindowState.Maximized;
        fixture.Platform.States.Clear();
        int changes = 0;
        fixture.Window.StateChanged += (_, _) => changes++;
        fixture.Platform.Capabilities = PlatformWindowCapabilities.StateRequestsRestricted | PlatformWindowCapabilities.Close;
        fixture.Dispatch(new() { Type = PlatformEventType.CapabilitiesChanged });
        Assert.Equal(WindowState.Normal, fixture.Window.WindowState);
        Assert.Equal(1, changes);
        Assert.Empty(fixture.Platform.States);
    }

    [Fact]
    public void LinuxCapabilities_DisallowedMoveAndResizeDoNotStartInteractiveRequests()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture(customTitleBar: true);
        fixture.Platform.Capabilities = PlatformWindowCapabilities.Close;
        fixture.Dispatch(new() { Type = PlatformEventType.CapabilitiesChanged });
        fixture.Dispatch(new() { Type = PlatformEventType.MouseDown, Button = 0, MouseX = 200, MouseY = 40, ClickCount = 1 });
        fixture.Dispatch(new() { Type = PlatformEventType.MouseDown, Button = 0, MouseX = 2, MouseY = 240, ClickCount = 1 });
        Assert.Equal(0, fixture.Platform.MoveCalls);
        Assert.Equal(0, fixture.Platform.ResizeEdge);
    }

    [Fact]
    public void LinuxCapabilities_ChromeReflectsActionsWithoutChangingRequestedButtons()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture(customTitleBar: true);
        fixture.Platform.Capabilities = PlatformWindowCapabilities.Close;
        fixture.Dispatch(new() { Type = PlatformEventType.CapabilitiesChanged });
        Assert.True(fixture.Window.IsShowMaximizeButton);
        Assert.True(fixture.Window.IsShowMinimizeButton);
        Assert.False(fixture.Window.TitleBar!.ShowMaximizeButton);
        Assert.False(fixture.Window.TitleBar.ShowMinimizeButton);
        fixture.Dispatch(new() { Type = PlatformEventType.MouseDown, Button = 0, MouseX = 200, MouseY = 40, ClickCount = 2 });
        Assert.Equal(WindowState.Normal, fixture.Window.WindowState);
        fixture.Platform.Capabilities = PlatformWindowCapabilities.All;
        fixture.Dispatch(new() { Type = PlatformEventType.CapabilitiesChanged });
        Assert.True(fixture.Window.TitleBar.ShowMaximizeButton);
        Assert.True(fixture.Window.TitleBar.ShowMinimizeButton);
    }

    [Fact]
    public void LinuxCapabilities_NativeStateRemainsAuthoritativeAndRestoreRemainsAvailable()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture(customTitleBar: true);
        fixture.Platform.Capabilities = PlatformWindowCapabilities.StateRequestsRestricted | PlatformWindowCapabilities.Close;
        fixture.Dispatch(new() { Type = PlatformEventType.CapabilitiesChanged });
        fixture.Platform.ActualState = WindowState.Maximized;
        fixture.Dispatch(new() { Type = PlatformEventType.StateChanged, NewState = 2 });
        Assert.Equal(WindowState.Maximized, fixture.Window.WindowState);
        Assert.True(fixture.Window.TitleBar!.ShowMaximizeButton);
        fixture.Window.WindowState = WindowState.Normal;
        Assert.Equal(WindowState.Normal, fixture.Window.WindowState);
    }

    private sealed class StateSource { public WindowState State { get; set; } }

    [Fact]
    public void LinuxRenderingSuspension_PausesFrameRegistrationWithoutChangingVisibilityOrState()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Register(0x7855);
        fixture.SetNativeVisible();
        Assert.True(fixture.IsRegisteredAsRenderable);
        fixture.Platform.IsRenderingSuspended = true;
        fixture.Dispatch(new() { Type = PlatformEventType.RenderingAvailabilityChanged });
        Assert.False(fixture.IsRegisteredAsRenderable);
        Assert.Equal(Visibility.Visible, fixture.Window.Visibility);
        Assert.Equal(WindowState.Normal, fixture.Window.WindowState);
        Assert.Equal(0, fixture.Platform.HideCalls);
        Assert.Empty(fixture.Platform.States);
        fixture.Platform.IsRenderingSuspended = false;
        fixture.Dispatch(new() { Type = PlatformEventType.RenderingAvailabilityChanged });
        Assert.True(fixture.IsRegisteredAsRenderable);
        fixture.Dispatch(new() { Type = PlatformEventType.RenderingAvailabilityChanged });
        Assert.True(fixture.IsRegisteredAsRenderable);
    }

    [Fact]
    public void LinuxCloseShortcut_UsesCancellableCloseAndIgnoresRepeatOrAdditionalModifiers()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        int closeRequests = 0;
        fixture.Window.Closing += (_, args) => { closeRequests++; args.Cancel = true; };
        fixture.Dispatch(new() { Type = PlatformEventType.KeyDown, KeyCode = 0x73, Modifiers = 4 });
        Assert.Equal(1, closeRequests);
        fixture.Dispatch(new() { Type = PlatformEventType.KeyDown, KeyCode = 0x73, Modifiers = 4, IsRepeat = 1 });
        fixture.Dispatch(new() { Type = PlatformEventType.KeyDown, KeyCode = 0x73, Modifiers = 6 });
        fixture.Window.IsEnabled = false;
        fixture.Dispatch(new() { Type = PlatformEventType.KeyDown, KeyCode = 0x73, Modifiers = 4 });
        Assert.Equal(1, closeRequests);
    }

    [Theory]
    [InlineData(ResizeMode.NoResize)]
    [InlineData(ResizeMode.CanMinimize)]
    public void LinuxCustomChrome_FixedWindowDoesNotStartResize(ResizeMode mode)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture(customTitleBar: true);
        fixture.Window.ResizeMode = mode;
        fixture.Dispatch(new() { Type = PlatformEventType.MouseDown, Button = 0, MouseX = 2, MouseY = 240, ClickCount = 1 });
        Assert.Equal(0, fixture.Platform.ResizeEdge);
    }

    [Theory]
    [InlineData(1, 100, 20, true)]
    [InlineData(0, 100, 20, false)]
    [InlineData(0, 300, 20, true)]
    [InlineData(0, 100, 160, true)]
    public void LinuxCustomChrome_RejectsNonCaptionAndDisabledInput(int button, float x, float y, bool enabled)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture(customTitleBar: true);
        fixture.Window.IsEnabled = enabled;
        fixture.Dispatch(new() { Type = PlatformEventType.MouseDown, Button = button,
            MouseX = x * 2, MouseY = y * 2, ClickCount = 1 });
        Assert.Equal(0, fixture.Platform.MoveCalls);
        Assert.Equal(0, fixture.Platform.ResizeEdge);
    }

    [Fact]
    public void LinuxWindowState_NativeChangePreservesBindingWithoutEcho()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        var source = new GeometrySource();
        var binding = fixture.Window.SetBinding(Window.WindowStateProperty,
            new Binding(nameof(GeometrySource.State)) { Source = source, Mode = BindingMode.TwoWay });
        fixture.Dispatch(new() { Type = PlatformEventType.StateChanged, NewState = 3 });
        Assert.Equal(WindowState.FullScreen, source.State);
        Assert.Same(binding, BindingOperations.GetBindingExpressionBase(fixture.Window, Window.WindowStateProperty));
        Assert.Empty(fixture.Platform.States);
    }

    [Fact]
    public void LinuxWindowState_ReentrantStateChangedRequestReachesNativeWindow()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Register(0x7840);
        fixture.Window.StateChanged += (_, _) =>
        {
            if (fixture.Window.WindowState == WindowState.Maximized)
                fixture.Window.WindowState = WindowState.FullScreen;
        };
        fixture.Dispatch(new() { Type = PlatformEventType.StateChanged, NewState = 2 });
        Assert.Equal(WindowState.FullScreen, fixture.Window.WindowState);
        Assert.Equal(new[] { WindowState.FullScreen }, fixture.Platform.States);
    }

    [Fact]
    public void LinuxVisibility_EffectivePropertyAndHidePreserveBindings()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Register(0x7310);
        var binding = fixture.Window.SetBinding(UIElement.VisibilityProperty,
            new Binding(nameof(GeometrySource.Visibility)) { Source = new GeometrySource(), Mode = BindingMode.TwoWay });
        fixture.Window.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Hidden);
        Assert.Equal(1, fixture.Platform.HideCalls);
        fixture.Window.Hide();
        Assert.Equal(Visibility.Hidden, fixture.Window.Visibility);
        Assert.Same(binding, BindingOperations.GetBindingExpressionBase(fixture.Window, UIElement.VisibilityProperty));
    }

    [Fact]
    public void LinuxDialogs_NestedSessionsBlockOtherWindowsAndRestoreTheirOriginalState()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var owner = new LinuxPlatformFixture();
        using var sibling = new LinuxPlatformFixture();
        owner.Register(0x7320);
        sibling.Register(0x7321);
        var outer = new CallbackDialogWindow { Owner = owner.Window };
        var inner = new CallbackDialogWindow { Owner = outer };
        using var outerFixture = new LinuxPlatformFixture(window: outer);
        using var innerFixture = new LinuxPlatformFixture(window: inner);
        outer.ShowAction = () =>
        {
            Assert.False(owner.Window.IsEnabled);
            Assert.False(sibling.Window.IsEnabled);
            Assert.True(outer.IsEnabled);
            inner.ShowAction = () =>
            {
                Assert.False(outer.IsEnabled);
                Assert.False(owner.Window.IsEnabled);
                Assert.True(inner.IsEnabled);
                inner.Hide();
            };
            Assert.False(inner.ShowDialog());
            Assert.True(outer.IsEnabled);
            Assert.False(owner.Window.IsEnabled);
            Assert.False(sibling.Window.IsEnabled);
            outer.Hide();
        };
        Assert.False(outer.ShowDialog());
        Assert.True(owner.Window.IsEnabled);
        Assert.True(sibling.Window.IsEnabled);
        Assert.True(outer.IsEnabled);
    }

    [Fact]
    public void LinuxOwnership_AcceptedOwnerCloseClosesChildrenWithoutAnotherCancellation()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var owner = new LinuxPlatformFixture();
        using var child = new LinuxPlatformFixture();
        owner.Register(0x7330);
        child.Register(0x7331);
        child.Window.Owner = owner.Window;
        int closingCalls = 0;
        child.Window.Closing += (_, e) => { closingCalls++; e.Cancel = true; };
        owner.Window.Close();
        Assert.Equal(0, closingCalls);
        Assert.True(child.Platform.Disposed);
        Assert.Equal(Visibility.Collapsed, child.Window.Visibility);
    }

    [Fact]
    public void LinuxOwnership_CancelingOwnerCloseKeepsOwnedWindowsAlive()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var owner = new LinuxPlatformFixture();
        using var child = new LinuxPlatformFixture();
        child.Window.Owner = owner.Window;
        EventHandler<System.ComponentModel.CancelEventArgs> cancel = (_, e) => e.Cancel = true;
        owner.Window.Closing += cancel;
        owner.Window.Close();
        Assert.False(child.Platform.Disposed);
        Assert.False(owner.Platform.Disposed);
        owner.Window.Closing -= cancel;
    }

    [Fact]
    public void LinuxDialogs_ClosingWithoutAResultReturnsFalse()
    {
        if (!OperatingSystem.IsLinux()) return;
        var dialog = new CallbackDialogWindow();
        using var fixture = new LinuxPlatformFixture(window: dialog);
        dialog.ShowAction = dialog.Close;
        Assert.False(dialog.ShowDialog());
    }

    [Fact]
    public void LinuxSystemMenu_KeyboardOperationsFollowPlatformAndResizeCapabilities()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        ContextMenu Menu() => (ContextMenu)Invoke(fixture.Window, "CreateFallbackSystemMenu")!;
        MenuItem Item(ContextMenu menu, string name) => menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, name));
        fixture.Platform.KeyboardOperations = PlatformWindowKeyboardOperations.All;
        var menu = Menu();
        Assert.True(Item(menu, "Move").IsEnabled);
        Assert.True(Item(menu, "Resize").IsEnabled);
        fixture.Window.ResizeMode = ResizeMode.NoResize;
        menu = Menu();
        Assert.True(Item(menu, "Move").IsEnabled);
        Assert.False(Item(menu, "Resize").IsEnabled);
        fixture.Window.ResizeMode = ResizeMode.CanResize;
        fixture.Platform.KeyboardOperations = PlatformWindowKeyboardOperations.None;
        menu = Menu();
        Assert.False(Item(menu, "Move").IsEnabled);
        Assert.False(Item(menu, "Resize").IsEnabled);
        fixture.Platform.KeyboardOperations = PlatformWindowKeyboardOperations.All;
        fixture.Platform.Capabilities &= ~PlatformWindowCapabilities.Move;
        fixture.Dispatch(new() { Type = PlatformEventType.CapabilitiesChanged });
        Assert.False(Item(Menu(), "Move").IsEnabled);
    }

    [Theory]
    [InlineData(WindowState.Maximized)]
    [InlineData(WindowState.FullScreen)]
    public void LinuxSystemMenu_LargeStatesOfferRestoreAndPreserveStateBinding(WindowState state)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Platform.KeyboardOperations = PlatformWindowKeyboardOperations.All;
        var source = new GeometrySource { State = state };
        var binding = fixture.Window.SetBinding(Window.WindowStateProperty,
            new Binding(nameof(GeometrySource.State)) { Source = source, Mode = BindingMode.TwoWay });
        var menu = (ContextMenu)Invoke(fixture.Window, "CreateFallbackSystemMenu")!;
        MenuItem Item(string name) => menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, name));
        Assert.True(Item("Restore").IsEnabled);
        Assert.False(Item("Move").IsEnabled);
        Assert.False(Item("Resize").IsEnabled);
        Item("Restore").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(WindowState.Normal, fixture.Window.WindowState);
        Assert.Same(binding, BindingOperations.GetBindingExpressionBase(fixture.Window, Window.WindowStateProperty));
        Assert.Equal(WindowState.Normal, source.State);
    }

    [Theory]
    [InlineData("Minimize", WindowState.Minimized)]
    [InlineData("Maximize", WindowState.Maximized)]
    public void LinuxSystemMenu_StateActionsPreserveBindings(string item, WindowState target)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        var source = new GeometrySource();
        var binding = fixture.Window.SetBinding(Window.WindowStateProperty,
            new Binding(nameof(GeometrySource.State)) { Source = source, Mode = BindingMode.TwoWay });
        var menu = (ContextMenu)Invoke(fixture.Window, "CreateFallbackSystemMenu")!;
        menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, item)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(target, fixture.Window.WindowState);
        Assert.Same(binding, BindingOperations.GetBindingExpressionBase(fixture.Window, Window.WindowStateProperty));
        Assert.Equal(target, source.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinuxSystemMenu_KeyboardHandoffClosesPopupBeforeCallingPlatform(bool resize)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Platform.KeyboardOperations = PlatformWindowKeyboardOperations.All;
        var menu = (ContextMenu)Invoke(fixture.Window, "CreateFallbackSystemMenu")!;
        menu.IsOpen = true;
        var popup = (Jalium.UI.Controls.Primitives.Popup)typeof(ContextMenu)
            .GetField("_popup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        fixture.Platform.KeyboardOperationAction = _ => Assert.False(popup.IsOpen);
        var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, resize ? "Resize" : "Move"));
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.False(menu.IsOpen);
        Assert.False(popup.IsOpen);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        DrainMenuHandoff();
        Assert.Equal(new[] { resize }, fixture.Platform.KeyboardOperationCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinuxSystemMenu_DeferredHandoffRechecksWindowLifetimeAndCapabilities(bool close)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Platform.KeyboardOperations = PlatformWindowKeyboardOperations.All;
        var menu = (ContextMenu)Invoke(fixture.Window, "CreateFallbackSystemMenu")!;
        menu.IsOpen = true;
        menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Move")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (close) fixture.Window.Close();
        else fixture.Platform.KeyboardOperations = PlatformWindowKeyboardOperations.None;
        DrainMenuHandoff();
        Assert.Empty(fixture.Platform.KeyboardOperationCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinuxSystemMenu_ConsumedAltSpaceDoesNotInsertACharacter(bool repeat)
    {
        if (!OperatingSystem.IsLinux()) return;
        Keyboard.ClearFocus();
        using var fixture = new LinuxPlatformFixture();
        var editor = new TextBox { Text = "Linux 中文焦点", Focusable = true };
        fixture.Window.Content = editor;
        fixture.Window.Measure(new Size(320, 240));
        fixture.Window.Arrange(new Rect(0, 0, 320, 240));
        Assert.True(editor.Focus());
        editor.CaretIndex = editor.Text.Length;
        fixture.Dispatch(new() { Type = PlatformEventType.KeyDown, KeyCode = 0x20, Modifiers = 4, IsRepeat = repeat ? 1 : 0 });
        fixture.Dispatch(new() { Type = PlatformEventType.CharInput, Codepoint = 0x20 });
        Assert.Equal("Linux 中文焦点", editor.Text);
        (typeof(Window).GetField("_fallbackSystemMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Window) as ContextMenu)?.CloseImmediately();
        DrainMenuHandoff();
        fixture.Dispatch(new() { Type = PlatformEventType.KeyUp, KeyCode = 0x20 });
        fixture.Dispatch(new() { Type = PlatformEventType.KeyDown, KeyCode = 0x20 });
        fixture.Dispatch(new() { Type = PlatformEventType.CharInput, Codepoint = 0x20 });
        Assert.Equal("Linux 中文焦点 ", editor.Text);
    }

    [Fact]
    public void LinuxSystemMenu_ShortcutWithoutCharacterDoesNotSuppressLaterTyping()
    {
        if (!OperatingSystem.IsLinux()) return;
        Keyboard.ClearFocus();
        using var fixture = new LinuxPlatformFixture();
        var editor = new TextBox { Text = "原文", Focusable = true };
        fixture.Window.Content = editor;
        fixture.Window.Measure(new Size(320, 240));
        fixture.Window.Arrange(new Rect(0, 0, 320, 240));
        Assert.True(editor.Focus());
        editor.CaretIndex = editor.Text.Length;
        fixture.Dispatch(new() { Type = PlatformEventType.KeyDown, KeyCode = 0x20, Modifiers = 4 });
        fixture.Dispatch(new() { Type = PlatformEventType.KeyUp, KeyCode = 0x20 });
        (typeof(Window).GetField("_fallbackSystemMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Window) as ContextMenu)?.CloseImmediately();
        DrainMenuHandoff();
        fixture.Dispatch(new() { Type = PlatformEventType.CharInput, Codepoint = 0x20 });
        Assert.Equal("原文 ", editor.Text);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("closing")]
    [InlineData("teardown")]
    public void LinuxSystemMenu_RejectsInactiveWindowLifetime(string state)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Register(new nint(0x8124));
        fixture.Platform.SystemMenuResult = true;
        switch (state)
        {
            case "disabled": fixture.Window.IsEnabled = false; break;
            case "hidden": fixture.Window.Visibility = Visibility.Hidden; break;
            case "closing": typeof(Window).GetField("_isClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(fixture.Window, true); break;
            case "teardown": typeof(Window).GetField("_managedTeardownStarted", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(fixture.Window, true); break;
        }
        Assert.False(fixture.Window.TryShowSystemMenuAtScreen(new Point(100, 100)));
        Assert.Equal(0, fixture.Platform.SystemMenuCalls);
        Assert.False((bool)Invoke(fixture.Window, "TryHandleLinuxWindowSystemKey", Key.Space, ModifierKeys.Alt, false)!);
    }

    [Fact]
    public void LinuxSystemMenu_ActiveWindowCanUseThePlatformMenu()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture();
        fixture.Register(new nint(0x8124));
        fixture.Platform.SystemMenuResult = true;
        Assert.True(fixture.Window.TryShowSystemMenuAtScreen(new Point(100, 100)));
        Assert.Equal(1, fixture.Platform.SystemMenuCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinuxMouseCancellation_ReleasesCaptureAndPressWithoutClicking(bool customTitleBar)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new LinuxPlatformFixture(customTitleBar);
        fixture.Register(0x8125);
        var editor = new TextBox { Text = "原始中文", Height = 36 };
        var button = new Button { Content = "操作", Height = 40 };
        var panel = new StackPanel();
        panel.Children.Add(editor);
        panel.Children.Add(button);
        fixture.Window.Content = panel;
        fixture.Window.Measure(new Size(320, 240));
        fixture.Window.Arrange(new Rect(0, 0, 320, 240));
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        var point = button.TranslatePoint(new Point(10, 10), fixture.Window);
        var down = new PlatformEvent { Type = PlatformEventType.MouseDown, Button = 0,
            MouseX = (float)(point.X * 2), MouseY = (float)(point.Y * 2), ClickCount = 1 };
        fixture.Dispatch(down);
        Assert.True(button.IsPressed);
        Assert.Same(button, Mouse.Captured);
        editor.Focus();
        fixture.Dispatch(new() { Type = PlatformEventType.MouseCancel });
        fixture.Dispatch(new() { Type = PlatformEventType.MouseCancel });
        Assert.Null(Mouse.Captured);
        Assert.False(button.IsPressed);
        Assert.False(button.IsMouseOver);
        Assert.Equal(MouseButtonState.Released, Mouse.LeftButton);
        Assert.Same(editor, Keyboard.FocusedElement);
        down.Type = PlatformEventType.MouseUp;
        fixture.Dispatch(down);
        Assert.Equal(0, clicks);
        down.Type = PlatformEventType.MouseDown;
        fixture.Dispatch(down);
        down.Type = PlatformEventType.MouseUp;
        fixture.Dispatch(down);
        Assert.Equal(1, clicks);
        Assert.Equal("原始中文", editor.Text);
    }

    private static void DrainMenuHandoff()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        bool pending = true;
        dispatcher.BeginInvoke(() => pending = false);
        dispatcher.RunModalLoop(() => pending);
    }

    private static object? Invoke(Window window, string name, params object[] arguments) =>
        typeof(Window).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);

    private sealed class GeometrySource
    {
        public SizeToContent AutoSize { get; set; }
        public double Width { get; set; }
        public double Left { get; set; }
        public WindowState State { get; set; }
        public Visibility Visibility { get; set; } = Visibility.Visible;
    }

    private sealed class CallbackDialogWindow : Window
    {
        public Action? ShowAction;
        public override void Show()
        {
            typeof(Window).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(this, Dispatcher.CurrentDispatcher);
            ShowAction?.Invoke();
        }
    }

    private sealed class LinuxPlatformFixture : IDisposable
    {
        public Window Window { get; }
        public FakeLinuxWindow Platform { get; } = new();
        private nint _registeredHandle;
        public LinuxPlatformFixture(bool customTitleBar = false, Window? window = null)
        {
            Window = window ?? new Window { Width = 320, Height = 240,
                TitleBarStyle = customTitleBar ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native };
            SetField("_platformWindow", Platform);
            SetField("_dpiScale", 2d);
            Platform.Handler = Dispatch;
            Window.Measure(new Size(320, 240));
            Window.Arrange(new Rect(0, 0, 320, 240));
        }
        public void Register(nint handle)
        {
            _registeredHandle = handle;
            SetField("<Handle>k__BackingField", handle);
            var windows = (Dictionary<nint, Window>)typeof(Window).GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            windows.Add(handle, Window);
        }
        public void Dispatch(PlatformEvent evt) => Invoke(Window, "OnPlatformEvent", evt);
        public bool IsRegisteredAsRenderable => (bool)typeof(Window)
            .GetField("_registeredAsRenderable", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window)!;
        public void SetNativeVisible()
        {
            SetField("_nativeWindowHidden", false);
            Invoke(Window, "UpdateRenderableRegistration");
        }
        private void SetField(string name, object? value) =>
            typeof(Window).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Window, value);
        public void Dispose()
        {
            SetField("_platformWindow", null);
            SetField("<Handle>k__BackingField", nint.Zero);
            if (_registeredHandle != nint.Zero)
            {
                var windows = (Dictionary<nint, Window>)typeof(Window).GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                windows.Remove(_registeredHandle);
            }
            Platform.Handler = null;
            Window.Close();
        }
    }

    private sealed class FakeLinuxWindow : IPlatformWindow
    {
        public int Width = 640, Height = 480, ResizeCalls, ResizeEdge, MoveCalls, HideCalls;
        public Action<PlatformEvent>? Handler;
        public List<WindowState> States = [];
        public bool Disposed;
        public nint NativeHandle => 0x1234;
        public NativeSurfaceDescriptor GetSurface() => default;
        public void Show() { }
        public void Hide() => HideCalls++;
        public void Close() { }
        public void SetTitle(string title) { }
        public void Resize(int width, int height)
        {
            Width = width; Height = height; ResizeCalls++;
            Handler?.Invoke(new() { Type = PlatformEventType.Resize, Width = width, Height = height });
        }
        public void Move(int x, int y) { }
        public int GetWidth() => Width;
        public int GetHeight() => Height;
        public void GetPosition(out int x, out int y) { x = 0; y = 0; }
        public void SetMinMaxSize(int minWidth, int minHeight, int maxWidth, int maxHeight) { }
        public bool BeginMoveDrag() { MoveCalls++; return true; }
        public bool BeginResizeDrag(int edge) { ResizeEdge = edge; return true; }
        public PlatformWindowKeyboardOperations KeyboardOperations { get; set; }
        public List<bool> KeyboardOperationCalls { get; } = [];
        public Action<bool>? KeyboardOperationAction;
        public bool BeginKeyboardMoveResize(bool resize)
        {
            KeyboardOperationCalls.Add(resize);
            KeyboardOperationAction?.Invoke(resize);
            return true;
        }
        public bool SetIcon(uint[]? pixels, int width, int height) => true;
        public bool SetTopmost(bool value) => true;
        public bool SetEnabled(bool value) => true;
        public bool SetOpacity(double value) => true;
        public bool SetShowInTaskbar(bool value) => true;
        public bool SetResizable(bool value) => true;
        public bool SetDecorated(bool value) => true;
        public PlatformWindowDecorationMode DecorationMode { get; set; }
        public PlatformWindowCapabilities Capabilities { get; set; } = PlatformWindowCapabilities.All;
        public bool IsRenderingSuspended { get; set; }
        public bool IsTiled { get; set; }
        public PlatformWindowResizeEdges ResizableEdges { get; set; } = PlatformWindowResizeEdges.All;
        public bool SetOwner(nint value) => true;
        public bool Activate() => true;
        public bool SystemMenuResult;
        public int SystemMenuCalls;
        public bool ShowSystemMenu(int x, int y) { SystemMenuCalls++; return SystemMenuResult; }
        public void SetState(WindowState state) => States.Add(state);
        public WindowState ActualState;
        public WindowState GetState() => ActualState;
        public void Invalidate() { }
        public float GetDpiScale() => 2;
        public int GetMonitorRefreshRate() => 60;
        public int CursorShape;
        public void SetCursor(int shape) => CursorShape = shape;
        public List<PlatformImeContext> ImeContexts { get; } = [];
        public Action<PlatformImeContext>? ImeAction;
        public void UpdateImeContext(PlatformImeContext context) { ImeContexts.Add(context); ImeAction?.Invoke(context); }
        public void SetEventHandler(Action<PlatformEvent>? handler) => Handler = handler;
        public void Dispose() => Disposed = true;
    }
}
