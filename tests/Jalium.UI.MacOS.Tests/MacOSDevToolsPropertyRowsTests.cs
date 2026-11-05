using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsPropertyRowsTests
{
    [Fact]
    public void InspectorUsesLiveTypedEditorsForEditableAndReadonlyPrimitiveProperties()
    {
        Initialize();
        var element = new Border { Name = "Inspected", Opacity = .5 };
        var target = new Window { Content = element }; var tools = new DevToolsWindow(target);
        try
        {
            Select(tools, element);
            var rows = Rows(tools);
            var opacity = rows.Single(row => row.Model!.Property == UIElement.OpacityProperty);
            Assert.Same(opacity.Model!.EditorProperty, opacity.Property);
            element.Opacity = .75; Assert.Equal(.75, opacity.Property!.Value);
            opacity.Property.Value = .25; Assert.Equal(.25, element.Opacity);
            var enabled = rows.Single(row => row.Model!.Property == UIElement.IsEnabledProperty);
            enabled.Property!.Value = false; Assert.False(element.IsEnabled);
            var alignment = rows.Single(row => row.Model!.Property == FrameworkElement.HorizontalAlignmentProperty);
            alignment.Property!.Value = HorizontalAlignment.Right; Assert.Equal(HorizontalAlignment.Right, element.HorizontalAlignment);
            Assert.True(rows.Single(row => row.Model!.Property == FrameworkElement.ActualWidthProperty).Property!.IsReadOnly);
            Assert.Equal(rows.Count, rows.Select(row => row.Model!.Property).Distinct().Count());
            Assert.Equal(rows.Count, SourceObservers(element));
        }
        finally { tools.CloseDevTools(); target.Close(); }
    }

    [Fact]
    public void RefreshAndTargetChangesDisposeRetiredModelsWithoutAccumulatingSubscriptions()
    {
        Initialize();
        var first = new Border(); var second = new Button();
        var target = new Window { Content = first }; var tools = new DevToolsWindow(target);
        try
        {
            Select(tools, first); int count = Rows(tools).Count;
            var retired = Rows(tools).Single(row => row.Model!.Property == UIElement.OpacityProperty).Model!;
            for (int pass = 0; pass < 4; pass++) { Select(tools, first); Assert.Equal(count, SourceObservers(first)); }
            Assert.Equal(DevToolsPropertyOrigin.Unavailable, retired.Origin);
            Assert.Throws<ObjectDisposedException>(() => retired.EditorProperty.Value = .5);
            Select(tools, second); Assert.Equal(0, SourceObservers(first)); Assert.Equal(Rows(tools).Count, SourceObservers(second));
            Select(tools, null); Assert.Equal(0, SourceObservers(second)); Assert.Empty(Rows(tools));
        }
        finally { tools.CloseDevTools(); target.Close(); }
    }

    [Fact]
    public void CancelledCloseKeepsModelsLiveAndAcceptedCloseReleasesThem()
    {
        Initialize();
        var element = new Border(); var target = new Window { Content = element }; var tools = new DevToolsWindow(target);
        int closing = 0; tools.Closing += (_, args) => args.Cancel = ++closing == 1;
        try
        {
            Select(tools, element); var model = Rows(tools).Single(row => row.Model!.Property == UIElement.OpacityProperty).Model!;
            int observers = SourceObservers(element);
            tools.CloseDevTools(); Assert.Equal(observers, SourceObservers(element));
            element.Opacity = .5; Assert.Equal(.5, model.EditorProperty.Value);
            tools.CloseDevTools(); Assert.Equal(2, closing); Assert.Equal(0, SourceObservers(element));
            Assert.Equal(DevToolsPropertyOrigin.Unavailable, model.Origin);
            Assert.Throws<ObjectDisposedException>(() => model.EditorProperty.Value = .25);
        }
        finally { tools.CloseDevTools(); target.Close(); }
    }

    private static void Initialize() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }
    private static List<DevToolsPropertyRow> Rows(DevToolsWindow tools) => Walk((DevToolsDetailsList)typeof(DevToolsWindow).GetField("_propertiesPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tools)!).OfType<DevToolsPropertyRow>().ToList();
    private static void Select(DevToolsWindow tools, Visual? element)
    {
        typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tools, [Visibility.Visible]);
        typeof(DevToolsWindow).GetMethod("UpdatePropertiesPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tools, [element]);
        for (int pass = 0; pass < 3; pass++) { tools.InvalidateMeasure(); tools.Measure(new Size(1040,860)); tools.Arrange(new Rect(0,0,1040,860)); tools.UpdateLayout(); }
    }
    private static IEnumerable<Visual> Walk(Visual root) { yield return root; for (int index = 0; index < root.VisualChildrenCount; index++) if (root.GetVisualChild(index) is { } child) foreach (var item in Walk(child)) yield return item; }
    private static int SourceObservers(DependencyObject target) => DependencyObjectSubscriptionProbe.Count(target, "ValueSourceInvalidatedInternal");
}
