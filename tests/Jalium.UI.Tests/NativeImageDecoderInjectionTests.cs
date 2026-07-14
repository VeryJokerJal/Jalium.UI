using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// 验证 <see cref="BitmapImage"/> 的 <see cref="INativeImageDecoder"/> 注入路径不依赖原生
/// jalium.native.media DLL — 测试时可用 mock 替代真实解码。
/// </summary>
public class NativeImageDecoderInjectionTests
{
    private sealed class FakeDecoder : INativeImageDecoder
    {
        public int DecodeCalls;
        public int Width = 4;
        public int Height = 3;

        public DecodedImage Decode(ReadOnlySpan<byte> data, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
        {
            DecodeCalls++;
            var stride = Width * 4;
            var buffer = new byte[stride * Height];
            for (var i = 0; i < buffer.Length; i += 4)
            {
                buffer[i + 0] = 0x10; // B
                buffer[i + 1] = 0x20; // G
                buffer[i + 2] = 0x30; // R
                buffer[i + 3] = 0xFF; // A
            }
            return new DecodedImage(buffer, Width, Height, stride, requestedFormat);
        }

        public DecodedImage Decode(Stream stream, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return Decode(ms.ToArray(), requestedFormat);
        }

        public DecodedImage DecodeFile(string filePath, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
            => Decode(File.ReadAllBytes(filePath), requestedFormat);

        public bool TryReadDimensions(ReadOnlySpan<byte> data, out int width, out int height)
        {
            width = Width;
            height = Height;
            return true;
        }
    }

    [Fact]
    public void FromBytes_invokes_injected_decoder()
    {
        var fake = new FakeDecoder { Width = 8, Height = 6 };
        BitmapImage.SetDecoder(fake);

        // Provide arbitrary bytes — the fake ignores them and returns deterministic pixels.
        var image = BitmapImage.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        Assert.Equal(1, fake.DecodeCalls);
        Assert.Equal(8, image.PixelWidth);
        Assert.Equal(6, image.PixelHeight);
        Assert.Equal(8 * 4, image.PixelStride);
        Assert.NotNull(image.RawPixelData);
        Assert.Equal(0x10, image.RawPixelData![0]); // B
        Assert.Equal(0x20, image.RawPixelData![1]); // G
        Assert.Equal(0x30, image.RawPixelData![2]); // R
        Assert.Equal(0xFF, image.RawPixelData![3]); // A
    }

    [Fact]
    public void FromPixels_does_not_invoke_decoder()
    {
        var fake = new FakeDecoder();
        BitmapImage.SetDecoder(fake);

        var pixels = new byte[16 * 16 * 4];
        var image = BitmapImage.FromPixels(pixels, 16, 16);

        Assert.Equal(0, fake.DecodeCalls);
        Assert.Equal(16, image.PixelWidth);
        Assert.Equal(16, image.PixelHeight);
    }

    [Fact]
    public void FromMediaFrame_copies_pixels_independent_of_decoder()
    {
        var fake = new FakeDecoder();
        BitmapImage.SetDecoder(fake);

        using var frame = DefaultMediaFramePool.Shared.Rent(4, 4, 16, TimeSpan.FromMilliseconds(33));
        frame.Pixels.Span.Fill(0xAB);

        var image = BitmapImage.FromMediaFrame(frame);

        Assert.Equal(0, fake.DecodeCalls);
        Assert.Equal(4, image.PixelWidth);
        Assert.Equal(4, image.PixelHeight);
        Assert.NotNull(image.RawPixelData);
        Assert.All(image.RawPixelData!, b => Assert.Equal(0xAB, b));

        // Frame can be Disposed (returned to pool) and the BitmapImage still owns its copy.
        frame.Dispose();
        Assert.All(image.RawPixelData!, b => Assert.Equal(0xAB, b));
    }
}
