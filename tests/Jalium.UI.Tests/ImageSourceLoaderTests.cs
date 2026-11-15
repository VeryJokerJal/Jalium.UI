using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// Verifies <see cref="ImageSourceLoader"/> picks an animated
/// <see cref="AnimatedBitmap"/> for multi-frame payloads (animated GIF / APNG /
/// animated WebP) and a static <see cref="BitmapImage"/> otherwise — the gap that
/// previously made <c>&lt;Image Source="x.gif"/&gt;</c> show only the first frame
/// (issue #121). Uses an injected fake decoder so it does not depend on the native
/// jalium.native.media DLL.
/// </summary>
// SetDecoder is process-global; serialize with every other decoder-injecting class.
[Collection("Application")]
public class ImageSourceLoaderTests
{
    private sealed class FrameFakeDecoder : INativeImageDecoder
    {
        public int FrameCount = 1;
        public int DecodeCalls;
        public int DecodeFrameCalls;
        public int Width = 4;
        public int Height = 4;

        /// <summary>
        /// Milliseconds every pixel decode blocks for. Lets a test prove that a synchronous caller
        /// — the XAML type converter — did not wait on the decode, by making the wait it must not
        /// have taken orders of magnitude longer than the bound being asserted.
        /// </summary>
        public int DecodeDelayMs;

        private DecodedImage MakeImage(NativePixelFormat format)
        {
            var stride = Width * 4;
            return new DecodedImage(new byte[stride * Height], Width, Height, stride, format);
        }

        public DecodedImage Decode(ReadOnlySpan<byte> data, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
        {
            Interlocked.Increment(ref DecodeCalls);
            if (DecodeDelayMs > 0)
            {
                Thread.Sleep(DecodeDelayMs);
            }

            return MakeImage(requestedFormat);
        }

        public DecodedImage Decode(Stream stream, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
            => Decode(ReadOnlySpan<byte>.Empty, requestedFormat);

        public DecodedImage DecodeFile(string filePath, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
            => Decode(ReadOnlySpan<byte>.Empty, requestedFormat);

        public bool TryReadDimensions(ReadOnlySpan<byte> data, out int width, out int height)
        {
            width = Width;
            height = Height;
            return true;
        }

        public int ReadFrameCount(ReadOnlySpan<byte> data) => FrameCount;

        public DecodedImageFrame DecodeFrame(ReadOnlySpan<byte> data, int frameIndex,
                                             NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
        {
            Interlocked.Increment(ref DecodeFrameCalls);
            // Distinct, deterministic per-frame delay so callers can assert metadata flows through.
            return new DecodedImageFrame(MakeImage(requestedFormat), delayMs: 40 * (frameIndex + 1));
        }
    }

    private static readonly byte[] SampleBytes = { 0x47, 0x49, 0x46, 0x38 }; // arbitrary; fake ignores content

    [Fact]
    public void FromBytes_single_frame_returns_static_BitmapImage()
    {
        BitmapImage.SetDecoder(new FrameFakeDecoder { FrameCount = 1, Width = 8, Height = 6 });

        var source = ImageSourceLoader.FromBytes(SampleBytes);

        var bitmap = Assert.IsType<BitmapImage>(source);
        Assert.Equal(8, bitmap.PixelWidth);
        Assert.Equal(6, bitmap.PixelHeight);
    }

    [Fact]
    public void FromBytes_multi_frame_returns_AnimatedBitmap_with_all_frames()
    {
        BitmapImage.SetDecoder(new FrameFakeDecoder { FrameCount = 3, Width = 5, Height = 5 });

        var source = ImageSourceLoader.FromBytes(SampleBytes);

        var animated = Assert.IsType<AnimatedBitmap>(source);
        Assert.Equal(3, animated.FrameCount);
        Assert.Equal(3, animated.Frames.Count);
        // Per-frame delays from the decoder must be preserved.
        Assert.Equal(new[] { 40, 80, 120 }, animated.FrameDelays);
        animated.Dispose();
    }

    [Fact]
    public void FromBytes_does_not_double_decode_static_images()
    {
        var fake = new FrameFakeDecoder { FrameCount = 1 };
        BitmapImage.SetDecoder(fake);

        _ = ImageSourceLoader.FromBytes(SampleBytes);

        // Single-frame payload: probe is metadata-only, so exactly one full pixel decode.
        Assert.Equal(1, fake.DecodeCalls);
        Assert.Equal(0, fake.DecodeFrameCalls);
    }

    /// <summary>
    /// A URI-backed multi-frame payload animates, and resolving it never blocks the caller.
    /// </summary>
    /// <remarks>
    /// <para><see cref="ImageSourceLoader.FromUri"/> is on the synchronous XAML type-converter path
    /// that <c>&lt;Image Source="cat.gif"/&gt;</c> takes, so it cannot read the encoded bytes to
    /// answer "is this animated?" — that is unbounded file or network I/O on the reader's thread,
    /// once per image. It used to try anyway, from <c>BitmapImage.ImageData</c>, which
    /// <c>ConfigureDeferredSource</c> has just set to null for every URI-backed source: the probe
    /// therefore never ran and every animated GIF/APNG/WebP reached through markup rendered one
    /// static frame, forever, on every machine.</para>
    /// <para>The frame count is resolved on a decode worker instead and, when it is greater than
    /// one, published as an internal <c>AnimatedSubstitute</c> that every consumer resolves through
    /// — so the application's own <c>Source</c> is never written to by the framework.</para>
    /// <para>The elapsed-time bound is expressed against the decoder's own blocking delay rather
    /// than as a flat few milliseconds: the assertion that matters is "did not wait for the
    /// decode", and a machine under load can spend longer than a fixed 5ms budget on the
    /// constructor alone.</para>
    /// </remarks>
    [Fact]
    public void DeferredMultiFrameSourceAnimatesWithoutBlockingTheConverterPath()
    {
        const int decodeDelayMs = 400;
        var decoder = new FrameFakeDecoder
        {
            FrameCount = 3,
            Width = 8,
            Height = 8,
            DecodeDelayMs = decodeDelayMs,
        };
        BitmapImage.SetDecoder(decoder);

        using var file = new TempImageFile();

        // Warm the constructor's one-time costs (static ctors, first-call JIT) so the measurement
        // below is of the loader's own work rather than of the runtime's.
        ((BitmapImage)ImageSourceLoader.FromUri(file.Uri)).Dispose();

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var source = ImageSourceLoader.FromUri(file.Uri);
        elapsed.Stop();

        using var bitmap = Assert.IsType<BitmapImage>(source);
        Assert.True(
            elapsed.Elapsed < TimeSpan.FromMilliseconds(decodeDelayMs / 2),
            $"FromUri took {elapsed.Elapsed.TotalMilliseconds:F1}ms against a {decodeDelayMs}ms " +
            "decoder — the type-converter path is blocking on I/O or on the decode");

        // Nothing has asked for pixels yet, so nothing has read the payload.
        Assert.True(bitmap.IsDeferredDecodePending);
        Assert.Null(bitmap.AnimatedSubstitute);

        // A renderer draws it. That is what reads the bytes, and the same read resolves the frame
        // count — the substitute must appear without the application touching Source.
        bitmap.RequestDecode(8, 8, cover: false);

        Assert.True(
            ImagePipelineTestHarness.SpinUntil(
                () => bitmap.AnimatedSubstitute is not null,
                ImagePipelineTestHarness.DecodeTimeout),
            "a 3-frame URI-backed payload never produced an animated substitute");

        var substitute = bitmap.AnimatedSubstitute;
        Assert.NotNull(substitute);
        Assert.Equal(3, substitute!.FrameCount);
        Assert.Equal(new[] { 40, 80, 120 }, substitute.FrameDelays);

        var firstGeneration = bitmap.ContentGeneration;
        substitute.Pause();
        substitute.CurrentFrameIndex = 1;
        Assert.True(bitmap.ContentGeneration > firstGeneration,
            "the outer BitmapImage cache stamp did not follow its animated substitute");

        // The framework never writes to the application's source: the instance the converter
        // handed back is still a BitmapImage, and it still reports its own pixel geometry.
        Assert.Equal(8, bitmap.PixelWidth);
    }
}
