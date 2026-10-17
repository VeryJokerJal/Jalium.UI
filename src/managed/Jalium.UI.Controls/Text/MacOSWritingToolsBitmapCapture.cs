using System.Runtime.InteropServices;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using WriteableBitmap = Jalium.UI.Media.Imaging.WriteableBitmap;

namespace Jalium.UI;

/// <summary>Renders through the real native backend into a windowless view.</summary>
internal static partial class MacOSWritingToolsBitmapCapture
{
    internal static WriteableBitmap? Capture(int width, int height, double dpi, Action<DrawingContext> draw,
        Func<bool> current, RenderContext? context = null)
    {
        if (!OperatingSystem.IsMacOS() || IsMainThread() == 0 || width <= 0 || height <= 0 || width > 16384 || height > 16384 ||
            (long)width * height > 16 * 1024 * 1024 || !double.IsFinite(dpi) || dpi <= 0 || !current()) return null;
        context ??= RenderContext.Current;
        if (context is null) return null;
        nint view = Send(Send(Class("NSView"), Selector("alloc")), Selector("init"));
        if (view == 0) return null;
        RenderTarget? target = null;
        RenderTargetDrawingContext? drawing = null;
        try
        {
            target = context.CreateRenderTargetForComposition(NativeSurfaceDescriptor.ForMacOSView(view, composition: true), width, height);
            target.SetDpi((float)(96 * dpi), (float)(96 * dpi));
            if (target.RequestReadback() != JaliumResult.Ok) return null;
            drawing = new RenderTargetDrawingContext(target, context);
            target.BeginDraw();
            try { target.Clear(0, 0, 0, 0); draw(drawing); }
            finally { drawing.Close(); target.EndDraw(); }
            if (!current()) return null;
            int stride = checked(width * 4);
            byte[] pixels = new byte[checked(stride * height)];
            if (target.FetchReadback(pixels, (uint)stride, out int capturedWidth, out int capturedHeight) != JaliumResult.Ok ||
                capturedWidth != width || capturedHeight != height || !current()) return null;
            // Metal composition surfaces contain premultiplied BGRA; Software
            // already supplies straight BGRA. Encoders/NSTextPreview use straight
            // alpha, so convert exactly once at this boundary.
            if (context.Backend != RenderBackend.Software)
            for (int index = 0; index < pixels.Length; index += 4)
            {
                int alpha = pixels[index + 3];
                if (alpha is > 0 and < 255)
                    for (int channel = 0; channel < 3; ++channel)
                        pixels[index + channel] = (byte)Math.Min(255, (pixels[index + channel] * 255 + alpha / 2) / alpha);
            }
            var bitmap = new WriteableBitmap(width, height, 96 * dpi, 96 * dpi, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            return bitmap;
        }
        finally
        {
            // Close unsubscribes image events; ClearCache releases backend-bound
            // font/brush/bitmap pins before retiring the private render target.
            drawing?.Close();
            drawing?.ClearCache();
            target?.Dispose();
            Send(view, Selector("release"));
        }
    }

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Class(string name);
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint target, nint selector);

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "pthread_main_np")]
    private static partial int IsMainThread();
}

