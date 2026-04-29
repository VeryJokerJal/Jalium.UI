using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// A map control that displays slippy-map tiles with support for markers, polylines,
/// polygons, panning, zooming, and custom tile sources.
/// </summary>
public class MapView : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.MapViewAutomationPeer(this);
    }

    // Cached brushes
    private static readonly SolidColorBrush s_defaultMarkerBrush = new(Color.FromRgb(220, 50, 50));
    private static readonly SolidColorBrush s_defaultPolylineBrush = new(Color.FromRgb(50, 120, 220));
    private static readonly SolidColorBrush s_defaultPolygonFill = new(Color.FromArgb(80, 50, 120, 220));
    private static readonly SolidColorBrush s_defaultPolygonStroke = new(Color.FromRgb(50, 120, 220));
    private static readonly SolidColorBrush s_scaleBarBrush = new(Color.FromRgb(40, 40, 40));
    private static readonly SolidColorBrush s_scaleBarBackground = new(Color.FromArgb(180, 255, 255, 255));
    private static readonly SolidColorBrush s_attributionForeground = new(Color.FromRgb(80, 80, 80));
    private static readonly SolidColorBrush s_attributionBackground = new(Color.FromArgb(180, 255, 255, 255));
    private static readonly SolidColorBrush s_zoomButtonBackground = new(Color.FromArgb(220, 255, 255, 255));
    private static readonly SolidColorBrush s_zoomButtonForeground = new(Color.FromRgb(40, 40, 40));
    private static readonly SolidColorBrush s_mapBackground = new(Color.FromRgb(228, 232, 238));
    private static readonly SolidColorBrush s_markerLabelForeground = new(Color.FromRgb(30, 30, 30));

    #region Dependency Properties

    /// <summary>
    /// Identifies the Center dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty CenterProperty =
        DependencyProperty.Register(nameof(Center), typeof(GeoPoint), typeof(MapView),
            new PropertyMetadata(new GeoPoint(0, 0), OnViewPropertyChanged));

    /// <summary>
    /// Identifies the ZoomLevel dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register(nameof(ZoomLevel), typeof(double), typeof(MapView),
            new PropertyMetadata(2.0, OnViewPropertyChanged, CoerceZoomLevel));

    /// <summary>
    /// Identifies the MinZoomLevel dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinZoomLevelProperty =
        DependencyProperty.Register(nameof(MinZoomLevel), typeof(double), typeof(MapView),
            new PropertyMetadata(1.0));

    /// <summary>
    /// Identifies the MaxZoomLevel dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxZoomLevelProperty =
        DependencyProperty.Register(nameof(MaxZoomLevel), typeof(double), typeof(MapView),
            new PropertyMetadata(19.0));

    /// <summary>
    /// Identifies the TileSource dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty TileSourceProperty =
        DependencyProperty.Register(nameof(TileSource), typeof(MapTileSource), typeof(MapView),
            new PropertyMetadata(MapTileSource.OpenStreetMap, OnTileSourceChanged));

    /// <summary>
    /// Identifies the Markers dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty MarkersProperty =
        DependencyProperty.Register(nameof(Markers), typeof(MapMarkerCollection), typeof(MapView),
            new PropertyMetadata(null, OnCollectionPropertyChanged));

    /// <summary>
    /// Identifies the Polylines dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PolylinesProperty =
        DependencyProperty.Register(nameof(Polylines), typeof(MapPolylineCollection), typeof(MapView),
            new PropertyMetadata(null, OnCollectionPropertyChanged));

    /// <summary>
    /// Identifies the Polygons dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PolygonsProperty =
        DependencyProperty.Register(nameof(Polygons), typeof(MapPolygonCollection), typeof(MapView),
            new PropertyMetadata(null, OnCollectionPropertyChanged));

    /// <summary>
    /// Identifies the ShowZoomControls dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowZoomControlsProperty =
        DependencyProperty.Register(nameof(ShowZoomControls), typeof(bool), typeof(MapView),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowScaleBar dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowScaleBarProperty =
        DependencyProperty.Register(nameof(ShowScaleBar), typeof(bool), typeof(MapView),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowAttribution dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowAttributionProperty =
        DependencyProperty.Register(nameof(ShowAttribution), typeof(bool), typeof(MapView),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsPanEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsPanEnabledProperty =
        DependencyProperty.Register(nameof(IsPanEnabled), typeof(bool), typeof(MapView),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the IsZoomEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsZoomEnabledProperty =
        DependencyProperty.Register(nameof(IsZoomEnabled), typeof(bool), typeof(MapView),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the MarkerTemplate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty MarkerTemplateProperty =
        DependencyProperty.Register(nameof(MarkerTemplate), typeof(DataTemplate), typeof(MapView),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the MapClick routed event.
    /// </summary>
    public static readonly RoutedEvent MapClickEvent =
        EventManager.RegisterRoutedEvent(nameof(MapClick), RoutingStrategy.Bubble,
            typeof(EventHandler<MapClickEventArgs>), typeof(MapView));

    /// <summary>
    /// Occurs when the user clicks on the map surface.
    /// </summary>
    public event EventHandler<MapClickEventArgs> MapClick
    {
        add => AddHandler(MapClickEvent, value);
        remove => RemoveHandler(MapClickEvent, value);
    }

    /// <summary>
    /// Identifies the ViewChanged routed event.
    /// </summary>
    public static readonly RoutedEvent ViewChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ViewChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<MapViewChangedEventArgs>), typeof(MapView));

    /// <summary>
    /// Occurs when the map view (center or zoom) changes.
    /// </summary>
    public event EventHandler<MapViewChangedEventArgs> ViewChanged
    {
        add => AddHandler(ViewChangedEvent, value);
        remove => RemoveHandler(ViewChangedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the geographic center of the map view.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public GeoPoint Center
    {
        get => (GeoPoint)GetValue(CenterProperty)!;
        set => SetValue(CenterProperty, value);
    }

    /// <summary>
    /// Gets or sets the zoom level.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double ZoomLevel
    {
        get => (double)GetValue(ZoomLevelProperty)!;
        set => SetValue(ZoomLevelProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum zoom level.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MinZoomLevel
    {
        get => (double)GetValue(MinZoomLevelProperty)!;
        set => SetValue(MinZoomLevelProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum zoom level.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxZoomLevel
    {
        get => (double)GetValue(MaxZoomLevelProperty)!;
        set => SetValue(MaxZoomLevelProperty, value);
    }

    /// <summary>
    /// Gets or sets the tile source.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public MapTileSource TileSource
    {
        get => (MapTileSource)(GetValue(TileSourceProperty) ?? MapTileSource.OpenStreetMap);
        set => SetValue(TileSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the collection of markers.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public MapMarkerCollection? Markers
    {
        get => (MapMarkerCollection?)GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    /// <summary>
    /// Gets or sets the collection of polylines.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public MapPolylineCollection? Polylines
    {
        get => (MapPolylineCollection?)GetValue(PolylinesProperty);
        set => SetValue(PolylinesProperty, value);
    }

    /// <summary>
    /// Gets or sets the collection of polygons.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public MapPolygonCollection? Polygons
    {
        get => (MapPolygonCollection?)GetValue(PolygonsProperty);
        set => SetValue(PolygonsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the zoom controls are visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowZoomControls
    {
        get => (bool)GetValue(ShowZoomControlsProperty)!;
        set => SetValue(ShowZoomControlsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the scale bar is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowScaleBar
    {
        get => (bool)GetValue(ShowScaleBarProperty)!;
        set => SetValue(ShowScaleBarProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the attribution text is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowAttribution
    {
        get => (bool)GetValue(ShowAttributionProperty)!;
        set => SetValue(ShowAttributionProperty, value);
    }

    /// <summary>
    /// Gets or sets whether panning by mouse drag is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsPanEnabled
    {
        get => (bool)GetValue(IsPanEnabledProperty)!;
        set => SetValue(IsPanEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether zooming by mouse wheel is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsZoomEnabled
    {
        get => (bool)GetValue(IsZoomEnabledProperty)!;
        set => SetValue(IsZoomEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the optional DataTemplate for rendering markers.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public DataTemplate? MarkerTemplate
    {
        get => (DataTemplate?)GetValue(MarkerTemplateProperty);
        set => SetValue(MarkerTemplateProperty, value);
    }

    #endregion

    #region Private Fields

    private readonly MapTileCache _tileCache = new();

    // Drag state
    private bool _isDragging;
    private Point _dragStart;
    private GeoPoint _dragStartCenter;

    // Hit-test rectangles for the on-rendered zoom buttons. Updated by
    // DrawZoomControls each frame and read by OnMouseDownHandler.
    private Rect _zoomInButtonRect;
    private Rect _zoomOutButtonRect;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="MapView"/> class.
    /// </summary>
    public MapView()
    {
        Focusable = true;
        ClipToBounds = true;

        Markers = new MapMarkerCollection();
        Polylines = new MapPolylineCollection();
        Polygons = new MapPolygonCollection();

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheelHandler));
    }

    #endregion

    // MapView is intentionally template-less: every visual element (tiles,
    // markers, polylines, polygons, scale bar, attribution, zoom buttons)
    // is drawn directly in OnRender. Visual children would render on top of
    // OnRender's output and occlude the map surface, so a ControlTemplate
    // is deliberately omitted from Maps.jalxaml.

    #region Public Methods

    /// <summary>
    /// Sets the map view to the specified center and zoom level.
    /// </summary>
    /// <param name="center">The geographic center.</param>
    /// <param name="zoom">The zoom level.</param>
    public void SetView(GeoPoint center, double zoom)
    {
        Center = center;
        ZoomLevel = zoom;
    }

    /// <summary>
    /// Pans the map to the specified geographic location without changing the zoom level.
    /// </summary>
    /// <param name="geo">The geographic location to pan to.</param>
    public void PanTo(GeoPoint geo)
    {
        Center = geo;
    }

    /// <summary>
    /// Increases the zoom level by one step.
    /// </summary>
    public void ZoomIn()
    {
        ZoomLevel = Math.Min(ZoomLevel + 1, MaxZoomLevel);
    }

    /// <summary>
    /// Decreases the zoom level by one step.
    /// </summary>
    public void ZoomOut()
    {
        ZoomLevel = Math.Max(ZoomLevel - 1, MinZoomLevel);
    }

    /// <summary>
    /// Converts a geographic point to a screen point relative to this control.
    /// </summary>
    /// <param name="geo">The geographic point.</param>
    /// <returns>The screen point relative to this control.</returns>
    public Point GeoToScreen(GeoPoint geo)
    {
        var centerPixel = MercatorProjection.GeoToPixel(Center, ZoomLevel);
        var geoPixel = MercatorProjection.GeoToPixel(geo, ZoomLevel);
        var halfWidth = RenderSize.Width / 2.0;
        var halfHeight = RenderSize.Height / 2.0;
        return new Point(
            geoPixel.X - centerPixel.X + halfWidth,
            geoPixel.Y - centerPixel.Y + halfHeight);
    }

    /// <summary>
    /// Converts a screen point relative to this control to a geographic point.
    /// </summary>
    /// <param name="screen">The screen point relative to this control.</param>
    /// <returns>The geographic point.</returns>
    public GeoPoint ScreenToGeo(Point screen)
    {
        var centerPixel = MercatorProjection.GeoToPixel(Center, ZoomLevel);
        var halfWidth = RenderSize.Width / 2.0;
        var halfHeight = RenderSize.Height / 2.0;
        var pixelX = screen.X - halfWidth + centerPixel.X;
        var pixelY = screen.Y - halfHeight + centerPixel.Y;
        return MercatorProjection.PixelToGeo(new Point(pixelX, pixelY), ZoomLevel);
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
            var position = e.GetPosition(this);

            // Hit-test the zoom buttons that OnRender draws as part of the
            // map overlay. They are not real Visual children, so input must
            // be dispatched manually from the rectangles cached during the
            // last DrawZoomControls pass.
            if (ShowZoomControls)
            {
                if (_zoomInButtonRect.Contains(position))
                {
                    ZoomIn();
                    e.Handled = true;
                    return;
                }
                if (_zoomOutButtonRect.Contains(position))
                {
                    ZoomOut();
                    e.Handled = true;
                    return;
                }
            }

            if (IsPanEnabled)
            {
                CaptureMouse();
                _isDragging = true;
                _dragStart = position;
                _dragStartCenter = Center;
            }

            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            var position = e.GetPosition(this);
            bool wasDragging = _isDragging;
            var dragDistance = wasDragging
                ? Math.Sqrt(Math.Pow(position.X - _dragStart.X, 2) + Math.Pow(position.Y - _dragStart.Y, 2))
                : 0;

            _isDragging = false;
            ReleaseMouseCapture();

            // If the mouse didn't move much, treat it as a click
            if (wasDragging && dragDistance < 3)
            {
                var geo = ScreenToGeo(position);
                RaiseEvent(new MapClickEventArgs(MapClickEvent, geo, position));
            }

            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (_isDragging && IsPanEnabled)
        {
            var position = e.GetPosition(this);
            var dx = position.X - _dragStart.X;
            var dy = position.Y - _dragStart.Y;

            // Convert the pixel drag offset to a geographic offset
            var startPixel = MercatorProjection.GeoToPixel(_dragStartCenter, ZoomLevel);
            var newPixel = new Point(startPixel.X - dx, startPixel.Y - dy);
            Center = MercatorProjection.PixelToGeo(newPixel, ZoomLevel);
        }
    }

    private void OnMouseWheelHandler(object sender, MouseWheelEventArgs e)
    {
        if (!IsEnabled || !IsZoomEnabled) return;

        var position = e.GetPosition(this);
        var geoBefore = ScreenToGeo(position);

        // Zoom in or out
        double delta = e.Delta > 0 ? 1 : -1;
        double newZoom = Math.Clamp(ZoomLevel + delta, MinZoomLevel, MaxZoomLevel);

        if (Math.Abs(newZoom - ZoomLevel) > 0.001)
        {
            ZoomLevel = newZoom;

            // Adjust center so the geographic point under the cursor stays in place
            var geoAfter = ScreenToGeo(position);
            var centerPixel = MercatorProjection.GeoToPixel(Center, ZoomLevel);
            var beforePixel = MercatorProjection.GeoToPixel(geoBefore, ZoomLevel);
            var afterPixel = MercatorProjection.GeoToPixel(geoAfter, ZoomLevel);
            var correctedPixel = new Point(
                centerPixel.X + (beforePixel.X - afterPixel.X),
                centerPixel.Y + (beforePixel.Y - afterPixel.Y));
            Center = MercatorProjection.PixelToGeo(correctedPixel, ZoomLevel);
        }

        e.Handled = true;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // MapView takes all available space
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

        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var cornerRadius = CornerRadius;
        var borderThickness = BorderThickness;
        var borderBrush = BorderBrush;
        bool hasCorners = cornerRadius.TopLeft > 0 || cornerRadius.TopRight > 0
            || cornerRadius.BottomLeft > 0 || cornerRadius.BottomRight > 0;

        // Clip the map surface to the (possibly rounded) control bounds so
        // tiles/markers don't leak past the styled border.
        if (hasCorners)
        {
            dc.PushClip(CreateRoundedClip(bounds, cornerRadius));
        }
        else
        {
            dc.PushClip(new RectangleGeometry(bounds));
        }

        // Draw background — Style sets Background to SurfaceBackground, so
        // honour it; only fall back to the neutral map colour when nothing
        // is supplied (e.g. when MapView is used outside any theme).
        var bgBrush = Background ?? s_mapBackground;
        dc.DrawRectangle(bgBrush, null, bounds);

        // Draw tiles
        DrawTiles(dc, bounds);

        // Draw polygons
        DrawPolygons(dc, bounds);

        // Draw polylines
        DrawPolylines(dc, bounds);

        // Draw markers
        DrawMarkers(dc, bounds);

        // Draw scale bar
        if (ShowScaleBar)
            DrawScaleBar(dc, bounds);

        // Draw attribution
        if (ShowAttribution)
            DrawAttribution(dc, bounds);

        // Draw zoom controls
        if (ShowZoomControls)
            DrawZoomControls(dc, bounds);

        dc.Pop(); // clip

        // Draw the styled border on top of the map content so it's not
        // overwritten by tile pixels along the edges.
        if (borderBrush != null && (borderThickness.Left > 0 || borderThickness.Top > 0
            || borderThickness.Right > 0 || borderThickness.Bottom > 0))
        {
            DrawBorder(dc, bounds, borderBrush, borderThickness, cornerRadius);
        }
    }

    private static Geometry CreateRoundedClip(Rect bounds, CornerRadius cornerRadius)
    {
        if (cornerRadius.TopLeft == cornerRadius.TopRight
            && cornerRadius.TopLeft == cornerRadius.BottomLeft
            && cornerRadius.TopLeft == cornerRadius.BottomRight)
        {
            return new RectangleGeometry(bounds, cornerRadius.TopLeft, cornerRadius.TopLeft);
        }
        // Uneven corners — fall back to a rectangle clip; uneven CornerRadius
        // on MapView is unusual and the styled border still paints the visual
        // rounded outline on top.
        return new RectangleGeometry(bounds);
    }

    private static void DrawBorder(DrawingContext dc, Rect bounds, Brush borderBrush,
        Thickness borderThickness, CornerRadius cornerRadius)
    {
        // Use uniform thickness when all sides are equal — that's the common
        // theme case (BorderThickness="1") and yields a clean stroke that
        // sits on the bounds. For non-uniform thickness fall back to the
        // simplest correct approach: stroke each side as a filled rectangle.
        if (borderThickness.Left == borderThickness.Top
            && borderThickness.Left == borderThickness.Right
            && borderThickness.Left == borderThickness.Bottom)
        {
            double t = borderThickness.Left;
            double half = t / 2.0;
            var inset = new Rect(bounds.X + half, bounds.Y + half,
                Math.Max(0, bounds.Width - t), Math.Max(0, bounds.Height - t));
            var pen = new Pen(borderBrush, t);
            if (cornerRadius.TopLeft > 0)
            {
                dc.DrawRoundedRectangle(null, pen, inset, cornerRadius.TopLeft, cornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(null, pen, inset);
            }
            return;
        }

        // Non-uniform: paint each edge separately. CornerRadius is ignored in
        // this rare path — controls rarely combine non-uniform thickness with
        // rounded corners.
        if (borderThickness.Top > 0)
            dc.DrawRectangle(borderBrush, null, new Rect(bounds.X, bounds.Y, bounds.Width, borderThickness.Top));
        if (borderThickness.Bottom > 0)
            dc.DrawRectangle(borderBrush, null, new Rect(bounds.X, bounds.Bottom - borderThickness.Bottom, bounds.Width, borderThickness.Bottom));
        if (borderThickness.Left > 0)
            dc.DrawRectangle(borderBrush, null, new Rect(bounds.X, bounds.Y, borderThickness.Left, bounds.Height));
        if (borderThickness.Right > 0)
            dc.DrawRectangle(borderBrush, null, new Rect(bounds.Right - borderThickness.Right, bounds.Y, borderThickness.Right, bounds.Height));
    }

    private void DrawTiles(DrawingContext dc, Rect bounds)
    {
        var tileSource = TileSource;
        double zoom = ZoomLevel;
        int intZoom = (int)Math.Floor(zoom);
        intZoom = Math.Clamp(intZoom, tileSource.MinZoom, tileSource.MaxZoom);

        var centerPixel = MercatorProjection.GeoToPixel(Center, intZoom);
        double halfWidth = bounds.Width / 2.0;
        double halfHeight = bounds.Height / 2.0;

        // Calculate the visible pixel range
        double leftPixel = centerPixel.X - halfWidth;
        double topPixel = centerPixel.Y - halfHeight;
        double rightPixel = centerPixel.X + halfWidth;
        double bottomPixel = centerPixel.Y + halfHeight;

        int tileSize = MercatorProjection.TileSize;
        int maxTile = MercatorProjection.TileCount(intZoom);

        // Calculate tile range
        int tileLeft = (int)Math.Floor(leftPixel / tileSize);
        int tileTop = (int)Math.Floor(topPixel / tileSize);
        int tileRight = (int)Math.Floor(rightPixel / tileSize);
        int tileBottom = (int)Math.Floor(bottomPixel / tileSize);

        for (int ty = tileTop; ty <= tileBottom; ty++)
        {
            for (int tx = tileLeft; tx <= tileRight; tx++)
            {
                // Wrap X coordinate for world wrapping
                int wrappedX = ((tx % maxTile) + maxTile) % maxTile;
                int clampedY = ty;

                if (clampedY < 0 || clampedY >= maxTile) continue;

                var tileImage = _tileCache.GetOrLoadTile(intZoom, wrappedX, clampedY, tileSource, () =>
                {
                    // Tile loaded callback - invalidate visual to redraw
                    InvalidateVisual();
                });

                // Calculate screen position for this tile
                double screenX = tx * tileSize - leftPixel;
                double screenY = ty * tileSize - topPixel;

                var tileRect = new Rect(screenX, screenY, tileSize, tileSize);
                dc.DrawImage(tileImage, tileRect);
            }
        }
    }

    private void DrawPolylines(DrawingContext dc, Rect bounds)
    {
        var polylines = Polylines;
        if (polylines == null || polylines.Count == 0) return;

        for (int i = 0; i < polylines.Count; i++)
        {
            var polyline = polylines[i];
            var points = polyline.Points;
            if (points == null || points.Count < 2) continue;

            var strokeBrush = polyline.Stroke ?? s_defaultPolylineBrush;
            var pen = new Pen(strokeBrush, polyline.StrokeThickness);

            var screenPoints = new Point[points.Count];
            for (int j = 0; j < points.Count; j++)
            {
                screenPoints[j] = GeoToScreen(points[j]);
            }

            for (int j = 1; j < screenPoints.Length; j++)
            {
                dc.DrawLine(pen, screenPoints[j - 1], screenPoints[j]);
            }
        }
    }

    private void DrawPolygons(DrawingContext dc, Rect bounds)
    {
        var polygons = Polygons;
        if (polygons == null || polygons.Count == 0) return;

        for (int i = 0; i < polygons.Count; i++)
        {
            var polygon = polygons[i];
            var points = polygon.Points;
            if (points == null || points.Count < 3) continue;

            var fillBrush = polygon.Fill ?? s_defaultPolygonFill;
            var strokeBrush = polygon.Stroke ?? s_defaultPolygonStroke;
            var pen = new Pen(strokeBrush, polygon.StrokeThickness);

            var figure = new PathFigure();
            figure.StartPoint = GeoToScreen(points[0]);
            figure.IsClosed = true;
            figure.IsFilled = true;

            for (int j = 1; j < points.Count; j++)
            {
                figure.Segments.Add(new LineSegment(GeoToScreen(points[j]), true));
            }

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            // Apply fill opacity
            double fillOpacity = polygon.FillOpacity;
            if (fillOpacity < 1.0)
            {
                dc.PushOpacity(fillOpacity);
                dc.DrawGeometry(fillBrush, null, geometry);
                dc.Pop();
                dc.DrawGeometry(null, pen, geometry);
            }
            else
            {
                dc.DrawGeometry(fillBrush, pen, geometry);
            }
        }
    }

    private void DrawMarkers(DrawingContext dc, Rect bounds)
    {
        var markers = Markers;
        if (markers == null || markers.Count == 0) return;

        for (int i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            var screen = GeoToScreen(marker.Location);

            // Skip markers outside visible bounds (with padding for marker size)
            double markerSize = marker.MarkerSize;
            if (screen.X < -markerSize || screen.X > bounds.Width + markerSize ||
                screen.Y < -markerSize || screen.Y > bounds.Height + markerSize)
                continue;

            var fillBrush = marker.Fill ?? s_defaultMarkerBrush;
            double radius = markerSize / 2.0;

            // Draw marker pin: a filled circle with a white border
            dc.DrawEllipse(fillBrush, new Pen(s_scaleBarBackground, 1.5), screen, radius, radius);

            // Draw label if present
            var label = marker.Label;
            if (!string.IsNullOrEmpty(label))
            {
                var fontSize = Math.Max(10, markerSize * 0.8);
                var ft = new FormattedText(label, FontFamily ?? FrameworkElement.DefaultFontFamilyName, fontSize)
                {
                    Foreground = s_markerLabelForeground
                };
                TextMeasurement.MeasureText(ft);

                // Position label above the marker
                var labelX = screen.X - ft.Width / 2.0;
                var labelY = screen.Y - radius - ft.Height - 2;

                // Draw label background
                var labelBg = new Rect(labelX - 2, labelY - 1, ft.Width + 4, ft.Height + 2);
                dc.DrawRoundedRectangle(s_attributionBackground, null, labelBg, 2, 2);

                dc.DrawText(ft, new Point(labelX, labelY));
            }
        }
    }

    private void DrawScaleBar(DrawingContext dc, Rect bounds)
    {
        const double barMaxWidth = 120;
        const double barHeight = 4;
        const double margin = 10;
        const double fontSize = 10;

        // Calculate ground resolution at center latitude
        double metersPerPixel = MercatorProjection.GroundResolution(Center.Latitude, ZoomLevel);

        // Find a nice round distance for the scale bar
        double maxDistance = barMaxWidth * metersPerPixel;
        double niceDistance = GetNiceScaleDistance(maxDistance);
        double barWidth = niceDistance / metersPerPixel;

        // Format the distance label
        string label;
        if (niceDistance >= 1000)
            label = $"{niceDistance / 1000:F0} km";
        else
            label = $"{niceDistance:F0} m";

        double x = margin;
        double y = bounds.Height - margin - barHeight - fontSize - 4;

        // Draw background
        var bgRect = new Rect(x - 4, y - 2, barWidth + 8, barHeight + fontSize + 8);
        dc.DrawRoundedRectangle(s_scaleBarBackground, null, bgRect, 3, 3);

        // Draw label
        var ft = new FormattedText(label, FontFamily ?? FrameworkElement.DefaultFontFamilyName, fontSize)
        {
            Foreground = s_scaleBarBrush
        };
        TextMeasurement.MeasureText(ft);
        dc.DrawText(ft, new Point(x, y));

        // Draw bar
        y += fontSize + 2;
        dc.DrawRectangle(s_scaleBarBrush, null, new Rect(x, y, barWidth, barHeight));

        // Draw end caps
        dc.DrawRectangle(s_scaleBarBrush, null, new Rect(x, y - 2, 1, barHeight + 4));
        dc.DrawRectangle(s_scaleBarBrush, null, new Rect(x + barWidth - 1, y - 2, 1, barHeight + 4));
    }

    private static double GetNiceScaleDistance(double maxDistance)
    {
        // Find the closest "nice" distance: 1, 2, 5, 10, 20, 50, 100, 200, 500, ...
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(maxDistance)));
        double normalized = maxDistance / magnitude;

        double nice;
        if (normalized >= 5) nice = 5;
        else if (normalized >= 2) nice = 2;
        else nice = 1;

        return nice * magnitude;
    }

    private void DrawAttribution(DrawingContext dc, Rect bounds)
    {
        var tileSource = TileSource;
        if (string.IsNullOrEmpty(tileSource.Attribution)) return;

        const double fontSize = 9;
        const double margin = 4;

        var ft = new FormattedText(tileSource.Attribution, FontFamily ?? FrameworkElement.DefaultFontFamilyName, fontSize)
        {
            Foreground = s_attributionForeground
        };
        TextMeasurement.MeasureText(ft);

        double x = bounds.Width - ft.Width - margin * 2;
        double y = bounds.Height - ft.Height - margin * 2;

        // Background
        var bgRect = new Rect(x - margin, y - margin / 2, ft.Width + margin * 2, ft.Height + margin);
        dc.DrawRectangle(s_attributionBackground, null, bgRect);

        dc.DrawText(ft, new Point(x, y));
    }

    private void DrawZoomControls(DrawingContext dc, Rect bounds)
    {
        const double buttonSize = 28;
        const double margin = 10;
        const double spacing = 2;
        const double fontSize = 16;

        double x = bounds.Width - buttonSize - margin;
        double y = margin;

        // Zoom in button (+)
        var zoomInRect = new Rect(x, y, buttonSize, buttonSize);
        dc.DrawRoundedRectangle(s_zoomButtonBackground, new Pen(s_scaleBarBrush, 0.5), zoomInRect, 4, 4);

        var plusText = new FormattedText("+", FontFamily ?? FrameworkElement.DefaultFontFamilyName, fontSize)
        {
            Foreground = s_zoomButtonForeground
        };
        TextMeasurement.MeasureText(plusText);
        dc.DrawText(plusText, new Point(
            x + (buttonSize - plusText.Width) / 2,
            y + (buttonSize - plusText.Height) / 2));

        // Zoom out button (-)
        y += buttonSize + spacing;
        var zoomOutRect = new Rect(x, y, buttonSize, buttonSize);
        dc.DrawRoundedRectangle(s_zoomButtonBackground, new Pen(s_scaleBarBrush, 0.5), zoomOutRect, 4, 4);

        var minusText = new FormattedText("\u2212", FontFamily ?? FrameworkElement.DefaultFontFamilyName, fontSize)
        {
            Foreground = s_zoomButtonForeground
        };
        TextMeasurement.MeasureText(minusText);
        dc.DrawText(minusText, new Point(
            x + (buttonSize - minusText.Width) / 2,
            y + (buttonSize - minusText.Height) / 2));

        // Store button rects for hit testing
        _zoomInButtonRect = zoomInRect;
        _zoomOutButtonRect = zoomOutRect;
    }

    #endregion

    #region Visual State

    private void UpdateVisualState()
    {
        InvalidateVisual();
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnViewPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MapView map)
        {
            map.InvalidateVisual();
            map.RaiseEvent(new MapViewChangedEventArgs(ViewChangedEvent, map.Center, map.ZoomLevel));
        }
    }

    private static void OnTileSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MapView map)
        {
            map._tileCache.Clear();
            map.UpdateVisualState();
        }
    }

    private static void OnCollectionPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MapView map)
        {
            map.InvalidateVisual();
        }
    }

    private new static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MapView map)
        {
            map.UpdateVisualState();
        }
    }

    private static object CoerceZoomLevel(DependencyObject d, object? value)
    {
        if (d is MapView map && value is double zoom)
        {
            return Math.Clamp(zoom, map.MinZoomLevel, map.MaxZoomLevel);
        }
        return value ?? 0.0;
    }

    #endregion
}
