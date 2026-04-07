using System.Runtime.InteropServices;

namespace Jalium.UI.Core.Platform;

/// <summary>
/// Cross-platform frame timer using the jalium.native.platform library.
/// On Windows, wraps CreateWaitableTimerExW with HIGH_RESOLUTION.
/// On Linux, wraps timerfd. On Android, wraps clock_nanosleep.
/// </summary>
internal sealed partial class NativeFrameTimer : IFrameTimer
{
    private const string PlatformLib = "jalium.native.platform";

    private nint _handle;
    private bool _disposed;

    public NativeFrameTimer()
    {
        int result = TimerCreate(out _handle);
        if (result != 0 || _handle == nint.Zero)
            throw new InvalidOperationException(
                $"Failed to create native timer (error {result}).");
    }

    public void Arm(long intervalMicroseconds)
    {
        if (_handle != nint.Zero)
            TimerArm(_handle, intervalMicroseconds);
    }

    public void ArmRepeating(long intervalMicroseconds)
    {
        if (_handle != nint.Zero)
            TimerArmRepeating(_handle, intervalMicroseconds);
    }

    public void Disarm()
    {
        if (_handle != nint.Zero)
            TimerDisarm(_handle);
    }

    public bool Wait(uint timeoutMs = 0)
    {
        if (_handle == nint.Zero) return false;
        return TimerWait(_handle, timeoutMs) != 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle != nint.Zero)
        {
            TimerDestroy(_handle);
            _handle = nint.Zero;
        }
    }

    [LibraryImport(PlatformLib, EntryPoint = "jalium_timer_create")]
    private static partial int TimerCreate(out nint timer);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_timer_destroy")]
    private static partial void TimerDestroy(nint timer);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_timer_arm")]
    private static partial void TimerArm(nint timer, long intervalMicroseconds);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_timer_arm_repeating")]
    private static partial void TimerArmRepeating(nint timer, long intervalMicroseconds);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_timer_disarm")]
    private static partial void TimerDisarm(nint timer);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_timer_wait")]
    private static partial int TimerWait(nint timer, uint timeoutMs);
}
