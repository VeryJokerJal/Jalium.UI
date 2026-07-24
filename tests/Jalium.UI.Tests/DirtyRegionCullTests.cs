using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Rendering;

namespace Jalium.UI.Tests;

/// <summary>
/// Locks the ancestor-clip culling added to the RC4-b displacement hook
/// (<c>UIElement.Arrange</c> → <c>TryGetAncestorClipScreenBounds</c>). A father-driven
/// displacement (virtualized scroll moving a row) whose old AND new AABB both fall entirely
/// outside the nearest clipping ancestor (a ScrollViewer viewport, or any ClipToBounds element)
/// must register NOTHING — that row is rejected by <c>Visual.ShouldRenderChild</c> this frame and
/// its old pixels were already outside the clip. Rows that touch the clip (viewport-interior, or
/// scrolling across the edge) must still register in full.
///
/// These tests run fully headless: a fake <see cref="IWindowHost"/> is the visual ancestor so
/// <c>GetWindowHostOrNull()</c> resolves, and a plain <c>ClipToBounds</c> container supplies the
/// clip via the base <c>GetLayoutClip</c> — no real Window, template, or present is needed.
///
/// TRAP: the existing <c>RecordingWindowHost</c> in CompositionInvalidationTests does NOT override
/// <c>AddDirtyRect</c> (it inherits the no-op default at <c>IWindowHost.AddDirtyRect</c>), so it
/// would silently swallow the old-AABB free rect and make "band row not registered" assertions
/// pass vacuously. This file uses a dedicated double-recording host and guards against that
/// false-green in <see cref="RegisteredRow_OldRectReachesAddDirtyRect_FalseGreenGuard"/>.
/// </summary>
public class DirtyRegionCullTests
{
    private const double Vp = 200; // viewport (clip) width/height

    // ── Fakes ────────────────────────────────────────────────────────────────

    /// <summary>Double-recording host: records BOTH channels so a missing AddDirtyRect can't hide.</summary>
    private sealed class RecordingCullHost : FrameworkElement, IWindowHost
    {
        public readonly List<UIElement> DirtyElements = new();
        public readonly List<Rect> DirtyRects = new();

        public void AddChild(UIElement child) => AddVisualChild(child);
        public void Reset() { DirtyElements.Clear(); DirtyRects.Clear(); }

        public void InvalidateWindow() { }
        public void AddDirtyElement(UIElement element) => DirtyElements.Add(element);
        public void AddDirtyElement(UIElement element, Rect localDirtyRect) => DirtyElements.Add(element);
        public void AddDirtyRect(Rect screenRect) => DirtyRects.Add(screenRect);
        public void RequestFullInvalidation() { }
        public void SetNativeCapture() { }
        public void ReleaseNativeCapture() { }
    }

    /// <summary>
    /// Clip container that never arranges its children — the test positions rows manually via
    /// direct <c>row.Arrange(rect)</c> so displacement is fully deterministic.
    /// </summary>
    private sealed class TestContainer : FrameworkElement
    {
        private readonly List<UIElement> _kids = new();
        public void AddChild(UIElement child) { _kids.Add(child); AddVisualChild(child); }
        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var k in _kids) k.Measure(availableSize);
            return new Size(0, 0);
        }
        protected override Size ArrangeOverride(Size finalSize) => finalSize; // do NOT arrange kids
    }

    private sealed class TestLeaf : FrameworkElement { }

    // ── Harness helpers ──────────────────────────────────────────────────────

    private static (RecordingCullHost host, TestContainer clip) BuildClipTree(bool clipToBounds = true)
    {
        var host = new RecordingCullHost();
        var clip = new TestContainer { ClipToBounds = clipToBounds };
        host.AddChild(clip);
        clip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        clip.Arrange(new Rect(0, 0, Vp, Vp)); // clip screen rect = (0,0,Vp,Vp) = the "viewport"
        return (host, clip);
    }

    private static TestLeaf AddRow(TestContainer parent, Rect initial)
    {
        var row = new TestLeaf();
        parent.AddChild(row);
        row.Measure(new Size(initial.Width, initial.Height));
        row.Arrange(initial); // first arrange establishes _hasArrangedOnce and the "old" position
        return row;
    }

    // Fully outside the viewport (well below the clip's bottom edge).
    private static Rect Band(double y) => new Rect(0, Vp + y, 100, 40);
    // Inside the viewport.
    private static Rect Inside(double y) => new Rect(0, y, 100, 40);

    // ── Tri-state ────────────────────────────────────────────────────────────

    [Fact]
    public void BandRow_OldAndNewOutsideClip_NotRegistered()
    {
        var (host, clip) = BuildClipTree();
        var row = AddRow(clip, Band(100));   // old: below viewport
        host.Reset();

        row.Arrange(Band(150));              // new: still below viewport

        Assert.DoesNotContain(row, host.DirtyElements);
        Assert.Empty(host.DirtyRects);       // old AABB must NOT be submitted either
    }

    [Fact]
    public void ViewportRow_Displaced_Registered()
    {
        var (host, clip) = BuildClipTree();
        var row = AddRow(clip, Inside(10));
        host.Reset();

        row.Arrange(Inside(20));

        Assert.Contains(row, host.DirtyElements);
    }

    [Fact]
    public void EdgeRow_OldInsideNewOutside_Registered()
    {
        var (host, clip) = BuildClipTree();
        var row = AddRow(clip, Inside(Vp - 20)); // old spans (Vp-20)..(Vp+20): intersects viewport
        host.Reset();

        row.Arrange(Band(100));                  // new: fully below viewport

        Assert.Contains(row, host.DirtyElements);
        // Old (intersecting) position must be submitted for erase.
        Assert.Contains(new Rect(0, Vp - 20, 100, 40), host.DirtyRects);
    }

    [Fact]
    public void EdgeRow_OldOutsideNewInside_Registered()
    {
        var (host, clip) = BuildClipTree();
        var row = AddRow(clip, Band(100));  // old: below viewport
        host.Reset();

        row.Arrange(Inside(Vp - 20));       // new: intersects viewport (scrolling in)

        Assert.Contains(row, host.DirtyElements);
    }

    // ── Conservative fallback ────────────────────────────────────────────────

    [Fact]
    public void RenderOffsetAncestor_FallsBackToFullRegistration()
    {
        var (host, clip) = BuildClipTree();
        var row = AddRow(clip, Band(100));
        // A non-zero RenderOffset on the clip owner makes its clip screen rect unlocatable via the
        // transform-unaware GetScreenBounds origin → TryGetAncestorClipScreenBounds bails the whole
        // chain → no cull → the band row registers in full (never under-erase).
        clip.RenderOffset = new Point(0, 3);
        host.Reset();

        row.Arrange(Band(150));

        Assert.Contains(row, host.DirtyElements);
    }

    [Fact]
    public void NoClipAncestor_NeverCulls()
    {
        var (host, clip) = BuildClipTree(clipToBounds: false); // clip=null everywhere
        var row = AddRow(clip, Band(100));
        host.Reset();

        row.Arrange(Band(150));

        // No clipping ancestor → TryGetAncestorClipScreenBounds returns false → zero behavior change.
        Assert.Contains(row, host.DirtyElements);
    }

    [Fact]
    public void EmptySuperEllipseAncestorClip_FallsBackWithoutConstructingEmptyRect()
    {
        var host = new RecordingCullHost();
        var leaf = new TestLeaf();
        var clip = new Border
        {
            ClipToBounds = true,
            Shape = BorderShape.SuperEllipse,
            BorderThickness = new Thickness(10),
            Child = leaf,
        };
        host.AddChild(clip);

        clip.Measure(new Size(10, 10));

        // Border's inner superellipse collapses to an empty StreamGeometry. Arranging its
        // child still walks ancestor clips for dirty-region culling and must conservatively
        // fall back instead of reconstructing Rect.Empty through Rect's public constructor.
        var exception = Record.Exception(() => clip.Arrange(new Rect(0, 0, 10, 10)));

        Assert.Null(exception);
    }

    // ── Nested clip ──────────────────────────────────────────────────────────

    [Fact]
    public void NestedClip_UsesInnermostIntersection()
    {
        var host = new RecordingCullHost();
        var outer = new TestContainer { ClipToBounds = true };
        var inner = new TestContainer { ClipToBounds = true };
        host.AddChild(outer);
        outer.AddChild(inner);
        outer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        outer.Arrange(new Rect(0, 0, 400, 400));    // outer clip = (0,0,400,400)
        inner.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        inner.Arrange(new Rect(0, 0, 200, 100));     // inner clip = (0,0,200,100)

        // Row sits inside OUTER but below INNER's bottom (y=150 > 100).
        var row = AddRow(inner, new Rect(0, 150, 100, 30));
        host.Reset();

        row.Arrange(new Rect(0, 160, 100, 30));      // still below inner clip

        // Intersection = inner ∩ outer = (0,0,200,100); row outside → culled.
        Assert.DoesNotContain(row, host.DirtyElements);
        Assert.Empty(host.DirtyRects);
    }

    // ── False-green guard ────────────────────────────────────────────────────

    [Fact]
    public void RegisteredRow_OldRectReachesAddDirtyRect_FalseGreenGuard()
    {
        // Proves the host actually records AddDirtyRect. If it silently swallowed the old rect
        // (the RecordingWindowHost trap), the "band row" tests above would be vacuously green.
        var (host, clip) = BuildClipTree();
        var row = AddRow(clip, Inside(10));
        host.Reset();

        row.Arrange(Inside(30)); // viewport-interior displacement → old AABB must be submitted

        Assert.Contains(new Rect(0, 10, 100, 40), host.DirtyRects);
    }

    // ── Implicit-coupling invariant (attack surface 5) ───────────────────────

    [Fact]
    public void ClipAncestorDisplaced_SelfRegistersOldRegion_CoversCulledDescendants()
    {
        // Invariant B: when a clipping ancestor itself moves, it hits its OWN RC4-b path and submits
        // its old AABB via AddDirtyRect — repainting the old clip region, which covers the old pixels
        // of any descendant this frame's cull skipped. Locks the one non-local correctness dependency.
        var (host, clip) = BuildClipTree();
        var row = AddRow(clip, Band(100)); // a descendant that will be culled
        host.Reset();

        clip.Arrange(new Rect(0, 30, Vp, Vp)); // clip translates down by 30 → its own hook fires
        row.Arrange(Band(110));                // descendant displaces and is culled

        // The clip's OLD screen region (0,0,Vp,Vp) was submitted, covering any wrongly-culled pixels.
        Assert.Contains(clip, host.DirtyElements);
        Assert.Contains(new Rect(0, 0, Vp, Vp), host.DirtyRects);
        // The band descendant itself was culled (no new registration for it).
        Assert.DoesNotContain(row, host.DirtyElements);
    }

    // ── Benefit: dirty area converges to ~viewport, not ~3× viewport ─────────

    [Fact]
    public void ScrollFrame_DirtyAreaConvergesToViewport_NotCacheBand()
    {
        // Emulate a virtualized scroll frame: rows realized across the 3× viewport cache band
        // (one viewport above, the viewport, one below), every row displaced by the scroll delta.
        // With culling, only viewport-touching rows register → aggregate real area stays ~viewport,
        // safely under the promote threshold. Without culling it would span ~3× viewport.
        var (host, clip) = BuildClipTree();

        const int rowH = 20;
        var rows = new List<TestLeaf>();
        // y from -Vp (a viewport above) to 2*Vp (a viewport below) — the realized band.
        // Full-viewport-width rows so the aggregate area maps cleanly onto the viewport.
        for (double y = -Vp; y < 2 * Vp; y += rowH)
            rows.Add(AddRow(clip, new Rect(0, y, Vp, rowH)));
        host.Reset();

        // Scroll up by one row: every row moves -rowH.
        foreach (var row in rows)
        {
            var b = GetVisualBounds(row);
            row.Arrange(new Rect(b.X, b.Y - rowH, b.Width, b.Height));
        }

        var agg = new DirtyRegionAggregator();
        foreach (var r in host.DirtyRects) agg.Add(r);
        foreach (var e in host.DirtyElements) agg.Add(GetVisualBounds(e)); // approximate new-position channel

        double area = agg.ComputeRealArea();
        double viewportArea = Vp * Vp;
        // Must be within a small multiple of the viewport (edge rows overflow by <1 row height),
        // and nowhere near the ~3× viewport a non-culling registration would produce.
        Assert.True(area <= viewportArea * 1.5,
            $"dirty real area {area} should stay ~viewport ({viewportArea}), not ~3x ({3 * viewportArea})");
    }

    private static Rect GetVisualBounds(UIElement e) => e.VisualBounds;
}
