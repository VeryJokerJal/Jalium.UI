using System.Reflection;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;

public sealed class MediaSmallSurfaceParityTests
{
    [Fact]
    public void CharacterMetricsRoundTripsCanonicalMetricsProperty()
    {
        var metrics = new CharacterMetrics
        {
            Metrics = "0.5,0.75,0.6,0.1,0.2,0.3,0.4"
        };

        Assert.Equal(0.5, metrics.BlackBoxWidth);
        Assert.Equal(0.75, metrics.BlackBoxHeight);
        Assert.Equal(0.6, metrics.Baseline);
        Assert.Equal(0.1, metrics.LeftSideBearing);
        Assert.Equal(0.2, metrics.RightSideBearing);
        Assert.Equal(0.3, metrics.TopSideBearing);
        Assert.Equal(0.4, metrics.BottomSideBearing);
        Assert.Equal("0.5,0.75,0.6,0.1,0.2,0.3,0.4", metrics.Metrics);
    }

    [Fact]
    public void CollectionGeometryAndMatrixExposeWpfMembers()
    {
        var typefaces = new FontFamily().FamilyTypefaces;
        Assert.False(typefaces.IsReadOnly);
        typefaces.Add(new FamilyTypeface());
        Assert.Single(typefaces);

        var geometry = new RectangleGeometry(new Rect(1, 2, 3, 4));
        var parameters = new GeometryHitTestParameters(geometry);
        Assert.Same(geometry, parameters.HitGeometry);
        Assert.Same(parameters.HitTestArea, parameters.HitGeometry);

        var first = new Matrix(1, 2, 3, 4, 5, 6);
        var second = new Matrix(1, 2, 3, 4, 5, 6);
        Assert.True(Matrix.Equals(first, second));
        MethodInfo equals = typeof(Matrix).GetMethod(
            nameof(Matrix.Equals),
            BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(Matrix), typeof(Matrix)],
            null)!;
        Assert.Equal(new[] { "matrix1", "matrix2" }, equals.GetParameters().Select(parameter => parameter.Name));
    }

    [Fact]
    public void BitmapBlobAndEmptySizeOptionsDoNotExposeMutableBackingState()
    {
        var source = new byte[] { 1, 2, 3 };
        var blob = new BitmapMetadataBlob(source);
        source[0] = 99;

        byte[] first = blob.GetBlobValue();
        Assert.Equal(new byte[] { 1, 2, 3 }, first);
        first[1] = 88;
        Assert.Equal(new byte[] { 1, 2, 3 }, blob.GetBlobValue());
        Assert.Equal(3, blob.Size);

        BitmapSizeOptions empty = BitmapSizeOptions.FromEmptyOptions();
        Assert.Equal(0, empty.PixelWidth);
        Assert.Equal(0, empty.PixelHeight);
        Assert.False(empty.PreservesAspectRatio);
        Assert.Equal(Rotation.Rotate0, empty.Rotation);
    }
}
