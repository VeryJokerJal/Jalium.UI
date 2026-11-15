using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class DrawingPrimitiveConstructionTests
{
    [Fact]
    public void DefaultConstructorArgumentsStillCreateLocalValues()
    {
        var brush = new SolidColorBrush();
        Assert.Equal(Color.Transparent, Assert.IsType<Color>(brush.ReadLocalValue(SolidColorBrush.ColorProperty)));
        var pen = new Pen(null!, 1d);
        Assert.Null(pen.ReadLocalValue(Pen.BrushProperty));
        Assert.Equal(1d, Assert.IsType<double>(pen.ReadLocalValue(Pen.ThicknessProperty)));
    }

    [Fact]
    public void InitialWriteReplacesAnExistingLocalCurrentValue()
    {
        var pen = new Pen(null!, 1d);
        pen.SetCurrentValue(Pen.ThicknessProperty, 8d);
        pen.SetInitialLocalValue(Pen.ThicknessProperty, 3d);
        Assert.Equal(3d, pen.Thickness);
        pen.ClearValue(Pen.ThicknessProperty);
        Assert.Equal(1d, pen.Thickness);
    }

    [Fact]
    public void InitialColorWritePreservesReentrantChangedNotifications()
    {
        var brush = new SolidColorBrush();
        brush.ClearValue(SolidColorBrush.ColorProperty);
        int changes = 0;
        brush.Changed += (_, _) => { if (++changes == 1) brush.Color = Colors.Blue; };
        brush.SetInitialLocalValue(SolidColorBrush.ColorProperty, Colors.Red);
        Assert.Equal(2, changes);
        Assert.Equal(Colors.Blue, brush.Color);
    }

    [Fact]
    public void InitialColorWritePreservesSuppliedMutableBox()
    {
        var brush = new SolidColorBrush();
        brush.ClearValue(SolidColorBrush.ColorProperty);
        var independent = new SolidColorBrush(Colors.Red);
        object color = Colors.Red;
        brush.SetInitialLocalValue(SolidColorBrush.ColorProperty, color);
        Assert.Same(color, brush.ReadLocalValue(SolidColorBrush.ColorProperty));
        typeof(Color).GetProperty(nameof(Color.R))!.SetValue(color, (byte)12);
        Assert.Equal(12, brush.Color.R);
        Assert.Equal(Colors.Red, independent.Color);
    }

    [Fact]
    public void InitialWriteRetainsFrozenTypeAndReadOnlyGuards()
    {
        var brush = new SolidColorBrush(Colors.Red);
        brush.Freeze();
        Assert.Throws<InvalidOperationException>(() => brush.SetInitialLocalValue(SolidColorBrush.ColorProperty, Colors.Red));
        var pen = new Pen();
        Assert.Throws<ArgumentException>(() => pen.SetInitialLocalValue(Pen.ThicknessProperty, "wrong type"));
        Assert.Throws<InvalidOperationException>(() => new CoercedOwner().SetInitialLocalValue(CoercedOwner.ReadOnlyProperty, 2d));
    }

    [Fact]
    public void InitialWriteUsesCoercionAndMetadataCallbacksForOtherOwners()
    {
        var owner = new CoercedOwner();
        owner.SetInitialLocalValue(CoercedOwner.LevelProperty, 10d);
        Assert.Equal(5d, owner.GetValue(CoercedOwner.LevelProperty));
        Assert.Equal(10d, owner.ReadLocalValue(CoercedOwner.LevelProperty));
        Assert.Equal(new[] { 5d }, owner.Notifications);
    }

    [Fact]
    public void InitialNullWriteRetainsNonNullableValueFallback()
    {
        var pen = new Pen(null!, 2d);
        pen.SetInitialLocalValue(Pen.ThicknessProperty, null);
        Assert.Equal(1d, pen.Thickness);
        Assert.Same(DependencyProperty.UnsetValue, pen.ReadLocalValue(Pen.ThicknessProperty));
    }

    [Fact]
    public void InitialWriteRetainsAnExistingTwoWayBinding()
    {
        var source = new ValueSource { Value = 2d };
        var pen = new Pen();
        pen.SetBinding(Pen.ThicknessProperty, new Binding(nameof(ValueSource.Value)) { Source = source, Mode = BindingMode.TwoWay });
        pen.SetInitialLocalValue(Pen.ThicknessProperty, 3d);
        Assert.Equal(3d, pen.Thickness);
        Assert.Equal(3d, source.Value);
        Assert.NotNull(pen.GetBindingExpression(Pen.ThicknessProperty));
    }

    [Theory]
    [InlineData("Geometry", false)]
    [InlineData("Geometry", true)]
    [InlineData("Figure", false)]
    [InlineData("Figure", true)]
    [InlineData("Polyline", false)]
    [InlineData("Polyline", true)]
    public void ConstructedCollectionsAreIndependentLocalValuesAndNotifyTheirOwner(string kind, bool populated)
    {
        var owner = CreateCollectionOwner(kind, populated);
        var property = CollectionProperty(kind);
        var child = Assert.IsAssignableFrom<Freezable>(owner.GetValue(property));
        Assert.Same(child, owner.ReadLocalValue(property));
        Assert.NotSame(child, CreateCollectionOwner(kind, populated).GetValue(property));

        int changes = 0;
        owner.Changed += (_, _) => changes++;
        MutateCollection(child);
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData("Geometry")]
    [InlineData("Figure")]
    [InlineData("Polyline")]
    public void ReplacingConstructedCollectionsDetachesTheOldOwnerSubscription(string kind)
    {
        var owner = CreateCollectionOwner(kind, true);
        var old = (Freezable)owner.GetValue(CollectionProperty(kind))!;
        int changes = 0;
        owner.Changed += (_, _) => changes++;
        MutateCollection(old);
        Assert.Equal(1, changes);

        switch (owner)
        {
            case PathGeometry geometry: geometry.Figures = new PathFigureCollection(); break;
            case PathFigure figure: figure.Segments = new PathSegmentCollection(); break;
            case PolyLineSegment segment: segment.Points = new PointCollection(); break;
        }
        Assert.Equal(2, changes);
        MutateCollection(old);
        Assert.Equal(2, changes);
        MutateCollection((Freezable)owner.GetValue(CollectionProperty(kind))!);
        Assert.Equal(3, changes);
    }

    [Theory]
    [InlineData("Geometry")]
    [InlineData("Figure")]
    [InlineData("Polyline")]
    public void ConstructedCollectionsCloneIndependentlyAndFreezeWithTheirOwner(string kind)
    {
        var owner = CreateCollectionOwner(kind, true);
        var property = CollectionProperty(kind);
        var child = (Freezable)owner.GetValue(property)!;
        var clone = owner.Clone();
        var cloneChild = (Freezable)clone.GetValue(property)!;
        Assert.NotSame(child, cloneChild);
        int sourceCount = ((System.Collections.IList)child).Count;
        MutateCollection(cloneChild);
        Assert.Equal(sourceCount, ((System.Collections.IList)child).Count);
        Assert.Equal(sourceCount + 1, ((System.Collections.IList)cloneChild).Count);

        owner.Freeze();
        Assert.True(child.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => MutateCollection(child));
        MutateCollection(cloneChild);
    }

    [Fact]
    public void ParameterizedGeometryCopiesTheSequenceOnceAndRetainsItsFiguresAndOptions()
    {
        var figure = new PathFigure();
        int enumerations = 0;
        IEnumerable<PathFigure> Enumerate()
        {
            enumerations++;
            yield return figure;
        }
        var transform = new TranslateTransform { X = 3, Y = 4 };
        var geometry = new PathGeometry(Enumerate(), FillRule.Nonzero, transform);
        Assert.Equal(1, enumerations);
        Assert.Same(figure, Assert.Single(geometry.Figures));
        Assert.Equal(FillRule.Nonzero, geometry.FillRule);
        Assert.Same(transform, geometry.Transform);

        int changes = 0;
        geometry.Changed += (_, _) => changes++;
        figure.StartPoint = new Point(5, 6);
        Assert.Equal(1, changes);
        Assert.Throws<ArgumentNullException>(() => new PathGeometry(null!));
    }

    private static Freezable CreateCollectionOwner(string kind, bool populated) => kind switch
    {
        "Geometry" => populated ? new PathGeometry(new[] { new PathFigure() }) : new PathGeometry(),
        "Figure" => populated ? new PathFigure(new Point(1, 2), new[] { new LineSegment() }, true) : new PathFigure(),
        _ => populated ? new PolyLineSegment(new[] { new Point(1, 2) }, false) : new PolyLineSegment(),
    };

    private static DependencyProperty CollectionProperty(string kind) => kind switch
    {
        "Geometry" => PathGeometry.FiguresProperty,
        "Figure" => PathFigure.SegmentsProperty,
        _ => PolyLineSegment.PointsProperty,
    };

    private static void MutateCollection(Freezable child)
    {
        switch (child)
        {
            case PathFigureCollection figures: figures.Add(new PathFigure()); break;
            case PathSegmentCollection segments: segments.Add(new LineSegment()); break;
            case PointCollection points: points.Add(new Point(8, 9)); break;
            default: throw new InvalidOperationException("Unexpected collection type.");
        }
    }

    private sealed class ValueSource { public double Value { get; set; } }

    private sealed class CoercedOwner : DependencyObject
    {
        internal readonly List<double> Notifications = new();
        internal static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
            "Level", typeof(double), typeof(CoercedOwner), new PropertyMetadata(0d,
                (owner, args) => ((CoercedOwner)owner).Notifications.Add((double)args.NewValue!),
                (_, value) => Math.Min(5d, (double)value!)));
        private static readonly DependencyPropertyKey ReadOnlyKey = DependencyProperty.RegisterReadOnly(
            "ReadOnly", typeof(double), typeof(CoercedOwner), new PropertyMetadata(0d));
        internal static DependencyProperty ReadOnlyProperty => ReadOnlyKey.DependencyProperty;
    }
}
