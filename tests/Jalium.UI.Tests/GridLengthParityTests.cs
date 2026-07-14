using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class GridLengthParityTests
{
    [Fact]
    public void TypesAreInTheWpfNamespace()
    {
        Assert.Equal("Jalium.UI.GridLength", typeof(GridLength).FullName);
        Assert.Equal("Jalium.UI.GridUnitType", typeof(GridUnitType).FullName);
        Assert.Equal(typeof(GridLengthConverter), TypeDescriptor.GetConverter(typeof(GridLength)).GetType());
    }

    [Fact]
    public void DefaultValueIsAutoAndReportsOne()
    {
        GridLength value = default;

        Assert.True(value.IsAuto);
        Assert.Equal(GridUnitType.Auto, value.GridUnitType);
        Assert.Equal(1.0, value.Value);
        Assert.Equal(GridLength.Auto, value);
    }

    [Fact]
    public void ConstructorMatchesWpfValidationAndNormalization()
    {
        Assert.Equal(1.0, new GridLength(123, GridUnitType.Auto).Value);
        Assert.Equal(-2.0, new GridLength(-2, GridUnitType.Pixel).Value);
        Assert.Equal(-3.0, new GridLength(-3, GridUnitType.Star).Value);
        Assert.Throws<ArgumentException>(() => new GridLength(double.NaN));
        Assert.Throws<ArgumentException>(() => new GridLength(double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => new GridLength(1, (GridUnitType)42));
    }

    [Fact]
    public void ConverterSupportsWpfTextNumericAndDescriptorPaths()
    {
        TypeConverter converter = TypeDescriptor.GetConverter(typeof(GridLength));

        Assert.Equal(GridLength.Auto, converter.ConvertFromInvariantString("Auto"));
        Assert.Equal(new GridLength(1, GridUnitType.Star), converter.ConvertFromInvariantString("*"));
        Assert.Equal(new GridLength(2.5, GridUnitType.Star), converter.ConvertFromInvariantString("2.5*"));
        Assert.Equal(new GridLength(192), converter.ConvertFromInvariantString("2in"));
        Assert.Equal(new GridLength(12), converter.ConvertFrom(null, CultureInfo.InvariantCulture, 12));
        Assert.Equal(GridLength.Auto, converter.ConvertFrom(null, CultureInfo.InvariantCulture, double.NaN));
        Assert.Equal("2.5*", converter.ConvertToInvariantString(new GridLength(2.5, GridUnitType.Star)));

        var descriptor = Assert.IsType<InstanceDescriptor>(
            converter.ConvertTo(new GridLength(3, GridUnitType.Star), typeof(InstanceDescriptor)));
        Assert.Equal(new GridLength(3, GridUnitType.Star), descriptor.Invoke());
    }
}
