using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Jalium.UI;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Native;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "--playback")
            return PlaybackSmoke.Run(args[1], args[2], args.Length > 3 ? args[3] : "d3d12");
        if (args.Length is < 2 or > 3)
        {
            Console.Error.WriteLine("Usage: Jalium.UI.Media.WindowsSmoke <local-video> <output-directory> [--allow-cpu-fallback]");
            return 2;
        }
        try
        {
            string source = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(output);
            using var context = new RenderContext(RenderBackend.D3D12);
            using var decoder = new NativeVideoDecoder();
            decoder.Open(new Uri(source));
            Console.WriteLine($"Decoder: {decoder.Width}x{decoder.Height}, {decoder.Fps:F2}fps, {decoder.ActiveVideoCodec}; renderer {context.Backend}");
            var results = new List<object>();
            var failures = new List<string>();
            var allowFallback = args.Length == 3 && args[2] == "--allow-cpu-fallback";
            var initialStats = VideoSurfaceStats.Query();
            NativeVideoSurface? retained = null;
            byte[]? retainedPixels = null;
            var imageHashes = new HashSet<string>();
            for (int index = 0; index < 96; index++)
            {
                if (!decoder.TryReadFrame(out var frame) || frame is null)
                    throw new InvalidOperationException($"Unexpected end of video at frame {index}.");
                using (frame)
                {
                    var decoded = frame.Pixels.ToArray();
                    var decodedStats = Describe(decoded);
                    bool capture = index is 0 or 1 or 10 or 29 or 60 or 95;
                    if (capture) File.WriteAllBytes(Path.Combine(output, $"decoded-{index}.bgra"), decoded);
                    using var staged = NativeVideoSurface.CreateBgra8(context.Handle, frame.Width, frame.Height);
                    using (var mapping = staged.Lock())
                    {
                        for (int row = 0; row < frame.Height; row++)
                            frame.Pixels.Span.Slice(row * frame.Stride, frame.Width * 4)
                                .CopyTo(mapping.Pixels.Slice(row * mapping.Stride, frame.Width * 4));
                    }
                    var stagedPixels = Render(context, staged);
                    if (capture) File.WriteAllBytes(Path.Combine(output, $"staged-{index}.bgra"), stagedPixels);
                    using var imported = decoder.AcquireGpuSurface(context.Handle);
                    byte[]? importedPixels = imported is null ? null : Render(context, imported);
                    if (capture && importedPixels is not null)
                        File.WriteAllBytes(Path.Combine(output, $"imported-{index}.bgra"), importedPixels);
                    if (!decoded.AsSpan().SequenceEqual(stagedPixels)) failures.Add($"CPU staging pixel mismatch at frame {index}.");
                    if (importedPixels is null && !allowFallback) failures.Add($"GPU import unavailable at frame {index}.");
                    if (importedPixels is not null && !decoded.AsSpan().SequenceEqual(importedPixels))
                        failures.Add($"GPU pixels differ from decoded RGB at frame {index}.");
                    imageHashes.Add(Convert.ToHexString(SHA256.HashData(importedPixels ?? stagedPixels)));
                    if (index == 0 && imported is not null)
                    {
                        retained = decoder.AcquireGpuSurface(context.Handle);
                        retainedPixels = importedPixels;
                    }
                    if (retained is not null && !Render(context, retained).AsSpan().SequenceEqual(retainedPixels))
                        failures.Add($"A retained frame was overwritten while decoding frame {index}.");
                    var result = new
                    {
                        index, frame.Width, frame.Height, frame.Stride,
                        pts = frame.PresentationTime.TotalMilliseconds,
                        decoded = decodedStats,
                        staged = Describe(stagedPixels),
                        imported = importedPixels is null ? null : Describe(importedPixels),
                        surfaceKind = imported?.Kind.ToString(),
                    };
                    if (capture)
                    {
                        results.Add(result);
                        Console.WriteLine(JsonSerializer.Serialize(result));
                    }
                }
            }
            decoder.Seek(TimeSpan.FromSeconds(1));
            if (!decoder.TryReadFrame(out var sought) || sought is null) failures.Add("Seek could not decode a new frame.");
            sought?.Dispose();
            decoder.Dispose();
            if (retained is not null && !Render(context, retained).AsSpan().SequenceEqual(retainedPixels))
                failures.Add("Retained GPU pixels changed after seek/decoder disposal and render-target recreation.");
            retained?.Dispose();
            if (imageHashes.Count < 10) failures.Add("The motion fixture did not produce distinct rendered frames.");
            var finalStats = VideoSurfaceStats.Query();
            File.WriteAllText(Path.Combine(output, "probe.json"), JsonSerializer.Serialize(new
            {
                runtime = typeof(Jalium.UI.Controls.MediaElement).Assembly.Location,
                frames = results, distinctFrames = imageHashes.Count, checkedFrames = 96,
                initialStats, finalStats, failures, passed = failures.Count == 0,
            }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
            Console.WriteLine($"Pixel validation: {(failures.Count == 0 ? "PASS" : "FAIL")}; {imageHashes.Count} distinct frames, {failures.Count} errors.");
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static byte[] Render(RenderContext context, NativeVideoSurface surface)
    {
        using var window = new NativeWindow(surface.PixelWidth, surface.PixelHeight);
        using var target = context.CreateRenderTarget(window.Handle, surface.PixelWidth, surface.PixelHeight);
        using var image = new D3DImage();
        image.SetBackBuffer(surface);
        if (!target.TryBeginDraw()) throw new InvalidOperationException("BeginDraw failed.");
        target.Clear(0, 0, 0, 1);
        using (var drawing = new RenderTargetDrawingContext(target, context))
            drawing.DrawImage(image, new Rect(0, 0, surface.PixelWidth, surface.PixelHeight), BitmapScalingMode.Linear);
        if (target.RequestReadback() != JaliumResult.Ok || target.TryEndDraw() != JaliumResult.Ok)
            throw new InvalidOperationException("Render/readback submission failed.");
        var pixels = new byte[checked(surface.PixelWidth * surface.PixelHeight * 4)];
        if (target.FetchReadback(pixels, (uint)surface.PixelWidth * 4, out _, out _) != JaliumResult.Ok)
            throw new InvalidOperationException("Pixel readback failed.");
        return pixels;
    }

    private static object Describe(byte[] pixels)
    {
        long sum = 0, nonblack = 0, opaque = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int rgb = pixels[i] + pixels[i + 1] + pixels[i + 2];
            sum += rgb;
            if (rgb > 30) nonblack++;
            if (pixels[i + 3] == 255) opaque++;
        }
        return new { mean = sum / (pixels.Length / 4.0 * 3), nonblack, opaque, hash = Convert.ToHexString(SHA256.HashData(pixels)) };
    }

    private sealed partial class NativeWindow : IDisposable
    {
        public nint Handle { get; }
        public NativeWindow(int width, int height)
        {
            Handle = CreateWindowExW(0, "Static", "Video pixel probe", 0x80000000,
                0, 0, width, height, 0, 0, 0, 0);
            if (Handle == 0) throw new InvalidOperationException($"CreateWindowEx: {Marshal.GetLastPInvokeError()}");
        }
        public void Dispose() => DestroyWindow(Handle);
        [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        private static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DestroyWindow(nint hwnd);
    }
}
