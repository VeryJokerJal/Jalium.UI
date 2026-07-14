using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents an item in a StatusBar control.
/// </summary>
public class StatusBarItem : ContentControl
{
    private UIElement? _contentVisual;

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
            RemoveVisualChild(_contentVisual);
            _contentVisual = null;
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
        var separatorWidth = Separator ? 9 : 0;

        if (Content is string text)
        {
            var fontFamily = FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName;
            var fontSize = FontSize > 0 ? FontSize : 12;
            var formattedText = new FormattedText(text, fontFamily, fontSize);
            TextMeasurement.MeasureText(formattedText);
            return new Size(
                formattedText.Width + padding.TotalWidth + separatorWidth,
                Math.Max(24, formattedText.Height + padding.TotalHeight));
        }

        if (_contentVisual != null)
        {
            var contentAvailable = new Size(
                Math.Max(0, availableSize.Width - padding.TotalWidth - separatorWidth),
                Math.Max(0, availableSize.Height - padding.TotalHeight));
            _contentVisual.Measure(contentAvailable);
            return new Size(
                _contentVisual.DesiredSize.Width + padding.TotalWidth + separatorWidth,
                Math.Max(24, _contentVisual.DesiredSize.Height + padding.TotalHeight));
        }

        return new Size(padding.TotalWidth + separatorWidth, 24);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_contentVisual != null)
        {
            var padding = Padding;
            var separatorWidth = Separator ? 9 : 0;
            _contentVisual.Arrange(new Rect(
                padding.Left,
                padding.Top,
                Math.Max(0, finalSize.Width - padding.TotalWidth - separatorWidth),
                Math.Max(0, finalSize.Height - padding.TotalHeight)));
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

        // Draw background if set
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, rect);
        }

        // Draw content
        if (Content is string text)
        {
            var fgBrush = ResolveForegroundBrush();
            var formattedText = new FormattedText(text, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 12)
            {
                Foreground = fgBrush
            };
            TextMeasurement.MeasureText(formattedText);

            var textX = padding.Left;
            var textY = (rect.Height - formattedText.Height) / 2;
            dc.DrawText(formattedText, new Point(textX, textY));
        }

        // Draw separator
        if (Separator)
        {
            var separatorX = rect.Width - 5;
            dc.DrawLine(s_separatorPen, new Point(separatorX, 4), new Point(separatorX, rect.Height - 4));
        }
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
