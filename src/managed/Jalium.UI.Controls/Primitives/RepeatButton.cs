using System.Timers;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a button that raises the Click event repeatedly while it is pressed.
/// </summary>
public class RepeatButton : ButtonBase
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Delay dependency property.
    /// </summary>
    public static readonly DependencyProperty DelayProperty =
        DependencyProperty.Register(nameof(Delay), typeof(int), typeof(RepeatButton),
            new PropertyMetadata(SystemParameters.KeyboardDelay, OnDelayChanged));

    /// <summary>
    /// Identifies the Interval dependency property.
    /// </summary>
    public static readonly DependencyProperty IntervalProperty =
        DependencyProperty.Register(nameof(Interval), typeof(int), typeof(RepeatButton),
            new PropertyMetadata(SystemParameters.KeyboardSpeed, OnIntervalChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the amount of time, in milliseconds, that the RepeatButton waits while it is pressed
    /// before it starts repeating the Click event.
    /// </summary>
    public int Delay
    {
        get => (int)(GetValue(DelayProperty) ?? SystemParameters.KeyboardDelay);
        set => SetValue(DelayProperty, value);
    }

    /// <summary>
    /// Gets or sets the amount of time, in milliseconds, between repeats of the Click event
    /// after repeating starts.
    /// </summary>
    public int Interval
    {
        get => (int)(GetValue(IntervalProperty) ?? SystemParameters.KeyboardSpeed);
        set => SetValue(IntervalProperty, value);
    }

    #endregion

    #region Private Fields

    private System.Timers.Timer? _timer;
    private bool _isInDelay;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="RepeatButton"/> class.
    /// </summary>
    public RepeatButton()
    {
    }

    #endregion

    #region Timer Handling

    private void StartTimer()
    {
        if (_timer == null)
        {
            _timer = new System.Timers.Timer();
            _timer.Elapsed += OnTimerElapsed;
        }

        _isInDelay = true;
        _timer.Interval = Delay;
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _isInDelay = false;
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_timer == null) return;

        if (_isInDelay)
        {
            // Switch from delay to repeat interval
            _isInDelay = false;
            _timer.Interval = Interval;
        }

        // Raise Click event on the UI thread
        Dispatcher?.Invoke(() =>
        {
            if (IsPressed && IsEnabled)
            {
                OnClick();
            }
        });
    }

    #endregion

    #region Overrides

    /// <inheritdoc />
    protected override void OnIsPressedChanged(bool oldValue, bool newValue)
    {
        base.OnIsPressedChanged(oldValue, newValue);

        if (newValue)
        {
            // Start repeating
            StartTimer();
        }
        else
        {
            // Stop repeating
            StopTimer();
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnDelayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RepeatButton button && button._timer != null && button._isInDelay)
        {
            button._timer.Interval = (int)(e.NewValue ?? SystemParameters.KeyboardDelay);
        }
    }

    private static void OnIntervalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RepeatButton button && button._timer != null && !button._isInDelay)
        {
            button._timer.Interval = (int)(e.NewValue ?? SystemParameters.KeyboardSpeed);
        }
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var border = BorderThickness;
        var contentAvailable = new Size(
            Math.Max(0, availableSize.Width - padding.TotalWidth - border.TotalWidth),
            Math.Max(0, availableSize.Height - padding.TotalHeight - border.TotalHeight));

        var contentSize = MeasureContent(contentAvailable);

        return new Size(
            contentSize.Width + padding.TotalWidth + border.TotalWidth,
            contentSize.Height + padding.TotalHeight + border.TotalHeight);
    }

    private Size MeasureContent(Size availableSize)
    {
        if (Content is string text)
        {
            var fontFamily = FontFamily ?? "Segoe UI";
            var fontSize = FontSize > 0 ? FontSize : 14;
            var formattedText = new FormattedText(text, fontFamily, fontSize);
            TextMeasurement.MeasureText(formattedText);
            return new Size(formattedText.Width, formattedText.Height);
        }

        if (Content is UIElement element)
        {
            element.Measure(availableSize);
            return element.DesiredSize;
        }

        return new Size(0, 0);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);
        var cornerRadius = CornerRadius;
        var hasCornerRadius = cornerRadius.TopLeft > 0 || cornerRadius.TopRight > 0 ||
                              cornerRadius.BottomLeft > 0 || cornerRadius.BottomRight > 0;

        var bgBrush = Background;
        var borderBrush = BorderBrush;
        var fgBrush = Foreground;

        // Draw background
        if (bgBrush != null)
        {
            if (hasCornerRadius)
            {
                dc.DrawRoundedRectangle(bgBrush, null, rect, cornerRadius.TopLeft, cornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(bgBrush, null, rect);
            }
        }

        // Draw border
        if (borderBrush != null && BorderThickness.TotalWidth > 0)
        {
            var pen = new Pen(borderBrush, BorderThickness.Left);
            if (hasCornerRadius)
            {
                dc.DrawRoundedRectangle(null, pen, rect, cornerRadius.TopLeft, cornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(null, pen, rect);
            }
        }

        // Draw content
        if (Content is string text && fgBrush != null)
        {
            var formattedText = new FormattedText(text, FontFamily, FontSize)
            {
                Foreground = fgBrush
            };

            TextMeasurement.MeasureText(formattedText);
            var textX = (rect.Width - formattedText.Width) / 2;
            var textY = (rect.Height - formattedText.Height) / 2;

            dc.DrawText(formattedText, new Point(textX, textY));
        }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Releases the timer resources.
    /// </summary>
    ~RepeatButton()
    {
        _timer?.Dispose();
    }

    #endregion
}

/// <summary>
/// Provides access to system parameters for repeat button timing.
/// </summary>
internal static class SystemParameters
{
    /// <summary>
    /// Gets the keyboard delay in milliseconds (default: 500ms).
    /// </summary>
    public static int KeyboardDelay => 500;

    /// <summary>
    /// Gets the keyboard speed (repeat interval) in milliseconds (default: 33ms for ~30 repeats/sec).
    /// </summary>
    public static int KeyboardSpeed => 33;
}
