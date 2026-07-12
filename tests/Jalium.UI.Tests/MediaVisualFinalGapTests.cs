using System.Collections;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Effects;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Media3D;
using ImagingBitmapSource = Jalium.UI.Media.Imaging.BitmapSource;
using ShapePath = Jalium.UI.Controls.Shapes.Path;

namespace Jalium.UI.Tests;

#pragma warning disable CS0618 // The tests intentionally cover WPF's retained legacy effect API.

public sealed class MediaVisualFinalGapTests
{
    [Fact]
    public void DrawingContextCurrentValueContractsAreAbstract()
    {
        AssertAbstract(nameof(DrawingContext.DrawDrawing), typeof(Drawing));
        AssertAbstract(
            nameof(DrawingContext.DrawLine),
            typeof(Pen), typeof(Point), typeof(AnimationClock), typeof(Point), typeof(AnimationClock));
        AssertAbstract(
            nameof(DrawingContext.DrawImage),
            typeof(ImageSource), typeof(Rect), typeof(AnimationClock));
        AssertAbstract(nameof(DrawingContext.DrawGlyphRun), typeof(Brush), typeof(GlyphRun));
        AssertAbstract(nameof(DrawingContext.DrawVideo), typeof(MediaPlayer), typeof(Rect));
        AssertAbstract(
            nameof(DrawingContext.DrawVideo),
            typeof(MediaPlayer), typeof(Rect), typeof(AnimationClock));
        AssertAbstract(
            nameof(DrawingContext.PushOpacity),
            typeof(double), typeof(AnimationClock));
        AssertAbstract(nameof(DrawingContext.PushGuidelineSet), typeof(GuidelineSet));
        AssertAbstract(nameof(DrawingContext.PushOpacityMask), typeof(Brush));
        AssertAbstract(nameof(DrawingContext.PushEffect), typeof(BitmapEffect), typeof(BitmapEffectInput));

        var disposeCore = typeof(DrawingContext).GetMethod(
            "DisposeCore",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.DeclaredOnly);
        Assert.NotNull(disposeCore);
        Assert.True(disposeCore.IsAbstract);
    }

    [Fact]
    public void DrawingContextDrawTextIsConcreteNonVirtualAndPreservesAdapterDispatch()
    {
        var method = typeof(DrawingContext).GetMethod(
            nameof(DrawingContext.DrawText),
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.DeclaredOnly,
            binder: null,
            types: [typeof(FormattedText), typeof(Point)],
            modifiers: null);
        Assert.NotNull(method);
        Assert.False(method.IsAbstract);
        Assert.False(method.IsVirtual);

        var formattedText = new FormattedText("parity", "Segoe UI", 12)
        {
            Foreground = Brushes.Black,
        };
        var group = new DrawingGroup();
        using (DrawingContext context = group.Open())
        {
            context.DrawText(formattedText, new Point(3, 4));
        }

        GlyphRunDrawing drawing = Assert.IsType<GlyphRunDrawing>(Assert.Single(group.Children));
        Assert.Same(formattedText, drawing.FormattedText);
        Assert.Equal(new Point(3, 4), drawing.Origin);
    }

    [Fact]
    public void MediaCollectionsHaveDirectAnimatableListShapeAndCloneOwnership()
    {
        AssertCollectionShape<GeometryCollection, Geometry>();
        AssertCollectionShape<DrawingCollection, Drawing>();
        AssertCollectionShape<PathFigureCollection, PathFigure>();
        AssertCollectionShape<PathSegmentCollection, PathSegment>();
        AssertCollectionShape<TextEffectCollection, TextEffect>();
        AssertCollectionShape<TransformCollection, Transform>();

        var original = new GeometryCollection
        {
            new RectangleGeometry(new Rect(1, 2, 3, 4)),
        };
        GeometryCollection clone = original.Clone();
        Assert.NotSame(original[0], clone[0]);
        clone.Freeze();
        Assert.True(clone[0].IsFrozen);
    }

    [Fact]
    public void BitmapEffectUsesCanonicalImagingBitmapSource()
    {
        Assert.NotNull(typeof(BitmapEffectInput).GetConstructor([typeof(ImagingBitmapSource)]));
        Assert.Equal(
            typeof(ImagingBitmapSource),
            typeof(BitmapEffectInput).GetProperty(nameof(BitmapEffectInput.ContextInputSource))!.PropertyType);
        Assert.Equal(
            typeof(ImagingBitmapSource),
            typeof(BitmapEffect).GetMethod(nameof(BitmapEffect.GetOutput))!.ReturnType);
    }

    [Fact]
    public void ShapePathDataUsesCanonicalGeometryContract()
    {
        Assert.Equal(typeof(Geometry), typeof(ShapePath).GetProperty(nameof(ShapePath.Data))!.PropertyType);
        Assert.Equal(typeof(Geometry), ShapePath.DataProperty.PropertyType);

        Geometry geometry = Geometry.Parse("M0,0 L4,4");
        var path = new ShapePath { Data = geometry };
        Assert.Same(geometry, path.Data);
        Assert.Same(geometry, path.Geometry);
    }

    [Fact]
    public void VisualTransformsReturnCanonicalMediaGeneralTransform()
    {
        var method = typeof(Visual).GetMethod(nameof(Visual.TransformToAncestor), [typeof(Visual)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(Jalium.UI.Media.GeneralTransform), method.ReturnType);

        var transform3D = typeof(Visual).GetMethod(
            nameof(Visual.TransformToAncestor),
            [typeof(Visual3D)]);
        Assert.NotNull(transform3D);
        Assert.Equal(typeof(GeneralTransform2DTo3D), transform3D.ReturnType);
    }

    [Fact]
    public void VisualTreeHooksHaveWpfProtectedMetadataWithPublicCompatibilityShim()
    {
        const System.Reflection.BindingFlags declaredNonPublic =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.DeclaredOnly;

        var parent = typeof(Visual).GetProperty("VisualParent", declaredNonPublic);
        var count = typeof(Visual).GetProperty("VisualChildrenCount", declaredNonPublic);
        var child = typeof(Visual).GetMethod("GetVisualChild", declaredNonPublic);
        Assert.NotNull(parent);
        Assert.NotNull(count);
        Assert.NotNull(child);
        Assert.Equal(typeof(DependencyObject), parent.PropertyType);
        Assert.True(parent.GetMethod!.IsFamily);
        Assert.True(count.GetMethod!.IsFamily);
        Assert.True(count.GetMethod.IsVirtual);
        Assert.True(child.IsFamily);
        Assert.True(child.IsVirtual);

        Assert.NotNull(typeof(DependencyObject).GetField("VisualParent"));
        Assert.NotNull(typeof(DependencyObject).GetField("VisualChildrenCount"));
        Assert.Equal(
            typeof(Func<int, Visual>),
            typeof(DependencyObject).GetField("GetVisualChild")!.FieldType);

        var container = new ContainerVisual();
        var visual = new DrawingVisual();
        container.Children.Add(visual);
        Assert.Same(container, visual.VisualParent);
        Assert.Equal(1, container.VisualChildrenCount);
        Assert.Same(visual, container.GetVisualChild(0));
    }

    [Fact]
    public void MediaBridgesExposeRetainedDrawingAndThreeDimensionalBounds()
    {
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Red, null, new Rect(2, 3, 4, 5));
        }

        Assert.Same(visual.Drawing, VisualTreeHelper.GetDrawing(visual));

        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(0, 0, 0));
        mesh.Positions.Add(new Point3D(2, 0, 0));
        mesh.Positions.Add(new Point3D(0, 3, 1));
        var child = new ModelVisual3D
        {
            Content = new GeometryModel3D { Geometry = mesh },
            Transform = new TranslateTransform3D(10, 20, 30),
        };
        var root = new ModelVisual3D();
        root.Children.Add(child);

        Assert.Equal(mesh.Bounds, VisualTreeHelper.GetContentBounds(child));
        Rect3D descendantBounds = VisualTreeHelper.GetDescendantBounds(root);
        Assert.Equal(10, descendantBounds.X);
        Assert.Equal(20, descendantBounds.Y);
        Assert.Equal(30, descendantBounds.Z);
        Assert.Equal(2, descendantBounds.SizeX);
        Assert.Equal(3, descendantBounds.SizeY);
        Assert.Equal(1, descendantBounds.SizeZ);

        var hostedVisual = new DrawingVisual();
        var host = new Viewport2DVisual3D { Visual = hostedVisual };
        root.Children.Add(host);
        Point3D transformed = hostedVisual.TransformToAncestor(root).Transform(new Point(4, 5));
        Assert.Equal(new Point3D(4, 5, 0), transformed);

        Assert.NotNull(typeof(VisualTreeHelper).GetMethod(
            nameof(VisualTreeHelper.HitTest),
            [
                typeof(Visual3D),
                typeof(HitTestFilterCallback),
                typeof(HitTestResultCallback),
                typeof(HitTestParameters3D),
            ]));
    }

    [Fact]
    public void DrawingGroupRecordsGlyphRunsAndVideoCommandsThroughExactContracts()
    {
        var glyphRun = new GlyphRun
        {
            FontRenderingEmSize = 12,
            BaselineOrigin = new Point(1, 10),
            GlyphIndices = new ushort[] { 1 },
            AdvanceWidths = new double[] { 6 },
        };
        var group = new DrawingGroup();
        using (DrawingContext context = group.Open())
        {
            context.DrawGlyphRun(Brushes.Black, glyphRun);
        }

        Assert.IsType<GlyphRunDrawing>(Assert.Single(group.Children));

        using var player = new MediaPlayer();
        var videoGroup = new DrawingGroup();
        using (DrawingContext context = videoGroup.Open())
        {
            context.DrawVideo(player, new Rect(2, 3, 40, 30));
        }

        VideoDrawing video = Assert.IsType<VideoDrawing>(Assert.Single(videoGroup.Children));
        Assert.Same(player, video.Player);
        Assert.Equal(new Rect(2, 3, 40, 30), video.Rect);
    }

    private static void AssertAbstract(string name, params Type[] parameterTypes)
    {
        var method = typeof(DrawingContext).GetMethod(name, parameterTypes);
        Assert.NotNull(method);
        Assert.True(method.IsAbstract);
    }

    private static void AssertCollectionShape<TCollection, TItem>()
    {
        Assert.Equal(typeof(Animatable), typeof(TCollection).BaseType);
        Assert.True(typeof(IList<TItem>).IsAssignableFrom(typeof(TCollection)));
        Assert.True(typeof(IList).IsAssignableFrom(typeof(TCollection)));
        Assert.True(typeof(TCollection).IsSealed);
    }
}

#pragma warning restore CS0618
