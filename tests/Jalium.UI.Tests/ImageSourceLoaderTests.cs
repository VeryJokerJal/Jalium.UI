using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using BitmapImage = Jalium.UI.Media.BitmapImage;
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
public class ImageSourceLoaderTests
{
    private sealed class FrameFakeDecoder : INativeImageDecoder
    {
        public int FrameCount = 1;
        public int DecodeCalls;
        public int DecodeFrameCalls;
        public int Width = 4;
        public int Height = 4;

        private DecodedImage MakeImage(NativePixelFormat format)
        {
            var stride = Width * 4;
            return new DecodedImage(new byte[stride * Height], Width, Height, stride, format);
        }

        public DecodedImage Decode(ReadOnlySpan<byte> data, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
        {
            DecodeCalls++;
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
            DecodeFrameCalls++;
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
}
