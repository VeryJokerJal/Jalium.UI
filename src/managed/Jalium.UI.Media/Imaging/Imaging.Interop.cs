using System.ComponentModel;
using System.Runtime.InteropServices;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using ImagingBitmapSource = Jalium.UI.Media.Imaging.BitmapSource;

namespace Jalium.UI.Interop;

/// <summary>Creates bitmap sources from Win32 and shared-memory image resources.</summary>
public static class Imaging
{
    /// <summary>Copies an HBITMAP into a managed bitmap source.</summary>
    public static ImagingBitmapSource CreateBitmapSourceFromHBitmap(
        IntPtr bitmap,
        IntPtr palette,
        Int32Rect sourceRect,
        BitmapSizeOptions sizeOptions)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("HBITMAP conversion requires Windows GDI.");
        }

        if (bitmap == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        ArgumentNullException.ThrowIfNull(sizeOptions);
        _ = palette; // Indexed palettes are already resolved by GetDIBits into BGRA pixels.
        RawBitmap raw = ReadHBitmap(bitmap);
        return CreateBitmap(ApplyOptions(Crop(raw, sourceRect), sizeOptions));
    }

    /// <summary>Copies an HICON into a managed bitmap source.</summary>
    public static ImagingBitmapSource CreateBitmapSourceFromHIcon(
        IntPtr icon,
        Int32Rect sourceRect,
        BitmapSizeOptions sizeOptions)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("HICON conversion requires Windows GDI.");
        }

        if (icon == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(icon));
        }

        ArgumentNullException.ThrowIfNull(sizeOptions);
        if (!NativeImagingMethods.GetIconInfo(icon, out IconInfo iconInfo))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            RawBitmap raw;
            if (iconInfo.ColorBitmap != IntPtr.Zero)
            {
                raw = ReadHBitmap(iconInfo.ColorBitmap);
                EnsureOpaqueAlphaWhenAbsent(raw.Pixels);
            }
            else if (iconInfo.MaskBitmap != IntPtr.Zero)
            {
                RawBitmap mask = ReadHBitmap(iconInfo.MaskBitmap);
                int iconHeight = Math.Max(1, mask.Height / 2);
                int start = checked((mask.Height - iconHeight) * mask.Stride);
                var pixels = new byte[checked(mask.Stride * iconHeight)];
                Buffer.BlockCopy(mask.Pixels, start, pixels, 0, pixels.Length);
                EnsureOpaqueAlphaWhenAbsent(pixels);
                raw = new RawBitmap(mask.Width, iconHeight, mask.Stride, pixels);
            }
            else
            {
                throw new ArgumentException("The icon does not contain a color or mask bitmap.", nameof(icon));
            }

            return CreateBitmap(ApplyOptions(Crop(raw, sourceRect), sizeOptions));
        }
        finally
        {
            if (iconInfo.ColorBitmap != IntPtr.Zero)
            {
                _ = NativeImagingMethods.DeleteObject(iconInfo.ColorBitmap);
            }

            if (iconInfo.MaskBitmap != IntPtr.Zero)
            {
                _ = NativeImagingMethods.DeleteObject(iconInfo.MaskBitmap);
            }
        }
    }

    /// <summary>
    /// Creates a live bitmap over a caller-owned Windows file-mapping section.
    /// Calling <see cref="InteropBitmap.Invalidate()"/> rereads the section.
    /// </summary>
    public static ImagingBitmapSource CreateBitmapSourceFromMemorySection(
        IntPtr section,
        int pixelWidth,
        int pixelHeight,
        PixelFormat format,
        int stride,
        int offset)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("File-mapping bitmap sections require Windows.");
        }

        if (section == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(section));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        int rowBytes = checked((pixelWidth * format.BitsPerPixel + 7) / 8);
        if (stride < rowBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        int byteCount = checked(stride * pixelHeight);
        byte[] ReadPixels() => ReadMemorySection(section, offset, byteCount);
        byte[] pixels = ReadPixels();
        return new InteropBitmap(
            pixelWidth,
            pixelHeight,
            96,
            96,
            format,
            pixels,
            stride,
            ReadPixels);
    }

    private static ImagingBitmapSource CreateBitmap(RawBitmap raw) =>
        new InteropBitmap(
            raw.Width,
            raw.Height,
            96,
            96,
            PixelFormat.Bgra32,
            raw.Pixels,
            raw.Stride);

    private static RawBitmap ReadHBitmap(IntPtr bitmap)
    {
        if (NativeImagingMethods.GetObject(
                bitmap,
                Marshal.SizeOf<GdiBitmapDescription>(),
                out GdiBitmapDescription description) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        int width = Math.Abs(description.Width);
        int height = Math.Abs(description.Height);
        if (width == 0 || height == 0)
        {
            throw new ArgumentException("The native bitmap has no pixel area.", nameof(bitmap));
        }

        int stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = 0,
                SizeImage = (uint)pixels.Length,
            },
        };

        IntPtr dc = NativeImagingMethods.GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            int lines = NativeImagingMethods.GetDIBits(
                dc,
                bitmap,
                0,
                (uint)height,
                pixels,
                ref info,
                0);
            if (lines == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            _ = NativeImagingMethods.ReleaseDC(IntPtr.Zero, dc);
        }

        return new RawBitmap(width, height, stride, pixels);
    }

    private static byte[] ReadMemorySection(IntPtr section, int offset, int byteCount)
    {
        nuint mappedLength = checked((nuint)(offset + byteCount));
        IntPtr view = NativeImagingMethods.MapViewOfFile(section, 0x0004, 0, 0, mappedLength);
        if (view == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            var pixels = new byte[byteCount];
            Marshal.Copy(IntPtr.Add(view, offset), pixels, 0, byteCount);
            return pixels;
        }
        finally
        {
            _ = NativeImagingMethods.UnmapViewOfFile(view);
        }
    }

    private static RawBitmap Crop(RawBitmap raw, Int32Rect sourceRect)
    {
        if (sourceRect.IsEmpty)
        {
            return raw;
        }

        if (sourceRect.X < 0 || sourceRect.Y < 0 || sourceRect.Width <= 0 || sourceRect.Height <= 0 ||
            sourceRect.X + sourceRect.Width > raw.Width ||
            sourceRect.Y + sourceRect.Height > raw.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRect));
        }

        int stride = checked(sourceRect.Width * 4);
        var pixels = new byte[checked(stride * sourceRect.Height)];
        for (int row = 0; row < sourceRect.Height; row++)
        {
            Buffer.BlockCopy(
                raw.Pixels,
                ((sourceRect.Y + row) * raw.Stride) + (sourceRect.X * 4),
                pixels,
                row * stride,
                stride);
        }

        return new RawBitmap(sourceRect.Width, sourceRect.Height, stride, pixels);
    }

    private static RawBitmap ApplyOptions(RawBitmap raw, BitmapSizeOptions options)
    {
        int width = options.PixelWidth;
        int height = options.PixelHeight;
        if (options.PreservesAspectRatio)
        {
            if (width > 0 && height <= 0)
            {
                height = Math.Max(1, (int)Math.Round(raw.Height * (width / (double)raw.Width)));
            }
            else if (height > 0 && width <= 0)
            {
                width = Math.Max(1, (int)Math.Round(raw.Width * (height / (double)raw.Height)));
            }
        }

        if (width <= 0)
        {
            width = raw.Width;
        }

        if (height <= 0)
        {
            height = raw.Height;
        }

        RawBitmap resized = width == raw.Width && height == raw.Height
            ? raw
            : Resize(raw, width, height);
        return Rotate(resized, options.Rotation);
    }

    private static RawBitmap Resize(RawBitmap source, int width, int height)
    {
        int stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(source.Height - 1, (int)((long)y * source.Height / height));
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(source.Width - 1, (int)((long)x * source.Width / width));
                Buffer.BlockCopy(
                    source.Pixels,
                    (sourceY * source.Stride) + (sourceX * 4),
                    pixels,
                    (y * stride) + (x * 4),
                    4);
            }
        }

        return new RawBitmap(width, height, stride, pixels);
    }

    private static RawBitmap Rotate(RawBitmap source, Rotation rotation)
    {
        if (rotation == Rotation.Rotate0)
        {
            return source;
        }

        int width = rotation is Rotation.Rotate90 or Rotation.Rotate270 ? source.Height : source.Width;
        int height = rotation is Rotation.Rotate90 or Rotation.Rotate270 ? source.Width : source.Height;
        int stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                (int targetX, int targetY) = rotation switch
                {
                    Rotation.Rotate90 => (source.Height - 1 - y, x),
                    Rotation.Rotate180 => (source.Width - 1 - x, source.Height - 1 - y),
                    Rotation.Rotate270 => (y, source.Width - 1 - x),
                    _ => throw new InvalidEnumArgumentException(nameof(rotation), (int)rotation, typeof(Rotation)),
                };
                Buffer.BlockCopy(
                    source.Pixels,
                    (y * source.Stride) + (x * 4),
                    pixels,
                    (targetY * stride) + (targetX * 4),
                    4);
            }
        }

        return new RawBitmap(width, height, stride, pixels);
    }

    private static void EnsureOpaqueAlphaWhenAbsent(byte[] pixels)
    {
        bool hasAlpha = false;
        for (int index = 3; index < pixels.Length; index += 4)
        {
            hasAlpha |= pixels[index] != 0;
        }

        if (hasAlpha)
        {
            return;
        }

        for (int index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = byte.MaxValue;
        }
    }

    private readonly record struct RawBitmap(int Width, int Height, int Stride, byte[] Pixels);
}

[StructLayout(LayoutKind.Sequential)]
internal struct GdiBitmapDescription
{
    internal int Type;
    internal int Width;
    internal int Height;
    internal int WidthBytes;
    internal ushort Planes;
    internal ushort BitsPixel;
    internal IntPtr Bits;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfoHeader
{
    internal uint Size;
    internal int Width;
    internal int Height;
    internal ushort Planes;
    internal ushort BitCount;
    internal uint Compression;
    internal uint SizeImage;
    internal int XPelsPerMeter;
    internal int YPelsPerMeter;
    internal uint ClrUsed;
    internal uint ClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfo
{
    internal BitmapInfoHeader Header;
    internal uint Colors;
}

[StructLayout(LayoutKind.Sequential)]
internal struct IconInfo
{
    [MarshalAs(UnmanagedType.Bool)]
    internal bool IsIcon;
    internal uint HotspotX;
    internal uint HotspotY;
    internal IntPtr MaskBitmap;
    internal IntPtr ColorBitmap;
}

internal static partial class NativeImagingMethods
{
    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW", SetLastError = true)]
    internal static partial int GetObject(IntPtr handle, int size, out GdiBitmapDescription bitmap);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial int GetDIBits(
        IntPtr dc,
        IntPtr bitmap,
        uint start,
        uint lines,
        [Out] byte[] bits,
        ref BitmapInfo info,
        uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(IntPtr handle);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr GetDC(IntPtr window);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial IntPtr MapViewOfFile(
        IntPtr mapping,
        uint desiredAccess,
        uint fileOffsetHigh,
        uint fileOffsetLow,
        nuint bytesToMap);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnmapViewOfFile(IntPtr address);
}
