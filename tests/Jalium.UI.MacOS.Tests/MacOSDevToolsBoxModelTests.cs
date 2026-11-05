using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsBoxModelTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void InspectorUsesTheReusableDiagramAndKeepsTargetBindingsLive(bool inspectBorder)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        FrameworkElement element = inspectBorder ? new Border { Padding = new Thickness(8), BorderThickness = new Thickness(1) } : new Button { Padding = new Thickness(8), BorderThickness = new Thickness(1) };
        element.Margin = new Thickness(2, 4, 6, 8); element.Width = 220; element.Height = 60;
        var target = new DisplayedTestWindow { Content = element }; Layout(target);
        var tools = new DevToolsWindow(target);
        try
        {
            Select(tools, element); var panel = Properties(tools);
            var diagram = Assert.Single(panel.Items.OfType<DevToolsBoxModel>());
            Assert.Equal(element.Margin, diagram.ElementMargin); Assert.Equal(220, diagram.ElementWidth); Assert.Equal(60, diagram.ElementHeight);
            element.Margin = new Thickness(-4, 8, 12, 0); element.Width = 260;
            if (element is Border border) { border.Padding = new Thickness(16); border.BorderThickness = new Thickness(3); }
            else { ((Control)element).Padding = new Thickness(16); ((Control)element).BorderThickness = new Thickness(3); }
            Layout(target);
            Assert.Same(diagram, Assert.Single(panel.Items.OfType<DevToolsBoxModel>()));
            Assert.Equal(element.Margin, diagram.ElementMargin); Assert.Equal(new Thickness(16), diagram.ElementPadding); Assert.Equal(new Thickness(3), diagram.ElementBorderThickness);
            Assert.Equal(260, diagram.ElementWidth); Assert.Equal("-4", diagram.Metrics.MarginLeftText);
            Assert.NotNull(diagram.GetBindingExpression(DevToolsBoxModel.ElementWidthProperty));
            Select(tools, null); Assert.Empty(panel.Items.OfType<DevToolsBoxModel>());
        }
        finally { tools.CloseDevTools(); target.Close(); }
    }
    [Fact]
    public void UnspacedElementsDoNotGetAnEmptyDiagnosticDiagram()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        var element = new Canvas(); var target = new Window { Content = element }; var tools = new DevToolsWindow(target);
        try { Select(tools, element); Assert.Empty(Properties(tools).Items.OfType<DevToolsBoxModel>()); }
        finally { tools.CloseDevTools(); target.Close(); }
    }
    private static DevToolsDetailsList Properties(DevToolsWindow tools) => (DevToolsDetailsList)typeof(DevToolsWindow).GetField("_propertiesPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tools)!;
    private static void Select(DevToolsWindow tools, Visual? element) => typeof(DevToolsWindow).GetMethod("UpdatePropertiesPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tools, [element]);
    private static void Layout(UIElement element) { for (int pass = 0; pass < 2; pass++) { element.InvalidateMeasure(); element.Measure(new Size(640, 480)); element.Arrange(new Rect(0, 0, 640, 480)); element.UpdateLayout(); } }
}
