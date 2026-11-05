using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Interop.Win32;

namespace Jalium.UI.Tests;

public class TitleBarGeometryTests
{
    [Theory]
    [InlineData(32, 64)]
    [InlineData(64, 24)]
    public void ChangingTitleBarHeight_UpdatesContentAndCaptionInOneLayout(double initialHeight, double height)
    {
        var content = new Border();
        var window = new Window { Width = 420, Height = 240, TitleBarHeight = initialHeight, Content = content };
        Layout(window);

        window.TitleBarHeight = height;
        Layout(window);

        Assert.Equal(height, window.TitleBar!.ActualHeight);
        Assert.Equal(height, content.VisualBounds.Y);
        Assert.Equal(240 - height, content.VisualBounds.Height);
        Assert.Equal(2, window.ComputeNcHitTestFromClientDip(20, height - 1, isMaximized: false));
        Assert.Equal(1, window.ComputeNcHitTestFromClientDip(20, height + 1, isMaximized: false));
    }

    [Theory]
    [InlineData(32, 64)]
    [InlineData(64, 24)]
    public void ChangingTitleBarControlHeight_UsesCurrentMeasuredSize(double initialHeight, double height)
    {
        var content = new Border();
        var window = new Window { Width = 420, Height = 240, TitleBarHeight = initialHeight, Content = content };
        Layout(window);

        window.TitleBar!.Height = height;
        Layout(window);

        Assert.Equal(height, window.TitleBar.ActualHeight);
        Assert.Equal(height, content.VisualBounds.Y);
        Assert.Equal(240 - height, content.VisualBounds.Height);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(2)]
    public void DwmMargin_TracksCaptionButtonHeightChangesAndVisibility(double dpiScale)
    {
        var window = new Window { Width = 420, Height = 240 };
        SetField(window, "_dpiScale", dpiScale);
        var maximize = window.TitleBar!.GetButtonByKind(TitleBarButtonKind.Maximize)!;
        Layout(window);

        maximize.Height = 95;
        Layout(window);
        Assert.Equal((int)Math.Ceiling(95 * dpiScale), GetDwmTopMargin(window));

        maximize.Height = 48;
        Layout(window);
        Assert.Equal((int)Math.Ceiling(48 * dpiScale), GetDwmTopMargin(window));

        maximize.Visibility = Visibility.Collapsed;
        Layout(window);
        Assert.Equal((int)Math.Ceiling(32 * dpiScale), GetDwmTopMargin(window));

        window.IsShowTitleBar = false;
        Assert.Equal(0, GetDwmTopMargin(window));
    }

    [Fact]
    public void NestedCaptionButton_UsesItsCurrentPositionInWindowCoordinates()
    {
        var window = new Window { Width = 420, Height = 240 };
        var titleBar = window.TitleBar!;
        var template = new ControlTemplate(typeof(TitleBar));
        TitleBarButton? maximize = null;
        template.SetVisualTree(() =>
        {
            maximize = new TitleBarButton { Name = "PART_MaximizeButton", Width = 60, Height = 24 };
            Canvas.SetLeft(maximize, 180);
            Canvas.SetTop(maximize, 36);
            var panel = new Canvas();
            panel.Children.Add(maximize);
            return new Border { Padding = new Thickness(20, 16, 0, 0), Child = panel };
        });
        titleBar.Template = template;
        Layout(window);

        Assert.NotNull(maximize);
        Assert.Equal(new Point(200, 52), maximize.TranslatePoint(Point.Zero, window));
        Assert.Equal(9, window.ComputeNcHitTestFromClientDip(230, 64, isMaximized: false));
        Assert.Equal(76, GetDwmTopMargin(window));
        Assert.Equal(2, window.ComputeNcHitTestFromClientDip(344, 12, isMaximized: false));

        Canvas.SetTop(maximize, 12);
        Layout(window);

        Assert.Equal(new Point(200, 28), maximize.TranslatePoint(Point.Zero, window));
        Assert.Equal(9, window.ComputeNcHitTestFromClientDip(230, 40, isMaximized: false));
        Assert.Equal(1, window.ComputeNcHitTestFromClientDip(230, 64, isMaximized: false));
        Assert.Equal(52, GetDwmTopMargin(window));
    }

    [RequiresWindowsFact]
    public void NativeHitTest_UsesResizedCustomMaximizeButtonAndUpdatesDwmMargin()
    {
        var window = new Window
        {
            Width = 420,
            Height = 560,
            Left = -20000,
            Top = -20000,
            ShowActivated = false,
            ShowInTaskbar = false,
            IsShowMinimizeButton = false,
            IsShowCloseButton = false,
            TitleBarHeight = 72
        };
        var maximize = window.TitleBar!.GetButtonByKind(TitleBarButtonKind.Maximize)!;
        maximize.Width = 420;
        maximize.Height = 72;
        try
        {
            window.Show();
            Assert.Equal(0x00C00000L /* WS_CAPTION */, Win32Methods.GetWindowLong(window.Handle, -16 /* GWL_STYLE */) & 0x00C00000L);
            CheckNativeGeometry(72);

            window.TitleBarHeight = 300;
            maximize.Height = 300;
            Layout(window);
            CheckNativeGeometry(300);

            window.TitleBarHeight = 40;
            maximize.Height = 40;
            Layout(window);
            CheckNativeGeometry(40);
        }
        finally
        {
            window.Close();
        }

        void CheckNativeGeometry(double height)
        {
            double dpiScale = (double)typeof(Window).GetField("_dpiScale", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(window)!;
            Assert.Equal((int)Math.Ceiling(height * dpiScale),
                typeof(Window).GetField("_appliedDwmTopMarginPhysical", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(window));

            CheckHit(12);
            CheckHit(height - 4);

            void CheckHit(double y)
            {
                var point = new POINT { X = (int)((window.Width - 12) * dpiScale), Y = (int)(y * dpiScale) };
                Assert.True(Win32Methods.ClientToScreen(window.Handle, ref point));
                nint coordinates = unchecked((nint)((ushort)point.X | ((int)(ushort)point.Y << 16)));
                Assert.Equal((nint)9, Win32Methods.SendMessage(window.Handle, 0x0084 /* WM_NCHITTEST */, 0, coordinates));
            }
        }
    }

    [RequiresWindowsFact]
    public void CustomCaptionStyle_SurvivesNativeChromeAndResizeModeChanges()
    {
        const long Caption = 0x00C00000;
        const long SystemMenu = 0x00080000;
        const long ThickFrame = 0x00040000;
        const long MaximizeBox = 0x00010000;
        var window = new Window
        {
            Width = 420,
            Height = 560,
            Left = -20000,
            Top = -20000,
            ShowActivated = false,
            ShowInTaskbar = false,
            TitleBarHeight = 300
        };
        try
        {
            window.Show();
            AssertCustomResizeStyle();

            window.TitleBarStyle = WindowTitleBarStyle.Native;
            Assert.Equal(Caption, Style() & Caption);

            window.TitleBarStyle = WindowTitleBarStyle.Custom;
            AssertCustomResizeStyle();

            window.ResizeMode = ResizeMode.CanMinimize;
            Assert.Equal(Caption, Style() & Caption);
            Assert.Equal(0, Style() & (ThickFrame | MaximizeBox));
            Assert.NotEqual(0, Style() & SystemMenu);

            window.ResizeMode = ResizeMode.CanResize;
            AssertCustomResizeStyle();
            Assert.NotEqual(nint.Zero, Win32Methods.GetSystemMenu(window.Handle, false));
        }
        finally
        {
            window.Close();
        }

        long Style() => Win32Methods.GetWindowLong(window.Handle, -16);
        void AssertCustomResizeStyle()
        {
            Assert.Equal(Caption, Style() & Caption);
            Assert.Equal(SystemMenu | ThickFrame | MaximizeBox, Style() & (SystemMenu | ThickFrame | MaximizeBox));
        }
    }

    private static void Layout(Window window)
    {
        ((ILayoutManagerHost)window).LayoutManager.UpdateLayout(window, new Size(window.Width, window.Height));
    }

    private static int GetDwmTopMargin(Window window)
        => (int)typeof(Window).GetMethod("GetCustomTitleBarTopMarginPhysical", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, null)!;

    private static void SetField(Window window, string name, object value)
        => typeof(Window).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, value);
}
