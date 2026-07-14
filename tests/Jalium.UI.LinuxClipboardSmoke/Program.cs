using System.Text;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using BitmapSource = Jalium.UI.Media.Imaging.BitmapSource;
using ClipboardBackend = Jalium.UI.Controls.Platform.ClipboardPlatform;
using WpfClipboard = Jalium.UI.Clipboard;

namespace Jalium.UI.Tests;

internal static class Program
{
    private static int s_failures;

    public static int Main()
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("This smoke test must run on Linux.");
            return 2;
        }

        Check(NativeMethods.PlatformInit() == 0, "initialize Linux platform clipboard");
        if (s_failures != 0)
            return s_failures;

        try
        {
            VerifyAtomicWpfDataObject();
            VerifyPngImageRoundTrip();
            VerifyClear();
        }
        finally
        {
            NativeMethods.PlatformShutdown();
        }

        if (s_failures == 0)
            Console.WriteLine("Managed Linux multi-format clipboard smoke passed.");
        return s_failures == 0 ? 0 : 1;
    }

    private static void VerifyAtomicWpfDataObject()
    {
        const string text = "managed-clipboard-中文-🚀";
        const string html = "<p>managed <b>HTML</b></p>";
        const string rtf = "{\\rtf1\\ansi managed RTF}";
        const string customMime = "application/vnd.jalium.managed-smoke";
        byte[] custom = Enumerable.Range(0, 196 * 1024 + 23)
            .Select(index => (byte)((index * 17 + 3) & 0xff))
            .ToArray();
        string[] files = ["/tmp/jalium clipboard.txt", "/tmp/jalium-中文.txt"];

        var dataObject = new DataObject();
        dataObject.SetData(DataFormats.UnicodeText, text, autoConvert: false);
        dataObject.SetData(DataFormats.Html, html, autoConvert: false);
        dataObject.SetData(DataFormats.Rtf, rtf, autoConvert: false);
        dataObject.SetData(DataFormats.FileDrop, files, autoConvert: false);
        dataObject.SetData(customMime, custom, autoConvert: false);
        WpfClipboard.SetDataObject(dataObject, copy: true);

        string[] formats = ClipboardBackend.GetAvailableDataFormats();
        Check(formats.Contains(DataFormats.UnicodeText), "managed transaction advertises UnicodeText");
        Check(formats.Contains(DataFormats.Html), "managed transaction advertises HTML");
        Check(formats.Contains(DataFormats.Rtf), "managed transaction advertises RTF");
        Check(formats.Contains(DataFormats.FileDrop), "managed transaction advertises FileDrop");
        Check(formats.Contains(customMime), "managed transaction advertises custom MIME");

        Check(ReadUtf8(DataFormats.UnicodeText) == text,
            "managed UnicodeText maps to UTF-8 text/plain");
        Check(ReadUtf8(DataFormats.Html) == html,
            "managed HTML maps to text/html");
        Check(ReadUtf8(DataFormats.Rtf) == rtf,
            "managed RTF maps to text/rtf");
        Check(ClipboardBackend.GetBinaryData(customMime)?.SequenceEqual(custom) == true,
            "managed custom MIME survives native marshalling and large-payload ownership");
        Check(ClipboardBackend.GetFileDropList()?.SequenceEqual(files) == true,
            "managed FileDrop maps through percent-escaped text/uri-list");
    }

    private static void VerifyPngImageRoundTrip()
    {
        byte[] pixels =
        [
            0x30, 0x20, 0x10, 0xFF,
            0x60, 0x50, 0x40, 0x80,
        ];
        WpfClipboard.SetImage(new SmokeBitmapSource(2, 1, pixels));

        byte[]? encoded = ClipboardBackend.GetBinaryData(DataFormats.Bitmap);
        Check(encoded is { Length: > 8 } &&
              encoded.AsSpan(0, 8).SequenceEqual(
                  new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "managed BitmapSource publishes a standards-compliant image/png payload");

        var decoded = ClipboardBackend.GetImage();
        Check(decoded is { Width: 2, Height: 1, Stride: 8 } &&
              decoded.Value.Data.SequenceEqual(pixels),
            "image/png decodes back to top-down BGRA clipboard pixels");
    }

    private static void VerifyClear()
    {
        WpfClipboard.Clear();
        Check(ClipboardBackend.GetAvailableDataFormats().Length == 0,
            "WPF Clear releases ownership instead of publishing empty text");
    }

    private static string? ReadUtf8(string format)
    {
        byte[]? bytes = ClipboardBackend.GetBinaryData(format);
        return bytes == null ? null : Encoding.UTF8.GetString(bytes);
    }

    private static void Check(bool condition, string message)
    {
        if (condition)
            return;
        Console.Error.WriteLine($"FAILED: {message}");
        s_failures++;
    }

    private sealed class SmokeBitmapSource : BitmapSource
    {
        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _pixels;

        public SmokeBitmapSource(int width, int height, byte[] pixels)
        {
            _width = width;
            _height = height;
            _pixels = pixels;
        }

        public override double Width => _width;
        public override double Height => _height;
        public override nint NativeHandle => nint.Zero;
        public override PixelFormat Format => PixelFormat.Bgra32;

        public override void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
        {
            Int32Rect rect = sourceRect.IsEmpty
                ? new Int32Rect(0, 0, _width, _height)
                : sourceRect;
            int rowBytes = checked(rect.Width * 4);
            for (int y = 0; y < rect.Height; y++)
            {
                _pixels.AsSpan(((rect.Y + y) * _width + rect.X) * 4, rowBytes)
                    .CopyTo(pixels.AsSpan(offset + y * stride, rowBytes));
            }
        }
    }
}
