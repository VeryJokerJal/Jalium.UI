using Jalium.UI;

namespace Jalium.UI.Tests;

public class StyleTriggerPrecedenceWpfTests
{
    [Fact]
    public void StyleTrigger_ShouldOverrideStyleSetter_AndRestoreOnDeactivate()
    {
        var element = new TriggerProbeElement();
        element.Style = BuildStyle();

        Assert.Equal("Style", element.GetValue(TriggerProbeElement.TokenProperty));
        Assert.Equal(BaseValueSource.Style, DependencyPropertyHelper.GetValueSource(element, TriggerProbeElement.TokenProperty).BaseValueSource);

        element.SetValue(TriggerProbeElement.FlagProperty, true);
        Assert.Equal("Flag", element.GetValue(TriggerProbeElement.TokenProperty));
        Assert.Equal(BaseValueSource.StyleTrigger, DependencyPropertyHelper.GetValueSource(element, TriggerProbeElement.TokenProperty).BaseValueSource);

        element.SetValue(TriggerProbeElement.FlagProperty, false);
        Assert.Equal("Style", element.GetValue(TriggerProbeElement.TokenProperty));
        Assert.Equal(BaseValueSource.Style, DependencyPropertyHelper.GetValueSource(element, TriggerProbeElement.TokenProperty).BaseValueSource);
    }

    [Fact]
    public void MultipleActiveTriggers_OnSameProperty_ShouldRespectActivationAndReapply()
    {
        var element = new TriggerProbeElement();
        element.Style = BuildStyle();

        element.SetValue(TriggerProbeElement.FlagProperty, true);
        Assert.Equal("Flag", element.GetValue(TriggerProbeElement.TokenProperty));

        element.SetValue(TriggerProbeElement.AltFlagProperty, true);
        Assert.Equal("Alt", element.GetValue(TriggerProbeElement.TokenProperty));

        element.SetValue(TriggerProbeElement.AltFlagProperty, false);
        Assert.Equal("Flag", element.GetValue(TriggerProbeElement.TokenProperty));

        element.SetValue(TriggerProbeElement.FlagProperty, false);
        Assert.Equal("Style", element.GetValue(TriggerProbeElement.TokenProperty));
    }

    [Fact]
    public void LocalValue_ShouldOutrankStyleTrigger()
    {
        var element = new TriggerProbeElement();
        element.Style = BuildStyle();
        element.SetValue(TriggerProbeElement.FlagProperty, true);

        element.SetValue(TriggerProbeElement.TokenProperty, "Local");
        Assert.Equal("Local", element.GetValue(TriggerProbeElement.TokenProperty));
        Assert.Equal(BaseValueSource.Local, DependencyPropertyHelper.GetValueSource(element, TriggerProbeElement.TokenProperty).BaseValueSource);

        element.ClearValue(TriggerProbeElement.TokenProperty);
        Assert.Equal("Flag", element.GetValue(TriggerProbeElement.TokenProperty));
        Assert.Equal(BaseValueSource.StyleTrigger, DependencyPropertyHelper.GetValueSource(element, TriggerProbeElement.TokenProperty).BaseValueSource);
    }

    private static Style BuildStyle()
    {
        var style = new Style(typeof(TriggerProbeElement));
        style.Setters.Add(new Setter(TriggerProbeElement.TokenProperty, "Style"));

        var trigger1 = new PropertyTrigger
        {
            Property = TriggerProbeElement.FlagProperty,
            Value = true
        };
        trigger1.Setters.Add(new Setter(TriggerProbeElement.TokenProperty, "Flag"));
        style.Triggers.Add(trigger1);

        var trigger2 = new PropertyTrigger
        {
            Property = TriggerProbeElement.AltFlagProperty,
            Value = true
        };
        trigger2.Setters.Add(new Setter(TriggerProbeElement.TokenProperty, "Alt"));
        style.Triggers.Add(trigger2);

        return style;
    }

    private sealed class TriggerProbeElement : FrameworkElement
    {
        public static readonly DependencyProperty TokenProperty =
            DependencyProperty.Register(
                "Token",
                typeof(string),
                typeof(TriggerProbeElement),
                new PropertyMetadata("Default"));

        public static readonly DependencyProperty FlagProperty =
            DependencyProperty.Register(
                "Flag",
                typeof(bool),
                typeof(TriggerProbeElement),
                new PropertyMetadata(false));

        public static readonly DependencyProperty AltFlagProperty =
            DependencyProperty.Register(
                "AltFlag",
                typeof(bool),
                typeof(TriggerProbeElement),
                new PropertyMetadata(false));
    }
}
