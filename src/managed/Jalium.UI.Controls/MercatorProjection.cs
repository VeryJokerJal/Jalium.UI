namespace Jalium.UI.Controls;

/// <summary>
/// Provides Web Mercator (EPSG:3857) projection utilities for converting between
/// geographic coordinates and pixel coordinates at a given zoom level.
/// </summary>
internal static class MercatorProjection
{
    /// <summary>
    /// The size of a single map tile in pixels.
    /// </summary>
    public const int TileSize = 256;

    /// <summary>
    /// Converts a geographic coordinate to a pixel coordinate at the specified zoom level.
    /// </summary>
    /// <param name="geo">The geographic coordinate (latitude/longitude).</param>
    /// <param name="zoom">The zoom level.</param>
    /// <returns>The pixel coordinate in the global pixel space.</returns>
    public static Point GeoToPixel(GeoPoint geo, double zoom)
    {
        double mapSize = MapSize(zoom);

        // Clamp latitude to valid Mercator range
        double lat = Math.Clamp(geo.Latitude, -85.05112878, 85.05112878);
        double lng = geo.Longitude;

        double x = (lng + 180.0) / 360.0 * mapSize;

        double sinLat = Math.Sin(lat * Math.PI / 180.0);
        double y = (0.5 - Math.Log((1.0 + sinLat) / (1.0 - sinLat)) / (4.0 * Math.PI)) * mapSize;

        return new Point(x, y);
    }

    /// <summary>
    /// Converts a pixel coordinate at the specified zoom level to a geographic coordinate.
    /// </summary>
    /// <param name="pixel">The pixel coordinate in the global pixel space.</param>
    /// <param name="zoom">The zoom level.</param>
    /// <returns>The geographic coordinate (latitude/longitude).</returns>
    public static GeoPoint PixelToGeo(Point pixel, double zoom)
    {
        double mapSize = MapSize(zoom);

        double lng = pixel.X / mapSize * 360.0 - 180.0;

        double n = Math.PI - 2.0 * Math.PI * pixel.Y / mapSize;
        double lat = 180.0 / Math.PI * Math.Atan(Math.Sinh(n));

        return new GeoPoint
        {
            Latitude = Math.Clamp(lat, -85.05112878, 85.05112878),
            Longitude = ((lng + 180.0) % 360.0 + 360.0) % 360.0 - 180.0
        };
    }

    /// <summary>
    /// Converts a pixel coordinate to a tile coordinate.
    /// </summary>
    /// <param name="pixel">The pixel coordinate in the global pixel space.</param>
    /// <returns>The tile X and Y indices.</returns>
    public static (int X, int Y) PixelToTile(Point pixel)
    {
        return ((int)Math.Floor(pixel.X / TileSize), (int)Math.Floor(pixel.Y / TileSize));
    }

    /// <summary>
    /// Gets the total pixel size of the map at the specified zoom level.
    /// </summary>
    /// <param name="zoom">The zoom level.</param>
    /// <returns>The total size in pixels (square).</returns>
    public static double MapSize(double zoom)
    {
        return TileSize * Math.Pow(2, zoom);
    }

    /// <summary>
    /// Gets the number of tiles per axis at the specified zoom level.
    /// </summary>
    /// <param name="zoom">The zoom level.</param>
    /// <returns>The number of tiles along each axis.</returns>
    public static int TileCount(int zoom)
    {
        return 1 << zoom;
    }

    /// <summary>
    /// Gets the ground resolution in meters per pixel at the specified latitude and zoom level.
    /// </summary>
    /// <param name="latitude">The latitude in degrees.</param>
    /// <param name="zoom">The zoom level.</param>
    /// <returns>Meters per pixel.</returns>
    public static double GroundResolution(double latitude, double zoom)
    {
        const double earthCircumference = 40075016.686;
        double lat = Math.Clamp(latitude, -85.05112878, 85.05112878);
        return earthCircumference * Math.Cos(lat * Math.PI / 180.0) / MapSize(zoom);
    }
}
