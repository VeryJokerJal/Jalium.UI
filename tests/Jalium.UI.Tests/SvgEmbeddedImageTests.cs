using System;
using System.Collections.Generic;
using System.IO;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using BitmapImage = Jalium.UI.Media.BitmapImage;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers SVG <c>&lt;image&gt;</c> elements that embed a raster image inline as a
/// base64 <c>data:</c> URI — both the parse side (<see cref="SvgImage"/> →
/// <see cref="ImageDrawing"/>) and the render side (<see cref="SoftwareVectorRasterizer"/>
/// actually blitting the decoded pixels into the SVG raster buffer).
/// </summary>
public class SvgEmbeddedImageTests
{
    /// <summary>
    /// A deterministic decoder that ignores the encoded payload and returns a solid
    /// color of a fixed size, so parse-side tests don't depend on the native codec.
    /// </summary>
    private sealed class SolidDecoder : INativeImageDecoder
    {
        public int Width = 4;
        public int Height = 4;
        public byte B = 0x10, G = 0x20, R = 0x30, A = 0xFF;

        public DecodedImage Decode(ReadOnlySpan<byte> data, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
        {
            var stride = Width * 4;
            var buffer = new byte[stride * Height];
            for (var i = 0; i < buffer.Length; i += 4)
            {
                buffer[i + 0] = B;
                buffer[i + 1] = G;
                buffer[i + 2] = R;
                buffer[i + 3] = A;
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

    // A short but valid base64 payload. The fake decoder ignores the bytes; only
    // Convert.FromBase64String in the parser must accept it.
    private static string SampleBase64() =>
        Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    private static List<ImageDrawing> CollectImageDrawings(Drawing? drawing)
    {
        var result = new List<ImageDrawing>();
        Walk(drawing, result);
        return result;

        static void Walk(Drawing? node, List<ImageDrawing> sink)
        {
            switch (node)
            {
                case ImageDrawing img:
                    sink.Add(img);
                    break;
                case DrawingGroup group:
                    foreach (var child in group.Children)
                        Walk(child, sink);
                    break;
            }
        }
    }

    [Fact]
    public void Parse_image_with_base64_data_uri_produces_image_drawing()
    {
        BitmapImage.SetDecoder(new SolidDecoder { Width = 8, Height = 6 });

        var svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\">" +
            $"<image x=\"10\" y=\"20\" width=\"30\" height=\"40\" href=\"data:image/png;base64,{SampleBase64()}\"/>" +
            "</svg>";

        var image = SvgImage.FromSvgString(svg);
        var drawings = CollectImageDrawings(image.Drawing);

        var only = Assert.Single(drawings);
        Assert.Equal(10, only.Rect.X);
        Assert.Equal(20, only.Rect.Y);
        Assert.Equal(30, only.Rect.Width);
        Assert.Equal(40, only.Rect.Height);

        var bitmap = Assert.IsType<BitmapImage>(only.ImageSource);
        Assert.Equal(8, bitmap.PixelWidth);
        Assert.Equal(6, bitmap.PixelHeight);
        Assert.NotNull(bitmap.RawPixelData);
    }

    [Fact]
    public void Parse_image_with_xlink_href_is_supported()
    {
        BitmapImage.SetDecoder(new SolidDecoder());

        var svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"50\" height=\"50\">" +
            $"<image width=\"50\" height=\"50\" xlink:href=\"data:image/png;base64,{SampleBase64()}\"/>" +
            "</svg>";

        var image = SvgImage.FromSvgString(svg);
        var drawings = CollectImageDrawings(image.Drawing);

        Assert.Single(drawings);
    }

    [Fact]
    public void Parse_image_with_opacity_wraps_in_group()
    {
        BitmapImage.SetDecoder(new SolidDecoder());

        var svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\">" +
            $"<image width=\"20\" height=\"20\" opacity=\"0.5\" href=\"data:image/png;base64,{SampleBase64()}\"/>" +
            "</svg>";

        var image = SvgImage.FromSvgString(svg);
        var root = Assert.IsType<DrawingGroup>(image.Drawing);

        // The opacity attribute must produce a wrapping DrawingGroup around the image.
        DrawingGroup? wrapper = null;
        foreach (var child in root.Children)
        {
            if (child is DrawingGroup g)
            {
                foreach (var inner in g.Children)
                {
                    if (inner is ImageDrawing)
                    {
                        wrapper = g;
                        break;
                    }
                }
            }
        }

        Assert.NotNull(wrapper);
        Assert.Equal(0.5, wrapper!.Opacity, 3);
    }

    [Fact]
    public void Parse_image_with_external_href_is_skipped()
    {
        BitmapImage.SetDecoder(new SolidDecoder());

        var svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\">" +
            "<image width=\"10\" height=\"10\" href=\"https://example.com/picture.png\"/>" +
            "</svg>";

        // Must not throw, and must not fabricate an ImageDrawing for an
        // unsupported (non-embedded) reference.
        var image = SvgImage.FromSvgString(svg);
        Assert.Empty(CollectImageDrawings(image.Drawing));
    }

    [Fact]
    public void Rasterizer_blits_image_drawing_into_pixels()
    {
        // Pure-red 2×2 BGRA source (B=0, G=0, R=255, A=255).
        var red = new byte[2 * 2 * 4];
        for (var i = 0; i < red.Length; i += 4)
        {
            red[i + 2] = 255; // R
            red[i + 3] = 255; // A
        }
        var bitmap = BitmapImage.FromPixels(red, 2, 2);

        var group = new DrawingGroup();
        group.Children.Add(new ImageDrawing(bitmap, new Rect(0, 0, 10, 10)));

        var pixels = SoftwareVectorRasterizer.Rasterize(group, 10, 10, new Rect(0, 0, 10, 10));
        Assert.NotNull(pixels);

        // Center pixel (5,5) must be the source red — before ImageDrawing support
        // the rasterizer ignored it and this stayed fully transparent (all zero).
        var offset = (5 * 10 + 5) * 4;
        Assert.Equal(0, pixels![offset + 0]);     // B
        Assert.Equal(0, pixels[offset + 1]);      // G
        Assert.Equal(255, pixels[offset + 2]);    // R
        Assert.Equal(255, pixels[offset + 3]);    // A
    }

    [Fact]
    public void Rasterizer_honors_row_padding_in_source_stride()
    {
        // 2×2 source with 4 bytes of row padding (stride = 2*4 + 4 = 12).
        // Row 0 is red, row 1 is green, so a row-offset (stride) mistake is visible.
        const int w = 2, h = 2, stride = w * 4 + 4;
        var src = new byte[stride * h];
        for (var row = 0; row < h; row++)
        {
            for (var col = 0; col < w; col++)
            {
                var off = row * stride + col * 4;
                if (row == 0) src[off + 2] = 255; // R
                else src[off + 1] = 255;          // G
                src[off + 3] = 255;               // A
            }
        }

        var bitmap = BitmapImage.FromPixels(src, w, h, stride);
        Assert.Equal(stride, bitmap.PixelStride);

        var group = new DrawingGroup();
        group.Children.Add(new ImageDrawing(bitmap, new Rect(0, 0, 10, 10)));

        var pixels = SoftwareVectorRasterizer.Rasterize(group, 10, 10, new Rect(0, 0, 10, 10));
        Assert.NotNull(pixels);

        // Pixel (2,7): u=0.25→sx=0, v=0.75→sy=1 — maps to the green second row.
        // A stride bug would instead read row-0 padding here and leave it transparent.
        var off2 = (7 * 10 + 2) * 4;
        Assert.Equal(0, pixels![off2 + 2]);   // R = 0
        Assert.Equal(255, pixels[off2 + 1]);  // G = 255
        Assert.Equal(255, pixels[off2 + 3]);  // A = 255
    }
}
