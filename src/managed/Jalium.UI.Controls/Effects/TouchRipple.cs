using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.Effects;

/// <summary>
/// Material-style ripple feedback. Listens on every element whose
/// <see cref="TouchHelper.IsRippleEnabled"/> attached property is true and on
/// each <c>PreviewTouchDown</c> / <c>PreviewMouseDown</c> spawns a
/// <see cref="TouchRippleAdorner"/> on the host <see cref="AdornerLayer"/>.
/// The adorner animates outward over ~400 ms and removes itself.
/// </summary>
internal static class TouchRippleHost
{
    private const int MaxConcurrentRipples = 8;
    private static int s_activeRippleCount;

    /// <summary>
    /// Wired up from <see cref="TouchHelper.IsRippleEnabledProperty"/> via the
    /// changed callback — see <see cref="TouchRippleRegistration"/> below.
    /// </summary>
    internal static void EnableRipple(UIElement element)
    {
        element.AddHandler(UIElement.PreviewTouchDownEvent, new RoutedEventHandler(OnDown), handledEventsToo: true);
        element.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnMouseDown), handledEventsToo: true);
    }

    internal static void DisableRipple(UIElement element)
    {
        element.RemoveHandler(UIElement.PreviewTouchDownEvent, new RoutedEventHandler(OnDown));
        element.RemoveHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnMouseDown));
    }

    private static void OnDown(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement element) return;
        if (!TouchHelper.GetIsRippleEnabled(element)) return;
        if (!TouchHelper.GetIsTouchInteractive(element)) return;
        if (s_activeRippleCount >= MaxConcurrentRipples) return;

        Point center;
        if (e is TouchEventArgs touchArgs)
        {
            center = touchArgs.GetTouchPoint(element).Position;
        }
        else
        {
            // Fallback to element center.
            center = new Point(element.RenderSize.Width / 2, element.RenderSize.Height / 2);
        }
        StartRipple(element, center);
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement element) return;
        if (!TouchHelper.GetIsRippleEnabled(element)) return;
        if (!TouchHelper.GetIsTouchInteractive(element)) return;
        if (s_activeRippleCount >= MaxConcurrentRipples) return;
        StartRipple(element, e.GetPosition(element));
    }

    private static void StartRipple(UIElement element, Point center)
    {
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer == null) return;
        var brush = TouchHelper.GetRippleBrush(element) ?? GetDefaultRippleBrush(element);
        var adorner = new TouchRippleAdorner(element, center, brush);
        layer.Add(adorner);
        s_activeRippleCount++;
        adorner.Completed += (_, _) =>
        {
            layer.Remove(adorner);
            if (s_activeRippleCount > 0) s_activeRippleCount--;
        };
        adorner.Start();
    }

    private static Brush GetDefaultRippleBrush(UIElement element)
    {
        // Approximate "ink-on-light-button" feedback: 24% black overlay.
        return new SolidColorBrush(Color.FromArgb(0x3D, 0, 0, 0));
    }
}

/// <summary>
/// Adorner that draws a single expanding circular ripple over its adorned element.
/// Driven by a <see cref="DispatcherTimer"/> on the UI thread; 16-ms interval
/// piggybacks on <c>CompositionTarget.Rendering</c>.
/// </summary>
internal sealed class TouchRippleAdorner : Adorner
{
    private const double DurationMs = 400.0;
    private const double PeakOpacity = 1.0;
    private const double InitialAlpha = 0.35;

    private readonly Point _center;
    private readonly Brush _brush;
    private readonly DispatcherTimer _timer;
    private long _startTicks;
    private double _progress;
    private double _maxRadius;

    public event EventHandler? Completed;

    public TouchRippleAdorner(UIElement adornedElement, Point center, Brush brush)
        : base(adornedElement)
    {
        _center = center;
        _brush = brush;
        IsHitTestVisible = false;
        IsClipEnabled = true;
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            OnTick,
            Dispatcher.CurrentDispatcher ?? Dispatcher.GetForCurrentThread())
        {
            IsEnabled = false
        };
    }

    public void Start()
    {
        var size = AdornedElement.RenderSize;
        // Distance to the farthest corner.
        double cw = Math.Max(_center.X, size.Width - _center.X);
        double ch = Math.Max(_center.Y, size.Height - _center.Y);
        _maxRadius = Math.Sqrt(cw * cw + ch * ch);
        _startTicks = Environment.TickCount64;
        _progress = 0;
        _timer.IsEnabled = true;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double elapsed = Environment.TickCount64 - _startTicks;
        _progress = Math.Clamp(elapsed / DurationMs, 0, 1);
        InvalidateVisual();
        if (_progress >= 1.0)
        {
            _timer.IsEnabled = false;
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override Size MeasureOverride(Size constraint) => AdornedElement.RenderSize;
    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_progress >= 1.0 || _maxRadius <= 0) return;

        double radius = _maxRadius * _progress;
        double alpha = InitialAlpha * (1.0 - _progress);
        if (alpha <= 0) return;

        // Wrap the brush with an opacity layer so the existing brush colour is preserved.
        drawingContext.PushOpacity(alpha);
        try
        {
            drawingContext.DrawEllipse(_brush, null, _center, radius, radius);
        }
        finally
        {
            drawingContext.Pop();
        }
    }
}

/// <summary>
/// Hooks <see cref="TouchHelper.IsRippleEnabledProperty"/> changes so that
/// setting <c>true</c> attaches the ripple host listeners and <c>false</c> detaches them.
/// </summary>
internal static class TouchRippleRegistration
{
    private static int s_registered;

    public static void Register()
    {
        if (System.Threading.Interlocked.Exchange(ref s_registered, 1) == 1) return;
        TouchHelper.IsRippleEnabledProperty.OverrideMetadata(typeof(UIElement),
            new PropertyMetadata(false, OnRippleEnabledChanged));
    }

    private static void OnRippleEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        if ((bool)e.NewValue!)
            TouchRippleHost.EnableRipple(element);
        else
            TouchRippleHost.DisableRipple(element);
    }
}
