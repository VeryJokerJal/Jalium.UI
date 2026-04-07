namespace Jalium.UI.Core.Platform;

/// <summary>
/// Abstraction for high-resolution frame timing. Used by CompositionTarget
/// to drive the rendering loop at the desired frame rate.
///
/// On Windows, uses CreateWaitableTimerExW with HIGH_RESOLUTION flag.
/// On Linux, uses timerfd_create(CLOCK_MONOTONIC).
/// On Android, uses AChoreographer.
/// Fallback: System.Threading.Timer.
/// </summary>
internal interface IFrameTimer : IDisposable
{
    /// <summary>Arms the timer to fire once after the specified interval.</summary>
    void Arm(long intervalMicroseconds);

    /// <summary>Arms the timer to fire repeatedly at the specified interval.</summary>
    void ArmRepeating(long intervalMicroseconds);

    /// <summary>Stops the timer.</summary>
    void Disarm();

    /// <summary>
    /// Blocks the calling thread until the timer fires or the timeout elapses.
    /// </summary>
    /// <param name="timeoutMs">Maximum wait time in milliseconds (0 = infinite).</param>
    /// <returns>True if the timer fired, false if timeout.</returns>
    bool Wait(uint timeoutMs = 0);
}
