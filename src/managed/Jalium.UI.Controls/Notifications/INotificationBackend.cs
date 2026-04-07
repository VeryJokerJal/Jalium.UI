using Jalium.UI.Media;

namespace Jalium.UI.Notifications;

/// <summary>
/// Platform-specific notification backend.
/// Each platform (Windows, Linux, Android) provides its own implementation.
/// </summary>
internal interface INotificationBackend : IDisposable
{
    /// <summary>
    /// Initializes the backend. Called once before any notifications are shown.
    /// </summary>
    /// <param name="appId">Application identifier (e.g. "com.jalium.myapp").</param>
    /// <param name="appName">Human-readable application name.</param>
    void Initialize(string appId, string appName);

    /// <summary>
    /// Shows a system notification and returns a handle for further operations.
    /// </summary>
    NotificationHandle Show(NotificationContent content);

    /// <summary>
    /// Hides / removes a previously shown notification.
    /// </summary>
    void Hide(NotificationHandle handle);

    /// <summary>
    /// Removes all notifications for this application.
    /// </summary>
    void ClearAll();

    /// <summary>
    /// Removes notifications matching the specified tag and optional group.
    /// </summary>
    void Remove(string tag, string? group = null);

    /// <summary>
    /// Gets whether the platform supports system notifications.
    /// </summary>
    bool IsSupported { get; }
}

/// <summary>
/// Opaque handle to a shown system notification.
/// </summary>
public sealed class NotificationHandle
{
    internal nint NativeHandle { get; init; }
    internal string? Tag { get; init; }
    internal string? Group { get; init; }
    internal uint PlatformId { get; init; }

    /// <summary>
    /// Occurs when the notification is activated (clicked or action pressed).
    /// </summary>
    public event EventHandler<NotificationActivatedEventArgs>? Activated;

    /// <summary>
    /// Occurs when the notification is dismissed.
    /// </summary>
    public event EventHandler<NotificationDismissedEventArgs>? Dismissed;

    /// <summary>
    /// Occurs when a notification error occurs.
    /// </summary>
    public event EventHandler<Exception>? Failed;

    internal void RaiseActivated(NotificationActivatedEventArgs args) => Activated?.Invoke(this, args);
    internal void RaiseDismissed(NotificationDismissedEventArgs args) => Dismissed?.Invoke(this, args);
    internal void RaiseFailed(Exception ex) => Failed?.Invoke(this, ex);
}

/// <summary>
/// Utility to materialize an <see cref="ImageSource"/> into a temporary PNG file
/// that platform notification APIs can reference by path.
/// </summary>
internal static class NotificationImageHelper
{
    private static readonly string s_tempDir = Path.Combine(Path.GetTempPath(), "jalium_notifications");

    /// <summary>
    /// Resolves an <see cref="ImageSource"/> to a file path usable by the OS notification system.
    /// <list type="bullet">
    ///   <item><see cref="BitmapImage"/> with <c>UriSource</c> (file://) – returns the local path directly.</item>
    ///   <item><see cref="BitmapImage"/> with pixel data – writes a temp PNG and returns its path.</item>
    ///   <item><see cref="BitmapSource"/> – copies pixels to a temp PNG and returns its path.</item>
    /// </list>
    /// Returns <c>null</c> if the image cannot be resolved.
    /// </summary>
    public static string? ResolveToPath(ImageSource? source)
    {
        if (source is null) return null;

        // BitmapImage with a local file URI – use the file directly
        if (source is BitmapImage bitmapImage)
        {
            var uri = bitmapImage.UriSource;
            if (uri is { IsFile: true })
                return uri.LocalPath;

            // Has raw encoded data (JPEG/PNG bytes) – write to temp
            if (bitmapImage.ImageData is { Length: > 0 } data)
                return WriteTempFile(data, ".png");

            // Has raw BGRA pixels – encode as BMP/PNG
            if (bitmapImage.RawPixelData is { Length: > 0 } pixels)
                return WriteTempBgra(pixels, bitmapImage.PixelWidth, bitmapImage.PixelHeight);
        }

        // BitmapSource with pixel copy support
        if (source is BitmapSource bitmapSource && bitmapSource.PixelWidth > 0 && bitmapSource.PixelHeight > 0)
        {
            int stride = bitmapSource.PixelWidth * 4;
            byte[] pixels = new byte[stride * bitmapSource.PixelHeight];
            bitmapSource.CopyPixels(pixels, stride, 0);
            return WriteTempBgra(pixels, bitmapSource.PixelWidth, bitmapSource.PixelHeight);
        }

        return null;
    }

    private static string WriteTempFile(byte[] data, string extension)
    {
        Directory.CreateDirectory(s_tempDir);
        string path = Path.Combine(s_tempDir, $"notif_{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>
    /// Writes raw BGRA8 pixels as a minimal BMP file (no compression, universal support).
    /// </summary>
    private static string WriteTempBgra(byte[] pixels, int width, int height)
    {
        Directory.CreateDirectory(s_tempDir);
        string path = Path.Combine(s_tempDir, $"notif_{Guid.NewGuid():N}.bmp");

        int stride = width * 4;
        int imageSize = stride * height;
        int fileSize = 54 + imageSize; // BMP header (14) + DIB header (40) + pixel data

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // BMP File Header (14 bytes)
        bw.Write((ushort)0x4D42); // 'BM'
        bw.Write(fileSize);
        bw.Write(0);              // reserved
        bw.Write(54);             // pixel data offset

        // DIB Header – BITMAPINFOHEADER (40 bytes)
        bw.Write(40);             // header size
        bw.Write(width);
        bw.Write(-height);        // negative = top-down
        bw.Write((ushort)1);      // planes
        bw.Write((ushort)32);     // bpp
        bw.Write(0);              // no compression
        bw.Write(imageSize);
        bw.Write(2835);           // ~72 DPI horizontal
        bw.Write(2835);           // ~72 DPI vertical
        bw.Write(0);              // colors in palette
        bw.Write(0);              // important colors

        // Pixel data (already BGRA, which is BMP's native format)
        bw.Write(pixels);

        return path;
    }
}
