using System.Diagnostics;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

using static Jalium.UI.Input.Cursors;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a button that displays as a hyperlink and can navigate to a URI.
/// </summary>
public class HyperlinkButton : ButtonBase
{
    private Border? _cssBorderPainter;
    protected override bool SupportsSelfDrawnCssContentAdapter => true;
    internal override Brush? ResolveCssAdapterForeground() => IsPressed
        ? ResolveHyperlinkBrush("HyperlinkForegroundPressed", s_hoverBrush)
        : _isHovered
            ? ResolveHyperlinkBrush("HyperlinkForegroundHover", s_hoverBrush)
            : Foreground ?? ResolveHyperlinkBrush("HyperlinkForeground", s_defaultBrush);
    internal override TextDecorationCollection? ResolveCssAdapterTextDecorations() =>
        IsUnderlined && GetValueSourceInternal(Styling.CssTextDecorationProperties.LineProperty).BaseValueSource == BaseValueSource.Default
            ? TextDecorations.Underline : null;

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.HyperlinkButtonAutomationPeer(this);
    }

    // Cached brushes for OnRender
    private static readonly SolidColorBrush s_defaultBrush = new(Color.FromRgb(0, 102, 204));
    private static readonly SolidColorBrush s_hoverBrush = new(Color.FromRgb(0, 51, 153));

    #region Dependency Properties

    /// <summary>
    /// Identifies the NavigateUri dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty NavigateUriProperty =
        DependencyProperty.Register(nameof(NavigateUri), typeof(Uri), typeof(HyperlinkButton),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the IsUnderlined dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsUnderlinedProperty =
        DependencyProperty.Register(nameof(IsUnderlined), typeof(bool), typeof(HyperlinkButton),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the URI to navigate to when the hyperlink is clicked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Uri? NavigateUri
    {
        get => (Uri?)GetValue(NavigateUriProperty);
        set => SetValue(NavigateUriProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the hyperlink text is underlined.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsUnderlined
    {
        get => (bool)GetValue(IsUnderlinedProperty)!;
        set => SetValue(IsUnderlinedProperty, value);
    }

    #endregion

    #region Private Fields

    private bool _isHovered;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="HyperlinkButton"/> class.
    /// </summary>
    public HyperlinkButton()
    {
        Cursor = Hand; // Hand cursor for clickable hyperlinks
    }

    #endregion

    #region Click Handling

    /// <inheritdoc />
    /// <summary>
    /// Allowed URI schemes for navigation. Only safe schemes are permitted.
    /// </summary>
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "ftp", "ftps", "mailto"
    };

    protected override void OnClick()
    {
        // Try to navigate to the URI if set
        if (NavigateUri != null)
        {
            try
            {
                // Only allow safe URI schemes to prevent execution of malicious URIs
                if (AllowedSchemes.Contains(NavigateUri.Scheme))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = NavigateUri.ToString(),
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception)
            {
                // Navigation failed - silently ignore
            }
        }

        base.OnClick();
    }

    /// <inheritdoc />
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        RefreshCssContentAdapterState();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        RefreshCssContentAdapterState();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        RefreshCssContentAdapterState();
        InvalidateVisual();
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureCssContentAdapter();

        // If using template, delegate to base class which handles template root
        if (Template != null)
        {
            return base.MeasureOverride(availableSize);
        }

        // Direct rendering fallback when no template
        var padding = Padding;

        // Negative Padding is legal; the Size constructor is not — clamp every
        // content+chrome summation sink.
        if (Content is string text)
        {
            var fontFamily = FontFamily?.GetRenderingSource(this) ?? FrameworkElement.DefaultFontFamilyName;
            var fontSize = FontSize;
            var formattedText = new FormattedText(text, fontFamily, fontSize);
            TextMeasurement.MeasureText(formattedText);
            return new Size(
                Math.Max(0, formattedText.Width + padding.TotalWidth),
                Math.Max(0, formattedText.Height + padding.TotalHeight));
        }

        if (Content is UIElement element)
        {
            element.Measure(availableSize);
            return new Size(
                Math.Max(0, element.DesiredSize.Width + padding.TotalWidth),
                Math.Max(0, element.DesiredSize.Height + padding.TotalHeight));
        }

        return new Size(Math.Max(0, padding.TotalWidth), Math.Max(0, padding.TotalHeight));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // If using template, delegate to base class
        if (Template != null)
        {
            return base.ArrangeOverride(finalSize);
        }

        // Direct rendering fallback
        return base.ArrangeOverride(finalSize);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (CssInlineBoxPainter.Background(this, BackgroundProperty, Background, drawingContext))
            return;

        // If using template, let the template handle rendering
        if (Template != null && !UsesCssContentAdapter)
        {
            return;
        }

        var dc = drawingContext;

        var rect = new Rect(RenderSize);
        var padding = Padding;

        // Draw background if set (usually null for hyperlinks)
        if (Background != null)
        {
            var radii = Styling.CssBorderRadiusProperties.Get(this)?.Resolve(RenderSize) ?? default;
            var shape = new Styling.CssRoundedRectangleGeometry(rect, radii);
            if (!CssBackgroundPainter.TryDraw(this, BackgroundProperty, Background, dc,
                    rect, radii, BorderThickness, padding,
                    brush => dc.DrawGeometry(brush, null, shape)))
                dc.DrawRectangle(Background, null, rect);
        }

        if (UsesCssContentAdapter)
        {
            return;
        }

        // Determine foreground color based on state
        var fgBrush = IsPressed
            ? ResolveHyperlinkBrush("HyperlinkForegroundPressed", s_hoverBrush)
            : _isHovered
                ? ResolveHyperlinkBrush("HyperlinkForegroundHover", s_hoverBrush)
                : Foreground ?? ResolveHyperlinkBrush("HyperlinkForeground", s_defaultBrush);

        // Draw content
        if (Content is string text && fgBrush != null && FontSize > 0)
        {
            var formattedText = new FormattedText(text, FontFamily.GetRenderingSource(this), FontSize)
            {
                Foreground = fgBrush
            };

            TextMeasurement.MeasureText(formattedText);
            var textX = padding.Left;
            var textY = padding.Top;

            dc.DrawText(formattedText, new Point(textX, textY));

            // Draw underline if enabled
            if (IsUnderlined)
            {
                var underlineY = textY + formattedText.Height - 1;
                var underlinePen = new Pen(fgBrush, 1);
                dc.DrawLine(underlinePen, new Point(textX, underlineY), new Point(textX + formattedText.Width, underlineY));
            }
        }
    }

    protected override void OnPostRender(DrawingContext drawingContext)
    {
        base.OnPostRender(drawingContext);
        if (CssInlineBoxPainter.Border(this, drawingContext, ref _cssBorderPainter))
            return;
        if (Template == null || UsesCssContentAdapter)
            CssBorderAdornment.Draw(this, drawingContext, ref _cssBorderPainter);
    }

    private SolidColorBrush ResolveHyperlinkBrush(string resourceKey, SolidColorBrush fallback)
    {
        return TryFindResource(resourceKey) as SolidColorBrush ?? fallback;
    }

    #endregion

    #region Property Changed Callbacks

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HyperlinkButton button)
        {
            button.RefreshCssContentAdapterState();
            button.InvalidateVisual();
        }
    }

    #endregion
}

