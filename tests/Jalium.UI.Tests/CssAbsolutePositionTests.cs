using Jalium.UI.Controls;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>position:absolute protocol: out-of-flow children, inset solving, panel opt-ins.</summary>
public sealed class CssAbsolutePositionTests
{
    static CssAbsolutePositionTests()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
            typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);
    }

    private static void MeasureArrange(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
    }

    [Fact]
    public void StackPanel_AbsoluteChild_LeavesTheFlow()
    {
        var flowA = new Border { Height = 30 };
        var absolute = new Border { Width = 40, Height = 40 };
        Css.SetStyle(absolute, "position: absolute; left: 10px; top: 20px");
        var flowB = new Border { Height = 30 };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(flowA);
        panel.Children.Add(absolute);
        panel.Children.Add(flowB);

        panel.Measure(new Size(200, double.PositiveInfinity));
        // Flow: 30 + 10 + 30 — the absolute child adds neither height nor spacing.
        Assert.Equal(70.0, panel.DesiredSize.Height);

        panel.Arrange(new Rect(0, 0, 200, 100));
        Assert.Equal(10.0, absolute.VisualBounds.X);
        Assert.Equal(20.0, absolute.VisualBounds.Y);
        Assert.Equal(0.0, flowA.VisualBounds.Y);
        Assert.Equal(40.0, flowB.VisualBounds.Y);
    }

    [Fact]
    public void BothEdges_AutoWidth_StretchesBetweenInsets()
    {
        var absolute = new Border();
        Css.SetStyle(absolute, "position: absolute; left: 10px; right: 30px; top: 0; height: 20px");
        var panel = new StackPanel();
        panel.Children.Add(absolute);
        MeasureArrange(panel, 200, 100);

        Assert.Equal(10.0, absolute.VisualBounds.X);
        Assert.Equal(160.0, absolute.VisualBounds.Width);
        Assert.Equal(20.0, absolute.VisualBounds.Height);
    }

    [Fact]
    public void PercentInsets_ResolveAgainstPanelSize()
    {
        var absolute = new Border { Width = 40, Height = 20 };
        Css.SetStyle(absolute, "position: absolute; left: 25%; top: 50%");
        var panel = new StackPanel();
        panel.Children.Add(absolute);
        MeasureArrange(panel, 200, 100);

        Assert.Equal(50.0, absolute.VisualBounds.X);
        Assert.Equal(50.0, absolute.VisualBounds.Y);
    }

    [Fact]
    public void PercentSize_OnAbsoluteChild_ResolvesAgainstPanel()
    {
        var absolute = new Border();
        Css.SetStyle(absolute, "position: absolute; left: 0; top: 0; width: 50%; height: 25%");
        var panel = new StackPanel();
        panel.Children.Add(absolute);
        MeasureArrange(panel, 200, 400);

        Assert.Equal(100.0, absolute.VisualBounds.Width);
        Assert.Equal(100.0, absolute.VisualBounds.Height);
    }

    [Fact]
    public void RightBottom_PositionFromFarEdge()
    {
        var absolute = new Border { Width = 40, Height = 20 };
        Css.SetStyle(absolute, "position: absolute; right: 10px; bottom: 5px");
        var panel = new StackPanel();
        panel.Children.Add(absolute);
        MeasureArrange(panel, 200, 100);

        Assert.Equal(150.0, absolute.VisualBounds.X);
        Assert.Equal(75.0, absolute.VisualBounds.Y);
    }

    [Fact]
    public void Grid_AbsoluteChild_DoesNotGrowAutoTracks()
    {
        var flow = new Border { Width = 50, Height = 30 };
        var absolute = new Border { Width = 200, Height = 200 };
        Css.SetStyle(absolute, "position: absolute; left: 0; top: 0");
        var grid = new Grid();
        grid.Children.Add(flow);
        grid.Children.Add(absolute);

        grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(50.0, grid.DesiredSize.Width);
        Assert.Equal(30.0, grid.DesiredSize.Height);
    }

    [Fact]
    public void DockPanel_AbsoluteChild_NoDockSlotNoSpacing()
    {
        var docked = new Border { Width = 40, Height = 100 };
        DockPanel.SetDock(docked, Dock.Left);
        var absolute = new Border { Width = 30, Height = 30 };
        Css.SetStyle(absolute, "position: absolute; left: 5px; top: 5px");
        var fill = new Border();
        var panel = new DockPanel { Spacing = 8 };
        panel.Children.Add(docked);
        panel.Children.Add(absolute);
        panel.Children.Add(fill);
        MeasureArrange(panel, 200, 100);

        // Fill starts after docked(40) + one spacing(8); the absolute child injects nothing.
        Assert.Equal(48.0, fill.VisualBounds.X);
        Assert.Equal(5.0, absolute.VisualBounds.X);
    }

    [Fact]
    public void Canvas_AbsoluteChild_UsesInsetProtocol()
    {
        var absolute = new Border();
        Css.SetStyle(absolute, "position: absolute; left: 10%; right: 10%; top: 0; height: 20px");
        var canvas = new Canvas();
        canvas.Children.Add(absolute);
        MeasureArrange(canvas, 300, 100);

        Assert.Equal(30.0, absolute.VisualBounds.X);
        Assert.Equal(240.0, absolute.VisualBounds.Width);
    }

    [Fact]
    public void StaticPosition_KeepsCanvasCompatibility()
    {
        var child = new Border { Width = 40, Height = 20 };
        Css.SetStyle(child, "left: 15px; top: 25px");
        var canvas = new Canvas();
        canvas.Children.Add(child);
        MeasureArrange(canvas, 200, 100);

        Assert.Equal(15.0, Canvas.GetLeft(child));
        Assert.Equal(25.0, Canvas.GetTop(child));
        Assert.Equal(15.0, child.VisualBounds.X);
        Assert.Equal(25.0, child.VisualBounds.Y);
    }

    [Fact]
    public void PositionToggle_RestoresFlow()
    {
        var absolute = new Border { Height = 30 };
        var sibling = new Border { Height = 30 };
        var panel = new StackPanel();
        panel.Children.Add(absolute);
        panel.Children.Add(sibling);

        Css.SetStyle(absolute, "position: absolute; left: 0; top: 0");
        panel.InvalidateMeasure(); // headless: no LayoutManager to propagate the child's invalidation
        panel.Measure(new Size(100, double.PositiveInfinity));
        Assert.Equal(30.0, panel.DesiredSize.Height);

        Css.SetStyle(absolute, string.Empty);
        panel.InvalidateMeasure();
        panel.Measure(new Size(100, double.PositiveInfinity));
        Assert.Equal(60.0, panel.DesiredSize.Height);
    }

    private sealed class InterceptingPanel : StackPanel
    {
        public string? LastValue;

        protected internal override bool TryApplyCssPropertyCore(
            string propertyName, string rawValue, in CssDeclarationSetter setter)
        {
            if (propertyName.Equals("panel-density", StringComparison.OrdinalIgnoreCase))
            {
                LastValue = rawValue;
                setter.Set(SpacingProperty, rawValue == "compact" ? 2.0 : 8.0);
                return true;
            }

            return base.TryApplyCssPropertyCore(propertyName, rawValue, in setter);
        }
    }

    [Fact]
    public void UnknownProperty_FallsBackToInterceptionProtocol()
    {
        var panel = new InterceptingPanel();
        Css.SetStyle(panel, "panel-density: compact");

        Assert.Equal("compact", panel.LastValue);
        Assert.Equal(2.0, panel.GetValue(StackPanel.SpacingProperty));

        // Rule removal clears the CSS layer and restores the default.
        Css.SetStyle(panel, string.Empty);
        Assert.Equal(0.0, panel.GetValue(StackPanel.SpacingProperty));
    }
}
