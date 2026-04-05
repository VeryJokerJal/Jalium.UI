using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class MapViewTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);
        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    #region MercatorProjection

    [Fact]
    public void MercatorProjection_GeoToPixel_PixelToGeo_Roundtrip()
    {
        var geoToPixel = typeof(MercatorProjection).GetMethod("GeoToPixel",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var pixelToGeo = typeof(MercatorProjection).GetMethod("PixelToGeo",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        var original = new GeoPoint(48.8566, 2.3522); // Paris
        double zoom = 10;

        var pixel = (Point)geoToPixel.Invoke(null, new object[] { original, zoom })!;
        var roundtrip = (GeoPoint)pixelToGeo.Invoke(null, new object[] { pixel, zoom })!;

        Assert.Equal(original.Latitude, roundtrip.Latitude, 4);
        Assert.Equal(original.Longitude, roundtrip.Longitude, 4);
    }

    [Fact]
    public void MercatorProjection_GeoToPixel_OriginAtZoom0()
    {
        var geoToPixel = typeof(MercatorProjection).GetMethod("GeoToPixel",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        // At zoom 0, map is 256x256. (0,0) lat/lon should map to center
        var pixel = (Point)geoToPixel.Invoke(null, new object[] { new GeoPoint(0, 0), 0.0 })!;

        Assert.Equal(128.0, pixel.X, 1);
        Assert.Equal(128.0, pixel.Y, 1);
    }

    [Fact]
    public void MercatorProjection_MapSize_Zoom0_Is256()
    {
        var mapSize = typeof(MercatorProjection).GetMethod("MapSize",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var size = (double)mapSize.Invoke(null, new object[] { 0.0 })!;
        Assert.Equal(256.0, size, 5);
    }

    [Fact]
    public void MercatorProjection_MapSize_Zoom1_Is512()
    {
        var mapSize = typeof(MercatorProjection).GetMethod("MapSize",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var size = (double)mapSize.Invoke(null, new object[] { 1.0 })!;
        Assert.Equal(512.0, size, 5);
    }

    [Fact]
    public void MercatorProjection_TileCount_Zoom0_Is1()
    {
        var tileCount = typeof(MercatorProjection).GetMethod("TileCount",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var count = (int)tileCount.Invoke(null, new object[] { 0 })!;
        Assert.Equal(1, count);
    }

    [Fact]
    public void MercatorProjection_TileCount_Zoom2_Is4()
    {
        var tileCount = typeof(MercatorProjection).GetMethod("TileCount",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var count = (int)tileCount.Invoke(null, new object[] { 2 })!;
        Assert.Equal(4, count);
    }

    [Fact]
    public void MercatorProjection_PixelToTile_Correct()
    {
        var pixelToTile = typeof(MercatorProjection).GetMethod("PixelToTile",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var result = ((int X, int Y))pixelToTile.Invoke(null, new object[] { new Point(300, 500) })!;

        // 300/256 = 1, 500/256 = 1
        Assert.Equal(1, result.X);
        Assert.Equal(1, result.Y);
    }

    [Fact]
    public void MercatorProjection_GroundResolution_AtEquator()
    {
        var groundResolution = typeof(MercatorProjection).GetMethod("GroundResolution",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        var resolution = (double)groundResolution.Invoke(null, new object[] { 0.0, 0.0 })!;
        // At zoom 0, equator: 40075016.686 / 256 ~ 156543.0
        Assert.True(resolution > 100000);
    }

    #endregion

    #region GeoPoint

    [Fact]
    public void GeoPoint_Equality()
    {
        var a = new GeoPoint(48.8566, 2.3522);
        var b = new GeoPoint(48.8566, 2.3522);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void GeoPoint_Inequality()
    {
        var a = new GeoPoint(48.8566, 2.3522);
        var b = new GeoPoint(40.7128, -74.0060);

        Assert.NotEqual(a, b);
        Assert.True(a != b);
        Assert.False(a == b);
    }

    [Fact]
    public void GeoPoint_GetHashCode_SameForEqual()
    {
        var a = new GeoPoint(48.8566, 2.3522);
        var b = new GeoPoint(48.8566, 2.3522);

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GeoPoint_ToString_ContainsCoordinates()
    {
        var point = new GeoPoint(48.8566, 2.3522);
        var str = point.ToString();
        Assert.Contains("48.856600", str);
        Assert.Contains("2.352200", str);
    }

    [Fact]
    public void GeoPoint_ConstructorSetsProperties()
    {
        var point = new GeoPoint(51.5074, -0.1278);
        Assert.Equal(51.5074, point.Latitude);
        Assert.Equal(-0.1278, point.Longitude);
    }

    #endregion

    #region MapTileSource

    [Fact]
    public void MapTileSource_OpenStreetMap_HasDefaults()
    {
        var osm = MapTileSource.OpenStreetMap;

        Assert.Contains("openstreetmap.org", osm.UrlTemplate);
        Assert.NotNull(osm.Attribution);
        Assert.Equal(0, osm.MinZoom);
        Assert.Equal(19, osm.MaxZoom);
        Assert.Equal(256, osm.TileSize);
    }

    [Fact]
    public void MapTileSource_GetTileUrl_SubstitutesPlaceholders()
    {
        var source = new MapTileSource
        {
            UrlTemplate = "https://example.com/{z}/{x}/{y}.png"
        };

        var url = source.GetTileUrl(5, 10, 20);
        Assert.Equal("https://example.com/5/10/20.png", url);
    }

    #endregion

    #region MapView

    [Fact]
    public void MapView_DefaultProperties()
    {
        var map = new MapView();

        Assert.Equal(2.0, map.ZoomLevel);
        Assert.Equal(1.0, map.MinZoomLevel);
        Assert.Equal(19.0, map.MaxZoomLevel);
        Assert.Equal(new GeoPoint(0, 0), map.Center);
        Assert.True(map.ShowZoomControls);
        Assert.True(map.ShowScaleBar);
        Assert.True(map.ShowAttribution);
        Assert.True(map.IsPanEnabled);
        Assert.True(map.IsZoomEnabled);
        Assert.Null(map.MarkerTemplate);
    }

    [Fact]
    public void MapView_TileSource_DefaultsToOpenStreetMap()
    {
        var map = new MapView();
        Assert.Same(MapTileSource.OpenStreetMap, map.TileSource);
    }

    [Fact]
    public void MapView_Collections_InitializedInConstructor()
    {
        var map = new MapView();
        Assert.NotNull(map.Markers);
        Assert.NotNull(map.Polylines);
        Assert.NotNull(map.Polygons);
        Assert.Empty(map.Markers);
        Assert.Empty(map.Polylines);
        Assert.Empty(map.Polygons);
    }

    [Fact]
    public void MapView_ZoomLevel_CanBeSet()
    {
        var map = new MapView();
        map.ZoomLevel = 10.0;
        Assert.Equal(10.0, map.ZoomLevel);
    }

    [Fact]
    public void MapView_Center_CanBeSet()
    {
        var map = new MapView();
        var center = new GeoPoint(48.8566, 2.3522);
        map.Center = center;
        Assert.Equal(center, map.Center);
    }

    [Fact]
    public void MapView_IsFocusable()
    {
        var map = new MapView();
        Assert.True(map.Focusable);
    }

    #endregion

    #region MiniMap

    [Fact]
    public void MiniMap_DefaultProperties()
    {
        var miniMap = new MiniMap();

        Assert.Equal(0.15, miniMap.ScaleRatio);
        Assert.True(miniMap.AutoScale);
    }

    [Fact]
    public void MiniMap_ScaleRatio_CanBeSet()
    {
        var miniMap = new MiniMap();
        miniMap.ScaleRatio = 0.25;
        Assert.Equal(0.25, miniMap.ScaleRatio);
    }

    [Fact]
    public void MiniMap_AutoScale_CanBeDisabled()
    {
        var miniMap = new MiniMap();
        miniMap.AutoScale = false;
        Assert.False(miniMap.AutoScale);
    }

    #endregion

    #region GeographicHeatmap

    [Fact]
    public void GeographicHeatmap_DefaultProperties()
    {
        var heatmap = new GeographicHeatmap();

        Assert.Equal(20.0, heatmap.Radius);
        Assert.Equal(1.0, heatmap.Intensity);
        Assert.Equal(0.5, heatmap.RenderScale);
        Assert.Null(heatmap.Points);
        Assert.Equal(0.6, heatmap.HeatmapOpacity);
        Assert.True(heatmap.ShowLegend);
    }

    [Fact]
    public void GeographicHeatmap_Radius_CanBeSet()
    {
        var heatmap = new GeographicHeatmap();
        heatmap.Radius = 30.0;
        Assert.Equal(30.0, heatmap.Radius);
    }

    [Fact]
    public void GeographicHeatmap_Intensity_CanBeSet()
    {
        var heatmap = new GeographicHeatmap();
        heatmap.Intensity = 2.0;
        Assert.Equal(2.0, heatmap.Intensity);
    }

    [Fact]
    public void GeographicHeatmap_RenderScale_CanBeSet()
    {
        var heatmap = new GeographicHeatmap();
        heatmap.RenderScale = 0.75;
        Assert.Equal(0.75, heatmap.RenderScale);
    }

    #endregion

    #region HeatmapGradient

    [Fact]
    public void HeatmapGradient_Default_HasStops()
    {
        var gradient = HeatmapGradient.Default;

        Assert.NotNull(gradient);
        Assert.NotNull(gradient.Stops);
        Assert.True(gradient.Stops.Count >= 5);
    }

    [Fact]
    public void HeatmapGradient_Default_FirstStopAtZero()
    {
        var gradient = HeatmapGradient.Default;
        Assert.Equal(0.0, gradient.Stops[0].Offset);
    }

    [Fact]
    public void HeatmapGradient_Default_LastStopAtOne()
    {
        var gradient = HeatmapGradient.Default;
        Assert.Equal(1.0, gradient.Stops[gradient.Stops.Count - 1].Offset);
    }

    [Fact]
    public void HeatmapGradient_SampleColor_AtZero_ReturnsFirstStopColor()
    {
        var gradient = HeatmapGradient.Default;
        var color = gradient.SampleColor(0.0);
        // First stop is transparent blue at offset 0
        Assert.Equal(0, color.A);
    }

    [Fact]
    public void HeatmapGradient_SampleColor_AtOne_ReturnsLastStopColor()
    {
        var gradient = HeatmapGradient.Default;
        var color = gradient.SampleColor(1.0);
        // Last stop is red
        Assert.Equal(255, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }

    [Fact]
    public void HeatmapGradient_BuildLookupTable_Returns256Entries()
    {
        var gradient = HeatmapGradient.Default;
        var lut = gradient.BuildLookupTable();

        Assert.Equal(256, lut.Length);
    }

    [Fact]
    public void HeatmapGradient_EmptyStops_SampleReturnsTransparent()
    {
        var gradient = new HeatmapGradient();
        var color = gradient.SampleColor(0.5);
        Assert.Equal(Color.Transparent, color);
    }

    #endregion

    #region HeatPoint

    [Fact]
    public void HeatPoint_Equality()
    {
        var a = new HeatPoint(40.0, -74.0, 1.0);
        var b = new HeatPoint(40.0, -74.0, 1.0);

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void HeatPoint_Inequality()
    {
        var a = new HeatPoint(40.0, -74.0, 1.0);
        var b = new HeatPoint(41.0, -74.0, 1.0);

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void HeatPoint_DefaultWeight()
    {
        var point = new HeatPoint(40.0, -74.0);
        Assert.Equal(1.0, point.Weight);
    }

    [Fact]
    public void HeatPoint_ToString_ContainsCoordinates()
    {
        var point = new HeatPoint(40.123456, -74.654321, 2.5);
        var str = point.ToString();
        Assert.Contains("40.123456", str);
        Assert.Contains("-74.654321", str);
        Assert.Contains("2.50", str);
    }

    #endregion

    #region MapMarker

    [Fact]
    public void MapMarker_DefaultProperties()
    {
        var marker = new MapMarker();

        Assert.Equal(default(GeoPoint), marker.Location);
        Assert.Equal(string.Empty, marker.Label);
        Assert.Equal(12.0, marker.MarkerSize);
        Assert.Null(marker.Fill);
        Assert.Null(marker.Tag);
    }

    [Fact]
    public void MapMarker_CanSetProperties()
    {
        var marker = new MapMarker
        {
            Location = new GeoPoint(48.8566, 2.3522),
            Label = "Paris",
            MarkerSize = 16.0
        };

        Assert.Equal(48.8566, marker.Location.Latitude);
        Assert.Equal("Paris", marker.Label);
        Assert.Equal(16.0, marker.MarkerSize);
    }

    #endregion

    #region MapPolyline

    [Fact]
    public void MapPolyline_DefaultProperties()
    {
        var polyline = new MapPolyline();

        Assert.NotNull(polyline.Points);
        Assert.Empty(polyline.Points);
        Assert.Equal(2.0, polyline.StrokeThickness);
        Assert.Null(polyline.Stroke);
    }

    #endregion

    #region MapPolygon

    [Fact]
    public void MapPolygon_DefaultProperties()
    {
        var polygon = new MapPolygon();

        Assert.NotNull(polygon.Points);
        Assert.Empty(polygon.Points);
        Assert.Equal(2.0, polygon.StrokeThickness);
        Assert.Equal(0.3, polygon.FillOpacity);
        Assert.Null(polygon.Fill);
        Assert.Null(polygon.Stroke);
    }

    #endregion
}
