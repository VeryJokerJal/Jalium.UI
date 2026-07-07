using System.Runtime.InteropServices;
using Jalium.UI.Interop;

namespace Jalium.UI.ParityHarness;

/// <summary>
/// A real (non-message-only) top-level Win32 window that is never shown, for
/// backing a swapchain surface. Message-only windows (HWND_MESSAGE parent)
/// cannot host a swapchain, so this creates an invisible WS_POPUP window of
/// the requested client size using the system "Static" window class (no
/// RegisterClass / WndProc needed). Mirrors the helper validated by
/// tests/Jalium.UI.Tests/VulkanBackendSmokeTests.cs.
/// </summary>
internal sealed partial class HiddenNativeWindow : IDisposable
{
    private const uint WS_POPUP = 0x8000_0000;

    private nint _hwnd;

    public HiddenNativeWindow(int width, int height)
    {
        _hwnd = CreateWindowExW(
            0, "Static", null, WS_POPUP,
            0, 0, width, height,
            nint.Zero, nint.Zero, nint.Zero, nint.Zero);
        if (_hwnd == nint.Zero)
        {
            throw new InvalidOperationException(
                $"CreateWindowExW failed (Win32 error {Marshal.GetLastPInvokeError()}).");
        }
    }

    public nint Hwnd => _hwnd;

    public void Dispose()
    {
        var hwnd = _hwnd;
        _hwnd = nint.Zero;
        if (hwnd != nint.Zero)
        {
            _ = DestroyWindow(hwnd);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(
        uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);
}

/// <summary>
/// Minimal 32bpp BMP writer — 14-byte BITMAPFILEHEADER + 40-byte
/// BITMAPINFOHEADER (the classic "54-byte header") + raw BGRA rows. Written
/// with a NEGATIVE biHeight so rows are stored top-down, matching both the
/// readback buffer layout and the diff tool's row order; 32bpp rows need no
/// padding. No external imaging dependency.
/// </summary>
internal static class BmpWriter
{
    public static void WriteBgra32TopDown(string path, byte[] bgra, int width, int height, int stride)
    {
        int rowBytes = width * 4;
        int pixelBytes = rowBytes * height;
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // BITMAPFILEHEADER (14 bytes)
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(54 + pixelBytes);   // bfSize
        bw.Write(0);                 // bfReserved1/2
        bw.Write(54);                // bfOffBits

        // BITMAPINFOHEADER (40 bytes)
        bw.Write(40);                // biSize
        bw.Write(width);             // biWidth
        bw.Write(-height);           // biHeight — negative = top-down rows
        bw.Write((short)1);          // biPlanes
        bw.Write((short)32);         // biBitCount
        bw.Write(0);                 // biCompression = BI_RGB
        bw.Write(pixelBytes);        // biSizeImage
        bw.Write(2835);              // biXPelsPerMeter (~72 DPI)
        bw.Write(2835);              // biYPelsPerMeter
        bw.Write(0);                 // biClrUsed
        bw.Write(0);                 // biClrImportant

        for (int y = 0; y < height; y++)
        {
            bw.Write(bgra, y * stride, rowBytes);
        }
    }
}

/// <summary>
/// Per-scene drawing context: exposes the render target plus create-helpers
/// whose products are tracked and released AFTER the frame is submitted
/// (backends may defer-reference brushes until their EndFrame flush).
/// </summary>
internal sealed class SceneContext
{
    private readonly RenderContext _context;
    private readonly List<IDisposable> _resources = new();

    public SceneContext(RenderContext context, RenderTarget target)
    {
        _context = context;
        Target = target;
    }

    public RenderTarget Target { get; }

    public NativeBrush Solid(float r, float g, float b, float a = 1f)
        => Track(_context.CreateSolidBrush(r, g, b, a));

    /// <param name="stops">Flat (position, r, g, b, a) tuples — 5 floats per stop.</param>
    public NativeBrush Linear(float x0, float y0, float x1, float y1, float[] stops)
        => Track(_context.CreateLinearGradientBrush(x0, y0, x1, y1, stops, (uint)(stops.Length / 5)));

    /// <param name="stops">Flat (position, r, g, b, a) tuples — 5 floats per stop.</param>
    public NativeBrush Radial(float cx, float cy, float rx, float ry, float ox, float oy, float[] stops)
        => Track(_context.CreateRadialGradientBrush(cx, cy, rx, ry, ox, oy, stops, (uint)(stops.Length / 5)));

    public NativeTextFormat Text(string family, float size, int weight = 400, int style = 0)
        => Track(_context.CreateTextFormat(family, size, weight, style));

    public NativeBitmap BitmapFromPixels(byte[] bgra, int width, int height)
        => Track(_context.CreateBitmapFromPixels(bgra, width, height));

    public NativeBitmap BitmapFromEncoded(byte[] encoded)
        => Track(_context.CreateBitmap(encoded));

    private T Track<T>(T resource) where T : IDisposable
    {
        _resources.Add(resource);
        return resource;
    }

    public void DisposeResources()
    {
        // Reverse order, defensive against duplicate registration.
        for (int i = _resources.Count - 1; i >= 0; i--)
        {
            try { _resources[i].Dispose(); } catch { /* teardown must not mask the scene error */ }
        }
        _resources.Clear();
    }
}
