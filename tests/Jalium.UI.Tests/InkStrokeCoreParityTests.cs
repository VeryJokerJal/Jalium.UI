using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Ink;
using Jalium.UI.Ink.Shaders;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class InkStrokeCoreParityTests
{
    [Fact]
    public void RefreshedInkContractsExposeWpfMembersAndHandlerTypes()
    {
        Assert.Equal(0, (int)StylusTip.Rectangle);
        Assert.Equal(1, (int)StylusTip.Ellipse);
        Assert.True(typeof(DrawingAttributeIds).IsAbstract);
        Assert.True(typeof(DrawingAttributeIds).IsSealed);

        Assert.Equal(
            typeof(PropertyDataChangedEventHandler),
            typeof(DrawingAttributes).GetEvent(nameof(DrawingAttributes.AttributeChanged))!.EventHandlerType);
        Assert.Equal(
            typeof(PropertyDataChangedEventHandler),
            typeof(Stroke).GetEvent(nameof(Stroke.DrawingAttributesChanged))!.EventHandlerType);
        Assert.Equal(
            typeof(DrawingAttributesReplacedEventHandler),
            typeof(Stroke).GetEvent(nameof(Stroke.DrawingAttributesReplaced))!.EventHandlerType);
        Assert.Equal(
            typeof(StylusPointsReplacedEventHandler),
            typeof(Stroke).GetEvent(nameof(Stroke.StylusPointsReplaced))!.EventHandlerType);

        Assert.NotNull(typeof(StrokeCollection).GetConstructor([typeof(Stream)]));
        Assert.NotNull(typeof(Stroke).GetMethod(nameof(Stroke.Draw),
            [typeof(DrawingContext), typeof(DrawingAttributes)]));
        Assert.NotNull(typeof(Stroke).GetMethod(nameof(Stroke.GetBezierStylusPoints)));
        Assert.NotNull(typeof(StrokeCollection).GetMethod(nameof(StrokeCollection.Save),
            [typeof(Stream), typeof(bool)]));

        AssertProtectedVirtual<Stroke>("DrawCore", typeof(DrawingContext), typeof(DrawingAttributes));
        AssertProtectedVirtual<Stroke>("OnDrawingAttributesChanged", typeof(PropertyDataChangedEventArgs));
        AssertProtectedVirtual<Stroke>("OnDrawingAttributesReplaced", typeof(DrawingAttributesReplacedEventArgs));
        AssertProtectedVirtual<Stroke>("OnStylusPointsReplaced", typeof(StylusPointsReplacedEventArgs));
        AssertProtectedVirtual<StrokeCollection>("OnStrokesChanged", typeof(StrokeCollectionChangedEventArgs));
        AssertProtectedVirtual<DrawingAttributes>("OnAttributeChanged", typeof(PropertyDataChangedEventArgs));
    }

    [Fact]
    public void DrawingAttributesDefaultsValidationPropertyDataAndCloneAreFunctional()
    {
        var attributes = new DrawingAttributes();

        Assert.Equal(2.0031496062992127, attributes.Width);
        Assert.Equal(2.0031496062992127, attributes.Height);
        Assert.Equal(Colors.Black, attributes.Color);
        Assert.Equal(StylusTip.Ellipse, attributes.StylusTip);
        Assert.Equal(Matrix.Identity, attributes.StylusTipTransform);
        Assert.False(attributes.FitToCurve);
        Assert.False(attributes.IgnorePressure);
        Assert.False(attributes.IsHighlighter);

        Assert.Throws<ArgumentOutOfRangeException>(() => attributes.Width = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => attributes.Height = double.NaN);
        Assert.Throws<ArgumentException>(() => attributes.StylusTip = (StylusTip)42);
        Assert.Throws<ArgumentException>(() => attributes.StylusTipTransform =
            new Matrix(1, 0, 0, 1, 1, 0));
        Assert.Throws<ArgumentException>(() => attributes.StylusTipTransform =
            new Matrix(0, 0, 0, 0, 0, 0));

        var changedProperties = new List<string?>();
        var attributesChanged = new List<PropertyDataChangedEventArgs>();
        ((INotifyPropertyChanged)attributes).PropertyChanged +=
            (_, args) => changedProperties.Add(args.PropertyName);
        attributes.AttributeChanged += (_, args) => attributesChanged.Add(args);

        attributes.Width = 8;

        PropertyDataChangedEventArgs widthChange = Assert.Single(attributesChanged);
        Assert.Equal(DrawingAttributeIds.StylusWidth, widthChange.PropertyGuid);
        Assert.Equal(8d, widthChange.NewValue);
        Assert.Equal(2.0031496062992127, widthChange.PreviousValue);
        Assert.Equal(new[] { nameof(DrawingAttributes.Width) }, changedProperties);
        Assert.True(attributes.ContainsPropertyData(DrawingAttributeIds.StylusWidth));
        Assert.Equal(8d, attributes.GetPropertyData(DrawingAttributeIds.StylusWidth));

        Guid customId = Guid.NewGuid();
        var original = new[] { 1, 2, 3 };
        PropertyDataChangedEventArgs? customChange = null;
        attributes.PropertyDataChanged += (_, args) => customChange = args;
        attributes.AddPropertyData(customId, original);
        original[0] = 99;

        Assert.Equal(new[] { 1, 2, 3 }, Assert.IsType<int[]>(attributes.GetPropertyData(customId)));
        Assert.Equal(customId, customChange!.PropertyGuid);
        Assert.Null(customChange.PreviousValue);

        DrawingAttributes clone = attributes.Clone();
        Assert.True(attributes == clone);
        Assert.NotSame(attributes.GetPropertyData(customId), clone.GetPropertyData(customId));
        clone.Width = 9;
        Assert.True(attributes != clone);

        attributes.RemovePropertyData(DrawingAttributeIds.StylusWidth);
        Assert.Equal(2.0031496062992127, attributes.Width);
        Assert.False(attributes.ContainsPropertyData(DrawingAttributeIds.StylusWidth));
        Assert.Throws<ArgumentException>(() => attributes.RemovePropertyData(Guid.NewGuid()));
    }

    [Fact]
    public void StrokeOwnsRealPointsRaisesReplacementEventsAndDeepClones()
    {
        Assert.Throws<ArgumentException>(() => new Stroke(new StylusPointCollection()));

        var originalPoints = Points((0, 0, 0.25f), (10, 0, 0.75f));
        var attributes = new DrawingAttributes { Width = 2, Height = 4 };
        var stroke = new Stroke(originalPoints, attributes);
        Guid customId = Guid.NewGuid();
        stroke.AddPropertyData(customId, "metadata");

        Assert.Same(originalPoints, stroke.StylusPoints);
        Assert.Equal(new Rect(-1, -2, 12, 4), stroke.GetBounds());
        Assert.True(stroke.HitTest(new Point(5, 0)));
        Assert.False(stroke.HitTest(new Point(5, 10)));
        Assert.Throws<InvalidOperationException>(() => originalPoints.Clear());
        Assert.Equal(2, originalPoints.Count);

        StylusPointsReplacedEventArgs? pointsReplaced = null;
        DrawingAttributesReplacedEventArgs? attributesReplaced = null;
        stroke.StylusPointsReplaced += (_, args) => pointsReplaced = args;
        stroke.DrawingAttributesReplaced += (_, args) => attributesReplaced = args;
        var replacementPoints = Points((20, 30, 0.5f), (25, 35, 0.5f));
        var replacementAttributes = new DrawingAttributes { Width = 6, Height = 8 };

        stroke.StylusPoints = replacementPoints;
        stroke.DrawingAttributes = replacementAttributes;

        Assert.Same(originalPoints, pointsReplaced!.PreviousStylusPoints);
        Assert.Same(replacementPoints, pointsReplaced.NewStylusPoints);
        Assert.Same(attributes, attributesReplaced!.PreviousDrawingAttributes);
        Assert.Same(replacementAttributes, attributesReplaced.NewDrawingAttributes);

        Stroke clone = stroke.Clone();
        Assert.NotSame(stroke, clone);
        Assert.NotSame(stroke.StylusPoints, clone.StylusPoints);
        Assert.NotSame(stroke.DrawingAttributes, clone.DrawingAttributes);
        Assert.Equal(stroke.StylusPoints.ToArray(), clone.StylusPoints.ToArray());
        Assert.Equal("metadata", clone.GetPropertyData(customId));

        clone.StylusPoints[0] = new StylusPoint(100, 100);
        Assert.NotEqual(clone.StylusPoints[0], stroke.StylusPoints[0]);
    }

    [Fact]
    public void StrokeTransformBezierHitClipEraseAndDrawingOperateOnGeometry()
    {
        var stroke = new Stroke(
            Points((0, 0, 0.2f), (5, 5, 0.5f), (10, 0, 0.8f)),
            new DrawingAttributes { Width = 2, Height = 2, FitToCurve = true });

        StylusPointCollection bezier = stroke.GetBezierStylusPoints();
        Assert.True(bezier.Count > stroke.StylusPoints.Count);
        Assert.Equal(stroke.StylusPoints[0].ToPoint(), bezier[0].ToPoint());
        Assert.Equal(stroke.StylusPoints[^1].ToPoint(), bezier[^1].ToPoint());

        Assert.True(stroke.HitTest(
            [new Point(-1, -1), new Point(11, -1), new Point(11, 6), new Point(-1, 6)],
            100));
        Assert.True(stroke.HitTest([new Point(5, -5), new Point(5, 10)],
            new RectangleStylusShape(2, 2)));

        StrokeCollection clipped = stroke.GetClipResult(new Rect(3, -1, 4, 7));
        Stroke clippedStroke = Assert.Single(clipped);
        Assert.InRange(clippedStroke.StylusPoints[0].X, 3, 4);
        Assert.InRange(clippedStroke.StylusPoints[^1].X, 6, 7);

        StrokeCollection erased = stroke.GetEraseResult(new Rect(3, -1, 4, 7));
        Assert.Equal(2, erased.Count);

        stroke.Transform(new Matrix(2, 0, 0, 3, 7, 11), applyToStylusTip: true);
        Assert.Equal(new Point(7, 11), stroke.StylusPoints[0].ToPoint());
        Assert.Equal(new Point(27, 11), stroke.StylusPoints[^1].ToPoint());
        Assert.Equal(new Matrix(2, 0, 0, 3, 0, 0), stroke.DrawingAttributes.StylusTipTransform);

        var drawing = new DrawingGroup();
        using DrawingContext context = drawing.Open();
        stroke.Draw(context);
        stroke.Draw(context, new DrawingAttributes { Color = Colors.Red, Width = 5, Height = 5 });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StrokeCollectionStreamRoundTripPreservesPointsAttributesAndPropertyData(bool compress)
    {
        Guid collectionId = Guid.NewGuid();
        Guid strokeId = Guid.NewGuid();
        Guid attributesId = Guid.NewGuid();
        var customPacketProperty = new StylusPointProperty(Guid.NewGuid(), isButton: false);
        var packetProperties = new StylusPointDescription().GetStylusPointProperties().ToList();
        packetProperties.Add(new StylusPointPropertyInfo(
            customPacketProperty,
            0,
            100,
            StylusPointPropertyUnit.None,
            1));
        var packetDescription = new StylusPointDescription(packetProperties);
        var packetPoints = new StylusPointCollection(packetDescription)
        {
            new StylusPoint(1, 2, 0.25f, packetDescription, [42]),
            new StylusPoint(8, 13, 0.9f, packetDescription, [73]),
        };
        var attributes = new DrawingAttributes
        {
            Color = Color.FromArgb(128, 10, 20, 30),
            Width = 7,
            Height = 9,
            StylusTip = StylusTip.Rectangle,
            StylusTipTransform = new Matrix(2, 0, 0, 3, 0, 0),
            FitToCurve = true,
            IgnorePressure = true,
            IsHighlighter = true,
            BrushType = BrushType.Calligraphy,
            BrushShader = EraserBrushShader.Instance,
        };
        attributes.AddPropertyData(attributesId, new[] { 1.5, 2.5 });
        var stroke = new Stroke(packetPoints, attributes)
        {
            TaperMode = StrokeTaperMode.TaperedEnd,
        };
        stroke.AddPropertyData(strokeId, "stroke-data");
        var source = new StrokeCollection([stroke]);
        source.AddPropertyData(collectionId, 1234L);

        using var stream = new MemoryStream();
        source.Save(stream, compress);
        stream.Position = 0;
        var restored = new StrokeCollection(stream);

        Assert.Equal(1234L, restored.GetPropertyData(collectionId));
        Stroke restoredStroke = Assert.Single(restored);
        Assert.Equal(stroke.StylusPoints.ToArray(), restoredStroke.StylusPoints.ToArray());
        Assert.Equal(42, restoredStroke.StylusPoints[0].GetPropertyValue(customPacketProperty));
        Assert.Equal(73, restoredStroke.StylusPoints[1].GetPropertyValue(customPacketProperty));
        Assert.Equal(stroke.DrawingAttributes, restoredStroke.DrawingAttributes);
        Assert.Equal(StrokeTaperMode.TaperedEnd, restoredStroke.TaperMode);
        Assert.Equal("stroke-data", restoredStroke.GetPropertyData(strokeId));
        Assert.Equal(
            new[] { 1.5, 2.5 },
            Assert.IsType<double[]>(restoredStroke.DrawingAttributes.GetPropertyData(attributesId)));
    }

    [Fact]
    public void StrokeCollectionBulkMutationHitTestingAndIncrementalEventsAreFunctional()
    {
        Stroke first = Line(0, 0, 10, 0);
        Stroke second = Line(0, 20, 10, 20);
        var strokes = new StrokeCollection();
        var changes = new List<StrokeCollectionChangedEventArgs>();
        var collectionChanges = new List<NotifyCollectionChangedEventArgs>();
        strokes.StrokesChanged += (_, args) => changes.Add(args);
        ((INotifyCollectionChanged)strokes).CollectionChanged +=
            (_, args) => collectionChanges.Add(args);

        strokes.Add(new StrokeCollection([first, second]));

        Assert.Equal(2, strokes.Count);
        StrokeCollectionChangedEventArgs added = Assert.Single(changes);
        Assert.Equal(new[] { first, second }, added.Added.ToArray());
        Assert.Empty(added.Removed);
        Assert.Single(collectionChanges);
        Assert.Same(first, Assert.Single(strokes.HitTest(new Point(5, 0), 2)));
        Assert.Same(second, Assert.Single(strokes.HitTest(new Rect(-1, 19, 12, 2), 50)));

        changes.Clear();
        Stroke replacement = Line(30, 30, 40, 30);
        strokes.Replace(first, new StrokeCollection([replacement]));
        StrokeCollectionChangedEventArgs replaced = Assert.Single(changes);
        Assert.Same(first, Assert.Single(replaced.Removed));
        Assert.Same(replacement, Assert.Single(replaced.Added));

        var incremental = strokes.GetIncrementalLassoHitTester(50);
        LassoSelectionChangedEventArgs? selection = null;
        incremental.SelectionChanged += (_, args) => selection = args;
        incremental.AddPoints(new StylusPointCollection(
        [
            new StylusPoint(-2, 18),
            new StylusPoint(12, 18),
            new StylusPoint(12, 22),
            new StylusPoint(-2, 22),
        ]));

        Assert.NotNull(selection);
        Assert.Same(second, Assert.Single(selection!.SelectedStrokes));
    }

    [Fact]
    public void EmptyCollectionUsesCanonicalWpfPayloadAndRejectsCorruption()
    {
        using var stream = new MemoryStream();
        new StrokeCollection().Save(stream);
        Assert.Equal("AAYCAA8AHwA=", Convert.ToBase64String(stream.ToArray()));

        stream.Position = 0;
        Assert.Empty(new StrokeCollection(stream));
        Assert.Throws<ArgumentException>(() =>
            new StrokeCollection(new MemoryStream([1, 2, 3])));
    }

    private static Stroke Line(double x1, double y1, double x2, double y2) =>
        new(Points((x1, y1, 0.5f), (x2, y2, 0.5f)));

    private static StylusPointCollection Points(params (double X, double Y, float Pressure)[] points) =>
        new(points.Select(static point => new StylusPoint(point.X, point.Y, point.Pressure)));

    private static void AssertProtectedVirtual<T>(string name, params Type[] parameterTypes)
    {
        MethodInfo? method = typeof(T).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameterTypes,
            modifiers: null);
        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);
    }
}
