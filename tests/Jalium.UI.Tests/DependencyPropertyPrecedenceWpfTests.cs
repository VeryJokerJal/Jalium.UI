using Jalium.UI;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class DependencyPropertyPrecedenceWpfTests
{
    [Fact]
    public void StyleAndLocal_Precedence_And_ClearValue_ShouldMatchWpfOrder()
    {
        var element = new PrecedenceProbeElement();
        element.Style = new Style(typeof(PrecedenceProbeElement))
        {
            Setters =
            {
                new Setter(PrecedenceProbeElement.TokenProperty, "StyleValue")
            }
        };

        Assert.Equal("StyleValue", element.GetValue(PrecedenceProbeElement.TokenProperty));
        Assert.Equal(BaseValueSource.Style, DependencyPropertyHelper.GetValueSource(element, PrecedenceProbeElement.TokenProperty).BaseValueSource);

        element.SetValue(PrecedenceProbeElement.TokenProperty, "LocalValue");
        Assert.Equal("LocalValue", element.GetValue(PrecedenceProbeElement.TokenProperty));
        Assert.Equal(BaseValueSource.Local, DependencyPropertyHelper.GetValueSource(element, PrecedenceProbeElement.TokenProperty).BaseValueSource);

        element.ClearValue(PrecedenceProbeElement.TokenProperty);
        Assert.Equal("StyleValue", element.GetValue(PrecedenceProbeElement.TokenProperty));
        Assert.Equal(BaseValueSource.Style, DependencyPropertyHelper.GetValueSource(element, PrecedenceProbeElement.TokenProperty).BaseValueSource);
    }

    [Fact]
    public void InheritedProperty_ShouldReportInheritedSource()
    {
        var parent = new StackPanel();
        var child = new PrecedenceProbeElement();
        parent.Children.Add(child);

        parent.SetValue(PrecedenceProbeElement.InheritedTokenProperty, "ParentValue");

        Assert.Equal("ParentValue", child.GetValue(PrecedenceProbeElement.InheritedTokenProperty));
        Assert.Equal(BaseValueSource.Inherited, DependencyPropertyHelper.GetValueSource(child, PrecedenceProbeElement.InheritedTokenProperty).BaseValueSource);
        Assert.False(child.HasLocalValue(PrecedenceProbeElement.InheritedTokenProperty));
    }

    [Fact]
    public void SetCurrentValue_OnDefaultSource_ShouldNotCreateLocalValue()
    {
        var element = new PrecedenceProbeElement();

        element.SetCurrentValue(PrecedenceProbeElement.TokenProperty, "CurrentDefault");

        Assert.Equal("CurrentDefault", element.GetValue(PrecedenceProbeElement.TokenProperty));
        Assert.False(element.HasLocalValue(PrecedenceProbeElement.TokenProperty));
        Assert.Equal(BaseValueSource.Default, DependencyPropertyHelper.GetValueSource(element, PrecedenceProbeElement.TokenProperty).BaseValueSource);
    }

    [Fact]
    public void SetCurrentValue_OnInheritedSource_ShouldKeepInheritedSource()
    {
        var parent = new StackPanel();
        var child = new PrecedenceProbeElement();
        parent.Children.Add(child);

        parent.SetValue(PrecedenceProbeElement.InheritedTokenProperty, "ParentValue");
        child.SetCurrentValue(PrecedenceProbeElement.InheritedTokenProperty, "ChildCurrent");

        Assert.Equal("ChildCurrent", child.GetValue(PrecedenceProbeElement.InheritedTokenProperty));
        Assert.False(child.HasLocalValue(PrecedenceProbeElement.InheritedTokenProperty));
        Assert.Equal(BaseValueSource.Inherited, DependencyPropertyHelper.GetValueSource(child, PrecedenceProbeElement.InheritedTokenProperty).BaseValueSource);
    }

    [Fact]
    public void Coercion_ShouldBeReportedWithoutBreakingBaseSource()
    {
        var element = new PrecedenceProbeElement();
        element.SetValue(PrecedenceProbeElement.BoundedIntProperty, 42);

        var source = DependencyPropertyHelper.GetValueSource(element, PrecedenceProbeElement.BoundedIntProperty);
        Assert.Equal(10, element.GetValue(PrecedenceProbeElement.BoundedIntProperty));
        Assert.Equal(BaseValueSource.Local, source.BaseValueSource);
        Assert.True(source.IsCoerced);
    }

    private sealed class PrecedenceProbeElement : FrameworkElement
    {
        public static readonly DependencyProperty TokenProperty =
            DependencyProperty.Register(
                "Token",
                typeof(string),
                typeof(PrecedenceProbeElement),
                new PropertyMetadata("DefaultValue"));

        public static readonly DependencyProperty InheritedTokenProperty =
            DependencyProperty.Register(
                "InheritedToken",
                typeof(string),
                typeof(PrecedenceProbeElement),
                new PropertyMetadata("InheritedDefault", null, null, true));

        public static readonly DependencyProperty BoundedIntProperty =
            DependencyProperty.Register(
                "BoundedInt",
                typeof(int),
                typeof(PrecedenceProbeElement),
                new PropertyMetadata(0, null, CoerceBoundedInt));

        private static object? CoerceBoundedInt(DependencyObject d, object? value)
        {
            var intValue = value is int i ? i : 0;
            return Math.Clamp(intValue, 0, 10);
        }
    }
}
