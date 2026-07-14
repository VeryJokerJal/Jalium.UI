using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Markup;
using CanonicalBrushValueSerializer = Jalium.UI.Media.Converters.BrushValueSerializer;
using CanonicalGeometryValueSerializer = Jalium.UI.Media.Converters.GeometryValueSerializer;
using CanonicalTransformValueSerializer = Jalium.UI.Media.Converters.TransformValueSerializer;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers the value-serializer infrastructure that DevTools uses to make
/// <c>CanSerializeToString</c> property types editable via their string form (issue #140).
/// </summary>
public class DevToolsValueSerializerTests
{
    [Fact]
    public void GetSerializerFor_ReturnsSerializer_ForKnownTypes()
    {
        Assert.IsType<ImageSourceValueSerializer>(ValueSerializer.GetSerializerFor(typeof(ImageSource)));
        Assert.IsType<FontFamilyValueSerializer>(ValueSerializer.GetSerializerFor(typeof(FontFamily)));
        Assert.IsType<CanonicalBrushValueSerializer>(ValueSerializer.GetSerializerFor(typeof(Brush)));
        Assert.IsType<CanonicalBrushValueSerializer>(ValueSerializer.GetSerializerFor(typeof(SolidColorBrush)));
        Assert.IsType<CanonicalGeometryValueSerializer>(ValueSerializer.GetSerializerFor(typeof(Geometry)));
        Assert.IsType<CanonicalTransformValueSerializer>(ValueSerializer.GetSerializerFor(typeof(Transform)));
    }

    [Fact]
    public void GetSerializerFor_UsesCanonicalTypeConverterFallback()
    {
        Assert.NotNull(ValueSerializer.GetSerializerFor(typeof(int)));
        Assert.NotNull(ValueSerializer.GetSerializerFor(typeof(string)));
        Assert.Null(ValueSerializer.GetSerializerFor((Type?)null));
    }

    [Fact]
    public void GeometrySerializer_RoundTripsPathMarkup()
    {
        var serializer = ValueSerializer.GetSerializerFor(typeof(Geometry))!;
        var geometry = Geometry.Parse("M 0,0 L 10,0 L 10,10 L 0,10 Z");

        var text = serializer.ConvertToString(geometry, null);
        var roundTrip = Assert.IsAssignableFrom<Geometry>(serializer.ConvertFromString(text, null));

        Assert.Equal(geometry.Bounds, roundTrip.Bounds);
    }

    [Fact]
    public void FontFamilySerializer_RoundTrips()
    {
        var serializer = ValueSerializer.GetSerializerFor(typeof(FontFamily))!;
        var family = new FontFamily("Segoe UI");

        Assert.True(serializer.CanConvertToString(family, null));
        var text = serializer.ConvertToString(family, null);
        Assert.Equal("Segoe UI", text);

        var roundTrip = Assert.IsType<FontFamily>(serializer.ConvertFromString(text, null));
        Assert.Equal("Segoe UI", roundTrip.Source);
    }

    [Fact]
    public void BrushSerializer_RoundTrips()
    {
        var serializer = ValueSerializer.GetSerializerFor(typeof(Brush))!;
        var brush = new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x99));

        Assert.True(serializer.CanConvertToString(brush, null));
        var text = serializer.ConvertToString(brush, null);

        var roundTrip = Assert.IsType<SolidColorBrush>(serializer.ConvertFromString(text, null));
        Assert.Equal(brush.Color, roundTrip.Color);
    }

    [Fact]
    public void TransformSerializer_RoundTrips()
    {
        var serializer = ValueSerializer.GetSerializerFor(typeof(Transform))!;
        var transform = new MatrixTransform(new Matrix(1, 0, 0, 1, 12, 24));

        Assert.True(serializer.CanConvertToString(transform, null));
        var text = serializer.ConvertToString(transform, null);

        var roundTrip = Assert.IsType<MatrixTransform>(serializer.ConvertFromString(text, null));
        Assert.Equal(12, roundTrip.Value.OffsetX);
        Assert.Equal(24, roundTrip.Value.OffsetY);
    }

    [Fact]
    public void FontDialog_SeedAndReadProperties()
    {
        // The DevTools font button seeds the dialog from the target's font properties.
        var dialog = new FontDialog
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            FontStyle = FontStyles.Italic
        };

        Assert.Equal("Consolas", dialog.FontFamily?.Source);
        Assert.Equal(18, dialog.FontSize);
        Assert.Equal(FontWeights.Bold, dialog.FontWeight);
        Assert.Equal(FontStyles.Italic, dialog.FontStyle);
    }
}
