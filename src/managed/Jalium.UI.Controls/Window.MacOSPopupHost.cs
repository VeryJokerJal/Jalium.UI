using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI;

public partial class Window
{
    private Window? _macOSPopupParent;
    private Popup? _macOSHostedPopup;
    private PopupRoot? _macOSHostedPopupRoot;
    private readonly HashSet<Window> _macOSPopupHosts = [];
    private bool _macOSPopupNativeDestroyed;

    internal bool IsMacOSPopupHost => _macOSPopupParent != null;
    internal bool CanCreateMacOSNativePopup => OperatingSystem.IsMacOS() && Handle != nint.Zero &&
        _platformWindow is Controls.Platform.NativePlatformWindow platform && platform.NativeHandle == Handle;
    internal Window MacOSInputOwner => _macOSPopupParent?.MacOSInputOwner ?? this;
    internal ContentDialog? MacOSInputDialog => MacOSInputOwner.ActiveContentDialog;
    internal IEnumerable<Popup> MacOSExternalPopupGroup =>
        MacOSInputOwner._macOSPopupHosts.Select(h => h._macOSHostedPopup).OfType<Popup>().ToArray();
    internal bool IsMacOSPopupHostCurrent => !IsMacOSPopupHost ||
        (_macOSHostedPopup?.IsMacOSExternalHostCurrent(this) == true &&
         _macOSPopupParent!.IsMacOSPopupInputOwnerAlive);
    internal nint MacOSPopupParentHandle => _macOSPopupParent?.Handle ?? nint.Zero;

    internal void ConfigureMacOSPopupHost(Window parent, Popup popup, PopupRoot root)
    {
        if (_macOSPopupParent != null) throw new InvalidOperationException("Popup host is already configured.");
        _macOSPopupParent = parent;
        _macOSHostedPopup = popup;
        _macOSHostedPopupRoot = root;
        parent.MacOSInputOwner._macOSPopupHosts.Add(this);
    }

    internal bool ReparentMacOSPopupHost(Window parent)
    {
        if (!ReferenceEquals(parent.MacOSInputOwner, MacOSInputOwner) || !parent.IsMacOSPopupInputOwnerAlive) return false;
        for (Window? node = parent; node != null; node = node._macOSPopupParent)
            if (ReferenceEquals(node, this)) return false;
        var previous = _macOSPopupParent;
        _macOSPopupParent = parent;
        if (_platformWindow != null && !_platformWindow.SetOwner(parent.Handle))
        { _macOSPopupParent = previous; return false; }
        if (this is MacOSPopupWindowHost host) host.BindOwnerTextProperties(parent);
        return !_isClosing && IsMacOSPopupHostCurrent;
    }

    internal void CloseMacOSPopupHost()
    {
        if (!IsMacOSPopupHost) return;
        Content = null;
        var owner = MacOSInputOwner;
        _macOSNativeIsKey = false;
        owner._macOSPopupHosts.Remove(this);
        owner.QueueMacOSPopupGroupDeactivation();
        _isClosing = true;
        CompleteManagedTeardown(Handle, _macOSPopupNativeDestroyed);
    }

    private uint ApplyMacOSPopupStyle(uint style) => !IsMacOSPopupHost ? style :
        (style & ~(2u | 4u | 8u | 16u | 32u | 64u)) | 1u | 128u | 256u |
        (IsHitTestVisible ? 0u : 1024u);

    internal void RefreshMacOSPopupStyle()
    {
        if (IsMacOSPopupHost) UpdateWindowStyle();
    }

    internal void RetireMacOSPopupPointerInput(PopupRoot root, Func<bool> isCurrent) =>
        _inputDispatcher.HandleSubtreeDetached(IsMacOSPopupHost ? this : root,
            preserveKeyboardPressedState: true, isCurrent: isCurrent);

    internal static Window? GetMacOSPhysicalWindow(Visual? element)
    {
        int depth = 0;
        for (var current = element; current != null && depth++ < 4096; current = current.VisualParent)
            if (current is Window window) return window;
        return null;
    }

    internal virtual Point? GetMacOSStartupPixelPosition() => null;

    internal Point GetMacOSClientScreenOrigin()
    {
        Point origin = GetMacOSClientPixelOrigin();
        return new Point(origin.X / DpiScale, origin.Y / DpiScale);
    }

    private Matrix GetMacOSInputTransform(UIElement target)
    {
        Matrix transform = target.GetRenderMatrix();
        var physical = GetMacOSPhysicalWindow(target);
        if (physical == null || ReferenceEquals(physical, this)) return transform;
        Matrix windows = GetMacOSWindowInputTransform(physical);
        return new Matrix(transform.M11 * windows.M11, transform.M12 * windows.M22,
            transform.M21 * windows.M11, transform.M22 * windows.M22,
            transform.OffsetX * windows.M11 + windows.OffsetX,
            transform.OffsetY * windows.M22 + windows.OffsetY);
    }
    internal static Rect GetMacOSMonitorWorkArea(Point point, double coordinateScale)
    {
        var monitors = new List<Interop.NativeMethods.NativeMonitorInfo>();
        int count = Interop.NativeMethods.PlatformGetMonitorCount();
        for (int i = 0; i < count; i++)
            if (Interop.NativeMethods.PlatformGetMonitorInfo(i, out var info) == 0) monitors.Add(info);
        return GetMacOSMonitorWorkArea(point, coordinateScale, monitors);
    }

    internal static Rect GetMacOSMonitorWorkArea(Point point, double coordinateScale,
        IReadOnlyList<Interop.NativeMethods.NativeMonitorInfo> monitors)
    {
        double scale = double.IsFinite(coordinateScale) && coordinateScale > 0 ? coordinateScale : 1;
        var physical = new Point(point.X * scale, point.Y * scale);
        Rect selected = new(point, new Size(1, 1));
        double distance = double.PositiveInfinity;
        foreach (var info in monitors)
        {
            if (info.Width <= 0 || info.Height <= 0 || info.WorkWidth <= 0 || info.WorkHeight <= 0) continue;
            // Both the anchor and monitor rectangles now use the native screen
            // coordinate space. Per-monitor backing scales describe local DIPs;
            // they must not rescale the shared desktop origin independently.
            var screen = new Rect(info.X, info.Y, info.Width, info.Height);
            double dx = Math.Max(screen.Left - physical.X, Math.Max(0, physical.X - screen.Right));
            double dy = Math.Max(screen.Top - physical.Y, Math.Max(0, physical.Y - screen.Bottom));
            double candidateDistance = dx * dx + dy * dy;
            if (candidateDistance >= distance) continue;
            distance = candidateDistance;
            selected = new Rect(info.WorkX / scale, info.WorkY / scale, info.WorkWidth / scale, info.WorkHeight / scale);
        }
        return selected;
    }

    private Point GetMacOSClientPixelOrigin()
    {
        if (TryGetClientOriginOnScreen(out var origin)) return origin;
        return new Point((double.IsFinite(Left) ? Left : 0) * DpiScale,
            (double.IsFinite(Top) ? Top : 0) * DpiScale);
    }

    private Matrix GetMacOSWindowInputTransform(Window source)
    {
        // Client origins share physical screen pixels; each window's local
        // coordinates use its own DIP scale, including the text transform.
        Point from = source.GetMacOSClientPixelOrigin(), to = GetMacOSClientPixelOrigin();
        double scale = source.DpiScale / DpiScale;
        return new Matrix(scale, 0, 0, scale, (from.X - to.X) / DpiScale, (from.Y - to.Y) / DpiScale);
    }

    private Point GetMacOSOwnerInputPoint(Point position) =>
        MacOSInputOwner.GetMacOSWindowInputTransform(this).Transform(position);

    private IReadOnlyList<Popup> GetMacOSMousePopupGroup()
    {
        var owner = MacOSInputOwner;
        return owner._macOSPopupHosts.Append(owner)
            .SelectMany(w => w.OverlayLayer.Children.OfType<PopupRoot>().Select(r => r.OwnerPopup))
            .Concat(owner.MacOSExternalPopupGroup).Where(p => p.IsOpen).Distinct().ToArray();
    }

    private static bool TryDismissMacOSFocusedPopup(UIElement target)
    {
        int depth = 0;
        for (Visual? node = target; node != null && depth++ < 4096; node = InputRoute.GetParent(node))
            if (node is PopupRoot root && root.OwnerPopup is { IsOpen: true, StaysOpen: false } popup)
            {
                popup.SetCurrentValue(Popup.IsOpenProperty, false);
                return true;
            }
        return false;
    }

}
