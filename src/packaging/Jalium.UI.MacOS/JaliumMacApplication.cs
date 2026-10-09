using AppKit;
using Foundation;
using ObjCRuntime;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>AppKit application that contains recursive termination calls while quit is being negotiated.</summary>
[Register("JaliumMacApplication")]
[SupportedOSPlatform("macos15.0")]
public class JaliumMacApplication : NSApplication
{
    private static NSApplication? _application;
    private bool _terminating;

    public JaliumMacApplication(NativeHandle handle) : base(handle) { }

    /// <summary>Creates the configured principal application before any SharedApplication access.</summary>
    public static NSApplication Initialize()
    {
        if (_application != null) return _application;
        NSApplication.Init();
        string principal = NSBundle.MainBundle.ObjectForInfoDictionary("NSPrincipalClass")?.ToString()
            ?? "JaliumMacApplication";
        nint nativeClass = Class.GetHandle(principal);
        if (nativeClass == 0) throw new InvalidOperationException($"Unknown AppKit principal class '{principal}'.");
        _application = Runtime.GetNSObject<NSApplication>(SharedApplicationForClass(nativeClass, Selector.GetHandle("sharedApplication")))
            ?? throw new InvalidOperationException("AppKit did not create its principal application.");
        return _application;
    }

    [Export("terminate:")]
    public override void Terminate(NSObject? sender)
    {
        // AppKit can bypass the delegate for a recursive terminate: and exit
        // while the outer Closing callback still owns live native resources.
        if (_terminating) return;
        _terminating = true;
        try { base.Terminate(sender); }
        finally { _terminating = false; }
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SharedApplicationForClass(nint receiver, nint selector);

}
