using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.Hardware.Input;
using Android.OS;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI;

/// <summary>Forwards Android device additions, removals and source changes to CSS.</summary>
[SupportedOSPlatform("android24.0")]
internal sealed class JaliumInputDeviceObserver : Java.Lang.Object, InputManager.IInputDeviceListener
{
    private readonly InputManager? _inputManager;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly long _activityGeneration;
    private bool _attached;

    internal JaliumInputDeviceObserver(Activity activity, long activityGeneration)
    {
        _inputManager = activity.GetSystemService(Context.InputService) as InputManager;
        _activityGeneration = activityGeneration;
    }

    internal void Attach()
    {
        if (_attached || _inputManager is null) return;
        _inputManager.RegisterInputDeviceListener(this, _handler);
        _attached = true;
    }

    internal void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _inputManager?.UnregisterInputDeviceListener(this);
    }

    public void OnInputDeviceAdded(int deviceId) => Changed();
    public void OnInputDeviceChanged(int deviceId) => Changed();
    public void OnInputDeviceRemoved(int deviceId) => Changed();

    private void Changed()
    {
        if (_attached)
            AndroidActivityBridge.OnInputDevicesChanged(_activityGeneration);
    }
}
