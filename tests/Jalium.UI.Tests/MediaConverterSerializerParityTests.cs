using System.Collections;
using System.Globalization;
using Jalium.UI.Converters;
using Jalium.UI.Media;
using Jalium.UI.Media.Converters;
using MarkupValueSerializer = Jalium.UI.Markup.ValueSerializer;

namespace Jalium.UI.Tests;

public sealed class MediaConverterSerializerParityTests
{
    [Fact]
    public void PrimitiveValueSerializersRoundTrip()
    {
        (MarkupValueSerializer Serializer, object Value)[] values =
        [
            (new PointValueSerializer(), new Point(1, 2)),
            (new VectorValueSerializer(), new Vector(3, 4)),
            (new SizeValueSerializer(), new Size(5, 6)),
            (new RectValueSerializer(), new Rect(1, 2, 3, 4)),
            (new Int32RectValueSerializer(), new Int32Rect(1, 2, 3, 4)),
        ];

        foreach (var (serializer, value) in values)
        {
            string text = serializer.ConvertToString(value, null);
            Assert.Equal(value, serializer.ConvertFromString(text, null));
        }
    }

    [Fact]
    public void IListConvertersUseCompactWpfTextForms()
    {
        Assert.Equal(new[] { true, false, true }, Assert.IsAssignableFrom<IEnumerable<bool>>(
            new BoolIListConverter().ConvertFrom("1 0 1")));
        Assert.Equal("abc", new CharIListConverter().ConvertTo(new[] { 'a', 'b', 'c' }, typeof(string)));
        Assert.Equal(new[] { 1d, 2.5d, 3d }, Assert.IsAssignableFrom<IEnumerable<double>>(
            new DoubleIListConverter().ConvertFrom(null, CultureInfo.InvariantCulture, "1 2.5 3")));
        Assert.Equal(new[] { new Point(1, 2), new Point(3, 4) }, Assert.IsAssignableFrom<IEnumerable<Point>>(
            new PointIListConverter().ConvertFrom("1,2 3,4")));
        Assert.Equal(new ushort[] { 1, 2, 65535 }, Assert.IsAssignableFrom<IEnumerable<ushort>>(
            new UShortIListConverter().ConvertFrom("1 2 65535")));
    }

    [Fact]
    public void MediaSerializersAndVectorCollectionRoundTrip()
    {
        var vectors = VectorCollection.Parse("1,2 3,4");
        Assert.Equal(2, vectors.Count);
        VectorCollection clone = vectors.Clone();
        Assert.NotSame(vectors, clone);
        Assert.Equal(vectors.ToString(CultureInfo.InvariantCulture), clone.ToString(CultureInfo.InvariantCulture));

        var serializer = new VectorCollectionValueSerializer();
        Assert.Equal(vectors.ToString(CultureInfo.InvariantCulture), serializer.ConvertToString(vectors, null));
        Assert.Equal(vectors.Count, Assert.IsType<VectorCollection>(serializer.ConvertFromString("1,2 3,4", null)).Count);

        var cacheSerializer = new CacheModeValueSerializer();
        Assert.IsType<BitmapCache>(cacheSerializer.ConvertFromString("BitmapCache", null));
        Assert.Equal("BitmapCache", cacheSerializer.ConvertToString(new BitmapCache(), null));
    }
}
