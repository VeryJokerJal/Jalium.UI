using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Media.Media3D;
using MediaBrushValueSerializer = Jalium.UI.Media.Converters.BrushValueSerializer;
using MediaGeometryValueSerializer = Jalium.UI.Media.Converters.GeometryValueSerializer;
using MediaTransformValueSerializer = Jalium.UI.Media.Converters.TransformValueSerializer;

namespace Jalium.UI.Tests;

public sealed class CanonicalApiConsolidationTests
{
    [Fact]
    public void HistoricalDurationDockSerializerAndPointCollectionTypesAreRetired()
    {
        string[] retiredNames =
        [
            "Jalium.UI.Controls.Duration",
            "Jalium.UI.Controls.Primitives.Dock",
            "Jalium.UI.Media.BrushValueSerializer",
            "Jalium.UI.Media.GeometryValueSerializer",
            "Jalium.UI.Media.TransformValueSerializer",
            "Jalium.UI.Controls.Shapes.PointCollection",
            "Jalium.UI.Media.Media3D.PointCollection",
        ];

        Type[] exported = typeof(FrameworkElement).Assembly.GetExportedTypes();
        Assert.All(retiredNames, name => Assert.DoesNotContain(exported, type => type.FullName == name));

        foreach (string facadeName in new[] { "Jalium.UI.Core", "Jalium.UI.Media", "Jalium.UI.Controls" })
        {
            Type[] forwarded = Assembly.Load(facadeName).GetForwardedTypes();
            Assert.All(retiredNames, name => Assert.DoesNotContain(forwarded, type => type.FullName == name));
        }
    }

    [Fact]
    public void PublicPropertiesUseCanonicalWpfTypes()
    {
        Assert.Equal(typeof(Duration), typeof(MediaElement).GetProperty(nameof(MediaElement.NaturalDuration))!.PropertyType);
        Assert.Equal(typeof(Dock), typeof(TabPanel).GetProperty(nameof(TabPanel.TabStripPlacement))!.PropertyType);
        Assert.Equal(typeof(PointCollection), typeof(Jalium.UI.Shapes.Polygon).GetProperty(nameof(Jalium.UI.Shapes.Polygon.Points))!.PropertyType);
        Assert.Equal(typeof(PointCollection), typeof(Jalium.UI.Shapes.Polyline).GetProperty(nameof(Jalium.UI.Shapes.Polyline.Points))!.PropertyType);
        Assert.Equal(typeof(PointCollection), typeof(MeshGeometry3D).GetProperty(nameof(MeshGeometry3D.TextureCoordinates))!.PropertyType);

        Assert.Equal(typeof(MediaBrushValueSerializer), Jalium.UI.Markup.ValueSerializer.GetSerializerFor(typeof(Brush))!.GetType());
        Assert.Equal(typeof(MediaGeometryValueSerializer), Jalium.UI.Markup.ValueSerializer.GetSerializerFor(typeof(Geometry))!.GetType());
        Assert.Equal(typeof(MediaTransformValueSerializer), Jalium.UI.Markup.ValueSerializer.GetSerializerFor(typeof(Transform))!.GetType());
    }

    [Fact]
    public void CanonicalTransformSerializerPreservesCaseInsensitiveIdentityParsing()
    {
        var serializer = new MediaTransformValueSerializer();

        var transform = Assert.IsType<MatrixTransform>(serializer.ConvertFromString("identity", null));

        Assert.True(transform.Value.IsIdentity);
    }

    [Fact]
    public void MeshGeometryCloneOwnsAnIndependentCanonicalTextureCoordinateCollection()
    {
        var mesh = new MeshGeometry3D
        {
            TextureCoordinates = new PointCollection([new Point(0, 0), new Point(1, 1)]),
        };

        MeshGeometry3D clone = mesh.Clone();

        Assert.NotSame(mesh.TextureCoordinates, clone.TextureCoordinates);
        Assert.Equal(mesh.TextureCoordinates.ToArray(), clone.TextureCoordinates.ToArray());
    }
}
