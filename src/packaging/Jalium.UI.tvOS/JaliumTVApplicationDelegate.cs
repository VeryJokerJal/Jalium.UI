using Foundation;
using ObjCRuntime;
using UIKit;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Jalium.UI.tvOS;

internal static partial class TvNativeBridge
{
    [LibraryImport("__Internal", EntryPoint = "jalium_aot_register_all_backends")]
    internal static partial void RegisterAllBackends();
    [LibraryImport("__Internal", EntryPoint = "jalium_apple_set_root_view")]
    internal static partial void SetRootView(nint view);
    [LibraryImport("__Internal", EntryPoint = "jalium_apple_notify_lifecycle")]
    internal static partial void NotifyLifecycle(int eventType);
}

[Register("JaliumTVApplicationDelegate")]
[SupportedOSPlatform("tvos18.0")]
public abstract class JaliumTVApplicationDelegate : UIApplicationDelegate
{
    private JaliumApp? _app;
    protected abstract JaliumApp CreateHostedApp();

    internal void Connect(UIView view)
    {
        TvNativeBridge.RegisterAllBackends();
        TvNativeBridge.SetRootView(view.Handle);
        if (_app != null) return;
        _app = CreateHostedApp();
        _app.StartHosted();
    }

    public override UISceneConfiguration GetConfiguration(UIApplication application,
        UISceneSession connectingSceneSession, UISceneConnectionOptions options) =>
        new("Jalium TV Scene", connectingSceneSession.Role)
        {
            DelegateType = typeof(JaliumTVSceneDelegate)
        };

    public override void OnResignActivation(UIApplication application) => TvNativeBridge.NotifyLifecycle(60);
    public override void OnActivated(UIApplication application) => TvNativeBridge.NotifyLifecycle(61);
    public override void ReceiveMemoryWarning(UIApplication application) => TvNativeBridge.NotifyLifecycle(63);
    public override void WillTerminate(UIApplication application)
    {
        TvNativeBridge.NotifyLifecycle(62);
        _app?.StopHosted();
        _app = null;
    }
}

[Register("JaliumTVSceneDelegate")]
[SupportedOSPlatform("tvos18.0")]
public class JaliumTVSceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public virtual void WillConnect(UIScene scene, UISceneSession session,
        UISceneConnectionOptions options)
    {
        if (scene is not UIWindowScene windowScene) return;
        var controller = new UIViewController();
        Window = new UIWindow(windowScene) { RootViewController = controller };
        Window.MakeKeyAndVisible();
        if (UIApplication.SharedApplication.Delegate is JaliumTVApplicationDelegate app)
            app.Connect(controller.View!);
    }
}
