using System.Reflection;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class LinuxWin32GuardTests
{
    [Fact]
    public void CompositionTargetSelection_WithLinuxHandle_DoesNotProbeUser32Style()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var window = new Window();
        SetHandle(window, (nint)0x3101);

        Assert.False(InvokePrivate<bool>(window, "ShouldUseCompositionRenderTarget"));
    }

    [Fact]
    public void NativeCaptureRelease_WithoutInitializedLinuxWindow_DoesNotCallUser32()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var window = new Window();

        window.ReleaseNativeCapture();
    }

    [Fact]
    public void KeyStateProviderReset_OnLinux_RestoresNativePlatformProvider()
    {
        if (!OperatingSystem.IsLinux())
            return;

        try
        {
            Window.SetKeyStateProviderForTesting(
                static _ => unchecked((short)0x8000));
            Assert.True(InvokePrivateStatic<bool>("IsVirtualKeyDown", -1));

            Window.SetKeyStateProviderForTesting(null);

            // -1 is deterministically rejected by jalium_input_get_key_state.
            // The important regression check is that resetting the test seam
            // reaches that Linux ABI instead of user32!GetKeyState.
            Assert.False(InvokePrivateStatic<bool>("IsVirtualKeyDown", -1));
        }
        finally
        {
            Window.SetKeyStateProviderForTesting(null);
        }
    }

    [Fact]
    public void ManagedTeardown_WithSyntheticLinuxHandle_DoesNotCallDestroyWindow()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var window = new Window();
        var handle = (nint)0x3102;
        SetHandle(window, handle);

        InvokePrivate<object?>(window, "CompleteManagedTeardown", handle, false);

        Assert.Equal(nint.Zero, window.Handle);
    }

    private static void SetHandle(Window window, nint handle)
    {
        FieldInfo? field = typeof(Window).GetField(
            "<Handle>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(window, handle);
    }

    private static T InvokePrivate<T>(object instance, string methodName, params object?[] arguments)
    {
        MethodInfo? method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(instance, arguments)!;
    }

    private static T InvokePrivateStatic<T>(string methodName, params object?[] arguments)
    {
        MethodInfo? method = typeof(Window).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(null, arguments)!;
    }
}
