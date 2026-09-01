using System.Runtime.InteropServices;

namespace Jalium.UI.iOS;

internal static partial class AppleNativeBridge
{
    [LibraryImport("__Internal", EntryPoint = "jalium_aot_register_all_backends")]
    internal static partial void RegisterAllBackends();

    [LibraryImport("__Internal", EntryPoint = "jalium_apple_set_root_view")]
    internal static partial void SetRootView(nint view);

    [LibraryImport("__Internal", EntryPoint = "jalium_apple_notify_lifecycle")]
    internal static partial void NotifyLifecycle(int eventType);
}
