using Jalium.UI.Controls;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// Pins the CSS layers' position in the dependency-property precedence chain:
/// Local &gt; ParentTemplateTrigger &gt; ParentTemplate &gt; CssState &gt; StyleTrigger
/// &gt; TemplateTrigger &gt; CssBase &gt; StyleSetter &gt; Current.
/// </summary>
public sealed class CssLayerPrecedenceTests
{
    [Fact]
    public void CssBase_OverridesStyleSetter()
    {
        var border = new Border();
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(UIElement.OpacityProperty, 0.3));
        border.Style = style;
        Assert.Equal(0.3, border.Opacity);

        Css.SetStyle(border, "opacity: .5");
        Assert.Equal(0.5, border.Opacity);
    }

    [Fact]
    public void LocalValue_OverridesCssBase()
    {
        var border = new Border();
        Css.SetStyle(border, "opacity: .5");
        border.Opacity = 0.9;
        Assert.Equal(0.9, border.Opacity);

        // Clearing the local value falls back to the CSS value, not the default.
        border.ClearValue(UIElement.OpacityProperty);
        Assert.Equal(0.5, border.Opacity);
    }

    [Fact]
    public void StyleTrigger_OverridesCssBase()
    {
        var border = new Border();
        Css.SetStyle(border, "opacity: .5");
        border.SetLayerValue(UIElement.OpacityProperty, 0.8, DependencyObject.LayerValueSource.StyleTrigger);
        Assert.Equal(0.8, border.Opacity);

        border.ClearLayerValue(UIElement.OpacityProperty, DependencyObject.LayerValueSource.StyleTrigger);
        Assert.Equal(0.5, border.Opacity);
    }

    [Fact]
    public void CssState_OverridesStyleTriggerButNotLocal()
    {
        var border = new Border();
        border.SetLayerValue(UIElement.OpacityProperty, 0.8, DependencyObject.LayerValueSource.StyleTrigger);
        border.SetLayerValue(UIElement.OpacityProperty, 0.6, DependencyObject.LayerValueSource.CssState);
        Assert.Equal(0.6, border.Opacity);

        border.Opacity = 0.95;
        Assert.Equal(0.95, border.Opacity);

        border.ClearValue(UIElement.OpacityProperty);
        Assert.Equal(0.6, border.Opacity);
    }

    [Fact]
    public void TemplateTrigger_OverridesCssBase()
    {
        var border = new Border();
        border.SetLayerValue(UIElement.OpacityProperty, 0.5, DependencyObject.LayerValueSource.CssBase);
        border.SetLayerValue(UIElement.OpacityProperty, 0.7, DependencyObject.LayerValueSource.TemplateTrigger);
        Assert.Equal(0.7, border.Opacity);
    }

    [Fact]
    public void CssState_OverridesCssBase()
    {
        var border = new Border();
        border.SetLayerValue(UIElement.OpacityProperty, 0.5, DependencyObject.LayerValueSource.CssBase);
        border.SetLayerValue(UIElement.OpacityProperty, 0.6, DependencyObject.LayerValueSource.CssState);
        Assert.Equal(0.6, border.Opacity);

        border.ClearLayerValue(UIElement.OpacityProperty, DependencyObject.LayerValueSource.CssState);
        Assert.Equal(0.5, border.Opacity);
    }

    [Fact]
    public void RemovingCssBase_FallsBackToStyleSetter()
    {
        var border = new Border();
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(UIElement.OpacityProperty, 0.3));
        border.Style = style;
        Css.SetStyle(border, "opacity: .5");
        Assert.Equal(0.5, border.Opacity);

        Css.SetStyle(border, string.Empty);
        Assert.Equal(0.3, border.Opacity);
    }

    [Fact]
    public void BaseValueSource_ReportsClosestWpfAnalogue()
    {
        var border = new Border();
        Css.SetStyle(border, "opacity: .5");
        Assert.Equal(BaseValueSource.Style, DependencyPropertyHelper.GetValueSource(border, UIElement.OpacityProperty).BaseValueSource);

        border.SetLayerValue(UIElement.OpacityProperty, 0.6, DependencyObject.LayerValueSource.CssState);
        Assert.Equal(BaseValueSource.StyleTrigger, DependencyPropertyHelper.GetValueSource(border, UIElement.OpacityProperty).BaseValueSource);
    }

    [Fact]
    public void SetCurrentValue_RoutesIntoOwningCssLayer()
    {
        var border = new Border();
        Css.SetStyle(border, "opacity: .5");

        border.SetCurrentValue(UIElement.OpacityProperty, 0.8);
        Assert.Equal(0.8, border.Opacity);

        // The write must have landed in the CSS layer, not local/StyleSetter: clearing the
        // inline style removes the whole layer and the value returns to the default.
        Css.SetStyle(border, string.Empty);
        Assert.Equal(1.0, border.Opacity);
    }

    [Fact]
    public void SetCurrentValue_IsOverwrittenByCssReevaluation()
    {
        var border = new Border();
        Css.SetStyle(border, "opacity: .5");
        border.SetCurrentValue(UIElement.OpacityProperty, 0.8);
        Assert.Equal(0.8, border.Opacity);

        Css.SetStyle(border, "opacity: .3");
        Assert.Equal(0.3, border.Opacity);
    }

    [Fact]
    public void SetCurrentValue_OnCssStateLayer_WritesBackToCssState()
    {
        var border = new Border();
        border.SetLayerValue(UIElement.OpacityProperty, 0.6, DependencyObject.LayerValueSource.CssState);
        border.SetCurrentValue(UIElement.OpacityProperty, 0.4);
        Assert.Equal(0.4, border.Opacity);

        border.ClearLayerValue(UIElement.OpacityProperty, DependencyObject.LayerValueSource.CssState);
        Assert.Equal(1.0, border.Opacity);
    }

    [Fact]
    public void FullChain_EveryLayerBeatsTheOneBelow()
    {
        var border = new Border();
        border.SetLayerValue(UIElement.OpacityProperty, 0.10, DependencyObject.LayerValueSource.StyleSetter);
        Assert.Equal(0.10, border.Opacity);

        border.SetLayerValue(UIElement.OpacityProperty, 0.20, DependencyObject.LayerValueSource.CssBase);
        Assert.Equal(0.20, border.Opacity);

        border.SetLayerValue(UIElement.OpacityProperty, 0.30, DependencyObject.LayerValueSource.TemplateTrigger);
        Assert.Equal(0.30, border.Opacity);

        border.SetLayerValue(UIElement.OpacityProperty, 0.40, DependencyObject.LayerValueSource.StyleTrigger);
        Assert.Equal(0.40, border.Opacity);

        border.SetLayerValue(UIElement.OpacityProperty, 0.50, DependencyObject.LayerValueSource.CssState);
        Assert.Equal(0.50, border.Opacity);

        border.SetLayerValue(UIElement.OpacityProperty, 0.60, DependencyObject.LayerValueSource.ParentTemplate);
        Assert.Equal(0.60, border.Opacity);

        border.SetLayerValue(UIElement.OpacityProperty, 0.70, DependencyObject.LayerValueSource.ParentTemplateTrigger);
        Assert.Equal(0.70, border.Opacity);

        border.Opacity = 0.80;
        Assert.Equal(0.80, border.Opacity);

        // Unwind top-down and confirm each fallback step.
        border.ClearValue(UIElement.OpacityProperty);
        Assert.Equal(0.70, border.Opacity);
        border.ClearLayerValue(UIElement.OpacityProperty, DependencyObject.LayerValueSource.ParentTemplateTrigger);
        Assert.Equal(0.60, border.Opacity);
        border.ClearLayerValue(UIElement.OpacityProperty, DependencyObject.LayerValueSource.ParentTemplate);
        Assert.Equal(0.50, border.Opacity);
        border.ClearLayerValue(UIElement.OpacityProperty, DependencyObject.LayerValueSource.CssState);
        Assert.Equal(0.40, border.Opacity);
        border.ClearLayerValue(UIElement.OpacityProperty, DependencyObject.LayerValueSource.StyleTrigger);
        Assert.Equal(0.30, border.Opacity);
        border.ClearLayerValue(UIElement.OpacityProperty, DependencyObject.LayerValueSource.TemplateTrigger);
        Assert.Equal(0.20, border.Opacity);
        border.ClearLayerValue(UIElement.OpacityProperty, DependencyObject.LayerValueSource.CssBase);
        Assert.Equal(0.10, border.Opacity);
        border.ClearLayerValue(UIElement.OpacityProperty, DependencyObject.LayerValueSource.StyleSetter);
        Assert.Equal(1.0, border.Opacity);
    }
}
