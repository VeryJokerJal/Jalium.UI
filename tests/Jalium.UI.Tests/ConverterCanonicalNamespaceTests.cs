using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

public sealed class ConverterCanonicalNamespaceTests
{
    [Fact]
    public void WpfConvertersHaveOnlyCanonicalPublicTypeIdentities()
    {
        Type[] exported = typeof(FrameworkElement).Assembly.GetExportedTypes();

        string[] canonical =
        [
            "Jalium.UI.Controls.AlternationConverter",
            "Jalium.UI.Controls.BooleanToVisibilityConverter",
            "Jalium.UI.KeySplineConverter",
            "Jalium.UI.KeyTimeConverter",
        ];
        string[] retired =
        [
            "Jalium.UI.Data.AlternationConverter",
            "Jalium.UI.Data.BooleanToVisibilityConverter",
            "Jalium.UI.Media.Animation.KeySplineConverter",
            "Jalium.UI.Media.Animation.KeyTimeConverter",
        ];

        Assert.All(canonical, name =>
            Assert.Contains(exported, type => type.FullName == name));
        Assert.All(retired, name =>
            Assert.DoesNotContain(exported, type => type.FullName == name));
    }

    [Fact]
    public void KeyFrameTypesAdvertiseRootWpfConverters()
    {
        Assert.Equal(
            typeof(KeyTimeConverter),
            TypeDescriptor.GetConverter(typeof(KeyTime)).GetType());
        Assert.Equal(
            typeof(KeySplineConverter),
            TypeDescriptor.GetConverter(typeof(KeySpline)).GetType());
    }

    [Fact]
    public void KeyFrameConvertersRoundTripSupportedWpfForms()
    {
        var timeConverter = new KeyTimeConverter();
        Assert.Equal(KeyTime.Uniform, timeConverter.ConvertFromInvariantString("Uniform"));
        Assert.Equal(KeyTime.Paced, timeConverter.ConvertFromInvariantString("Paced"));
        Assert.Equal(KeyTime.FromPercent(0.25), timeConverter.ConvertFromInvariantString("25%"));
        Assert.Equal(KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2)), timeConverter.ConvertFromInvariantString("0:0:2"));
        Assert.Throws<IndexOutOfRangeException>(() => timeConverter.ConvertFromInvariantString(string.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => KeyTime.FromTimeSpan(TimeSpan.FromTicks(-1)));
        Assert.Throws<InvalidOperationException>(() => _ = KeyTime.Uniform.Percent);
        Assert.Throws<InvalidOperationException>(() => _ = KeyTime.Paced.TimeSpan);

        var splineConverter = new KeySplineConverter();
        var spline = Assert.IsType<KeySpline>(
            splineConverter.ConvertFrom(null, CultureInfo.InvariantCulture, "0.1,0.2,0.8,0.9"));
        Assert.Equal(new Point(0.1, 0.2), spline.ControlPoint1);
        Assert.Equal(new Point(0.8, 0.9), spline.ControlPoint2);
        Assert.Equal(
            "0.1,0.2,0.8,0.9",
            splineConverter.ConvertTo(null, CultureInfo.InvariantCulture, spline, typeof(string)));
        Assert.Throws<InvalidOperationException>(() =>
            splineConverter.ConvertFromInvariantString(string.Empty));
    }
}
