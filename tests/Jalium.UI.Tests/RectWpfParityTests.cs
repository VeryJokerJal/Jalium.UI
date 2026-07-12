using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class RectWpfParityTests
{
    [Fact]
    public void Empty_IsTheWpfSentinel_AndZeroAreaRectsRemainNonEmpty()
    {
        Rect zero = default;

        Assert.False(zero.IsEmpty);
        Assert.Equal(new Rect(0, 0, 0, 0), zero);
        Assert.Equal(default(Size), zero.Size);
        Assert.True(zero.Contains(0, 0));

        Rect empty = Rect.Empty;
        Assert.True(empty.IsEmpty);
        Assert.Equal(double.PositiveInfinity, empty.X);
        Assert.Equal(double.PositiveInfinity, empty.Y);
        Assert.Equal(double.NegativeInfinity, empty.Width);
        Assert.Equal(double.NegativeInfinity, empty.Height);
        Assert.Equal(double.NegativeInfinity, empty.Right);
        Assert.Equal(double.NegativeInfinity, empty.Bottom);
        Assert.True(empty.Size.IsEmpty);
        Assert.False(empty.Contains(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(0, empty.GetHashCode());
    }

    [Fact]
    public void Constructors_FollowWpfSizeAndNegativeDimensionRules()
    {
        Assert.Equal(Rect.Empty, new Rect(new Point(1, 2), Size.Empty));
        Assert.Equal(Rect.Empty, new Rect(Size.Empty));
        Assert.Equal(new Rect(0, 0, 3, 4), new Rect(new Size(3, 4)));
        Assert.Equal(new Rect(2, -4, 10, 9), new Rect(new Point(12, -4), new Point(2, 5)));
        Assert.Equal(new Rect(7, 15, 3, 5), new Rect(new Point(10, 20), new Vector(-3, -5)));

        Assert.Throws<ArgumentException>(() => new Rect(0, 0, -1, 0));
        Assert.Throws<ArgumentException>(() => new Rect(0, 0, 0, double.NegativeInfinity));

        var nan = new Rect(0, 0, double.NaN, double.PositiveInfinity);
        Assert.True(double.IsNaN(nan.Width));
        Assert.Equal(double.PositiveInfinity, nan.Height);
    }

    [Fact]
    public void MutableProperties_RejectMutationOfEmptyAndNegativeDimensions()
    {
        var rect = new Rect(1, 2, 3, 4)
        {
            Location = new Point(10, 20),
            Size = new Size(30, 40)
        };

        rect.X = 11;
        rect.Y = 22;
        rect.Width = 33;
        rect.Height = 44;
        Assert.Equal(new Rect(11, 22, 33, 44), rect);

        Assert.Throws<ArgumentException>(() => rect.Width = -1);
        Assert.Throws<ArgumentException>(() => rect.Height = -1);

        rect.Size = Size.Empty;
        Assert.True(rect.IsEmpty);
        rect.Size = Size.Empty;
        Assert.True(rect.IsEmpty);
        Assert.Throws<InvalidOperationException>(() => rect.X = 0);
        Assert.Throws<InvalidOperationException>(() => rect.Y = 0);
        Assert.Throws<InvalidOperationException>(() => rect.Width = 0);
        Assert.Throws<InvalidOperationException>(() => rect.Height = 0);
        Assert.Throws<InvalidOperationException>(() => rect.Location = default);
        Assert.Throws<InvalidOperationException>(() => rect.Size = default);
    }

    [Fact]
    public void ContainsAndIntersection_AreInclusiveOfCoincidentEdges()
    {
        var rect = new Rect(10, 20, 30, 40);

        Assert.True(rect.Contains(new Point(10, 20)));
        Assert.True(rect.Contains(40, 60));
        Assert.False(rect.Contains(40.0001, 60));
        Assert.True(rect.Contains(new Rect(10, 20, 30, 40)));
        Assert.False(rect.Contains(Rect.Empty));

        var touching = new Rect(40, 25, 5, 10);
        Assert.True(rect.IntersectsWith(touching));
        Rect edge = Rect.Intersect(rect, touching);
        Assert.False(edge.IsEmpty);
        Assert.Equal(new Rect(40, 25, 0, 10), edge);

        rect.Intersect(new Rect(100, 100, 1, 1));
        Assert.True(rect.IsEmpty);
    }

    [Fact]
    public void Union_HandlesEmptyPointsAndInfiniteExtents()
    {
        var rect = new Rect(2, 3, 4, 5);

        Assert.Equal(rect, Rect.Union(Rect.Empty, rect));
        Assert.Equal(rect, Rect.Union(rect, Rect.Empty));
        Assert.Equal(new Rect(-2, 3, 8, 7), Rect.Union(rect, new Point(-2, 10)));

        rect.Union(new Rect(20, 30, 2, 3));
        Assert.Equal(new Rect(2, 3, 20, 30), rect);

        var unbounded = new Rect(double.NegativeInfinity, 0, double.PositiveInfinity, 10);
        Rect union = Rect.Union(unbounded, new Rect(1, -5, 2, 3));
        Assert.Equal(double.NegativeInfinity, union.X);
        Assert.Equal(double.PositiveInfinity, union.Width);
        Assert.False(double.IsNaN(union.Width));
    }

    [Fact]
    public void OffsetInflateAndScale_MutateAndProvideStaticCopies()
    {
        var rect = new Rect(2, 3, 4, 5);

        Assert.Equal(new Rect(12, 23, 4, 5), Rect.Offset(rect, 10, 20));
        Assert.Equal(new Rect(1, 5, 4, 5), Rect.Offset(rect, new Vector(-1, 2)));
        rect.Offset(3, -1);
        Assert.Equal(new Rect(5, 2, 4, 5), rect);

        Assert.Equal(new Rect(0, 1, 8, 9), Rect.Inflate(new Rect(2, 3, 4, 5), 2, 2));
        Assert.Equal(new Rect(1, 2, 6, 7), Rect.Inflate(new Rect(2, 3, 4, 5), new Size(1, 1)));

        var collapsed = new Rect(0, 0, 10, 10);
        collapsed.Inflate(-6, 0);
        Assert.True(collapsed.IsEmpty);

        var scaled = new Rect(2, 3, 4, 5);
        scaled.Scale(-2, -3);
        Assert.Equal(new Rect(-12, -24, 8, 15), scaled);

        Assert.Throws<InvalidOperationException>(() => Rect.Offset(Rect.Empty, 1, 1));
        Assert.Throws<InvalidOperationException>(() => Rect.Inflate(Rect.Empty, 1, 1));
    }

    [Fact]
    public void Transform_ReturnsTheAxisAlignedBoundsAndLeavesEmptyUnchanged()
    {
        var rect = new Rect(1, 2, 3, 4);
        var matrix = new Matrix(0, 1, -1, 0, 10, 20);

        Assert.Equal(new Rect(4, 21, 4, 3), Rect.Transform(rect, matrix));

        rect.Transform(new Matrix(2, 0, 0, 3, 5, 7));
        Assert.Equal(new Rect(7, 13, 6, 12), rect);

        Rect empty = Rect.Empty;
        empty.Transform(matrix);
        empty.Scale(-2, -3);
        Assert.Equal(Rect.Empty, empty);

        var unbounded = new Rect(double.NegativeInfinity, 1, double.PositiveInfinity, 2);
        unbounded.Transform(Matrix.Identity);
        Assert.Equal(double.NegativeInfinity, unbounded.X);
        Assert.Equal(double.PositiveInfinity, unbounded.Width);
        Assert.False(double.IsNaN(unbounded.X));
        Assert.False(double.IsNaN(unbounded.Width));

        var reflected = new Rect(2, 3, 4, 5);
        reflected.Transform(new Matrix(-2, 0, 0, -3, 10, 20));
        Assert.Equal(new Rect(-2, -4, 8, 15), reflected);
    }

    [Fact]
    public void Equality_UsesWpfNanRulesAndFieldwiseHashing()
    {
        var first = new Rect(double.NaN, 2, 3, 4);
        var second = new Rect(double.NaN, 2, 3, 4);

        Assert.False(first == second);
        Assert.True(first != second);
        Assert.True(Rect.Equals(first, second));
        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));
        Assert.True(Rect.Equals(Rect.Empty, Rect.Empty));
        Assert.False(Rect.Equals(Rect.Empty, default));

        var ordinary = new Rect(1, 2, 3, 4);
        Assert.Equal(
            1d.GetHashCode() ^ 2d.GetHashCode() ^ 3d.GetHashCode() ^ 4d.GetHashCode(),
            ordinary.GetHashCode());
    }

    [Fact]
    public void ParseAndFormatting_UseInvariantInputAndCultureSpecificOutput()
    {
        var rect = new Rect(1.5, 2.25, 3.5, 4.75);
        var french = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(rect, Rect.Parse(" 1.5, 2.25, 3.5, 4.75 "));
        Assert.Equal(rect, Rect.Parse("1.5 2.25 3.5 4.75"));
        Assert.Equal(Rect.Empty, Rect.Parse(" Empty "));
        Assert.Throws<InvalidOperationException>(() => Rect.Parse("1,2,3"));
        Assert.Throws<InvalidOperationException>(() => Rect.Parse("Empty,1"));
        Assert.Throws<FormatException>(() => Rect.Parse("empty"));

        Assert.Equal("1.5,2.25,3.5,4.75", rect.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("1,5;2,25;3,5;4,75", rect.ToString(french));
        Assert.Equal(
            "1.50,2.25,3.50,4.75",
            ((IFormattable)rect).ToString("F2", CultureInfo.InvariantCulture));
        Assert.Equal("Empty", Rect.Empty.ToString(french));
    }

    [Fact]
    public void TypeMetadataAndCollapsedScrollBarArrange_AreCompatible()
    {
        Assert.IsType<RectConverter>(TypeDescriptor.GetConverter(typeof(Rect)));

        var scrollViewer = new ScrollViewer();
        scrollViewer.Measure(new Size(100, 100));
        scrollViewer.Arrange(new Rect(0, 0, 100, 100));
    }
}
