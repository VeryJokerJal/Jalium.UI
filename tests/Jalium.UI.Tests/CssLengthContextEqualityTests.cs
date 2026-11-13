using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssLengthContextEqualityTests
{
    [Fact]
    public void TypedEquality_MatchesLegacyFieldEqualityIncludingReferencesAndSpecialNumbers()
    {
        var element = CssNode.Get(new DependencyObject());
        var otherElement = CssNode.Get(new DependencyObject());
        var fonts = CssFontContext.Initial;
        var viewports = CssViewportMetrics.Uniform(new Size(800, 600));
        var containers = new CssContainerUnitContext(null, null, null!, 800, 600);
        var cases = new List<CssLengthContext> { default, CssLengthContext.Default };
        CssLengthContext Create(double a = 14, double b = 16, double c = 20, double w = 800, double h = 600,
            CssContainerUnitContext? container = null, CssFontContext? font = null,
            CssViewportMetrics? viewport = null, CssNode? node = null, CssNode? dependent = null, bool provisional = false)
            => new(a, b, c, w, h, container, font, viewport, node, dependent, provisional);
        cases.Add(Create());
        cases.Add(Create(a: 15)); cases.Add(Create(b: 17)); cases.Add(Create(c: 21));
        cases.Add(Create(w: 801)); cases.Add(Create(h: 601));
        cases.Add(Create(container: containers));
        cases.Add(Create(container: containers with { }));
        cases.Add(Create(container: containers with { Width = 801 }));
        cases.Add(Create(font: fonts)); cases.Add(Create(font: CssFontContext.Initial));
        cases.Add(Create(font: fonts with { MetricsEpoch = 1 }));
        cases.Add(Create(viewport: viewports));
        cases.Add(Create(viewport: CssViewportMetrics.Uniform(new Size(800, 600))));
        cases.Add(Create(viewport: CssViewportMetrics.Uniform(new Size(801, 600))));
        cases.Add(Create(node: element)); cases.Add(Create(node: otherElement));
        cases.Add(Create(dependent: element)); cases.Add(Create(dependent: otherElement));
        cases.Add(Create(provisional: true));
        cases.Add(Create(w: 0)); cases.Add(Create(w: -0d));
        cases.Add(Create(w: double.NaN));
        cases.Add(Create(w: BitConverter.Int64BitsToDouble(0x7ff8000000000001)));
        cases.Add(Create(w: double.PositiveInfinity)); cases.Add(Create(w: double.NegativeInfinity));
        var comparer = EqualityComparer<CssLengthContext>.Default;
        foreach (var left in cases)
        foreach (var right in cases)
        {
            // No IEquatable/Equals override: this deliberately uses the runtime's
            // original field-by-field equality independently of the new method.
            bool expected = new LegacyFields(left).Equals(new LegacyFields(right));
            Assert.Equal(expected, comparer.Equals(left, right));
            Assert.Equal(expected, object.Equals(left, right));
            if (expected) Assert.Equal(comparer.GetHashCode(left), comparer.GetHashCode(right));
        }
    }

    [Fact]
    public void RepeatedDefaultInsetComparison_DoesNotBoxContextsOrFields()
    {
        var comparer = EqualityComparer<CssLengthContext>.Default;
        var context = CssLengthContext.Default;
        _ = comparer.Equals(context, context);
        _ = comparer.GetHashCode(context);
        _ = CssTextDecorationInset.Zero.Equals(CssTextDecorationInset.Zero);
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool equal = true;
        for (int index = 0; index < 2_000; index++)
        {
            equal &= comparer.Equals(context, context);
            equal &= CssTextDecorationInset.Zero.Equals(CssTextDecorationInset.Zero);
            _ = comparer.GetHashCode(context);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(equal);
        Assert.Equal(0, allocated);
    }

    private readonly struct LegacyFields(CssLengthContext context)
    {
        public readonly double ElementFontSize = context.ElementFontSize;
        public readonly double InheritedFontSize = context.InheritedFontSize;
        public readonly double RootFontSize = context.RootFontSize;
        public readonly double ViewportWidth = context.ViewportWidth;
        public readonly double ViewportHeight = context.ViewportHeight;
        public readonly CssContainerUnitContext? Containers = context.Containers;
        public readonly CssFontContext? Fonts = context.Fonts;
        public readonly CssViewportMetrics? Viewports = context.Viewports;
        public readonly CssNode? Element = context.Element;
        public readonly CssNode? Dependent = context.Dependent;
        public readonly bool ProvisionalTreeCounts = context.ProvisionalTreeCounts;
    }
}
