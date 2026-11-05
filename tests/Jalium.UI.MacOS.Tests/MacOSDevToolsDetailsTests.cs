using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsDetailsTests
{
    [Fact]
    public void DetailBuildersKeepDefinitionStyleAndResourceDataInTemplateItems()
    {
        Initialize(); var target = new DisplayedTestWindow(); var tools = new DevToolsWindow(target);
        try
        {
            Select(tools,null);
            var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2,GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Invoke(tools,"AddGridDefinitions",grid); var definitions = Sections(tools); Assert.Equal(2,definitions.Length);
            Assert.Equal("2*",Items(definitions[0]).Single().ValueText); Assert.StartsWith("Actual:",Items(definitions[0]).Single().DetailText);
            Assert.Equal("Auto",Items(definitions[1]).Single().ValueText);
            Select(tools,null); var style = new Style(typeof(Border)) { BasedOn = new Style(typeof(Border)) }; style.Setters.Add(new Setter(UIElement.OpacityProperty,.5));
            Invoke(tools,"AddStyleInspector",style); var section = Assert.Single(Sections(tools)); Assert.Equal("Style",section.Title);
            Assert.Equal(new[] { "TargetType","BasedOn","Opacity" },Items(section).Select(item => item.Name));
            var button = Assert.Single(Properties(tools).Items.OfType<DevToolsActionButton>()); Assert.True(button.Command!.CanExecute(null)); Assert.True(button.IsEnabled);
            Select(tools,null); Assert.False(button.Command.CanExecute(null));
            IDictionary resources = new Hashtable { ["Brush"] = new SolidColorBrush(Color.FromRgb(10,20,30)), ["Null"] = null, ["Bool"] = true };
            Invoke(tools,"AddResourceInspector",resources); section = Assert.Single(Sections(tools)); Assert.True(section.ShowCount); Assert.Equal(3,section.Items.Count);
            Assert.Equal("#FF0A141E",Items(section).Single(item => item.Name == "Brush").ValueText);
            Assert.All(Properties(tools).Items.Cast<UIElement>(), child => Assert.IsType<DevToolsDetailSection>(child));
        }
        finally { tools.CloseDevTools(); target.Close(); }
    }

    [Fact]
    public void BindingDetailsIncludeOrdinaryMultiAndPriorityExpressionsWithoutDuplicates()
    {
        Initialize(); var target = new DisplayedTestWindow(); var tools = new DevToolsWindow(target);
        try
        {
            Select(tools,null); var source = new Border { Name = "Source", Width = 120 }; var element = new TextBlock();
            BindingOperations.SetBinding(element,TextBlock.TextProperty,new Binding(nameof(FrameworkElement.Name)) { Source = source });
            var multi = new MultiBinding { StringFormat = "{0} {1}" }; multi.Bindings.Add(new Binding(nameof(FrameworkElement.Name)) { Source = source }); multi.Bindings.Add(new Binding(nameof(FrameworkElement.Width)) { Source = source });
            BindingOperations.SetBinding(element,FrameworkElement.TagProperty,multi);
            var priority = new PriorityBinding(); priority.Bindings.Add(new Binding(nameof(FrameworkElement.Name)) { Source = source });
            BindingOperations.SetBinding(element,FrameworkElement.DataContextProperty,priority);
            Invoke(tools,"AddBindingInspector",element); var items = Items(Assert.Single(Sections(tools))).ToArray(); Assert.Equal(3,items.Length);
            Assert.Equal("Name",items.Single(item => item.Name == "Text").ValueText);
            Assert.Contains("MultiBinding",items.Single(item => item.Name == "Tag").DetailText); Assert.Equal("Name + Width",items.Single(item => item.Name == "Tag").ValueText);
            Assert.Contains("PriorityBinding",items.Single(item => item.Name == "DataContext").DetailText);
        }
        finally { tools.CloseDevTools(); target.Close(); }
    }
    private static void Initialize() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }
    private static void Invoke(DevToolsWindow tools, string name, object? value) => typeof(DevToolsWindow).GetMethod(name,BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tools,[value]);
    private static void Select(DevToolsWindow tools, Visual? value) => Invoke(tools,"UpdatePropertiesPanel",value);
    private static DevToolsDetailsList Properties(DevToolsWindow tools) => (DevToolsDetailsList)typeof(DevToolsWindow).GetField("_propertiesPanel",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tools)!;
    private static DevToolsDetailSection[] Sections(DevToolsWindow tools) => Properties(tools).Items.OfType<DevToolsDetailSection>().ToArray();
    private static IEnumerable<DevToolsDetailItem> Items(DevToolsDetailSection section) => (IEnumerable<DevToolsDetailItem>)section.ItemsSource!;
}
