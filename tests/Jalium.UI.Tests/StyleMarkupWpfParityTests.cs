using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class StyleMarkupWpfParityTests
{
    [Fact]
    public void StyleImplementsCanonicalMarkupContracts()
    {
        Assert.True(typeof(IAddChild).IsAssignableFrom(typeof(Style)));
        Assert.True(typeof(Jalium.UI.Markup.INameScope).IsAssignableFrom(typeof(Style)));
        Assert.Equal(
            typeof(TriggerCollection),
            typeof(Style).GetProperty(nameof(Style.Triggers))!.PropertyType);

        var style = new Style();
        var addChild = (IAddChild)style;
        var setter = new Setter();
        addChild.AddText(" \r\n");
        addChild.AddChild(setter);

        Assert.Same(setter, Assert.Single(style.Setters));
        Assert.Throws<ArgumentException>(() => addChild.AddChild(new object()));
        Assert.Throws<ArgumentException>(() => addChild.AddText("content"));
    }

    [Fact]
    public void CanonicalNameScopeRegistersFindsAndUnregistersNames()
    {
        var style = new Style();
        var scope = (Jalium.UI.Markup.INameScope)style;
        var value = new object();

        scope.RegisterName("part", value);
        Assert.Same(value, scope.FindName("part"));
        scope.UnregisterName("part");
        Assert.Null(scope.FindName("part"));
    }
}
