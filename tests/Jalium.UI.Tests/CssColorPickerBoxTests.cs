using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssColorPickerBoxTests
{
    private static readonly Lazy<ResourceDictionary> Defaults=new(()=>
    {
        ThemeLoader.Initialize();using var stream=new MemoryStream();
        return ThemeManager.XamlLoader!(stream,"Themes/Generic.jalxaml",typeof(Control).Assembly)!;
    });
    public CssColorPickerBoxTests()
    {
        ThemeManager.RegisterControlThemes(new ResourceDictionary { [typeof(ColorSpectrum)]=Defaults.Value[typeof(ColorSpectrum)], [typeof(ColorRamp)]=Defaults.Value[typeof(ColorRamp)], [typeof(ColorPreview)]=Defaults.Value[typeof(ColorPreview)] });
    }
    // These pixel fixtures exercise a deliberately stretched box, independently
    // of the picker's content-sized default alignment.
    private static ColorPicker Picker()=>new() { Style=Assert.IsType<Style>(Defaults.Value[typeof(ColorPicker)]),HorizontalAlignment=HorizontalAlignment.Stretch };

    [Fact]
    public void BackgroundLayers_UseContentBoxAndRoundedContour()
    {
        var picker = EmptyPicker();
        Css.SetStyle(picker,
            "border:8px solid transparent;padding:4px;border-radius:20px;" +
            "background-color:blue;" +
            "background-image:linear-gradient(to right,red,lime);" +
            "background-size:20px 10px;background-position:right bottom;" +
            "background-repeat:no-repeat;background-origin:content-box;" +
            "background-clip:content-box");
        CssBorderRadiusTests.Layout(picker, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(picker, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 30, 30, 0));
        Assert.True(Channel(pixels, 50, 43, 2) > 100);
        Assert.True(Channel(pixels, 65, 43, 1) > 100);
    }

    [Fact]
    public void CssBorder_PaintsDistinctSideColors()
    {
        var picker = EmptyPicker();
        Css.SetStyle(picker, "border:8px solid; border-color:red green blue yellow");
        CssBorderRadiusTests.Layout(picker, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(picker, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 40, 4, 2));
        Assert.Equal((byte)128, Channel(pixels, 76, 30, 1));
        Assert.Equal((byte)255, Channel(pixels, 40, 56, 0));
        Assert.Equal((byte)255, Channel(pixels, 4, 30, 1));
    }

    [Fact]
    public void CssEdges_MoveSpectrumAndDesiredSizeTogether()
    {
        var native = Picker(); native.IsCompact=true; native.Padding=new Thickness(0); native.BorderThickness=new Thickness(0);
        native.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var picker = Picker(); picker.IsCompact=true;
        Css.SetStyle(picker, "border:8px solid transparent;padding:4px");
        picker.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        CssBorderRadiusTests.Layout(picker, 200, 200);
        picker.Render(new NoOpContext());

        var spectrum = SpectrumBounds(picker);
        Assert.Equal(12, spectrum.X);
        Assert.Equal(12, spectrum.Y);
        Assert.Equal(native.DesiredSize.Width + 24, picker.DesiredSize.Width);
        Assert.Equal(native.DesiredSize.Height + 24, picker.DesiredSize.Height);
    }

    [Fact]
    public void NativeBackgroundAndBorderBrush_KeepPriority()
    {
        var picker = EmptyPicker();
        picker.Background = Brushes.Lime;
        picker.BorderBrush = Brushes.Lime;
        Css.SetStyle(picker,
            "background-color:blue;background-clip:content-box;" +
            "border:8px solid; border-color:red blue yellow white;padding:4px");
        CssBorderRadiusTests.Layout(picker, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(picker, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 10, 30, 1));
        Assert.Equal((byte)0, Channel(pixels, 10, 30, 0));
        Assert.Equal((byte)255, Channel(pixels, 40, 4, 1));
        Assert.Equal((byte)0, Channel(pixels, 40, 4, 2));
    }

    [Fact]
    public void CssRadius_ClipsNativeBackground()
    {
        var picker = EmptyPicker();
        picker.Background = Brushes.Blue;
        Css.SetStyle(picker, "border-radius:50%");
        CssBorderRadiusTests.Layout(picker, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(picker, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 1, 1, 3));
        Assert.Equal((byte)255, Channel(pixels, 40, 10, 0));
    }

    [Fact]
    public void RemovingCssEdges_RestoresSpectrumPosition()
    {
        var picker = Picker(); picker.IsCompact=true;
        CssBorderRadiusTests.Layout(picker,200,200);
        var original=SpectrumBounds(picker);
        Css.SetStyle(picker, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(picker, 200, 200);
        picker.Render(new NoOpContext());
        Assert.Equal(12, SpectrumBounds(picker).X);

        Css.SetStyle(picker, string.Empty);
        CssBorderRadiusTests.Layout(picker, 200, 200);
        picker.Render(new NoOpContext());
        Assert.Equal(original.X, SpectrumBounds(picker).X);
    }

    private static ColorPicker EmptyPicker()
    {
        var picker=Picker();picker.IsColorSpectrumVisible=false;picker.IsColorSliderVisible=false;
        picker.IsColorPreviewVisible=false;picker.IsHexInputVisible=false;return picker;
    }
    private static Rect SpectrumBounds(ColorPicker picker)
    {
        var spectrum=Assert.IsType<ColorSpectrum>(picker.FindName("PART_Spectrum"));
        var origin=spectrum.TransformToAncestor(picker);
        return new Rect(origin.X,origin.Y,spectrum.ActualWidth,spectrum.ActualHeight);
    }

    private sealed class NoOpContext : DrawingContextAdapter
    {
        public override void DrawText(FormattedText formattedText, Point origin) { }
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }

    private static byte Channel(byte[] pixels, int x, int y, int channel)
        => pixels[(y * 80 + x) * 4 + channel];
}
