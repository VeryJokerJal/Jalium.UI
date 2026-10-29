using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssScrollbarWidthTests
{
    [Fact]
    public void Thin_ReducesBothClassicBarStripsAndExpandsTheViewport()
    {
        var viewer = CreateViewer();
        CssBorderRadiusTests.Layout(viewer, 200, 140);
        var originalViewport = new Size(viewer.ViewportWidth, viewer.ViewportHeight);
        var vertical = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
        var horizontal = Assert.IsType<ScrollBar>(viewer.GetVisualChild(2));
        Assert.Equal(new Size(12, 128), vertical.RenderSize);
        Assert.Equal(new Size(188, 12), horizontal.RenderSize);

        Css.SetStyle(viewer, "scrollbar-width: thin");
        CssBorderRadiusTests.Layout(viewer, 200, 140);

        Assert.Equal(new Size(188, 128), originalViewport);
        Assert.Equal(new Size(190, 130), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(new Size(10, 130), vertical.RenderSize);
        Assert.Equal(new Size(190, 10), horizontal.RenderSize);
        Assert.Equal(Visibility.Visible, vertical.Visibility);
        Assert.Equal(Visibility.Visible, horizontal.Visibility);

        Css.SetStyle(viewer, "scrollbar-width: auto");
        CssBorderRadiusTests.Layout(viewer, 200, 140);
        Assert.Equal(new Size(12, 128), vertical.RenderSize);
        Assert.Equal(new Size(188, 12), horizontal.RenderSize);
    }

    [Fact]
    public void None_HidesBarsButKeepsBothAxesScrollable_AndRemovalRestoresThem()
    {
        var viewer = CreateViewer();
        Css.SetStyle(viewer, "scrollbar-width: none");
        CssBorderRadiusTests.Layout(viewer, 200, 140);

        Assert.True(viewer.CanScrollVertically);
        Assert.True(viewer.CanScrollHorizontally);
        Assert.Equal(Visibility.Collapsed, viewer.ComputedVerticalScrollBarVisibility);
        Assert.Equal(Visibility.Collapsed, viewer.ComputedHorizontalScrollBarVisibility);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).Visibility);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).Visibility);
        viewer.ScrollToVerticalOffset(30);
        viewer.ScrollToHorizontalOffset(20);
        Assert.Equal(30, viewer.VerticalOffset);
        Assert.Equal(20, viewer.HorizontalOffset);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).Visibility);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).Visibility);

        Css.SetStyle(viewer, string.Empty);
        CssBorderRadiusTests.Layout(viewer, 200, 140);
        Assert.Equal(Visibility.Visible, viewer.ComputedVerticalScrollBarVisibility);
        Assert.Equal(Visibility.Visible, viewer.ComputedHorizontalScrollBarVisibility);
        Assert.Equal(Visibility.Visible, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).Visibility);
        Assert.Equal(Visibility.Visible, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).Visibility);
        Assert.Equal(30, viewer.VerticalOffset);
        Assert.Equal(20, viewer.HorizontalOffset);
    }

    [Fact]
    public void InvalidValues_DoNotReplaceEarlierValidDeclaration()
    {
        var viewer = CreateViewer();
        Css.SetStyle(viewer,
            "scrollbar-width: thin; scrollbar-width: 4px; scrollbar-width: none thin");
        CssBorderRadiusTests.Layout(viewer, 200, 140);

        Assert.Equal(10, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).RenderSize.Width);
        Assert.Equal(Visibility.Visible, viewer.ComputedVerticalScrollBarVisibility);
    }

    [Fact]
    public void OverlayBars_KeepTheirPlatformStripForThin_AndHideForNone()
    {
        var viewer = CreateViewer();
        viewer.IsOverlayScrollBarEnabled = true;
        Css.SetStyle(viewer, "scrollbar-width: thin");
        CssBorderRadiusTests.Layout(viewer, 200, 140);
        Assert.Equal(40, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).RenderSize.Width);
        Assert.Equal(40, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).RenderSize.Height);

        Css.SetStyle(viewer, "scrollbar-width: none");
        CssBorderRadiusTests.Layout(viewer, 200, 140);
        Assert.Equal(Visibility.Collapsed, viewer.ComputedVerticalScrollBarVisibility);
        Assert.Equal(Visibility.Collapsed, viewer.ComputedHorizontalScrollBarVisibility);
        Assert.True(viewer.CanScrollVertically);
        Assert.True(viewer.CanScrollHorizontally);
    }

    [Fact]
    public void ThemedBars_UseThinStripDespiteThemeMinimums()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var viewer = CreateViewer();
            Css.SetStyle(viewer, "scrollbar-width: thin");
            CssBorderRadiusTests.Layout(viewer, 200, 140);

            Assert.Equal(10, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).RenderSize.Width);
            Assert.Equal(10, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).RenderSize.Height);

            Css.SetStyle(viewer, string.Empty);
            CssBorderRadiusTests.Layout(viewer, 200, 140);
            Assert.Equal(12, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).RenderSize.Width);
            Assert.Equal(12, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).RenderSize.Height);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TemplateHostedViewerUsesOwnersWidth_AndOwnDeclarationWins()
    {
        var template = new ControlTemplate(typeof(ListBox));
        template.SetVisualTree(() => CreateViewer());
        var owner = new ListBox { Template = template };

        Css.SetStyle(owner, "scrollbar-width: thin");
        CssBorderRadiusTests.Layout(owner, 200, 140);
        var viewer = Assert.IsType<ScrollViewer>(owner.GetVisualChild(0));
        Assert.Same(owner, viewer.TemplatedParent);
        Assert.Equal(10, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).RenderSize.Width);

        Css.SetStyle(viewer, "scrollbar-width: auto");
        CssBorderRadiusTests.Layout(owner, 200, 140);
        Assert.Equal(12, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).RenderSize.Width);

        Css.SetStyle(viewer, "scrollbar-width: unset");
        CssBorderRadiusTests.Layout(owner, 200, 140);
        Assert.Equal(12, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).RenderSize.Width);

        Css.SetStyle(viewer, "scrollbar-width: inherit");
        CssBorderRadiusTests.Layout(owner, 200, 140);
        Assert.Equal(10, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).RenderSize.Width);

        Css.SetStyle(viewer, string.Empty);
        Css.SetStyle(owner, "scrollbar-width: none");
        CssBorderRadiusTests.Layout(owner, 200, 140);
        Assert.True(viewer.CanScrollVertically);
        Assert.Equal(Visibility.Collapsed, viewer.ComputedVerticalScrollBarVisibility);

        Css.SetStyle(owner, string.Empty);
        CssBorderRadiusTests.Layout(owner, 200, 140);
        Assert.Equal(Visibility.Visible, viewer.ComputedVerticalScrollBarVisibility);
        Assert.Equal(12, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).RenderSize.Width);
    }

    [Fact]
    public void OrdinaryChildViewerDoesNotInheritWidth()
    {
        var parent = new Grid();
        var viewer = CreateViewer();
        parent.Children.Add(viewer);
        Css.SetStyle(parent, "scrollbar-width: thin");
        CssBorderRadiusTests.Layout(parent, 200, 140);

        Assert.Equal(12, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).RenderSize.Width);
        Assert.Equal(12, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).RenderSize.Height);
    }

    [Fact]
    public void DefaultListBoxTemplateUsesTheOwnersWidth()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var owner = new ListBox();
            for (var index = 0; index < 30; index++)
                owner.Items.Add($"Row {index}");
            var host = new Grid();
            host.Children.Add(owner);
            Css.SetStyle(owner, "scrollbar-width: thin");
            CssBorderRadiusTests.Layout(host, 200, 140);

            var viewer = Assert.IsType<ScrollViewer>(FindDescendant<ScrollViewer>(owner));
            Assert.Same(owner, viewer.TemplatedParent);
            Assert.True(viewer.CanScrollVertically);
            var bar = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
            Assert.Equal(10, bar.MinWidth);
            Assert.Equal(10, bar.RenderSize.Width);

            Css.SetStyle(owner, "scrollbar-width: auto");
            CssBorderRadiusTests.Layout(host, 200, 140);
            Assert.Equal(12, bar.MinWidth);
            Assert.Equal(12, bar.RenderSize.Width);

            Css.SetStyle(owner, "scrollbar-width: none");
            CssBorderRadiusTests.Layout(host, 200, 140);
            Assert.True(viewer.CanScrollVertically);
            Assert.Equal(Visibility.Collapsed, viewer.ComputedVerticalScrollBarVisibility);

            Css.SetStyle(owner, "scrollbar-width: thin");
            CssBorderRadiusTests.Layout(host, 200, 140);
            Assert.Equal(10, bar.MinWidth);
            Assert.Equal(10, bar.RenderSize.Width);
        }
        finally
        {
            ResetApplicationState();
        }
    }

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

    private static ScrollViewer CreateViewer() => new()
    {
        Content = new Border { Width = 300, Height = 240 },
        HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
        VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        IsScrollBarAutoHideEnabled = false,
        IsOverlayScrollBarEnabled = false,
    };

    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }
}
