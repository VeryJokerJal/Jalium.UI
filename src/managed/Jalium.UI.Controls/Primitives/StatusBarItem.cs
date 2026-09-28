using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents an item in a StatusBar control.
/// </summary>
public class StatusBarItem : ContentControl
{
    private UIElement? _contentVisual;
    private Border? _cssBorderPainter;

    #region Static Brushes & Pens

    private static readonly SolidColorBrush s_defaultFgBrush = new(Color.White);
    private static readonly SolidColorBrush s_separatorBrush = new(Color.FromRgb(100, 100, 100));
    private static readonly Pen s_separatorPen = new(s_separatorBrush, 1);

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the internal separator-state dependency property.
    /// </summary>
    internal static readonly DependencyProperty SeparatorProperty =
        DependencyProperty.Register(nameof(Separator), typeof(bool), typeof(StatusBarItem),
            new PropertyMetadata(false, OnSeparatorChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether this item shows an internal separator.
    /// </summary>
    internal bool Separator
    {
        get => (bool)GetValue(SeparatorProperty)!;
        set => SetValue(SeparatorProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusBarItem"/> class.
    /// </summary>
    public StatusBarItem()
    {
        IsTabStop = false;
        UseTemplateContentManagement();
    }

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.StatusBarItemAutomationPeer(this);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        if (_contentVisual != null)
        {
            var visual = _contentVisual;
            _contentVisual = null;
            RemoveVisualChild(visual);
        }

        if (newContent is UIElement element)
        {
            _contentVisual = element;
            AddVisualChild(element);
        }

        InvalidateMeasure();
    }

    /// <inheritdoc />
    protected override int VisualChildrenCount => _contentVisual != null ? 1 : 0;

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
    {
        if (index == 0 && _contentVisual != null)
        {
            return _contentVisual;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var border = BorderThickness;
        var separatorWidth = Separator ? 9 : 0;

        if (Content is string text)
        {
            var fontFamily = FontFamily?.GetRenderingSource(this) ?? FrameworkElement.DefaultFontFamilyName;
            var fontSize = FontSize;
            var formattedText = new FormattedText(text, fontFamily, fontSize);
            TextMeasurement.MeasureText(formattedText);
            return new Size(
                formattedText.Width + padding.TotalWidth + border.TotalWidth + separatorWidth,
                Math.Max(24, formattedText.Height + padding.TotalHeight) + border.TotalHeight);
        }

        if (_contentVisual != null)
        {
            var contentAvailable = new Size(
                Math.Max(0, availableSize.Width - padding.TotalWidth - border.TotalWidth - separatorWidth),
                Math.Max(0, availableSize.Height - padding.TotalHeight - border.TotalHeight));
            _contentVisual.Measure(contentAvailable);
            return new Size(
                _contentVisual.DesiredSize.Width + padding.TotalWidth + border.TotalWidth + separatorWidth,
                Math.Max(24, _contentVisual.DesiredSize.Height + padding.TotalHeight) + border.TotalHeight);
        }

        return new Size(padding.TotalWidth + border.TotalWidth + separatorWidth,
            24 + border.TotalHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_contentVisual != null)
        {
            var padding = Padding;
            var border = BorderThickness;
            var separatorWidth = Separator ? 9 : 0;
            _contentVisual.Arrange(new Rect(
                border.Left + padding.Left,
                border.Top + padding.Top,
                Math.Max(0, finalSize.Width - border.TotalWidth - padding.TotalWidth - separatorWidth),
                Math.Max(0, finalSize.Height - border.TotalHeight - padding.TotalHeight)));
        }

        return finalSize;
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

        var rect = new Rect(RenderSize);
        var padding = Padding;
        var border = BorderThickness;

        // Draw background if set
        if (Background is { } background)
        {
            var radii = CssBorderRadiusProperties.Get(this)?.Resolve(RenderSize) ??
                CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(RenderSize);
            var shape = new CssRoundedRectangleGeometry(rect, radii);
            if (!CssBackgroundPainter.TryDraw(this, BackgroundProperty, background, dc,
                    rect, radii, border, padding,
                    brush => dc.DrawGeometry(brush, null, shape)))
                dc.DrawRectangle(background, null, rect);
        }

        // Draw content
        if (Content is string text && FontSize > 0)
        {
            var fgBrush = ResolveForegroundBrush();
            var formattedText = new FormattedText(text, FontFamily?.GetRenderingSource(this) ?? FrameworkElement.DefaultFontFamilyName, FontSize)
            {
                Foreground = fgBrush
            };
            TextMeasurement.MeasureText(formattedText);

            var textX = border.Left + padding.Left;
            var contentHeight = Math.Max(0, rect.Height - border.TotalHeight - padding.TotalHeight);
            var textY = border.Top + padding.Top + (contentHeight - formattedText.Height) / 2;
            dc.DrawText(formattedText, new Point(textX, textY));
        }

        // Draw separator
        if (Separator)
        {
            var separatorX = rect.Width - border.Right - 5;
            dc.DrawLine(s_separatorPen,
                new Point(separatorX, border.Top + 4),
                new Point(separatorX, rect.Height - border.Bottom - 4));
        }
    }

    /// <inheritdoc />
    protected override void OnPostRender(DrawingContext drawingContext)
    {
        base.OnPostRender(drawingContext);
        var border = BorderThickness;
        if (CssBorderPaintProperties.Get(this) is null && BorderBrush is { } brush && border.Left > 0)
        {
            var rect = ControlRenderGeometry.GetStrokeAlignedRect(new Rect(RenderSize), border.Left);
            var radius = ControlRenderGeometry.GetStrokeAlignedCornerRadius(CornerRadius, border.Left);
            drawingContext.DrawRoundedRectangle(null, new Pen(brush, border.Left), rect, radius);
        }
        CssBorderAdornment.Draw(this, drawingContext, ref _cssBorderPainter);
    }

    private Brush ResolveForegroundBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return TryFindResource("TextSecondary") as Brush
            ?? Foreground
            ?? s_defaultFgBrush;
    }

    private static void OnSeparatorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusBarItem item)
        {
            item.InvalidateMeasure();
            item.InvalidateVisual();
        }
    }

    #endregion
}
