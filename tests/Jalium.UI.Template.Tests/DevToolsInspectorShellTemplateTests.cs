using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsInspectorShellTemplateTests
{
    public DevToolsInspectorShellTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Theory]
    [InlineData(240)] [InlineData(480)] [InlineData(1040)]
    public void PaneLayoutAndFiniteDetailRowsFollowTheAvailableWidth(double width)
    {
        var target = new Border { Name = new string('名',30),Opacity = .5 };
        using var name = new DevToolsDependencyPropertyModel(target,FrameworkElement.NameProperty);
        using var opacity = new DevToolsDependencyPropertyModel(target,UIElement.OpacityProperty);
        var group = new DevToolsPropertyGroup { ItemsSource = new[] { name,opacity } }; var body = new DevToolsDetailsList(); body.Items.Add(group);
        var inspector = new DevToolsInspector { TreeContent = new ListBox { ItemsSource = Enumerable.Range(1,50).ToArray() },DetailsContent = body }; Layout(inspector,width);
        var tree = Part<Grid>(inspector,"PART_TreePane"); var details = Part<Border>(inspector,"PART_DetailsPane"); var scroll = Part<ScrollViewer>(inspector,"PART_DetailsScrollViewer");
        Assert.Equal(width < 760,inspector.IsCompact); Assert.Equal(ScrollBarVisibility.Disabled,scroll.HorizontalScrollBarVisibility);
        Assert.Equal(inspector.IsCompact ? GridResizeDirection.Rows : GridResizeDirection.Columns,Part<GridSplitter>(inspector,"PART_Splitter").ResizeDirection);
        var treeOrigin = tree.TranslatePoint(new Point(0,0),inspector); var detailOrigin = details.TranslatePoint(new Point(0,0),inspector);
        if (inspector.IsCompact) { Assert.True(detailOrigin.Y >= treeOrigin.Y + tree.ActualHeight); Assert.True(details.ActualWidth >= width - 2); Assert.True(tree.ActualHeight > 100); Assert.True(details.ActualHeight > 200); }
        else Assert.True(detailOrigin.X >= treeOrigin.X + tree.ActualWidth);
        Assert.InRange(scroll.ScrollableWidth,0,.5);
        foreach (var row in Walk(group).OfType<DevToolsPropertyRow>())
        {
            Assert.True(row.ActualWidth >= body.ActualWidth - 13);
            Assert.True(row.TranslatePoint(new Point(row.ActualWidth,0),body).X <= body.ActualWidth + .5);
            Assert.Equal(body.ActualWidth - 12 < 305,double.IsNaN(row.EffectiveNameColumnWidth));
        }
    }

    [Fact]
    public void WidthTransitionsRestoreTheSameContentAndSplitterDirection()
    {
        var tree = new ListBox(); var details = new TextBlock { Text = "Details" }; var inspector = new DevToolsInspector { TreeContent = tree,DetailsContent = details };
        foreach (double width in new[] { 1040d,240d,480d,1040d })
        {
            Layout(inspector,width); Assert.Equal(width < 760,inspector.IsCompact);
            Assert.Same(tree,Part<ContentPresenter>(inspector,"PART_TreeContent").Content); Assert.Same(details,Part<ContentPresenter>(inspector,"PART_DetailsContent").Content);
            Assert.Equal(inspector.IsCompact ? GridResizeDirection.Rows : GridResizeDirection.Columns,Part<GridSplitter>(inspector,"PART_Splitter").ResizeDirection);
        }
    }

    [Fact]
    public void SplitterMovesInBothLayoutsAndAResizedWidePaneDoesNotOverflowCompactMode()
    {
        var inspector = new DevToolsInspector { TreeContent = new Border(),DetailsContent = new Border() }; Layout(inspector,1040);
        var splitter = Part<GridSplitter>(inspector,"PART_Splitter"); var tree = Part<Grid>(inspector,"PART_TreePane");
        var transform = Assert.IsAssignableFrom<ITransformProvider>(splitter.GetAutomationPeer());
        double originalWidth = tree.ActualWidth, originalWideHeight = tree.ActualHeight; transform.Move(40,0); Layout(inspector,1040); Assert.True(tree.ActualWidth > originalWidth + 30);
        double resizedWidth = tree.ActualWidth; Layout(inspector,480); Assert.True(tree.ActualWidth <= 480.5);
        double originalHeight = tree.ActualHeight; transform.Move(0,30); Layout(inspector,480); Assert.True(tree.ActualHeight > originalHeight + 20); double resizedHeight = tree.ActualHeight;
        Layout(inspector,1040); Assert.InRange(tree.ActualWidth,resizedWidth - .5,resizedWidth + .5); Assert.InRange(tree.ActualHeight,originalWideHeight - .5,originalWideHeight + .5);
        Layout(inspector,480); Assert.InRange(tree.ActualHeight,resizedHeight - .5,resizedHeight + .5);
    }

    [Fact]
    public void EmptyStateAndMissingPartsAreTemplateOwnedAndRecoverable()
    {
        var inspector = new DevToolsInspector { IsDetailsEmpty = true,EmptyText = "选择元素",DetailsContent = new TextBlock { Text = "Value" } }; Layout(inspector,480);
        Assert.Equal(Visibility.Visible,Part<TextBlock>(inspector,"PART_EmptyDetails").Visibility); Assert.Equal("选择元素",Part<TextBlock>(inspector,"PART_EmptyDetails").Text);
        Assert.Equal(Visibility.Collapsed,Part<ScrollViewer>(inspector,"PART_DetailsScrollViewer").Visibility);
        inspector.IsDetailsEmpty = false; Assert.Equal(Visibility.Collapsed,Part<TextBlock>(inspector,"PART_EmptyDetails").Visibility);
        var original = inspector.Template; var blank = new ControlTemplate(typeof(DevToolsInspector)); blank.SetVisualTree(() => new Border()); inspector.Template = blank; Layout(inspector,240);
        Assert.False(inspector.FocusSearch()); inspector.IsDetailsEmpty = true; inspector.Template = original; Layout(inspector,1040);
        Assert.False(inspector.IsCompact); Assert.Equal(Visibility.Visible,Part<TextBlock>(inspector,"PART_EmptyDetails").Visibility);
    }

    [Fact]
    public void ConsumersCanOptIntoHorizontalScrollingForTheirOwnWideContent()
    {
        var inspector = new DevToolsInspector { DetailsContent = new Border { Width = 1200,Height = 100 },DetailsHorizontalScrollBarVisibility = ScrollBarVisibility.Auto }; Layout(inspector,480);
        var scroll = Part<ScrollViewer>(inspector,"PART_DetailsScrollViewer"); Assert.Equal(ScrollBarVisibility.Auto,scroll.HorizontalScrollBarVisibility); Assert.True(scroll.ScrollableWidth > 500);
        inspector.DetailsHorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; Assert.Equal(ScrollBarVisibility.Disabled,scroll.HorizontalScrollBarVisibility);
    }

    private static T Part<T>(FrameworkElement owner,string name) where T : class => Assert.IsType<T>(owner.FindName(name));
    private static IEnumerable<Visual> Walk(Visual root) { yield return root; for (int index = 0; index < root.VisualChildrenCount; index++) if (root.GetVisualChild(index) is { } child) foreach (var item in Walk(child)) yield return item; }
    private static void Layout(UIElement element,double width) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(width,800)); element.Arrange(new Rect(0,0,width,800)); element.UpdateLayout(); } }
}
