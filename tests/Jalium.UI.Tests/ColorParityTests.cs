using System.Globalization;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class ColorParityTests
{
    [Fact]
    public void ByteAndScRgbPropertiesStaySynchronizedAndMutable()
    {
        Color color = Color.FromScRgb(1.2f, -0.2f, 0.5f, 1.5f);
        Assert.Equal((byte)255, color.A);
        Assert.Equal((byte)0, color.R);
        Assert.Equal((byte)188, color.G);
        Assert.Equal((byte)255, color.B);
        Assert.Equal(1.2f, color.ScA);
        Assert.Equal(-0.2f, color.ScR);
        Assert.Equal(0.5f, color.ScG);
        Assert.Equal(1.5f, color.ScB);

        color.R = 128;
        Assert.Equal(128, color.R);
        Assert.InRange(color.ScR, 0.2158f, 0.2160f);
        color.ScB = 0.2f;
        Assert.Equal(124, color.B);

        color.Clamp();
        Assert.Equal(1f, color.ScA);
        Assert.InRange(color.ScR, 0f, 1f);
        Assert.Equal(0.2f, color.ScB);
    }

    [Fact]
    public void ArithmeticUsesLinearScRgbComponents()
    {
        Color first = Color.FromScRgb(0.8f, 0.2f, 0.3f, 0.4f);
        Color second = Color.FromScRgb(0.5f, 0.9f, -0.1f, 0.8f);

        Color added = first + second;
        Assert.Equal(1.3f, added.ScA);
        Assert.Equal(1.1f, added.ScR);
        Assert.Equal(0.2f, added.ScG, 6);
        Assert.Equal(1.2f, added.ScB);

        Color subtracted = first - second;
        Assert.Equal(0.3f, subtracted.ScA);
        Assert.Equal(-0.7f, subtracted.ScR);

        Color multiplied = first * 2f;
        Assert.Equal(1.6f, multiplied.ScA);
        Assert.Equal(0.4f, multiplied.ScR);
        Assert.True(Color.AreClose(multiplied, Color.Multiply(first, 2f)));
        Assert.True(Color.Equals(added, Color.Add(first, second)));
    }

    [Fact]
    public void ContextColorsRetainProfileAndDefensivelyCopyNativeValues()
    {
        var uri = new Uri("profile.icc", UriKind.Relative);
        Color color = Color.FromAValues(0.5f, [0.1f, 0.2f, 0.3f], uri);

        Assert.NotNull(color.ColorContext);
        Assert.Equal(uri, color.ColorContext!.ProfileUri);
        Assert.Equal([0.1f, 0.2f, 0.3f], color.GetNativeColorValues());
        float[] copy = color.GetNativeColorValues();
        copy[0] = 1;
        Assert.Equal(0.1f, color.GetNativeColorValues()[0]);
        Assert.StartsWith("ContextColor ", color.ToString(CultureInfo.InvariantCulture));
        Assert.Throws<InvalidOperationException>(() => Color.Red.GetNativeColorValues());
        Assert.Throws<ArgumentException>(() => Color.FromValues([0.1f, 0.2f], uri));
    }

    [Fact]
    public void ColorContextHasWpfNamespaceAndValueOperators()
    {
        Assert.Equal("Jalium.UI.Media", typeof(ColorContext).Namespace);
        var first = new ColorContext(new Uri("profile.icc", UriKind.Relative));
        var second = new ColorContext(new Uri("profile.icc", UriKind.Relative));
        var third = new ColorContext(new Uri("other.icc", UriKind.Relative));

        Assert.True(first == second);
        Assert.False(first != second);
        Assert.False(first == third);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
}
