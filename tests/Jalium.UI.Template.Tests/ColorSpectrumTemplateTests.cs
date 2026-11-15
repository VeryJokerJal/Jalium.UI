using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class ColorSpectrumTemplateTests
{
    [Fact]
    public void VerticalAndReversedRampsKeepTheThumbAndGradientAlignedWithTheRange()
    {
        var ramp = new ColorRamp
        {
            Style = Assert.IsType<Style>(Defaults.Value[typeof(ColorRamp)]),
            Mode = ColorRampMode.Alpha, Color = Color.Blue, Maximum = 255,
            Orientation = Orientation.Vertical, Width = 24, Height = 200
        };
        Layout(ramp, 24);
        var thumb = Assert.IsType<Grid>(ramp.FindName("PART_Thumb"));
        Assert.Equal(16, thumb.ActualHeight);
        Assert.InRange(thumb.TransformToAncestor(ramp).Y, 183, 185);
        var gradient = Assert.IsType<LinearGradientBrush>(ramp.RampBrush);
        Assert.Equal(new Point(.5, 1), gradient.StartPoint);
        Assert.Equal(new Point(.5, 0), gradient.EndPoint);
        ramp.Value = 255;
        Layout(ramp, 24);
        Assert.InRange(thumb.TransformToAncestor(ramp).Y, 0, 1);
        ramp.IsDirectionReversed = true;
        Layout(ramp, 24);
        Assert.Same(gradient, ramp.RampBrush);
        Assert.Equal(new Point(.5, 0), gradient.StartPoint);
        Assert.InRange(thumb.TransformToAncestor(ramp).Y, 183, 185);
        ramp.Mode = ColorRampMode.Hue;
        var hue = Assert.IsType<LinearGradientBrush>(ramp.RampBrush);
        Assert.True(hue.IsFrozen);
        Assert.Equal(new Point(.5, 0), hue.StartPoint);
    }

    [Fact]
    public void ValueAutomationPreservesBindingsAndDisabledStateAndPageKeysAdjustHue()
    {
        var spectrum = Create();
        var source = new ColorSpectrum();
        spectrum.SetBinding(ColorSpectrum.HueProperty, new Jalium.UI.Data.Binding(nameof(ColorSpectrum.Hue)) { Source = source, Mode = Jalium.UI.Data.BindingMode.TwoWay });
        var value = Assert.IsAssignableFrom<Jalium.UI.Automation.Provider.IValueProvider>(spectrum.GetAutomationPeer());
        value.SetValue("#0000FF");
        Assert.Equal(Color.Blue, spectrum.SelectedColor);
        Assert.Equal(240, source.Hue);
        spectrum.RaiseEvent(KeyArgs(spectrum, Key.PageUp));
        Assert.Equal(241, source.Hue);
        Assert.Throws<ArgumentException>(() => value.SetValue("#invalid"));
        spectrum.IsEnabled = false;
        Assert.True(value.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => value.SetValue("#FF0000"));
    }

    [Fact]
    public void ColorRampReusesSliderRangeAndMutableAlphaGradient()
    {
        var ramp = new ColorRamp
        {
            Style = Assert.IsType<Style>(Defaults.Value[typeof(ColorRamp)]),
            Mode = ColorRampMode.Alpha,
            Maximum = 255,
            Color = Color.Red
        };
        Layout(ramp);
        Assert.IsType<Grid>(ramp.FindName("PART_Thumb"));
        var gradient = Assert.IsType<LinearGradientBrush>(ramp.RampBrush);
        Assert.Equal(Color.FromArgb(0, 255, 0, 0), gradient.GradientStops[0].Color);
        Assert.Equal(Color.Red, gradient.GradientStops[1].Color);
        ramp.Color = Color.Blue;
        Assert.Same(gradient, ramp.RampBrush);
        Assert.Equal(Color.Blue, gradient.GradientStops[1].Color);
        var range = Assert.IsAssignableFrom<Jalium.UI.Automation.Provider.IRangeValueProvider>(ramp.GetAutomationPeer());
        range.SetValue(120);
        Assert.Equal(120, ramp.Value);
        ramp.Mode = ColorRampMode.Hue;
        Assert.Equal(7, Assert.IsType<LinearGradientBrush>(ramp.RampBrush).GradientStops.Count);
    }

    [Fact]
    public void ColorPreviewProjectsItsColorWithoutAnInteractiveDrawingLeaf()
    {
        var preview = new ColorPreview
        {
            Style = Assert.IsType<Style>(Defaults.Value[typeof(ColorPreview)]),
            Color = Color.FromArgb(128, 11, 22, 33)
        };
        Layout(preview, 40);
        var surface = Assert.IsAssignableFrom<Control>(preview.FindName("PART_Surface"));
        Assert.Equal(preview.Color, surface.GetType().GetProperty("Color")!.GetValue(surface));
        Assert.False(surface.Focusable);
        Assert.False(surface.IsHitTestVisible);
        Assert.Null(surface.GetAutomationPeer());
    }

    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    [Fact]
    public void DefaultTemplateBindsTheDrawingSurfaceAndExposesStates()
    {
        var spectrum = Create();
        Assert.IsAssignableFrom<Control>(spectrum.FindName("PART_Surface"));
        Assert.Equal("Box", VisualStateManager.GetCurrentStateName(spectrum, "ShapeStates"));
        spectrum.Shape = ColorSpectrumShape.Ring;
        Assert.Equal("Ring", VisualStateManager.GetCurrentStateName(spectrum, "ShapeStates"));
        spectrum.IsEnabled = false;
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(spectrum, "CommonStates"));
        Assert.Equal(.56, Assert.IsType<Border>(spectrum.FindName("PART_Root")).Opacity);
        spectrum.IsEnabled = true;
        Assert.Equal(1, Assert.IsType<Border>(spectrum.FindName("PART_Root")).Opacity);
    }

    [Fact]
    public void ArrowInputChangesBothAxesAndDisabledInputIsIgnored()
    {
        var spectrum = Create();
        spectrum.Saturation = .5;
        spectrum.Value = .5;
        spectrum.RaiseEvent(KeyArgs(spectrum, Key.Right));
        spectrum.RaiseEvent(KeyArgs(spectrum, Key.Up));
        Assert.Equal(.51, spectrum.Saturation, 8);
        Assert.Equal(.51, spectrum.Value, 8);
        spectrum.IsEnabled = false;
        spectrum.RaiseEvent(KeyArgs(spectrum, Key.Left));
        Assert.Equal(.51, spectrum.Saturation, 8);
    }

    [Fact]
    public void ConsumerTemplateCanOmitTheSurfaceWithoutLosingColorState()
    {
        var spectrum = Create();
        spectrum.Hue = 240;
        spectrum.Saturation = 1;
        spectrum.Value = 1;
        Assert.Equal(Color.Blue, spectrum.SelectedColor);
        var minimal = new ControlTemplate(typeof(ColorSpectrum));
        minimal.SetVisualTree(() => new Border());
        spectrum.Template = minimal;
        Layout(spectrum);
        Assert.Null(spectrum.FindName("PART_Surface"));
        spectrum.RaiseEvent(KeyArgs(spectrum, Key.Home));
        Assert.Equal(Color.White, spectrum.SelectedColor);
        spectrum.RaiseEvent(KeyArgs(spectrum, Key.End));
        Assert.Equal(Color.Black, spectrum.SelectedColor);
        spectrum.Hue = double.NaN;
        spectrum.Saturation = 5;
        spectrum.Value = -1;
        Assert.Equal(0, spectrum.Hue);
        Assert.Equal(1, spectrum.Saturation);
        Assert.Equal(0, spectrum.Value);
    }

    [Fact]
    public void NarrowMeasurementsKeepTheSurfaceInsideItsAvailableWidth()
    {
        var spectrum = Create();
        Layout(spectrum, 80);
        var surface = Assert.IsAssignableFrom<FrameworkElement>(spectrum.FindName("PART_Surface"));
        Assert.True(spectrum.DesiredSize.Width <= 80);
        Assert.True(surface.ActualWidth <= 80);
        Assert.True(surface.ActualHeight > 0);
    }

    private static ColorSpectrum Create()
    {
        var spectrum = new ColorSpectrum
        {
            Style = Assert.IsType<Style>(Defaults.Value[typeof(ColorSpectrum)])
        };
        Layout(spectrum);
        return spectrum;
    }

    private static void Layout(UIElement element, double width = 202)
    {
        element.Measure(new Size(width, 220));
        element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height));
        element.UpdateLayout();
    }

    private static KeyEventArgs KeyArgs(UIElement target, Key key) => new(Keyboard.PrimaryDevice, new Source { RootVisual = target }, Environment.TickCount, key)
    {
        RoutedEvent = UIElement.KeyDownEvent,
        Source = target
    };
    private sealed class Source : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;

        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
}
