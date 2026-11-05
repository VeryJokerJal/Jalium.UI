namespace Jalium.UI;

public partial class FrameworkElement
{
    // Dirty registration can run off-dispatcher; only its mirrored ink extent is read there.
    internal bool CssBorderImageInkEnabled;
    internal FrameworkElement? CssBorderImageTemplatePaintOwner;
    internal FrameworkElement? CssBorderImageTemplateInkOwner;
    private double _cssBorderImageInk;
    internal double CssBorderImageInkPadding()
    {
        if (CssBorderImageTemplateInkOwner is { } templateOwner) return templateOwner.CssBorderImageInkPadding();
        if (Dispatcher.CheckAccess())
        {
            _cssBorderImageInk = 0;
            if (CssBorderImageInkEnabled)
            {
                var border = Styling.CssBoxMetrics.BackgroundInsets(this, CssLayout?.ContainingWidthCache ?? RenderSize.Width).Border;
                var outset = Styling.CssBorderImageProperties.Outset(this, border);
                _cssBorderImageInk = Math.Max(Math.Max(outset.Left, outset.Right), Math.Max(outset.Top, outset.Bottom));
            }
            if (this is Controls.TextBlock text)
                _cssBorderImageInk = Math.Max(_cssBorderImageInk, text.DocumentBorderImageInkPadding());
        }
        return _cssBorderImageInk;
    }
    internal void SetCssBorderImageInk(bool enabled)
    {
        CssBorderImageInkEnabled = enabled;
        if (!enabled) _cssBorderImageInk = 0;
        else CssBorderImageInkPadding();
    }
}
