using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Shell;
using ObjCRuntime;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>Window-owned Dock surfaces and generation-bound native commands.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class MacOSTaskbar
{
    private static readonly List<Entry> Entries = [];
    private static NSView? _originalApplicationView;
    private static TileView? _applicationView;
    private static bool _applicationOverridden;
    private static NSTimer? _timer;
    private static long _activation;
    private static long _nextThumbnail;

    // The current managed binding rejects null although AppKit supports nil.
    internal static void SetContentView(NSDockTile tile, NSView? view)
    {
        if (view != null) tile.ContentView = view;
        else SetNullableObject(tile.Handle, Selector.GetHandle("setContentView:"), 0);
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetNullableObject(nint receiver, nint selector, nint value);

    internal static IPlatformTaskbarIntegration? Attach(Window window)
    {
        var view = Runtime.GetNSObject<NSView>(window.Handle);
        if (view?.Window is not { } native) return null;
        var entry = new Entry(window, view, native);
        Entries.Add(entry);
        try { entry.Refresh(); return entry; }
        catch { entry.Dispose(); throw; }
    }

    internal static NSMenu CreateDockMenu()
    {
        var result = new NSMenu { AutoEnablesItems = false };
        foreach (var entry in Entries.ToArray())
        {
            if (!entry.Eligible || entry.Lease.Info is not { } info) continue;
            var buttons = info.ThumbButtonInfos?.Where(b => b.Visibility == Visibility.Visible).ToArray() ?? [];
            if (buttons.Length == 0) continue;
            var commands = new NSMenu { AutoEnablesItems = true };
            for (int index = 0; index < buttons.Length; index++)
            {
                var button = buttons[index];
                var commandLease = entry.Lease.CaptureButton(button);
                var weakOwner = new WeakReference<Entry>(entry);
                string title = string.IsNullOrWhiteSpace(button.Description)
                    ? $"{(Chinese ? "操作" : "Action")} {index + 1}" : button.Description;
                var item = new NSMenuItem(title)
                {
                    Enabled = entry.Window.IsEnabled && commandLease.CanInvoke,
                    ToolTip = button.Description,
                    Image = CaptureImage(button.ImageSource),
                };
                if (item.Image != null) item.Image.Size = new CGSize(18, 18);
                item.Activated += (_, _) =>
                {
                    if (!weakOwner.TryGetTarget(out var owner) || !owner.Eligible || !owner.Window.IsEnabled) return;
                    commandLease.Invoke(() => owner.Eligible && owner.Window.IsEnabled);
                };
                item.ValidateMenuItem = _ => weakOwner.TryGetTarget(out var owner) && owner.Eligible &&
                    owner.Window.IsEnabled && commandLease.CanInvoke;
                commands.AddItem(item);
            }
            string name = string.IsNullOrWhiteSpace(info.Description) ? entry.Window.Title : info.Description;
            if (string.IsNullOrWhiteSpace(name)) name = Chinese ? "窗口" : "Window";
            result.AddItem(new NSMenuItem(name) { Submenu = commands });
        }
        return result;
    }

    private static bool Chinese => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";

    private static void RefreshApplication()
    {
        var tile = NSApplication.SharedApplication.DockTile;
        var selected = Entries.Where(e => e.Eligible && e.Lease.Info is { } model &&
                (model.ProgressState != TaskbarItemProgressState.None || model.Overlay != null))
            .OrderByDescending(e => e.Native.IsKeyWindow).ThenByDescending(e => e.Activation).FirstOrDefault();
        var info = selected?.Lease.Info;
        bool custom = info != null && (info.ProgressState != TaskbarItemProgressState.None || info.Overlay != null);
        if (_applicationView != null && tile.ContentView?.Handle != _applicationView.Handle)
            _applicationOverridden = true;
        if (!custom)
        {
            if (_applicationView != null && tile.ContentView?.Handle == _applicationView.Handle)
            {
                SetContentView(tile, _originalApplicationView);
                tile.Display();
            }
            _applicationView = null;
            _originalApplicationView = null;
            _applicationOverridden = false;
        }
        else if (!_applicationOverridden)
        {
            if (_applicationView == null)
            {
                _originalApplicationView = tile.ContentView;
                _applicationView = new TileView();
            }
            _applicationView.Update(NSApplication.SharedApplication.ApplicationIconImage,
                selected!.Overlay.Image, info!, selected.Window.Title);
            tile.ContentView = _applicationView;
            tile.Display();
        }
        RefreshTimer();
    }

    private static void RefreshTimer()
    {
        bool needed = Entries.Any(e => e.Eligible && e.Lease.Info != null &&
            (e.Native.IsMiniaturized || e.Overlay.NeedsPolling || e.Lease.Info.ProgressState == TaskbarItemProgressState.Indeterminate));
        if (needed && _timer == null)
            _timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(100), _ => Tick());
        else if (!needed && _timer != null)
        {
            _timer.Invalidate();
            _timer.Dispose();
            _timer = null;
        }
    }

    private static void Tick()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        bool capture = now >= _nextThumbnail;
        if (capture) _nextThumbnail = now + System.Diagnostics.Stopwatch.Frequency;
        foreach (var entry in Entries.ToArray())
        {
            if (!entry.Eligible) continue;
            if (entry.Overlay.ContentChanged) entry.Refresh();
            if (capture && entry.Native.IsMiniaturized) entry.RefreshWindowTile(capture: true);
            if (entry.Lease.Info?.ProgressState == TaskbarItemProgressState.Indeterminate)
                entry.Native.DockTile.Display();
        }
        if (_applicationView != null && !_applicationOverridden &&
            NSApplication.SharedApplication.DockTile.ContentView?.Handle == _applicationView.Handle)
            NSApplication.SharedApplication.DockTile.Display();
        RefreshTimer();
    }

    private static NSImage? CaptureImage(ImageSource? source)
    {
        if (source == null) return null;
        double width = source.Width, height = source.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return null;
        double fit = Math.Min(64 / width, 64 / height);
        var rect = new Rect((64 - width * fit) / 2, (64 - height * fit) / 2, width * fit, height * fit);
        var bitmap = MacOSWritingToolsBitmapCapture.Capture(64, 64, 1,
            dc => dc.DrawImage(source, rect), () => true);
        return Image(bitmap);
    }

    private static NSImage? Image(BitmapSource? bitmap)
    {
        if (bitmap == null) return null;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        using var data = NSData.FromArray(stream.ToArray());
        return new NSImage(data);
    }

    private sealed class ImageCache : IDisposable
    {
        private ImageSource? _source;
        private readonly Action _changed;
        private long _revision, _contentGeneration;
        internal bool NeedsPolling => _source is BitmapSource { IsFrozen: false } || _source is WriteableBitmap;
        internal bool ContentChanged => _source is BitmapSource raster && raster.ContentGeneration != _contentGeneration;
        private bool _dirty;
        internal ImageCache(Action changed) => _changed = changed;
        internal NSImage? Image { get; private set; }
        internal void Update(ImageSource? source)
        {
            if (!ReferenceEquals(source, _source))
            {
                if (_source != null) _source.Changed -= Changed;
                _source = source;
                if (source != null) source.Changed += Changed;
                _dirty = true;
            }
            _dirty |= ContentChanged;
            if (!_dirty) return;
            _dirty = false;
            long revision = ++_revision;
            var image = CaptureImage(source);
            if (revision == _revision && ReferenceEquals(source, _source))
            { Image = image; _contentGeneration = (source as BitmapSource)?.ContentGeneration ?? 0; }
        }
        private void Changed(object? sender, EventArgs e)
        { ++_revision; _dirty = true; _changed(); }
        public void Dispose()
        {
            if (_source != null) _source.Changed -= Changed;
            _source = null;
            Image = null;
        }
    }

    private sealed class Entry : IPlatformTaskbarIntegration
    {
        internal readonly Window Window;
        internal readonly NSWindow Native;
        internal readonly TaskbarItemInfoLease Lease;
        internal readonly ImageCache Overlay;
        internal long Activation;
        private readonly NSView _view;
        private readonly List<NSObject> _observers = [];
        private readonly string _originalTitle;
        private readonly string _initialWindowTitle;
        private string? _ownedTitle;
        private NSView? _originalView;
        private TileView? _tileView;
        private NSImage? _thumbnail;
        private bool _overridden;
        private bool _disposed, _refreshing, _refreshAgain;
        private Thickness _capturedMargin;
        private bool _observedVisibility;

        internal Entry(Window window, NSView view, NSWindow native)
        {
            Window = window; _view = view; Native = native;
            _originalTitle = native.MiniWindowTitle;
            _initialWindowTitle = native.Title;
            Lease = new(() => Window.TaskbarItemInfo, () => Alive, Changed);
            Overlay = new ImageCache(() => Lease.Refresh());
            Observe(NSWindow.DidBecomeKeyNotification, () => { Activation = ++_activation; RefreshApplication(); });
            Observe(NSWindow.DidUpdateNotification, () =>
            {
                bool eligible = Eligible;
                if (eligible == _observedVisibility) return;
                _observedVisibility = eligible;
                RefreshWindowTile(false);
                RefreshApplication();
            });
            Observe(NSWindow.WillMiniaturizeNotification, () => RefreshWindowTile(true));
            Observe(NSWindow.DidMiniaturizeNotification, () => { RefreshWindowTile(true); RefreshApplication(); });
            Observe(NSWindow.DidDeminiaturizeNotification, RefreshApplication);
            Observe(NSWindow.WillCloseNotification, Dispose);
        }

        private void Observe(NSString name, Action action) =>
            _observers.Add(NSNotificationCenter.DefaultCenter.AddObserver(name, _ => action(), Native));

        private bool Alive => !_disposed && Window.Handle != 0 && Window.Handle == _view.Handle &&
            _view.Window?.Handle == Native.Handle && !Window.IsCloseRequestedForPlatformTermination;
        internal bool Eligible => Alive && Window.ShowInTaskbar && Window.Visibility == Visibility.Visible &&
            (Native.IsVisible || Native.IsMiniaturized);

        public void Refresh() => Lease.Refresh();

        private void Changed()
        {
            if (_refreshing) { _refreshAgain = true; return; }
            _refreshing = true;
            try
            {
                long revision = Lease.Revision;
                Overlay.Update(Lease.Info?.Overlay);
                if (!Lease.IsCurrent(revision)) return;
                RefreshWindowTile(Native.IsMiniaturized || _thumbnail == null ||
                    Lease.Info?.ThumbnailClipMargin != _capturedMargin);
                if (Lease.IsCurrent(revision)) RefreshApplication();
            }
            finally
            {
                _refreshing = false;
                if (_refreshAgain && Alive)
                {
                    _refreshAgain = false;
                    Window.Dispatcher.BeginInvoke(() => { if (Alive) Changed(); });
                }
            }
        }

        internal void RefreshWindowTile(bool capture)
        {
            if (_disposed) return;
            var info = Lease.Info;
            string? description = Eligible && !string.IsNullOrEmpty(info?.Description) ? info.Description : null;
            if (description != null)
            {
                Native.MiniWindowTitle = description;
                _ownedTitle = description;
            }
            else if (_ownedTitle != null)
            {
                if (Native.MiniWindowTitle == _ownedTitle)
                    Native.MiniWindowTitle = _originalTitle == _initialWindowTitle ? Native.Title : _originalTitle;
                _ownedTitle = null;
            }
            bool custom = Eligible && info != null && (info.ProgressState != TaskbarItemProgressState.None ||
                info.Overlay != null || info.ThumbnailClipMargin != Thickness.Zero);
            if (!custom && _tileView == null) return;
            var tile = Native.DockTile;
            if (_tileView != null && tile.ContentView?.Handle != _tileView.Handle) _overridden = true;
            if (!custom)
            {
                if (_tileView != null && tile.ContentView?.Handle == _tileView.Handle)
                {
                    SetContentView(tile, _originalView);
                    tile.Display();
                }
                _tileView = null; _originalView = null; _thumbnail = null; _overridden = false;
                return;
            }
            if (_overridden) return;
            long revision = Lease.Revision;
            if (capture)
            {
                double Margin(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
                var margin = info!.ThumbnailClipMargin;
                _capturedMargin = margin;
                double left = Margin(margin.Left), top = Margin(margin.Top);
                double width = Window.ActualWidth - left - Margin(margin.Right);
                double height = Window.ActualHeight - top - Margin(margin.Bottom);
                _thumbnail = null;
                if (width > 0 && height > 0 && double.IsFinite(width) && double.IsFinite(height))
                {
                    double scale = Math.Min(256 / width, 256 / height);
                    var bitmap = MacOSWritingToolsBitmapCapture.Capture(
                        Math.Max(1, (int)Math.Ceiling(width * scale)), Math.Max(1, (int)Math.Ceiling(height * scale)), scale,
                        dc => { dc.PushTransform(new TranslateTransform(-left, -top)); Window.Render(dc); dc.Pop(); },
                        () => Lease.IsCurrent(revision));
                    if (!Lease.IsCurrent(revision)) return;
                    _thumbnail = Image(bitmap);
                }
            }
            if (!Lease.IsCurrent(revision)) return;
            if (_tileView == null)
            {
                _originalView = tile.ContentView;
                _tileView = new TileView();
            }
            _tileView.Update(_thumbnail, Overlay.Image, info!, Window.Title);
            tile.ContentView = _tileView;
            tile.Display();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Lease.Dispose();
            // Clear only values still owned by this registration.
            if (_tileView != null && Native.DockTile.ContentView?.Handle == _tileView.Handle)
            {
                SetContentView(Native.DockTile, _originalView);
                Native.DockTile.Display();
            }
            if (_ownedTitle != null && Native.MiniWindowTitle == _ownedTitle)
                Native.MiniWindowTitle = _originalTitle == _initialWindowTitle ? Native.Title : _originalTitle;
            foreach (var observer in _observers) { NSNotificationCenter.DefaultCenter.RemoveObserver(observer); observer.Dispose(); }
            _observers.Clear();
            Overlay.Dispose();
            Entries.Remove(this);
            RefreshApplication();
        }
    }

    [Register("JaliumTaskbarTileView")]
    internal sealed class TileView : NSView
    {
        private NSImage? _baseImage, _overlay;
        private TaskbarItemProgressState _state;
        private double _value;
        internal TileView() : base(new CGRect(0, 0, 128, 128))
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
            AccessibilityElement = true;
            AccessibilityRole = "AXImage";
        }
        internal void Update(NSImage? image, NSImage? overlay, TaskbarItemInfo info, string title)
        {
            _baseImage = image; _overlay = overlay; _state = info.ProgressState; _value = info.ProgressValue;
            AccessibilityLabel = string.IsNullOrWhiteSpace(info.Description) ? title : info.Description;
            string value = _state switch
            {
                TaskbarItemProgressState.Indeterminate => Chinese ? "进行中" : "In progress",
                TaskbarItemProgressState.Error => Chinese ? "错误" : "Error",
                TaskbarItemProgressState.Paused => Chinese ? "已暂停" : "Paused",
                TaskbarItemProgressState.Normal => _value.ToString("P0"),
                _ => string.Empty,
            };
            AccessibilityValue = new NSString(value);
            ToolTip = AccessibilityLabel;
            NeedsDisplay = true;
        }
        public override void DrawRect(CGRect dirtyRect)
        {
            var bounds = Bounds;
            if (_baseImage is { } image && image.Size.Width > 0 && image.Size.Height > 0)
            {
                double fit = Math.Min(bounds.Width / image.Size.Width, bounds.Height / image.Size.Height);
                double width = image.Size.Width * fit, height = image.Size.Height * fit;
                image.Draw(new CGRect((bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height),
                    CGRect.Empty, NSCompositingOperation.SourceOver, 1);
            }
            NFloat unit = (NFloat)(Math.Min(bounds.Width, bounds.Height) / 128);
            if (_overlay != null)
                _overlay.Draw(new CGRect(bounds.Right - 40 * unit, bounds.Bottom - 40 * unit, 36 * unit, 36 * unit),
                    CGRect.Empty, NSCompositingOperation.SourceOver, 1);
            if (_state == TaskbarItemProgressState.None) return;
            var track = new CGRect(8 * unit, 6 * unit, bounds.Width - 16 * unit, 12 * unit);
            NSColor.Black.ColorWithAlphaComponent(0.8f).SetFill();
            NSBezierPath.FromRoundedRect(track, 6 * unit, 6 * unit).Fill();
            var fill = track.Inset(2 * unit, 2 * unit);
            NSColor color = _state == TaskbarItemProgressState.Error ? NSColor.SystemRed :
                _state == TaskbarItemProgressState.Paused ? NSColor.SystemYellow : NSColor.ControlAccent;
            color.SetFill();
            if (_state == TaskbarItemProgressState.Indeterminate)
            {
                double phase = NSWorkspace.SharedWorkspace.AccessibilityDisplayShouldReduceMotion ? 0.5 :
                    (Math.Sin(System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency * 4) + 1) / 2;
                fill.X += (NFloat)(fill.Width * 0.65 * phase);
                fill.Width *= (NFloat)0.35;
            }
            else fill.Width *= (NFloat)Math.Clamp(_value, 0, 1);
            if (fill.Width > 0) NSBezierPath.FromRoundedRect(fill, 4 * unit, 4 * unit).Fill();
            if (_state is TaskbarItemProgressState.Error or TaskbarItemProgressState.Paused)
            {
                string symbol = _state == TaskbarItemProgressState.Error ? "exclamationmark.circle.fill" : "pause.circle.fill";
                using var marker = NSImage.GetSystemSymbol(symbol, AccessibilityValue?.ToString());
                marker?.Draw(new CGRect(6 * unit, bounds.Bottom - 36 * unit, 28 * unit, 28 * unit),
                    CGRect.Empty, NSCompositingOperation.SourceOver, 1);
            }
        }
    }
}
