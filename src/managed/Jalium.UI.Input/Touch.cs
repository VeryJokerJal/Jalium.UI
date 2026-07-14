using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Jalium.UI.Input;

/// <summary>
/// Manages active touch contacts and exposes platform-level touch capabilities.
/// Core types (TouchDevice / TouchPoint / TouchPointCollection / TouchAction /
/// TouchCapabilities) live in Jalium.UI.Core/Input/TouchDevice.cs so that
/// UIElement can reference them without a circular dependency.
/// </summary>
public static class Touch
{
    private static readonly Dictionary<int, TouchDevice> _touchDevices = new();
    private static TouchCapabilities? _cachedCapabilities;
    private static TouchCapabilities? _overrideCapabilities;
    private static Func<(int Result, int TouchPresent, int Contacts)>?
        _linuxCapabilitiesQueryForTesting;

    static Touch()
    {
        TouchDevice.FrameUpdated += static timestamp =>
            FrameReported?.Invoke(null!, new TouchFrameEventArgs(timestamp));
    }

    /// <summary>Occurs whenever any active touch device reports a state change.</summary>
    public static event TouchFrameEventHandler? FrameReported;

    /// <summary>Gets the collection of active touch devices.</summary>
    public static IReadOnlyCollection<TouchDevice> ActiveDevices => _touchDevices.Values;

    /// <summary>Gets the number of active touch points.</summary>
    public static int TouchPointCount => _touchDevices.Count;

    /// <summary>Gets a value indicating whether touch input is present on this system.</summary>
    public static bool IsTouchAvailable => GetTouchCapabilities().TouchPresent;

    /// <summary>
    /// Returns current touch capabilities. Windows keeps the process-lifetime
    /// system-metric snapshot; Linux re-queries the active Wayland seat or XI2
    /// devices so hot-plug changes become visible. Unchanged Linux snapshots
    /// reuse the previous object.
    /// </summary>
    public static TouchCapabilities GetTouchCapabilities()
    {
        if (_overrideCapabilities is not null)
            return _overrideCapabilities;

        if (OperatingSystem.IsLinux() || _linuxCapabilitiesQueryForTesting is not null)
        {
            var current = QueryLinuxPlatformCapabilities();
            if (_cachedCapabilities is not null &&
                _cachedCapabilities.TouchPresent == current.TouchPresent &&
                _cachedCapabilities.Contacts == current.Contacts)
            {
                return _cachedCapabilities;
            }
            return _cachedCapabilities = current;
        }

        return _cachedCapabilities ??= QueryPlatformCapabilities();
    }

    private static TouchCapabilities QueryPlatformCapabilities()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new TouchCapabilities { TouchPresent = false, Contacts = 0 };
        }

        try
        {
            int digitizer = NativeTouchInterop.GetSystemMetrics(NativeTouchInterop.SM_DIGITIZER);
            const int TouchMask = NativeTouchInterop.NID_READY
                                   | NativeTouchInterop.NID_INTEGRATED_TOUCH
                                   | NativeTouchInterop.NID_EXTERNAL_TOUCH;
            bool touchPresent = (digitizer & TouchMask) != 0;
            int contacts = touchPresent
                ? Math.Max(0, NativeTouchInterop.GetSystemMetrics(NativeTouchInterop.SM_MAXIMUMTOUCHES))
                : 0;
            return new TouchCapabilities { TouchPresent = touchPresent, Contacts = contacts };
        }
        catch (DllNotFoundException)
        {
            return new TouchCapabilities { TouchPresent = false, Contacts = 0 };
        }
        catch (EntryPointNotFoundException)
        {
            return new TouchCapabilities { TouchPresent = false, Contacts = 0 };
        }
    }

    private static TouchCapabilities QueryLinuxPlatformCapabilities()
    {
        try
        {
            int result;
            int touchPresent;
            int contacts;
            if (_linuxCapabilitiesQueryForTesting is not null)
            {
                (result, touchPresent, contacts) = _linuxCapabilitiesQueryForTesting();
            }
            else
            {
                result = Jalium.UI.Interop.NativeMethods.InputGetTouchCapabilities(
                    out touchPresent, out contacts);
            }

            if (result != 0 || touchPresent == 0)
                return new TouchCapabilities { TouchPresent = false, Contacts = 0 };
            return new TouchCapabilities
            {
                TouchPresent = true,
                Contacts = Math.Max(0, contacts),
            };
        }
        catch (DllNotFoundException)
        {
            return new TouchCapabilities { TouchPresent = false, Contacts = 0 };
        }
        catch (EntryPointNotFoundException)
        {
            return new TouchCapabilities { TouchPresent = false, Contacts = 0 };
        }
        catch (BadImageFormatException)
        {
            return new TouchCapabilities { TouchPresent = false, Contacts = 0 };
        }
    }

    /// <summary>Resets the cached capabilities. Intended for tests.</summary>
    internal static void ResetCapabilitiesCacheForTesting()
    {
        _cachedCapabilities = null;
        _overrideCapabilities = null;
        _linuxCapabilitiesQueryForTesting = null;
    }

    /// <summary>Allows tests to inject a synthetic capabilities snapshot.</summary>
    internal static void OverrideCapabilitiesForTesting(TouchCapabilities capabilities)
    {
        _overrideCapabilities = capabilities;
    }

    /// <summary>Overrides only the native Linux query while preserving refresh behavior.</summary>
    internal static void OverrideLinuxCapabilitiesQueryForTesting(
        Func<(int Result, int TouchPresent, int Contacts)> query)
    {
        _cachedCapabilities = null;
        _overrideCapabilities = null;
        _linuxCapabilitiesQueryForTesting = query;
    }

    /// <summary>Registers a new touch contact.</summary>
    public static TouchDevice RegisterTouchPoint(int pointerId, Point position, UIElement? target)
    {
        if (_touchDevices.Remove(pointerId, out TouchDevice? previous))
            previous.DeactivateForManager();
        var device = new PointerTouchDevice(pointerId, target);
        device.UpdatePosition(position);
        _touchDevices[pointerId] = device;
        return device;
    }

    /// <summary>Updates the position of an existing touch contact.</summary>
    public static void UpdateTouchPoint(int pointerId, Point position)
    {
        if (_touchDevices.TryGetValue(pointerId, out var device))
        {
            device.UpdatePosition(position);
        }
    }

    /// <summary>Removes a touch contact from the active set.</summary>
    public static void UnregisterTouchPoint(int pointerId)
    {
        if (_touchDevices.Remove(pointerId, out TouchDevice? device))
            device.DeactivateForManager();
    }

    /// <summary>Looks up an active touch contact by id, or returns null.</summary>
    public static TouchDevice? GetDevice(int pointerId)
    {
        _touchDevices.TryGetValue(pointerId, out var device);
        return device;
    }
}

[SupportedOSPlatform("windows")]
internal static partial class NativeTouchInterop
{
    public const int SM_DIGITIZER = 94;
    public const int SM_MAXIMUMTOUCHES = 95;
    public const int NID_INTEGRATED_TOUCH = 0x01;
    public const int NID_EXTERNAL_TOUCH = 0x02;
    public const int NID_READY = 0x80;

    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    public static partial int GetSystemMetrics(int index);
}

/// <summary>
/// Re-export of the routed events for touch (used as <c>TouchEvents.TouchDownEvent</c> etc.).
/// </summary>
public static class TouchEvents
{
    public static readonly RoutedEvent TouchDownEvent =
        UIElement.TouchDownEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent TouchMoveEvent =
        UIElement.TouchMoveEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent TouchUpEvent =
        UIElement.TouchUpEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent TouchEnterEvent =
        UIElement.TouchEnterEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent TouchLeaveEvent =
        UIElement.TouchLeaveEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewTouchDownEvent =
        UIElement.PreviewTouchDownEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewTouchMoveEvent =
        UIElement.PreviewTouchMoveEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewTouchUpEvent =
        UIElement.PreviewTouchUpEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent GotTouchCaptureEvent =
        UIElement.GotTouchCaptureEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent LostTouchCaptureEvent =
        UIElement.LostTouchCaptureEvent.AddOwner(typeof(UIElement));
}
