using Jalium.UI.Interop;
using Jalium.UI.Media.Imaging;

// Jalium.UI.Media.Imaging carries a same-named type; the GPU bitmap cache under test is the
// Interop one.
using RenderTargetDrawingContext = Jalium.UI.Interop.RenderTargetDrawingContext;

namespace Jalium.UI.Tests;

/// <summary>
/// Pins the thread ownership of <c>RenderTargetDrawingContext</c>'s bitmap cache against the two
/// static <see cref="ImageSource"/> eviction events.
/// </summary>
/// <remarks>
/// <para><c>ImageSource.RasterChanged</c> and <c>ImageSource.GpuCacheEvictionRequested</c> are bare
/// synchronous invokes on the RAISING thread, and every raiser — a decode publish, an animated
/// substitute becoming ready, any source assignment — is delivered on the main dispatcher. The
/// caches they reach are not the main dispatcher's: on the default Windows/D3D12 configuration a
/// dedicated render thread exclusively owns the drawing context and is inside
/// <c>GetNativeBitmap</c> (dictionary lookup, in-place entry mutation, indexer insert, a
/// non-atomic byte accumulation and an enumerating trim loop) at exactly the moment a publish
/// lands. Handling the event inline therefore put a second writer on a plain
/// <c>Dictionary</c> and disposed a native texture the render thread was about to sample — the
/// "native brush use-after-free + Dictionary corruption" pair the render-thread ownership rule in
/// <c>Window.RenderFrame</c> exists to prevent — and both failure legs are swallowed (the decode
/// notifier catches per item, the render loop discards the frame), so it surfaced as an image that
/// is wrong on some machines with nothing in the log.</para>
/// <para>Nothing in the suite could see that: <c>grep -rn RasterChanged tests/</c> returned
/// nothing, and a headless host never starts a render thread, so the whole green suite had zero
/// discriminating power here. These two tests supply it — the first deterministically (the
/// eviction must NOT have been applied when the raiser returns, and must still be applied by the
/// owning thread's next frame), the second by running the real interleaving.</para>
/// <para>Both need a real GPU texture to evict and are reported as SKIPPED rather than silently
/// passing where the D3D12 backend is absent.</para>
/// </remarks>
[Collection("Application")]
public sealed class ImageCacheEvictionThreadingTests
{
    private const int SurfaceWidth = 256;
    private const int SurfaceHeight = 128;

    /// <summary>Pixel size of every source here.</summary>
    /// <remarks>
    /// Deliberately far SMALLER than <see cref="PaintRect"/>: a source drawn larger than itself is
    /// refused by <c>BitmapDownscaleCache</c>, so the cache entry is keyed on the bitmap the test
    /// raises the event for rather than on a synthesized thumbnail, and the entry count below means
    /// what it says.
    /// </remarks>
    private const int SourcePixels = 32;

    /// <summary>Rect the tests paint into, well inside the surface.</summary>
    private static readonly Rect PaintRect = new(32, 16, 160, 80);

    /// <summary>Frames the race test draws, and images it draws per frame.</summary>
    private const int RaceFrames = 60;
    private const int RaceSources = 8;

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ConfiguredEntryBudgetEvictsOldImagesAndProtectsCurrentFrame()
    {
        using var first = CreateOpaqueWhiteBitmap();
        using var second = CreateOpaqueWhiteBitmap();
        using var third = CreateOpaqueWhiteBitmap();
        using var surface = new GpuSurface(bitmapCapacity: 2);
        var pixels = surface.DrawFrameAndReadBack(context =>
        {
            context.DrawImage(first, PaintRect);
            context.DrawImage(second, PaintRect);
            context.DrawImage(third, PaintRect);
        });
        surface.TrimCaches();
        Assert.Equal(3, surface.CachedBitmapCount);
        Assert.True(SampleChannelAverage(pixels, PaintRect) > 240);

        surface.DrawFrame(context => context.DrawImage(third, PaintRect));
        surface.TrimCaches();
        Assert.Equal(2, surface.CachedBitmapCount);

        pixels = surface.DrawFrameAndReadBack(context => context.DrawImage(first, PaintRect));
        surface.TrimCaches();
        Assert.Equal(2, surface.CachedBitmapCount);
        Assert.True(SampleChannelAverage(pixels, PaintRect) > 240);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ConfiguredByteBudgetEvictsOnlyImagesFromEarlierFrames()
    {
        using var first = CreateOpaqueWhiteBitmap();
        using var second = CreateOpaqueWhiteBitmap();
        using var surface = new GpuSurface(bitmapByteBudget: SourcePixels * SourcePixels * 4);
        surface.DrawFrame(context => context.DrawImage(first, PaintRect));
        surface.TrimCaches();
        surface.DrawFrame(context => context.DrawImage(second, PaintRect));
        surface.TrimCaches();
        Assert.Equal(1, surface.CachedBitmapCount);

        surface.DrawFrame(context =>
        {
            context.DrawImage(first, PaintRect);
            context.DrawImage(second, PaintRect);
        });
        surface.TrimCaches();
        Assert.Equal(2, surface.CachedBitmapCount);
    }

    /// <summary>
    /// An eviction raised off the owning thread must be RECORDED there and APPLIED here — not
    /// applied by the raiser.
    /// </summary>
    /// <remarks>
    /// The two assertions are one property seen from both sides. If the eviction were applied
    /// inline, the entry would already be gone (and its <c>NativeBitmap</c> disposed) the moment
    /// the raising thread returned, which is the unsynchronised behaviour and fails the first
    /// assertion. If the deferral simply dropped the request, the entry would still be there after
    /// the owning thread's next frame prologue, which fails the second — a texture that is never
    /// released is a leak, and for a source-assignment raise it is also a stale upload.
    /// </remarks>
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void RasterChangedRaisedOffTheOwningThreadIsAppliedOnlyByTheOwningThreadsNextFrame()
    {
        using var bitmap = CreateOpaqueWhiteBitmap();
        using var surface = new GpuSurface();

        surface.DrawFrame(context => context.DrawImage(bitmap, PaintRect));
        Assert.Equal(1, surface.CachedBitmapCount);

        RaiseRasterChangedOnAnotherThread(bitmap);

        Assert.Equal(1, surface.CachedBitmapCount);

        // The owning thread's frame prologue — the same call every window makes before it draws.
        surface.BeginFrame();

        Assert.Equal(0, surface.CachedBitmapCount);
    }

    /// <summary>
    /// The eviction events hammered from one thread while the thread that owns the context draws
    /// through the cache must leave both threads standing and the cache usable.
    /// </summary>
    /// <remarks>
    /// <para>This is the interleaving the deterministic test above models one step of. It is not a
    /// proof of absence — a data race can decline to manifest — but it is the shape that actually
    /// runs in production (one publish per bucket step, several per image, against a render thread
    /// inserting those same keys), and on the unsynchronised version it puts concurrent
    /// <c>Remove</c> + <c>Dispose</c> against concurrent <c>TryGetValue</c> + insert on eight keys
    /// of one plain dictionary for sixty frames.</para>
    /// <para>Measured against that version, it does not report a failed assertion: it takes the
    /// test host down with <c>0xC0000005</c> inside <c>NativeMethods.DrawBitmapEx</c>, the draw
    /// reaching a <c>NativeBitmap</c> whose <c>jalium_bitmap_destroy</c> the raising thread had
    /// already run. An access violation cannot be caught, so the regression signal here is an
    /// aborted run rather than a red test — which is also why the deterministic case above exists
    /// and is the one that names the defect.</para>
    /// </remarks>
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ConcurrentEvictionRaisesNeverDisturbTheOwningThreadsDraw()
    {
        var sources = new BitmapImage[RaceSources];
        for (var i = 0; i < sources.Length; i++)
        {
            sources[i] = CreateOpaqueWhiteBitmap();
        }

        try
        {
            using var surface = new GpuSurface();
            using var drawingFinished = new ManualResetEventSlim(false);
            Exception? renderFault = null;
            byte[]? finalPixels = null;

            // Everything that touches the surface runs on this one thread, exactly as the render
            // thread owns the context in production. The test thread only raises events.
            var renderThread = new Thread(() =>
            {
                try
                {
                    for (var frame = 0; frame < RaceFrames; frame++)
                    {
                        surface.DrawFrame(context =>
                        {
                            for (var i = 0; i < sources.Length; i++)
                            {
                                context.DrawImage(sources[i], PaintRect);
                            }
                        });
                    }

                    finalPixels = surface.DrawFrameAndReadBack(
                        context => context.DrawImage(sources[0], PaintRect));
                }
                catch (Exception ex)
                {
                    renderFault = ex;
                }
                finally
                {
                    drawingFinished.Set();
                }
            })
            {
                IsBackground = true,
                Name = "image-cache-eviction-race",
            };

            renderThread.Start();

            var raiseCount = 0L;
            while (!drawingFinished.IsSet)
            {
                for (var i = 0; i < sources.Length; i++)
                {
                    ImageSource.RaiseRasterChanged(sources[i]);
                }

                raiseCount += sources.Length;
            }

            Assert.True(
                renderThread.Join(TimeSpan.FromSeconds(60)),
                "the drawing thread never finished — a torn dictionary can wedge a lookup in its " +
                "bucket chain, which is one of the documented outcomes of this race");

            Assert.Null(renderFault);
            Assert.True(
                raiseCount > 0,
                "the raise loop never ran, so this exercised nothing");

            // Coherent afterwards, not merely un-crashed: the eviction drain runs at the top of
            // every one of those frames, so the last one still has to produce a usable texture.
            Assert.NotNull(finalPixels);
            var average = SampleChannelAverage(finalPixels!, PaintRect);
            Assert.True(
                average >= 0xF0,
                $"the surface should still carry the uploaded white raster; the probe block " +
                $"averaged {average:F1} per channel, i.e. the cache stopped producing textures " +
                "after the concurrent evictions");
        }
        finally
        {
            foreach (var source in sources)
            {
                source.Dispose();
            }
        }
    }

    /// <summary>
    /// A publish by a bitmap INSIDE a vector drawing evicts the raster that baked its pixels in.
    /// </summary>
    /// <remarks>
    /// <para>Drawing a <see cref="DrawingImage"/> as an <see cref="ImageSource"/> CPU-rasterizes it
    /// and caches the result keyed on the OUTER drawing, but the pixels in that raster came from an
    /// inner bitmap with its own identity. The eviction handler looked the raised source up under
    /// the outer key only, so a publish by the inner bitmap matched nothing and the raster built
    /// from its previous pixels stayed cached — and, being reproducible from the drawing, it is
    /// never wrong enough for anything downstream to notice.</para>
    /// <para>Asserted on the cache COUNT rather than on pixels because this is about the eviction
    /// arriving at all: the generation compare on the cache-hit path independently guarantees the
    /// next draw is correct, so a pixel assertion would pass with the eviction leg deleted. What
    /// only the eviction can do is release the superseded texture without waiting for a draw.</para>
    /// </remarks>
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void APublishByABitmapInsideAVectorDrawingEvictsTheRasterBuiltFromIt()
    {
        using var inner = CreateOpaqueWhiteBitmap();
        using var surface = new GpuSurface();

        var group = new DrawingGroup();
        group.Children.Add(new ImageDrawing(inner, PaintRect));
        var drawingImage = new DrawingImage(group);

        // Rasterized on the CPU and uploaded as ONE texture keyed on the rasterization, not on the
        // bitmap inside it — which is precisely why the inner identity has to be tracked.
        surface.DrawFrame(context => context.DrawImage(drawingImage, PaintRect));
        Assert.Equal(1, surface.CachedBitmapCount);

        RaiseRasterChangedOnAnotherThread(inner);
        surface.BeginFrame();

        Assert.Equal(0, surface.CachedBitmapCount);
    }

    /// <summary>
    /// Clearing the caches clears the vector rasterizations too.
    /// </summary>
    /// <remarks>
    /// Neither <c>ClearCache</c> nor <c>ClearBitmapCache</c> touched the vector cache, so window
    /// teardown, the render-thread handover and the low-memory purge each left behind one
    /// full-size <see cref="BitmapImage"/> raster per vector source drawn — a strong reference to
    /// exactly the pixels those calls exist to release, and after the GPU uploads had been disposed
    /// nothing was drawing them. Safe because the raster is reproducible: the worst case is one
    /// re-rasterization on the next frame that needs it, and no caller runs per frame.
    /// </remarks>
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ClearingTheCachesDropsTheVectorRastersToo()
    {
        using var inner = CreateOpaqueWhiteBitmap();
        using var surface = new GpuSurface();

        var group = new DrawingGroup();
        group.Children.Add(new ImageDrawing(inner, PaintRect));
        var drawingImage = new DrawingImage(group);

        surface.DrawFrame(context => context.DrawImage(drawingImage, PaintRect));
        Assert.Equal(1, surface.CachedVectorDrawingCount);

        surface.ClearCaches();

        Assert.Equal(0, surface.CachedVectorDrawingCount);
        Assert.Equal(0, surface.CachedBitmapCount);
    }

    /// <summary>
    /// A 32x32 opaque white BGRA8 bitmap with pixels already resident, so nothing in these tests
    /// depends on the deferred decoder and no publish raises an eviction of its own.
    /// </summary>
    private static BitmapImage CreateOpaqueWhiteBitmap()
    {
        var pixels = new byte[SourcePixels * SourcePixels * 4];
        Array.Fill(pixels, (byte)0xFF);
        return BitmapImage.FromPixels(pixels, SourcePixels, SourcePixels);
    }

    /// <summary>
    /// Raises the event from a thread that does not own the drawing context and waits for it to
    /// finish, so the assertion that follows observes a completed raise rather than racing it.
    /// </summary>
    private static void RaiseRasterChangedOnAnotherThread(ImageSource source)
    {
        Exception? fault = null;
        var raiser = new Thread(() =>
        {
            try
            {
                ImageSource.RaiseRasterChanged(source);
            }
            catch (Exception ex)
            {
                fault = ex;
            }
        })
        {
            IsBackground = true,
            Name = "image-cache-eviction-raiser",
        };

        raiser.Start();
        Assert.True(raiser.Join(TimeSpan.FromSeconds(30)), "the raising thread never returned");
        Assert.Null(fault);
    }

    /// <summary>Mean B/G/R of a small probe block at the centre of <paramref name="rect"/>.</summary>
    private static double SampleChannelAverage(byte[] pixels, Rect rect)
    {
        var centerX = (int)(rect.X + rect.Width / 2);
        var centerY = (int)(rect.Y + rect.Height / 2);
        double total = 0;
        var samples = 0;
        for (var y = centerY - 2; y <= centerY + 2; y++)
        {
            for (var x = centerX - 2; x <= centerX + 2; x++)
            {
                var offset = (y * SurfaceWidth + x) * 4;
                total += pixels[offset] + pixels[offset + 1] + pixels[offset + 2];
                samples += 3;
            }
        }

        return total / samples;
    }

    /// <summary>
    /// A hidden-window D3D12 surface plus the drawing context that paints into it, driven through
    /// the same per-frame sequence a window uses: frame prologue first, then the draw.
    /// </summary>
    private sealed class GpuSurface : IDisposable
    {
        private readonly HiddenNativeWindow _window;
        private readonly RenderContext _context;
        private readonly RenderTarget _target;
        private readonly RenderTargetDrawingContext _drawingContext;

        internal GpuSurface(int bitmapCapacity = 192, long bitmapByteBudget = 256L * 1024 * 1024)
        {
            _window = new HiddenNativeWindow(SurfaceWidth, SurfaceHeight);
            _context = new RenderContext(RenderBackend.D3D12);
            _context.BitmapCacheCapacity = bitmapCapacity;
            _context.BitmapCacheByteBudget = bitmapByteBudget;
            _target = _context.CreateRenderTarget(_window.Hwnd, SurfaceWidth, SurfaceHeight);
            _drawingContext = new RenderTargetDrawingContext(_target, _context);
        }

        internal int CachedBitmapCount => _drawingContext.CachedBitmapCount;

        internal int CachedVectorDrawingCount => _drawingContext.CachedVectorDrawingCount;

        /// <summary>What window teardown, the render-thread handover and a low-memory purge call.</summary>
        internal void ClearCaches() => _drawingContext.ClearCache();

        internal void TrimCaches() => _drawingContext.TrimCacheIfNeeded();

        /// <summary>
        /// The owning thread's frame prologue, verbatim what <c>Window</c>, <c>PopupWindow</c> and
        /// <c>DockIndicatorWindow</c> call before drawing. Exposed on its own so a test can pin
        /// that the eviction drain is wired into it rather than only that the drain exists.
        /// </summary>
        internal void BeginFrame() => _drawingContext.DrainPendingRetainedLayers();

        internal void DrawFrame(Action<RenderTargetDrawingContext> draw)
        {
            BeginFrame();
            Assert.True(_target.TryBeginDraw());
            _target.Clear(0f, 0f, 0f);
            draw(_drawingContext);
            Assert.Equal(JaliumResult.Ok, _target.TryEndDraw());
        }

        internal byte[] DrawFrameAndReadBack(Action<RenderTargetDrawingContext> draw)
        {
            BeginFrame();
            Assert.True(_target.TryBeginDraw());
            _target.Clear(0f, 0f, 0f);
            draw(_drawingContext);
            Assert.Equal(JaliumResult.Ok, _target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, _target.TryEndDraw());

            var pixels = new byte[SurfaceWidth * SurfaceHeight * 4];
            Assert.Equal(
                JaliumResult.Ok,
                _target.FetchReadback(pixels, SurfaceWidth * 4u, out var width, out var height));
            Assert.Equal(SurfaceWidth, width);
            Assert.Equal(SurfaceHeight, height);
            return pixels;
        }

        public void Dispose()
        {
            // ClearCache before Close, and both before the context goes away: Close deliberately
            // does not release cached resources, so every NativeBitmap this fixture uploaded would
            // otherwise survive as garbage and have its FINALIZER call BitmapDestroy against a
            // destroyed native context — an access violation on a GC thread, which takes the host
            // down at whatever unrelated test happens to be running.
            _drawingContext.ClearCache();
            _drawingContext.Close();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            _target.Dispose();
            _context.Dispose();
            _window.Dispose();
        }
    }
}
