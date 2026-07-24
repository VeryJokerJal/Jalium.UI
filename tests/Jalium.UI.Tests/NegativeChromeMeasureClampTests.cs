using System.Collections.Generic;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Charts;

namespace Jalium.UI.Tests;

/// <summary>
/// Negative Padding / BorderThickness / PlotAreaMargin values are legal (Thickness
/// deliberately performs no sign validation), but the Size constructor rejects negative
/// dimensions. Every control whose MeasureOverride sums content + chrome straight into a
/// Size must clamp at that summation sink instead of throwing mid-measure. Follow-up
/// hardening for the new Size(-18, 0) theme-switch crash — see
/// <see cref="FrameworkElementArrangeClampTests"/> for the arrange-side twin.
/// </summary>
public class NegativeChromeMeasureClampTests
{
    private static readonly Size Available = new(200, 200);

    private static Size MeasureAndAssertNonNegative(UIElement element)
    {
        element.Measure(Available);
        Assert.True(element.DesiredSize.Width >= 0,
            $"DesiredSize.Width was {element.DesiredSize.Width}");
        Assert.True(element.DesiredSize.Height >= 0,
            $"DesiredSize.Height was {element.DesiredSize.Height}");
        return element.DesiredSize;
    }

    [Fact]
    public void Border_NoChild_NegativePadding_ClampsDesiredSize()
    {
        var desired = MeasureAndAssertNonNegative(new Border { Padding = new Thickness(-20) });
        Assert.Equal(new Size(0, 0), desired);
    }

    [Fact]
    public void Border_NegativeBorderThicknessExceedingChild_ClampsDesiredSize()
    {
        var border = new Border
        {
            BorderThickness = new Thickness(-8),
            Child = new Border { Width = 4, Height = 4 },
        };
        MeasureAndAssertNonNegative(border);
    }

    [Fact]
    public void ContentControl_DirectContent_NegativePaddingExceedingContent_ClampsDesiredSize()
    {
        var control = new ContentControl
        {
            Content = new Border { Width = 4, Height = 4 },
            Padding = new Thickness(-20),
        };
        // The regression path is the untemplated direct-content branch.
        Assert.Null(control.Template);
        MeasureAndAssertNonNegative(control);
    }

    [Fact]
    public void TextBlock_EmptyText_NegativePadding_ClampsDesiredSize()
    {
        var desired = MeasureAndAssertNonNegative(
            new TextBlock { Text = string.Empty, Padding = new Thickness(-10) });
        Assert.Equal(new Size(0, 0), desired);
    }

    [Fact]
    public void TextBlock_WithText_NegativePaddingExceedingText_ClampsDesiredSize()
    {
        MeasureAndAssertNonNegative(
            new TextBlock { Text = "x", Padding = new Thickness(-500) });
    }

    [Fact]
    public void HyperlinkButton_NoContent_NegativePadding_ClampsDesiredSize()
    {
        var button = new HyperlinkButton { Padding = new Thickness(-15) };
        // The regression path is the untemplated direct-measure fallback.
        Assert.Null(button.Template);
        MeasureAndAssertNonNegative(button);
    }

    [Fact]
    public void QRCode_NoSymbol_NegativePadding_ClampsDesiredSize()
    {
        // Default (empty) Text produces no symbol, hitting the chrome-only fast path.
        MeasureAndAssertNonNegative(new QRCode { Padding = new Thickness(-25) });
    }

    [Fact]
    public void Label_NoContent_NegativePadding_ClampsDesiredSize()
    {
        var label = new Label { Padding = new Thickness(-20) };
        // The regression path is the untemplated fallback measurement.
        Assert.Null(label.Template);
        MeasureAndAssertNonNegative(label);
    }

    [Fact]
    public void MenuFlyoutPresenter_NegativePadding_ClampsDesiredSize()
    {
        var presenter = new MenuFlyoutPresenter(new MenuFlyout())
        {
            Padding = new Thickness(-30),
        };
        MeasureAndAssertNonNegative(presenter);
    }

    [Fact]
    public void ChartLegend_EmptyItems_NegativePadding_ClampsDesiredSize()
    {
        var legend = new ChartLegend
        {
            Items = new List<ChartLegendItem>(),
            Padding = new Thickness(-10),
        };
        MeasureAndAssertNonNegative(legend);
    }

    [Fact]
    public void FlowchartDiagram_NoNodes_NegativePlotAreaMargin_ClampsDesiredSize()
    {
        var diagram = new FlowchartDiagram { PlotAreaMargin = new Thickness(-30) };
        MeasureAndAssertNonNegative(diagram);
    }
}
