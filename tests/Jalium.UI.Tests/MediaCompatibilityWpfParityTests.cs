using System.ComponentModel;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class MediaCompatibilityWpfParityTests
{
    [Fact]
    public void FontFamilyExposesCompositeFontDataAndBuildsTypefaces()
    {
        var baseUri = new Uri("https://example.test/fonts/");
        var family = new FontFamily(baseUri, "Demo Family")
        {
            Baseline = 0.75,
            LineSpacing = 1.25,
        };

        family.FamilyNames.Add(XmlLanguage.GetLanguage("en-us"), "Demo Family");
        family.FamilyMaps.Add(new FontFamilyMap
        {
            Unicode = "0000-007f",
            Target = "Demo Latin",
            Scale = 0.9,
        });
        family.FamilyTypefaces.Add(new FamilyTypeface
        {
            Style = FontStyles.Italic,
            Weight = FontWeights.Bold,
            Stretch = FontStretches.Expanded,
            CapsHeight = 0.66,
            StrikethroughPosition = 0.31,
            StrikethroughThickness = 0.07,
            UnderlinePosition = -0.12,
            UnderlineThickness = 0.06,
            XHeight = 0.48,
        });

        Assert.Equal(baseUri, family.BaseUri);
        Assert.Equal("Demo Family", family.FamilyNames[XmlLanguage.GetLanguage("en-us")]);
        Assert.Single(family.FamilyMaps);
        Typeface typeface = Assert.Single(family.GetTypefaces());
        Assert.Same(family, typeface.FontFamily);
        Assert.Equal(FontStyles.Italic, typeface.Style);
        Assert.Equal(FontWeights.Bold, typeface.Weight);
        Assert.Equal(FontStretches.Expanded, typeface.Stretch);
        Assert.Equal(0.66, typeface.CapsHeight);
        Assert.Equal(0.31, typeface.StrikethroughPosition);
        Assert.Equal(0.07, typeface.StrikethroughThickness);
        Assert.Equal(-0.12, typeface.UnderlinePosition);
        Assert.Equal(0.06, typeface.UnderlineThickness);
        Assert.Equal(0.48, typeface.XHeight);
        Assert.Equal("Regular", typeface.FaceNames[XmlLanguage.GetLanguage("en-us")]);
        Assert.Equal(0.75, family.Baseline);
        Assert.Equal(1.25, family.LineSpacing);
    }

    [Fact]
    public void FontFamilyMapCollectionRejectsIncompleteTargets()
    {
        var family = new FontFamily();
        Assert.Throws<ArgumentException>(() => family.FamilyMaps.Add(new FontFamilyMap()));

        var map = new FontFamilyMap { Target = "Fallback" };
        family.FamilyMaps.Add(map);
        Assert.Same(map, family.FamilyMaps[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Scale = 0);
    }

    [Fact]
    public void MediaConvertersRoundTripWpfTextForms()
    {
        var matrix = new Matrix(1, 2, 3, 4, 5, 6);
        var matrixConverter = new MatrixConverter();
        Assert.Equal(matrix, matrixConverter.ConvertFromInvariantString("1,2,3,4,5,6"));
        Assert.Equal("1,2,3,4,5,6", matrixConverter.ConvertToInvariantString(matrix));

        var doubles = Assert.IsType<DoubleCollection>(
            new DoubleCollectionConverter().ConvertFromInvariantString("1 2.5 3"));
        Assert.Equal(new[] { 1d, 2.5d, 3d }, doubles);

        var transformConverter = new TransformConverter();
        MatrixTransform transform = Assert.IsType<MatrixTransform>(transformConverter.ConvertFromInvariantString("Identity"));
        Assert.True(transform.Value.IsIdentity);

        System.ComponentModel.TypeConverter cacheConverter = TypeDescriptor.GetConverter(typeof(CacheMode));
        Assert.IsType<BitmapCache>(cacheConverter.ConvertFromInvariantString("BitmapCache"));

        var pointConverter = new Jalium.UI.Media.PointCollectionConverter();
        PointCollection points = Assert.IsType<PointCollection>(pointConverter.ConvertFromInvariantString("1,2 3,4"));
        Assert.Equal("1,2 3,4", pointConverter.ConvertToInvariantString(points));

        var brushConverter = new Jalium.UI.Media.BrushConverter();
        var brush = Assert.IsType<SolidColorBrush>(brushConverter.ConvertFromInvariantString("#FF0A141E"));
        Assert.Equal("#FF0A141E", brushConverter.ConvertToInvariantString(brush));
    }

    [Fact]
    public void GeneralTransformCollectionSupportsTypedCloneAndEnumeration()
    {
        var transform = new TestGeneralTransform();
        var collection = new GeneralTransformCollection(1) { transform };

        GeneralTransformCollection clone = collection.Clone();
        Assert.Single(clone);
        Assert.NotSame(collection, clone);
        Assert.NotSame(transform, clone[0]);

        GeneralTransformCollection.Enumerator enumerator = collection.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Same(transform, enumerator.Current);
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void AffineTransformsAreFreezableCloneableAndComposable()
    {
        var scale = new ScaleTransform(2, 3, 1, 2);
        ScaleTransform clone = scale.Clone();
        Assert.Equal(scale.Value, clone.Value);
        Assert.NotSame(scale, clone);

        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(new TranslateTransform(5, 7));
        Assert.Equal(group.Value.Transform(new Point(2, 4)), group.Transform(new Point(2, 4)));
        Assert.NotNull(group.Inverse);

        TransformGroup groupClone = group.Clone();
        Assert.Equal(2, groupClone.Children.Count);
        Assert.NotSame(group.Children, groupClone.Children);
        Assert.NotSame(group.Children[0], groupClone.Children[0]);

        group.Freeze();
        Assert.True(group.IsFrozen);
        Assert.True(scale.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => scale.ScaleX = 4);
    }

    [Fact]
    public void FontEmbeddingManagerDeduplicatesAndSortsGlyphUsage()
    {
        var fontUri = new Uri("file:///C:/Windows/Fonts/arial.ttf");
        var glyphRun = new GlyphRun
        {
            GlyphTypeface = new GlyphTypeface(fontUri),
            GlyphIndices = new ushort[] { 7, 2, 7, 4 },
        };
        var manager = new FontEmbeddingManager();

        manager.RecordUsage(glyphRun);

        Assert.Equal(new ushort[] { 2, 4, 7 }, manager.GetUsedGlyphs(fontUri));
        Assert.Equal(fontUri, Assert.Single(manager.GlyphTypefaceUris));
        Assert.Throws<KeyNotFoundException>(() => manager.GetUsedGlyphs(new Uri("file:///missing.ttf")));
    }

    [Fact]
    public void GradientAndRenderOptionsExposeNewMediaEnums()
    {
        Assert.Equal(1, (int)BitmapScalingMode.Linear);
        Assert.Equal(2, (int)BitmapScalingMode.Fant);
        Assert.Equal(1, (int)PenLineCap.Square);
        Assert.Equal(4, (int)TileMode.Tile);
        Assert.Equal(6, (int)HitTestFilterBehavior.Continue);
        Assert.Equal(2, (int)IntersectionDetail.FullyInside);
        Assert.Equal(new Size(16384, 16384), RenderCapability.MaxHardwareTextureSize);
        Assert.True(RenderCapability.IsPixelShaderVersionSupported(3, 0));
        Assert.True(RenderCapability.IsPixelShaderVersionSupportedInSoftware(3, 0));
        Assert.Equal(512, RenderCapability.MaxPixelShaderInstructionSlots(3, 0));

        var brush = new LinearGradientBrush();
        Assert.Equal(ColorInterpolationMode.SRgbLinearInterpolation, brush.ColorInterpolationMode);
        brush.ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation;
        Assert.Equal(ColorInterpolationMode.ScRgbLinearInterpolation, brush.ColorInterpolationMode);

        var target = new DependencyObject();
        Assert.Equal(ClearTypeHint.Auto, RenderOptions.GetClearTypeHint(target));
        RenderOptions.SetClearTypeHint(target, ClearTypeHint.Enabled);
        Assert.Equal(ClearTypeHint.Enabled, RenderOptions.GetClearTypeHint(target));

        var culture = System.Globalization.CultureInfo.GetCultureInfo("ar-SA");
        NumberSubstitution.SetCultureOverride(target, culture);
        Assert.Same(culture, NumberSubstitution.GetCultureOverride(target));

        var fontBase = new Uri("https://example.test/fonts/");
        FontFamily family = Assert.Single(Fonts.GetFontFamilies(fontBase, "Composite"));
        Assert.Equal(fontBase, family.BaseUri);
        Assert.Equal("Composite", family.Source);
        Assert.Single(Fonts.GetTypefaces(fontBase, "Composite"));
    }

    [Fact]
    public void BrushesExposeDependencyPropertiesAndDeepFreezableClones()
    {
        var stops = new GradientStopCollection
        {
            new GradientStop(Colors.Red, 0),
            new GradientStop(Colors.Blue, 1),
        };
        var brush = new LinearGradientBrush(stops, new Point(0, 0), new Point(1, 0))
        {
            Opacity = 1.5,
            Transform = new ScaleTransform(2, 3),
            RelativeTransform = new TranslateTransform(4, 5),
            ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation,
        };

        LinearGradientBrush clone = brush.Clone();
        Assert.Equal(1.5, clone.Opacity);
        Assert.Equal(brush.StartPoint, clone.StartPoint);
        Assert.Equal(brush.EndPoint, clone.EndPoint);
        Assert.NotSame(brush.GradientStops, clone.GradientStops);
        Assert.NotSame(brush.Transform, clone.Transform);
        Assert.NotSame(brush.RelativeTransform, clone.RelativeTransform);

        brush.Freeze();
        Assert.True(brush.IsFrozen);
        Assert.True(brush.GradientStops.IsFrozen);
        Assert.True(brush.Transform!.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => brush.Opacity = 0.5);

        Assert.Same(Brush.OpacityProperty, typeof(Brush).GetField(nameof(Brush.OpacityProperty))!.GetValue(null));
        Assert.Same(TileBrush.ViewboxProperty, typeof(TileBrush).GetField(nameof(TileBrush.ViewboxProperty))!.GetValue(null));
    }

    [Fact]
    public void SolidAndBitmapCacheBrushCompatibilityMembersRoundTrip()
    {
        using var stream = new MemoryStream(new byte[] { 255, 10, 20, 30 });
        using var reader = new BinaryReader(stream);
        var solid = Assert.IsType<SolidColorBrush>(SolidColorBrush.DeserializeFrom(reader));
        Assert.Equal(Color.FromArgb(255, 10, 20, 30), solid.Color);
        Assert.Equal(solid.Color, solid.Clone().Color);

        var cache = new BitmapCache(2)
        {
            EnableClearType = true,
            SnapsToDevicePixels = true,
        };
        BitmapCache cacheClone = cache.Clone();
        Assert.Equal(2, cacheClone.RenderAtScale);
        Assert.True(cacheClone.EnableClearType);
        Assert.True(cacheClone.SnapsToDevicePixels);

        var cacheBrush = new BitmapCacheBrush { BitmapCache = cache, AutoLayoutContent = false };
        BitmapCacheBrush brushClone = cacheBrush.Clone();
        Assert.False(brushClone.AutoLayoutContent);
        Assert.NotSame(cache, brushClone.BitmapCache);
    }

    [Fact]
    public void DrawingGraphUsesFreezableCollectionsAndDeepClones()
    {
        var geometryDrawing = new GeometryDrawing(
            new SolidColorBrush(Colors.Red),
            new Pen(new SolidColorBrush(Colors.Black), 2),
            new RectangleGeometry(new Rect(1, 2, 10, 20)));
        var group = new DrawingGroup();
        group.Children.Add(geometryDrawing);
        group.Transform = new TranslateTransform(3, 4);
        group.OpacityMask = new SolidColorBrush(Colors.White);
        group.GuidelineSet = new GuidelineSet(new[] { 1d }, new[] { 2d });

        DrawingGroup clone = group.Clone();
        Assert.NotSame(group.Children, clone.Children);
        GeometryDrawing clonedDrawing = Assert.IsType<GeometryDrawing>(Assert.Single(clone.Children));
        Assert.NotSame(geometryDrawing, clonedDrawing);
        Assert.NotSame(geometryDrawing.Brush, clonedDrawing.Brush);
        Assert.NotSame(geometryDrawing.Pen, clonedDrawing.Pen);
        Assert.NotSame(geometryDrawing.Geometry, clonedDrawing.Geometry);
        Assert.NotSame(group.Transform, clone.Transform);

        var image = new DrawingImage(group);
        DrawingImage imageClone = image.Clone();
        Assert.NotSame(group, imageClone.Drawing);
        Assert.Null(image.Metadata);

        var drawingBrush = new DrawingBrush(group);
        DrawingBrush brushClone = drawingBrush.Clone();
        Assert.NotSame(group, brushClone.Drawing);

        group.Freeze();
        Assert.True(group.IsFrozen);
        Assert.True(group.Children.IsFrozen);
        Assert.True(geometryDrawing.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => group.Opacity = 0.5);

        DrawingCollection.Enumerator enumerator = clone.Children.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.False(enumerator.MoveNext());
    }

    private sealed class TestGeneralTransform : Jalium.UI.Media.GeneralTransform
    {
        public override Jalium.UI.Media.GeneralTransform Inverse => this;
        public override bool TryTransform(Point inPoint, out Point result)
        {
            result = inPoint;
            return true;
        }
    }
}
