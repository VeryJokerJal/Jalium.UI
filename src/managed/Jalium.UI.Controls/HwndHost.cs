using System.Runtime.InteropServices;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls;

/// <summary>
/// Hosts a Win32 window as an element within Jalium.UI content.
/// </summary>
public abstract class HwndHost : FrameworkElement, IDisposable
{
    private IntPtr _hwnd;
    private bool _disposed;

    /// <summary>
    /// Gets the window handle of the hosted window.
    /// </summary>
    public IntPtr Handle => _hwnd;

    /// <summary>
    /// When overridden in a derived class, creates the window to be hosted.
    /// </summary>
    /// <param name="hwndParent">The window handle of the parent window.</param>
    /// <returns>The handle to the child Win32 window to host.</returns>
    protected abstract HandleRef BuildWindowCore(HandleRef hwndParent);

    /// <summary>
    /// When overridden in a derived class, destroys the hosted window.
    /// </summary>
    /// <param name="hwnd">A structure that contains the window handle.</param>
    protected abstract void DestroyWindowCore(HandleRef hwnd);

    /// <summary>
    /// Adds a hook to receive window messages through the hosted window.
    /// </summary>
    protected void AddHook(HwndSourceHook hook)
    {
    }

    /// <summary>
    /// Removes a hook.
    /// </summary>
    protected void RemoveHook(HwndSourceHook hook)
    {
    }

    /// <summary>
    /// Processes window messages sent to the hosted window.
    /// </summary>
    protected virtual IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        return IntPtr.Zero;
    }

    /// <summary>
    /// Called when the hosted window's position changes.
    /// </summary>
    protected virtual void OnWindowPositionChanged(Rect rcBoundingBox)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the HwndHost.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
