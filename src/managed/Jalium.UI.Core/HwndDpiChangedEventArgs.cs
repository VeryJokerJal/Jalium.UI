using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Jalium.UI;

/// <summary>
/// Provides data for a native-window DPI change notification.
/// </summary>
public sealed class HwndDpiChangedEventArgs : HandledEventArgs
{
    [Obsolete]
    internal HwndDpiChangedEventArgs(
        double oldDpiX,
        double oldDpiY,
        double newDpiX,
        double newDpiY,
        nint suggestedRect)
        : this(
            new DpiScale(oldDpiX / 96d, oldDpiY / 96d),
            new DpiScale(newDpiX / 96d, newDpiY / 96d),
            ReadSuggestedRect(suggestedRect))
    {
    }

    internal HwndDpiChangedEventArgs(
        DpiScale oldDpi,
        DpiScale newDpi,
        Rect suggestedRect)
    {
        OldDpi = oldDpi;
        NewDpi = newDpi;
        SuggestedRect = suggestedRect;
    }

    /// <summary>
    /// Gets the DPI scale in effect before the change.
    /// </summary>
    public DpiScale OldDpi { get; private set; }

    /// <summary>
    /// Gets the new DPI scale.
    /// </summary>
    public DpiScale NewDpi { get; private set; }

    /// <summary>
    /// Gets the native window rectangle suggested for the new DPI.
    /// </summary>
    public Rect SuggestedRect { get; private set; }

    private static Rect ReadSuggestedRect(nint address)
    {
        NativeRect nativeRect = Marshal.PtrToStructure<NativeRect>(address);
        return new Rect(
            nativeRect.Left,
            nativeRect.Top,
            nativeRect.Right - nativeRect.Left,
            nativeRect.Bottom - nativeRect.Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

/// <summary>
/// Represents the method that handles native-window DPI change notifications.
/// </summary>
public delegate void HwndDpiChangedEventHandler(object sender, HwndDpiChangedEventArgs e);
