using System.Buffers.Binary;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Styling;

/// <summary>First-frame, origin-approved alpha mixing for CSS shape exclusions.</summary>
internal static class CssCrossFadeShape
{
    internal static CssShapeImageValue Create((CssShapeImageValue Image, double Weight)[] parts)
    {
        Size previousSize = default; CssShapeImagePixels?[]? previousParts = null; CssShapeImagePixels? previous = null;
        CssShapeImagePixels? Resolve(Size size)
        {
            if (!(size.Width > 0 && size.Height > 0 && double.IsFinite(size.Width) && double.IsFinite(size.Height))) return null;
            var pixels = parts.Select(part => part.Image.ResolvePixels(size)).ToArray();
            if (previous is not null && previousSize == size && previousParts is not null && previousParts.AsSpan().SequenceEqual(pixels)) return previous;
            var scale = Math.Min(1, 2048 / Math.Max(size.Width, size.Height));
            var width = Math.Max(1, (int)Math.Ceiling(size.Width * scale)); var height = Math.Max(1, (int)Math.Ceiling(size.Height * scale));
            var bytes = new byte[checked(width * height * 16)];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                double alpha = 0;
                for (var index = 0; index < parts.Length; index++)
                    alpha += (pixels[index]?.Alpha(x, y, width, height) ?? 0) * parts[index].Weight;
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan((y * width + x) * 16 + 12), (float)Math.Clamp(alpha, 0, 1));
            }
            previousSize = size; previousParts = pixels;
            var intrinsic = CssImageIntrinsicSize.Mix(parts.Select((part, index) =>
                (pixels[index]?.Intrinsic ?? default, part.Weight)), size);
            return previous = new(new(bytes, width, height, width * 16, PixelFormats.Rgba128Float, null), size, intrinsic);
        }
        return new(null, null, Resolve, parts.SelectMany(part => part.Image.Resources).Distinct().ToArray());
    }
}
