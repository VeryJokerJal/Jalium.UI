using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssAnimationPriorityTests
{
    [Fact]
    public void CssAnimationValueObeysImportantLocalAndNativeAnimationPriority()
    {
        var element = new Border();
        Css.SetStyle(element, "opacity: .2");
        Assert.Equal(.2, element.Opacity);

        Assert.True(element.SetCssAnimatedValue(UIElement.OpacityProperty, .8));
        Assert.Equal(.8, element.Opacity);

        Css.SetStyle(element, "opacity: .3 !important");
        Assert.Equal(.3, element.Opacity);
        Css.SetStyle(element, "opacity: .3");
        Assert.Equal(.8, element.Opacity);

        element.Opacity = .4;
        Assert.Equal(.4, element.Opacity);
        element.ClearValue(UIElement.OpacityProperty);
        Assert.Equal(.8, element.Opacity);

        element.SetAnimatedValue(UIElement.OpacityProperty, .9, holdEndValue: false);
        Assert.Equal(.9, element.Opacity);
        element.ClearAnimatedValue(UIElement.OpacityProperty);
        Assert.Equal(.8, element.Opacity);

        Assert.True(element.ClearCssAnimatedValue(UIElement.OpacityProperty));
        Assert.Equal(.3, element.Opacity);
    }

    [Fact]
    public void ImportantBackgroundColorMasksTheComposedBackgroundProperty()
    {
        var element = new Border();
        Css.SetStyle(element, "background-color: red !important");
        Assert.False(element.SetCssAnimatedValue(Border.BackgroundProperty, Brushes.Blue));
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(element.Background).Color);

        Css.SetStyle(element, "background-color: red");
        Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(element.Background).Color);
        element.ClearCssAnimatedValue(Border.BackgroundProperty);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(element.Background).Color);
    }
}
