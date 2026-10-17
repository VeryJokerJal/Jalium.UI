using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

public partial class Popup
{
    private MacOSPopupWindowHost? _macOSPopupWindow;
    internal virtual bool CanCreateMacOSPopupHost => _parentWindow?.CanCreateMacOSNativePopup == true;
    internal virtual MacOSPopupWindowHost CreateMacOSPopupHost(Window owner, PopupRoot root) => new(owner, this, root);
    internal bool IsMacOSExternalHostCurrent(Window host) => IsOpen && _isUsingExternalWindow &&
        _popupRoot != null && ReferenceEquals(host, _macOSPopupWindow) && ReferenceEquals(host.Content, _popupRoot);

    private void OpenAsMacOSExternalWindow(Point screenPosition, Size popupSize)
    {
        var root = _popupRoot!;
        var parent = _parentWindow!;
        var host = CreateMacOSPopupHost(parent, root);
        _macOSPopupWindow = host;
        if (!parent.ActiveExternalPopups.Contains(this)) parent.ActiveExternalPopups.Add(this);
        host.Closed += (_, _) =>
        {
            if (ReferenceEquals(_popupRoot, root) && ReferenceEquals(_macOSPopupWindow, host))
                SetCurrentValue(IsOpenProperty, false);
        };
        try
        {
            // Native hosting supplies inherited fonts and owner resources. Measure
            // in that final context before publishing the first native frame.
            PreparePopupSubtree(Child!);
            InvalidateSubtree(root);
            popupSize = MeasurePopupChild(Child!);
            root.Width = popupSize.Width; root.Height = popupSize.Height;
            screenPosition = ApplyScreenAutoFlip(WindowLocalToScreen(CalculateWindowLocalPosition(popupSize)), popupSize);
            host.ShowAt(new Point(screenPosition.X / parent.DpiScale, screenPosition.Y / parent.DpiScale), popupSize);
        }
        catch
        {
            SetCurrentValue(IsOpenProperty, false);
            throw;
        }
    }

    internal virtual Rect GetMacOSPopupWorkArea()
    {
        var owner = _parentWindow!;
        var point = owner.GetMacOSClientScreenOrigin();
        if (owner.Handle == nint.Zero)
            return new Rect(point, new Size(Math.Max(1, owner.ActualWidth), Math.Max(1, owner.ActualHeight)));
        var anchor = GetPlacementBounds(PlacementTarget ?? this);
        point = new Point(point.X + anchor.X, point.Y + anchor.Y);
        return Window.GetMacOSMonitorWorkArea(point);
    }
}
