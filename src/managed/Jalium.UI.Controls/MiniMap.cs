using System.Collections.ObjectModel;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies how the mini-map renders the content of the target control.
/// </summary>
public enum MiniMapRenderMode
{
    /// <summary>No content is rendered; only the viewport rectangle is shown.</summary>
    None,
    /// <summary>An outline of the target's visual children is drawn as scaled rectangles.</summary>
    Outline,
    /// <summary>A simplified representation of the target is drawn.</summary>
    Simplified,
    /// <summary>The full content of the target is drawn (requires render-target support).</summary>
    Full
}

/// <summary>
/// Specifies the docking position of the mini-map relative to its parent.
/// </summary>
public enum MiniMapPosition
{
    /// <summary>Top-left corner.</summary>
    TopLeft,
    /// <summary>Top-right corner.</summary>
    TopRight,
    /// <summary>Bottom-left corner.</summary>
    BottomLeft,
    /// <summary>Bottom-right corner.</summary>
    BottomRight,
    /// <summary>Custom position (uses explicit layout).</summary>
    Custom
}

/// <summary>
/// Represents a highlight marker on the mini-map, positioned in normalized (0-1) coordinates.
/// </summary>
public class MiniMapMarker
{
    /// <summary>
    /// Gets or sets the normalized X position (0.0 - 1.0).
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the normalized Y position (0.0 - 1.0).
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Gets or sets the color brush for the marker.
    /// </summary>
    public Brush? Color { get; set; }

    /// <summary>
    /// Gets or sets the display size of the marker in pixels.
    /// </summary>
    public double Size { get; set; } = 6;
}

/// <summary>
/// A mini-map control that shows a scaled-down overview of a target element
/// with a viewport rectangle indicating the currently visible area.
/// Supports click-to-navigate and viewport rectangle dragging.
/// </summary>
public class MiniMap : FrameworkElement
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.MiniMapAutomationPeer(this);
    }

    // Cached brushes
    private static readonly SolidColorBrush s_defaultViewportBrush = new(Color.FromArgb(40, 50, 120, 220));
    private static readonly SolidColorBrush s_defaultViewportBorderBrush = new(Color.FromRgb(50, 120, 220));
    private static readonly SolidColorBrush s_defaultContentBrush = new(Color.FromRgb(200, 200, 200));
    private static readonly SolidColorBrush s_outlineStrokeBrush = new(Color.FromRgb(160, 160, 160));
    private static readonly SolidColorBrush s_defaultBackground = new(Color.FromArgb(220, 240, 240, 240));
    private static readonly SolidColorBrush s_borderBrush = new(Color.FromRgb(180, 180, 180));
    private static readonly SolidColorBrush s_defaultMarkerBrush = new(Color.FromRgb(220, 50, 50));

    #region Dependency Properties

    /// <summary>
    /// Identifies the Target dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.Register(nameof(Target), typeof(FrameworkElement), typeof(MiniMap),
            new PropertyMetadata(null, OnTargetChanged));

    /// <summary>
    /// Identifies the MapViewTarget dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty MapViewTargetProperty =
        DependencyProperty.Register(nameof(MapViewTarget), typeof(MapView), typeof(MiniMap),
            new PropertyMetadata(null, OnTargetChanged));

    /// <summary>
    /// Identifies the ViewportBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ViewportBrushProperty =
        DependencyProperty.Register(nameof(ViewportBrush), typeof(Brush), typeof(MiniMap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(MiniMap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(MiniMap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BorderBrushProperty =
        DependencyProperty.Register(nameof(BorderBrush), typeof(Brush), typeof(MiniMap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BorderThicknessProperty =
        DependencyProperty.Register(nameof(BorderThickness), typeof(Thickness), typeof(MiniMap),
            new PropertyMetadata(new Thickness(1), OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(MiniMap),
            new PropertyMetadata(new CornerRadius(3), OnVisualPropertyChanged));

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush? BorderBrush
    {
        get => (Brush?)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>
    /// Identifies the ViewportBorderBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ViewportBorderBrushProperty =
        DependencyProperty.Register(nameof(ViewportBorderBrush), typeof(Brush), typeof(MiniMap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ViewportBorderThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ViewportBorderThicknessProperty =
        DependencyProperty.Register(nameof(ViewportBorderThickness), typeof(double), typeof(MiniMap),
            new PropertyMetadata(1.5, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ContentRenderMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ContentRenderModeProperty =
        DependencyProperty.Register(nameof(ContentRenderMode), typeof(MiniMapRenderMode), typeof(MiniMap),
            new PropertyMetadata(MiniMapRenderMode.Outline, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ContentBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ContentBrushProperty =
        DependencyProperty.Register(nameof(ContentBrush), typeof(Brush), typeof(MiniMap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the HighlightMarkers dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty HighlightMarkersProperty =
        DependencyProperty.Register(nameof(HighlightMarkers), typeof(ObservableCollection<MiniMapMarker>), typeof(MiniMap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Position dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(nameof(Position), typeof(MiniMapPosition), typeof(MiniMap),
            new PropertyMetadata(MiniMapPosition.TopRight));

    /// <summary>
    /// Identifies the AutoScale dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty AutoScaleProperty =
        DependencyProperty.Register(nameof(AutoScale), typeof(bool), typeof(MiniMap),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ScaleRatio dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ScaleRatioProperty =
        DependencyProperty.Register(nameof(ScaleRatio), typeof(double), typeof(MiniMap),
            new PropertyMetadata(0.15, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the target FrameworkElement whose content this mini-map visualizes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public FrameworkElement? Target
    {
        get => (FrameworkElement?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>
    /// Gets or sets the target MapView for geographic mini-map mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public MapView? MapViewTarget
    {
        get => (MapView?)GetValue(MapViewTargetProperty);
        set => SetValue(MapViewTargetProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used to fill the viewport rectangle.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? ViewportBrush
    {
        get => (Brush?)GetValue(ViewportBrushProperty);
        set => SetValue(ViewportBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for the viewport rectangle border.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? ViewportBorderBrush
    {
        get => (Brush?)GetValue(ViewportBorderBrushProperty);
        set => SetValue(ViewportBorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the thickness of the viewport border.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double ViewportBorderThickness
    {
        get => (double)GetValue(ViewportBorderThicknessProperty)!;
        set => SetValue(ViewportBorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets how the target content is rendered.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public MiniMapRenderMode ContentRenderMode
    {
        get => (MiniMapRenderMode)(GetValue(ContentRenderModeProperty) ?? MiniMapRenderMode.Outline);
        set => SetValue(ContentRenderModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for the simplified content rendering.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? ContentBrush
    {
        get => (Brush?)GetValue(ContentBrushProperty);
        set => SetValue(ContentBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the collection of highlight markers displayed on the mini-map.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ObservableCollection<MiniMapMarker>? HighlightMarkers
    {
        get => (ObservableCollection<MiniMapMarker>?)GetValue(HighlightMarkersProperty);
        set => SetValue(HighlightMarkersProperty, value);
    }

    /// <summary>
    /// Gets or sets the docking position of the mini-map.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public MiniMapPosition Position
    {
        get => (MiniMapPosition)(GetValue(PositionProperty) ?? MiniMapPosition.TopRight);
        set => SetValue(PositionProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the mini-map auto-scales based on the target size.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public bool AutoScale
    {
        get => (bool)GetValue(AutoScaleProperty)!;
        set => SetValue(AutoScaleProperty, value);
    }

    /// <summary>
    /// Gets or sets the scale ratio relative to the target size (0.0 - 1.0).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double ScaleRatio
    {
        get => (double)GetValue(ScaleRatioProperty)!;
        set => SetValue(ScaleRatioProperty, value);
    }

    #endregion

    #region Private Fields

    private bool _isDraggingViewport;
    private Point _dragOffset;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="MiniMap"/> class.
    /// </summary>
    public MiniMap()
    {
        Focusable = false;
        ClipToBounds = true;

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double w, h;

        if (AutoScale && Target != null)
        {
            var targetSize = Target.RenderSize;
            if (targetSize.Width > 0 && targetSize.Height > 0)
            {
                w = targetSize.Width * ScaleRatio;
                h = targetSize.Height * ScaleRatio;
            }
            else
            {
                w = 120;
                h = 90;
            }
        }
        else
        {
            w = double.IsInfinity(availableSize.Width) ? 150 : Math.Min(availableSize.Width, 200);
            h = double.IsInfinity(availableSize.Height) ? 112 : Math.Min(availableSize.Height, 150);
        }

        return new Size(w, h);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // Draw background
        var bg = Background ?? s_defaultBackground;
        var border = BorderBrush ?? s_borderBrush;
        var borderThickness = BorderThickness.Left > 0 ? BorderThickness.Left : 1;
        var radius = CornerRadius.TopLeft > 0 ? CornerRadius.TopLeft : 3;
        dc.DrawRoundedRectangle(bg, new Pen(border, borderThickness), bounds, radius, radius);

        var contentArea = new Rect(2, 2, bounds.Width - 4, bounds.Height - 4);
        dc.PushClip(new RectangleGeometry(contentArea));

        // Draw content based on render mode
        var mode = ContentRenderMode;
        if (mode != MiniMapRenderMode.None)
        {
            DrawContent(dc, contentArea, mode);
        }

        // Draw viewport rectangle
        DrawViewport(dc, contentArea);

        // Draw highlight markers
        DrawHighlightMarkers(dc, contentArea);

        dc.Pop(); // clip
    }

    private void DrawContent(DrawingContext dc, Rect contentArea, MiniMapRenderMode mode)
    {
        var target = Target ?? (FrameworkElement?)MapViewTarget;
        if (target == null) return;

        var targetSize = target.RenderSize;
        if (targetSize.Width <= 0 || targetSize.Height <= 0) return;

        // For ScrollViewer targets, use the full extent size instead of the visible viewport size
        double contentW = targetSize.Width;
        double contentH = targetSize.Height;
        double scrollOffsetX = 0;
        double scrollOffsetY = 0;

        if (target is ScrollViewer sv)
        {
            if (sv.ExtentWidth > 0) contentW = sv.ExtentWidth;
            if (sv.ExtentHeight > 0) contentH = sv.ExtentHeight;
            scrollOffsetX = sv.HorizontalOffset;
            scrollOffsetY = sv.VerticalOffset;
        }

        double scaleX = contentArea.Width / contentW;
        double scaleY = contentArea.Height / contentH;
        double scale = Math.Min(scaleX, scaleY);

        var brush = ContentBrush ?? s_defaultContentBrush;
        var pen = new Pen(s_outlineStrokeBrush, 0.5);

        switch (mode)
        {
            case MiniMapRenderMode.Outline:
                DrawOutlineContent(dc, contentArea, target, scale, brush, pen, scrollOffsetX, scrollOffsetY);
                break;

            case MiniMapRenderMode.Simplified:
                DrawSimplifiedContent(dc, contentArea, target, scale, brush, pen, contentW, contentH, scrollOffsetX, scrollOffsetY);
                break;

            case MiniMapRenderMode.Full:
                // Full rendering would require RenderTargetBitmap; fall back to simplified
                DrawSimplifiedContent(dc, contentArea, target, scale, brush, pen, contentW, contentH, scrollOffsetX, scrollOffsetY);
                break;
        }
    }

    private void DrawOutlineContent(DrawingContext dc, Rect contentArea, FrameworkElement target,
        double scale, Brush brush, Pen pen, double scrollOffsetX, double scrollOffsetY)
    {
        // Walk the target's visual children and draw scaled rectangles
        var children = GetVisualChildren(target);
        double offsetX = contentArea.X;
        double offsetY = contentArea.Y;

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var childSize = child.RenderSize;
            if (childSize.Width <= 0 || childSize.Height <= 0) continue;

            // Get relative position of child to target, then add scroll offset
            // to get position within the full scrollable content
            var relativePos = GetRelativePosition(child, target);

            var scaledRect = new Rect(
                offsetX + (relativePos.X + scrollOffsetX) * scale,
                offsetY + (relativePos.Y + scrollOffsetY) * scale,
                childSize.Width * scale,
                childSize.Height * scale);

            if (scaledRect.Width >= 1 && scaledRect.Height >= 1)
            {
                dc.DrawRectangle(brush, pen, scaledRect);
            }
        }
    }

    private void DrawSimplifiedContent(DrawingContext dc, Rect contentArea, FrameworkElement target,
        double scale, Brush brush, Pen pen, double contentW, double contentH,
        double scrollOffsetX, double scrollOffsetY)
    {
        // Draw a simplified representation: the full content area plus major children
        double offsetX = contentArea.X;
        double offsetY = contentArea.Y;

        // Draw the full content background area (using extent size, not just render size)
        var targetRect = new Rect(offsetX, offsetY, contentW * scale, contentH * scale);
        dc.DrawRectangle(brush, pen, targetRect);

        // Walk visual children and draw them as filled rectangles with slightly different shading
        var children = GetVisualChildren(target);
        var childBrush = new SolidColorBrush(Color.FromArgb(60, 100, 100, 100));

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var childSize = child.RenderSize;
            if (childSize.Width <= 0 || childSize.Height <= 0) continue;

            // Add scroll offset to get position within the full scrollable content
            var relativePos = GetRelativePosition(child, target);
            var scaledRect = new Rect(
                offsetX + (relativePos.X + scrollOffsetX) * scale,
                offsetY + (relativePos.Y + scrollOffsetY) * scale,
                childSize.Width * scale,
                childSize.Height * scale);

            if (scaledRect.Width >= 1 && scaledRect.Height >= 1)
            {
                dc.DrawRectangle(childBrush, pen, scaledRect);
            }
        }
    }

    private void DrawViewport(DrawingContext dc, Rect contentArea)
    {
        var viewportRect = ComputeViewportRect(contentArea);
        if (viewportRect.Width <= 0 || viewportRect.Height <= 0) return;

        var fillBrush = ViewportBrush ?? s_defaultViewportBrush;
        var borderBrush = ViewportBorderBrush ?? s_defaultViewportBorderBrush;
        var borderPen = new Pen(borderBrush, ViewportBorderThickness);

        dc.DrawRectangle(fillBrush, borderPen, viewportRect);
    }

    private void DrawHighlightMarkers(DrawingContext dc, Rect contentArea)
    {
        var markers = HighlightMarkers;
        if (markers == null || markers.Count == 0) return;

        for (int i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            double x = contentArea.X + marker.X * contentArea.Width;
            double y = contentArea.Y + marker.Y * contentArea.Height;
            double radius = marker.Size / 2.0;
            var brush = marker.Color ?? s_defaultMarkerBrush;

            dc.DrawEllipse(brush, null, new Point(x, y), radius, radius);
        }
    }

    /// <summary>
    /// Computes the viewport rectangle in mini-map coordinates that represents
    /// the visible area of the target control.
    /// </summary>
    private Rect ComputeViewportRect(Rect contentArea)
    {
        var mapView = MapViewTarget;
        if (mapView != null)
        {
            return ComputeMapViewViewport(contentArea, mapView);
        }

        var target = Target;
        if (target == null) return Rect.Empty;

        var targetSize = target.RenderSize;
        if (targetSize.Width <= 0 || targetSize.Height <= 0) return Rect.Empty;

        double scaleX = contentArea.Width / targetSize.Width;
        double scaleY = contentArea.Height / targetSize.Height;
        double scale = Math.Min(scaleX, scaleY);

        // If the target is a ScrollViewer or has scrollable content, compute visible portion
        // For a generic FrameworkElement, the viewport is the entire visible area
        // Use ActualWidth/ActualHeight vs content extent if available
        double viewportW = targetSize.Width;
        double viewportH = targetSize.Height;
        double scrollX = 0;
        double scrollY = 0;

        // Try to detect if the target has scroll offset
        if (target is ScrollViewer sv)
        {
            scrollX = sv.HorizontalOffset;
            scrollY = sv.VerticalOffset;
            viewportW = sv.ViewportWidth > 0 ? sv.ViewportWidth : targetSize.Width;
            viewportH = sv.ViewportHeight > 0 ? sv.ViewportHeight : targetSize.Height;
            var extentW = sv.ExtentWidth > 0 ? sv.ExtentWidth : targetSize.Width;
            var extentH = sv.ExtentHeight > 0 ? sv.ExtentHeight : targetSize.Height;

            return new Rect(
                contentArea.X + (scrollX / extentW) * contentArea.Width,
                contentArea.Y + (scrollY / extentH) * contentArea.Height,
                (viewportW / extentW) * contentArea.Width,
                (viewportH / extentH) * contentArea.Height);
        }

        // Default: viewport fills the entire content area
        return new Rect(contentArea.X, contentArea.Y, contentArea.Width, contentArea.Height);
    }

    private Rect ComputeMapViewViewport(Rect contentArea, MapView mapView)
    {
        // For MapView, compute the visible geographic area and map it to the mini-map
        // The mini-map shows a wider area at a lower zoom level
        double overviewZoom = Math.Max(0, mapView.ZoomLevel - 3);
        double mapSize = MercatorProjection.MapSize(overviewZoom);

        // Center of the overview
        var centerPixel = MercatorProjection.GeoToPixel(mapView.Center, overviewZoom);

        // The overview shows the full mini-map area
        double overviewScaleX = contentArea.Width / mapSize;
        double overviewScaleY = contentArea.Height / mapSize;
        double overviewScale = Math.Min(overviewScaleX, overviewScaleY);

        // Get the viewport corners of the main map
        var topLeft = mapView.ScreenToGeo(new Point(0, 0));
        var bottomRight = mapView.ScreenToGeo(new Point(mapView.RenderSize.Width, mapView.RenderSize.Height));

        var topLeftPixel = MercatorProjection.GeoToPixel(topLeft, overviewZoom);
        var bottomRightPixel = MercatorProjection.GeoToPixel(bottomRight, overviewZoom);

        // Convert to mini-map coordinates
        double miniCenterX = contentArea.X + contentArea.Width / 2.0;
        double miniCenterY = contentArea.Y + contentArea.Height / 2.0;

        double vpX = miniCenterX + (topLeftPixel.X - centerPixel.X) * overviewScale;
        double vpY = miniCenterY + (topLeftPixel.Y - centerPixel.Y) * overviewScale;
        double vpW = (bottomRightPixel.X - topLeftPixel.X) * overviewScale;
        double vpH = (bottomRightPixel.Y - topLeftPixel.Y) * overviewScale;

        return new Rect(vpX, vpY, Math.Max(4, vpW), Math.Max(4, vpH));
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            CaptureMouse();
            var position = e.GetPosition(this);
            var contentArea = new Rect(2, 2, RenderSize.Width - 4, RenderSize.Height - 4);
            var viewportRect = ComputeViewportRect(contentArea);

            if (viewportRect.Contains(position))
            {
                // Start dragging the viewport
                _isDraggingViewport = true;
                _dragOffset = new Point(position.X - viewportRect.X, position.Y - viewportRect.Y);
            }
            else
            {
                // Click to navigate - center the viewport on the click position
                NavigateToMiniMapPosition(position, contentArea);
            }

            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _isDraggingViewport = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (_isDraggingViewport)
        {
            var position = e.GetPosition(this);
            var contentArea = new Rect(2, 2, RenderSize.Width - 4, RenderSize.Height - 4);

            // Calculate the new center based on drag
            double newX = position.X - _dragOffset.X;
            double newY = position.Y - _dragOffset.Y;

            var viewportRect = ComputeViewportRect(contentArea);
            double centerX = newX + viewportRect.Width / 2.0;
            double centerY = newY + viewportRect.Height / 2.0;

            NavigateToMiniMapPosition(new Point(centerX, centerY), contentArea);
        }
    }

    private void NavigateToMiniMapPosition(Point miniMapPos, Rect contentArea)
    {
        var mapView = MapViewTarget;
        if (mapView != null)
        {
            // For MapView: convert mini-map position to geographic coordinates
            double overviewZoom = Math.Max(0, mapView.ZoomLevel - 3);
            double mapSize = MercatorProjection.MapSize(overviewZoom);

            var centerPixel = MercatorProjection.GeoToPixel(mapView.Center, overviewZoom);

            double overviewScaleX = contentArea.Width / mapSize;
            double overviewScaleY = contentArea.Height / mapSize;
            double overviewScale = Math.Min(overviewScaleX, overviewScaleY);

            double miniCenterX = contentArea.X + contentArea.Width / 2.0;
            double miniCenterY = contentArea.Y + contentArea.Height / 2.0;

            double pixelX = centerPixel.X + (miniMapPos.X - miniCenterX) / overviewScale;
            double pixelY = centerPixel.Y + (miniMapPos.Y - miniCenterY) / overviewScale;

            var geo = MercatorProjection.PixelToGeo(new Point(pixelX, pixelY), overviewZoom);
            mapView.PanTo(geo);
            InvalidateVisual();
            return;
        }

        var target = Target;
        if (target is ScrollViewer sv)
        {
            double normalizedX = Math.Clamp((miniMapPos.X - contentArea.X) / contentArea.Width, 0, 1);
            double normalizedY = Math.Clamp((miniMapPos.Y - contentArea.Y) / contentArea.Height, 0, 1);

            var extentW = sv.ExtentWidth > 0 ? sv.ExtentWidth : sv.RenderSize.Width;
            var extentH = sv.ExtentHeight > 0 ? sv.ExtentHeight : sv.RenderSize.Height;
            var viewportW = sv.ViewportWidth > 0 ? sv.ViewportWidth : sv.RenderSize.Width;
            var viewportH = sv.ViewportHeight > 0 ? sv.ViewportHeight : sv.RenderSize.Height;

            double scrollX = normalizedX * extentW - viewportW / 2.0;
            double scrollY = normalizedY * extentH - viewportH / 2.0;

            sv.ScrollToHorizontalOffset(Math.Max(0, scrollX));
            sv.ScrollToVerticalOffset(Math.Max(0, scrollY));
            InvalidateVisual();
        }
    }

    #endregion

    #region Helpers

    private static List<FrameworkElement> GetVisualChildren(FrameworkElement parent)
    {
        var result = new List<FrameworkElement>();
        CollectVisualChildren(parent, result, 0, 3); // Max depth of 3 for performance
        return result;
    }

    private static void CollectVisualChildren(FrameworkElement parent, List<FrameworkElement> result, int depth, int maxDepth)
    {
        if (depth >= maxDepth) return;

        int count = parent.VisualChildrenCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.GetVisualChild(i);
            if (child is FrameworkElement fe && fe.Visibility == Visibility.Visible)
            {
                result.Add(fe);
                CollectVisualChildren(fe, result, depth + 1, maxDepth);
            }
        }
    }

    private static Point GetRelativePosition(FrameworkElement child, FrameworkElement ancestor)
    {
        return child.TransformToAncestor(ancestor);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MiniMap miniMap)
        {
            miniMap.InvalidateVisual();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MiniMap miniMap)
        {
            miniMap.InvalidateVisual();
        }
    }

    #endregion
}
