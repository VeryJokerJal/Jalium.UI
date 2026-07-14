using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class DependencyPropertyHelperDynamicParityTests
{
    [Fact]
    public void IsTemplatedValueDynamicDistinguishesDynamicTemplateResources()
    {
        Border? dynamicBorder = null;
        Border? literalBorder = null;
        var template = new ControlTemplate(typeof(Control));
        template.SetVisualTree(() =>
        {
            var root = new Grid();
            dynamicBorder = new Border();
            literalBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(1, 2, 3)),
            };
            DynamicResourceBindingOperations.SetDynamicResource(
                dynamicBorder,
                Border.BackgroundProperty,
                "TemplateBrush");
            root.Children.Add(dynamicBorder);
            root.Children.Add(literalBorder);
            return root;
        });

        var control = new Control { Template = template };
        control.Resources["TemplateBrush"] = new SolidColorBrush(Color.FromRgb(4, 5, 6));
        control.ApplyTemplate();

        Assert.NotNull(dynamicBorder);
        Assert.NotNull(literalBorder);
        Assert.True(DependencyPropertyHelper.IsTemplatedValueDynamic(
            dynamicBorder!,
            Border.BackgroundProperty));
        Assert.False(DependencyPropertyHelper.IsTemplatedValueDynamic(
            literalBorder!,
            Border.BackgroundProperty));
    }

    [Fact]
    public void IsTemplatedValueDynamicValidatesArgumentsAndTemplateMembership()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DependencyPropertyHelper.IsTemplatedValueDynamic(null!, Border.BackgroundProperty));
        Assert.Throws<ArgumentNullException>(() =>
            DependencyPropertyHelper.IsTemplatedValueDynamic(new Border(), null!));
        Assert.Throws<ArgumentException>(() =>
            DependencyPropertyHelper.IsTemplatedValueDynamic(new Border(), Border.BackgroundProperty));
    }
}
