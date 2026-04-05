using System.Collections.ObjectModel;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a geographic point with latitude and longitude.
/// </summary>
public readonly struct GeoPoint : IEquatable<GeoPoint>
{
    /// <summary>
    /// Gets the latitude in degrees (-85.05 to 85.05 for Web Mercator).
    /// </summary>
    public double Latitude { get; init; }

    /// <summary>
    /// Gets the longitude in degrees (-180 to 180).
    /// </summary>
    public double Longitude { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GeoPoint"/> struct.
    /// </summary>
    public GeoPoint(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <inheritdoc />
    public bool Equals(GeoPoint other)
        => Latitude.Equals(other.Latitude) && Longitude.Equals(other.Longitude);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GeoPoint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Latitude, Longitude);

    /// <inheritdoc />
    public override string ToString() => $"({Latitude:F6}, {Longitude:F6})";

    /// <summary>Equality operator.</summary>
    public static bool operator ==(GeoPoint left, GeoPoint right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(GeoPoint left, GeoPoint right) => !left.Equals(right);
}

/// <summary>
/// Describes a tile source for the map control.
/// </summary>
public class MapTileSource
{
    /// <summary>
    /// Gets or sets the URL template for tiles. Use {z}, {x}, {y} as placeholders.
    /// </summary>
    public string UrlTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the attribution text for the tile source.
    /// </summary>
    public string? Attribution { get; set; }

    /// <summary>
    /// Gets or sets the minimum zoom level supported by this tile source.
    /// </summary>
    public int MinZoom { get; set; }

    /// <summary>
    /// Gets or sets the maximum zoom level supported by this tile source.
    /// </summary>
    public int MaxZoom { get; set; } = 19;

    /// <summary>
    /// Gets or sets the size of a single tile in pixels.
    /// </summary>
    public int TileSize { get; set; } = 256;

    /// <summary>
    /// Gets the default OpenStreetMap tile source.
    /// </summary>
    public static MapTileSource OpenStreetMap { get; } = new()
    {
        UrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
        Attribution = "\u00a9 OpenStreetMap contributors",
        MinZoom = 0,
        MaxZoom = 19,
        TileSize = 256
    };

    /// <summary>
    /// Builds the URL for a specific tile.
    /// </summary>
    /// <param name="zoom">The zoom level.</param>
    /// <param name="x">The tile X index.</param>
    /// <param name="y">The tile Y index.</param>
    /// <returns>The URL for the tile.</returns>
    public string GetTileUrl(int zoom, int x, int y)
    {
        return UrlTemplate
            .Replace("{z}", zoom.ToString(), StringComparison.Ordinal)
            .Replace("{x}", x.ToString(), StringComparison.Ordinal)
            .Replace("{y}", y.ToString(), StringComparison.Ordinal);
    }
}

/// <summary>
/// Represents a marker placed on a <see cref="MapView"/> at a geographic location.
/// </summary>
public class MapMarker : DependencyObject
{
    /// <summary>
    /// Identifies the Location dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty LocationProperty =
        DependencyProperty.Register(nameof(Location), typeof(GeoPoint), typeof(MapMarker),
            new PropertyMetadata(default(GeoPoint)));

    /// <summary>
    /// Identifies the Label dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(MapMarker),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the Fill dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Jalium.UI.Media.Brush), typeof(MapMarker),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the MarkerSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty MarkerSizeProperty =
        DependencyProperty.Register(nameof(MarkerSize), typeof(double), typeof(MapMarker),
            new PropertyMetadata(12.0));

    /// <summary>
    /// Identifies the Tag dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty TagProperty =
        DependencyProperty.Register(nameof(Tag), typeof(object), typeof(MapMarker),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the geographic location of the marker.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public GeoPoint Location
    {
        get => (GeoPoint)GetValue(LocationProperty)!;
        set => SetValue(LocationProperty, value);
    }

    /// <summary>
    /// Gets or sets the label text for the marker.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string Label
    {
        get => (string)(GetValue(LabelProperty) ?? string.Empty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// Gets or sets the fill brush for the marker.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Jalium.UI.Media.Brush? Fill
    {
        get => (Jalium.UI.Media.Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>
    /// Gets or sets the size of the marker in pixels.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double MarkerSize
    {
        get => (double)GetValue(MarkerSizeProperty)!;
        set => SetValue(MarkerSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets an arbitrary tag object associated with this marker.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public object? Tag
    {
        get => GetValue(TagProperty);
        set => SetValue(TagProperty, value);
    }
}

/// <summary>
/// Represents a polyline drawn on a <see cref="MapView"/> through geographic coordinates.
/// </summary>
public class MapPolyline : DependencyObject
{
    /// <summary>
    /// Identifies the Points dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(ObservableCollection<GeoPoint>), typeof(MapPolyline),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Stroke dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Jalium.UI.Media.Brush), typeof(MapPolyline),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the StrokeThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(MapPolyline),
            new PropertyMetadata(2.0));

    /// <summary>
    /// Gets or sets the geographic points that define the polyline.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public ObservableCollection<GeoPoint>? Points
    {
        get => (ObservableCollection<GeoPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Jalium.UI.Media.Brush? Stroke
    {
        get => (Jalium.UI.Media.Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke thickness.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty)!;
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MapPolyline"/> class.
    /// </summary>
    public MapPolyline()
    {
        Points = new ObservableCollection<GeoPoint>();
    }
}

/// <summary>
/// Represents a filled polygon drawn on a <see cref="MapView"/> through geographic coordinates.
/// </summary>
public class MapPolygon : DependencyObject
{
    /// <summary>
    /// Identifies the Points dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(ObservableCollection<GeoPoint>), typeof(MapPolygon),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Fill dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Jalium.UI.Media.Brush), typeof(MapPolygon),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Stroke dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Jalium.UI.Media.Brush), typeof(MapPolygon),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the StrokeThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(MapPolygon),
            new PropertyMetadata(2.0));

    /// <summary>
    /// Identifies the FillOpacity dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty FillOpacityProperty =
        DependencyProperty.Register(nameof(FillOpacity), typeof(double), typeof(MapPolygon),
            new PropertyMetadata(0.3));

    /// <summary>
    /// Gets or sets the geographic points that define the polygon boundary.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public ObservableCollection<GeoPoint>? Points
    {
        get => (ObservableCollection<GeoPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>
    /// Gets or sets the fill brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Jalium.UI.Media.Brush? Fill
    {
        get => (Jalium.UI.Media.Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Jalium.UI.Media.Brush? Stroke
    {
        get => (Jalium.UI.Media.Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke thickness.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty)!;
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the opacity of the fill (0.0 - 1.0).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double FillOpacity
    {
        get => (double)GetValue(FillOpacityProperty)!;
        set => SetValue(FillOpacityProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MapPolygon"/> class.
    /// </summary>
    public MapPolygon()
    {
        Points = new ObservableCollection<GeoPoint>();
    }
}

/// <summary>
/// A typed collection of <see cref="MapMarker"/> objects.
/// </summary>
public class MapMarkerCollection : ObservableCollection<MapMarker>
{
}

/// <summary>
/// A typed collection of <see cref="MapPolyline"/> objects.
/// </summary>
public class MapPolylineCollection : ObservableCollection<MapPolyline>
{
}

/// <summary>
/// A typed collection of <see cref="MapPolygon"/> objects.
/// </summary>
public class MapPolygonCollection : ObservableCollection<MapPolygon>
{
}

/// <summary>
/// Provides data for the MapClick event.
/// </summary>
public sealed class MapClickEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the geographic location that was clicked.
    /// </summary>
    public GeoPoint Location { get; }

    /// <summary>
    /// Gets the screen position of the click relative to the map control.
    /// </summary>
    public Point ScreenPosition { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MapClickEventArgs"/> class.
    /// </summary>
    public MapClickEventArgs(RoutedEvent routedEvent, GeoPoint location, Point screenPosition)
    {
        RoutedEvent = routedEvent;
        Location = location;
        ScreenPosition = screenPosition;
    }
}

/// <summary>
/// Provides data for the ViewChanged event.
/// </summary>
public sealed class MapViewChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the new center of the map view.
    /// </summary>
    public GeoPoint Center { get; }

    /// <summary>
    /// Gets the new zoom level.
    /// </summary>
    public double ZoomLevel { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MapViewChangedEventArgs"/> class.
    /// </summary>
    public MapViewChangedEventArgs(RoutedEvent routedEvent, GeoPoint center, double zoomLevel)
    {
        RoutedEvent = routedEvent;
        Center = center;
        ZoomLevel = zoomLevel;
    }
}
