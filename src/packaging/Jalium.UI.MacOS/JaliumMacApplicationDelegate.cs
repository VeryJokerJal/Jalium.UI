using AppKit;
using Foundation;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>AppKit delegate that hosts one Jalium application per process.</summary>
[Register("JaliumMacApplicationDelegate")]
[SupportedOSPlatform("macos15.0")]
public abstract class JaliumMacApplicationDelegate : NSApplicationDelegate
{
    private JaliumApp? _hostedApp;
    private bool _stopped;

    /// <summary>Creates and configures the Jalium host and application.</summary>
    protected abstract JaliumApp CreateHostedApp();

    public override void DidFinishLaunching(NSNotification notification)
    {
        _hostedApp = CreateHostedApp()
            ?? throw new InvalidOperationException("CreateHostedApp returned null.");
        _hostedApp.StartHosted();
    }

    public override void WillTerminate(NSNotification notification) => StopHosted();

    protected virtual void StopHosted(int exitCode = 0)
    {
        if (_stopped) return;
        _stopped = true;
        _hostedApp?.StopHosted(exitCode);
        _hostedApp = null;
    }
}
