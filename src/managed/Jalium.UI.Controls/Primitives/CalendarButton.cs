using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a button used in Calendar controls for month and year selection.
/// </summary>
public sealed class CalendarButton : Button
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the HasSelectedDays read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey HasSelectedDaysPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasSelectedDays), typeof(bool), typeof(CalendarButton),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the HasSelectedDays dependency property.
    /// </summary>
    public static readonly DependencyProperty HasSelectedDaysProperty = HasSelectedDaysPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the IsInactive dependency property.
    /// </summary>
    public static readonly DependencyProperty IsInactiveProperty =
        DependencyProperty.Register(nameof(IsInactive), typeof(bool), typeof(CalendarButton),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets a value indicating whether the month/year contains selected days.
    /// </summary>
    public bool HasSelectedDays => (bool)GetValue(HasSelectedDaysProperty)!;

    /// <summary>
    /// Gets or sets a value indicating whether this button is for display purposes only.
    /// </summary>
    public bool IsInactive
    {
        get => (bool)GetValue(IsInactiveProperty)!;
        set => SetValue(IsInactiveProperty, value);
    }

    /// <summary>
    /// Gets or sets the Calendar that owns this button.
    /// </summary>
    public Calendar? Owner { get; internal set; }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="CalendarButton"/> class.
    /// </summary>
    public CalendarButton()
    {
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
    }

    #endregion

    #region Internal Methods

    /// <summary>
    /// Sets the HasSelectedDays property.
    /// </summary>
    internal void SetHasSelectedDays(bool value)
    {
        SetValue(HasSelectedDaysPropertyKey.DependencyProperty, value);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);

        // Draw background based on state
        Brush? bgBrush = null;
        if (HasSelectedDays)
        {
            bgBrush = new SolidColorBrush(Color.FromArgb(64, 0, 120, 212));
        }
        else if (IsPressed)
        {
            bgBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
        }
        else if (Background != null)
        {
            bgBrush = Background;
        }

        if (bgBrush != null)
        {
            dc.DrawRectangle(bgBrush, null, rect);
        }

        // Draw content
        if (Content is string text)
        {
            Brush fgBrush;
            if (!IsEnabled || IsInactive)
            {
                fgBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128));
            }
            else
            {
                fgBrush = Foreground ?? new SolidColorBrush(Color.White);
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
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CalendarButton button)
        {
            button.InvalidateVisual();
        }
    }

    #endregion
}
