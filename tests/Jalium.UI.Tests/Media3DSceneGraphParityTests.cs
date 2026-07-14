using Jalium.UI.Media;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

public sealed class Media3DSceneGraphParityTests
{
    [Fact]
    public void CamerasExposeDependencyPropertiesCloneAndFreezeContracts()
    {
        var camera = new PerspectiveCamera(
            new Point3D(1, 2, 3),
            new Vector3D(0, 0, -1),
            new Vector3D(0, 1, 0),
            60)
        {
            NearPlaneDistance = 0.5,
            FarPlaneDistance = 500,
        };

        var localValues = camera.GetLocalValueEnumerator();
        var hasFieldOfView = false;
        while (localValues.MoveNext())
        {
            if (localValues.Current.Property == PerspectiveCamera.FieldOfViewProperty)
            {
                hasFieldOfView = true;
                break;
            }
        }
        Assert.True(hasFieldOfView);

        PerspectiveCamera clone = camera.Clone();
        Assert.Equal(camera.Position, clone.Position);
        Assert.Equal(60, clone.FieldOfView);
        Assert.NotSame(camera, clone);

        camera.Freeze();
        Assert.True(camera.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => camera.FieldOfView = 45);
    }

    [Fact]
    public void ModelAndMaterialCollectionsCloneTheirFreezableChildren()
    {
        var materials = new MaterialGroup();
        materials.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(1, 2, 3))));
        MaterialGroup materialClone = materials.Clone();

        Assert.NotSame(materials.Children, materialClone.Children);
        Assert.NotSame(materials.Children[0], materialClone.Children[0]);

        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new Point3D(-1, -1, 0),
                new Point3D(1, -1, 0),
                new Point3D(0, 1, 0),
            },
        };
        var model = new GeometryModel3D(mesh, materials)
        {
            Transform = new TranslateTransform3D(5, 2, -3),
        };

        Assert.Equal(new Rect3D(4, 1, -3, 2, 2, 0), model.Bounds);
        GeometryModel3D modelClone = model.Clone();
        Assert.NotSame(model, modelClone);
        Assert.NotSame(model.Geometry, modelClone.Geometry);
    }

    [Fact]
    public void VisualTreeMaintainsOwnershipAndComputesAncestorTransforms()
    {
        var root = new ModelVisual3D { Transform = new TranslateTransform3D(10, 0, 0) };
        var child = new ModelVisual3D { Transform = new TranslateTransform3D(2, 3, 4) };
        root.Children.Add(child);

        Assert.True(root.IsAncestorOf(child));
        Assert.True(child.IsDescendantOf(root));
        Assert.Same(root, child.FindCommonVisualAncestor(root));
        Assert.Equal(
            new Point3D(2, 3, 4),
            child.TransformToAncestor(root).Transform(new Point3D()));
    }

    [Fact]
    public void ViewportProjectsBoundsAndReturnsTriangleHitDetails()
    {
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new Point3D(-1, -1, -3),
                new Point3D(1, -1, -3),
                new Point3D(0, 1, -3),
            },
            TriangleIndices = new Int32Collection { 0, 1, 2 },
        };
        var modelVisual = new ModelVisual3D
        {
            Content = new GeometryModel3D(mesh, new DiffuseMaterial()),
        };
        var viewport = new Viewport3DVisual
        {
            Viewport = new Rect(0, 0, 100, 100),
        };
        viewport.Children.Add(modelVisual);

        Assert.False(viewport.DescendantBounds.IsEmpty);
        var hit = Assert.IsType<RayMeshGeometry3DHitTestResult>(viewport.HitTest(new Point(50, 50)));
        Assert.Same(mesh, hit.MeshHit);
        Assert.Equal(0, hit.VertexIndex1);
        Assert.Equal(1, hit.VertexIndex2);
        Assert.Equal(2, hit.VertexIndex3);
        Assert.InRange(hit.VertexWeight1 + hit.VertexWeight2 + hit.VertexWeight3, 0.999999, 1.000001);
    }

    [Fact]
    public void Viewport2DVisual3DUsesDependencyPropertiesAndHostMaterialMarker()
    {
        var material = new DiffuseMaterial();
        var visual = new Viewport2DVisual3D
        {
            Geometry = new MeshGeometry3D(),
            Material = material,
        };

        Viewport2DVisual3D.SetIsVisualHostMaterial(material, true);
        Assert.True(Viewport2DVisual3D.GetIsVisualHostMaterial(material));
        Assert.Same(visual.Geometry, visual.GetValue(Viewport2DVisual3D.GeometryProperty));
        Assert.Same(material, visual.GetValue(Viewport2DVisual3D.MaterialProperty));
    }
}
