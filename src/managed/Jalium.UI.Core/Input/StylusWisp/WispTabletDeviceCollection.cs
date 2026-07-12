namespace Jalium.UI.Input.StylusWisp;

/// <summary>
/// Windows Ink Services Platform tablet collection. Jalium's pointer pipeline
/// already normalizes WISP, WM_POINTER and non-Windows devices into
/// <see cref="TabletDeviceCollection"/>, so this compatibility type exposes the
/// same live collection contract without starting a second input stack.
/// </summary>
public class WispTabletDeviceCollection : TabletDeviceCollection
{
    internal WispTabletDeviceCollection()
    {
    }
}
