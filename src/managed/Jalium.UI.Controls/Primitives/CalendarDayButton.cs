using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a button for a single day in a Calendar control.
/// </summary>
public sealed class CalendarDayButton : Button
{
    #region Static Brushes & Pens

    private static readonly SolidColorBrush s_selectionBrush = new(Color.FromRgb(0, 120, 212));
    private static readonly SolidColorBrush s_highlightBrush = new(Color.FromRgb(60, 60, 60));
    private static readonly SolidColorBrush s_todayRingBrush = new(Color.FromRgb(0, 120, 212));
    private static readonly Pen s_todayPen = new(s_todayRingBrush, 2);
    private static readonly SolidColorBrush s_blackedOutFgBrush = new(Color.FromRgb(80, 80, 80));
    private static readonly SolidColorBrush s_inactiveFgBrush = new(Color.FromRgb(128, 128, 128));
    private static readonly SolidColorBrush s_selectedFgBrush = new(Color.White);
    private static readonly SolidColorBrush s_defaultFgBrush = new(Color.White);
    private static readonly SolidColorBrush s_strikeBrush = new(Color.FromRgb(150, 80, 80));
    private static readonly Pen s_strikePen = new(s_strikeBrush, 1);

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(CalendarDayButton),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsToday dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsTodayProperty =
        DependencyProperty.Register(nameof(IsToday), typeof(bool), typeof(CalendarDayButton),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsBlackedOut dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsBlackedOutProperty =
        DependencyProperty.Register(nameof(IsBlackedOut), typeof(bool), typeof(CalendarDayButton),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsInactive dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsInactiveProperty =
        DependencyProperty.Register(nameof(IsInactive), typeof(bool), typeof(CalendarDayButton),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsHighlighted dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(CalendarDayButton),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether this day is selected.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this day is today.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsToday
    {
        get => (bool)GetValue(IsTodayProperty)!;
        set => SetValue(IsTodayProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this day is blacked out (not selectable).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsBlackedOut
    {
        get => (bool)GetValue(IsBlackedOutProperty)!;
        set => SetValue(IsBlackedOutProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this day is from an adjacent month.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsInactive
    {
        get => (bool)GetValue(IsInactiveProperty)!;
        set => SetValue(IsInactiveProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this day is highlighted.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsHighlighted
    {
        get => (bool)GetValue(IsHighlightedProperty)!;
        set => SetValue(IsHighlightedProperty, value);
    }

    /// <summary>
    /// Gets or sets the Calendar that owns this button.
    /// </summary>
    public Calendar? Owner { get; internal set; }

    /// <summary>
    /// Gets or sets the date this button represents.
    /// </summary>
    public DateTime? Date { get; internal set; }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="CalendarDayButton"/> class.
    /// </summary>
    public CalendarDayButton()
    {
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        Width = 32;
        Height = 32;
    }

    #endregion

    #region Rendering

    private Brush ResolveSelectedBackgroundBrush()
    {
        if (HasLocalValue(Control.BackgroundProperty) && Background != null)
        {
            return Background;
        }

        return ResolveThemeBrush("AccentBrush", s_selectionBrush, "AccentFillColorDefaultBrush");
    }

    private Brush ResolveHighlightBackgroundBrush()
    {
        if (HasLocalValue(Control.BackgroundProperty) && Background != null)
        {
            return Background;
        }

        return ResolveThemeBrush("HighlightBackground", s_highlightBrush, "SubtleFillColorSecondaryBrush");
    }

    private Pen ResolveTodayRingPen()
    {
        if (HasLocalValue(Control.BorderBrushProperty) && BorderBrush != null)
        {
            var thickness = BorderThickness.Left > 0 ? BorderThickness.Left : s_todayPen.Thickness;
            return new Pen(BorderBrush, thickness);
        }

        return new Pen(ResolveThemeBrush("AccentBrush", s_todayRingBrush, "AccentFillColorDefaultBrush"), s_todayPen.Thickness);
    }

    private Brush ResolveBlackedOutForegroundBrush()
    {
        return GetLocalForegroundBrush()
            ?? ResolveThemeBrush("TextDisabled", s_blackedOutFgBrush, "TextFillColorDisabledBrush");
    }

    private Brush ResolveInactiveForegroundBrush()
    {
        return GetLocalForegroundBrush()
            ?? ResolveThemeBrush("TextSecondary", s_inactiveFgBrush, "TextFillColorSecondaryBrush");
    }

    private Brush ResolveSelectedForegroundBrush()
    {
        return GetLocalForegroundBrush()
            ?? ResolveThemeBrush("TextOnAccent", s_selectedFgBrush, "TextOnAccentFillColorPrimaryBrush");
    }

    private Brush ResolveDefaultForegroundBrush()
    {
        return GetLocalForegroundBrush()
            ?? ResolveThemeBrush("TextPrimary", s_defaultFgBrush, "TextFillColorPrimaryBrush");
    }

    private Pen ResolveStrikePen()
    {
        return new Pen(
            ResolveThemeBrush("TextDisabled", s_strikeBrush, "TextFillColorDisabledBrush"),
            s_strikePen.Thickness);
    }

    private Brush ResolveThemeBrush(string resourceKey, Brush fallback, string? secondaryResourceKey = null)
    {
        if (TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }

        if (secondaryResourceKey != null && TryFindResource(secondaryResourceKey) is Brush secondaryBrush)
        {
            return secondaryBrush;
        }

        return fallback;
    }

    private Brush? GetLocalForegroundBrush()
    {
        return HasLocalValue(Control.ForegroundProperty) ? Foreground : null;
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);
        var inset = new Rect(2, 2, rect.Width - 4, rect.Height - 4);

        // Draw selection background
        if (IsSelected)
        {
            dc.DrawEllipse(ResolveSelectedBackgroundBrush(), null, new Point(rect.Width / 2, rect.Height / 2),
                inset.Width / 2, inset.Height / 2);
        }
        else if (IsHighlighted || IsPressed)
        {
            dc.DrawEllipse(ResolveHighlightBackgroundBrush(), null, new Point(rect.Width / 2, rect.Height / 2),
                inset.Width / 2, inset.Height / 2);
        }

        // Draw today indicator (ring around the day)
        if (IsToday && !IsSelected)
        {
            dc.DrawEllipse(null, ResolveTodayRingPen(), new Point(rect.Width / 2, rect.Height / 2),
                inset.Width / 2 - 1, inset.Height / 2 - 1);
        }

        // Draw day number
        if (Content is string text)
        {
            Brush fgBrush;
            if (IsBlackedOut)
            {
                fgBrush = ResolveBlackedOutForegroundBrush();
            }
            else if (!IsEnabled || IsInactive)
            {
                fgBrush = ResolveInactiveForegroundBrush();
            }
            else if (IsSelected)
            {
                fgBrush = ResolveSelectedForegroundBrush();
            }
            else
            {
                fgBrush = ResolveDefaultForegroundBrush();
            }

            var formattedText = new FormattedText(text, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 14)
            {
                Foreground = fgBrush
            };
            TextMeasurement.MeasureText(formattedText);

            var textX = (rect.Width - formattedText.Width) / 2;
            var textY = (rect.Height - formattedText.Height) / 2;
            dc.DrawText(formattedText, new Point(textX, textY));
        }

        // Draw blacked out strikethrough
        if (IsBlackedOut)
        {
            dc.DrawLine(ResolveStrikePen(), new Point(4, rect.Height / 2), new Point(rect.Width - 4, rect.Height / 2));
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CalendarDayButton button)
        {
            button.InvalidateVisual();
        }
    }

    #endregion
}
