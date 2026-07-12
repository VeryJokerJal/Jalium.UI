using System.Diagnostics;
using Jalium.UI.Diagnostics;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using RenderTargetDrawingContext = Jalium.UI.Interop.RenderTargetDrawingContext;

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

    // Glow effect parameters - signal amber (#FFB22E) normalized to 0..1
    private const float GlowColorR = 1.0f;
    private const float GlowColorG = 0.698f;
    private const float GlowColorB = 0.18f;
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

        // Margin/padding bands aligned to the box-model legend: margin = caution amber-orange, padding = green.
        _marginBrush = new SolidColorBrush(Color.FromArgb(60, DevToolsTheme.WarningColor.R, DevToolsTheme.WarningColor.G, DevToolsTheme.WarningColor.B));
        _paddingBrush = new SolidColorBrush(Color.FromArgb(60, DevToolsTheme.SuccessColor.R, DevToolsTheme.SuccessColor.G, DevToolsTheme.SuccessColor.B));

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
    /// Returns the highlighted element's true on-screen bounds in window (DIP)
    /// coordinates — the axis-aligned box the user actually sees.
    ///
    /// The old implementation summed raw <see cref="FrameworkElement.VisualBounds"/>
    /// offsets and returned the element's UNSCALED layout size
    /// (<c>ActualWidth × ActualHeight</c>), ignoring every ancestor
    /// <see cref="UIElement.RenderTransform"/>. That is wrong for any element inside a
    /// scaled subtree — most visibly a <see cref="Viewbox"/>, whose inner wrapper
    /// carries a ScaleTransform: the child keeps its natural size while rendering
    /// larger on screen, so the highlight box was drawn too small / mis-placed and the
    /// size label read e.g. "41 × 23" for text that is visibly bigger (the "text size
    /// not computing the real size" symptom).
    ///
    /// We now delegate to <see cref="UIElement.GetRenderBounds"/>, the framework's
    /// authoritative local→window mapping: it composes the full ancestor + own
    /// RenderTransform chain (each about its origin) AND per-element RenderOffset —
    /// the exact matrix the renderer and the dirty-region pipeline use (pinned by
    /// RenderBoundsTransformTests) — so the box tracks scaled subtrees (Viewbox /
    /// zoom) and render-time offsets (e.g. DataGrid frozen columns) alike. With no
    /// transform on the chain it reduces to the element's window-space layout box,
    /// identical to the previous behaviour.
    /// </summary>
    private Rect? GetElementBoundsInWindow(FrameworkElement element)
    {
        // The picker / inspector tree can still reference an element that was detached
        // from the inspected window's tree between selection and this paint. Only draw
        // a highlight for elements still parented under _targetWindow (GetRenderBounds
        // maps relative to the first window-host ancestor, which for a connected
        // element is exactly _targetWindow).
        Visual? current = element;
        while (current != null && current != _targetWindow)
        {
            current = current.VisualParent;
        }

        if (current != _targetWindow)
        {
            return null;
        }

        return element.GetRenderBounds();
    }

    /// <summary>
    /// On-screen (window-space) rectangle of the element's margin band: its local box
    /// grown outward by <see cref="FrameworkElement.Margin"/>, mapped through the same
    /// element→window transform as the highlight box (via
    /// <see cref="UIElement.MapLocalRectToScreen"/>) so the band stays aligned and
    /// correctly scaled inside a Viewbox / zoomed subtree. Adding raw Thickness to the
    /// already-scaled bounds would draw the wrong thickness. Returns null when there is
    /// no margin.
    /// </summary>
    private static Rect? GetMarginBandBounds(FrameworkElement element)
    {
        if (!HasNonZeroMargin(element.Margin))
        {
            return null;
        }

        var m = element.Margin;
        var w = element.ActualWidth;
        var h = element.ActualHeight;
        return element.MapLocalRectToScreen(new Rect(
            -m.Left,
            -m.Top,
            w + m.Left + m.Right,
            h + m.Top + m.Bottom));
    }

    /// <summary>
    /// On-screen (window-space) rectangle of the control's padding band: its content
    /// box inset by <see cref="Control.Padding"/>, mapped through the element→window
    /// transform so it scales with a Viewbox / zoomed ancestor. Returns null when there
    /// is no padding or the inset collapses to a non-positive size.
    /// </summary>
    private static Rect? GetPaddingBandBounds(Control control)
    {
        if (!HasNonZeroThickness(control.Padding))
        {
            return null;
        }

        var p = control.Padding;
        var innerW = control.ActualWidth - p.Left - p.Right;
        var innerH = control.ActualHeight - p.Top - p.Bottom;
        if (innerW <= 0 || innerH <= 0)
        {
            return null;
        }

        return control.MapLocalRectToScreen(new Rect(p.Left, p.Top, innerW, innerH));
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
        DrawRenderDiagnosticsOverlay(dc);
        DrawRulerOverlay(dc);

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
            if (_highlightedElement is FrameworkElement fe && GetMarginBandBounds(fe) is { } marginRect)
            {
                dc.DrawRectangle(_marginBrush, null, marginRect);
            }

            // Draw padding area (if the element is a Control with padding)
            if (_highlightedElement is Control control && GetPaddingBandBounds(control) is { } paddingRect)
            {
                dc.DrawRectangle(_paddingBrush, null, paddingRect);
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
        var highlightFillBrush = new SolidColorBrush(Color.FromArgb(50, DevToolsTheme.AccentColor.R, DevToolsTheme.AccentColor.G, DevToolsTheme.AccentColor.B));
        var highlightBorderBrush = new SolidColorBrush(DevToolsTheme.AccentColor);

        // Draw margin area
        if (_highlightedElement is FrameworkElement fe && GetMarginBandBounds(fe) is { } marginRect)
        {
            dc.DrawRectangle(_marginBrush, null, marginRect);
        }

        // Draw the main element bounds
        dc.DrawRectangle(highlightFillBrush, null, rect);

        // Draw padding area
        if (_highlightedElement is Control control && GetPaddingBandBounds(control) is { } paddingRect)
        {
            dc.DrawRectangle(_paddingBrush, null, paddingRect);
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
        var typeText = new FormattedText(typeName, "Bahnschrift SemiBold", 11)
        {
            Foreground = new SolidColorBrush(DevToolsTheme.AccentColor)
        };
        TextMeasurement.MeasureText(typeText);

        // Create formatted text for size (white). elementBounds is the on-screen
        // axis-aligned bounding box (GetRenderBounds), so this is the real size the
        // user sees — it equals the element's layout ActualWidth/Height only when no
        // ancestor scales/rotates it, and reports the AABB for a rotated element.
        var sizeText = new FormattedText($"{elementBounds.Width:F0} × {elementBounds.Height:F0}", "Cascadia Code", 10)
        {
            Foreground = new SolidColorBrush(DevToolsTheme.TextSecondaryColor)
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
            new SolidColorBrush(Color.FromArgb(240, DevToolsTheme.ChromeColor.R, DevToolsTheme.ChromeColor.G, DevToolsTheme.ChromeColor.B)),
            null,
            panelBounds,
            6, 6);

        // Draw subtle amber glow border matching the animation
        var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(120, DevToolsTheme.AccentColor.R, DevToolsTheme.AccentColor.G, DevToolsTheme.AccentColor.B)), 1);
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

    private void DrawRenderDiagnosticsOverlay(DrawingContext dc)
    {
        switch (RenderDiagnostics.Mode)
        {
            case RenderDiagnostics.OverlayMode.Overdraw:
                DrawOverdrawOverlay(dc);
                break;
            case RenderDiagnostics.OverlayMode.DirtyRegions:
                DrawDirtyRegionOverlay(dc);
                break;
        }
    }

    private void DrawOverdrawOverlay(DrawingContext dc)
    {
        var cells = RenderDiagnostics.SnapshotOverdraw();
        foreach (var cell in cells)
        {
            byte alpha = cell.DrawCount switch
            {
                <= 1 => 60,
                2 => 90,
                3 => 120,
                4 => 160,
                _ => 200,
            };
            Color color = cell.DrawCount switch
            {
                <= 1 => Color.FromArgb(alpha, 0, 255, 0),
                2 => Color.FromArgb(alpha, 255, 255, 0),
                3 => Color.FromArgb(alpha, 255, 160, 0),
                4 => Color.FromArgb(alpha, 255, 80, 0),
                _ => Color.FromArgb(alpha, 255, 0, 0),
            };
            dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(cell.X, cell.Y, cell.Width, cell.Height));
        }
    }

    private void DrawDirtyRegionOverlay(DrawingContext dc)
    {
        var history = RenderDiagnostics.SnapshotDirtyHistory();
        int idx = history.Count - 1;
        for (int i = 0; i < Math.Min(history.Count, 16); i++)
        {
            var entry = history[idx - i];
            byte alpha = (byte)Math.Max(30, 180 - i * 12);
            var brush = new SolidColorBrush(Color.FromArgb((byte)(alpha / 5), 255, 120, 60));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 255, 120, 60)), 1);
            dc.DrawRectangle(brush, pen, entry.Region);
        }
    }

    // ── Ruler ─────────────────────────────────────────────────────────────

    internal bool IsRulerActive { get; private set; }
    internal Point? RulerStart { get; private set; }
    internal Point? RulerEnd { get; private set; }
    internal bool RulerEndCommitted { get; private set; }

    internal void BeginRuler()
    {
        IsRulerActive = true;
        RulerStart = null;
        RulerEnd = null;
        RulerEndCommitted = false;
        RequestRedraw();
    }

    internal void EndRuler()
    {
        IsRulerActive = false;
        RulerStart = null;
        RulerEnd = null;
        RulerEndCommitted = false;
        RequestRedraw();
    }

    internal void SetRulerStart(Point p)
    {
        RulerStart = p;
        RulerEnd = null;
        RulerEndCommitted = false;
        RequestRedraw();
    }

    /// <summary>Live preview position — overwritten on mouse-move until the user clicks.</summary>
    internal void SetRulerPreviewEnd(Point p)
    {
        if (!IsRulerActive || RulerStart == null || RulerEndCommitted) return;
        RulerEnd = p;
        RequestRedraw();
    }

    internal void CommitRulerEnd(Point p)
    {
        RulerEnd = p;
        RulerEndCommitted = true;
        RequestRedraw();
    }

    private void RequestRedraw()
    {
        _targetWindow.RequestFullInvalidation();
        _targetWindow.InvalidateWindow();
    }

    // Ruler palette — a cool cyan/blue with pop.
    private static readonly Color RulerColor         = DevToolsTheme.AccentColor;
    private static readonly Color RulerColorLight    = DevToolsTheme.AccentHoverColor;
    private static readonly Color RulerShadow        = Color.FromArgb(0x60, 0x00, 0x00, 0x00);

    private void DrawRulerOverlay(DrawingContext dc)
    {
        if (!IsRulerActive || RulerStart == null) return;
        var start = RulerStart.Value;

        // Pen for guide axes (dashed look is faked with short segments).
        var axisPen = new Pen(new SolidColorBrush(Color.FromArgb(0x80, RulerColor.R, RulerColor.G, RulerColor.B)), 1);
        var linePenGlow = new Pen(new SolidColorBrush(Color.FromArgb(0x40, RulerColor.R, RulerColor.G, RulerColor.B)), 6);
        var linePen = new Pen(new SolidColorBrush(RulerColor), 2);
        var shadowBrush = new SolidColorBrush(RulerShadow);
        var dotFill = new SolidColorBrush(RulerColorLight);
        var dotStroke = new Pen(new SolidColorBrush(RulerColor), 2);

        // Draw the start endpoint first (pulsing ring when we have no end yet).
        DrawEndpoint(dc, start, dotFill, dotStroke, shadowBrush);

        if (RulerEnd is not { } end)
        {
            // Only the start point — draw cross-hair axes anchored there so the
            // user gets a visible reference before placing the second point.
            DrawCrossAxes(dc, start, axisPen);
            return;
        }

        // Draw the dashed right-triangle (Δx / Δy) guides first so the main line
        // stays visually on top.
        DrawDashedLine(dc, new Point(start.X, start.Y), new Point(end.X, start.Y), axisPen);
        DrawDashedLine(dc, new Point(end.X, start.Y), new Point(end.X, end.Y),   axisPen);

        // Glow underlay + sharp main line.
        dc.DrawLine(linePenGlow, start, end);
        dc.DrawLine(linePen, start, end);

        // End dot + a callout at the midpoint with the distance.
        DrawEndpoint(dc, end, dotFill, dotStroke, shadowBrush);

        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        DrawCallout(dc,
            text: $"{dist:F1} px",
            anchor: new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2),
            accent: RulerColor);

        // Small dx / dy chips on the axis midpoints when segments are long enough to be readable.
        if (Math.Abs(dx) > 40)
        {
            DrawInlineChip(dc, $"Δx {dx:F0}", new Point((start.X + end.X) / 2, start.Y - 10), RulerColorLight);
        }
        if (Math.Abs(dy) > 40)
        {
            DrawInlineChip(dc, $"Δy {dy:F0}", new Point(end.X + 10, (start.Y + end.Y) / 2), RulerColorLight);
        }
    }

    private static void DrawEndpoint(DrawingContext dc, Point p, Brush fill, Pen stroke, Brush shadow)
    {
        // Soft shadow drop.
        dc.DrawEllipse(shadow, null, new Point(p.X + 0.5, p.Y + 1.5), 6, 6);
        // Solid fill + ring.
        dc.DrawEllipse(fill, stroke, p, 5, 5);
        // Inner dark center.
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x10, 0x22, 0x35)), null, p, 2, 2);
    }

    private static void DrawCrossAxes(DrawingContext dc, Point p, Pen pen)
    {
        const double reach = 14;
        dc.DrawLine(pen, new Point(p.X - reach, p.Y), new Point(p.X - 6, p.Y));
        dc.DrawLine(pen, new Point(p.X + 6, p.Y), new Point(p.X + reach, p.Y));
        dc.DrawLine(pen, new Point(p.X, p.Y - reach), new Point(p.X, p.Y - 6));
        dc.DrawLine(pen, new Point(p.X, p.Y + 6), new Point(p.X, p.Y + reach));
    }

    private static void DrawDashedLine(DrawingContext dc, Point a, Point b, Pen pen)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        double nx = dx / len;
        double ny = dy / len;
        const double dashLen = 6;
        const double gapLen = 4;
        double t = 0;
        while (t < len)
        {
            double segEnd = Math.Min(t + dashLen, len);
            dc.DrawLine(pen,
                new Point(a.X + nx * t, a.Y + ny * t),
                new Point(a.X + nx * segEnd, a.Y + ny * segEnd));
            t = segEnd + gapLen;
        }
    }

    private static void DrawCallout(DrawingContext dc, string text, Point anchor, Color accent)
    {
        var ft = new FormattedText(text, FrameworkElement.DefaultFontFamilyName, 11)
        {
            Foreground = new SolidColorBrush(DevToolsTheme.TextPrimaryColor),
        };
        TextMeasurement.MeasureText(ft);
        double w = ft.Width + 14;
        double h = ft.Height + 6;
        var rect = new Rect(anchor.X - w / 2, anchor.Y - h / 2 - 16, w, h);

        // Drop shadow.
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), null,
            new Rect(rect.X + 1, rect.Y + 2, rect.Width, rect.Height), 6, 6);
        // Background with accent border.
        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromRgb(0x18, 0x20, 0x2C)),
            new Pen(new SolidColorBrush(accent), 1),
            rect, 6, 6);
        // Text.
        dc.DrawText(ft, new Point(rect.X + 7, rect.Y + 3));
    }

    private static void DrawInlineChip(DrawingContext dc, string text, Point anchor, Color accent)
    {
        var ft = new FormattedText(text, FrameworkElement.DefaultFontFamilyName, 10)
        {
            Foreground = new SolidColorBrush(accent),
        };
        TextMeasurement.MeasureText(ft);
        double w = ft.Width + 10;
        double h = ft.Height + 4;
        var rect = new Rect(anchor.X - w / 2, anchor.Y - h / 2, w, h);
        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x18, 0x24)),
            null,
            rect, 4, 4);
        dc.DrawText(ft, new Point(rect.X + 5, rect.Y + 2));
    }
}
