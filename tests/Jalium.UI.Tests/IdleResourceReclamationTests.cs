using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Hosting;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Direct unit tests for the idle-resource reclamation pipeline. They drive
/// the reclaimer's <c>Visual.VisualRenderedObserver</c> hook and
/// <c>ScanAndReclaim()</c> entry point directly, without spinning up a full
/// <see cref="Application"/> or the <c>CompositionTarget.Rendering</c> timer,
/// so the suite stays fast and side-effect free.
/// </summary>
public class IdleResourceReclamationTests : IDisposable
{
    public IdleResourceReclamationTests()
    {
        // Defensive: clear any prior test's stale observer subscription so each
        // test starts from a clean global hook state.
        Visual.VisualRenderedObserver = null;
    }

    public void Dispose()
    {
        Visual.VisualRenderedObserver = null;
    }

    private sealed class TestVisual : UIElement
    {
    }

    private sealed class ReclaimableTestVisual : UIElement, IReclaimableResource
    {
        public int ReclaimCallCount;
        public void ReclaimIdleResources() => ReclaimCallCount++;
    }

    [Fact]
    public void RenderDirect_StampsLastRenderedTickMs()
    {
        var visual = new TestVisual();
        Assert.Equal(0, visual.LastRenderedTickMs);

        var before = Environment.TickCount64;
        visual.Render(drawingContext: new object());
        var after = Environment.TickCount64;

        Assert.InRange(visual.LastRenderedTickMs, before, after);
    }

    [Fact]
    public void RenderDirect_DoesNotStamp_WhenCollapsed()
    {
        var visual = new TestVisual { Visibility = Visibility.Collapsed };

        visual.Render(drawingContext: new object());

        Assert.Equal(0, visual.LastRenderedTickMs);
    }

    [Fact]
    public void RenderDirect_DoesNotStamp_WhenHidden()
    {
        var visual = new TestVisual { Visibility = Visibility.Hidden };

        visual.Render(drawingContext: new object());

        Assert.Equal(0, visual.LastRenderedTickMs);
    }

    [Fact]
    public void VisualRenderedObserver_FiresOnceVisualRenders()
    {
        var visual = new TestVisual();
        Visual seen = null!;
        Visual.VisualRenderedObserver = v => seen = v;

        visual.Render(drawingContext: new object());

        Assert.Same(visual, seen);
    }

    [Fact]
    public void Reclaimer_Start_TracksRenderedVisualsAndSetsFlag()
    {
        var options = new ResourceReclamationOptions { IdleTimeoutMs = 250 };
        using var reclaimer = new ResourceReclaimer(options);
        reclaimer.Start();

        var visual = new TestVisual();
        Assert.False(visual.IsTrackedByIdleReclaimer);

        visual.Render(drawingContext: new object());

        Assert.True(visual.IsTrackedByIdleReclaimer);
    }

    [Fact]
    public void Reclaimer_Stop_DetachesHookAndClearsTrackingFlag()
    {
        var options = new ResourceReclamationOptions { IdleTimeoutMs = 250 };
        var reclaimer = new ResourceReclaimer(options);
        reclaimer.Start();

        var visual = new TestVisual();
        visual.Render(drawingContext: new object());
        Assert.True(visual.IsTrackedByIdleReclaimer);

        reclaimer.Stop();

        Assert.False(visual.IsTrackedByIdleReclaimer);
        Assert.False(options.Enabled);

        // Subsequent renders must not re-track while the reclaimer is stopped.
        visual.Render(drawingContext: new object());
        Assert.False(visual.IsTrackedByIdleReclaimer);
    }

    [Fact]
    public void ScanAndReclaim_InvokesReclaimableResources_AfterIdleTimeout()
    {
        var options = new ResourceReclamationOptions
        {
            IdleTimeoutMs = 250,
            EvictDrawingCache = false,
        };
        using var reclaimer = new ResourceReclaimer(options);
        reclaimer.Start();

        var idle = new ReclaimableTestVisual();
        var live = new ReclaimableTestVisual();

        idle.Render(drawingContext: new object());
        live.Render(drawingContext: new object());

        // Force idle's last-rendered tick into the past beyond the timeout.
        // Sleeping is reliable but slow; we'd rather adjust state directly,
        // but _lastRenderedTickMs is private. Sleep just past the threshold.
        Thread.Sleep(options.IdleTimeoutMs + 100);

        // Re-render the "live" visual so its tick is fresh again.
        live.Render(drawingContext: new object());

        reclaimer.ScanAndReclaim();

        Assert.Equal(1, idle.ReclaimCallCount);
        Assert.Equal(0, live.ReclaimCallCount);
    }

    [Fact]
    public void ScanAndReclaim_RespectsInvokeReclaimableResourcesFlag()
    {
        var options = new ResourceReclamationOptions
        {
            IdleTimeoutMs = 250,
            EvictDrawingCache = false,
            InvokeReclaimableResources = false,
        };
        using var reclaimer = new ResourceReclaimer(options);
        reclaimer.Start();

        var visual = new ReclaimableTestVisual();
        visual.Render(drawingContext: new object());

        Thread.Sleep(options.IdleTimeoutMs + 100);

        reclaimer.ScanAndReclaim();

        Assert.Equal(0, visual.ReclaimCallCount);
    }

    [Fact]
    public void ScanAndReclaim_SwallowsExceptionsFromReclaimers()
    {
        var options = new ResourceReclamationOptions
        {
            IdleTimeoutMs = 250,
            EvictDrawingCache = false,
        };
        using var reclaimer = new ResourceReclaimer(options);
        reclaimer.Start();

        var throwing = new ThrowingReclaimable();
        var follower = new ReclaimableTestVisual();

        throwing.Render(drawingContext: new object());
        follower.Render(drawingContext: new object());

        Thread.Sleep(options.IdleTimeoutMs + 100);

        // Must not propagate; follower must still be reclaimed.
        reclaimer.ScanAndReclaim();

        Assert.Equal(1, throwing.Calls);
        Assert.Equal(1, follower.ReclaimCallCount);
    }

    private sealed class ThrowingReclaimable : UIElement, IReclaimableResource
    {
        public int Calls;
        public void ReclaimIdleResources()
        {
            Calls++;
            throw new InvalidOperationException("boom");
        }
    }

    [Fact]
    public void ScanAndReclaim_DropsDeadWeakReferences()
    {
        var options = new ResourceReclamationOptions { IdleTimeoutMs = 250 };
        using var reclaimer = new ResourceReclaimer(options);
        reclaimer.Start();

        // Render then drop the strong reference so the weak ref can collect.
        var weak = RenderThenDropStrongReference();

        // Force the GC to reclaim it.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weak.TryGetTarget(out _));

        // Scan must complete without throwing despite a dead entry in the list.
        reclaimer.ScanAndReclaim();
    }

    private static WeakReference<TestVisual> RenderThenDropStrongReference()
    {
        var v = new TestVisual();
        v.Render(drawingContext: new object());
        return new WeakReference<TestVisual>(v);
    }

    // ── GPU / BitmapImage reclamation ───────────────────────────────────────

    private static void SetBitmapImagePrivate(BitmapImage image, byte[]? raw, byte[]? encoded)
    {
        var t = typeof(BitmapImage);
        t.GetField("_rawPixelData", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(image, raw);
        t.GetField("_imageData", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(image, encoded);
    }

    // The GpuCacheEvictionRequested event is `internal static` on ImageSource.
    // The test assembly is in Jalium.UI.Media's InternalsVisibleTo list, but
    // SDK-generated reference assemblies strip internal members from the
    // refint dll the C# compiler reads — so direct `+=` against the event from
    // the test assembly fails CS0117. Reach the runtime add/remove methods via
    // reflection on the implementation assembly instead; that always sees the
    // full type metadata.
    private static MethodInfo s_evictionAdd =
        typeof(BitmapImage).Assembly
            .GetType("Jalium.UI.Media.ImageSource", throwOnError: true)!
            .GetMethod("add_GpuCacheEvictionRequested", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static MethodInfo s_evictionRemove =
        typeof(BitmapImage).Assembly
            .GetType("Jalium.UI.Media.ImageSource", throwOnError: true)!
            .GetMethod("remove_GpuCacheEvictionRequested", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static void SubscribeEviction(Action<ImageSource> h)
        => s_evictionAdd.Invoke(null, new object[] { h });
    private static void UnsubscribeEviction(Action<ImageSource> h)
        => s_evictionRemove.Invoke(null, new object[] { h });

    [Fact]
    public void BitmapImage_ReclaimIdleResources_DropsRawPixels_WhenEncodedAvailable()
    {
        var image = new BitmapImage();
        SetBitmapImagePrivate(image, raw: new byte[64], encoded: new byte[8]);
        Assert.NotNull(image.RawPixelData);

        image.ReclaimIdleResources();

        Assert.Null(image.RawPixelData);
        Assert.NotNull(image.ImageData);
    }

    [Fact]
    public void BitmapImage_ReclaimIdleResources_KeepsRawPixels_WhenNoEncodedSource()
    {
        var image = new BitmapImage();
        SetBitmapImagePrivate(image, raw: new byte[64], encoded: null);

        image.ReclaimIdleResources();

        // No encoded fallback — pixels must stay so the image is not lost.
        Assert.NotNull(image.RawPixelData);
    }

    [Fact]
    public void BitmapImage_ReclaimIdleResources_RaisesGpuCacheEvictionEvent()
    {
        var image = new BitmapImage();
        SetBitmapImagePrivate(image, raw: new byte[64], encoded: new byte[8]);

        ImageSource? evicted = null;
        Action<ImageSource> handler = s => evicted = s;
        SubscribeEviction(handler);
        try
        {
            image.ReclaimIdleResources();
        }
        finally
        {
            UnsubscribeEviction(handler);
        }

        Assert.Same(image, evicted);
    }

    [Fact]
    public void Image_Control_ReclaimIdleResources_ForwardsToSource()
    {
        var bitmap = new BitmapImage();
        SetBitmapImagePrivate(bitmap, raw: new byte[64], encoded: new byte[8]);
        var image = new Image { Source = bitmap };

        Assert.IsAssignableFrom<IReclaimableResource>(image);

        ((IReclaimableResource)image).ReclaimIdleResources();

        Assert.Null(bitmap.RawPixelData);
    }

    // ── Backend cache reclamation throttle ──────────────────────────────────

    private static long ReadLastBackendReclaimTick(ResourceReclaimer reclaimer)
    {
        var f = typeof(ResourceReclaimer)
            .GetField("_lastBackendReclaimTickMs",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (long)f.GetValue(reclaimer)!;
    }

    [Fact]
    public void ScanAndReclaim_BackendReclaim_DoesNotFire_WhenDisabled()
    {
        var options = new ResourceReclamationOptions
        {
            IdleTimeoutMs = 250,
            ReclaimBackendCaches = false,
        };
        using var reclaimer = new ResourceReclaimer(options);
        reclaimer.Start();

        reclaimer.ScanAndReclaim();

        Assert.Equal(0L, ReadLastBackendReclaimTick(reclaimer));
    }

    [Fact]
    public void ScanAndReclaim_BackendReclaim_FiresOnFirstScan_WhenEnabled()
    {
        var options = new ResourceReclamationOptions
        {
            IdleTimeoutMs = 250,
            ReclaimBackendCaches = true,
        };
        using var reclaimer = new ResourceReclaimer(options);
        reclaimer.Start();

        reclaimer.ScanAndReclaim();

        Assert.NotEqual(0L, ReadLastBackendReclaimTick(reclaimer));
    }

    [Fact]
    public void ScanAndReclaim_BackendReclaim_IsThrottledByIdleTimeout()
    {
        var options = new ResourceReclamationOptions
        {
            IdleTimeoutMs = 10_000,    // long enough that the second scan is inside the window
            ReclaimBackendCaches = true,
        };
        using var reclaimer = new ResourceReclaimer(options);
        reclaimer.Start();

        reclaimer.ScanAndReclaim();
        var firstTick = ReadLastBackendReclaimTick(reclaimer);
        Assert.NotEqual(0L, firstTick);

        // Second scan inside the throttle window — timestamp must not advance.
        reclaimer.ScanAndReclaim();
        var secondTick = ReadLastBackendReclaimTick(reclaimer);

        Assert.Equal(firstTick, secondTick);
    }

    [Fact]
    public void EndToEnd_ReclaimerEvictsImageGpuCache_AfterIdleTimeout()
    {
        var bitmap = new BitmapImage();
        SetBitmapImagePrivate(bitmap, raw: new byte[64], encoded: new byte[8]);
        var image = new Image { Source = bitmap };

        ImageSource? evicted = null;
        Action<ImageSource> handler = s => evicted = s;
        SubscribeEviction(handler);

        var options = new ResourceReclamationOptions
        {
            IdleTimeoutMs = 250,
            EvictDrawingCache = false,
        };
        using var reclaimer = new ResourceReclaimer(options);
        reclaimer.Start();

        try
        {
            image.Render(drawingContext: new object());
            Thread.Sleep(options.IdleTimeoutMs + 100);

            reclaimer.ScanAndReclaim();
        }
        finally
        {
            UnsubscribeEviction(handler);
        }

        Assert.Same(bitmap, evicted);
        Assert.Null(bitmap.RawPixelData);
    }
}
