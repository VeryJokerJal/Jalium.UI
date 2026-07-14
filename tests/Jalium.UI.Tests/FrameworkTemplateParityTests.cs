using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class FrameworkTemplateParityTests
{
    [Fact]
    public void FrameworkTemplateAndDataTemplateExposeWpfInheritanceContract()
    {
        Assert.True(typeof(FrameworkTemplate).IsAbstract);
        Assert.Equal(typeof(DispatcherObject), typeof(FrameworkTemplate).BaseType);
        Assert.Contains(typeof(Jalium.UI.Markup.INameScope), typeof(FrameworkTemplate).GetInterfaces());
        Assert.Contains(typeof(IQueryAmbient), typeof(FrameworkTemplate).GetInterfaces());
        Assert.Equal(typeof(FrameworkTemplate), typeof(DataTemplate).BaseType);

        var validate = typeof(DataTemplate).GetMethod(
            "ValidateTemplatedParent",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(validate);
        Assert.True(validate!.IsFamily);
        Assert.Equal(typeof(FrameworkTemplate), validate.GetBaseDefinition().DeclaringType);
        Assert.Equal(typeof(DependencyObject), typeof(FrameworkTemplate).GetMethod(nameof(FrameworkTemplate.LoadContent))!.ReturnType);
    }

    [Fact]
    public void DataTemplateFactoryLoadsIndependentRootsAndFindsAppliedNames()
    {
        var template = new DataTemplate();
        template.SetVisualTree(() => new Border { Name = "PART_Root" });

        var first = Assert.IsType<Border>(template.LoadContent());
        var second = Assert.IsType<Border>(template.LoadContent());
        Assert.NotSame(first, second);
        Assert.True(template.HasContent);

        var presenter = new ContentPresenter { ContentTemplate = template, Content = new object() };
        Assert.Equal("PART_Root", Assert.IsType<Border>(template.FindName("PART_Root", presenter)).Name);
    }

    [Fact]
    public void DataTemplateValidatesContentPresenterParentAndHonorsSealing()
    {
        var template = new ProbeDataTemplate();
        template.Validate(new ContentPresenter());
        Assert.Throws<ArgumentNullException>(() => template.Validate(null!));
        Assert.Throws<ArgumentException>(() => template.Validate(new Border()));

        var resources = template.Resources;
        template.Seal();
        Assert.True(template.IsSealed);
        Assert.Same(resources, template.Resources);
        Assert.Throws<InvalidOperationException>(() => template.DataType = typeof(string));
        Assert.Throws<InvalidOperationException>(() => template.Resources = new ResourceDictionary());
        Assert.Throws<InvalidOperationException>(() => template.SetVisualTree(() => new Border()));
    }

    [Fact]
    public void FrameworkTemplateProvidesRealDeferredContentAndDefinitionNameScope()
    {
        var template = new ProbeFrameworkTemplate();
        template.Template = new TemplateContent(() => new Border());
        Assert.IsType<Border>(template.LoadContent());

        var marker = new object();
        template.RegisterName("marker", marker);
        Assert.Same(marker, ((Jalium.UI.Markup.INameScope)template).FindName("marker"));
        template.UnregisterName("marker");
        Assert.Null(((Jalium.UI.Markup.INameScope)template).FindName("marker"));
    }

    private sealed class ProbeDataTemplate : DataTemplate
    {
        public void Validate(FrameworkElement parent) => ValidateTemplatedParent(parent);
    }

    private sealed class ProbeFrameworkTemplate : FrameworkTemplate
    {
    }
}
