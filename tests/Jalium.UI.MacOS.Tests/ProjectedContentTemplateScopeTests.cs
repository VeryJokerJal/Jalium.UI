using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class ProjectedContentTemplateScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PresentedContentKeepsItsOwnContextAndLocalValues(bool dataTemplate)
    {
        var model = new object();
        var content = new Border { Name = "UserContent", Width = 123, DataContext = model };
        var owner = new ContentControl { DataContext = new object() };
        if (dataTemplate)
        {
            var template = new DataTemplate();
            template.SetVisualTree(() => content);
            owner.ContentTemplate = template;
            owner.Content = model;
        }
        else owner.Content = content;

        owner.Template = Template();
        Layout(owner);
        Assert.Same(model, content.DataContext);
        Assert.True(content.HasLocalValue(FrameworkElement.DataContextProperty));
        Assert.Equal(BaseValueSource.Local, DependencyPropertyHelper.GetValueSource(content, FrameworkElement.WidthProperty).BaseValueSource);
        Assert.Null(content.TemplatedParent);
        Assert.Null(owner.FindName("UserContent"));

        owner.DataContext = new object();
        owner.Template = Template();
        Layout(owner);
        Assert.Same(model, content.DataContext);
        Assert.Equal(123, content.Width);
        Assert.Null(content.TemplatedParent);
    }

    [Fact]
    public void LiteralTemplatePartsStillUseTemplatePrecedenceAndNames()
    {
        var owner = new ContentControl { Template = Template() };
        Layout(owner);
        var literal = Assert.IsType<Border>(owner.FindName("PART_Literal"));
        Assert.Same(owner, literal.TemplatedParent);
        Assert.Equal(BaseValueSource.ParentTemplate, DependencyPropertyHelper.GetValueSource(literal, FrameworkElement.WidthProperty).BaseValueSource);
        Assert.False(literal.HasLocalValue(FrameworkElement.WidthProperty));
    }

    private static ControlTemplate Template()
    {
        var template = new ControlTemplate(typeof(ContentControl));
        template.SetVisualTree(() =>
        {
            var grid = new Grid();
            grid.Children.Add(new Border { Name = "PART_Literal", Width = 42 });
            var presenter = new ContentPresenter();
            presenter.SetTemplateBinding(ContentPresenter.ContentProperty, ContentControl.ContentProperty);
            presenter.SetTemplateBinding(ContentPresenter.ContentTemplateProperty, ContentControl.ContentTemplateProperty);
            grid.Children.Add(presenter);
            return grid;
        });
        return template;
    }
    private static void Layout(UIElement root)
    {
        root.Measure(new Size(600, 400));
        root.Arrange(new Rect(0, 0, 600, 400));
    }
}
