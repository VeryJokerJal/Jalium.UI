using Jalium.UI.Diagnostics;
using Jalium.UI.Interop;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

// Jalium.UI.Media.Imaging carries a same-named type; the GPU choke point under test is the
// Interop one.
using RenderTargetDrawingContext = Jalium.UI.Interop.RenderTargetDrawingContext;

namespace Jalium.UI.Tests;

/// <summary>
/// Pins RC2: <b>every</b> bitmap consumer drives the deferred decode, not just the
/// <c>Image</c> control.
/// </summary>
/// <remarks>
/// <para>The decode used to be requested from exactly one place — <c>Image.OnRender</c> — while the
/// GPU choke point (<c>RenderTargetDrawingContext.GetNativeBitmap</c>) merely bailed out on a
/// pending deferred source and returned null. So an <see cref="ImageBrush"/>, an
/// <see cref="ImageDrawing"/> inside a <see cref="DrawingImage"/>, or a <c>Shape.Fill</c> backed by
/// a URI <see cref="BitmapImage"/> had NOTHING in the process that would ever ask for its pixels: it
/// returned null on the first frame and on every frame after it, forever, with no error anywhere.
/// That is a permanent blank on EVERY machine, not only the ones that also hit the RC1 decode
/// loop.</para>
/// <para>Three consumers reach the pixels by three different routes and each needs its own leg of
/// the fix, so each gets its own test here: the GPU choke point, the software rasterizer (which
/// reads the pixel buffer directly and never calls <c>GetNativeBitmap</c> at all), and the retained
/// render cache (whose replay side is where a clean, non-render-dirty visual reaches the
/// renderer).</para>
/// <para>The GPU-backed cases are gated on the D3D12 backend being present and are reported as
/// SKIPPED rather than silently passing when it is not; the mechanism they pin is also covered
/// host-independently by the software-rasterizer and replay cases.</para>
/// </remarks>
[Collection("Application")]
public sealed class ImageBrushDecodeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void ResizingAnImageBrush_RequestsAnUpgradeEvenWithACachedNativeUpload()
    {
        var decoder = new RecordingImageDecoder { NaturalWidth = 512, NaturalHeight = 512, Fill = 0xFF };
        BitmapImage.SetDecoder(decoder);
        using var file = new TempImageFile();
        using var bitmap = file.CreateDeferredImage();
        using var surface = new GpuSurface();
        var brush = new ImageBrush(bitmap);
        surface.DrawFrame(dc => dc.DrawRectangle(brush, null, SmallPaintRect));
        Assert.True(ImagePipelineTestHarness.SpinUntil(() => bitmap.TryGetPixelSnapshot(out var p) && p is not null,
            ImagePipelineTestHarness.DecodeTimeout));
        surface.DrawFrame(dc => dc.DrawRectangle(brush, null, SmallPaintRect));
        Assert.True(bitmap.TryGetPixelSnapshot(out var before));
        var firstWidth = before!.Width;
        surface.DrawFrame(dc => dc.DrawRectangle(brush, null, PaintRect));
        Assert.True(ImagePipelineTestHarness.SpinUntil(() => bitmap.TryGetPixelSnapshot(out var p) && p is not null && p.Width > firstWidth,
            ImagePipelineTestHarness.DecodeTimeout), "The cached brush upload suppressed its larger decode request.");
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void CssBackgroundSize_LargerThanPaintBoundsRequestsEnoughSourcePixels()
    {
        BitmapImage.SetDecoder(new RecordingImageDecoder
        {
            NaturalWidth = 512,
            NaturalHeight = 512,
            Fill = 0xFF,
        });
        using var ordinaryFile = new TempImageFile();
        using var sizedFile = new TempImageFile();
        using var ordinaryBitmap = ordinaryFile.CreateDeferredImage();
        using var sizedBitmap = sizedFile.CreateDeferredImage();
        using var surface = new GpuSurface();
        var size = new CssBackgroundSize(new CssLength(400, CssUnit.Px), new CssLength(400, CssUnit.Px));
        var ordinaryBrush = new ImageBrush(ordinaryBitmap);
        var sizedBrush = new ImageBrush(sizedBitmap)
        {
            CssBackgroundLayout = new CssBackgroundImageLayout(size, CssBackgroundRepeat.Both),
        };

        void DrawBoth() => surface.DrawFrame(dc =>
        {
            dc.DrawRectangle(ordinaryBrush, null, SmallPaintRect);
            dc.DrawRectangle(sizedBrush, null, SmallPaintRect);
        });

        DrawBoth();
        Assert.True(ImagePipelineTestHarness.SpinUntil(
            () => ordinaryBitmap.TryGetPixelSnapshot(out var ordinaryPixels) && ordinaryPixels is not null &&
                  sizedBitmap.TryGetPixelSnapshot(out var sizedPixels) && sizedPixels is not null,
            ImagePipelineTestHarness.DecodeTimeout));
        DrawBoth();
        Assert.True(ImagePipelineTestHarness.SpinUntil(
            () => ImagePipelineTestHarness.PublishedBucket(sizedBitmap).Width >
                  ImagePipelineTestHarness.PublishedBucket(ordinaryBitmap).Width,
            ImagePipelineTestHarness.DecodeTimeout),
            "CSS background-size did not request more pixels than an ordinary brush in the same paint bounds.");
    }

    private const int SurfaceWidth = 256;
    private const int SurfaceHeight = 128;

    /// <summary>Rect the GPU consumers paint into, well inside the surface.</summary>
    private static readonly Rect PaintRect = new(32, 16, 160, 80);

    /// <summary>
    /// Paint rect for the upgrade test. Small enough that the draw's own device-rect hint resolves
    /// to the SAME bucket the test seeded, so the only publication change is the explicit upgrade.
    /// </summary>
    private static readonly Rect SmallPaintRect = new(8, 8, 32, 24);

    /// <summary>
    /// Captures the arguments a replayed <see cref="RecordedDrawing"/> hands to its target, so a
    /// test can assert on the <see cref="ImageSource"/> INSTANCE that survived record -> replay.
    /// </summary>
    private sealed class ImageSourceCapturingContext : DrawingContextAdapter
    {
        internal List<ImageSource> DrawnImages { get; } = [];

        internal List<Brush> DrawnBrushes { get; } = [];

        public override void DrawImage(ImageSource imageSource, Rect rectangle) =>
            DrawnImages.Add(imageSource);

        public override void DrawImage(
            ImageSource imageSource, Rect rectangle, BitmapScalingMode scalingMode) =>
            DrawnImages.Add(imageSource);

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
            if (brush is not null)
            {
                DrawnBrushes.Add(brush);
            }
        }

        public override void DrawLine(Pen pen, Point point0, Point point1) { }

        public override void DrawRoundedRectangle(
            Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }

        public override void DrawEllipse(
            Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }

        public override void DrawText(FormattedText formattedText, Point origin) { }

        public override void DrawBackdropEffect(
            Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }

        public override void PushClip(Geometry clipGeometry) { }

        public override void PushTransform(Transform transform) { }

        public override void PushOpacity(double opacity) { }

        public override void Pop() { }

        public override void Close() { }
    }

    /// <summary>
    /// The software rasterizer reads the pixel buffer directly and never routes through
    /// <c>GetNativeBitmap</c>, so fixing the GPU choke point alone leaves this path blank.
    /// </summary>
    /// <remarks>
    /// This is the WARP / software-backend / <see cref="DrawingImage"/> / SVG <c>&lt;image&gt;</c>
    /// case, and it needs no GPU at all — which is what makes it the host-independent proof that
    /// "the consumer drives the decode" is a property of the pipeline rather than of one code path.
    /// </remarks>
    [Fact]
    public void SoftwareVectorRasterizerPathReachesADecode()
    {
        // Position-encoded pixels: B = source x, G = source y, R = 0x80, A = 0xFF. The two constant
        // channels survive the rasterizer's bilinear filter exactly, so they pin "these are the
        // decoded pixels" rather than merely "something non-transparent was written"; the two
        // varying channels pin that the blit sampled ACROSS the image rather than smearing one
        // texel.
        var decoder = new RecordingImageDecoder
        {
            NaturalWidth = 32,
            NaturalHeight = 32,
            PositionEncodedPixels = true,
        };
        BitmapImage.SetDecoder(decoder);

        using var file = new TempImageFile();
        using var bitmap = file.CreateDeferredImage();

        var group = new DrawingGroup();
        group.Children.Add(new ImageDrawing(bitmap, new Rect(0, 0, 16, 16)));

        Assert.True(bitmap.IsDeferredDecodePending);

        // First rasterization: no pixels exist yet, so nothing can be blitted — but the call must
        // have REQUESTED the decode. Before the fix nothing in this method touched the deferred
        // source at all, so the count below stayed at zero forever.
        _ = SoftwareVectorRasterizer.Rasterize(group, 16, 16, new Rect(0, 0, 16, 16));

        Assert.True(
            ImagePipelineTestHarness.SpinUntil(
                () => decoder.DecodeCalls >= 1, ImagePipelineTestHarness.DecodeTimeout),
            "rasterizing an ImageDrawing never requested a decode of its deferred source");
        Assert.True(
            ImagePipelineTestHarness.SpinUntil(
                () => !bitmap.IsDeferredDecodePending, ImagePipelineTestHarness.DecodeTimeout),
            "the requested decode never published pixels");

        // Second rasterization, now that the decode has landed: the pixels must actually reach the
        // output buffer.
        var pixels = SoftwareVectorRasterizer.Rasterize(group, 16, 16, new Rect(0, 0, 16, 16));
        Assert.NotNull(pixels);

        const int topLeft = 0;
        var bottomRight = ((15 * 16) + 15) * 4;

        // The constant channels are exact: these bytes came from the decoded raster.
        Assert.Equal(0x80, pixels![topLeft + 2]);
        Assert.Equal(0xFF, pixels[topLeft + 3]);
        Assert.Equal(0x80, pixels[bottomRight + 2]);
        Assert.Equal(0xFF, pixels[bottomRight + 3]);

        // The varying channels prove the 32x32 source really was sampled across its whole extent:
        // the top-left destination pixel maps near source (0,0) and the bottom-right near (31,31).
        Assert.InRange(pixels[topLeft], 0, 4);          // B = source x
        Assert.InRange(pixels[topLeft + 1], 0, 4);      // G = source y
        Assert.InRange(pixels[bottomRight], 27, 31);
        Assert.InRange(pixels[bottomRight + 1], 27, 31);

        Assert.Equal(1, decoder.DecodeCalls);
    }

    /// <summary>
    /// A degenerate rasterization must cost a 1px bucket, never the "unbounded" full-resolution one.
    /// </summary>
    /// <remarks>
    /// The rasterizer floors its size hint at 1 rather than passing a computed zero straight
    /// through. An all-zero request is the deferred decoder's encoding of "the caller does not know
    /// the size yet, so it may need every pixel", and because a display bucket may only ever GROW,
    /// letting one degenerate frame through would pin the full-resolution raster resident for the
    /// life of the source — a memory regression with no visible symptom, which is the kind that
    /// survives.
    /// </remarks>
    [Fact]
    public void ADegenerateRasterizationNeverAsksForTheFullResolutionRaster()
    {
        var decoder = new RecordingImageDecoder { NaturalWidth = 512, NaturalHeight = 512 };
        BitmapImage.SetDecoder(decoder);

        using var file = new TempImageFile();
        using var bitmap = file.CreateDeferredImage();

        var group = new DrawingGroup();
        group.Children.Add(new ImageDrawing(bitmap, new Rect(0, 0, 1, 1)));

        _ = SoftwareVectorRasterizer.Rasterize(group, 1, 1, new Rect(0, 0, 1, 1));

        Assert.True(
            ImagePipelineTestHarness.SpinUntil(
                () => bitmap.TryGetPixelSnapshot(out var s) && s is not null,
                ImagePipelineTestHarness.DecodeTimeout),
            "the 1x1 rasterization never produced a publication");

        Assert.True(bitmap.TryGetPixelSnapshot(out var snapshot));
        Assert.NotNull(snapshot);
        Assert.Equal(512, snapshot!.CanonicalWidth);
        Assert.True(
            snapshot.Width < snapshot.CanonicalWidth,
            $"a 1x1 draw published the full {snapshot.Width}px raster — the hint was read as " +
            "\"unbounded\" rather than as one pixel");
    }

    /// <summary>
    /// The retained render cache replays the <see cref="ImageSource"/> INSTANCE, so the replay side
    /// — which is where the decode is now driven — reaches the live deferred source.
    /// </summary>
    /// <remarks>
    /// <para>A visual whose <c>_isRenderDirty</c> is false legitimately never re-runs
    /// <c>OnRender</c>; its recorded drawing is replayed instead. With the decode trigger living in
    /// <c>Image.OnRender</c> that meant a scrolled-away-and-back image could never ask for its
    /// pixels again. Moving the trigger to the replay side only works because the recorder stores
    /// the source by reference (<c>DrawInputSnapshotter.SnapshotImage</c> returns the same instance
    /// for anything that is not a <see cref="WriteableBitmap"/>) rather than snapshotting its
    /// pixels — so this test pins that identity, which the whole leg rests on.</para>
    /// <para>An <see cref="ImageBrush"/> is recorded the same way, so both routes are asserted.</para>
    /// </remarks>
    [Fact]
    public void RetainedRenderCacheReplayPreservesTheLiveImageSourceInstance()
    {
        BitmapImage.SetDecoder(new RecordingImageDecoder { NaturalWidth = 64, NaturalHeight = 64 });

        using var file = new TempImageFile();
        using var bitmap = file.CreateDeferredImage();
        var brush = new ImageBrush(bitmap);

        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder();
        Assert.NotNull(recorder);

        recorder.DrawImage(bitmap, PaintRect);
        recorder.DrawRectangle(brush, null, PaintRect);
        var drawing = host.FinishRecord(recorder);

        var replay = new ImageSourceCapturingContext();
        host.Replay(drawing, replay);

        // Reference identity, not equality: a replay that handed over a copy — or a pixel snapshot
        // — would leave the live source with no consumer asking for its decode, which is the exact
        // shape of the bug.
        Assert.Same(bitmap, Assert.Single(replay.DrawnImages));
        var replayedBrush = Assert.IsType<ImageBrush>(Assert.Single(replay.DrawnBrushes));
        Assert.Same(brush, replayedBrush);
        Assert.Same(bitmap, replayedBrush.ImageSource);

        // Replaying again must reach the same instance, because a cached drawing is replayed once
        // per frame for as long as the visual stays clean.
        var secondReplay = new ImageSourceCapturingContext();
        host.Replay(drawing, secondReplay);
        Assert.Same(bitmap, Assert.Single(secondReplay.DrawnImages));
    }

    /// <summary>
    /// A bucket upgrade must not keep being served from a thumbnail synthesised off the superseded
    /// small buffer.
    /// </summary>
    /// <remarks>
    /// The downscale cache key used to be a fully-packed <c>ulong</c> — 32 bits of source identity
    /// hash plus 16 bits each of bucket width and height, with no spare bit — so an upgraded raster
    /// produced the SAME key as the raster it replaced and the pre-upgrade thumbnail kept being
    /// returned. No crash, no log line, just a permanently soft image. The key now carries the
    /// publication generation, which is monotonic, so a superseded entry's key can never be
    /// produced again and it ages out of the LRU by itself.
    /// </remarks>
    [Fact]
    public void UpgradedRasterIsNotServedFromThePreUpgradeDownscaleThumbnail()
    {
        BitmapDownscaleCache.Clear();

        // 0x11 and 0x22 fills make "which publication did this thumbnail come from?" readable off a
        // single probe byte, whatever the box filter does to the geometry.
        var small = MakeSnapshot(256, 256, 0x11, canonicalWidth: 1024, canonicalHeight: 1024);
        var large = MakeSnapshot(1024, 1024, 0x22, canonicalWidth: 1024, canonicalHeight: 1024);
        Assert.True(
            large.Generation > small.Generation,
            "publication generations must increase, or a generation-keyed cache cannot notice a " +
            "replaced raster");

        var owner = BitmapImage.FromPixels(small.Pixels, small.Width, small.Height, small.Stride);

        // First draw at 32px: a miss that enqueues synthesis from the SMALL publication.
        Assert.False(BitmapDownscaleCache.TryGetOrCreate(owner, small, 32, 32, out _));
        Assert.True(
            ImagePipelineTestHarness.SpinUntil(
                () => BitmapDownscaleCache.TryGetOrCreate(owner, small, 32, 32, out _),
                ImagePipelineTestHarness.DecodeTimeout),
            "the thumbnail was never synthesised, so the staleness test below proves nothing");
        Assert.True(BitmapDownscaleCache.TryGetOrCreate(owner, small, 32, 32, out var fromSmall));
        Assert.Equal(0x11, fromSmall.RawPixelData![0]);

        // The source upgrades its bucket under the SAME reference identity and at the SAME
        // requested display size. Without the generation in the key this returns the 0x11 thumbnail.
        Assert.False(
            BitmapDownscaleCache.TryGetOrCreate(owner, large, 32, 32, out _),
            "an upgraded publication was served the thumbnail synthesised from the superseded one");

        Assert.True(
            ImagePipelineTestHarness.SpinUntil(
                () => BitmapDownscaleCache.TryGetOrCreate(owner, large, 32, 32, out var candidate) &&
                      candidate.RawPixelData is { Length: > 0 } bytes && bytes[0] == 0x22,
                ImagePipelineTestHarness.DecodeTimeout),
            "the upgraded publication never produced a thumbnail of its own");
    }

    /// <summary>
    /// The RC2 acceptance criterion on real hardware: an <see cref="ImageBrush"/> with no
    /// <c>Image</c> control anywhere resolves a native bitmap and paints.
    /// </summary>
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ImageBrushWithNoImageControlInTheTreeResolvesANativeBitmap()
    {
        var decoder = new RecordingImageDecoder
        {
            NaturalWidth = 64,
            NaturalHeight = 64,
            Fill = 0xFF, // opaque white on every channel
        };
        BitmapImage.SetDecoder(decoder);

        using var file = new TempImageFile();
        using var bitmap = file.CreateDeferredImage();
        var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };

        using var surface = new GpuSurface();

        // Frame 1 cannot paint — the decode has not run — but it must REQUEST it. That is the whole
        // fix: before it, the choke point saw a pending deferred source, returned null, and left
        // nobody in the process asking for the pixels.
        surface.DrawFrame(context => context.DrawRectangle(brush, null, PaintRect));
        Assert.True(
            ImagePipelineTestHarness.SpinUntil(
                () => bitmap.TryGetPixelSnapshot(out var s) && s is not null,
                ImagePipelineTestHarness.DecodeTimeout),
            "an ImageBrush with no Image control never drove a decode of its deferred source");

        // Frame 2 has pixels, so the upload must happen and the brush must paint.
        var pixels = surface.DrawFrameAndReadBack(
            context => context.DrawRectangle(brush, null, PaintRect));

        AssertOpaqueWhite(pixels, PaintRect);
    }

    /// <summary>
    /// Same acceptance criterion through the <see cref="ImageDrawing"/> / <see cref="DrawingImage"/>
    /// route, which reaches the choke point via <c>DrawImage</c> rather than via the brush tile
    /// filler — including the <b>pre-decode</b> frame, which is the one that used to be cached.
    /// </summary>
    /// <remarks>
    /// <para>The ordering here is the test. Drawing a <see cref="DrawingImage"/> AS an
    /// <see cref="ImageSource"/> CPU-rasterizes it into a <see cref="BitmapImage"/> and caches that
    /// raster keyed on the OUTER vector source, with a hit test that used to compare nothing but
    /// the target pixel size. The very first rasterization of a URI-backed inner bitmap necessarily
    /// runs before its decode has published anything, and produces a perfectly valid, entirely
    /// transparent buffer — so the blank frame got cached and, because the publish raises its
    /// staleness signal for the INNER bitmap while the entry is keyed on the OUTER drawing, the
    /// eviction missed and the blank raster was served for the life of the window. An icon in a
    /// fixed-size cell never changes target size, so nothing else would ever have displaced it.</para>
    /// <para>This version therefore draws through the cache FIRST, while the source is still
    /// pending, and only then waits for the decode and draws again at exactly the same size. The
    /// previous version did it the other way round — awaiting the publish before ever touching the
    /// vector cache — so it never populated the cache with a blank raster and passed against the
    /// defect.</para>
    /// </remarks>
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ImageDrawingInsideADrawingImageResolvesANativeBitmap()
    {
        var decoder = new RecordingImageDecoder
        {
            NaturalWidth = 64,
            NaturalHeight = 64,
            Fill = 0xFF,
        };
        BitmapImage.SetDecoder(decoder);

        using var file = new TempImageFile();
        using var bitmap = file.CreateDeferredImage();

        var group = new DrawingGroup();
        group.Children.Add(new ImageDrawing(bitmap, PaintRect));
        var drawingImage = new DrawingImage(group);

        using var surface = new GpuSurface();

        // Frame 1, through the VECTOR RASTER CACHE, with no pixels in existence: this populates
        // the cache with the blank rasterization and must also request the decode.
        Assert.True(bitmap.IsDeferredDecodePending);
        surface.DrawFrame(context => context.DrawImage(drawingImage, PaintRect));

        Assert.True(
            ImagePipelineTestHarness.SpinUntil(
                () => bitmap.TryGetPixelSnapshot(out var s) && s is not null,
                ImagePipelineTestHarness.DecodeTimeout),
            "a DrawingImage rasterization never drove a decode of the bitmap inside it");

        // Frame 2 at exactly the same size, so nothing but the staleness signal can displace the
        // cached raster. A blank buffer here is the defect: the pixels exist and the window is
        // still showing the frame that was rendered before they did.
        var viaImageSource = surface.DrawFrameAndReadBack(
            context => context.DrawImage(drawingImage, PaintRect));

        AssertOpaqueWhite(viaImageSource, PaintRect);

        // And rendered as a Drawing, which bypasses the vector cache: the ImageDrawing's own
        // DrawImage is what has to reach the decode driver. This is the leg an SVG <image> and a
        // DrawingBrush both take.
        var pixels = surface.DrawFrameAndReadBack(
            context => context.DrawDrawing(drawingImage.Drawing!));

        AssertOpaqueWhite(pixels, PaintRect);
    }

    /// <summary>
    /// An <see cref="ImageBrush"/> used as a vector FILL drives its own decode, and paints nothing
    /// rather than opaque black while that decode is in flight.
    /// </summary>
    /// <remarks>
    /// <para>The last leg of RC2, and the one the GPU choke-point fix cannot reach: a brush filling
    /// a <see cref="GeometryDrawing"/> inside a <see cref="DrawingImage"/> or an SVG is resolved by
    /// the software rasterizer, which samples the source's pixel buffer directly and never calls
    /// <c>GetNativeBitmap</c>. Nothing in the process asked such a source for its pixels, so
    /// <c>SampleImageAverage</c> found none, and the fill fell through to the rasterizer's
    /// opaque-black last resort — permanently, and cached. Painting a black silhouette is worse
    /// than painting nothing, which is why the pending frame is now transparent.</para>
    /// <para>Host-independent by construction: no GPU, no dispatcher, just the rasterizer.</para>
    /// </remarks>
    [Fact]
    public void AnImageBrushUsedAsAVectorFillDrivesItsOwnDecode()
    {
        var decoder = new RecordingImageDecoder
        {
            NaturalWidth = 512,
            NaturalHeight = 512,
            Fill = 0xFF,
        };
        BitmapImage.SetDecoder(decoder);

        using var file = new TempImageFile();
        using var bitmap = file.CreateDeferredImage();

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new ImageBrush(bitmap),
            null,
            new RectangleGeometry(new Rect(4, 4, 24, 24))));

        Assert.True(bitmap.IsDeferredDecodePending);

        // Frame 1: nothing can be sampled yet.
        var pending = SoftwareVectorRasterizer.Rasterize(group, 32, 32, new Rect(0, 0, 32, 32));
        Assert.NotNull(pending);

        var center = ((16 * 32) + 16) * 4;
        Assert.Equal(0, pending![center + 3]);
        Assert.True(
            pending[center] == 0 && pending[center + 1] == 0 && pending[center + 2] == 0,
            "a pending image brush painted something opaque — the black silhouette is what gets " +
            "baked into the caller's cached raster");

        Assert.True(
            ImagePipelineTestHarness.SpinUntil(
                () => decoder.DecodeCalls >= 1, ImagePipelineTestHarness.DecodeTimeout),
            "an ImageBrush used as a vector fill never requested a decode of its source");
        Assert.True(
            ImagePipelineTestHarness.SpinUntil(
                () => !bitmap.IsDeferredDecodePending, ImagePipelineTestHarness.DecodeTimeout),
            "the requested decode never published pixels");

        // The hint is the SHAPE's device extent, not "unbounded": a 24x24 fill must not pin the
        // 512x512 raster resident, because a growth-only bucket ladder can never walk that back.
        Assert.True(bitmap.TryGetPixelSnapshot(out var snapshot));
        Assert.NotNull(snapshot);
        Assert.Equal(512, snapshot!.CanonicalWidth);
        Assert.True(
            snapshot.Width < snapshot.CanonicalWidth,
            $"a 24x24 vector fill published the full {snapshot.Width}px raster");

        // Frame 2: the fill paints the source's average colour.
        var painted = SoftwareVectorRasterizer.Rasterize(group, 32, 32, new Rect(0, 0, 32, 32));
        Assert.NotNull(painted);
        Assert.True(
            painted![center + 3] > 0xF0 &&
            painted[center] > 0xF0 && painted[center + 1] > 0xF0 && painted[center + 2] > 0xF0,
            "the decoded white source should have filled the shape; the centre pixel was " +
            $"({painted[center]}, {painted[center + 1]}, {painted[center + 2]}, " +
            $"{painted[center + 3]})");
    }

    /// <summary>
    /// A rasterization reports the inner sources it read, which is what lets the caller's cache
    /// know a publish invalidated it.
    /// </summary>
    /// <remarks>
    /// The GPU-backed test above proves the end-to-end behaviour on a real device; this pins the
    /// mechanism it rests on without one, for both routes into an inner bitmap (an
    /// <see cref="ImageDrawing"/> and an <see cref="ImageBrush"/> fill) and specifically on the
    /// PENDING frame, where nothing was blitted and an early return could plausibly skip the
    /// bookkeeping.
    /// </remarks>
    [Fact]
    public void ARasterizationReportsEveryInnerSourceItRead()
    {
        BitmapImage.SetDecoder(new RecordingImageDecoder { NaturalWidth = 64, NaturalHeight = 64 });

        using var drawn = new TempImageFile();
        using var filled = new TempImageFile();
        using var drawnBitmap = drawn.CreateDeferredImage();
        using var filledBitmap = filled.CreateDeferredImage();

        var group = new DrawingGroup();
        group.Children.Add(new ImageDrawing(drawnBitmap, new Rect(0, 0, 16, 16)));
        group.Children.Add(new GeometryDrawing(
            new ImageBrush(filledBitmap),
            null,
            new RectangleGeometry(new Rect(0, 0, 16, 16))));

        Assert.True(drawnBitmap.IsDeferredDecodePending);
        Assert.True(filledBitmap.IsDeferredDecodePending);

        var touched = new HashSet<ImageSource>();
        _ = SoftwareVectorRasterizer.Rasterize(group, 16, 16, new Rect(0, 0, 16, 16), touched);

        Assert.Contains(drawnBitmap, touched);
        Assert.Contains(filledBitmap, touched);
        Assert.Equal(2, touched.Count);
    }

    /// <summary>
    /// A raster upgrade under a reference-equal identity must actually re-upload.
    /// </summary>
    /// <remarks>
    /// The GPU cache is keyed on the source's reference identity, and a deferred bitmap that grows
    /// its display bucket keeps that identity. The staleness test used to cover only
    /// <see cref="WriteableBitmap"/>, so an upgraded <see cref="BitmapImage"/> kept being served the
    /// superseded texture: the upgrade decode ran in full and the screen never changed.
    /// </remarks>
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void UpgradedRasterIsReUploadedRatherThanServedFromTheStaleTexture()
    {
        // Decode #1 produces a barely-visible raster, the upgrade an opaque white one. Same source,
        // same reference identity: the ONLY thing that changes is the publication, which is exactly
        // what the cache has to notice.
        var decoder = new RecordingImageDecoder
        {
            NaturalWidth = 512,
            NaturalHeight = 512,
            Fill = 0x30,
        };
        BitmapImage.SetDecoder(decoder);

        using var file = new TempImageFile();
        using var bitmap = file.CreateDeferredImage();
        using var surface = new GpuSurface();

        bitmap.RequestDecode(32, 24, cover: false);
        Assert.True(
            ImagePipelineTestHarness.SpinUntil(
                () => bitmap.TryGetPixelSnapshot(out var s) && s is not null,
                ImagePipelineTestHarness.DecodeTimeout),
            "the seed decode never published");

        var beforePixels = surface.DrawFrameAndReadBack(
            context => context.DrawImage(bitmap, SmallPaintRect));
        Assert.True(
            SampleChannelAverage(beforePixels, SmallPaintRect) < 0x80,
            "the seed raster was expected to paint dark, so the probe below cannot tell the two " +
            "publications apart");

        // Genuine growth to the full-resolution bucket. The attempt bound counts only CONSECUTIVE
        // unproductive decodes, so a real upgrade is never refused.
        decoder.Fill = 0xFF;
        bitmap.RequestDecode(400, 400, cover: false);
        Assert.True(
            ImagePipelineTestHarness.SpinUntil(
                () => bitmap.TryGetPixelSnapshot(out var s) && s is { Width: 512 },
                ImagePipelineTestHarness.DecodeTimeout),
            "the bucket upgrade never reached the full-resolution publication");

        var afterPixels = surface.DrawFrameAndReadBack(
            context => context.DrawImage(bitmap, SmallPaintRect));

        Assert.True(
            SampleChannelAverage(afterPixels, SmallPaintRect) >= 0xF0,
            "the upgraded raster was never re-uploaded — the cache kept serving the superseded " +
            "texture under the same reference identity");
    }

    /// <summary>
    /// A real upload that succeeds must release the latch a failed one left behind.
    /// </summary>
    /// <remarks>
    /// <para>The other half of "an upload failure never stops the draw", and the half that only a
    /// real device can pin: the element keeps issuing <c>DrawImage</c> so the retry happens, and
    /// <c>GetNativeBitmap</c>'s successful branch is what records that it worked. Without the
    /// release the latch is permanent — the once-per-episode <c>ImageFailed</c> report is silenced
    /// for the life of the source, and <c>ImageSource.LoadFailure</c> keeps describing a fault the
    /// GPU has demonstrably recovered from.</para>
    /// <para>The source is resident (no deferred decode) and is drawn LARGER than itself on
    /// purpose. A decode publish would clear the latch through its own completion path and prove
    /// nothing about the upload, and a source drawn smaller than itself is served through
    /// <c>BitmapDownscaleCache</c>, which would hand the choke point a synthesized thumbnail rather
    /// than the instance the latch is on.</para>
    /// </remarks>
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ASuccessfulUploadReleasesTheLatchLeftByAFailedOne()
    {
        var pixels = new byte[64 * 64 * 4];
        Array.Fill(pixels, (byte)0xFF);
        using var bitmap = BitmapImage.FromPixels(pixels, 64, 64);
        using var surface = new GpuSurface();

        // Verbatim what the upload catch in GetNativeBitmap does through
        // BitmapDecodeNotifier.PostSourceFailure.
        var uploadFault = new InvalidOperationException("gpu upload failed");
        Assert.True(bitmap.TryLatchUploadFailure(uploadFault));
        Assert.Same(uploadFault, bitmap.LoadFailure);

        var painted = surface.DrawFrameAndReadBack(
            context => context.DrawImage(bitmap, PaintRect));

        // The retry really did upload and sample, so the release below is about a successful
        // upload rather than about a frame that quietly drew nothing.
        AssertOpaqueWhite(painted, PaintRect);

        Assert.Null(bitmap.LoadFailure);
    }

    /// <summary>
    /// A failing GPU upload is reported on the Release-live channel AND into the source's own
    /// failure chain, and the next successful upload paints.
    /// </summary>
    /// <remarks>
    /// <para>The upload catch is the site RC3 exists for — it was a <c>Debug.WriteLine</c>, i.e.
    /// nothing at all in the builds users run — and it was also the one site no test could reach,
    /// because the faults it handles are VRAM exhaustion, a device-removed frame and an over-limit
    /// texture dimension on a WARP/RDP adapter. Reverting BOTH of its reporting calls to
    /// <c>Debug.WriteLine</c> left the whole suite green. <c>UploadFaultInjector</c> is the seam
    /// that stands in for the adapter; the two assertions below are the two reporting calls, and
    /// they are separately discriminating (one is the support channel, the other is the
    /// application's <c>ImageFailed</c> chain).</para>
    /// <para>The recovery leg matters as much as the failure leg: the failure is UPLOAD-class, so
    /// the element must keep drawing and the retry must be allowed to succeed. A test that only
    /// asserted the report would pass against a fix that turned one bad frame into a permanently
    /// blank image.</para>
    /// </remarks>
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void AFailedGpuUploadIsReportedOnBothChannelsAndTheRetryStillPaints()
    {
        var pixels = new byte[64 * 64 * 4];
        Array.Fill(pixels, (byte)0xFF);
        using var bitmap = BitmapImage.FromPixels(pixels, 64, 64);
        using var surface = new GpuSurface();
        using var recorder = new ImageDiagnosticsRecorder("<bitmap>");

        var fault = new InvalidOperationException("injected upload fault");
        var before = ImageDiagnostics.Snapshot().UploadFailureCount;

        try
        {
            RenderTargetDrawingContext.UploadFaultInjector = _ => fault;

            // Drawn LARGER than itself on purpose: a source drawn smaller is served through
            // BitmapDownscaleCache, whose synthesized thumbnail is a different instance and would
            // carry the latch somewhere the assertions below cannot see.
            var blank = surface.DrawFrameAndReadBack(
                context => context.DrawImage(bitmap, PaintRect));

            Assert.True(
                SampleChannelAverage(blank, PaintRect) < 0x10,
                "the injected fault did not actually stop the upload, so nothing below is about a " +
                "failed one");
        }
        finally
        {
            RenderTargetDrawingContext.UploadFaultInjector = null;
        }

        // Channel 1: the always-live support channel. Counter AND record — a support capture reads
        // Snapshot() and the trace file, and they are written by different lines.
        Assert.True(
            ImageDiagnostics.Snapshot().UploadFailureCount > before,
            "a failed GPU upload did not move the UploadFailureCount counter");
        var reported = Assert.Single(
            recorder.Events, e => e.Kind == ImageDiagnosticKind.UploadFailed);
        Assert.Same(fault, reported.Error);
        Assert.False(string.IsNullOrEmpty(reported.Detail));

        // Channel 2: the application's chain. PostSourceFailure latches on the discovering thread,
        // so this holds without a dispatcher having drained anything.
        Assert.Same(fault, bitmap.LoadFailure);

        // And the retry: the fault is gone, so the very next frame must upload and paint. This is
        // the half that fails if an upload failure is ever allowed to gate the draw.
        var painted = surface.DrawFrameAndReadBack(
            context => context.DrawImage(bitmap, PaintRect));

        AssertOpaqueWhite(painted, PaintRect);
        Assert.Null(bitmap.LoadFailure);
    }

    /// <summary>
    /// The decode hint is the caller's real DEVICE rect: it tracks the draw rectangle, and it folds
    /// in the transform the draw happens under.
    /// </summary>
    /// <remarks>
    /// Deleting the device-rect conversion and passing the source's own size — or any
    /// process-global scale — left every test green, because the rest of the suite only asserts
    /// that a decode HAPPENED. Both halves are load-bearing in opposite directions: a hint that
    /// ignores the rect publishes a full-resolution raster for a thumbnail slot (the memory
    /// regression the display bucket exists to prevent), and a hint that ignores the transform
    /// publishes a 1x raster for content scaled up 8x (a visibly soft image on every zoomed or
    /// high-DPI surface). Two sources rather than one measurement, because a bucket may only ever
    /// grow — asking the same source twice would measure the ladder, not the hint.
    /// </remarks>
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void TheDecodeHintIsTheDrawRectFoldedThroughTheLiveTransform()
    {
        BitmapImage.SetDecoder(new RecordingImageDecoder
        {
            NaturalWidth = 512,
            NaturalHeight = 512,
        });

        using var unscaledFile = new TempImageFile();
        using var scaledFile = new TempImageFile();
        using var unscaled = unscaledFile.CreateDeferredImage();
        using var scaled = scaledFile.CreateDeferredImage();
        using var surface = new GpuSurface();

        surface.DrawFrame(context => context.DrawImage(unscaled, SmallPaintRect));
        surface.DrawFrame(context =>
        {
            context.PushTransform(new ScaleTransform { ScaleX = 8, ScaleY = 8 });
            try
            {
                context.DrawImage(scaled, SmallPaintRect);
            }
            finally
            {
                context.Pop();
            }
        });

        Assert.True(
            ImagePipelineTestHarness.SpinUntil(
                () => ImagePipelineTestHarness.PublishedBucket(unscaled).Width > 0 &&
                      ImagePipelineTestHarness.PublishedBucket(scaled).Width > 0,
                ImagePipelineTestHarness.DecodeTimeout),
            "one of the two draws never published a raster, so the comparison below is vacuous");

        var small = ImagePipelineTestHarness.PublishedBucket(unscaled);
        var large = ImagePipelineTestHarness.PublishedBucket(scaled);

        Assert.True(
            small.Width < 512,
            $"a {SmallPaintRect.Width}x{SmallPaintRect.Height} draw published a {small.Width}px " +
            "raster of a 512px source — the hint is not derived from the draw rect");
        Assert.True(
            large.Width > small.Width,
            $"the same rect under an 8x transform published {large.Width}px against " +
            $"{small.Width}px unscaled — the hint does not fold in the transform");
    }

    /// <summary>
    /// A hidden-window D3D12 surface plus the drawing context that paints into it.
    /// </summary>
    /// <remarks>
    /// Deliberately a real backend rather than a fake: the property under test is that a
    /// <see cref="NativeBitmap"/> is produced and sampled, and a fake render context would assert
    /// only that a managed call was made.
    /// </remarks>
    private sealed class GpuSurface : IDisposable
    {
        private readonly HiddenNativeWindow _window;
        private readonly RenderContext _context;
        private readonly RenderTarget _target;
        private readonly RenderTargetDrawingContext _drawingContext;

        internal GpuSurface()
        {
            _window = new HiddenNativeWindow(SurfaceWidth, SurfaceHeight);
            _context = new RenderContext(RenderBackend.D3D12);
            _target = _context.CreateRenderTarget(_window.Hwnd, SurfaceWidth, SurfaceHeight);
            _drawingContext = new RenderTargetDrawingContext(_target, _context);
        }

        /// <summary>
        /// The owning thread's frame prologue, verbatim what <c>Window</c>, <c>PopupWindow</c> and
        /// <c>DockIndicatorWindow</c> call before drawing. Every frame here runs it, because it is
        /// where a queued cache eviction is applied — a fixture that skipped it would be testing a
        /// per-frame sequence no window uses.
        /// </summary>
        private void BeginFrame() => _drawingContext.DrainPendingRetainedLayers();

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
            // ClearCache before Close, and both before the context goes away.
            //
            // Close() deliberately does NOT release cached resources — a real window reuses them
            // across the context's life — so every NativeBitmap this fixture uploaded would
            // otherwise survive as garbage and have its FINALIZER call BitmapDestroy against a
            // destroyed native context. That is an access violation on a GC thread, which takes the
            // whole test host down at whatever unrelated test happens to be running. Window
            // teardown pays the same cost through ClearBitmapCache for the same reason.
            _drawingContext.ClearCache();
            _drawingContext.Close();

            // Drain anything the cache did not own (a raster replaced mid-frame, an upload dropped
            // by the staleness compare) while the context that created it is still alive.
            GC.Collect();
            GC.WaitForPendingFinalizers();

            _target.Dispose();
            _context.Dispose();
            _window.Dispose();
        }
    }

    /// <summary>
    /// Mean B/G/R of a small probe block at the centre of <paramref name="rect"/>. Averaging rather
    /// than sampling one pixel keeps the assertion independent of filtering at the rect's edges.
    /// </summary>
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

    private static void AssertOpaqueWhite(byte[] pixels, Rect rect)
    {
        var average = SampleChannelAverage(pixels, rect);
        Assert.True(
            average >= 0xF0,
            $"expected the uploaded white raster inside {rect}; the probe block averaged " +
            $"{average:F1} per channel. A near-zero average means the surface stayed at the " +
            "clear colour, i.e. no NativeBitmap was produced.");
    }

    private static BitmapPixelSnapshot MakeSnapshot(
        int width, int height, byte fill, int canonicalWidth, int canonicalHeight)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        Array.Fill(pixels, fill);
        return BitmapPixelSnapshot.Create(
            pixels, width, height, stride, NativePixelFormat.Bgra8,
            canonicalWidth, canonicalHeight);
    }
}
