using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// A FrameworkElement that renders a geographic heatmap visualization.
/// Data points are projected using Mercator projection and rendered as a Gaussian-blurred
/// intensity field colorized by a configurable gradient.
/// </summary>
public class GeographicHeatmap : FrameworkElement
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.GeographicHeatmapAutomationPeer(this);
    }

    // Cached brushes
    private static readonly SolidColorBrush s_legendBackground = new(Color.FromArgb(200, 255, 255, 255));
    private static readonly SolidColorBrush s_legendBorder = new(Color.FromRgb(180, 180, 180));
    private static readonly SolidColorBrush s_legendTextBrush = new(Color.FromRgb(60, 60, 60));

    // Cached heatmap image
    private ImageSource? _cachedHeatmapImage;
    private int _cachedWidth;
    private int _cachedHeight;
    private int _cachedPointsVersion;
    private int _pointsVersion;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Points dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(HeatPointCollection), typeof(GeographicHeatmap),
            new PropertyMetadata(null, OnDataPropertyChanged));

    /// <summary>
    /// Identifies the Radius dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.Register(nameof(Radius), typeof(double), typeof(GeographicHeatmap),
            new PropertyMetadata(20.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Intensity dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty IntensityProperty =
        DependencyProperty.Register(nameof(Intensity), typeof(double), typeof(GeographicHeatmap),
            new PropertyMetadata(1.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Gradient dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty GradientProperty =
        DependencyProperty.Register(nameof(Gradient), typeof(HeatmapGradient), typeof(GeographicHeatmap),
            new PropertyMetadata(HeatmapGradient.Default, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the HeatmapOpacity dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty HeatmapOpacityProperty =
        DependencyProperty.Register(nameof(HeatmapOpacity), typeof(double), typeof(GeographicHeatmap),
            new PropertyMetadata(0.6, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowLegend dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowLegendProperty =
        DependencyProperty.Register(nameof(ShowLegend), typeof(bool), typeof(GeographicHeatmap),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LegendPosition dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty LegendPositionProperty =
        DependencyProperty.Register(nameof(LegendPosition), typeof(HeatmapLegendPosition), typeof(GeographicHeatmap),
            new PropertyMetadata(HeatmapLegendPosition.BottomRight, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the MapView dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty MapViewProperty =
        DependencyProperty.Register(nameof(MapView), typeof(MapView), typeof(GeographicHeatmap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Bounds dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BoundsProperty =
        DependencyProperty.Register(nameof(Bounds), typeof(Rect), typeof(GeographicHeatmap),
            new PropertyMetadata(Rect.Empty, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the RenderScale dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty RenderScaleProperty =
        DependencyProperty.Register(nameof(RenderScale), typeof(double), typeof(GeographicHeatmap),
            new PropertyMetadata(0.5, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the collection of heat data points.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public HeatPointCollection? Points
    {
        get => (HeatPointCollection?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>
    /// Gets or sets the radius of each heat point's Gaussian kernel in pixels.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double Radius
    {
        get => (double)GetValue(RadiusProperty)!;
        set => SetValue(RadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets the global intensity multiplier.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double Intensity
    {
        get => (double)GetValue(IntensityProperty)!;
        set => SetValue(IntensityProperty, value);
    }

    /// <summary>
    /// Gets or sets the gradient used to colorize the heatmap.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public HeatmapGradient Gradient
    {
        get => (HeatmapGradient)(GetValue(GradientProperty) ?? HeatmapGradient.Default);
        set => SetValue(GradientProperty, value);
    }

    /// <summary>
    /// Gets or sets the opacity of the heatmap overlay (0.0 - 1.0).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double HeatmapOpacity
    {
        get => (double)GetValue(HeatmapOpacityProperty)!;
        set => SetValue(HeatmapOpacityProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the legend is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowLegend
    {
        get => (bool)GetValue(ShowLegendProperty)!;
        set => SetValue(ShowLegendProperty, value);
    }

    /// <summary>
    /// Gets or sets the position of the legend.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public HeatmapLegendPosition LegendPosition
    {
        get => (HeatmapLegendPosition)(GetValue(LegendPositionProperty) ?? HeatmapLegendPosition.BottomRight);
        set => SetValue(LegendPositionProperty, value);
    }

    /// <summary>
    /// Gets or sets the MapView to use for geographic projection.
    /// When set, points are projected using the map's Mercator projection.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public MapView? MapView
    {
        get => (MapView?)GetValue(MapViewProperty);
        set => SetValue(MapViewProperty, value);
    }

    /// <summary>
    /// Gets or sets explicit bounds for non-MapView mode.
    /// When empty, bounds are auto-computed from the data points.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Rect Bounds
    {
        get => (Rect)GetValue(BoundsProperty)!;
        set => SetValue(BoundsProperty, value);
    }

    /// <summary>
    /// Gets or sets the render scale factor (0.0 - 1.0). Lower values render faster
    /// but with less detail. The image is scaled up to fill the control.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double RenderScale
    {
        get => (double)GetValue(RenderScaleProperty)!;
        set => SetValue(RenderScaleProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicHeatmap"/> class.
    /// </summary>
    public GeographicHeatmap()
    {
        ClipToBounds = true;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 300 : availableSize.Height;
        return new Size(w, h);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var renderBounds = new Rect(RenderSize);
        if (renderBounds.Width <= 0 || renderBounds.Height <= 0) return;

        var points = Points;
        if (points == null || points.Count == 0)
        {
            if (ShowLegend)
                DrawLegend(dc, renderBounds);
            return;
        }

        // Compute or use cached heatmap image
        double scale = Math.Clamp(RenderScale, 0.1, 1.0);
        int bufferWidth = Math.Max(1, (int)(renderBounds.Width * scale));
        int bufferHeight = Math.Max(1, (int)(renderBounds.Height * scale));

        bool needsRedraw = _cachedHeatmapImage == null ||
                           _cachedWidth != bufferWidth ||
                           _cachedHeight != bufferHeight ||
                           _cachedPointsVersion != _pointsVersion;

        if (needsRedraw)
        {
            _cachedHeatmapImage = RenderHeatmapImage(points, bufferWidth, bufferHeight, renderBounds);
            _cachedWidth = bufferWidth;
            _cachedHeight = bufferHeight;
            _cachedPointsVersion = _pointsVersion;
        }

        if (_cachedHeatmapImage != null)
        {
            dc.PushOpacity(HeatmapOpacity);
            dc.DrawImage(_cachedHeatmapImage, renderBounds);
            dc.Pop();
        }

        if (ShowLegend)
            DrawLegend(dc, renderBounds);
    }

    private ImageSource RenderHeatmapImage(HeatPointCollection points, int width, int height, Rect renderBounds)
    {
        // Step 1: Compute intensity buffer
        var intensity = new double[width * height];
        double radius = Radius * RenderScale;
        double radiusSq = radius * radius;
        double sigma = radius / 3.0;
        double twoSigmaSq = 2.0 * sigma * sigma;
        double globalIntensity = Intensity;

        var mapView = MapView;

        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            double screenX, screenY;

            if (mapView != null)
            {
                // Use MapView's Mercator projection
                var geo = new GeoPoint(point.Latitude, point.Longitude);
                var screenPt = mapView.GeoToScreen(geo);
                screenX = screenPt.X * RenderScale;
                screenY = screenPt.Y * RenderScale;
            }
            else
            {
                // Use Bounds mapping or auto-compute
                var bounds = GetEffectiveBounds(points);
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;

                double normalizedX = (point.Longitude - bounds.X) / bounds.Width;
                double normalizedY = 1.0 - (point.Latitude - bounds.Y) / bounds.Height; // Flip Y
                screenX = normalizedX * width;
                screenY = normalizedY * height;
            }

            double weight = point.Weight * globalIntensity;

            // Stamp Gaussian kernel
            int minX = Math.Max(0, (int)(screenX - radius));
            int maxX = Math.Min(width - 1, (int)(screenX + radius));
            int minY = Math.Max(0, (int)(screenY - radius));
            int maxY = Math.Min(height - 1, (int)(screenY + radius));

            for (int py = minY; py <= maxY; py++)
            {
                double dy = py - screenY;
                double dySq = dy * dy;

                for (int px = minX; px <= maxX; px++)
                {
                    double dx = px - screenX;
                    double distSq = dx * dx + dySq;

                    if (distSq <= radiusSq)
                    {
                        double gaussianValue = Math.Exp(-distSq / twoSigmaSq);
                        intensity[py * width + px] += weight * gaussianValue;
                    }
                }
            }
        }

        // Step 2: Normalize intensity
        double maxIntensity = 0;
        for (int i = 0; i < intensity.Length; i++)
        {
            if (intensity[i] > maxIntensity)
                maxIntensity = intensity[i];
        }

        if (maxIntensity <= 0) maxIntensity = 1;

        // Step 3: Build gradient LUT
        var gradient = Gradient;
        var lut = gradient.BuildLookupTable();

        // Step 4: Colorize to pixel buffer (BGRA format)
        var pixels = new byte[width * height * 4];

        for (int i = 0; i < intensity.Length; i++)
        {
            double normalized = Math.Clamp(intensity[i] / maxIntensity, 0, 1);

            if (normalized < 0.001)
            {
                // Fully transparent - skip
                continue;
            }

            int lutIndex = (int)(normalized * 255);
            var color = lut[lutIndex];

            int pixelOffset = i * 4;
            pixels[pixelOffset + 0] = color.B;  // B
            pixels[pixelOffset + 1] = color.G;  // G
            pixels[pixelOffset + 2] = color.R;  // R
            pixels[pixelOffset + 3] = color.A;  // A
        }

        return BitmapImage.FromPixels(pixels, width, height);
    }

    private Rect GetEffectiveBounds(HeatPointCollection points)
    {
        var bounds = Bounds;
        if (bounds != Rect.Empty && bounds.Width > 0 && bounds.Height > 0)
            return bounds;

        // Auto-compute from points
        if (points.Count == 0) return Rect.Empty;

        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLng = double.MaxValue, maxLng = double.MinValue;

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.Latitude < minLat) minLat = p.Latitude;
            if (p.Latitude > maxLat) maxLat = p.Latitude;
            if (p.Longitude < minLng) minLng = p.Longitude;
            if (p.Longitude > maxLng) maxLng = p.Longitude;
        }

        // Add 10% padding
        double latPad = (maxLat - minLat) * 0.1;
        double lngPad = (maxLng - minLng) * 0.1;

        if (latPad < 0.001) latPad = 0.5;
        if (lngPad < 0.001) lngPad = 0.5;

        return new Rect(minLng - lngPad, minLat - latPad,
            (maxLng - minLng) + 2 * lngPad, (maxLat - minLat) + 2 * latPad);
    }

    private void DrawLegend(DrawingContext dc, Rect renderBounds)
    {
        const double legendWidth = 20;
        const double legendHeight = 120;
        const double margin = 10;
        const double padding = 6;
        const double fontSize = 9;
        const double totalWidth = legendWidth + padding * 2 + 30; // extra space for labels
        const double totalHeight = legendHeight + padding * 2 + 20; // extra for title

        double x, y;
        switch (LegendPosition)
        {
            case HeatmapLegendPosition.TopLeft:
                x = margin;
                y = margin;
                break;
            case HeatmapLegendPosition.TopRight:
                x = renderBounds.Width - totalWidth - margin;
                y = margin;
                break;
            case HeatmapLegendPosition.BottomLeft:
                x = margin;
                y = renderBounds.Height - totalHeight - margin;
                break;
            case HeatmapLegendPosition.BottomRight:
            default:
                x = renderBounds.Width - totalWidth - margin;
                y = renderBounds.Height - totalHeight - margin;
                break;
        }

        // Draw background
        var bgRect = new Rect(x, y, totalWidth, totalHeight);
        dc.DrawRoundedRectangle(s_legendBackground, new Pen(s_legendBorder, 0.5), bgRect, 3, 3);

        // Draw title
        var titleText = new FormattedText("Intensity", FrameworkElement.DefaultFontFamilyName, fontSize)
        {
            Foreground = s_legendTextBrush
        };
        TextMeasurement.MeasureText(titleText);
        dc.DrawText(titleText, new Point(x + padding, y + padding));

        // Draw gradient bar
        double barX = x + padding;
        double barY = y + padding + titleText.Height + 4;
        double barH = legendHeight;

        var gradient = Gradient;
        var lut = gradient.BuildLookupTable();

        // Draw the gradient bar as horizontal slices
        for (int i = 0; i < (int)barH; i++)
        {
            double t = 1.0 - (i / barH); // Top = high, bottom = low
            int lutIndex = (int)(t * 255);
            var color = lut[Math.Clamp(lutIndex, 0, 255)];
            var sliceBrush = new SolidColorBrush(color);
            dc.DrawRectangle(sliceBrush, null, new Rect(barX, barY + i, legendWidth, 1));
        }

        // Draw border around gradient bar
        dc.DrawRectangle(null, new Pen(s_legendBorder, 0.5), new Rect(barX, barY, legendWidth, barH));

        // Draw labels
        double labelX = barX + legendWidth + 4;

        var highLabel = new FormattedText("High", FrameworkElement.DefaultFontFamilyName, fontSize)
        {
            Foreground = s_legendTextBrush
        };
        TextMeasurement.MeasureText(highLabel);
        dc.DrawText(highLabel, new Point(labelX, barY));

        var midLabel = new FormattedText("Mid", FrameworkElement.DefaultFontFamilyName, fontSize)
        {
            Foreground = s_legendTextBrush
        };
        TextMeasurement.MeasureText(midLabel);
        dc.DrawText(midLabel, new Point(labelX, barY + barH / 2 - midLabel.Height / 2));

        var lowLabel = new FormattedText("Low", FrameworkElement.DefaultFontFamilyName, fontSize)
        {
            Foreground = s_legendTextBrush
        };
        TextMeasurement.MeasureText(lowLabel);
        dc.DrawText(lowLabel, new Point(labelX, barY + barH - lowLabel.Height));
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnDataPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GeographicHeatmap heatmap)
        {
            heatmap._pointsVersion++;
            heatmap.InvalidateVisual();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GeographicHeatmap heatmap)
        {
            heatmap._pointsVersion++; // Force cache invalidation
            heatmap.InvalidateVisual();
        }
    }

    #endregion
}
