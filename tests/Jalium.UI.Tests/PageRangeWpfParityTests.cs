using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Printing;

namespace Jalium.UI.Tests;

public sealed class PageRangeWpfParityTests
{
    [Fact]
    public void ConstructorsAndProperties_PreserveTheSuppliedEndpoints()
    {
        var single = new PageRange(7);
        Assert.Equal(7, single.PageFrom);
        Assert.Equal(7, single.PageTo);

        var range = new PageRange(-2, 0);
        Assert.Equal(-2, range.PageFrom);
        Assert.Equal(0, range.PageTo);

        range.PageFrom = 11;
        range.PageTo = 3;
        Assert.Equal(11, range.PageFrom);
        Assert.Equal(3, range.PageTo);
    }

    [Fact]
    public void EqualityHashAndOperators_UseBothEndpoints()
    {
        var first = new PageRange(3, 7);
        var same = new PageRange(3, 7);
        var differentStart = new PageRange(2, 7);
        var differentEnd = new PageRange(3, 8);

        Assert.True(first.Equals(same));
        Assert.True(first.Equals((object)same));
        Assert.False(first.Equals(null));
        Assert.False(first.Equals("3-7"));
        Assert.True(first == same);
        Assert.False(first != same);
        Assert.False(first == differentStart);
        Assert.False(first == differentEnd);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
    }

    [Fact]
    public void ToString_UsesTheInvariantSinglePageOrRangeForm()
    {
        Assert.Equal("0", default(PageRange).ToString());
        Assert.Equal("3", new PageRange(3).ToString());
        Assert.Equal("3-7", new PageRange(3, 7).ToString());
        Assert.Equal("7-3", new PageRange(7, 3).ToString());
        Assert.Equal("-2-0", new PageRange(-2, 0).ToString());
    }

    [Fact]
    public void PublicSurface_IsLocatedInTheWpfMappedControlsNamespace()
    {
        Assert.Equal("Jalium.UI.Controls", typeof(PageRange).Namespace);
        Assert.Equal("Jalium.UI.Controls", typeof(PageRangeSelection).Namespace);
        Assert.Null(typeof(PageRange).Assembly.GetType("Jalium.UI.Controls.Printing.PageRange"));
        Assert.Null(typeof(PageRange).Assembly.GetType("Jalium.UI.Controls.Printing.PageRangeSelection"));

        Assert.NotNull(typeof(PageRange).GetConstructor([typeof(int)]));
        Assert.NotNull(typeof(PageRange).GetConstructor([typeof(int), typeof(int)]));
        Assert.False(typeof(IEquatable<PageRange>).IsAssignableFrom(typeof(PageRange)));

        Assert.Equal(0, (int)PageRangeSelection.AllPages);
        Assert.Equal(1, (int)PageRangeSelection.UserPages);
        Assert.Equal(2, (int)PageRangeSelection.CurrentPage);
        Assert.Equal(3, (int)PageRangeSelection.SelectedPages);

        MethodInfo equals = typeof(PageRange).GetMethod(
            nameof(PageRange.Equals),
            [typeof(PageRange)])!;
        Assert.Equal(typeof(bool), equals.ReturnType);
        Assert.True(typeof(PageRange).GetMethod("op_Equality")!.IsStatic);
        Assert.True(typeof(PageRange).GetMethod("op_Inequality")!.IsStatic);
    }

    [Fact]
    public void PrintDialogPageRange_ValidatesSortsAndDoesNotClampToDialogLimits()
    {
        var dialog = new PrintDialog
        {
            MinPage = 1,
            MaxPage = 10
        };

        Assert.Equal(default(PageRange), dialog.PageRange);

        dialog.PageRange = new PageRange(8, 3);
        Assert.Equal(new PageRange(3, 8), dialog.PageRange);
        Assert.Equal(3, dialog.PageRangeFrom);
        Assert.Equal(8, dialog.PageRangeTo);

        dialog.PageRange = new PageRange(100, 200);
        Assert.Equal(new PageRange(100, 200), dialog.PageRange);

        ArgumentException fromException = Assert.Throws<ArgumentException>(
            () => dialog.PageRange = new PageRange(0, 1));
        ArgumentException toException = Assert.Throws<ArgumentException>(
            () => dialog.PageRange = new PageRange(1, 0));
        Assert.Equal("PageRange", fromException.ParamName);
        Assert.Equal("PageRange", toException.ParamName);

        PropertyInfo property = typeof(PrintDialog).GetProperty(nameof(PrintDialog.PageRange))!;
        Assert.Equal(typeof(PageRange), property.PropertyType);
        Assert.NotNull(property.GetMethod);
        Assert.NotNull(property.SetMethod);
    }
}
