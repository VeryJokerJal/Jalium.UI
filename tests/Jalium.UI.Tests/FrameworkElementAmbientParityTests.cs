using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class FrameworkElementAmbientParityTests
{
    [Fact]
    public void SplashScreenRemainsDerivableLikeWpf()
    {
        Assert.False(typeof(Jalium.UI.SplashScreen).IsSealed);
    }

    [Fact]
    public void FrameworkElementImplementsQueryAmbientWithoutEagerlyCreatingResources()
    {
        var element = new FrameworkElement();
        var ambient = Assert.IsAssignableFrom<IQueryAmbient>(element);

        Assert.False(ambient.IsAmbientPropertyAvailable(nameof(FrameworkElement.Resources)));
        Assert.False(ambient.IsAmbientPropertyAvailable(nameof(FrameworkElement.Style)));

        _ = element.Resources;
        element.Style = new Style(typeof(FrameworkElement));

        Assert.True(ambient.IsAmbientPropertyAvailable(nameof(FrameworkElement.Resources)));
        Assert.True(ambient.IsAmbientPropertyAvailable(nameof(FrameworkElement.Style)));
        Assert.Throws<ArgumentNullException>(() => ambient.IsAmbientPropertyAvailable(null!));
    }
}
