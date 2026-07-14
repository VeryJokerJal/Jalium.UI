using System.Globalization;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class Int32RectParityTests
{
    [Fact]
    public void TypeLivesInUnifiedManagedRootNamespace()
    {
        Assert.Equal("Jalium.UI", typeof(Int32Rect).Namespace);
        Assert.Equal("Jalium.UI.Managed", typeof(Int32Rect).Assembly.GetName().Name);
        Assert.Null(typeof(Int32Rect).Assembly.GetType("Jalium.UI.Media.Int32Rect"));
        Assert.True(typeof(IFormattable).IsAssignableFrom(typeof(Int32Rect)));
    }

    [Fact]
    public void ComponentsAreMutableAndAcceptAllInt32Values()
    {
        var rect = new Int32Rect(1, 2, 3, 4)
        {
            X = int.MinValue,
            Y = int.MaxValue,
            Width = -3,
            Height = -4,
        };

        Assert.Equal(int.MinValue, rect.X);
        Assert.Equal(int.MaxValue, rect.Y);
        Assert.Equal(-3, rect.Width);
        Assert.Equal(-4, rect.Height);
    }

    [Fact]
    public void EmptyAndHasAreaUseWpfDefinitions()
    {
        Assert.True(Int32Rect.Empty.IsEmpty);
        Assert.Equal(new Int32Rect(0, 0, 0, 0), Int32Rect.Empty);

        Assert.False(new Int32Rect(1, 2, 0, 0).IsEmpty);
        Assert.False(new Int32Rect(0, 0, 0, 2).IsEmpty);
        Assert.False(new Int32Rect(0, 0, -1, 2).HasArea);
        Assert.False(new Int32Rect(0, 0, 1, 0).HasArea);
        Assert.True(new Int32Rect(0, 0, 1, 2).HasArea);
    }

    [Fact]
    public void EqualityAndHashCodeMatchWpfComponentSemantics()
    {
        var value = new Int32Rect(1, 2, 3, 4);
        var same = new Int32Rect(1, 2, 3, 4);
        var different = new Int32Rect(1, 2, 3, 5);

        Assert.True(Int32Rect.Equals(value, same));
        Assert.True(value.Equals(same));
        Assert.True(value == same);
        Assert.False(value != same);
        Assert.False(Int32Rect.Equals(value, different));
        Assert.Equal(1 ^ 2 ^ 3 ^ 4, value.GetHashCode());
        Assert.Equal(0, Int32Rect.Empty.GetHashCode());
    }

    [Fact]
    public void ParseAcceptsInvariantCommaAndWhitespaceSyntax()
    {
        Assert.Equal(Int32Rect.Empty, Int32Rect.Parse(" Empty "));
        Assert.Equal(new Int32Rect(1, -2, 3, 4), Int32Rect.Parse("1,-2,3,4"));
        Assert.Equal(new Int32Rect(1, 2, 3, 4), Int32Rect.Parse("1  2\t3\r\n4"));
        Assert.Equal(new Int32Rect(1, 2, 3, 4), Int32Rect.Parse("1 , 2,3 4"));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("1;2;3;4")]
    [InlineData("1,2,3")]
    [InlineData("1,2,3,4,5")]
    [InlineData("1,,2,3,4")]
    public void ParseRejectsNonWpfSyntax(string source)
    {
        Assert.ThrowsAny<Exception>(() => Int32Rect.Parse(source));
    }

    [Fact]
    public void ToStringUsesCultureAppropriateListSeparator()
    {
        var rect = new Int32Rect(1, 2, 3, 4);

        Assert.Equal("1,2,3,4", rect.ToString());
        Assert.Equal("1,2,3,4", rect.ToString(CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("1;2;3;4", rect.ToString(CultureInfo.GetCultureInfo("fr-FR")));
        Assert.Equal("0001,0002,0003,0004", ((IFormattable)rect).ToString("D4", CultureInfo.InvariantCulture));
        Assert.Equal("Empty", Int32Rect.Empty.ToString(CultureInfo.GetCultureInfo("fr-FR")));
    }
}
