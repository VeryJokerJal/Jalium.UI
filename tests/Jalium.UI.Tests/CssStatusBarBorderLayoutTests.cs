using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssStatusBarBorderLayoutTests
{
    [Fact]
    public void CssBorderAndPadding_ReserveSpaceForItemsHost()
    {
        var statusBar = CreateStatusBar();

        CssBorderRadiusTests.Layout(statusBar, 200, 60);

        var host = Assert.IsType<StatusBarPanel>(statusBar.GetVisualChild(0));
        Assert.Equal(new Vector(14, 12), VisualTreeHelper.GetOffset(host));
        Assert.Equal(new Size(172, 36), host.RenderSize);
    }

    [Fact]
    public void CssRemovalAndNativeBorderThickness_ReflowItemsHost()
    {
        var statusBar = CreateStatusBar();
        CssBorderRadiusTests.Layout(statusBar, 200, 60);
        var host = Assert.IsType<StatusBarPanel>(statusBar.GetVisualChild(0));

        Css.SetStyle(statusBar, "padding:2px");
        CssBorderRadiusTests.Layout(statusBar, 200, 60);
        Assert.Equal(new Vector(2, 2), VisualTreeHelper.GetOffset(host));
        Assert.Equal(new Size(196, 56), host.RenderSize);

        statusBar.BorderThickness = new Thickness(2, 3, 4, 5);
        Css.SetStyle(statusBar, "border:8px solid red; padding:4px 6px");
        CssBorderRadiusTests.Layout(statusBar, 200, 60);
        Assert.Equal(new Vector(8, 7), VisualTreeHelper.GetOffset(host));
        Assert.Equal(new Size(182, 44), host.RenderSize);
    }

    [Fact]
    public void IntrinsicSize_IncludesBorderAndPadding()
    {
        var statusBar = CreateStatusBar();
        statusBar.Measure(new Size(500, 500));
        var decoratedSize = statusBar.DesiredSize;

        Css.SetStyle(statusBar, "");
        statusBar.Measure(new Size(500, 500));

        Assert.Equal(statusBar.DesiredSize.Width + 28, decoratedSize.Width);
        Assert.Equal(statusBar.DesiredSize.Height + 24, decoratedSize.Height);
    }

    private static StatusBar CreateStatusBar()
    {
        // This fixture exercises the explicit bare-control CSS layout path.
        // The themed presenter/frame path is covered by StatusBarTemplateTests.
        var bareStyle = new Style(typeof(StatusBar));
        bareStyle.Setters.Add(new Setter(Control.TemplateProperty, null));
        bareStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        bareStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        bareStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, double.NaN));
        var statusBar = new StatusBar { Style = bareStyle };
        statusBar.Items.Add(new StatusBarItem { Content = "Ready" });
        Css.SetStyle(statusBar, "border:8px solid red; padding:4px 6px");
        return statusBar;
    }
}
