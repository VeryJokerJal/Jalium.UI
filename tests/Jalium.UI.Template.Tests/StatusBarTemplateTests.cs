using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class StatusBarTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void DefaultBarOwnsItsFramePresenterAndStatusPanel()
    {
        var bar = new StatusBar(); bar.Resources.MergedDictionaries.Add(Defaults.Value);
        bar.Items.Add("Ready"); bar.Items.Add(new StatusBarItem { Content = "UTF-8" });
        Layout(bar);
        Assert.IsType<Border>(bar.GetVisualChild(0));
        var presenter = Assert.IsType<ItemsPresenter>(bar.FindName("PART_ItemsPresenter"));
        var panel = Assert.IsAssignableFrom<StackPanel>(presenter.GetVisualChild(0));
        Assert.Equal("StatusBarPanel", panel.GetType().Name);
        Assert.IsType<StatusBarItem>(bar.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Equal(2, panel.Children.Count);
    }

    [Fact]
    public void ItemOwnsItsContentAndRetemplatingReparentsTheSameVisual()
    {
        var content = new Button { Content = "Action" };
        var item = Create(content); Layout(item);
        var old = Assert.IsType<ContentPresenter>(item.FindName("PART_ContentPresenter"));
        Assert.Same(content, old.Content);
        Assert.Same(old, content.VisualParent);
        var custom = new ControlTemplate(typeof(StatusBarItem)); custom.SetVisualTree(() => new Border { Height = 20 });
        item.Template = custom; Layout(item);
        item.ClearValue(Control.TemplateProperty); Layout(item);
        var current = Assert.IsType<ContentPresenter>(item.FindName("PART_ContentPresenter"));
        Assert.NotSame(old, current);
        Assert.Same(content, current.Content);
        Assert.Same(current, content.VisualParent);
    }

    [Fact]
    public void ScalarFormattingAndDataTemplatesUseTheStandardContentPipeline()
    {
        var item = Create(123); item.ContentStringFormat = "Value: {0}"; Layout(item);
        var presenter = Assert.IsType<ContentPresenter>(item.FindName("PART_ContentPresenter"));
        Assert.Equal("Value: 123", Assert.IsType<TextBlock>(presenter.GetVisualChild(0)).Text);
        var data = new object(); var template = new DataTemplate();
        template.SetVisualTree(() => new Border { Width = 50, Height = 18 });
        item.ContentTemplate = template; item.Content = data; Layout(item);
        Assert.Same(data, Assert.IsType<Border>(presenter.GetVisualChild(0)).DataContext);
    }

    [Fact]
    public void SeparatorAndDisabledStatesRemainSemanticAndReplaceable()
    {
        var item = Create("Ready"); Layout(item);
        Assert.Equal(Visibility.Collapsed, item.SeparatorVisibility);
        var property = (DependencyProperty)typeof(StatusBarItem).GetField("SeparatorProperty", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        item.SetValue(property, true); Layout(item);
        Assert.Equal(Visibility.Visible, item.SeparatorVisibility);
        Assert.Throws<InvalidOperationException>(() => item.SetValue(StatusBarItem.SeparatorVisibilityProperty, Visibility.Collapsed));
        item.IsEnabled = false;
        Assert.Equal(.5, Assert.IsType<Border>(item.GetVisualChild(0)).Opacity);
        item.IsEnabled = true;
        Assert.Equal(1, Assert.IsType<Border>(item.GetVisualChild(0)).Opacity);
    }

    [Fact]
    public void ThemedCssBoxesReserveBorderAndPaddingForActualContent()
    {
        var child = new Border { Width = 30, Height = 12 };
        var item = Create(child);
        Css.SetStyle(item, "border:8px solid red;padding:4px 6px");
        Layout(item);
        var point = child.TransformToAncestor(item);
        Assert.Equal(14, point.X);
        Assert.True(point.Y >= 12);
        var bar = new StatusBar { Height = double.NaN };
        bar.Resources.MergedDictionaries.Add(Defaults.Value);
        bar.Items.Add(item);
        Css.SetStyle(bar, "border:8px solid red;padding:4px 6px");
        Layout(bar);
        var presenter = Assert.IsType<ItemsPresenter>(bar.FindName("PART_ItemsPresenter"));
        Assert.Equal(new Point(14, 12), presenter.TransformToAncestor(bar));
        Assert.Equal(332, presenter.RenderSize.Width);
        Assert.Equal(26, presenter.RenderSize.Height);
    }

    [Fact]
    public void ContentBindingSurvivesReplacementAndRetiredPresenterStopsUpdating()
    {
        var source = new TextBlock { Text = "Ready" };
        var item = Create("Initial");
        BindingOperations.SetBinding(item, ContentControl.ContentProperty,
            new Binding(nameof(TextBlock.Text)) { Source = source });
        Layout(item);
        var old = Assert.IsType<ContentPresenter>(item.FindName("PART_ContentPresenter"));
        var custom = new ControlTemplate(typeof(StatusBarItem));
        custom.SetVisualTree(() => new Border { Height = 20 });
        item.Template = custom; Layout(item);
        var retiredValue = old.Content;
        source.Text = "Updated";
        Assert.Equal(retiredValue, old.Content);
        item.ClearValue(Control.TemplateProperty); Layout(item);
        Assert.Equal("Updated", Assert.IsType<ContentPresenter>(item.FindName("PART_ContentPresenter")).Content);
        Assert.NotNull(BindingOperations.GetBindingExpression(item, ContentControl.ContentProperty));
    }

    private static StatusBarItem Create(object content)
    {
        var item = new StatusBarItem { Content = content };
        item.Resources.MergedDictionaries.Add(Defaults.Value);
        return item;
    }

    private static void Layout(FrameworkElement element)
    {
        element.InvalidateMeasure(); element.Measure(new Size(360, 50));
        element.Arrange(new Rect(0, 0, 360, 50)); element.UpdateLayout();
    }
}
