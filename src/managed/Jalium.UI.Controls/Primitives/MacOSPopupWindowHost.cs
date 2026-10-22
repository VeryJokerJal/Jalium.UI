using Jalium.UI.Media;
using Jalium.UI.Data;

namespace Jalium.UI.Controls.Primitives;

/// <summary>Uses the ordinary Window renderer, native text input and accessibility bridge.</summary>
internal class MacOSPopupWindowHost : Window
{
    private readonly Popup _popup;
    private readonly double _initialDpiScale;
    private Point? _startupPixelPosition;
    internal MacOSPopupWindowHost(Window parent, Popup popup, PopupRoot root)
    {
        _popup = popup;
        _initialDpiScale = parent.DpiScale;
        ConfigureMacOSPopupHost(parent, popup, root);
        WindowStyle = WindowStyle.None;
        TitleBarStyle = WindowTitleBarStyle.Native;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        IsShowCloseButton = IsShowMinimizeButton = IsShowMaximizeButton = false;
        IsShowTitleBar = false;
        IsHitTestVisible = popup.IsHitTestVisible;
        Style = new Style(typeof(MacOSPopupWindowHost));
        Template = null;
        BindOwnerTextProperties(parent);
        Content = root;
    }

    internal void BindOwnerTextProperties(Window parent)
    {
        foreach (var (property, name) in new[] {
            (FontFamilyProperty, nameof(FontFamily)), (FontSizeProperty, nameof(FontSize)),
            (FontWeightProperty, nameof(FontWeight)), (FontStyleProperty, nameof(FontStyle)),
            (FontStretchProperty, nameof(FontStretch)), (ForegroundProperty, nameof(Foreground)),
            (FlowDirectionProperty, nameof(FlowDirection)), (LanguageProperty, nameof(Language)) })
            SetBinding(property, new Binding(name) { Source = parent, Mode = BindingMode.OneWay });
    }

    public override void Close()
    {
        if (_popup.IsMacOSExternalHostCurrent(this)) _popup.SetCurrentValue(Popup.IsOpenProperty, false);
        else base.Close();
    }

    internal virtual void ShowAt(Point screenPosition, Size size)
    {
        MoveTo(screenPosition, size);
        Show();
    }

    internal override Point? GetMacOSStartupPixelPosition() => _startupPixelPosition;

    // Screen positions remain physical pixels across the owner/host DPI boundary.
    internal virtual void MoveTo(Point screenPosition, Size size)
    {
        _startupPixelPosition = screenPosition;
        double scale = Handle == nint.Zero ? _initialDpiScale : DpiScale;
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        Left = screenPosition.X / scale; Top = screenPosition.Y / scale;
        Width = Math.Max(1, Math.Ceiling(size.Width * scale) / scale);
        Height = Math.Max(1, Math.Ceiling(size.Height * scale) / scale);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsHitTestVisibleProperty) RefreshMacOSPopupStyle();
    }
}
