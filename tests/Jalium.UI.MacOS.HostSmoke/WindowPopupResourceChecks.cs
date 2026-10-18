using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Diagnostics;
using Jalium.UI.Hosting;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static class WindowPopupResourceChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static int Run(string argument)
    {
        const string prefix = "--window-popup-resource-case=";
        if (!int.TryParse(argument.AsSpan(prefix.Length), out int index) || (uint)index >= 12) return 2;
        bool custom = index % 2 == 1, software = index >= 6; int kind = index % 6 / 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_POPUP_RESOURCE_OUTPUT")!;
        Directory.CreateDirectory(output); var watch = Stopwatch.StartNew(); var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ResourceDelegate(); app.Delegate = host; app.FinishLaunching();
        using (var note = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(note);
        RenderContext.GetOrCreateCurrent(software ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
        Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var previousDecoder = (INativeImageDecoder)typeof(BitmapImage).GetMethod("ResolveDecoder", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        var decoder = new CountingDecoder(previousDecoder); BitmapImage.SetDecoder(decoder);
        using var bitmap = BitmapImage.FromBytes(File.ReadAllBytes(Environment.GetEnvironmentVariable("JALIUM_RESOURCE_PNG")!));
        var image = new Image { Source = bitmap, Width = 144, Height = 88, Stretch = Stretch.Fill };
        AutomationProperties.SetName(image, "四色示例图片");
        var probe = new ResourceBorder { Child = image, Width = 160, Height = 104, Padding = new Thickness(8) };
        var text = new TextBlock { Text = "主窗口更新后，图片应保持完整。", FontSize = 15, TextWrapping = TextWrapping.Wrap };
        var subjectContent = new StackPanel { Spacing = 12 };
        subjectContent.Children.Add(new TextBlock { Text = "图片保持显示", FontSize = 20 });
        subjectContent.Children.Add(probe); subjectContent.Children.Add(text);
        Brush Theme(string name, Color fallback) => Application.Current!.TryFindResource(name) as Brush ?? new SolidColorBrush(fallback);
        var subject = new Border { Width = 350, Height = 230, Padding = new Thickness(16), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), Background = Theme("LayerFillColorDefaultBrush", Color.FromRgb(44, 44, 46)),
            BorderBrush = Theme("ControlStrokeColorDefaultBrush", Color.FromRgb(72, 72, 74)), Child = subjectContent };
        var anchor = new Button { Content = "弹窗位置", Width = 140, Height = 44 };
        var editor = new TextBox { Text = "图片验证 · 中文🙂", Width = 350, Height = 40 };
        AutomationProperties.SetName(editor, "编辑验证");
        var root = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
        root.Children.Add(new TextBlock { Text = "窗口与弹窗图片", FontSize = 24 });
        root.Children.Add(editor); root.Children.Add(kind == 0 ? subject : anchor);
        var owner = new Window { Title = "Jalium Popup Image Lifetime", Width = kind == 0 ? 760 : 420, Height = 520,
            MinWidth = 420, MinHeight = 480, TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
            Background = Theme("SolidBackgroundFillColorBaseBrush", Color.FromRgb(28, 28, 30)), Content = root };
        Popup? popup = kind == 0 ? null : new Popup { Child = subject, PlacementTarget = anchor,
            Placement = PlacementMode.Bottom, ShouldConstrainToRootBounds = kind == 1,
            PreferExternalWindow = kind == 2, PopupAnimation = PopupAnimation.None, StaysOpen = true };
        Application.Current!.MainWindow = owner;
        var reclaimerType = typeof(Window).Assembly.GetType("Jalium.UI.Hosting.ResourceReclaimer", throwOnError: true)!;
        var options = new ResourceReclamationOptions { IdleTimeoutMs = 400, ScanFrameInterval = int.MaxValue,
            EvictDrawingCache = true, ReclaimBackendCaches = false };
        object reclaimer = Activator.CreateInstance(reclaimerType, [options])!;
        reclaimerType.GetMethod("Start")!.Invoke(reclaimer, null);
        int evictions = 0;
        Action<ImageSource> eviction = source => { if (ReferenceEquals(source, bitmap)) Interlocked.Increment(ref evictions); };
        typeof(ImageSource).GetMethod("add_GpuCacheEvictionRequested", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [eviction]);
        long Tick(Visual value) => (long)typeof(Visual).GetProperty("LastRenderedTickMs", Private)!.GetValue(value)!;
        long Frame(Window value) => (long)typeof(Window).GetProperty("LastFullFramePaintStartTickMs", Private)!.GetValue(value)!;
        long Scan() => (long)reclaimerType.GetField("_lastScanTickMs", Private)!.GetValue(reclaimer)!;
        long Drawn() => (long)typeof(ImageSource).GetProperty("LastDrawnTickMs", Private)!.GetValue(bitmap)!;
        byte[]? Raw() => (byte[]?)typeof(BitmapImage).GetProperty("RawPixelData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(bitmap);
        Window? Physical() => (Window?)typeof(Window).GetMethod("GetMacOSPhysicalWindow", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [image]);
        var checks = new List<string>(); var failures = new List<string>(); var states = new List<object>();
        Window? surface = null; byte[]? initialPixels = null; string? initialHash = null; int initialDecodes = 0;
        long previousOwnerFrame = 0, witness = 0, hiddenAt = 0; int phase = 0; bool done = false;
        void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks.Add(message); }
        object Snapshot(string label) => new { label, phase, tick = Environment.TickCount64,
            imageTick = Tick(image), sourceDrawTick = Drawn(), sourceFrame = surface == null ? 0 : Frame(surface), ownerFrame = Frame(owner),
            ownerBackend = owner.RenderTarget?.Backend.ToString(), surfaceBackend = surface?.RenderTarget?.Backend.ToString(),
            scanTick = Scan(), visible = image.IsVisible, raw = Raw() != null, decodes = decoder.Calls,
            evictions, probeReclaims = probe.Reclaims, appActive = app.Active,
            frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,
            physicalHandle = surface?.Handle.ToString(), logicalHandle = Window.GetWindow(image)?.Handle.ToString() };
        void Capture(string name)
        {
            if (surface == null || surface.Handle == 0) return;
            var capture = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(surface.ActualWidth * surface.DpiScale),
                (int)Math.Ceiling(surface.ActualHeight * surface.DpiScale), surface.DpiScale,
                drawing => { drawing.DrawRectangle(surface.Background, null, new Rect(0, 0, surface.ActualWidth, surface.ActualHeight)); surface.Render(drawing); },
                () => surface.Handle != 0);
            if (capture == null) throw new InvalidOperationException("render backend did not provide readback");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(capture));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        void Finish()
        {
            if (done) return; done = true;
            ((IDisposable)reclaimer).Dispose();
            typeof(ImageSource).GetMethod("remove_GpuCacheEvictionRequested", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [eviction]);
            if (popup != null) popup.IsOpen = false; owner.Close();
            File.WriteAllText(Path.Combine(output,"host-result.json"), JsonSerializer.Serialize(new { index, custom, software,
                kind = new[] { "owner", "overlay", "native" }[kind], passed = failures.Count == 0, checks, failures, states,
                completedPhase = phase, decodes = decoder.Calls, evictions, processId = Environment.ProcessId,
                physicalInput = false, desktopScreenshot = false, actualFrameTimestamps = true, automaticIdleTimer = true }));
            app.Stop(app); using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0); app.PostEvent(wake, false);
        }
        owner.Show(); owner.UpdateLayout(); editor.Focus(); editor.Select(2, 3);
        if (popup != null) popup.IsOpen = true;
        using var pulse = NSTimer.CreateRepeatingScheduledTimer(.03, _ =>
        {
            if (done) return;
            try
            {
                if (watch.Elapsed.TotalSeconds > 12) throw new TimeoutException("actual resource scene did not reach its required frame/scan state");
                if (phase == 0)
                {
                    surface = Physical();
                    if (surface == null || surface.Handle == 0 || image.ActualWidth == 0 || Tick(image) == 0 || Frame(surface) == 0 || Raw() == null) return;
                    Require((surface != owner) == (kind == 2), "actual physical owner/overlay/native host matches the configuration");
                    Require(owner.RenderTarget?.Backend == (software ? RenderBackend.Software : RenderBackend.Metal) &&
                        surface.RenderTarget?.Backend == (software ? RenderBackend.Software : RenderBackend.Metal),
                        "both actual render targets use the requested Metal or Software backend");
                    Require(Window.GetWindow(image) == owner, "logical input ownership remains on the original window");
                    initialPixels = Raw()!; initialHash = Convert.ToHexString(SHA256.HashData(initialPixels)); initialDecodes = decoder.Calls;
                    Require(initialDecodes > 0 && bitmap.ImageData?.Length > 0, "actual native PNG decoder publishes pixels and retains encoded bytes");
                    states.Add(Snapshot("initial")); previousOwnerFrame = Frame(owner);
                    owner.InvalidateVisual(); phase = 1;
                }
                else if (phase == 1)
                {
                    if (Frame(owner) <= previousOwnerFrame) return;
                    witness = Frame(owner); states.Add(Snapshot("owner-full-frame")); phase = 2;
                }
                else if (phase == 2)
                {
                    if (Scan() <= witness || Scan() - Tick(image) < options.IdleTimeoutMs) return;
                    states.Add(Snapshot("visible-after-idle-scan"));
                    Require(image.IsVisible && (popup == null || popup.IsOpen), "image remains on a visible presentation surface");
                    Require(ReferenceEquals(initialPixels, Raw()) && decoder.Calls == initialDecodes,
                        "owner full frames cannot reclaim or re-decode a visible image");
                    Require(evictions == 0 && probe.Reclaims == 0, "visible bitmap and reclaimable visual retain their resources");
                    Require(Keyboard.FocusedElement == editor && editor.Text == "图片验证 · 中文🙂" && editor.SelectionStart == 2 && editor.SelectionLength == 3,
                        "editing text, emoji, selection and logical focus survive resource scanning");
                    Capture("visible"); probe.Visibility = Visibility.Collapsed; hiddenAt = Environment.TickCount64;
                    surface!.InvalidateVisual(); phase = 3;
                }
                else if (phase == 3)
                {
                    if (Scan() <= hiddenAt || Scan() - Tick(image) < options.IdleTimeoutMs || Raw() != null) return;
                    states.Add(Snapshot("hidden-reclaimed"));
                    Require(!image.IsVisible && evictions > 0 && probe.Reclaims > 0, "automatic idle scan still reclaims hidden bitmap and visual resources");
                    Require(bitmap.ImageData?.Length > 0, "hidden reclamation retains encoded image data for restoration");
                    probe.Visibility = Visibility.Visible; surface!.InvalidateVisual(); phase = 4;
                }
                else if (phase == 4)
                {
                    if (!image.IsVisible || Raw() == null || decoder.Calls <= initialDecodes) return;
                    states.Add(Snapshot("shown-restored"));
                    Require(Convert.ToHexString(SHA256.HashData(Raw()!)) == initialHash, "showing the image restores identical native-decoded pixels");
                    Require(!ReferenceEquals(initialPixels, Raw()), "reclaimed image restoration publishes a new pixel buffer");
                    Require(Keyboard.FocusedElement == editor && editor.Text == "图片验证 · 中文🙂", "editor remains available after hidden image restoration");
                    Capture("restored"); phase = 5; Finish();
                }
            }
            catch (Exception error)
            {
                states.Add(Snapshot("failure-before-readback")); failures.Add(error.ToString());
                try { Capture("failure-readback"); } catch (Exception captureError) { failures.Add(captureError.ToString()); }
                Finish();
            }
        });
        try { app.Run(); return failures.Count == 0 ? 0 : 1; }
        finally
        {
            pulse.Invalidate(); if (!done) ((IDisposable)reclaimer).Dispose();
            if (popup != null) popup.IsOpen = false; owner.Close(); BitmapImage.SetDecoder(previousDecoder);
            using var note = NSNotification.FromName(NSApplication.WillTerminateNotification, app); host.WillTerminate(note); app.Delegate = null!;
        }
    }
    private sealed class ResourceBorder : Border, IReclaimableResource
    { internal int Reclaims; public void ReclaimIdleResources() => Reclaims++; }
    private sealed class CountingDecoder(INativeImageDecoder inner) : INativeImageDecoder
    {
        internal int Calls;
        public DecodedImage Decode(ReadOnlySpan<byte> data, NativePixelFormat format = NativePixelFormat.Bgra8)
        { Interlocked.Increment(ref Calls); return inner.Decode(data, format); }
        public DecodedImage Decode(Stream data, NativePixelFormat format = NativePixelFormat.Bgra8)
        { Interlocked.Increment(ref Calls); return inner.Decode(data, format); }
        public DecodedImage DecodeFile(string path, NativePixelFormat format = NativePixelFormat.Bgra8)
        { Interlocked.Increment(ref Calls); return inner.DecodeFile(path, format); }
        public bool TryReadDimensions(ReadOnlySpan<byte> data, out int width, out int height) => inner.TryReadDimensions(data, out width, out height);
    }
    private sealed class ResourceDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification)
        { if (_started) return; _started = true; base.DidFinishLaunching(notification); }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}
