using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsElementSummaryTemplateTests
{
    public DevToolsElementSummaryTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }
    [Fact]
    public void TypeNameBaseNameAndStatisticsAreTemplateOwnedAndAccessible()
    {
        var summary = new DevToolsElementSummary { TypeName = "Button", BaseTypeName = "ButtonBase", ElementName = "Save", ShowStatistics = true, Depth = 2, ChildrenCount = 3, DescendantCount = 7 };
        Layout(summary, 480);
        Assert.Equal("Button", Part<TextBlock>(summary, "PART_TypeName").Text); Assert.Equal("ButtonBase", Part<TextBlock>(summary, "PART_BaseType").Text);
        Assert.Equal("#Save", Part<TextBlock>(summary, "PART_ElementName").Text);
        Assert.Equal(new[] { "2", "3", "7" }, Part<WrapPanel>(summary, "PART_Statistics").Children.OfType<DevToolsReadout>().Select(value => value.ValueText));
        Assert.IsType<SolidColorBrush>(summary.TryFindResource("DevTools.TokenKeyword"));
        var peer = summary.GetAutomationPeer()!; Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType()); Assert.Contains("Button #Save", peer.GetName()); Assert.Contains("descendants 7", peer.GetName());
        AutomationProperties.SetName(summary, "Selected element"); Assert.Equal("Selected element", peer.GetName());
        summary.ElementName = "Open"; summary.Depth = 4; Assert.Equal("#Open", Part<TextBlock>(summary, "PART_ElementName").Text); Assert.Equal("4", summary.DepthText);
    }
    [Fact]
    public void OptionalMetadataAndMissingTemplatesKeepTheirDataAndState()
    {
        var summary = new DevToolsElementSummary { TypeName = "Border" }; Layout(summary, 240); var original = summary.Template;
        Assert.Equal("NameHidden", VisualStateManager.GetCurrentStateName(summary, "NameStates")); Assert.Equal(Visibility.Collapsed, Part<TextBlock>(summary, "PART_BaseType").Visibility);
        Assert.Equal(Visibility.Collapsed, Part<WrapPanel>(summary, "PART_Statistics").Visibility);
        var blank = new ControlTemplate(typeof(DevToolsElementSummary)); blank.SetVisualTree(() => new Border()); summary.Template = blank; summary.ApplyTemplate();
        summary.ElementName = "Current"; summary.BaseTypeName = "Decorator"; summary.ShowStatistics = true; summary.DescendantCount = 10;
        summary.Template = original; Layout(summary, 240);
        Assert.Equal("#Current", Part<TextBlock>(summary, "PART_ElementName").Text); Assert.Equal(Visibility.Visible, Part<TextBlock>(summary, "PART_BaseType").Visibility); Assert.Equal("10", summary.DescendantsText);
        Assert.Throws<ArgumentException>(() => summary.Depth = -1);
    }
    [Theory]
    [InlineData(200)] [InlineData(480)]
    public void LongIdentifiersAndStatisticsWrapInsideTheAvailableWidth(double width)
    {
        var summary = new DevToolsElementSummary { TypeName = "AControlWithAnIntentionallyLongRuntimeTypeNameAndGenericArguments", BaseTypeName = "AnIntentionallyLongBaseControlType", ElementName = new string('名', 40), ShowStatistics = true, Depth = 12345, ChildrenCount = 6789, DescendantCount = 1234567 };
        Layout(summary, width); Assert.InRange(summary.DesiredSize.Width, 0, width + .5);
        foreach (string name in new[] { "PART_TypeName", "PART_BaseType", "PART_ElementName", "PART_Statistics" })
        {
            var part = Part<FrameworkElement>(summary, name); Assert.True(part.TranslatePoint(new Point(part.ActualWidth, 0), summary).X <= width + .5);
        }
        Layout(summary, 480); Assert.Equal(480, summary.AvailableTextWidth);
    }
    [Fact]
    public void AContentSizedSummaryCanGrowAfterAZeroWidthArrangement()
    {
        var summary = new DevToolsElementSummary { TypeName = "Border", HorizontalAlignment = HorizontalAlignment.Left };
        summary.Measure(new Size(0, 400)); summary.Arrange(new Rect(0,0,0,summary.DesiredSize.Height));
        summary.TypeName = "AControlThatCanGrow"; summary.InvalidateMeasure(); summary.Measure(new Size(double.PositiveInfinity, 400));
        Assert.True(summary.DesiredSize.Width > 20); Assert.True(double.IsPositiveInfinity(summary.AvailableTextWidth));
    }
    [Fact]
    public void AnEmptySummaryDoesNotDisplayAnUnusableClassKeyword()
    {
        var summary = new DevToolsElementSummary(); Layout(summary, 200);
        Assert.Equal(0, summary.DesiredSize.Height); Assert.Equal(Visibility.Collapsed, Part<WrapPanel>(summary, "PART_TypeDeclaration").Visibility);
        summary.TypeName = "Border"; Layout(summary, 200); Assert.True(summary.DesiredSize.Height > 0);
        Assert.Equal(Visibility.Visible, Part<WrapPanel>(summary, "PART_TypeDeclaration").Visibility);
    }
    private static T Part<T>(Control control, string name) where T : class => Assert.IsAssignableFrom<T>(control.FindName(name));
    private static void Layout(UIElement element, double width) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(width, 800)); element.Arrange(new Rect(0,0,width,element.DesiredSize.Height)); element.UpdateLayout(); } }
}
