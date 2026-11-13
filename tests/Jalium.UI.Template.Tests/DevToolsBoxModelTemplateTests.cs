using System.Globalization;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsBoxModelTemplateTests
{
    public DevToolsBoxModelTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }
    [Fact]
    public void DefaultTemplateProjectsEveryEdgeAndAnAccessibleSize()
    {
        var view = View(); Layout(view, 400);
        Assert.NotNull(view.Template); Assert.Equal("Wide", State(view, "LayoutStates"));
        foreach (var (part, value) in new[] { ("PART_MarginLeft", "1"), ("PART_MarginTop", "2"), ("PART_MarginRight", "3"), ("PART_MarginBottom", "4"), ("PART_BorderLeft", "5"), ("PART_BorderTop", "6"), ("PART_BorderRight", "7"), ("PART_BorderBottom", "8"), ("PART_PaddingLeft", "9"), ("PART_PaddingTop", "10"), ("PART_PaddingRight", "11"), ("PART_PaddingBottom", "12") })
            Assert.Equal(value, Part<TextBlock>(view, part).Text);
        Assert.Equal("320 × 180", Part<TextBlock>(view, "PART_Size").Text); Assert.Equal("SizeKnown", State(view, "SizeStates"));
        var peer = view.GetAutomationPeer()!;
        Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType()); Assert.Contains("Margin: left 1, top 2", peer.GetName());
        AutomationProperties.SetName(view, "Selected element spacing"); Assert.Equal("Selected element spacing", peer.GetName());
        view.MarginAreaBrush = Brushes.Magenta; Assert.Same(Brushes.Magenta, Part<Grid>(view, "PART_MarginArea").Background);
        Assert.Throws<InvalidOperationException>(() => view.SetValue(DevToolsBoxModel.MetricsProperty, null));
    }
    [Fact]
    public void LiveInputsPreserveBindingsAndRetiredTemplateElementsStopUpdating()
    {
        var source = new Border { Margin = new Thickness(8), Padding = new Thickness(12), Width = 320, Height = 180 };
        var view = new DevToolsBoxModel(); view.SetBinding(DevToolsBoxModel.ElementPaddingProperty, new Binding(nameof(Border.Padding)) { Source = source });
        Layout(view, 400); var original = view.Template; var retired = Part<TextBlock>(view, "PART_PaddingLeft");
        Assert.Equal("12", retired.Text);
        source.Padding = new Thickness(16, 4, 8, 2); Assert.Equal("16", retired.Text); Assert.Equal("4", Part<TextBlock>(view, "PART_PaddingTop").Text);
        var empty = new ControlTemplate(typeof(DevToolsBoxModel)); empty.SetVisualTree(() => new Border());
        view.Template = empty; view.ApplyTemplate(); source.Padding = new Thickness(24); Layout(view, 240);
        Assert.Equal("16", retired.Text); Assert.Equal(new Thickness(24), view.ElementPadding);
        Assert.NotNull(view.GetBindingExpression(DevToolsBoxModel.ElementPaddingProperty));
        view.Template = original; Layout(view, 400); Assert.Equal("24", Part<TextBlock>(view, "PART_PaddingLeft").Text);
        view.ShowLegend = false; Assert.Equal(Visibility.Collapsed, Part<WrapPanel>(view, "PART_Legend").Visibility);
        view.ElementWidth = double.NaN; Assert.Equal("SizeUnknown", State(view, "SizeStates")); Assert.Equal("No layout", Part<TextBlock>(view, "PART_Size").Text);
    }
    [Theory]
    [InlineData(200)] [InlineData(240)] [InlineData(400)]
    public void DiagramAndLegendFitTheirViewportAndSwitchLayouts(double width)
    {
        var view = View(); Layout(view, width);
        Assert.InRange(view.DesiredSize.Width, 0, width + .5);
        Assert.Equal(width <= view.CompactWidth ? "Compact" : "Wide", State(view, "LayoutStates"));
        Assert.Equal(width <= view.CompactWidth ? Visibility.Collapsed : Visibility.Visible, Part<TextBlock>(view, "PART_PaddingLabel").Visibility);
        foreach (var name in new[] { "PART_MarginArea", "PART_BorderArea", "PART_PaddingArea", "PART_Size", "PART_Legend" })
        {
            var part = Part<FrameworkElement>(view, name);
            Assert.True(part.TranslatePoint(new Point(part.ActualWidth, 0), view).X <= width + .5, name);
            Assert.True(double.IsFinite(part.ActualWidth)); Assert.True(double.IsFinite(part.ActualHeight));
        }
        Layout(view, 400); Assert.Equal("Wide", State(view, "LayoutStates"));
        Assert.Equal(Visibility.Visible, Part<TextBlock>(view, "PART_PaddingLabel").Visibility);
    }
    [Fact]
    public void ContentSizedConsumersChooseTheirStateFromTheArrangedWidth()
    {
        var view = View(); view.HorizontalAlignment = HorizontalAlignment.Left;
        view.Measure(new Size(400, 600)); view.Arrange(new Rect(0, 0, 220, view.DesiredSize.Height));
        Assert.True(view.ActualWidth <= view.CompactWidth); Assert.Equal("Compact", State(view, "LayoutStates"));
        view.InvalidateMeasure(); view.Measure(new Size(400, 600)); view.Arrange(new Rect(0, 0, 220, view.DesiredSize.Height));
        Assert.Equal("Compact", State(view, "LayoutStates"));
        view.HorizontalAlignment = HorizontalAlignment.Stretch; Layout(view, 400);
        Assert.Equal("Wide", State(view, "LayoutStates"));
    }
    [Fact]
    public void MetricsUseStableNumericFormattingForNegativeAndUnmeasuredValues()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var metrics = new DevToolsBoxModelMetrics(new Thickness(-1.25, 2.5, 3, 4), new Thickness(1), new Thickness(0), double.PositiveInfinity, 10);
            Assert.Equal("-1.25", metrics.MarginLeftText); Assert.Equal("2.5", metrics.MarginTopText); Assert.Equal("1", metrics.BorderText);
            Assert.False(metrics.HasMeasuredSize); Assert.Equal("No layout", metrics.SizeText); Assert.Contains("left -1.25", metrics.AutomationSummary);
            metrics = new(default, default, default, 0, 0); Assert.True(metrics.HasMeasuredSize); Assert.Equal("0 × 0", metrics.SizeText);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    private static DevToolsBoxModel View() => new() { ElementMargin = new Thickness(1, 2, 3, 4), ElementBorderThickness = new Thickness(5, 6, 7, 8), ElementPadding = new Thickness(9, 10, 11, 12), ElementWidth = 320, ElementHeight = 180 };
    [Theory]
    [InlineData(200, 1.25, true)] [InlineData(400, 1.25, true)] [InlineData(200, -999999.25, true)]
    [InlineData(200, 1.25, false)] [InlineData(400, 1.25, false)] [InlineData(200, -999999.25, false)]
    public void FractionalAndLongEdgesWrapWithinTheDiagramInsteadOfEllipsizing(double width, double margin, bool showLegend)
    {
        var view = new DevToolsBoxModel { ElementMargin = new Thickness(margin), ElementBorderThickness = new Thickness(1), ElementPadding = new Thickness(4.5), ElementWidth = 260, ElementHeight = 80, ShowLegend = showLegend };
        Layout(view, width); Assert.InRange(view.DesiredSize.Width, 0, width + .5);
        foreach (string area in new[] { "Margin", "Border", "Padding" })
        foreach (string edge in new[] { "Left", "Top", "Right", "Bottom" })
        {
            var text = Part<TextBlock>(view, "PART_" + area + edge);
            Assert.Equal(TextTrimming.None, text.TextTrimming); Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
            Assert.True(text.TranslatePoint(new Point(text.ActualWidth, 0), view).X <= width + .5);
            if (edge is "Left" or "Right" && margin == 1.25)
            {
                var glyphs = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight };
                glyphs.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); Assert.True(glyphs.DesiredSize.Width <= text.ActualWidth + .5);
            }
        }
    }
    private static T Part<T>(Control view, string name) where T : class => Assert.IsAssignableFrom<T>(view.FindName(name));
    private static string? State(Control view, string group) => VisualStateManager.GetCurrentStateName(view, group);
    private static void Layout(UIElement element, double width) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(width, 600)); element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height)); element.UpdateLayout(); } }
}
