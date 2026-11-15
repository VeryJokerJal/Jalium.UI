using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssTemplateRadiusTests
{
    private static Control Owner(string css, Action<Border>? configure = null)
    {
        var owner = new Control();
        var template = new ControlTemplate(typeof(Control));
        template.SetVisualTree(() =>
        {
            var chrome = new Border { Name = "Chrome", Background = Brushes.Lime };
            chrome.SetTemplateBinding(Border.CornerRadiusProperty, Control.CornerRadiusProperty);
            configure?.Invoke(chrome);
            return chrome;
        });
        owner.Template = template;
        Css.SetStyle(owner, css);
        CssBorderRadiusTests.Layout(owner, 200, 100);
        return owner;
    }

    private static Border Chrome(Control owner) => Assert.IsType<Border>(owner.GetVisualChild(0));

    [Fact]
    public void TypedTemplateBinding_ForwardsBothAxesAndPreservesTheExpression()
    {
        var owner = Owner("border-radius:50%"); var chrome = Chrome(owner);
        var expression = BindingOperations.GetBindingExpressionBase(chrome, Border.CornerRadiusProperty);
        Assert.NotNull(expression);
        Assert.Equal(new Size(100, 50), CssBorderRadiusTests.Used(chrome).TopLeft);
        Assert.False(chrome.HasLocalValue(Border.CornerRadiusProperty));
        Assert.Equal(BaseValueSource.ParentTemplate, DependencyPropertyHelper.GetValueSource(chrome, Border.CornerRadiusProperty).BaseValueSource);
        Css.SetStyle(owner, "border-radius:40px / 12px");
        Assert.Equal(new Size(40, 12), CssBorderRadiusTests.Used(chrome).TopLeft);
        Css.SetStyle(owner, "border-radius:40px / 24px");
        Assert.Equal(new Size(40, 24), CssBorderRadiusTests.Used(chrome).TopLeft);
        Assert.Same(expression, BindingOperations.GetBindingExpressionBase(chrome, Border.CornerRadiusProperty));
        Assert.Null(owner.HitTest(new Point(1, 1)));
    }

    [Fact]
    public void HeightOnlyResize_RepaintsChromeEvenWhenItsSizeAndHorizontalRadiusStayFixed()
    {
        var owner = Owner("border-radius:50%", chrome => { chrome.Width = 150; chrome.Height = 60; });
        var chrome = Chrome(owner);
        Assert.Equal(new Size(60, 30), CssBorderRadiusTests.Used(chrome).TopLeft);
        chrome.ClearRenderDirty();
        CssBorderRadiusTests.Layout(owner, 200, 200);
        Assert.Equal(new Size(150, 60), chrome.RenderSize);
        Assert.Equal(new CornerRadius(100), chrome.CornerRadius);
        Assert.Equal(new Size(30, 30), CssBorderRadiusTests.Used(chrome).TopLeft);
        Assert.True(chrome.IsRenderDirty);
    }

    [Fact]
    public void NativeSourceValueAndAnimation_TakeOverEvenAtTheSameHorizontalValue()
    {
        var owner = Owner("border-radius:50%"); var chrome = Chrome(owner);
        var projection = owner.CornerRadius;
        owner.CornerRadius = projection;
        Assert.Null(CssBorderRadiusProperties.Get(chrome));
        Assert.Equal(projection, chrome.CornerRadius);
        owner.ClearValue(Control.CornerRadiusProperty);
        Assert.Equal(new Size(100, 50), CssBorderRadiusTests.Used(chrome).TopLeft);
        owner.SetAnimatedValue(Control.CornerRadiusProperty, projection, false);
        Assert.Null(CssBorderRadiusProperties.Get(chrome));
        owner.ClearAnimatedValue(Control.CornerRadiusProperty);
        Assert.Equal(new Size(100, 50), CssBorderRadiusTests.Used(chrome).TopLeft);
    }

    [Fact]
    public void NativeTargetValuesAndTemplateTriggers_OutrankForwardedCss()
    {
        var owner = Owner("border-radius:50%"); var chrome = Chrome(owner);
        chrome.SetLayerValue(Border.CornerRadiusProperty, new CornerRadius(7), DependencyObject.LayerValueSource.ParentTemplateTrigger, false);
        Assert.Null(CssBorderRadiusProperties.Get(chrome));
        Assert.Equal(new CornerRadius(7), chrome.CornerRadius);
        chrome.ClearLayerValue(Border.CornerRadiusProperty, DependencyObject.LayerValueSource.ParentTemplateTrigger, false);
        Assert.Equal(new Size(100, 50), CssBorderRadiusTests.Used(chrome).TopLeft);
        chrome.CornerRadius = chrome.CornerRadius;
        Assert.Null(CssBorderRadiusProperties.Get(chrome));
        chrome.ClearValue(Border.CornerRadiusProperty);
        Assert.NotNull(CssBorderRadiusProperties.Get(chrome));
    }

    [Fact]
    public void NativeCurrentValue_SuppressesStaleCssShapeUntilTheSourceUpdates()
    {
        var owner = Owner("border-radius:30px / 10px"); var chrome = Chrome(owner);
        owner.SetCurrentValue(Control.CornerRadiusProperty, new CornerRadius(7));
        Assert.Null(CssBorderRadiusProperties.Get(owner));
        Assert.Null(CssBorderRadiusProperties.Get(chrome));
        Assert.Equal(new CornerRadius(7), chrome.CornerRadius);
        Css.SetStyle(owner, "border-radius:40px / 12px");
        Assert.Equal(new Size(40, 12), CssBorderRadiusTests.Used(chrome).TopLeft);
        chrome.SetCurrentValue(Border.CornerRadiusProperty, new CornerRadius(9));
        Assert.Null(CssBorderRadiusProperties.Get(chrome));
        Assert.NotNull(BindingOperations.GetBindingExpressionBase(chrome, Border.CornerRadiusProperty));
    }

    [Fact]
    public void SourceStyleTrigger_OverridesCssBaseWithoutLosingItsShapeOnRemoval()
    {
        var owner = Owner("border-radius:50%"); var chrome = Chrome(owner);
        owner.SetLayerValue(Control.CornerRadiusProperty, owner.CornerRadius, DependencyObject.LayerValueSource.StyleTrigger, false);
        Assert.Null(CssBorderRadiusProperties.Get(chrome));
        owner.ClearLayerValue(Control.CornerRadiusProperty, DependencyObject.LayerValueSource.StyleTrigger, false);
        Assert.NotNull(CssBorderRadiusProperties.Get(chrome));
    }

    [Fact]
    public void Overflow_ReachesTheTemplatePaddingEdgeWithoutReplacingClipProperties()
    {
        var owner = Owner("border-radius:50%; overflow:hidden", chrome =>
        {
            chrome.BorderThickness = new(8, 4, 12, 6);
            chrome.Child = new Border { Background = Brushes.Red };
        });
        var chrome = Chrome(owner);
        Assert.False(chrome.ClipToBounds);
        var clip = Assert.IsType<CssRoundedRectangleGeometry>(chrome.GetLayoutClip());
        Assert.Equal(new Rect(8, 4, 180, 90), clip.ClipReferenceRect);
        Assert.Equal(new Size(92, 46), clip.Radii.TopLeft);
        Assert.Null(owner.HitTest(new Point(1, 1)));
        Assert.Same(chrome.Child, owner.HitTest(new Point(100, 50))?.VisualHit);
        var pixels = CssBorderRadiusTests.Pixels(owner, 200, 100);
        Assert.Equal((byte)0, pixels[(1 * 200 + 1) * 4 + 3]);
        Assert.Equal((byte)255, pixels[(50 * 200 + 100) * 4 + 2]);
        chrome.ClipToBounds = false; // an explicit native opt-out remains authoritative
        Assert.Null(chrome.GetLayoutClip());
    }

    [Fact]
    public void RemovingCssAndReplacingTheTemplate_ClearForwardedState()
    {
        var owner = Owner("border-radius:50%; overflow:hidden"); var old = Chrome(owner);
        Css.SetStyle(owner, "");
        Assert.Null(CssBorderRadiusProperties.Get(old));
        Assert.Null(old.GetValue(CssBorderRadiusProperties.TemplateValueProperty));
        Css.SetStyle(owner, "border-radius:40px / 20px");
        Assert.NotNull(CssBorderRadiusProperties.Get(old));
        var replacement = new ControlTemplate(typeof(Control));
        replacement.SetVisualTree(() => new Border());
        owner.Template = replacement;
        CssBorderRadiusTests.Layout(owner, 200, 100);
        Assert.Null(old.GetValue(CssBorderRadiusProperties.TemplateValueProperty));
        Css.SetStyle(owner, "border-radius:60px / 30px");
        Assert.Null(old.GetValue(CssBorderRadiusProperties.TemplateValueProperty));
        Assert.Null(CssBorderRadiusProperties.Get(Chrome(owner)));
    }

    [Fact]
    public void RuntimeXamlTemplateBinding_PreservesTheEllipticalContour()
    {
        var owner = Assert.IsType<Control>(XamlReader.Parse("""
            <Control xmlns="http://schemas.jalium.ui/2024" Css.Style="border-radius:50%">
              <Control.Template>
                <ControlTemplate TargetType="Control">
                  <Border CornerRadius="{TemplateBinding CornerRadius}" Background="Lime" />
                </ControlTemplate>
              </Control.Template>
            </Control>
            """));
        CssBorderRadiusTests.Layout(owner, 200, 100);
        Assert.Equal(new Size(100, 50), CssBorderRadiusTests.Used(Chrome(owner)).TopLeft);
        Css.SetStyle(owner, "border-radius:40px / 12px");
        Assert.Equal(new Size(40, 12), CssBorderRadiusTests.Used(Chrome(owner)).TopLeft);
    }

    [Fact]
    public void BuiltInButtonTemplate_ForwardsCssWhileKeepingNativeBackgroundAndContent()
    {
        ResetTheme();
        var app = new Application();
        try
        {
            var button = new Button { Content = "Native content", Background = Brushes.Lime };
            var host = new Border { Child = button };
            Css.SetStyle(button, "border-radius:50%; overflow:hidden");
            CssBorderRadiusTests.Layout(host, 200, 100);
            var chrome = Assert.IsType<Border>(button.FindName("RootBorder"));
            Assert.Equal(new Size(100, 50), CssBorderRadiusTests.Used(chrome).TopLeft);
            Assert.Same(Brushes.Lime, chrome.Background);
            Assert.Equal("Native content", button.Content);
            Assert.NotNull(chrome.GetLayoutClip());
            Assert.Null(button.HitTest(new Point(1, 1)));
            CssBorderRadiusTests.Layout(host, 120, 80);
            Assert.Equal(new Size(60, 40), CssBorderRadiusTests.Used(chrome).TopLeft);
        }
        finally { ResetTheme(); GC.KeepAlive(app); }
    }

    private static void ResetTheme()
    {
        typeof(Application).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, null);
        typeof(Jalium.UI.Controls.Themes.ThemeManager).GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
    }

    [Fact]
    public void PublicTemplateBindingExtension_PreservesExpressionAndConverterAuthority()
    {
        var owner = CssBorderRadiusTests.Box("border-radius:50%", 200, 100);
        var chrome = new Border(); chrome.SetTemplatedParent(owner);
        var extension = new Jalium.UI.TemplateBindingExtension(Border.CornerRadiusProperty);
        extension.ProvideValue(new BindingTarget(chrome, Border.CornerRadiusProperty));
        CssBorderRadiusTests.Layout(chrome, 200, 100);
        var expression = BindingOperations.GetBindingExpression(chrome, Border.CornerRadiusProperty);
        Assert.NotNull(expression);
        Assert.Equal(new Size(100, 50), CssBorderRadiusTests.Used(chrome).TopLeft);
        Assert.False(chrome.HasLocalValue(Border.CornerRadiusProperty));
        Css.SetStyle(owner, "border-radius:30px / 8px");
        Assert.Equal(new Size(30, 8), CssBorderRadiusTests.Used(chrome).TopLeft);
        Assert.Same(expression, BindingOperations.GetBindingExpression(chrome, Border.CornerRadiusProperty));
        extension.Converter = new RadiusConverter();
        extension.ProvideValue(new BindingTarget(chrome, Border.CornerRadiusProperty));
        Assert.Equal(new CornerRadius(3), chrome.CornerRadius);
        Assert.Null(CssBorderRadiusProperties.Get(chrome));
    }

    [Fact]
    public void TemplateBinding_ReattachesToANewOwner_AndStopsWatchingTheOldOne()
    {
        var first = CssBorderRadiusTests.Box("border-radius:20px / 10px", 100, 80);
        var second = CssBorderRadiusTests.Box("border-radius:30px / 5px", 100, 80);
        var chrome = new Border();
        chrome.SetTemplateBinding(Border.CornerRadiusProperty, Border.CornerRadiusProperty);
        chrome.SetTemplatedParent(first);
        CssBorderRadiusTests.Layout(chrome, 100, 80);
        Assert.Equal(new Size(20, 10), CssBorderRadiusTests.Used(chrome).TopLeft);
        chrome.SetTemplatedParent(second);
        Assert.Equal(new Size(30, 5), CssBorderRadiusTests.Used(chrome).TopLeft);
        Css.SetStyle(first, "border-radius:40px / 30px");
        Assert.Equal(new Size(30, 5), CssBorderRadiusTests.Used(chrome).TopLeft);
        chrome.SetTemplatedParent(null);
        Assert.Null(CssBorderRadiusProperties.Get(chrome));
        Assert.Null(chrome.GetValue(CssBorderRadiusProperties.TemplateValueProperty));
    }

    [Fact]
    public void DiscardedTemplate_DoesNotStayAliveThroughRadiusNotifications()
    {
        var owner = Owner("border-radius:50%");
        var weak = ReplaceTemplate(owner);
        for (var i = 0; i < 3 && weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.False(weak.IsAlive);
        GC.KeepAlive(owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReplaceTemplate(Control owner)
    {
        var weak = new WeakReference(Chrome(owner));
        var replacement = new ControlTemplate(typeof(Control)); replacement.SetVisualTree(() => new Border());
        owner.Template = replacement; CssBorderRadiusTests.Layout(owner, 200, 100);
        return weak;
    }

    private sealed class BindingTarget(DependencyObject target, DependencyProperty property) : IServiceProvider, IProvideValueTarget
    {
        public object TargetObject => target;
        public object TargetProperty => property;
        public object? GetService(Type serviceType) => serviceType == typeof(IProvideValueTarget) ? this : null;
    }

    private sealed class RadiusConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => new CornerRadius(3);
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value;
    }

    [Fact]
    public void CircularCssRadii_UseAxisSpecificInsetsAndNativeClipContours()
    {
        var border = CssBorderRadiusTests.Box("border-radius:30px; border:solid blue; overflow:hidden", 120, 70);
        border.BorderThickness = new Thickness(10, 4, 20, 8);
        var clip = Assert.IsType<CssRoundedRectangleGeometry>(border.GetLayoutClip());
        Assert.Equal(new Size(20, 26), clip.Radii.TopLeft);
        Assert.Equal(new Size(10, 22), clip.Radii.BottomRight);
        Assert.Null(border.HitTest(new Point(1, 1)));
    }
}
