using System.Runtime.InteropServices;

namespace Jalium.UI.Media;

/// <summary>
/// Provides a BitmapSource that can be written to and updated.
/// </summary>
public sealed class WriteableBitmap : BitmapSource
{
    private byte[] _backBuffer;
    private readonly int _pixelWidth;
    private readonly int _pixelHeight;
    private readonly double _dpiX;
    private readonly double _dpiY;
    private readonly PixelFormat _format;
    private readonly int _stride;
    private bool _isLocked;
    private nint _backBufferPointer;
    private GCHandle _pinnedHandle;

    /// <summary>
    /// Gets the width of the bitmap in pixels.
    /// </summary>
    public override double Width => _pixelWidth;

    /// <summary>
    /// Gets the height of the bitmap in pixels.
    /// </summary>
    public override double Height => _pixelHeight;

    /// <summary>
    /// Gets the pixel width.
    /// </summary>
    public override int PixelWidth => _pixelWidth;

    /// <summary>
    /// Gets the pixel height.
    /// </summary>
    public override int PixelHeight => _pixelHeight;

    /// <summary>
    /// Gets the horizontal DPI.
    /// </summary>
    public override double DpiX => _dpiX;

    /// <summary>
    /// Gets the vertical DPI.
    /// </summary>
    public override double DpiY => _dpiY;

    /// <summary>
    /// Gets the pixel format.
    /// </summary>
    public override PixelFormat Format => _format;

    /// <summary>
    /// Gets the stride (bytes per row).
    /// </summary>
    public int BackBufferStride => _stride;

    /// <summary>
    /// Gets a pointer to the back buffer.
    /// </summary>
    public nint BackBuffer => _backBufferPointer;

    /// <summary>
    /// Gets the native handle.
    /// </summary>
    public override nint NativeHandle { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WriteableBitmap"/> class.
    /// </summary>
    /// <param name="pixelWidth">The width in pixels.</param>
    /// <param name="pixelHeight">The height in pixels.</param>
    /// <param name="dpiX">The horizontal DPI.</param>
    /// <param name="dpiY">The vertical DPI.</param>
    /// <param name="pixelFormat">The pixel format.</param>
    /// <param name="palette">The color palette (not used).</param>
    public WriteableBitmap(int pixelWidth, int pixelHeight, double dpiX, double dpiY, PixelFormat pixelFormat, object? palette)
    {
        if (pixelWidth <= 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pixelHeight));

        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _dpiX = dpiX > 0 ? dpiX : 96.0;
        _dpiY = dpiY > 0 ? dpiY : 96.0;
        _format = pixelFormat;

        // Calculate stride (bytes per row, aligned to 4 bytes)
        var bytesPerPixel = GetBytesPerPixel(pixelFormat);
        _stride = ((pixelWidth * bytesPerPixel) + 3) & ~3;

        // Allocate back buffer
        _backBuffer = new byte[_stride * pixelHeight];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WriteableBitmap"/> class from a BitmapSource.
    /// </summary>
    /// <param name="source">The source bitmap.</param>
    public WriteableBitmap(BitmapSource source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        _pixelWidth = (int)source.Width;
        _pixelHeight = (int)source.Height;
        _dpiX = 96.0;
        _dpiY = 96.0;
        _format = PixelFormat.Bgra32;

        var bytesPerPixel = 4;
        _stride = ((PixelWidth * bytesPerPixel) + 3) & ~3;
        _backBuffer = new byte[_stride * _pixelHeight];

        // Copy pixels from source
        source.CopyPixels(_backBuffer, _stride, 0);
    }

    /// <summary>
    /// Locks the bitmap for writing.
    /// </summary>
    /// <returns>True if the lock was successful.</returns>
    public bool TryLock(TimeSpan timeout)
    {
        if (_isLocked) return false;

        var handle = GCHandle.Alloc(_backBuffer, GCHandleType.Pinned);
        try
        {
            _backBufferPointer = handle.AddrOfPinnedObject();
            _pinnedHandle = handle;
            _isLocked = true;
            return true;
        }
        catch
        {
            handle.Free();
            throw;
        }
    }

    /// <summary>
    /// Locks the bitmap for writing.
    /// </summary>
    public void Lock()
    {
        if (_isLocked)
            throw new InvalidOperationException("The bitmap is already locked.");

        var handle = GCHandle.Alloc(_backBuffer, GCHandleType.Pinned);
        try
        {
            _backBufferPointer = handle.AddrOfPinnedObject();
            _pinnedHandle = handle;
            _isLocked = true;
        }
        catch
        {
            handle.Free();
            throw;
        }
    }

    /// <summary>
    /// Unlocks the bitmap.
    /// </summary>
    public void Unlock()
    {
        if (!_isLocked) return;

        if (_pinnedHandle.IsAllocated)
        {
            _pinnedHandle.Free();
        }
        _backBufferPointer = IntPtr.Zero;
        _isLocked = false;
    }

    /// <summary>
    /// Marks a region as dirty (needs to be redrawn).
    /// </summary>
    public void AddDirtyRect(Int32Rect dirtyRect)
    {
        // This would notify the rendering system that the bitmap has changed
        // In this implementation, the entire bitmap is always considered dirty
    }

    /// <summary>
    /// Writes pixels to the bitmap.
    /// </summary>
    /// <param name="sourceRect">The source rectangle.</param>
    /// <param name="pixels">The pixel data.</param>
    /// <param name="stride">The stride of the source data.</param>
    /// <param name="offset">The offset in the source data.</param>
    public void WritePixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
    {
        if (pixels == null) throw new ArgumentNullException(nameof(pixels));

        var x = sourceRect.X;
        var y = sourceRect.Y;
        var width = sourceRect.Width;
        var height = sourceRect.Height;

        if (x < 0 || y < 0 || x + width > _pixelWidth || y + height > _pixelHeight)
            throw new ArgumentOutOfRangeException(nameof(sourceRect));

        var bytesPerPixel = GetBytesPerPixel(_format);

        for (var row = 0; row < height; row++)
        {
            var srcOffset = offset + (row * stride);
            var dstOffset = ((y + row) * _stride) + (x * bytesPerPixel);
            var bytesToCopy = Math.Min(width * bytesPerPixel, stride);
            Array.Copy(pixels, srcOffset, _backBuffer, dstOffset, bytesToCopy);
        }
    }

    /// <summary>
    /// Writes pixels to the bitmap from an IntPtr.
    /// </summary>
    public void WritePixels(Int32Rect sourceRect, nint buffer, int bufferSize, int stride)
    {
        if (buffer == IntPtr.Zero) throw new ArgumentNullException(nameof(buffer));

        var pixels = new byte[bufferSize];
        Marshal.Copy(buffer, pixels, 0, bufferSize);
        WritePixels(sourceRect, pixels, stride, 0);
    }

    /// <summary>
    /// Writes pixels to the entire bitmap.
    /// </summary>
    public void WritePixels(Int32Rect sourceRect, Array pixels, int stride, int offset)
    {
        if (pixels is byte[] byteArray)
        {
            WritePixels(sourceRect, byteArray, stride, offset);
        }
        else if (pixels is int[] intArray)
        {
            var bytePixels = new byte[intArray.Length * 4];
            Buffer.BlockCopy(intArray, 0, bytePixels, 0, bytePixels.Length);
            WritePixels(sourceRect, bytePixels, stride * 4, offset * 4);
        }
    }

    /// <inheritdoc />
    public override void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
    {
        var x = sourceRect.X;
        var y = sourceRect.Y;
        var width = sourceRect.Width == 0 ? _pixelWidth : sourceRect.Width;
        var height = sourceRect.Height == 0 ? _pixelHeight : sourceRect.Height;

        var bytesPerPixel = GetBytesPerPixel(_format);

        for (var row = 0; row < height; row++)
        {
            var srcOffset = ((y + row) * _stride) + (x * bytesPerPixel);
            var dstOffset = offset + (row * stride);
            var bytesToCopy = Math.Min(width * bytesPerPixel, stride);
            Array.Copy(_backBuffer, srcOffset, pixels, dstOffset, bytesToCopy);
        }
    }

    /// <summary>
    /// Clears the bitmap to a specified color.
    /// </summary>
    public void Clear(Color color)
    {
        var bytesPerPixel = GetBytesPerPixel(_format);

        // Build a single pixel value, then fill the entire row and replicate
        var pixel = new byte[bytesPerPixel];
        if (bytesPerPixel >= 4)
        {
            pixel[0] = color.B;
            pixel[1] = color.G;
            pixel[2] = color.R;
            pixel[3] = color.A;
        }

        // Fill the first row
        for (var x = 0; x < _pixelWidth; x++)
        {
            Array.Copy(pixel, 0, _backBuffer, x * bytesPerPixel, bytesPerPixel);
        }

        // Copy first row to all subsequent rows
        var rowBytes = _pixelWidth * bytesPerPixel;
        for (var y = 1; y < _pixelHeight; y++)
        {
            Array.Copy(_backBuffer, 0, _backBuffer, y * _stride, rowBytes);
        }
    }

    /// <summary>
    /// Sets a pixel at the specified coordinates.
    /// </summary>
    public void SetPixel(int x, int y, Color color)
    {
        if (x < 0 || x >= _pixelWidth || y < 0 || y >= _pixelHeight) return;

        var bytesPerPixel = GetBytesPerPixel(_format);
        var offset = (y * _stride) + (x * bytesPerPixel);

        switch (_format)
        {
            case PixelFormat.Bgra32:
            case PixelFormat.Pbgra32:
                _backBuffer[offset] = color.B;
                _backBuffer[offset + 1] = color.G;
                _backBuffer[offset + 2] = color.R;
                _backBuffer[offset + 3] = color.A;
                break;
            case PixelFormat.Rgba32:
                _backBuffer[offset] = color.R;
                _backBuffer[offset + 1] = color.G;
                _backBuffer[offset + 2] = color.B;
                _backBuffer[offset + 3] = color.A;
                break;
            case PixelFormat.Bgr24:
                _backBuffer[offset] = color.B;
                _backBuffer[offset + 1] = color.G;
                _backBuffer[offset + 2] = color.R;
                break;
            case PixelFormat.Rgb24:
                _backBuffer[offset] = color.R;
                _backBuffer[offset + 1] = color.G;
                _backBuffer[offset + 2] = color.B;
                break;
            case PixelFormat.Gray8:
                _backBuffer[offset] = (byte)((color.R * 77 + color.G * 150 + color.B * 29) >> 8);
                break;
        }
    }

    /// <summary>
    /// Gets a pixel at the specified coordinates.
    /// </summary>
    public Color GetPixel(int x, int y)
    {
        if (x < 0 || x >= _pixelWidth || y < 0 || y >= _pixelHeight)
            return Color.Transparent;

        var bytesPerPixel = GetBytesPerPixel(_format);
        var offset = (y * _stride) + (x * bytesPerPixel);

        return _format switch
        {
            PixelFormat.Bgra32 or PixelFormat.Pbgra32 =>
                Color.FromArgb(_backBuffer[offset + 3], _backBuffer[offset + 2], _backBuffer[offset + 1], _backBuffer[offset]),
            PixelFormat.Rgba32 =>
                Color.FromArgb(_backBuffer[offset + 3], _backBuffer[offset], _backBuffer[offset + 1], _backBuffer[offset + 2]),
            PixelFormat.Bgr24 =>
                Color.FromRgb(_backBuffer[offset + 2], _backBuffer[offset + 1], _backBuffer[offset]),
            PixelFormat.Rgb24 =>
                Color.FromRgb(_backBuffer[offset], _backBuffer[offset + 1], _backBuffer[offset + 2]),
            PixelFormat.Gray8 =>
                Color.FromRgb(_backBuffer[offset], _backBuffer[offset], _backBuffer[offset]),
            _ => Color.Transparent
        };
    }

    /// <summary>
    /// Draws a line using Bresenham's algorithm.
    /// </summary>
    public void DrawLine(int x0, int y0, int x1, int y1, Color color)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        while (true)
        {
            SetPixel(x0, y0, color);

            if (x0 == x1 && y0 == y1) break;

            var e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    /// <summary>
    /// Draws a rectangle outline.
    /// </summary>
    public void DrawRectangle(int x, int y, int width, int height, Color color)
    {
        DrawLine(x, y, x + width - 1, y, color);
        DrawLine(x + width - 1, y, x + width - 1, y + height - 1, color);
        DrawLine(x + width - 1, y + height - 1, x, y + height - 1, color);
        DrawLine(x, y + height - 1, x, y, color);
    }

    /// <summary>
    /// Fills a rectangle.
    /// </summary>
    public void FillRectangle(int x, int y, int width, int height, Color color)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var col = x; col < x + width; col++)
            {
                SetPixel(col, row, color);
            }
        }
    }

    /// <summary>
    /// Draws an ellipse outline.
    /// </summary>
    public void DrawEllipse(int cx, int cy, int rx, int ry, Color color)
    {
        var x = 0;
        var y = ry;
        var a2 = rx * rx;
        var b2 = ry * ry;
        var fa2 = 4 * a2;
        var fb2 = 4 * b2;
        var sigma = 2 * b2 + a2 * (1 - 2 * ry);

        while (b2 * x <= a2 * y)
        {
            SetPixel(cx + x, cy + y, color);
            SetPixel(cx - x, cy + y, color);
            SetPixel(cx + x, cy - y, color);
            SetPixel(cx - x, cy - y, color);

            if (sigma >= 0)
            {
                sigma += fa2 * (1 - y);
                y--;
            }
            sigma += b2 * (4 * x + 6);
            x++;
        }

        x = rx;
        y = 0;
        sigma = 2 * a2 + b2 * (1 - 2 * rx);

        while (a2 * y <= b2 * x)
        {
            SetPixel(cx + x, cy + y, color);
            SetPixel(cx - x, cy + y, color);
            SetPixel(cx + x, cy - y, color);
            SetPixel(cx - x, cy - y, color);

            if (sigma >= 0)
            {
                sigma += fb2 * (1 - x);
                x--;
            }
            sigma += a2 * (4 * y + 6);
            y++;
        }
    }

    /// <summary>
    /// Fills an ellipse.
    /// </summary>
    public void FillEllipse(int cx, int cy, int rx, int ry, Color color)
    {
        for (var y = -ry; y <= ry; y++)
        {
            for (var x = -rx; x <= rx; x++)
            {
                if ((x * x * ry * ry + y * y * rx * rx) <= rx * rx * ry * ry)
                {
                    SetPixel(cx + x, cy + y, color);
                }
            }
        }
    }

    /// <summary>
    /// Copies a region from another bitmap.
    /// </summary>
    public void Blit(WriteableBitmap source, Int32Rect sourceRect, Point destPoint)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        var srcX = sourceRect.X;
        var srcY = sourceRect.Y;
        var width = sourceRect.Width;
        var height = sourceRect.Height;
        var dstX = (int)destPoint.X;
        var dstY = (int)destPoint.Y;

        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                var color = source.GetPixel(srcX + col, srcY + row);
                if (color.A > 0) // Simple alpha test
                {
                    SetPixel(dstX + col, dstY + row, color);
                }
            }
        }
    }

    /// <summary>
    /// Creates a cropped copy of the bitmap.
    /// </summary>
    public WriteableBitmap Crop(Int32Rect rect)
    {
        var cropped = new WriteableBitmap(rect.Width, rect.Height, _dpiX, _dpiY, _format, null);

        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < rect.Width; x++)
            {
                cropped.SetPixel(x, y, GetPixel(rect.X + x, rect.Y + y));
            }
        }

        return cropped;
    }

    /// <summary>
    /// Creates a resized copy of the bitmap.
    /// </summary>
    public WriteableBitmap Resize(int newWidth, int newHeight)
    {
        var resized = new WriteableBitmap(newWidth, newHeight, _dpiX, _dpiY, _format, null);

        var xRatio = (double)_pixelWidth / newWidth;
        var yRatio = (double)_pixelHeight / newHeight;

        for (var y = 0; y < newHeight; y++)
        {
            for (var x = 0; x < newWidth; x++)
            {
                var srcX = (int)(x * xRatio);
                var srcY = (int)(y * yRatio);
                resized.SetPixel(x, y, GetPixel(srcX, srcY));
            }
        }

        return resized;
    }

    /// <summary>
    /// Applies a grayscale filter.
    /// </summary>
    public void ApplyGrayscale()
    {
        for (var y = 0; y < _pixelHeight; y++)
        {
            for (var x = 0; x < _pixelWidth; x++)
            {
                var color = GetPixel(x, y);
                var gray = (byte)((color.R * 77 + color.G * 150 + color.B * 29) >> 8);
                SetPixel(x, y, Color.FromArgb(color.A, gray, gray, gray));
            }
        }
    }

    /// <summary>
    /// Inverts the colors.
    /// </summary>
    public void Invert()
    {
        for (var y = 0; y < _pixelHeight; y++)
        {
            for (var x = 0; x < _pixelWidth; x++)
            {
                var color = GetPixel(x, y);
                SetPixel(x, y, Color.FromArgb(color.A, (byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B)));
            }
        }
    }

    /// <summary>
    /// Flips the bitmap horizontally.
    /// </summary>
    public void FlipHorizontal()
    {
        for (var y = 0; y < _pixelHeight; y++)
        {
            for (var x = 0; x < _pixelWidth / 2; x++)
            {
                var left = GetPixel(x, y);
                var right = GetPixel(_pixelWidth - 1 - x, y);
                SetPixel(x, y, right);
                SetPixel(_pixelWidth - 1 - x, y, left);
            }
        }
    }

    /// <summary>
    /// Flips the bitmap vertically.
    /// </summary>
    public void FlipVertical()
    {
        for (var y = 0; y < _pixelHeight / 2; y++)
        {
            for (var x = 0; x < _pixelWidth; x++)
            {
                var top = GetPixel(x, y);
                var bottom = GetPixel(x, _pixelHeight - 1 - y);
                SetPixel(x, y, bottom);
                SetPixel(x, _pixelHeight - 1 - y, top);
            }
        }
    }

    private static int GetBytesPerPixel(PixelFormat format) => format switch
    {
        PixelFormat.Bgra32 or PixelFormat.Rgba32 or PixelFormat.Rgb32 or PixelFormat.Pbgra32 => 4,
        PixelFormat.Bgr24 or PixelFormat.Rgb24 => 3,
        PixelFormat.Gray16 => 2,
        PixelFormat.Gray8 => 1,
        _ => 4
    };
}
