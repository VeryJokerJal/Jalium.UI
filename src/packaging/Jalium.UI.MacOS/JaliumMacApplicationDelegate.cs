using AppKit;
using Foundation;
using Jalium.UI.Controls.Platform;
using ObjCRuntime;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>AppKit delegate that hosts one Jalium application per process.</summary>
[Register("JaliumMacApplicationDelegate")]
[SupportedOSPlatform("macos15.0")]
public abstract class JaliumMacApplicationDelegate : NSApplicationDelegate
{
    private JaliumApp? _hostedApp;
    private bool _stopped;
    private bool _handlingReopen;
    private MacOSWindowCloseRequest? _windowCloseRequest;
    private bool _retryTerminationAfterClose;

    /// <summary>Creates and configures the Jalium host and application.</summary>
    protected abstract JaliumApp CreateHostedApp();

    public override void DidFinishLaunching(NSNotification notification)
    {
        PlatformFileDialogs.Show = MacOSFileDialogs.Show;
        ConfigureNativeWindowMenu(NSApplication.SharedApplication);
        _hostedApp = CreateHostedApp()
            ?? throw new InvalidOperationException("CreateHostedApp returned null.");
        _hostedApp.StartHosted();
    }

    /// <summary>Registers AppKit Window actions and supplies standard visibility and quit actions for an empty application menu.</summary>
    protected virtual void ConfigureNativeWindowMenu(NSApplication application)
    {
        var menu = application.MainMenu;
        if (menu == null)
        {
            menu = new NSMenu();
            application.MainMenu = menu;
        }
        var appMenu = menu.Items.FirstOrDefault()?.Submenu;
        if (appMenu == null || appMenu.Handle == application.WindowsMenu?.Handle || appMenu.Title == "Window")
        {
            appMenu = new NSMenu();
            menu.InsertItem(new NSMenuItem { Submenu = appMenu }, 0);
        }
        if (appMenu.Items.Length == 0)
        {
            string appName = NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleDisplayName")?.ToString()
                ?? NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleName")?.ToString()
                ?? NSProcessInfo.ProcessInfo.ProcessName;
            appMenu.Title = appName;
            appMenu.AddItem(new NSMenuItem($"Hide {appName}", new Selector("hide:"), "h")
            { Target = application });
            appMenu.AddItem(new NSMenuItem("Hide Others", new Selector("hideOtherApplications:"), "h")
            {
                Target = application,
                KeyEquivalentModifierMask = NSEventModifierMask.CommandKeyMask | NSEventModifierMask.AlternateKeyMask
            });
            appMenu.AddItem(new NSMenuItem("Show All", new Selector("unhideAllApplications:"), string.Empty)
            { Target = application });
            appMenu.AddItem(NSMenuItem.SeparatorItem);
            appMenu.AddItem(new NSMenuItem($"Quit {appName}", new ObjCRuntime.Selector("terminate:"), "q"));
        }
        if (application.WindowsMenu != null) return;
        var existing = menu.Items.Select(item => item.Submenu).FirstOrDefault(submenu => submenu?.Title == "Window");
        if (existing != null)
        {
            application.WindowsMenu = existing;
            return;
        }
        var windowMenu = new NSMenu("Window");
        windowMenu.AddItem(new NSMenuItem("Minimize", new ObjCRuntime.Selector("performMiniaturize:"), "m"));
        windowMenu.AddItem(new NSMenuItem("Zoom", new ObjCRuntime.Selector("zoom:"), string.Empty));
        windowMenu.AddItem(new NSMenuItem("Enter Full Screen", new ObjCRuntime.Selector("toggleFullScreen:"), "f")
        { KeyEquivalentModifierMask = NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ControlKeyMask });
        windowMenu.AddItem(new NSMenuItem("Close", new ObjCRuntime.Selector("performClose:"), "w"));
        windowMenu.AddItem(NSMenuItem.SeparatorItem);
        windowMenu.AddItem(new NSMenuItem("Bring All to Front", new ObjCRuntime.Selector("arrangeInFront:"), string.Empty));
        menu.AddItem(new NSMenuItem { Submenu = windowMenu });
        application.WindowsMenu = windowMenu;
    }

    public override void WillTerminate(NSNotification notification) => StopHosted();

    public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender)
    {
        if (_stopped || Jalium.UI.Application.Current is not { } application)
            return NSApplicationTerminateReply.Now;
        if (_windowCloseRequest != null)
        {
            // Terminate(Later) enters a modal run loop. A nested Terminate from
            // Closing would prevent that very callback from making its decision.
            return _retryTerminationAfterClose || application.Windows.Cast<Jalium.UI.Window>()
                .Any(window => window.IsCloseDecisionPendingForPlatformTermination)
                ? NSApplicationTerminateReply.Cancel : NSApplicationTerminateReply.Later;
        }
        MacOSWindowCloseRequest? request = null;
        request = new MacOSWindowCloseRequest(application, allowed =>
            sender.BeginInvokeOnMainThread(() =>
            {
                if (_stopped || !ReferenceEquals(_windowCloseRequest, request)) return;
                bool retryTermination = _retryTerminationAfterClose;
                // The coordinator releases its own scope. A rejected decision
                // may still own earlier accepted windows awaiting render teardown.
                _windowCloseRequest = null;
                _retryTerminationAfterClose = false;
                if (retryTermination)
                {
                    if (allowed) sender.Terminate(null);
                }
                else sender.ReplyToApplicationShouldTerminate(allowed);
            }));
        _windowCloseRequest = request;
        var result = request.Begin();
        if (result == MacOSWindowCloseResult.Pending)
        {
            // Let an already running Closing return first. Continue its quit
            // negotiation, then retry AppKit termination only if it was accepted.
            _retryTerminationAfterClose = request.IsWaitingForCloseDecision;
            return _retryTerminationAfterClose ? NSApplicationTerminateReply.Cancel : NSApplicationTerminateReply.Later;
        }
        _windowCloseRequest = null;
        _retryTerminationAfterClose = false;
        return result == MacOSWindowCloseResult.Complete
            ? NSApplicationTerminateReply.Now : NSApplicationTerminateReply.Cancel;
    }

    public override bool ApplicationShouldHandleReopen(NSApplication sender, bool hasVisibleWindows)
    {
        if (_stopped || _handlingReopen) return false;
        if (Jalium.UI.Application.Current is not { } application) return true;
        IEnumerable<Jalium.UI.Window> candidates = application.Windows.Cast<Jalium.UI.Window>().Reverse().ToArray();
        if (application.MainWindow is { } main)
            candidates = new[] { main }.Concat(candidates.Where(candidate => !ReferenceEquals(candidate, main)));
        // AppKit counts miniaturized windows as visible in this callback even
        // though NSWindow.IsVisible is false. Leave an already displayed native
        // window to AppKit, but restore a usable window when all are hidden or
        // miniaturized. A disabled modal owner cannot be the reopen target.
        if (hasVisibleWindows)
        {
            var managedByNativeWindow = new Dictionary<nint, Jalium.UI.Window>();
            foreach (var candidate in candidates)
            {
                if (candidate.Handle == 0) continue;
                nint nativeWindow = Runtime.GetNSObject<NSView>(candidate.Handle)?.Window?.Handle ?? 0;
                if (nativeWindow != 0) managedByNativeWindow[nativeWindow] = candidate;
            }
            bool hasDisplayedWindow = false;
            sender.EnumerateWindows(0, (NSWindow window, ref bool stop) =>
            {
                if (!window.IsVisible || window.IsMiniaturized) return;
                if (managedByNativeWindow.TryGetValue(window.Handle, out var managedWindow) &&
                    (!managedWindow.IsEnabled || managedWindow.IsCloseRequestedForPlatformTermination))
                    return;
                hasDisplayedWindow = true;
                stop = true;
            });
            if (hasDisplayedWindow) return true;
        }

        _handlingReopen = true;
        try
        {
            foreach (var window in candidates)
            {
                if (_stopped) return false;
                if (window.Handle == 0 || !window.IsEnabled || window.IsCloseRequestedForPlatformTermination) continue;
                // Activate owns visibility and close guards. If an application
                // callback closes or hides this candidate, try the next live
                // window from the snapshot rather than mutating it again.
                if (window.Activate()) return false;
            }
            return true;
        }
        finally { _handlingReopen = false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopHosted();
        base.Dispose(disposing);
    }

    protected virtual void StopHosted(int exitCode = 0)
    {
        if (_stopped) return;
        _stopped = true;
        _windowCloseRequest?.Dispose();
        _windowCloseRequest = null;
        _retryTerminationAfterClose = false;
        _hostedApp?.StopHosted(exitCode);
        _hostedApp = null;
    }
}
