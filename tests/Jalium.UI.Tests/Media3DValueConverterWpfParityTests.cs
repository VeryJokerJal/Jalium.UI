using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Media;
using Jalium.UI.Media.Media3D;
using Jalium.UI.Media.Media3D.Converters;
using MarkupValueSerializer = Jalium.UI.Markup.ValueSerializer;

namespace Jalium.UI.Tests;

public sealed class Media3DValueConverterWpfParityTests
{
    [Theory]
    [InlineData(typeof(Point3D), typeof(Point3DConverter), "1,2,3")]
    [InlineData(typeof(Vector3D), typeof(Vector3DConverter), "1,2,3")]
    [InlineData(typeof(Point4D), typeof(Point4DConverter), "1,2,3,4")]
    [InlineData(typeof(Size3D), typeof(Size3DConverter), "1,2,3")]
    [InlineData(typeof(Rect3D), typeof(Rect3DConverter), "1,2,3,4,5,6")]
    [InlineData(typeof(Quaternion), typeof(QuaternionConverter), "1,2,3,4")]
    [InlineData(typeof(Matrix3D), typeof(Matrix3DConverter), "Identity")]
    public void TypeDescriptorUsesMedia3DConverters(Type valueType, Type converterType, string text)
    {
        TypeConverter converter = TypeDescriptor.GetConverter(valueType);
        Assert.IsType(converterType, converter);
        object value = converter.ConvertFrom(null, CultureInfo.InvariantCulture, text)!;
        string serialized = Assert.IsType<string>(
            converter.ConvertTo(null, CultureInfo.InvariantCulture, value, typeof(string)));
        Assert.False(string.IsNullOrWhiteSpace(serialized));
    }

    [Fact]
    public void Media3DValueSerializersRoundTripInvariantStrings()
    {
        MarkupValueSerializer[] serializers =
        [
            new Point3DValueSerializer(),
            new Vector3DValueSerializer(),
            new Point4DValueSerializer(),
            new Size3DValueSerializer(),
            new Rect3DValueSerializer(),
            new QuaternionValueSerializer(),
            new Matrix3DValueSerializer(),
        ];
        object[] values =
        [
            new Point3D(1, 2, 3),
            new Vector3D(1, 2, 3),
            new Point4D(1, 2, 3, 4),
            new Size3D(1, 2, 3),
            new Rect3D(1, 2, 3, 4, 5, 6),
            new Quaternion(1, 2, 3, 4),
            Matrix3D.Identity,
        ];

        for (var index = 0; index < serializers.Length; index++)
        {
            Assert.True(serializers[index].CanConvertToString(values[index], null));
            string text = serializers[index].ConvertToString(values[index], null);
            object restored = serializers[index].ConvertFromString(text, null);
            Assert.Equal(values[index], restored);
        }
    }

    [Fact]
    public void LegacyValueSerializerRegistryReturnsWorkingMedia3DSerializers()
    {
        Assert.NotNull(ValueSerializer.GetSerializerFor(typeof(Point3D)));
        Assert.NotNull(ValueSerializer.GetSerializerFor(typeof(Matrix3D)));
        Assert.Null(ValueSerializer.GetSerializerFor(typeof(Media3DValueConverterWpfParityTests)));
    }
}
