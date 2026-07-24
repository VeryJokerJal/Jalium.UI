using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Helpers;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;

public class TitleBarHitTestTests
{
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;

    private static readonly byte[] s_testPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO7+4u0AAAAASUVORK5CYII=");

    [Fact]
    public void Window_NewTitleBarProperties_ShouldSyncToTitleBar()
    {
        var leftCommands = new Border { Width = 42, Height = 20 };
        var rightCommands = new Border { Width = 48, Height = 20 };
        var explicitIcon = BitmapImage.FromBytes(s_testPngBytes);

        var window = new Window
        {
            Width = 320,
            Height = 180,
            Title = "Sync Test",
            LeftWindowCommands = leftCommands,
            RightWindowCommands = rightCommands,
            IsShowIcon = false,
            IsShowTitle = false,
            IsShowTitleBar = false,
            IsShowMinimizeButton = false,
            IsShowMaximizeButton = false,
            IsShowCloseButton = false,
            TitleBarHeight = 44,
            WindowIcon = explicitIcon
        };

        var titleBar = Assert.IsType<TitleBar>(window.TitleBar);

        Assert.Equal("Sync Test", titleBar.Title);
        Assert.Same(leftCommands, titleBar.LeftWindowCommands);
        Assert.Same(rightCommands, titleBar.RightWindowCommands);
        Assert.False(titleBar.IsShowIcon);
        Assert.False(titleBar.IsShowTitle);
        Assert.False(titleBar.ShowMinimizeButton);
        Assert.False(titleBar.ShowMaximizeButton);
        Assert.False(titleBar.ShowCloseButton);
        Assert.Equal(44, titleBar.Height);
        Assert.Same(explicitIcon, titleBar.WindowIcon);
        Assert.Equal(Visibility.Collapsed, titleBar.Visibility);
    }

    [Fact]
    public void ComputeNcHitTest_WhenTitleBarHidden_ShouldReturnHTCLIENT()
    {
        var window = new Window
        {
            Width = 320,
            Height = 200,
            IsShowTitleBar = false
        };

        LayoutWindow(window, 320, 200);

        int hit = window.ComputeNcHitTestFromClientDip(120, 12, isMaximized: false);
        Assert.Equal(HTCLIENT, hit);
    }

    [Fact]
    public void ComputeNcHitTest_WhenInsideLeftOrRightCommands_ShouldReturnHTCLIENT()
    {
        var leftCommands = new Border { Width = 64, Height = 20 };
        var rightCommands = new Border { Width = 72, Height = 20 };

        var window = new Window
        {
            Width = 420,
            Height = 220,
            LeftWindowCommands = leftCommands,
            RightWindowCommands = rightCommands
        };

        LayoutWindow(window, 420, 220);

        var titleBar = Assert.IsType<TitleBar>(window.TitleBar);
        SetPrivateField(titleBar, "_templateLookupAttempted", true);

        var leftBounds = new Rect(14, 6, 64, 20);
        var rightBounds = new Rect(150, 6, 72, 20);
        SetCommandHost(titleBar, "_leftWindowCommandsHost", leftBounds);
        SetCommandHost(titleBar, "_rightWindowCommandsHost", rightBounds);

        var leftCenter = GetRectCenter(leftBounds);
        var rightCenter = GetRectCenter(rightBounds);

        int leftHit = window.ComputeNcHitTestFromClientDip(leftCenter.X, leftCenter.Y, isMaximized: false);
        int rightHit = window.ComputeNcHitTestFromClientDip(rightCenter.X, rightCenter.Y, isMaximized: false);

        Assert.Equal(HTCLIENT, leftHit);
        Assert.Equal(HTCLIENT, rightHit);
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(5.999, 90)]
    [InlineData(294, 90)]
    [InlineData(299.999, 90)]
    [InlineData(150, 0)]
    [InlineData(150, 5.999)]
    [InlineData(150, 174)]
    [InlineData(150, 179.999)]
    public void ComputeNcHitTest_WhenPointIsInsideClientEdge_ShouldNotResize(double x, double y)
    {
        var window = new Window
        {
            Width = 300,
            Height = 180,
            IsShowTitleBar = false,
            ResizeMode = ResizeMode.CanResize
        };

        LayoutWindow(window, 300, 180);

        Assert.Equal(HTCLIENT, window.ComputeNcHitTestFromClientDip(x, y, isMaximized: false));
    }

    [Fact]
    public void ComputeNcHitTest_WhenPointIsInOuterFrame_ShouldResize()
    {
        const int HTLEFT = 10;
        const int HTRIGHT = 11;
        const int HTTOP = 12;
        const int HTBOTTOM = 15;

        var window = new Window
        {
            Width = 300,
            Height = 180,
            IsShowTitleBar = false,
            ResizeMode = ResizeMode.CanResize
        };

        LayoutWindow(window, 300, 180);

        Assert.Equal(HTLEFT, window.ComputeNcHitTestFromClientDip(-1, 90, isMaximized: false));
        Assert.Equal(HTRIGHT, window.ComputeNcHitTestFromClientDip(300, 90, isMaximized: false));
        Assert.Equal(HTTOP, window.ComputeNcHitTestFromClientDip(150, -1, isMaximized: false));
        Assert.Equal(HTBOTTOM, window.ComputeNcHitTestFromClientDip(150, 180, isMaximized: false));
    }

    [Fact]
    public void Arrange_WhenTitleBarHidden_ContentStartsAtTop()
    {
        var content = new Border();
        var window = new Window
        {
            Width = 360,
            Height = 240,
            IsShowTitleBar = false,
            Content = content
        };

        LayoutWindow(window, 360, 240);

        Assert.Equal(0, content.VisualBounds.Y);
        Assert.Equal(360, content.VisualBounds.Width);
        Assert.Equal(240, content.VisualBounds.Height);
    }

    [Fact]
    public void Arrange_WithCustomTitleBarHeight_ContentStartsBelowConfiguredHeight()
    {
        var content = new Border();
        var window = new Window
        {
            Width = 360,
            Height = 240,
            TitleBarHeight = 48,
            Content = content
        };

        LayoutWindow(window, 360, 240);

        var titleBar = Assert.IsType<TitleBar>(window.TitleBar);
        Assert.Equal(48, titleBar.VisualBounds.Height);
        Assert.Equal(48, content.VisualBounds.Y);
        Assert.Equal(192, content.VisualBounds.Height);
    }

    [Fact]
    public void ComputeNcHitTest_WithCustomTitleBarHeight_UsesConfiguredCaptionBand()
    {
        var window = new Window
        {
            Width = 320,
            Height = 200,
            TitleBarHeight = 48
        };

        LayoutWindow(window, 320, 200);

        int inCaptionBand = window.ComputeNcHitTestFromClientDip(20, 40, isMaximized: false);
        int belowCaptionBand = window.ComputeNcHitTestFromClientDip(20, 56, isMaximized: false);

        Assert.Equal(HTCAPTION, inCaptionBand);
        Assert.Equal(HTCLIENT, belowCaptionBand);
    }

    [Fact]
    public void IsSystemMenuCaptionPoint_OnlyAcceptsDraggableCaptionArea()
    {
        var window = new Window
        {
            Width = 320,
            Height = 200,
            TitleBarHeight = 48,
            HasSystemMenu = true
        };

        LayoutWindow(window, 320, 200);

        Assert.True(window.IsSystemMenuCaptionPoint(new Point(20, 40)));
        Assert.False(window.IsSystemMenuCaptionPoint(new Point(20, 56)));

        window.HasSystemMenu = false;
        Assert.False(window.IsSystemMenuCaptionPoint(new Point(20, 40)));
    }

    [Fact]
    public void WindowIcon_DefaultAutoLoad_ShouldNotThrow_And_ExplicitOverrideWorks()
    {
        Exception? creationException = Record.Exception(() => _ = new Window());
        Assert.Null(creationException);

        var window = new Window
        {
            Width = 300,
            Height = 180
        };
        var titleBar = Assert.IsType<TitleBar>(window.TitleBar);

        // Ensure the default path can be exercised without exceptions.
        _ = window.WindowIcon;

        var explicitIcon = BitmapImage.FromBytes(s_testPngBytes);
        window.WindowIcon = explicitIcon;

        Assert.Same(explicitIcon, window.WindowIcon);
        Assert.Same(explicitIcon, titleBar.WindowIcon);
    }

    [Fact]
    public void ProcessIconExtraction_ReturnsPixelsReadyForDirectBitmapCreation()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var processPath = Assert.IsType<string>(Environment.ProcessPath);
        var extracted = Assert.IsType<ProcessIconPixels>(
            IconHelper.ExtractProcessIconPixels(processPath));

        Assert.True(extracted.Width > 0);
        Assert.True(extracted.Height > 0);
        Assert.Equal(extracted.Width * 4, extracted.Stride);
        Assert.True(extracted.Pixels.Length >= extracted.Stride * extracted.Height);

        using var bitmap = BitmapImage.FromPixels(
            extracted.Pixels,
            extracted.Width,
            extracted.Height,
            extracted.Stride);
        Assert.Equal(extracted.Width, bitmap.PixelWidth);
        Assert.Equal(extracted.Height, bitmap.PixelHeight);
    }

    [Fact]
    public void GetTitleBarButtonAtPoint_ShouldRespectButtonWidths()
    {
        var window = new Window
        {
            Width = 300,
            Height = 180
        };

        var titleBar = Assert.IsType<TitleBar>(window.TitleBar);
        SetButtonWidth(titleBar, TitleBarButtonKind.Minimize, 20);
        SetButtonWidth(titleBar, TitleBarButtonKind.Maximize, 50);
        SetButtonWidth(titleBar, TitleBarButtonKind.Close, 80);

        LayoutWindow(window, 300, 180);

        Assert.Equal(TitleBarButtonKind.Close, HitTestTitleBarButton(window, new Point(225, 8)).Kind);
        Assert.Equal(TitleBarButtonKind.Maximize, HitTestTitleBarButton(window, new Point(185, 8)).Kind);
        Assert.Equal(TitleBarButtonKind.Minimize, HitTestTitleBarButton(window, new Point(155, 8)).Kind);
    }

    [Fact]
    public void GetTitleBarButtonAtPoint_ShouldRespectButtonHeightOutsideTitleBarCaptionHeight()
    {
        var window = new Window
        {
            Width = 300,
            Height = 180
        };

        var titleBar = Assert.IsType<TitleBar>(window.TitleBar);
        titleBar.Height = 32;

        var maximizeButton = GetButtonByKind(titleBar, TitleBarButtonKind.Maximize);
        var closeButton = GetButtonByKind(titleBar, TitleBarButtonKind.Close);
        Assert.NotNull(maximizeButton);
        Assert.NotNull(closeButton);
        maximizeButton!.Height = 96;
        closeButton!.Height = 32;

        LayoutWindow(window, 300, 180);

        // Below caption height (32), but inside maximize button height (96).
        Assert.Equal(TitleBarButtonKind.Maximize, HitTestTitleBarButton(window, new Point(210, 64)).Kind);

        // Same Y is outside close button (height 32), so it should not hit close.
        Assert.Null(TryHitTestTitleBarButton(window, new Point(260, 64)));
    }

    [Fact]
    public void NCHitTest_MaxButton_FullHeight_ReturnsHTMAXBUTTON()
    {
        var window = new Window
        {
            Width = 300,
            Height = 180
        };

        var titleBar = Assert.IsType<TitleBar>(window.TitleBar);
        titleBar.Height = 32;

        var maximizeButton = GetButtonByKind(titleBar, TitleBarButtonKind.Maximize);
        Assert.NotNull(maximizeButton);
        maximizeButton!.Height = 96;

        LayoutWindow(window, 300, 180);

        const int HTMAXBUTTON = 9;

        // The maximize button must return HTMAXBUTTON across its full
        // height so the Win11 Snap Layouts flyout can appear anywhere on
        // it. DWM/Shell does not filter HTMAXBUTTON by y coordinate —
        // the flyout relies solely on the WM_NCHITTEST return value.
        Assert.Equal(HTMAXBUTTON, window.ComputeNcHitTestFromClientDip(210, 16, isMaximized: false));
        Assert.Equal(HTMAXBUTTON, window.ComputeNcHitTestFromClientDip(210, 64, isMaximized: false));
    }

    [Fact]
    public void NCHitTest_TallButtons_AllReturnCaptionButtonCodes()
    {
        // Snap Layouts flyout relies only on the WM_NCHITTEST return value —
        // there is no y-coordinate filter. All three caption buttons must
        // therefore keep their HT* code for their full visual height, at any
        // title bar height. Min/Max/Close are symmetric in this regard.
        var window = new Window
        {
            Width = 300,
            Height = 180
        };

        var titleBar = Assert.IsType<TitleBar>(window.TitleBar);
        titleBar.Height = 32;

        var minimizeButton = GetButtonByKind(titleBar, TitleBarButtonKind.Minimize);
        var maximizeButton = GetButtonByKind(titleBar, TitleBarButtonKind.Maximize);
        var closeButton = GetButtonByKind(titleBar, TitleBarButtonKind.Close);
        Assert.NotNull(minimizeButton);
        Assert.NotNull(maximizeButton);
        Assert.NotNull(closeButton);
        minimizeButton!.Height = 96;
        maximizeButton!.Height = 96;
        closeButton!.Height = 96;

        LayoutWindow(window, 300, 180);

        const int HTMINBUTTON = 8;
        const int HTMAXBUTTON = 9;
        const int HTCLOSE = 20;

        const double minX = 180;
        const double maxX = 220;
        const double closeX = 270;

        // Every button stays HT* at every y inside its visual rect.
        Assert.Equal(HTMINBUTTON, window.ComputeNcHitTestFromClientDip(minX, 8, isMaximized: false));
        Assert.Equal(HTMAXBUTTON, window.ComputeNcHitTestFromClientDip(maxX, 8, isMaximized: false));
        Assert.Equal(HTCLOSE, window.ComputeNcHitTestFromClientDip(closeX, 8, isMaximized: false));

        Assert.Equal(HTMINBUTTON, window.ComputeNcHitTestFromClientDip(minX, 64, isMaximized: false));
        Assert.Equal(HTMAXBUTTON, window.ComputeNcHitTestFromClientDip(maxX, 64, isMaximized: false));
        Assert.Equal(HTCLOSE, window.ComputeNcHitTestFromClientDip(closeX, 64, isMaximized: false));
    }

    [Fact]
    public void NCCalcSize_CustomTitleBar_DoesNotFlattenNcSemantics()
    {
        var originalRect = (left: 0, top: 0, right: 1200, bottom: 930);
        var defClientRect = (left: 0, top: 30, right: 1200, bottom: 900);

        // Normal window: keep DefWindowProc side/bottom insets, but remove caption inset on top.
        var normal = Window.ComputeCustomNcCalcSizeRect(originalRect, defClientRect, isMaximized: false, workAreaRect: null);
        Assert.Equal((defClientRect.left, originalRect.top, defClientRect.right, defClientRect.bottom), normal);

        // Maximized window: switch to monitor work area only.
        var workArea = (left: 8, top: 8, right: 1912, bottom: 1072);
        var maximized = Window.ComputeCustomNcCalcSizeRect(originalRect, defClientRect, isMaximized: true, workAreaRect: workArea);
        Assert.Equal(workArea, maximized);
    }

    [Fact]
    public void SnapProxy_MaxButton_FullHeight_BuildsProxyInsideDwmMaxRect()
    {
        var customMaxRect = (left: 1200, top: 200, right: 1246, bottom: 520);
        var dwmMaxRect = (left: 1047, top: 0, right: 1093, bottom: 30);

        Assert.True(Window.TryBuildMaxButtonProxyScreenPoint((1230, 210), customMaxRect, dwmMaxRect, out var proxyTop));
        Assert.True(Window.TryBuildMaxButtonProxyScreenPoint((1230, 510), customMaxRect, dwmMaxRect, out var proxyBottom));

        Assert.InRange(proxyTop.x, dwmMaxRect.left, dwmMaxRect.right - 1);
        Assert.InRange(proxyTop.y, dwmMaxRect.top, dwmMaxRect.bottom - 1);
        Assert.InRange(proxyBottom.x, dwmMaxRect.left, dwmMaxRect.right - 1);
        Assert.InRange(proxyBottom.y, dwmMaxRect.top, dwmMaxRect.bottom - 1);
    }

    [Fact]
    public void SnapProxy_MaxButton_ClampsOutOfRangeCoordinates()
    {
        var customMaxRect = (left: 1200, top: 200, right: 1246, bottom: 520);
        var dwmMaxRect = (left: 1047, top: 0, right: 1093, bottom: 30);

        Assert.True(Window.TryBuildMaxButtonProxyScreenPoint((1000, 300), customMaxRect, dwmMaxRect, out var proxyLeft));
        Assert.True(Window.TryBuildMaxButtonProxyScreenPoint((2000, 300), customMaxRect, dwmMaxRect, out var proxyRight));

        Assert.Equal(dwmMaxRect.left + 1, proxyLeft.x);
        Assert.Equal(dwmMaxRect.right - 2, proxyRight.x);
    }

    [Fact]
    public void SnapProxy_MaxButton_UsesCenterYMapping()
    {
        var customMaxRect = (left: 1200, top: 200, right: 1246, bottom: 520);
        var dwmMaxRect = (left: 1047, top: 0, right: 1093, bottom: 30);

        Assert.True(Window.TryBuildMaxButtonProxyScreenPoint((1220, 210), customMaxRect, dwmMaxRect, out var proxyTop));
        Assert.True(Window.TryBuildMaxButtonProxyScreenPoint((1220, 500), customMaxRect, dwmMaxRect, out var proxyBottom));

        int expectedCenterY = (dwmMaxRect.top + 1) + (((dwmMaxRect.bottom - 2) - (dwmMaxRect.top + 1)) / 2);
        Assert.Equal(expectedCenterY, proxyTop.y);
        Assert.Equal(expectedCenterY, proxyBottom.y);
    }

    private static void LayoutWindow(Window window, double width, double height)
    {
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
    }

    private static void SetButtonWidth(TitleBar titleBar, TitleBarButtonKind kind, double width)
    {
        var button = GetButtonByKind(titleBar, kind);
        Assert.NotNull(button);
        button!.Width = width;
    }

    private static TitleBarButton? GetButtonByKind(TitleBar titleBar, TitleBarButtonKind kind)
    {
        return titleBar.GetButtonByKind(kind);
    }

    private static TitleBarButton HitTestTitleBarButton(Window window, Point point)
    {
        var method = typeof(Window).GetMethod("GetTitleBarButtonAtPoint", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var hit = method!.Invoke(window, new object[] { point, window.Width }) as TitleBarButton;
        return Assert.IsType<TitleBarButton>(hit);
    }

    private static TitleBarButton? TryHitTestTitleBarButton(Window window, Point point)
    {
        var method = typeof(Window).GetMethod("GetTitleBarButtonAtPoint", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return method!.Invoke(window, new object[] { point, window.Width }) as TitleBarButton;
    }

    private static void SetCommandHost(TitleBar titleBar, string fieldName, Rect bounds)
    {
        var field = typeof(TitleBar).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var host = new Border
        {
            Visibility = Visibility.Visible
        };
        host.SetVisualBounds(bounds);

        field!.SetValue(titleBar, host);
    }

    private static Point GetRectCenter(Rect bounds)
    {
        return new Point(
            bounds.X + (bounds.Width / 2.0),
            bounds.Y + (bounds.Height / 2.0));
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        field!.SetValue(instance, value);
    }
}
