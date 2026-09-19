using Foundation;
using ObjCRuntime;
using UIKit;
using System.Runtime.Versioning;

namespace Jalium.UI.iOS;

[Register("JaliumIOSApplicationDelegate")]
[SupportedOSPlatform("ios18.0")]
public abstract class JaliumIOSApplicationDelegate : UIApplicationDelegate
{
    private JaliumApp? _hostedApp;
    private bool _registered;
    private bool _stopped;

    protected abstract JaliumApp CreateHostedApp();

    internal void ConnectRootView(string sceneId, UIView view)
    {
        if (!_registered)
        {
            AppleNativeBridge.RegisterAllBackends();
            _registered = true;
        }
        AppleNativeBridge.RegisterSceneRoot(sceneId, view.Handle);
        if (_hostedApp != null) return;
        _hostedApp = CreateHostedApp()
            ?? throw new InvalidOperationException("CreateHostedApp returned null.");
        _hostedApp.StartHosted();
    }

    public override UISceneConfiguration GetConfiguration(UIApplication application,
        UISceneSession connectingSceneSession, UISceneConnectionOptions options) =>
        new("Jalium Scene", connectingSceneSession.Role)
        {
            DelegateType = typeof(JaliumIOSSceneDelegate)
        };

    public override void OnResignActivation(UIApplication application) =>
        AppleNativeBridge.NotifyLifecycle(60);
    public override void OnActivated(UIApplication application) =>
        AppleNativeBridge.NotifyLifecycle(61);
    public override void DidEnterBackground(UIApplication application) =>
        AppleNativeBridge.NotifyLifecycle(60);
    public override void WillEnterForeground(UIApplication application) =>
        AppleNativeBridge.NotifyLifecycle(61);
    public override void ReceiveMemoryWarning(UIApplication application) =>
        AppleNativeBridge.NotifyLifecycle(63);
    public override void WillTerminate(UIApplication application) => StopHosted();

    private void StopHosted()
    {
        if (_stopped) return;
        _stopped = true;
        AppleNativeBridge.NotifyLifecycle(62);
        _hostedApp?.StopHosted();
        _hostedApp = null;
    }
}

[Register("JaliumIOSSceneDelegate")]
[SupportedOSPlatform("ios18.0")]
public class JaliumIOSSceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public virtual void WillConnect(UIScene scene, UISceneSession session,
        UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene) return;
        var controller = new UIViewController();
        controller.View!.BackgroundColor = UIColor.Clear;
        Window = new UIWindow(windowScene) { RootViewController = controller };
        Window.MakeKeyAndVisible();
        if (UIApplication.SharedApplication.Delegate is JaliumIOSApplicationDelegate app)
            app.ConnectRootView(session.PersistentIdentifier, controller.View);
    }

    [Export("sceneDidDisconnect:")]
    public virtual void DidDisconnect(UIScene scene)
    {
        AppleNativeBridge.UnregisterSceneRoot(scene.Session.PersistentIdentifier);
        Window = null;
    }
}
