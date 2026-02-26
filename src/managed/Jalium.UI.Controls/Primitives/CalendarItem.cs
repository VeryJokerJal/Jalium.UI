using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents the visual container for the Calendar control's current display mode.
/// </summary>
public sealed class CalendarItem : Control
{
    #region Static Brushes

    private static readonly SolidColorBrush s_defaultBackgroundBrush = new(Color.FromRgb(45, 45, 45));
    private static readonly SolidColorBrush s_dayOfWeekHeaderBrush = new(Color.FromRgb(160, 160, 160));

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the DisplayMode dependency property.
    /// </summary>
    public static readonly DependencyProperty DisplayModeProperty =
        DependencyProperty.Register(nameof(DisplayMode), typeof(CalendarMode), typeof(CalendarItem),
            new PropertyMetadata(CalendarMode.Month, OnDisplayModeChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the current display mode.
    /// </summary>
    public CalendarMode DisplayMode
    {
        get => (CalendarMode)GetValue(DisplayModeProperty)!;
        set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the Calendar that owns this item.
    /// </summary>
    public Calendar? Owner { get; internal set; }

    /// <summary>
    /// Gets the header button.
    /// </summary>
    public Button? HeaderButton { get; private set; }

    /// <summary>
    /// Gets the previous button.
    /// </summary>
    public Button? PreviousButton { get; private set; }

    /// <summary>
    /// Gets the next button.
    /// </summary>
    public Button? NextButton { get; private set; }

    /// <summary>
    /// Gets the month view grid.
    /// </summary>
    public Grid? MonthView { get; private set; }

    /// <summary>
    /// Gets the year view grid.
    /// </summary>
    public Grid? YearView { get; private set; }

    #endregion

    #region Private Fields

    private const int DaysPerWeek = 7;
    private const int RowsInMonthView = 6;
    private const int MonthsPerRow = 4;
    private const int RowsInYearView = 3;
    private const double HeaderHeight = 32;
    private const double DayHeaderHeight = 24;
    private const double CellSize = 32;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="CalendarItem"/> class.
    /// </summary>
    public CalendarItem()
    {
        CreateVisualTree();
    }

    private void CreateVisualTree()
    {
        // Create header button
        HeaderButton = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Height = HeaderHeight,
            Background = new SolidColorBrush(Color.Transparent)
        };
        HeaderButton.Click += OnHeaderButtonClick;

        // Create navigation buttons
        PreviousButton = new Button
        {
            Content = "<",
            Width = 28,
            Height = HeaderHeight,
            Background = new SolidColorBrush(Color.Transparent)
        };
        PreviousButton.Click += OnPreviousButtonClick;

        NextButton = new Button
        {
            Content = ">",
            Width = 28,
            Height = HeaderHeight,
            Background = new SolidColorBrush(Color.Transparent)
        };
        NextButton.Click += OnNextButtonClick;

        // Add as visual children
        AddVisualChild(PreviousButton);
        AddVisualChild(HeaderButton);
        AddVisualChild(NextButton);
    }

    #endregion

    #region Visual Children

    /// <inheritdoc />
    public override int VisualChildrenCount
    {
        get
        {
            var count = 3; // Header, Previous, Next buttons
            if (MonthView != null) count++;
            if (YearView != null) count++;
            return count;
        }
    }

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        return index switch
        {
            0 => PreviousButton,
            1 => HeaderButton,
            2 => NextButton,
            3 => DisplayMode == CalendarMode.Month ? MonthView : YearView,
            4 => DisplayMode == CalendarMode.Month ? null : MonthView,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = CellSize * DaysPerWeek;
        var height = HeaderHeight + DayHeaderHeight + CellSize * RowsInMonthView;

        PreviousButton?.Measure(new Size(28, HeaderHeight));
        NextButton?.Measure(new Size(28, HeaderHeight));
        HeaderButton?.Measure(new Size(width - 56, HeaderHeight));
        MonthView?.Measure(new Size(width, CellSize * RowsInMonthView));
        YearView?.Measure(new Size(width, CellSize * RowsInYearView));

        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = finalSize.Width;

        PreviousButton?.Arrange(new Rect(0, 0, 28, HeaderHeight));
        HeaderButton?.Arrange(new Rect(28, 0, width - 56, HeaderHeight));
        NextButton?.Arrange(new Rect(width - 28, 0, 28, HeaderHeight));

        var contentY = HeaderHeight + DayHeaderHeight;
        MonthView?.Arrange(new Rect(0, contentY, width, CellSize * RowsInMonthView));
        YearView?.Arrange(new Rect(0, contentY, width, CellSize * RowsInYearView));

        return finalSize;
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);

        // Draw background
        var bgBrush = Background ?? s_defaultBackgroundBrush;
        dc.DrawRectangle(bgBrush, null, rect);

        // Draw day of week headers if in month mode
        if (DisplayMode == CalendarMode.Month)
        {
            DrawDayOfWeekHeaders(dc);
        }
    }

    private void DrawDayOfWeekHeaders(DrawingContext dc)
    {
        var fgBrush = s_dayOfWeekHeaderBrush;
        var dayNames = new[] { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };

        for (var i = 0; i < DaysPerWeek; i++)
        {
            var formattedText = new FormattedText(dayNames[i], FontFamily ?? "Segoe UI", 12)
            {
                Foreground = fgBrush
            };
            TextMeasurement.MeasureText(formattedText);

            var x = i * CellSize + (CellSize - formattedText.Width) / 2;
            var y = HeaderHeight + (DayHeaderHeight - formattedText.Height) / 2;
            dc.DrawText(formattedText, new Point(x, y));
        }
    }

    #endregion

    #region Event Handlers

    private void OnHeaderButtonClick(object sender, RoutedEventArgs e)
    {
        // Toggle between month and year view
        DisplayMode = DisplayMode == CalendarMode.Month
            ? CalendarMode.Year
            : CalendarMode.Month;
    }

    private void OnPreviousButtonClick(object sender, RoutedEventArgs e)
    {
        // Navigate to previous period - would be implemented by Calendar
        InvalidateVisual();
    }

    private void OnNextButtonClick(object sender, RoutedEventArgs e)
    {
        // Navigate to next period - would be implemented by Calendar
        InvalidateVisual();
    }

    private static void OnDisplayModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CalendarItem item)
        {
            item.InvalidateMeasure();
            item.InvalidateVisual();
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Updates the header text.
    /// </summary>
    /// <param name="text">The new header text.</param>
    public void UpdateHeader(string text)
    {
        if (HeaderButton != null)
        {
            HeaderButton.Content = text;
        }
    }

    #endregion
}

/// <summary>
/// Specifies the display mode of a Calendar.
/// </summary>
public enum CalendarMode
{
    /// <summary>
    /// Display days of a month.
    /// </summary>
    Month,

    /// <summary>
    /// Display months of a year.
    /// </summary>
    Year,

    /// <summary>
    /// Display years of a decade.
    /// </summary>
    Decade
}
