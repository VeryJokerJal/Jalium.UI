using System.Globalization;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class SizeWpfParityTests
{
    [Fact]
    public void Empty_IsDistinctFromTheDefaultZeroSize()
    {
        Size zero = default;

        Assert.Equal(0, zero.Width);
        Assert.Equal(0, zero.Height);
        Assert.False(zero.IsEmpty);

        Assert.True(Size.Empty.IsEmpty);
        Assert.Equal(double.NegativeInfinity, Size.Empty.Width);
        Assert.Equal(double.NegativeInfinity, Size.Empty.Height);
        Assert.NotEqual(zero, Size.Empty);
        Assert.False(zero == Size.Empty);
        var empty1 = Size.Empty;
        var empty2 = Size.Empty;
        Assert.True(empty1 == empty2);

        Assert.False(Size.Infinity.IsEmpty);
        Assert.Equal(double.PositiveInfinity, Size.Infinity.Width);
        Assert.Equal(double.PositiveInfinity, Size.Infinity.Height);
    }

    [Fact]
    public void ConstructorAndSetters_RejectNegativeValuesButAllowNanAndPositiveInfinity()
    {
        Assert.Throws<ArgumentException>(() => new Size(-1, 0));
        Assert.Throws<ArgumentException>(() => new Size(0, -1));
        Assert.Throws<ArgumentException>(() => new Size(double.NegativeInfinity, 0));

        var size = new Size(double.NaN, double.PositiveInfinity);
        Assert.True(double.IsNaN(size.Width));
        Assert.Equal(double.PositiveInfinity, size.Height);

        size.Width = 12;
        size.Height = 34;
        Assert.Equal(new Size(12, 34), size);
        Assert.Throws<ArgumentException>(() => size.Width = -1);
        Assert.Throws<ArgumentException>(() => size.Height = double.NegativeInfinity);

        var empty = Size.Empty;
        Assert.Throws<InvalidOperationException>(() => empty.Width = 0);
        Assert.Throws<InvalidOperationException>(() => empty.Height = 0);
    }

    [Fact]
    public void Equality_UsesWpfNanAndEmptyRules()
    {
        var first = new Size(double.NaN, 1);
        var second = new Size(double.NaN, 1);

        Assert.False(first == second);
        Assert.True(first != second);
        Assert.True(Size.Equals(first, second));
        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));

        Assert.True(Size.Equals(Size.Empty, Size.Empty));
        Assert.False(Size.Equals(Size.Empty, default));
        Assert.Equal(0, Size.Empty.GetHashCode());

        var ordinary = new Size(12, 34);
        Assert.Equal(12d.GetHashCode() ^ 34d.GetHashCode(), ordinary.GetHashCode());
    }

    [Fact]
    public void Parse_UsesInvariantWpfTokenRules()
    {
        var english = CultureInfo.GetCultureInfo("en-US");

        Assert.Equal(new Size(1.5, 2.25), Size.Parse("1.5,2.25"));
        Assert.Equal(new Size(3, 4), Size.Parse(" 3  4 "));
        Assert.Equal(new Size(5, 6), Size.Parse("5 , 6"));
        Assert.Equal(Size.Empty, Size.Parse(" Empty "));
        Assert.Equal(
            new Size(double.NaN, double.PositiveInfinity),
            Size.Parse($"NaN,{english.NumberFormat.PositiveInfinitySymbol}"));

        Assert.Throws<InvalidOperationException>(() => Size.Parse(null!));
        Assert.Throws<InvalidOperationException>(() => Size.Parse(string.Empty));
        Assert.Throws<InvalidOperationException>(() => Size.Parse("1"));
        Assert.Throws<InvalidOperationException>(() => Size.Parse("1,2,3"));
        Assert.Throws<InvalidOperationException>(() => Size.Parse("1,,2"));
        Assert.Throws<InvalidOperationException>(() => Size.Parse("1,2,"));
        Assert.Throws<InvalidOperationException>(() => Size.Parse("Empty,1"));
        Assert.Throws<FormatException>(() => Size.Parse("empty"));
        Assert.Throws<ArgumentException>(() => Size.Parse("-1,2"));
    }

    [Fact]
    public void Formatting_UsesCultureSeparatorsAndIFormattableFormatStrings()
    {
        var size = new Size(1.5, 2.25);
        var french = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal("1.5,2.25", size.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("1,5;2,25", size.ToString(french));
        Assert.Equal(
            "1.50,2.25",
            ((IFormattable)size).ToString("F2", CultureInfo.InvariantCulture));
        Assert.Equal("Empty", Size.Empty.ToString());
        Assert.Equal("Empty", Size.Empty.ToString(french));
    }

    [Fact]
    public void ExplicitConversions_CopyBothDimensions()
    {
        var size = new Size(12, 34);

        Assert.Equal(new Point(12, 34), (Point)size);
        Assert.Equal(new Vector(12, 34), (Vector)size);
    }

    [Fact]
    public void LayoutZeroAndDocumentMissingSentinels_RemainDistinct()
    {
        var collapsed = new Border { Visibility = Visibility.Collapsed };
        collapsed.Measure(new Size(100, 100));
        collapsed.Arrange(new Rect(0, 0, 100, 100));

        Assert.Equal(default(Size), collapsed.DesiredSize);
        Assert.Equal(default(Size), collapsed.RenderSize);
        Assert.False(collapsed.DesiredSize.IsEmpty);
        Assert.False(collapsed.RenderSize.IsEmpty);

        Assert.True(new Jalium.UI.Documents.DocumentPage().Size.IsEmpty);
        Assert.True(new Jalium.UI.Controls.Printing.DocumentPage(null).Size.IsEmpty);
        Assert.True(Jalium.UI.Controls.Primitives.DocumentPage.Missing.Size.IsEmpty);

        var pageView = new Jalium.UI.Controls.Primitives.DocumentPageView
        {
            DocumentPaginator = new EmptyPagePaginator()
        };
        pageView.Measure(new Size(800, 600));
        Assert.Equal(default(Size), pageView.DesiredSize);
        Assert.False(pageView.DesiredSize.IsEmpty);
    }

    private sealed class EmptyPagePaginator : Jalium.UI.Controls.Primitives.DocumentPaginator
    {
        public override int PageCount => 1;
        public override bool IsPageCountValid => true;
        public override Size PageSize { get; set; } = new(800, 600);

        public override Jalium.UI.Controls.Primitives.DocumentPage GetPage(int pageNumber) =>
            new() { Size = Size.Empty };
    }
}
