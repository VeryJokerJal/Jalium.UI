using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// A map control that displays slippy-map tiles with support for markers, polylines,
/// polygons, panning, zooming, and custom tile sources.
/// </summary>
public partial class MapView : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.MapViewAutomationPeer(this);
    }

    // Cached brushes
    private static readonly SolidColorBrush s_defaultMarkerBrush = new(Color.FromRgb(220, 50, 50));
    private static readonly SolidColorBrush s_defaultPolylineBrush = new(Color.FromRgb(50, 120, 220));
    private static readonly SolidColorBrush s_defaultPolygonFill = new(Color.FromArgb(80, 50, 120, 220));
    private static readonly SolidColorBrush s_defaultPolygonStroke = new(Color.FromRgb(50, 120, 220));
    private static readonly SolidColorBrush s_scaleBarBackground = new(Color.FromArgb(180, 255, 255, 255));
    private static readonly SolidColorBrush s_attributionBackground = new(Color.FromArgb(180, 255, 255, 255));
    private static readonly SolidColorBrush s_markerLabelForeground = new(Color.FromRgb(30, 30, 30));

    // 标记描边和缩放按钮描边的画刷与线宽都是编译期常量，而标记那支画笔原本在
    // 逐标记的循环里新建 —— 平移拖动时每次指针移动都要重来一遍整个循环。
    private static readonly Pen s_markerOutlinePen = new(s_scaleBarBackground, 1.5);

    // 折线/多边形的描边画刷来自各自的模型对象，同一帧内多数共用同一支默认画刷，
    // 单槽缓存能吃掉绝大多数分配；画刷真的换了就退回新建。
    private RenderPenCache _polylinePen;
    private RenderPenCache _polygonPen;

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

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="MapView"/> class.
    /// </summary>
    public MapView()
    {
        Focusable = true;
        InitializeMapTemplateBehavior();
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
        if (!IsEnabled || IsMapOverlaySource(e.OriginalSource)) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
            var position = e.GetPosition(this);

            if (IsPanEnabled)
            {
                CaptureMouse();
                _isDragging = true;
                _dragStart = position;
                _dragStartCenter = Center;
                RefreshMapPresentation();
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

            if(IsMapOverlaySource(e.OriginalSource))return;
            _isDragging = false;
            ReleaseMouseCapture();
            RefreshMapPresentation();

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
        base.MeasureOverride(availableSize);
        // MapView takes all available space
        double w = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 300 : availableSize.Height;
        return new Size(w, h);
    }

    #endregion

    protected override Size ArrangeOverride(Size finalSize){base.ArrangeOverride(finalSize);return finalSize;}

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if(TemplateRootInternal is null)RenderMapSurface(drawingContext);
    }

    internal void RenderMapSurface(DrawingContext dc)
    {
        var bounds=new Rect(RenderSize);if(bounds.Width<=0||bounds.Height<=0)return;
        dc.PushClip(new RectangleGeometry(bounds));
        try{DrawTiles(dc,bounds);DrawPolygons(dc,bounds);DrawPolylines(dc,bounds);DrawMarkers(dc,bounds);}
        finally{dc.Pop();}
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
                    InvalidateMapView();
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
            var pen = _polylinePen.Get(strokeBrush, polyline.StrokeThickness);

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
            var pen = _polygonPen.Get(strokeBrush, polygon.StrokeThickness);

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
            dc.DrawEllipse(fillBrush, s_markerOutlinePen, screen, radius, radius);

            // Draw label if present
            var label = marker.Label;
            if (!string.IsNullOrEmpty(label))
            {
                var fontSize = Math.Max(10, markerSize * 0.8);
                var ft = new FormattedText(label, FontFamily?.GetRenderingSource(this) ?? FrameworkElement.DefaultFontFamilyName, fontSize)
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

    #endregion

    #region Visual State

    private void UpdateVisualState()
    {
        InvalidateMapView();
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnViewPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MapView map)
        {
            map.InvalidateMapView();
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
            map.InvalidateMapView();
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
