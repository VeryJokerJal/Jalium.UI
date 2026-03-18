using System.Diagnostics;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.DevTools;

/// <summary>
/// Provides visual overlay functionality for highlighting elements in the DevTools.
/// Uses an animated glowing border effect with a trailing glow that follows the element perimeter.
/// </summary>
internal sealed class DevToolsOverlay
{
    private readonly Window _targetWindow;
    private FrameworkElement? _highlightedElement;
    private readonly SolidColorBrush _marginBrush;
    private readonly SolidColorBrush _paddingBrush;

    // Animation state
    private readonly Stopwatch _animationStopwatch;
    private DispatcherTimer? _animationTimer;
    private const double AnimationDurationMs = 1500.0; // Full cycle duration in milliseconds (iOS-style smooth)

    // Transition animation state (element change)
    private Rect? _previousBounds;
    private Rect? _targetBounds;
    private readonly Stopwatch _transitionStopwatch = new();
    private const double TransitionDurationMs = 350.0; // Transition duration
    private float _headProgress; // 0-1, head position progress
    private float _tailProgress; // 0-1, tail position progress (lags behind head)
    private bool _isTransitioning;

    // Ripple animation state (plays after transition ends)
    private readonly Stopwatch _rippleStopwatch = new();
    private const double RippleDurationMs = 400.0; // Ripple expansion duration
    private float _rippleProgress; // 0-1, ripple expansion progress
    private bool _isRippling;

    // Glow effect parameters
    private const float GlowColorR = 0.0f;    // Blue glow
    private const float GlowColorG = 0.47f;   // (0, 120, 215) normalized
    private const float GlowColorB = 0.84f;
    private const float StrokeWidth = 2.0f;   // Thinner base stroke
    private const float TrailLength = 0.44f;  // 24% of perimeter (increased by 20%)
    private const float DimOpacity = 0.35f;   // Slightly less dimming

    /// <summary>
    /// Gets the currently highlighted element.
    /// </summary>
    public FrameworkElement? HighlightedElement => _highlightedElement;

    public DevToolsOverlay(Window targetWindow)
    {
        _targetWindow = targetWindow ?? throw new ArgumentNullException(nameof(targetWindow));

        // Create brushes for margin/padding visualization - cohesive blue theme
        _marginBrush = new SolidColorBrush(Color.FromArgb(60, 138, 180, 248)); // Light blue for margin
        _paddingBrush = new SolidColorBrush(Color.FromArgb(60, 180, 140, 255)); // Light purple for padding

        // Initialize animation
        _animationStopwatch = new Stopwatch();
    }

    /// <summary>
    /// Highlights the specified element in the target window.
    /// </summary>
    /// <param name="element">The element to highlight, or null to clear highlighting.</param>
    public void HighlightElement(UIElement? element)
    {
        var previousElement = _highlightedElement;
        _highlightedElement = element as FrameworkElement;

        if (_highlightedElement != null)
        {
            // Check if we're switching to a different element (start transition)
            if (previousElement != null && previousElement != _highlightedElement)
            {
                // Store previous bounds for transition
                _previousBounds = GetElementBoundsInWindow(previousElement);
                _targetBounds = GetElementBoundsInWindow(_highlightedElement);

                if (_previousBounds.HasValue && _targetBounds.HasValue)
                {
                    // Start transition animation
                    _isTransitioning = true;
                    _headProgress = 0f;
                    _tailProgress = 0f;
                    _transitionStopwatch.Restart();
                }
            }

            // Start animation if not already running
            if (_animationTimer == null)
            {
                _animationStopwatch.Restart();
                _animationTimer = new DispatcherTimer
                {
                    Interval = CompositionTarget.FrameInterval
                };
                _animationTimer.Tick += OnAnimationTick;
                _animationTimer.Start();
            }
        }
        else
        {
            // Stop animation when no element is highlighted
            StopAnimation();
        }

        // Request full redraw — overlay covers entire window (dim + glow)
        _targetWindow.RequestFullInvalidation();
        _targetWindow.InvalidateWindow();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_highlightedElement != null)
        {
            // Update transition progress if transitioning
            if (_isTransitioning)
            {
                double elapsed = _transitionStopwatch.Elapsed.TotalMilliseconds;
                double t = Math.Min(1.0, elapsed / TransitionDurationMs);

                // Head uses fast ease-out (cubic)
                _headProgress = (float)EaseOutCubic(t);

                // Tail follows with delay (starts at 20% and catches up)
                double tailT = Math.Max(0, (t - 0.15) / 0.85);
                _tailProgress = (float)EaseOutCubic(tailT);

                // End transition when tail catches up - start ripple effect
                if (t >= 1.0 && _tailProgress >= 0.99f)
                {
                    _isTransitioning = false;
                    _previousBounds = null;

                    // Start ripple animation
                    _isRippling = true;
                    _rippleProgress = 0f;
                    _rippleStopwatch.Restart();
                }
            }
            // Update ripple progress if rippling
            else if (_isRippling)
            {
                double elapsed = _rippleStopwatch.Elapsed.TotalMilliseconds;
                double t = Math.Min(1.0, elapsed / RippleDurationMs);

                // Use ease-out for fast expansion that slows down
                _rippleProgress = (float)EaseOutCubic(t);

                // End ripple when complete
                if (t >= 1.0)
                {
                    _isRippling = false;
                    _rippleStopwatch.Stop();
                    // Reset animation phase for fresh rotation start
                    _animationStopwatch.Restart();
                }
            }

            _targetWindow.RequestFullInvalidation();
            _targetWindow.InvalidateWindow();
        }
        else
        {
            StopAnimation();
        }
    }

    /// <summary>
    /// Cubic ease-out function for smooth deceleration.
    /// </summary>
    private static double EaseOutCubic(double t)
    {
        return 1.0 - Math.Pow(1.0 - t, 3.0);
    }

    private void StopAnimation()
    {
        _animationTimer?.Stop();
        _animationTimer = null;
        _animationStopwatch.Stop();
    }

    /// <summary>
    /// Gets the bounds of the highlighted element in window coordinates.
    /// </summary>
    /// <returns>The bounds, or null if no element is highlighted.</returns>
    public Rect? GetHighlightBounds()
    {
        if (_highlightedElement == null)
        {
            return null;
        }

        // Calculate the element's bounds relative to the window
        return GetElementBoundsInWindow(_highlightedElement);
    }

    /// <summary>
    /// Calculates the element's bounds relative to the window.
    /// </summary>
    private Rect? GetElementBoundsInWindow(FrameworkElement element)
    {
        double x = 0;
        double y = 0;

        Visual? current = element;
        while (current != null && current != _targetWindow)
        {
            if (current is UIElement uiElement)
            {
                var bounds = uiElement.VisualBounds;
                x += bounds.X;
                y += bounds.Y;
            }

            current = current.VisualParent;
        }

        if (current == _targetWindow)
        {
            return new Rect(x, y, element.ActualWidth, element.ActualHeight);
        }

        return null;
    }

    /// <summary>
    /// Gets the current animation phase (0.0 to 1.0) with smooth constant-speed rotation.
    /// Uses pure linear motion for iOS-style elegant animation.
    /// </summary>
    private float GetAnimationPhase()
    {
        double elapsedMs = _animationStopwatch.Elapsed.TotalMilliseconds;
        // Pure linear phase for smooth constant-speed rotation (iOS style)
        double linearPhase = (elapsedMs % AnimationDurationMs) / AnimationDurationMs;

        return (float)linearPhase;
    }

    /// <summary>
    /// Draws the highlight overlay onto a drawing context.
    /// Call this from the target window's render method.
    /// </summary>
    /// <param name="dc">The drawing context.</param>
    public void DrawOverlay(DrawingContext dc)
    {
        if (_highlightedElement == null)
        {
            return;
        }

        var bounds = GetHighlightBounds();
        if (!bounds.HasValue)
        {
            return;
        }

        var rect = bounds.Value;

        // Try to get the native RenderTarget for the advanced glow effect
        if (dc is RenderTargetDrawingContext rtdc)
        {
            var renderTarget = rtdc.RenderTarget;
            float animationPhase = GetAnimationPhase();

            // Check if we're in transition mode
            if (_isTransitioning && _previousBounds.HasValue && _targetBounds.HasValue)
            {
                var prevRect = _previousBounds.Value;
                var targetRect = _targetBounds.Value;

                // Draw transition animation
                renderTarget.DrawGlowingBorderTransition(
                    (float)prevRect.X, (float)prevRect.Y, (float)prevRect.Width, (float)prevRect.Height,
                    (float)targetRect.X, (float)targetRect.Y, (float)targetRect.Width, (float)targetRect.Height,
                    _headProgress, _tailProgress,
                    animationPhase,
                    GlowColorR, GlowColorG, GlowColorB,
                    StrokeWidth,
                    TrailLength,
                    DimOpacity,
                    (float)_targetWindow.Width, (float)_targetWindow.Height);
            }
            else if (_isRippling)
            {
                // Draw ripple effect expanding from element center
                renderTarget.DrawRippleEffect(
                    (float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height,
                    _rippleProgress,
                    GlowColorR, GlowColorG, GlowColorB,
                    StrokeWidth,
                    DimOpacity,
                    (float)_targetWindow.Width, (float)_targetWindow.Height);
            }
            else
            {
                // Draw the normal glowing border highlight using native rendering
                renderTarget.DrawGlowingBorderHighlight(
                    (float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height,
                    animationPhase,
                    GlowColorR, GlowColorG, GlowColorB,
                    StrokeWidth,
                    TrailLength,
                    DimOpacity,
                    (float)_targetWindow.Width, (float)_targetWindow.Height);
            }

            // Draw margin area (if the element is a FrameworkElement with margin)
            if (_highlightedElement is FrameworkElement fe && HasNonZeroMargin(fe.Margin))
            {
                var marginRect = new Rect(
                    rect.X - fe.Margin.Left,
                    rect.Y - fe.Margin.Top,
                    rect.Width + fe.Margin.Left + fe.Margin.Right,
                    rect.Height + fe.Margin.Top + fe.Margin.Bottom);

                dc.DrawRectangle(_marginBrush, null, marginRect);
            }

            // Draw padding area (if the element is a Control with padding)
            if (_highlightedElement is Control control && HasNonZeroThickness(control.Padding))
            {
                var paddingRect = new Rect(
                    rect.X + control.Padding.Left,
                    rect.Y + control.Padding.Top,
                    rect.Width - control.Padding.Left - control.Padding.Right,
                    rect.Height - control.Padding.Top - control.Padding.Bottom);

                if (paddingRect is { Width: > 0, Height: > 0 })
                {
                    dc.DrawRectangle(_paddingBrush, null, paddingRect);
                }
            }

            // Draw size label
            DrawSizeLabel(dc, rect);
        }
        else
        {
            // Fallback to simple rendering if native context not available
            DrawSimpleOverlay(dc, rect);
        }
    }

    /// <summary>
    /// Fallback simple overlay drawing when native rendering is not available.
    /// </summary>
    private void DrawSimpleOverlay(DrawingContext dc, Rect rect)
    {
        var highlightFillBrush = new SolidColorBrush(Color.FromArgb(50, 0, 120, 215));
        var highlightBorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215));

        // Draw margin area
        if (_highlightedElement is FrameworkElement fe && HasNonZeroMargin(fe.Margin))
        {
            var marginRect = new Rect(
                rect.X - fe.Margin.Left,
                rect.Y - fe.Margin.Top,
                rect.Width + fe.Margin.Left + fe.Margin.Right,
                rect.Height + fe.Margin.Top + fe.Margin.Bottom);

            dc.DrawRectangle(_marginBrush, null, marginRect);
        }

        // Draw the main element bounds
        dc.DrawRectangle(highlightFillBrush, null, rect);

        // Draw padding area
        if (_highlightedElement is Control control && HasNonZeroThickness(control.Padding))
        {
            var paddingRect = new Rect(
                rect.X + control.Padding.Left,
                rect.Y + control.Padding.Top,
                rect.Width - control.Padding.Left - control.Padding.Right,
                rect.Height - control.Padding.Top - control.Padding.Bottom);

            if (paddingRect is { Width: > 0, Height: > 0 })
            {
                dc.DrawRectangle(_paddingBrush, null, paddingRect);
            }
        }

        // Draw border
        var pen = new Pen(highlightBorderBrush, 2);
        dc.DrawRectangle(null, pen, rect);

        // Draw size label
        DrawSizeLabel(dc, rect);
    }

    private void DrawSizeLabel(DrawingContext dc, Rect elementBounds)
    {
        if (_highlightedElement == null) return;

        // Get element type name
        var typeName = _highlightedElement.GetType().Name;

        // Create formatted text for type name (blue accent color)
        var typeText = new FormattedText(typeName, "Segoe UI Semibold", 11)
        {
            Foreground = new SolidColorBrush(Color.FromRgb(100, 180, 255))
        };
        TextMeasurement.MeasureText(typeText);

        // Create formatted text for size (white)
        var sizeText = new FormattedText($"{elementBounds.Width:F0} × {elementBounds.Height:F0}", "Segoe UI", 10)
        {
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180))
        };
        TextMeasurement.MeasureText(sizeText);

        // Calculate panel dimensions
        double panelWidth = Math.Max(typeText.Width, sizeText.Width) + 16;
        double panelHeight = typeText.Height + sizeText.Height + 10;

        // Position panel below or above the element
        double panelX = elementBounds.X;
        double panelY = elementBounds.Bottom + 6;

        // If panel would go off screen, place it above
        if (panelY + panelHeight > _targetWindow.Height)
        {
            panelY = elementBounds.Y - panelHeight - 6;
        }

        // Adjust horizontal position if needed
        if (panelX + panelWidth > _targetWindow.Width)
        {
            panelX = _targetWindow.Width - panelWidth - 4;
        }

        var panelBounds = new Rect(panelX, panelY, panelWidth, panelHeight);

        // Draw premium dark background with subtle blur effect simulation
        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(240, 18, 22, 32)),
            null,
            panelBounds,
            6, 6);

        // Draw subtle blue glow border matching the animation
        var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 120, 215)), 1);
        dc.DrawRoundedRectangle(
            null,
            borderPen,
            panelBounds,
            6, 6);

        // Draw type name
        dc.DrawText(typeText, new Point(panelX + 8, panelY + 4));

        // Draw size below type name
        dc.DrawText(sizeText, new Point(panelX + 8, panelY + typeText.Height + 6));
    }

    private static bool HasNonZeroMargin(Thickness thickness)
    {
        return thickness.Left != 0 || thickness.Top != 0 ||
               thickness.Right != 0 || thickness.Bottom != 0;
    }

    private static bool HasNonZeroThickness(Thickness thickness)
    {
        return thickness.Left > 0 || thickness.Top > 0 ||
               thickness.Right > 0 || thickness.Bottom > 0;
    }

    /// <summary>
    /// Removes the overlay from the target window.
    /// </summary>
    public void RemoveOverlay()
    {
        _highlightedElement = null;
        StopAnimation();
        _targetWindow.RequestFullInvalidation();
        _targetWindow.InvalidateWindow();
    }
}
