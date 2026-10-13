using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSAutomationVisibilityTests
{
    [Fact]
    public void ConcaveClipUsesFilledAreaAndProducesAnInteriorClickPoint()
    {
        var owner = Arrange(new Button { Clip = Geometry.Parse("M0,0 L80,0 L80,20 L20,20 L20,80 L0,80 Z") });
        var visible = new AutomationVisibility(owner);
        Assert.False(visible.TryClip(new Rect(50, 50, 10, 10), out _));
        Assert.True(visible.TryClip(new Rect(15, 15, 20, 20), out Rect bounds));
        Assert.Equal(new Rect(15, 15, 20, 20), bounds);
        Point clickable = owner.GetAutomationPeer()!.GetClickablePoint();
        Assert.True(visible.Contains(clickable)); Assert.False(double.IsNaN(clickable.X));
        Assert.False(visible.Contains(new Point(40, 40)));
    }

    [Fact]
    public void TwoClipsWithOverlappingBoundsAndNoSharedAreaAreOffscreen()
    {
        var child = new Button { Clip = Geometry.Parse("M0,0 L80,0 L0,80 Z") };
        var parent = Arrange(new Border { Child = child, Clip = Geometry.Parse("M80,0 L80,80 L0,80 Z") });
        var visibility = new AutomationVisibility(child);
        Assert.False(visibility.TryClip(new Rect(0, 0, 80, 80), out _));
        Assert.True(child.GetAutomationPeer()!.IsOffscreen());
        parent.Clip = null;
        Assert.False(child.GetAutomationPeer()!.IsOffscreen());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CurvedClipsExcludeEmptyCorners(bool ellipse)
    {
        Geometry clip = ellipse ? new EllipseGeometry(new Point(40, 40), 40, 40) : new RectangleGeometry(new Rect(0, 0, 80, 80), 35, 35);
        var owner = Arrange(new Button { Clip = clip });
        var visibility = new AutomationVisibility(owner);
        Assert.False(visibility.TryClip(new Rect(0, 0, 2, 2), out _));
        Assert.True(visibility.TryClip(new Rect(35, 35, 10, 10), out Rect bounds));
        Assert.Equal(new Rect(35, 35, 10, 10), bounds);
    }

    [Fact]
    public void ThinPolygonSliversRemainVisibleWithoutRasterSampling()
    {
        var owner = Arrange(new Button { Clip = Geometry.Parse("M20,20 L20.000001,20 L20.000001,70 L20,70 Z") });
        Assert.True(new AutomationVisibility(owner).TryClip(new Rect(0, 0, 80, 80), out Rect bounds));
        Assert.Equal(.000001, bounds.Width, 8); Assert.Equal(50, bounds.Height);
        Assert.False(owner.GetAutomationPeer()!.IsOffscreen());
    }

    [Fact]
    public void ScreenProjectionUsesClippedPolygonsInsteadOfRotatingTheirBoundingBoxes()
    {
        var owner = Arrange(new Button { Clip = Geometry.Parse("M0,0 L80,0 L0,80 Z") });
        Matrix rotation = Matrix.Identity; rotation.Rotate(45);
        Assert.True(new AutomationVisibility(owner).TryClip(new Rect(0, 0, 80, 80), rotation, out Rect bounds));
        Assert.Equal(-80 / Math.Sqrt(2), bounds.Left, 6); Assert.Equal(80 / Math.Sqrt(2), bounds.Right, 6);
        Assert.Equal(0, bounds.Top, 6); Assert.Equal(80 / Math.Sqrt(2), bounds.Bottom, 6);
    }

    [Fact]
    public async Task EmptyGeometryIsImmutableAndCanBeUsedByAnotherDispatcher()
    {
        Assert.True(Geometry.Empty.IsFrozen);
        Assert.True(await Task.Run(() => new AutomationVisibility(Arrange(new Button { Clip = Geometry.Empty }))
            .TryClip(new Rect(0, 0, 80, 80), out _) == false));
    }

    [Fact]
    public void LegacySourceBoundsAreFilteredWithoutInventingZeroRectangles()
    {
        var owner = Arrange(new Button { Clip = new RectangleGeometry(new Rect(0, 0, 20, 20)) });
        var provider = new AutomationTextProvider(owner.GetAutomationPeer()!, new LegacyTextSource());
        Assert.Equal(new double[] { 5, 5, 10, 10 }, provider.DocumentRange.GetBoundingRectangles());
        owner.Clip = Geometry.Empty;
        Assert.Empty(provider.DocumentRange.GetBoundingRectangles());
        Assert.Equal("first\nsecond", provider.DocumentRange.GetText(-1));
    }

    [Fact]
    public void GeometryGroupKeepsChildTransformsAndBothWindingRules()
    {
        var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
        mask.Children.Add(new RectangleGeometry(new Rect(0, 0, 80, 80)));
        var hole = new RectangleGeometry(new Rect(0, 0, 20, 20)) { Transform = new TranslateTransform(30, 30) };
        mask.Children.Add(hole);
        var owner = Arrange(new Button { Clip = mask });
        Assert.False(new AutomationVisibility(owner).TryClip(new Rect(35, 35, 10, 10), out _));
        mask.FillRule = FillRule.Nonzero;
        Assert.True(new AutomationVisibility(owner).TryClip(new Rect(35, 35, 10, 10), out _));
        mask.FillRule = FillRule.EvenOdd; hole.Transform = new TranslateTransform(60, 60);
        Assert.True(new AutomationVisibility(owner).TryClip(new Rect(35, 35, 10, 10), out _));
        Assert.False(new AutomationVisibility(owner).TryClip(new Rect(65, 65, 10, 10), out _));
    }

    [Fact]
    public void ChildOnlyAndPerChildClipsDoNotClipTheParentsOwnPresentation()
    {
        var first = new Button(); var second = new Button();
        var panel = new ChildClipPanel(first); panel.Children.Add(first); panel.Children.Add(second); Arrange(panel);
        Assert.True(new AutomationVisibility(panel).TryClip(new Rect(50, 50, 10, 10), out _));
        Assert.False(new AutomationVisibility(first).TryClip(new Rect(50, 50, 10, 10), out _));
        Assert.True(new AutomationVisibility(second).TryClip(new Rect(50, 50, 10, 10), out _));
        Assert.False(new AutomationVisibility(second).TryClip(new Rect(75, 75, 5, 5), out _));
    }

    [Theory]
    [InlineData(IsOffscreenBehavior.Onscreen, false)]
    [InlineData(IsOffscreenBehavior.Offscreen, true)]
    [InlineData(IsOffscreenBehavior.FromClip, true)]
    [InlineData(IsOffscreenBehavior.Default, true)]
    public void ExplicitOffscreenBehaviorControlsThePropertyButCannotInventClickPoints(IsOffscreenBehavior behavior, bool expected)
    {
        var owner = Arrange(new Button { Clip = Geometry.Empty });
        AutomationProperties.SetIsOffscreenBehavior(owner, behavior);
        Assert.Equal(expected, owner.GetAutomationPeer()!.IsOffscreen());
        Assert.True(double.IsNaN(owner.GetAutomationPeer()!.GetClickablePoint().X));
    }

    private static T Arrange<T>(T owner) where T : FrameworkElement
    { owner.Measure(new Size(80, 80)); owner.Arrange(new Rect(0, 0, 80, 80)); return owner; }

    private sealed class ChildClipPanel(UIElement first) : Panel
    {
        internal override Geometry? GetChildLayoutClip() => new RectangleGeometry(new Rect(0, 0, 70, 70));
        internal override Geometry? GetAdditionalChildLayoutClip(Visual child) => ReferenceEquals(child, first)
            ? new RectangleGeometry(new Rect(0, 0, 30, 30)) : null;
        protected override Size MeasureOverride(Size availableSize)
        { foreach (UIElement child in Children) child.Measure(availableSize); return availableSize; }
        protected override Size ArrangeOverride(Size finalSize)
        { foreach (UIElement child in Children) child.Arrange(new Rect(finalSize)); return finalSize; }
    }

    private sealed class LegacyTextSource : IAutomationTextProviderSource
    {
        public string Text => "first\nsecond";
        public int SelectionStart => 0;
        public int SelectionLength => 0;
        public bool IsReadOnly => true;
        public SupportedTextSelection SupportedTextSelection => SupportedTextSelection.Single;
        public void Select(int start, int length) { }
        public IReadOnlyList<Rect> GetBoundingRectangles(int start, int length) => [new(5, 5, 10, 10), new(30, 30, 10, 10)];
        public void ScrollIntoView(int start, int length) { }
    }
}
