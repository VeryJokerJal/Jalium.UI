using System.Reflection;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Verifies the composition-only invalidation pathway for Opacity, RenderTransform,
/// and RenderTransformOrigin. Before the fix, animations on these properties
/// (typical hover transitions on every card in a 30-card list) drove every
/// animated visual to flip _isRenderDirty each frame, evicting its retained-mode
/// drawing cache and re-recording the OnRender command list. The parent's
/// child-render loop already reads these values live each frame via PushOpacity /
/// PushTransform, so the cached drawing is unaffected by the change. The new
/// path schedules a present without flipping the render-dirty flag.
/// </summary>
public class CompositionInvalidationTests
{
    private static FieldInfo? s_isRenderDirtyField;
    private static FieldInfo IsRenderDirtyField =>
        s_isRenderDirtyField ??= typeof(Visual).GetField("_isRenderDirty",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Visual._isRenderDirty field not found");

    private static bool GetIsRenderDirty(Visual v) =>
        (bool)IsRenderDirtyField.GetValue(v)!;

    private static void ClearIsRenderDirty(Visual v) =>
        IsRenderDirtyField.SetValue(v, false);

    [Fact]
    public void InvalidateComposition_DoesNotFlipRenderDirty()
    {
        var element = new TestVisualElement();
        // Establish a clean baseline: pretend we just rendered.
        ClearIsRenderDirty(element);

        element.InvalidateComposition();

        Assert.False(GetIsRenderDirty(element),
            "InvalidateComposition must not flip _isRenderDirty — the parent re-traverses " +
            "the child loop each frame and reads PushOpacity/PushTransform live.");
    }

    [Fact]
    public void InvalidateComposition_NotifiesWindowHost()
    {
        var host = new RecordingWindowHost();
        var child = new TestVisualElement();
        host.AddChild(child);
        // Adding the child fires OnVisualParentChanged which can invalidate; reset so
        // we measure only the InvalidateComposition call below.
        host.Reset();

        child.InvalidateComposition();

        Assert.True(host.InvalidateWindowCalled, "InvalidateComposition must request a present.");
        Assert.Equal(1, host.AddDirtyElementCount);
        Assert.Same(child, host.LastDirtyElement);
    }

    [Fact]
    public void OpacityChange_DoesNotFlipRenderDirty()
    {
        var element = new TestVisualElement();
        ClearIsRenderDirty(element);

        element.Opacity = 0.5;

        Assert.False(GetIsRenderDirty(element),
            "Opacity is a composition-only property — changing it must not invalidate the cache.");
    }

    [Fact]
    public void RenderTransformChange_DoesNotFlipRenderDirty()
    {
        var element = new TestVisualElement();
        ClearIsRenderDirty(element);

        element.RenderTransform = new ScaleTransform(2.0, 2.0);

        Assert.False(GetIsRenderDirty(element),
            "RenderTransform is a composition-only property — changing it must not invalidate the cache.");
    }

    [Theory]
    [InlineData(1.0, 1.0, false)]
    [InlineData(0.5, 0.75, false)]
    [InlineData(-1.0, 1.0, false)]
    [InlineData(2.1666666667, 2.1666666667, true)]
    [InlineData(1.0, 1.01, true)]
    [InlineData(-2.0, 1.0, true)]
    public void RetainedLayerEligibility_RejectsTextureUpscaling(
        double scaleX, double scaleY, bool expected)
    {
        var transform = new ScaleTransform(scaleX, scaleY);

        Assert.Equal(expected, Visual.TransformWouldUpscaleRetainedLayer(transform));
    }

    [Fact]
    public void RenderTransformOriginChange_DoesNotFlipRenderDirty()
    {
        var element = new TestVisualElement();
        ClearIsRenderDirty(element);

        element.RenderTransformOrigin = new Point(0.5, 0.5);

        Assert.False(GetIsRenderDirty(element),
            "RenderTransformOrigin is a composition-only property — changing it must not invalidate the cache.");
    }

    [Fact]
    public void OpacityChange_StillSchedulesPresentViaCompositionPath()
    {
        var host = new RecordingWindowHost();
        var element = new TestVisualElement();
        host.AddChild(element);

        host.Reset();
        element.Opacity = 0.7;

        Assert.True(host.InvalidateWindowCalled, "Opacity change must still request a present (via InvalidateComposition).");
        Assert.True(host.AddDirtyElementCount >= 1);
    }

    [Fact]
    public void NormalRenderProperty_StillFlipsRenderDirty()
    {
        // Sanity check: properties without AffectsCompositionOnly still flip the
        // render-dirty flag (the historical behaviour). This protects against
        // accidentally mis-categorising regular DPs as composition-only.
        var element = new TestVisualElement();
        ClearIsRenderDirty(element);

        element.InvalidateVisual();

        Assert.True(GetIsRenderDirty(element));
    }

    [Fact]
    public void OpacityProperty_IsCompositionOnly()
    {
        var meta = UIElement.OpacityProperty.DefaultMetadata as FrameworkPropertyMetadata;
        Assert.NotNull(meta);
        Assert.True(meta!.AffectsCompositionOnly,
            "OpacityProperty must be flagged AffectsCompositionOnly so animation ticks " +
            "use the composition path.");
        Assert.True(meta.AffectsRender);
    }

    [Fact]
    public void RenderTransformProperty_IsCompositionOnly()
    {
        var meta = UIElement.RenderTransformProperty.DefaultMetadata as FrameworkPropertyMetadata;
        Assert.NotNull(meta);
        Assert.True(meta!.AffectsCompositionOnly);
        Assert.True(meta.AffectsRender);
    }

    [Fact]
    public void RenderTransformOriginProperty_IsCompositionOnly()
    {
        var meta = UIElement.RenderTransformOriginProperty.DefaultMetadata as FrameworkPropertyMetadata;
        Assert.NotNull(meta);
        Assert.True(meta!.AffectsCompositionOnly);
        Assert.True(meta.AffectsRender);
    }

    private sealed class TestVisualElement : FrameworkElement
    {
    }

    /// <summary>
    /// Test fake that satisfies <see cref="IWindowHost"/> so children can resolve
    /// a host via the visual-tree walk in <c>UIElement.GetWindowHost</c>.
    /// </summary>
    private sealed class RecordingWindowHost : FrameworkElement, IWindowHost
    {
        public bool InvalidateWindowCalled { get; private set; }
        public int AddDirtyElementCount { get; private set; }
        public UIElement? LastDirtyElement { get; private set; }

        public void AddChild(UIElement child) => AddVisualChild(child);

        public void Reset()
        {
            InvalidateWindowCalled = false;
            AddDirtyElementCount = 0;
            LastDirtyElement = null;
        }

        public void InvalidateWindow() => InvalidateWindowCalled = true;

        public void AddDirtyElement(UIElement element)
        {
            AddDirtyElementCount++;
            LastDirtyElement = element;
        }

        public void RequestFullInvalidation() { }
        public void SetNativeCapture() { }
        public void ReleaseNativeCapture() { }
    }
}
