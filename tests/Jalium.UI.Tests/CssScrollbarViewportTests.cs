using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssScrollbarViewportTests : IDisposable
{
    private readonly List<Window> _windows = [];
    private bool _resetApplicationOnDispose;

    [Fact]
    public void WindowRootStylesItsDirectScrollViewerViewport()
    {
        var viewer = CreateViewer(300, 240);
        var window = CreateWindow(viewer);
        Css.SetStyleSheet(window,
            ":root { scrollbar-width:thin; scrollbar-gutter:stable both-edges; " +
            "scrollbar-color:red blue }");

        CssBorderRadiusTests.Layout(window, 200, 140);

        Assert.Equal(180, viewer.ViewportWidth);
        var vertical = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
        var horizontal = Assert.IsType<ScrollBar>(viewer.GetVisualChild(2));
        Assert.Equal(10, vertical.RenderSize.Width);
        Assert.Equal(10, horizontal.RenderSize.Height);
        Assert.Equal(Colors.Blue.ToArgb(), ColorOf(vertical.Background));
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(vertical.Track.Thumb!.Background));
        Assert.Equal(Colors.Blue.ToArgb(), ColorOf(horizontal.Background));
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(horizontal.Track.Thumb!.Background));
    }

    [Fact]
    public void ExplicitRootAutoOverridesViewerDeclarations_AndRemovalRestoresThem()
    {
        var viewer = CreateViewer(300, 240);
        Css.SetStyle(viewer,
            "scrollbar-width:thin; scrollbar-gutter:stable both-edges; " +
            "scrollbar-color:green yellow");
        var window = CreateWindow(viewer);
        CssBorderRadiusTests.Layout(window, 200, 140);
        Assert.Equal(180, viewer.ViewportWidth);

        // The root's explicit initial values target the viewport even though
        // its computed values are the same as an unstyled Window's defaults.
        Css.SetStyle(window,
            "scrollbar-width:auto; scrollbar-gutter:auto; scrollbar-color:auto");
        CssBorderRadiusTests.Layout(window, 200, 140);
        Assert.Equal(188, viewer.ViewportWidth);
        var vertical = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
        Assert.Equal(12, vertical.RenderSize.Width);
        if (vertical.Background is SolidColorBrush themeTrack)
            Assert.NotEqual(Colors.Yellow.ToArgb(), themeTrack.Color.ToArgb());

        Css.SetStyle(window,
            "scrollbar-width:none; scrollbar-gutter:stable; scrollbar-color:red blue");
        CssBorderRadiusTests.Layout(window, 200, 140);
        Assert.Equal(200, viewer.ViewportWidth);
        Assert.Equal(Visibility.Collapsed, vertical.Visibility);
        Assert.True(viewer.CanScrollVertically);

        Css.SetStyle(window, string.Empty);
        CssBorderRadiusTests.Layout(window, 200, 140);
        Assert.Equal(180, viewer.ViewportWidth);
        Assert.Equal(Visibility.Visible, vertical.Visibility);
        Assert.Equal(10, vertical.RenderSize.Width);
        Assert.Equal(Colors.Yellow.ToArgb(), ColorOf(vertical.Background));
        Assert.Equal(Colors.Green.ToArgb(), ColorOf(vertical.Track.Thumb!.Background));
    }

    [Fact]
    public void RootCurrentColorUpdatesViewportBarsDespiteViewerColorDeclaration()
    {
        var viewer = CreateViewer(300, 240);
        Css.SetStyle(viewer, "scrollbar-color:green yellow");
        var window = CreateWindow(viewer);
        Css.SetStyle(window, "color:red; scrollbar-color:currentColor blue");

        CssBorderRadiusTests.Layout(window, 200, 140);
        var bar = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(bar.Track.Thumb!.Background));
        Assert.Equal(Colors.Blue.ToArgb(), ColorOf(bar.Background));

        Css.SetStyle(window, "color:purple; scrollbar-color:currentColor blue");
        CssBorderRadiusTests.Layout(window, 200, 140);
        Assert.Equal(Colors.Purple.ToArgb(), ColorOf(bar.Track.Thumb!.Background));
        Assert.Equal(Colors.Blue.ToArgb(), ColorOf(bar.Background));
    }

    [Fact]
    public void DirectViewerNativeLocalValuesRetainPriorityOverRootCss()
    {
        var viewer = CreateViewer(300, 240);
        viewer.CssScrollBarWidth = CssScrollBarWidthMode.Thin;
        viewer.CssScrollBarGutter = CssScrollBarGutterMode.StableBothEdges;
        viewer.CssScrollBarColors = new CssScrollBarColors(Colors.Green, Colors.Yellow);
        var window = CreateWindow(viewer);
        Css.SetStyle(window,
            "scrollbar-width:none; scrollbar-gutter:auto; scrollbar-color:red blue");

        CssBorderRadiusTests.Layout(window, 200, 140);

        Assert.Equal(180, viewer.ViewportWidth);
        var bar = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
        Assert.Equal(Visibility.Visible, bar.Visibility);
        Assert.Equal(10, bar.RenderSize.Width);
        Assert.Equal(Colors.Yellow.ToArgb(), ColorOf(bar.Background));
        Assert.Equal(Colors.Green.ToArgb(), ColorOf(bar.Track.Thumb!.Background));
    }

    [Fact]
    public void SwappingDirectViewportReleasesDetachedViewer()
    {
        var first = CreateViewer(80, 40);
        first.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var second = CreateViewer(80, 40);
        second.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var window = CreateWindow(first);
        Css.SetStyle(window, "scrollbar-width:thin; scrollbar-gutter:stable");
        CssBorderRadiusTests.Layout(window, 200, 140);
        Assert.Equal(190, first.ViewportWidth);

        window.Content = second;
        CssBorderRadiusTests.Layout(window, 200, 140);
        Assert.Equal(190, second.ViewportWidth);

        CssBorderRadiusTests.Layout(first, 200, 140);
        Assert.Equal(200, first.ViewportWidth);
    }

    [Fact]
    public void NestedViewerDoesNotTakeNonInheritedViewportDeclarations()
    {
        var viewer = CreateViewer(300, 240);
        var host = new Grid();
        host.Children.Add(viewer);
        var window = CreateWindow(host);
        Css.SetStyle(window,
            "scrollbar-width:none; scrollbar-gutter:stable both-edges");

        CssBorderRadiusTests.Layout(window, 200, 140);

        Assert.Equal(188, viewer.ViewportWidth);
        var vertical = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
        Assert.Equal(Visibility.Visible, vertical.Visibility);
        Assert.Equal(12, vertical.RenderSize.Width);
    }

    [Fact]
    public void RootStylesDirectListBoxTemplateViewer_AndDetachRestoresOwnerStyle()
    {
        var template = new ControlTemplate(typeof(ListBox));
        template.SetVisualTree(() => CreateViewer(300, 240));
        var owner = new ListBox { Template = template };
        Css.SetStyle(owner,
            "scrollbar-width:thin; scrollbar-gutter:stable both-edges; " +
            "scrollbar-color:green yellow");
        var window = CreateWindow(owner);
        CssBorderRadiusTests.Layout(window, 200, 140);
        var viewer = Assert.IsType<ScrollViewer>(owner.GetVisualChild(0));
        Assert.Same(owner, viewer.TemplatedParent);
        Assert.Equal(180, viewer.ViewportWidth);

        Css.SetStyle(window,
            "scrollbar-width:auto; scrollbar-gutter:auto; scrollbar-color:red blue");
        CssBorderRadiusTests.Layout(window, 200, 140);
        Assert.Equal(188, viewer.ViewportWidth);
        var bar = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
        Assert.Equal(12, bar.RenderSize.Width);
        Assert.Equal(Colors.Blue.ToArgb(), ColorOf(bar.Background));
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(bar.Track.Thumb!.Background));

        Css.SetStyle(window,
            "scrollbar-width:none; scrollbar-gutter:auto; scrollbar-color:red blue");
        CssBorderRadiusTests.Layout(window, 200, 140);
        Assert.Equal(200, viewer.ViewportWidth);
        Assert.Equal(Visibility.Collapsed,
            Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).Visibility);

        Css.SetStyle(window,
            "scrollbar-width:auto; scrollbar-gutter:auto; scrollbar-color:red blue");
        CssBorderRadiusTests.Layout(window, 200, 140);
        Assert.Equal(188, viewer.ViewportWidth);
        Assert.Equal(12, bar.RenderSize.Width);
        Assert.Equal(Colors.Blue.ToArgb(), ColorOf(bar.Background));
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(bar.Track.Thumb!.Background));

        window.Content = new Border();
        CssBorderRadiusTests.Layout(owner, 200, 140);
        Assert.Equal(180, viewer.ViewportWidth);
        Assert.Equal(10, bar.RenderSize.Width);
        Assert.Equal(Colors.Yellow.ToArgb(), ColorOf(bar.Background));
        Assert.Equal(Colors.Green.ToArgb(), ColorOf(bar.Track.Thumb!.Background));
    }

    [Fact]
    public void NestedListBoxTemplateViewerKeepsItsOwnScrollbarStyle()
    {
        var template = new ControlTemplate(typeof(ListBox));
        template.SetVisualTree(() => CreateViewer(300, 240));
        var owner = new ListBox { Template = template };
        var host = new Grid();
        host.Children.Add(owner);
        var window = CreateWindow(host);
        Css.SetStyle(window,
            "scrollbar-width:none; scrollbar-gutter:stable both-edges");

        CssBorderRadiusTests.Layout(window, 200, 140);

        var viewer = Assert.IsType<ScrollViewer>(owner.GetVisualChild(0));
        Assert.Equal(188, viewer.ViewportWidth);
        Assert.Equal(Visibility.Visible,
            Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).Visibility);
    }

    [Fact]
    public void DefaultListBoxTemplateUsesRootViewportScrollbarDeclarations()
    {
        ResetApplicationState();
        _resetApplicationOnDispose = true;
        _ = new Application();
        var owner = new ListBox();
        for (var index = 0; index < 30; index++)
            owner.Items.Add($"Row {index}");
        var window = CreateWindow(owner);
        Css.SetStyle(window, "scrollbar-width:none");

        CssBorderRadiusTests.Layout(window, 200, 140);
        var viewer = Assert.IsType<ScrollViewer>(FindDescendant<ScrollViewer>(owner));
        var bar = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
        Assert.Equal(Visibility.Collapsed, bar.Visibility);

        Css.SetStyle(window, "scrollbar-width:thin; scrollbar-color:red blue");
        CssBorderRadiusTests.Layout(window, 200, 140);
        Assert.Equal(Visibility.Visible, bar.Visibility);
        Assert.Equal(10, bar.RenderSize.Width);
        Assert.Equal(Colors.Blue.ToArgb(), ColorOf(bar.Background));
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(bar.Track.Thumb!.Background));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DataGridRootStylesDataViewportWithoutStylingItsHeaderViewport(bool treeGrid)
    {
        var template = new ControlTemplate(treeGrid ? typeof(TreeDataGrid) : typeof(DataGrid));
        template.SetVisualTree(() =>
        {
            var root = new Grid();
            var header = CreateViewer(300, 240);
            header.Name = "PART_ColumnHeadersScrollViewer";
            header.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Css.SetStyle(header, "scrollbar-color:green yellow");
            var data = CreateViewer(300, 240);
            data.Name = "PART_DataScrollViewer";
            Css.SetStyle(data, "scrollbar-width:thin; scrollbar-color:green yellow");
            root.Children.Add(header);
            root.Children.Add(data);
            return root;
        });
        Control owner = treeGrid
            ? new TreeDataGrid { Template = template }
            : new DataGrid { Template = template };
        var window = CreateWindow(owner);
        CssBorderRadiusTests.Layout(window, 200, 140);
        var headerViewer = Assert.IsType<ScrollViewer>(
            owner.FindName("PART_ColumnHeadersScrollViewer"));
        var dataViewer = Assert.IsType<ScrollViewer>(owner.FindName("PART_DataScrollViewer"));
        Assert.Same(owner, dataViewer.TemplatedParent);

        Css.SetStyle(window,
            "scrollbar-width:none; scrollbar-gutter:stable both-edges; " +
            "scrollbar-color:red blue");
        CssBorderRadiusTests.Layout(window, 200, 140);
        var headerBar = Assert.IsType<ScrollBar>(headerViewer.GetVisualChild(1));
        var dataBar = Assert.IsType<ScrollBar>(dataViewer.GetVisualChild(1));
        Assert.Equal(Visibility.Visible, headerBar.Visibility);
        Assert.Equal(12, headerBar.RenderSize.Width);
        Assert.Equal(Colors.Yellow.ToArgb(), ColorOf(headerBar.Background));
        Assert.Equal(Visibility.Collapsed, dataBar.Visibility);
        Assert.Equal(dataViewer.RenderSize.Width, dataViewer.ViewportWidth);

        Css.SetStyle(window,
            "scrollbar-width:auto; scrollbar-gutter:stable both-edges; " +
            "scrollbar-color:red blue");
        CssBorderRadiusTests.Layout(window, 200, 140);
        Assert.Equal(Visibility.Visible, dataBar.Visibility);
        Assert.Equal(12, dataBar.RenderSize.Width);
        Assert.Equal(24, dataViewer.RenderSize.Width - dataViewer.ViewportWidth);
        Assert.Equal(Colors.Blue.ToArgb(), ColorOf(dataBar.Background));
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(dataBar.Track.Thumb!.Background));
        Assert.Equal(Colors.Yellow.ToArgb(), ColorOf(headerBar.Background));

        window.Content = new Border();
        CssBorderRadiusTests.Layout(owner, 200, 140);
        Assert.Equal(10, dataBar.RenderSize.Width);
        Assert.Equal(Colors.Yellow.ToArgb(), ColorOf(dataBar.Background));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultGridTemplatesSelectTheDataViewport(bool treeGrid)
    {
        ResetApplicationState();
        _resetApplicationOnDispose = true;
        _ = new Application();
        Control owner = treeGrid ? new TreeDataGrid() : new DataGrid();
        var window = CreateWindow(owner);
        Css.SetStyle(window, "scrollbar-width:thin; scrollbar-gutter:stable");

        CssBorderRadiusTests.Layout(window, 200, 140);

        var data = Assert.IsType<ScrollViewer>(owner.FindName("PART_DataScrollViewer"));
        var header = Assert.IsType<ScrollViewer>(
            owner.FindName("PART_ColumnHeadersScrollViewer"));
        Assert.Same(owner, data.TemplatedParent);
        Assert.Equal(CssScrollBarWidthMode.Thin, data.CssScrollBarWidth);
        Assert.Equal(CssScrollBarGutterMode.Stable, data.CssScrollBarGutter);
        Assert.Equal(CssScrollBarWidthMode.Auto, header.CssScrollBarWidth);
        Assert.Equal(CssScrollBarGutterMode.Auto, header.CssScrollBarGutter);
        Assert.Equal(10, data.RenderSize.Width - data.ViewportWidth);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NamedScrollControlViewportUsesRootDeclarations_WithoutStylingOtherTemplateViewers(int kind)
    {
        var owner = CreateNamedScrollControl(kind);
        var template = new ControlTemplate(owner.GetType());
        template.SetVisualTree(() =>
        {
            var root = new Grid();
            var secondary = CreateViewer(300, 240);
            secondary.Name = "SecondaryScrollViewer";
            Css.SetStyle(secondary,
                "scrollbar-width:thin; scrollbar-gutter:stable; scrollbar-color:green yellow");
            var viewport = CreateViewer(300, 240);
            viewport.Name = "PART_ScrollViewer";
            root.Children.Add(secondary);
            root.Children.Add(viewport);
            return root;
        });
        owner.Template = template;
        Css.SetStyle(owner,
            "scrollbar-width:thin; scrollbar-gutter:stable; scrollbar-color:green yellow");
        var window = CreateWindow(owner);
        Css.SetStyle(window,
            "scrollbar-width:none; scrollbar-gutter:stable both-edges; scrollbar-color:red blue");

        CssBorderRadiusTests.Layout(window, 200, 140);
        var viewport = Assert.IsType<ScrollViewer>(owner.FindName("PART_ScrollViewer"));
        var secondary = Assert.IsType<ScrollViewer>(owner.FindName("SecondaryScrollViewer"));
        Assert.Same(owner, viewport.TemplatedParent);
        Assert.Equal(CssScrollBarWidthMode.None, viewport.CssScrollBarWidth);
        Assert.Equal(CssScrollBarGutterMode.StableBothEdges, viewport.CssScrollBarGutter);
        Assert.Equal(CssScrollBarWidthMode.Thin, secondary.CssScrollBarWidth);
        Assert.Equal(CssScrollBarGutterMode.Stable, secondary.CssScrollBarGutter);
        var viewportBar = Assert.IsType<ScrollBar>(viewport.GetVisualChild(1));
        var secondaryBar = Assert.IsType<ScrollBar>(secondary.GetVisualChild(1));
        Assert.Equal(Visibility.Collapsed, viewportBar.Visibility);
        Assert.Equal(Visibility.Visible, secondaryBar.Visibility);
        Assert.Equal(Colors.Yellow.ToArgb(), ColorOf(secondaryBar.Background));
        Assert.Equal(Colors.Green.ToArgb(), ColorOf(secondaryBar.Track.Thumb!.Background));

        Css.SetStyle(window,
            "scrollbar-width:auto; scrollbar-gutter:stable both-edges; scrollbar-color:red blue");
        CssBorderRadiusTests.Layout(window, 200, 140);
        Assert.Equal(Visibility.Visible, viewportBar.Visibility);
        Assert.Equal(Colors.Blue.ToArgb(), ColorOf(viewportBar.Background));
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(viewportBar.Track.Thumb!.Background));
        Assert.Equal(CssScrollBarWidthMode.Thin, secondary.CssScrollBarWidth);

        window.Content = new Border();
        CssBorderRadiusTests.Layout(owner, 200, 140);
        Assert.Equal(CssScrollBarWidthMode.Thin, viewport.CssScrollBarWidth);
        Assert.Equal(CssScrollBarGutterMode.Stable, viewport.CssScrollBarGutter);
        Assert.Equal(Colors.Yellow.ToArgb(), ColorOf(viewportBar.Background));
        Assert.Equal(Colors.Green.ToArgb(), ColorOf(viewportBar.Track.Thumb!.Background));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DefaultNamedScrollControlTemplateSelectsItsViewport(int kind)
    {
        ResetApplicationState();
        _resetApplicationOnDispose = true;
        _ = new Application();
        var owner = CreateNamedScrollControl(kind);
        var window = CreateWindow(owner);
        Css.SetStyle(window,
            "scrollbar-width:thin; scrollbar-gutter:stable; scrollbar-color:red blue");

        CssBorderRadiusTests.Layout(window, 200, 140);
        var viewport = Assert.IsType<ScrollViewer>(owner.FindName("PART_ScrollViewer"));
        Assert.Same(owner, viewport.TemplatedParent);
        Assert.Equal(CssScrollBarWidthMode.Thin, viewport.CssScrollBarWidth);
        Assert.Equal(CssScrollBarGutterMode.Stable, viewport.CssScrollBarGutter);
        var bar = Assert.IsType<ScrollBar>(viewport.GetVisualChild(1));
        Assert.Equal(Colors.Blue.ToArgb(), ColorOf(bar.Background));
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(bar.Track.Thumb!.Background));
    }

    private static Control CreateNamedScrollControl(int kind) => kind switch
    {
        0 => new TreeView(),
        1 => new Markdown(),
        _ => new Terminal(),
    };

    private static ScrollViewer CreateViewer(double contentWidth, double contentHeight) => new()
    {
        Content = new Border { Width = contentWidth, Height = contentHeight },
        HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
        VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        IsScrollBarAutoHideEnabled = false,
        IsOverlayScrollBarEnabled = false,
    };

    private static uint ColorOf(Brush? brush)
        => Assert.IsType<SolidColorBrush>(brush).Color.ToArgb();

    private static T? FindDescendant<T>(Visual root) where T : Visual
    {
        for (var index = 0; index < root.VisualChildrenCount; index++)
        {
            if (root.GetVisualChild(index) is not { } child)
                continue;
            if (child is T match)
                return match;
            if (FindDescendant<T>(child) is { } nested)
                return nested;
        }
        return null;
    }

    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }

    private Window CreateWindow(UIElement content)
    {
        var window = new Window { Content = content, Width = 200, Height = 140 };
        _windows.Add(window);
        return window;
    }

    public void Dispose()
    {
        foreach (var window in _windows)
            window.Close();
        if (_resetApplicationOnDispose)
            ResetApplicationState();
    }
}
