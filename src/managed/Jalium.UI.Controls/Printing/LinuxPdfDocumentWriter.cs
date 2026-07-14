using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Jalium.UI.Controls.Printing;

/// <summary>
/// A raster page ready to be embedded in a portal-print PDF. Pixel data is
/// top-down, tightly packed DeviceRGB.
/// </summary>
internal sealed record LinuxPdfRasterPage(
    int PixelWidth,
    int PixelHeight,
    double WidthPoints,
    double HeightPoints,
    byte[] RgbPixels);

/// <summary>
/// Minimal dependency-free PDF writer used by the Linux xdg Print portal.
/// Pages are deliberately raster based so every Jalium visual that can be
/// rendered by RenderTargetBitmap has identical print semantics.
/// </summary>
internal static class LinuxPdfDocumentWriter
{
    private static readonly Encoding Ascii = Encoding.ASCII;

    internal static void Write(Stream destination, IReadOnlyList<LinuxPdfRasterPage> pages)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(pages);
        if (!destination.CanWrite || !destination.CanSeek)
            throw new ArgumentException("The PDF destination must be writable and seekable.", nameof(destination));
        if (pages.Count == 0)
            throw new ArgumentException("At least one page is required.", nameof(pages));

        foreach (var page in pages)
            ValidatePage(page);

        destination.SetLength(0);
        destination.Position = 0;
        WriteAscii(destination, "%PDF-1.4\n");
        destination.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);

        var objectCount = 2 + pages.Count * 3;
        var offsets = new long[objectCount + 1];

        WriteObject(destination, offsets, 1,
            "<< /Type /Catalog /Pages 2 0 R >>");

        var pageReferences = string.Join(' ', Enumerable.Range(0, pages.Count)
            .Select(static index => $"{3 + index * 3} 0 R"));
        WriteObject(destination, offsets, 2,
            $"<< /Type /Pages /Count {pages.Count} /Kids [{pageReferences}] >>");

        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            var pageObject = 3 + index * 3;
            var contentObject = pageObject + 1;
            var imageObject = pageObject + 2;
            var imageName = $"Im{index + 1}";
            var width = FormatNumber(page.WidthPoints);
            var height = FormatNumber(page.HeightPoints);

            WriteObject(destination, offsets, pageObject,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] " +
                $"/Resources << /XObject << /{imageName} {imageObject} 0 R >> >> " +
                $"/Contents {contentObject} 0 R >>");

            var content = Ascii.GetBytes(
                $"q\n{width} 0 0 {height} 0 0 cm\n/{imageName} Do\nQ\n");
            WriteStreamObject(destination, offsets, contentObject, string.Empty, content);

            var compressedPixels = Compress(page.RgbPixels);
            var dictionary =
                $"/Type /XObject /Subtype /Image /Width {page.PixelWidth} /Height {page.PixelHeight} " +
                "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode";
            WriteStreamObject(destination, offsets, imageObject, dictionary, compressedPixels);
        }

        var xrefOffset = destination.Position;
        WriteAscii(destination, $"xref\n0 {objectCount + 1}\n");
        WriteAscii(destination, "0000000000 65535 f \n");
        for (var objectNumber = 1; objectNumber <= objectCount; objectNumber++)
        {
            WriteAscii(destination,
                offsets[objectNumber].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        WriteAscii(destination,
            $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\n" +
            $"startxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
        destination.Flush();
    }

    internal static byte[] CompositeBgraOnWhite(ReadOnlySpan<byte> bgraPixels)
    {
        if (bgraPixels.Length % 4 != 0)
            throw new ArgumentException("BGRA data must contain complete four-byte pixels.", nameof(bgraPixels));

        var rgb = new byte[checked(bgraPixels.Length / 4 * 3)];
        for (int source = 0, target = 0; source < bgraPixels.Length; source += 4, target += 3)
        {
            var alpha = bgraPixels[source + 3];
            rgb[target] = CompositeChannel(bgraPixels[source + 2], alpha);
            rgb[target + 1] = CompositeChannel(bgraPixels[source + 1], alpha);
            rgb[target + 2] = CompositeChannel(bgraPixels[source], alpha);
        }

        return rgb;
    }

    private static byte CompositeChannel(byte color, byte alpha) =>
        (byte)((color * alpha + 255 * (255 - alpha) + 127) / 255);

    private static void ValidatePage(LinuxPdfRasterPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.PixelWidth <= 0 || page.PixelHeight <= 0 ||
            !double.IsFinite(page.WidthPoints) || page.WidthPoints <= 0 ||
            !double.IsFinite(page.HeightPoints) || page.HeightPoints <= 0)
        {
            throw new ArgumentException("PDF page dimensions must be finite and positive.", nameof(page));
        }

        var expectedLength = checked(page.PixelWidth * page.PixelHeight * 3);
        if (page.RgbPixels == null || page.RgbPixels.Length != expectedLength)
            throw new ArgumentException("PDF page RGB data has an invalid length.", nameof(page));
    }

    private static byte[] Compress(ReadOnlySpan<byte> bytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(bytes);
        return output.ToArray();
    }

    private static void WriteObject(
        Stream destination,
        long[] offsets,
        int objectNumber,
        string body)
    {
        offsets[objectNumber] = destination.Position;
        WriteAscii(destination, $"{objectNumber} 0 obj\n{body}\nendobj\n");
    }

    private static void WriteStreamObject(
        Stream destination,
        long[] offsets,
        int objectNumber,
        string dictionaryEntries,
        byte[] data)
    {
        offsets[objectNumber] = destination.Position;
        var separator = string.IsNullOrWhiteSpace(dictionaryEntries) ? string.Empty : dictionaryEntries + " ";
        WriteAscii(destination,
            $"{objectNumber} 0 obj\n<< {separator}/Length {data.Length} >>\nstream\n");
        destination.Write(data);
        WriteAscii(destination, "\nendstream\nendobj\n");
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void WriteAscii(Stream destination, string value) =>
        destination.Write(Ascii.GetBytes(value));
}
