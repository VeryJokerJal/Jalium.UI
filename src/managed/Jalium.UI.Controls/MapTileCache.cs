using System.Collections.Concurrent;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides an in-memory LRU cache for map tiles. Tiles are stored as <see cref="ImageSource"/>
/// objects (either loaded bitmaps or generated fallback images).
/// </summary>
internal sealed class MapTileCache
{
    private readonly struct TileKey : IEquatable<TileKey>
    {
        public readonly int Zoom;
        public readonly int X;
        public readonly int Y;

        public TileKey(int zoom, int x, int y)
        {
            Zoom = zoom;
            X = x;
            Y = y;
        }

        public bool Equals(TileKey other) => Zoom == other.Zoom && X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is TileKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Zoom, X, Y);
    }

    private sealed class CacheEntry
    {
        public ImageSource Image { get; set; } = null!;
        public long LastAccess { get; set; }
        public bool IsLoading { get; set; }
    }

    private const int MaxCachedTiles = 512;

    private readonly ConcurrentDictionary<TileKey, CacheEntry> _cache = new();
    private readonly object _evictionLock = new();
    private long _accessCounter;

    /// <summary>
    /// Gets or loads a tile image for the specified coordinates.
    /// Returns the cached image immediately if available, otherwise starts an async HTTP
    /// load and returns a fallback placeholder in the meantime.
    /// </summary>
    /// <param name="zoom">The zoom level.</param>
    /// <param name="x">The tile X index.</param>
    /// <param name="y">The tile Y index.</param>
    /// <param name="tileSource">The tile source configuration.</param>
    /// <param name="onTileLoaded">Callback invoked on the UI thread when the real tile finishes loading.</param>
    /// <returns>The cached tile image, or a placeholder if the tile is still loading.</returns>
    public ImageSource GetOrLoadTile(int zoom, int x, int y, MapTileSource tileSource, Action? onTileLoaded = null)
    {
        var key = new TileKey(zoom, x, y);

        if (_cache.TryGetValue(key, out var entry))
        {
            entry.LastAccess = Interlocked.Increment(ref _accessCounter);
            if (!entry.IsLoading)
            {
                return entry.Image;
            }
            // Still loading - return the placeholder image stored in the entry
            return entry.Image;
        }

        // Create a placeholder tile
        var placeholder = CreatePlaceholderTile(zoom, x, y);

        var newEntry = new CacheEntry
        {
            Image = placeholder,
            LastAccess = Interlocked.Increment(ref _accessCounter),
            IsLoading = true
        };

        if (!_cache.TryAdd(key, newEntry))
        {
            // Another thread beat us - return whatever it stored
            if (_cache.TryGetValue(key, out var existing))
            {
                existing.LastAccess = Interlocked.Increment(ref _accessCounter);
                return existing.Image;
            }
            return placeholder;
        }

        EvictIfNeeded();

        // Start async HTTP load
        StartTileLoad(key, newEntry, tileSource, onTileLoaded);

        return placeholder;
    }

    /// <summary>
    /// Clears all cached tiles.
    /// </summary>
    public void Clear()
    {
        foreach (var kvp in _cache)
        {
            if (kvp.Value.Image is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        _cache.Clear();
    }

    /// <summary>
    /// Gets the number of tiles currently cached.
    /// </summary>
    public int Count => _cache.Count;

    private void EvictIfNeeded()
    {
        if (_cache.Count <= MaxCachedTiles) return;

        lock (_evictionLock)
        {
            if (_cache.Count <= MaxCachedTiles) return;

            // Remove the oldest entries (lowest LastAccess) until we're under the limit
            int toRemove = _cache.Count - MaxCachedTiles + MaxCachedTiles / 4; // Remove an extra 25% to avoid thrashing
            var entries = new List<(TileKey Key, long LastAccess)>(_cache.Count);

            foreach (var kvp in _cache)
            {
                entries.Add((kvp.Key, kvp.Value.LastAccess));
            }

            entries.Sort((a, b) => a.LastAccess.CompareTo(b.LastAccess));

            for (int i = 0; i < toRemove && i < entries.Count; i++)
            {
                if (_cache.TryRemove(entries[i].Key, out var removed))
                {
                    if (!removed.IsLoading && removed.Image is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
        }
    }

    private void StartTileLoad(TileKey key, CacheEntry entry, MapTileSource tileSource, Action? onTileLoaded)
    {
        var url = tileSource.GetTileUrl(key.Zoom, key.X, key.Y);

        _ = Task.Run(async () =>
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Jalium.UI.MapView/1.0");
                httpClient.Timeout = TimeSpan.FromSeconds(15);

                var data = await httpClient.GetByteArrayAsync(url).ConfigureAwait(false);

                var bitmap = new BitmapImage();
                // Load from raw image data by setting the internal state
                // BitmapImage supports construction from URI which handles HTTP internally,
                // but since we already downloaded the bytes, use FromFile-like approach
                // For the framework, the best approach is to create a new BitmapImage with URI
                var tileImage = new BitmapImage(new Uri(url));

                tileImage.OnImageLoaded += (_, _) =>
                {
                    entry.Image = tileImage;
                    entry.IsLoading = false;
                    entry.LastAccess = Interlocked.Increment(ref _accessCounter);
                    onTileLoaded?.Invoke();
                };

                // If BitmapImage loaded synchronously (unlikely for HTTP), update directly
                if (tileImage.Width > 0 && tileImage.Height > 0)
                {
                    entry.Image = tileImage;
                    entry.IsLoading = false;
                    entry.LastAccess = Interlocked.Increment(ref _accessCounter);
                    onTileLoaded?.Invoke();
                }
            }
            catch
            {
                // On failure, keep the placeholder; mark as not loading so we don't retry on every frame
                entry.IsLoading = false;
            }
        });
    }

    /// <summary>
    /// Creates a fallback placeholder tile image showing a colored rectangle with tile coordinate text.
    /// </summary>
    private static ImageSource CreatePlaceholderTile(int zoom, int x, int y)
    {
        const int size = MercatorProjection.TileSize;
        var pixels = new byte[size * size * 4];

        // Generate a subtle color based on tile coordinates for visual distinction
        byte baseR = (byte)(200 + (x * 7) % 40);
        byte baseG = (byte)(210 + (y * 11) % 30);
        byte baseB = (byte)(220 + ((x + y) * 5) % 25);

        // Fill background
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                int idx = (py * size + px) * 4;
                pixels[idx + 0] = baseB;  // B
                pixels[idx + 1] = baseG;  // G
                pixels[idx + 2] = baseR;  // R
                pixels[idx + 3] = 255;    // A
            }
        }

        // Draw a 1px border in darker shade
        byte borderR = (byte)(baseR - 40);
        byte borderG = (byte)(baseG - 40);
        byte borderB = (byte)(baseB - 40);

        for (int i = 0; i < size; i++)
        {
            // Top row
            SetPixel(pixels, size, i, 0, borderB, borderG, borderR);
            // Bottom row
            SetPixel(pixels, size, i, size - 1, borderB, borderG, borderR);
            // Left column
            SetPixel(pixels, size, 0, i, borderB, borderG, borderR);
            // Right column
            SetPixel(pixels, size, size - 1, i, borderB, borderG, borderR);
        }

        // Draw tile coordinates text as simple pixel patterns in the center area
        // Render "z/x/y" text by drawing a small crosshair pattern in the center
        int cx = size / 2;
        int cy = size / 2;
        byte textB = (byte)Math.Max(0, baseB - 80);
        byte textG = (byte)Math.Max(0, baseG - 80);
        byte textR = (byte)Math.Max(0, baseR - 80);

        // Draw a small + sign at center to indicate this is a placeholder
        for (int i = -8; i <= 8; i++)
        {
            SetPixel(pixels, size, cx + i, cy, textB, textG, textR);
            SetPixel(pixels, size, cx, cy + i, textB, textG, textR);
        }

        // Draw digit patterns for the zoom level near the center
        DrawDigits(pixels, size, zoom, cx - 20, cy - 20, textB, textG, textR);
        DrawDigits(pixels, size, x % 1000, cx - 20, cy + 14, textB, textG, textR);
        DrawDigits(pixels, size, y % 1000, cx + 4, cy + 14, textB, textG, textR);

        return BitmapImage.FromPixels(pixels, size, size);
    }

    private static void SetPixel(byte[] pixels, int stride, int x, int y, byte b, byte g, byte r)
    {
        if (x < 0 || x >= stride || y < 0 || y >= stride) return;
        int idx = (y * stride + x) * 4;
        pixels[idx + 0] = b;
        pixels[idx + 1] = g;
        pixels[idx + 2] = r;
        pixels[idx + 3] = 255;
    }

    /// <summary>
    /// Draws a simple numeric value as small pixel-art digits.
    /// </summary>
    private static void DrawDigits(byte[] pixels, int stride, int value, int startX, int startY, byte b, byte g, byte r)
    {
        // Simple 3x5 digit font patterns (each digit is stored as 5 rows of 3-bit patterns)
        ReadOnlySpan<ushort> font = stackalloc ushort[]
        {
            // 0      1      2      3      4      5      6      7      8      9
            0x7B6F, 0x2492, 0x73E7, 0x73CF, 0x49ED, 0x79CF, 0x79EF, 0x7249, 0x7BEF, 0x7BCD
        };

        string text = value.ToString();
        int offsetX = startX;

        for (int ci = 0; ci < text.Length; ci++)
        {
            int digit = text[ci] - '0';
            if (digit < 0 || digit > 9) { offsetX += 4; continue; }

            ushort pattern = font[digit];
            for (int row = 0; row < 5; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    int bit = 14 - (row * 3 + col);
                    if (((pattern >> bit) & 1) == 1)
                    {
                        SetPixel(pixels, stride, offsetX + col, startY + row, b, g, r);
                    }
                }
            }
            offsetX += 4;
        }
    }
}
