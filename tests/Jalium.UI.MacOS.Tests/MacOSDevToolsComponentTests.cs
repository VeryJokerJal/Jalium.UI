using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsComponentTests
{
    [Fact]
    public void GridAndCanvasAttachedPropertiesUseValidatedLiveEditorsWithoutDuplicates()
    {
        Initialize(); var child = new Border { Child = new TextBlock() }; var grid = new Grid(); grid.Children.Add(child);
        var target = new DisplayedTestWindow { Content = grid }; Layout(target); var tools = new DevToolsWindow(target);
        try
        {
            Visible(tools); Select(tools,child); Layout(tools); var models = Models(tools); Assert.Equal(models.Length,models.Select(model => model.Property).Distinct().Count());
            foreach (var name in new[] { "Grid.Row","Grid.Column","Grid.RowSpan","Grid.ColumnSpan" }) Assert.Contains(models,model => model.EditorProperty.DisplayName == name);
            var span = models.Single(model => model.Property == Grid.ColumnSpanProperty); var editor = Editor(tools,span); Assert.Equal("Grid.ColumnSpan",editor.DisplayName);
            editor.BeginEdit(); editor.EditText = "3"; Assert.True(editor.CommitEdit()); Assert.Equal(3,Grid.GetColumnSpan(child));
            editor.BeginEdit(); editor.EditText = "invalid"; Assert.False(editor.CommitEdit()); Assert.True(editor.HasError); Assert.Equal(3,Grid.GetColumnSpan(child)); editor.CancelEdit();
            editor.BeginEdit(); editor.EditText = "0"; Assert.True(editor.CommitEdit()); Assert.Equal(1,Grid.GetColumnSpan(child));
            Grid.SetColumnSpan(child,2); Assert.Equal(2,span.EditorProperty.Value);
            Assert.Empty(Properties(tools).Items.OfType<StackPanel>());
            var canvas = new Canvas(); grid.Children.Remove(child); canvas.Children.Add(child); target.Content = canvas; Layout(target); Select(tools,child); Layout(tools);
            Assert.All(models,model => Assert.Equal(DevToolsPropertyOrigin.Unavailable,model.Origin)); models = Models(tools);
            Assert.DoesNotContain(models,model => model.Property == Grid.ColumnSpanProperty);
            foreach (var name in new[] { "Canvas.Left","Canvas.Top","Canvas.Right","Canvas.Bottom" }) Assert.Contains(models,model => model.EditorProperty.DisplayName == name);
            var right = models.Single(model => model.Property == Canvas.RightProperty); editor = Editor(tools,right); editor.BeginEdit(); editor.EditText = "12.5"; Assert.True(editor.CommitEdit()); Assert.Equal(12.5,Canvas.GetRight(child));
        }
        finally { tools.CloseDevTools(); target.Close(); }
    }

    [Fact]
    public void GridChildrenThatAlreadyDeclareAttachedFieldsStillShowEachPropertyOnce()
    {
        Initialize(); var child = new Grid(); var parent = new Grid(); parent.Children.Add(child); var target = new DisplayedTestWindow { Content = parent }; var tools = new DevToolsWindow(target);
        try
        {
            Select(tools,child); var models = Models(tools); Assert.Equal(models.Length,models.Select(model => model.Property).Distinct().Count());
            Assert.Equal("Grid.RowSpan",models.Single(model => model.Property == Grid.RowSpanProperty).EditorProperty.DisplayName);
        }
        finally { tools.CloseDevTools(); target.Close(); }
    }

    [Fact]
    public void ComponentDiagnosticsPreserveCountsTypesBoundsAndNumericWeight()
    {
        Initialize(); var button = new Button { Content = "Content",FontWeight = FontWeights.Bold,DataContext = new object() }; var target = new DisplayedTestWindow { Content = button }; var tools = new DevToolsWindow(target);
        try
        {
            Select(tools,button); var section = Properties(tools).Items.OfType<DevToolsDetailSection>().Single(value => value.Title == "Diagnostics");
            var items = Assert.IsAssignableFrom<IEnumerable<DevToolsDetailItem>>(section.ItemsSource).ToArray();
            Assert.Contains(items,item => item.Name == "DesiredSize"); Assert.Contains(items,item => item.Name == "VisualBounds");
            Assert.Equal("String",items.Single(item => item.Name == "Content Type").ValueText); Assert.Equal("700",items.Single(item => item.Name == "FontWeight.OpenType").ValueText);
            Assert.Equal("Object",items.Single(item => item.Name == "DataContext Type").ValueText);
            var list = new ListBox(); list.Items.Add("A"); list.Items.Add("B"); Select(tools,list);
            section = Properties(tools).Items.OfType<DevToolsDetailSection>().Single(value => value.Title == "Diagnostics");
            Assert.Equal("2",Assert.IsAssignableFrom<IEnumerable<DevToolsDetailItem>>(section.ItemsSource).Single(item => item.Name == "Items.Count").ValueText);
        }
        finally { tools.CloseDevTools(); target.Close(); }
    }
    private static void Initialize() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }
    private static void Visible(Window window) => typeof(Window).GetMethod("SetMacOSVisibilityForDisplay",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window,[Visibility.Visible]);
    private static DevToolsDetailsList Properties(DevToolsWindow tools) => (DevToolsDetailsList)typeof(DevToolsWindow).GetField("_propertiesPanel",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tools)!;
    private static void Select(DevToolsWindow tools,Visual visual) => typeof(DevToolsWindow).GetMethod("UpdatePropertiesPanel",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tools,[visual]);
    private static DevToolsDependencyPropertyModel[] Models(DevToolsWindow tools) => Properties(tools).Items.OfType<DevToolsPropertyGroup>().SelectMany(group => (IEnumerable<DevToolsDependencyPropertyModel>)group.ItemsSource!).ToArray();
    private static PropertyGridEditor Editor(DevToolsWindow tools,DevToolsDependencyPropertyModel model) => (PropertyGridEditor)Walk(Properties(tools)).OfType<DevToolsPropertyRow>().Single(row => ReferenceEquals(row.Model,model)).FindName("PART_Editor")!;
    private static IEnumerable<Visual> Walk(Visual root) { yield return root; for (int index = 0; index < root.VisualChildrenCount; index++) if (root.GetVisualChild(index) is { } child) foreach (var item in Walk(child)) yield return item; }
    private static void Layout(UIElement element) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(1040,860)); element.Arrange(new Rect(0,0,1040,860)); element.UpdateLayout(); } }
}
