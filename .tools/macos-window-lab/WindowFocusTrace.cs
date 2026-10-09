using AppKit;
using Foundation;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Window = Jalium.UI.Window;

namespace MacOSWindowLab;

// Opt-in observation of the existing validation windows. Reading these
// properties must not activate a window or change its first responder.
internal sealed partial class WindowFocusTrace : IDisposable
{
    private readonly Window _window;
    private readonly List<NSObject> _observers = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private string? _last;

    private WindowFocusTrace(Window window)
    {
        _window = window;
        window.Loaded += Loaded;
        window.StateChanged += Changed;
        window.Activated += Changed;
        window.Deactivated += Changed;
        window.Closed += Closed;
        _timer.Tick += Tick;
    }

    internal static void Attach(Window window) => _ = new WindowFocusTrace(window);

    private void Loaded(object? sender, EventArgs args)
    {
        var native = Runtime.GetNSObject<NSView>(_window.Handle)?.Window;
        if (native is null) return;
        foreach (var name in new[]
        {
            NSWindow.WillEnterFullScreenNotification, NSWindow.DidEnterFullScreenNotification,
            NSWindow.WillExitFullScreenNotification, NSWindow.DidExitFullScreenNotification,
            NSWindow.DidBecomeKeyNotification, NSWindow.DidResignKeyNotification
        })
            _observers.Add(NSNotificationCenter.DefaultCenter.AddObserver(name, note =>
            {
                string notificationName = note.Name.ToString();
                Capture(notificationName, true);
                NSApplication.SharedApplication.BeginInvokeOnMainThread(() => Capture(notificationName + ":next-loop", true));
            }, native));
        foreach (var name in new[] { NSApplication.DidHideNotification, NSApplication.DidUnhideNotification })
            _observers.Add(NSNotificationCenter.DefaultCenter.AddObserver(name, note =>
                Capture(note.Name.ToString(), true), NSApplication.SharedApplication));
        _timer.Start();
        Capture("loaded", true);
    }

    private void Changed(object? sender, EventArgs args) => Capture("managed-event", true);
    private void Tick(object? sender, EventArgs args) => Capture("poll", false);
    private void Closed(object? sender, EventArgs args) => Dispose();

    private void Capture(string reason, bool force)
    {
        if (_window.Handle == 0) return;
        try
        {
            var app = NSApplication.SharedApplication;
            var view = Runtime.GetNSObject<NSView>(_window.Handle)!;
            var native = view.Window!;
            var focus = Keyboard.FocusedElement;
            (string? Text, int? Start, int? Length) source = focus switch
            {
                TextBox text => (text.Text, text.SelectionStart, text.SelectionLength),
                RichTextBox rich => (rich.Document.GetText(), rich.Selection.Start.DocumentOffset,
                    rich.Selection.End.DocumentOffset - rich.Selection.Start.DocumentOffset),
                EditControl edit => (edit.Text, edit.SelectionStart, edit.SelectionLength),
                _ => (null, null, null)
            };
            string fingerprint = JsonSerializer.Serialize(new
            {
                pid = Environment.ProcessId, appActive = app.Active, appHidden = app.Hidden,
                contentHandle = (long)_window.Handle, nativeHandle = (long)native.Handle,
                managedVisibility = _window.Visibility.ToString(),
                nativeKey = native.IsKeyWindow, nativeMain = native.IsMainWindow,
                appKey = app.KeyWindow?.Handle == native.Handle, appMain = app.MainWindow?.Handle == native.Handle,
                visible = native.IsVisible, managedActive = _window.IsActive,
                titlebar = _window.TitleBarStyle.ToString(), state = _window.WindowState.ToString(),
                nativeFullscreen = (native.StyleMask & NSWindowStyle.FullScreenWindow) != 0,
                viewFirstResponder = native.FirstResponder?.Handle == view.Handle,
                windowFirstResponder = native.FirstResponder?.Handle == native.Handle,
                firstResponder = native.FirstResponder?.Description,
                viewAxFocus = Describe(Focused(view)), windowAxFocus = Describe(Focused(native)),
                appAxFocus = Describe(Focused(app)), managedFocus = focus?.GetType().Name,
                text = source.Text, selectionStart = source.Start, selectionLength = source.Length,
                width = _window.Width, height = _window.Height,
                nativeContentWidth = (double)view.Bounds.Width, nativeContentHeight = (double)view.Bounds.Height
            });
            if (!force && fingerprint == _last) return;
            _last = fingerprint;
            File.AppendAllText(Program.LogPrefix + "-focus.jsonl",
                $"{{\"time\":\"{DateTimeOffset.UtcNow:O}\",\"reason\":{JsonSerializer.Serialize(reason)},\"snapshot\":{fingerprint}}}\n");
        }
        catch (Exception error)
        {
            File.AppendAllText(Program.LogPrefix + "-focus.jsonl",
                JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, reason, error = error.ToString() }) + "\n");
        }
    }

    private static NSObject? Focused(NSObject value)
    {
        var selector = new Selector("accessibilityFocusedUIElement");
        return value.RespondsToSelector(selector) ? Runtime.GetNSObject(ReadObject(value.Handle, selector.Handle)) : null;
    }

    private static object? Describe(NSObject? value) => value is null ? null : new
    {
        handle = (long)value.Handle, description = value.Description,
        role = (value as NSAccessibilityElement)?.AccessibilityRole,
        label = (value as NSAccessibilityElement)?.AccessibilityLabel
    };

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Tick;
        foreach (var token in _observers) { NSNotificationCenter.DefaultCenter.RemoveObserver(token); token.Dispose(); }
        _observers.Clear();
        _window.Loaded -= Loaded;
        _window.StateChanged -= Changed;
        _window.Activated -= Changed;
        _window.Deactivated -= Changed;
        _window.Closed -= Closed;
    }

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint ReadObject(nint receiver, nint selector);
}
