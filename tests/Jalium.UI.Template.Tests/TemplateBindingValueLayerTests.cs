using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class TemplateBindingValueLayerTests
{
    public TemplateBindingValueLayerTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Fact]
    public void InitialTemplateBindingOutranksConstructorDefaultsWhenTheOwnerValueDoesNotChange()
    {
        var template=new ControlTemplate(typeof(ContentControl));
        template.SetVisualTree(()=>
        {
            var bar=new Jalium.UI.Controls.Primitives.ScrollBar{Name="Range"};
            bar.SetBinding(Jalium.UI.Controls.Primitives.RangeBase.MaximumProperty,new Jalium.UI.Markup.DeferredTemplateBinding(nameof(ContentControl.Content)));
            return bar;
        });
        var host=new ContentControl{Content=2400.0,Template=template};
        host.ApplyTemplate();
        Assert.Equal(2400,Assert.IsType<Jalium.UI.Controls.Primitives.ScrollBar>(host.FindName("Range")).Maximum);
        var blank=new ControlTemplate(typeof(ContentControl));blank.SetVisualTree(()=>new Border());host.Template=blank;host.ApplyTemplate();host.Template=template;host.ApplyTemplate();
        Assert.Equal(2400,Assert.IsType<Jalium.UI.Controls.Primitives.ScrollBar>(host.FindName("Range")).Maximum);
    }

    [Fact]
    public void RuntimeAttachedTemplateBindingTracksItsOwnerWithoutWritingALocalDefault()
    {
        var template = Assert.IsType<ControlTemplate>(Jalium.UI.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ContentControl">
                <Border Name="Positioned" Grid.Column="{TemplateBinding Content}" />
            </ControlTemplate>
            """));
        var host = new ContentControl { Content = 2, Template = template };
        host.ApplyTemplate();
        var child = Assert.IsType<Border>(host.FindName("Positioned"));
        Assert.Equal(2, Grid.GetColumn(child));
        host.Content = 3;
        Assert.Equal(3, Grid.GetColumn(child));
        Assert.Equal(BaseValueSource.ParentTemplate, DependencyPropertyHelper.GetValueSource(child, Grid.ColumnProperty).BaseValueSource);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void TemplateBindingUpdatesRemainBelowTheActiveVisualState(int kind)
    {
        var item = new DevToolsDrawApiItem { Name = "initial" };
        var host = CreateHost(item, kind);
        var text = Assert.IsType<TextBlock>(host.FindName("Value"));
        Assert.Equal("initial", text.Text);
        VisualStateManager.GoToState(host, "Selected", false);
        item.Name = "updated";
        Assert.Equal("state", text.Text);
        VisualStateManager.GoToState(host, "Normal", false);
        Assert.Equal("updated", text.Text);
        Assert.Equal(BaseValueSource.ParentTemplate, DependencyPropertyHelper.GetValueSource(text, TextBlock.TextProperty).BaseValueSource);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void DeferredTemplateBindingAndUserOverrideKeepTheirOwnPrecedence(int kind)
    {
        var host = CreateHost(null, kind);
        var text = Assert.IsType<TextBlock>(host.FindName("Value"));
        VisualStateManager.GoToState(host, "Selected", false);
        var item = new DevToolsDrawApiItem { Name = "late" };
        host.DataContext = item;
        Assert.Equal("state", text.Text);
        VisualStateManager.GoToState(host, "Normal", false);
        Assert.Equal("late", text.Text);
        text.Text = "local";
        item.Name = "new value";
        Assert.Equal("local", text.Text);
        VisualStateManager.GoToState(host, "Selected", false);
        Assert.Equal("local", text.Text);
        text.ClearValue(TextBlock.TextProperty);
        Assert.Equal("state", text.Text);
        VisualStateManager.GoToState(host, "Normal", false);
        Assert.Equal("", text.Text);
    }

    [Fact]
    public void TemplateTwoWayEditorSupportsRepeatedEditsAndSourceResets()
    {
        var item = new DevToolsDrawApiItem { Name = "committed" };
        var host = CreateHost(item, mode: BindingMode.TwoWay);
        var text = Assert.IsType<TextBlock>(host.FindName("Value"));
        text.Text = "draft";
        Assert.Equal("draft", item.Name);
        item.Name = "committed";
        Assert.Equal("committed", text.Text);
        text.Text = "draft";
        Assert.Equal("draft", item.Name);
        Assert.NotNull(text.GetBindingExpression(TextBlock.TextProperty));
        Assert.Equal(BaseValueSource.ParentTemplate, DependencyPropertyHelper.GetValueSource(text, TextBlock.TextProperty).BaseValueSource);
    }

    [Fact]
    public void ReplacingAPromotedBindingCreatesALocalExpressionAndClearRemovesIt()
    {
        var item = new DevToolsDrawApiItem { Name = "template" };
        var host = CreateHost(item);
        var text = Assert.IsType<TextBlock>(host.FindName("Value"));
        var user = new DevToolsDrawApiItem { Name = "user" };
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(DevToolsDrawApiItem.Name)) { Source = user });
        VisualStateManager.GoToState(host, "Selected", false);
        item.Name = "old changed";
        user.Name = "local update";
        Assert.Equal("local update", text.Text);
        text.ClearBinding(TextBlock.TextProperty);
        Assert.Equal("state", text.Text);
    }

    private static ContentControl CreateHost(DevToolsDrawApiItem? source, int kind = 0, BindingMode mode = BindingMode.OneWay)
    {
        var template = new ControlTemplate(typeof(ContentControl));
        template.SetVisualTree(() =>
        {
            var text = new TextBlock { Name = "Value" };
            var sourceBinding = new Binding(nameof(DevToolsDrawApiItem.Name)) { Source = source, Mode = mode };
            BindingBase binding = kind switch
            {
                1 => new MultiBinding { Bindings = { sourceBinding } },
                2 => new PriorityBinding { Bindings = { sourceBinding } },
                _ => sourceBinding,
            };
            text.SetBinding(TextBlock.TextProperty, binding);
            var root = new Border { Child = text };
            var group = new VisualStateGroup { Name = "States" };
            group.States.Add(new VisualState("Normal"));
            var selected = new VisualState("Selected");
            selected.Setters.Add(new Setter(TextBlock.TextProperty, "state") { TargetName = "Value" });
            group.States.Add(selected);
            VisualStateManager.SetVisualStateGroups(root, new[] { group });
            return root;
        });
        var host = new ContentControl { Template = template };
        host.Measure(new Size(200, 80)); host.Arrange(new Rect(0, 0, 200, 80)); host.UpdateLayout();
        return host;
    }
}
