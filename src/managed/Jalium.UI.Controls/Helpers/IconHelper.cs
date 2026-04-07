using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Jalium.UI.Controls.Helpers;

/// <summary>
/// Extracts an application icon from an executable and encodes it as PNG
/// using only Win32 P/Invoke — no System.Drawing dependency.
/// </summary>
internal static partial class IconHelper
{
    internal static byte[]? ExtractProcessIconAsPng(string exePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return ExtractProcessIconAsPngWindows(exePath);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[]? ExtractProcessIconAsPngWindows(string exePath)
    {
        nint hIconLarge = 0;
        nint hIconSmall = 0;
        try
        {
            var count = ExtractIconExW(exePath, 0, out hIconLarge, out hIconSmall, 1);
            if (count == 0 || hIconLarge == 0)
            {
                // Fall back to default application icon.
                hIconLarge = LoadIconW(0, IDI_APPLICATION);
                if (hIconLarge == 0)
                {
                    return null;
                }
            }

            return IconHandleToPng(hIconLarge);
        }
        finally
        {
            if (hIconLarge != 0) DestroyIcon(hIconLarge);
            if (hIconSmall != 0) DestroyIcon(hIconSmall);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[]? IconHandleToPng(nint hIcon)
    {
        if (!GetIconInfo(hIcon, out var iconInfo))
        {
            return null;
        }

        nint hdc = 0;
        try
        {
            var hbmColor = iconInfo.hbmColor;
            if (hbmColor == 0)
            {
                return null;
            }

            // Get bitmap dimensions.
            var bmpSize = Marshal.SizeOf<BITMAP>();
            var bmp = new BITMAP();
            if (GetObjectW(hbmColor, bmpSize, ref bmp) == 0)
            {
                return null;
            }

            var width = bmp.bmWidth;
            var height = bmp.bmHeight;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            // Prepare BITMAPINFOHEADER for 32-bit BGRA.
            var bih = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };

            var pixelData = new byte[width * height * 4];
            hdc = CreateCompatibleDC(0);
            if (hdc == 0)
            {
                return null;
            }

            var lines = GetDIBits(hdc, hbmColor, 0, (uint)height, pixelData, ref bih, 0);
            if (lines == 0)
            {
                return null;
            }

            // Check if the color bitmap already has an alpha channel.
            var hasAlpha = false;
            for (var i = 3; i < pixelData.Length; i += 4)
            {
                if (pixelData[i] != 0)
                {
                    hasAlpha = true;
                    break;
                }
            }

            if (!hasAlpha && iconInfo.hbmMask != 0)
            {
                // Read the mask bitmap and apply it as alpha.
                var maskData = new byte[width * height * 4];
                var maskBih = bih;
                var maskLines = GetDIBits(hdc, iconInfo.hbmMask, 0, (uint)height, maskData, ref maskBih, 0);
                if (maskLines > 0)
                {
                    for (var i = 0; i < width * height; i++)
                    {
                        // In AND mask: 0 = opaque, 1 = transparent (when reading as 32bpp, 0x00 = opaque, 0xFF = transparent).
                        pixelData[i * 4 + 3] = (byte)(maskData[i * 4] == 0 ? 255 : 0);
                    }
                }
                else
                {
                    // If mask read fails, make fully opaque.
                    for (var i = 3; i < pixelData.Length; i += 4)
                    {
                        pixelData[i] = 255;
                    }
                }
            }

            // Convert BGRA to RGBA in-place.
            for (var i = 0; i < pixelData.Length; i += 4)
            {
                (pixelData[i], pixelData[i + 2]) = (pixelData[i + 2], pixelData[i]);
            }

            return EncodePng(width, height, pixelData);
        }
        finally
        {
            if (hdc != 0) DeleteDC(hdc);
            if (iconInfo.hbmColor != 0) DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != 0) DeleteObject(iconInfo.hbmMask);
        }
    }

    /// <summary>
    /// Encodes raw RGBA pixel data as a minimal PNG file.
    /// </summary>
    private static byte[] EncodePng(int width, int height, byte[] rgbaPixels)
    {
        using var output = new MemoryStream();

        // PNG signature.
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        // IHDR chunk.
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type: RGBA
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace
        WriteChunk(output, "IHDR"u8, ihdr);

        // IDAT chunk: deflate the filtered scanlines.
        byte[] idatPayload;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
            {
                var stride = width * 4;
                for (var y = 0; y < height; y++)
                {
                    zlib.WriteByte(0); // filter: None
                    zlib.Write(rgbaPixels, y * stride, stride);
                }
            }

            idatPayload = compressedStream.ToArray();
        }

        WriteChunk(output, "IDAT"u8, idatPayload);

        // IEND chunk.
        WriteChunk(output, "IEND"u8, []);

        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> lengthBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBuf, data.Length);
        output.Write(lengthBuf);
        output.Write(type);
        output.Write(data);

        // CRC32 over type + data.
        var crcState = UpdateCrc32(0xFFFFFFFF, type);
        crcState = UpdateCrc32(crcState, data);
        var crcValue = crcState ^ 0xFFFFFFFF;
        Span<byte> crcBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBuf, crcValue);
        output.Write(crcBuf);
    }

    #region CRC32

    private static readonly uint[] Crc32Table = GenerateCrc32Table();

    private static uint[] GenerateCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var crc = i;
            for (var j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }

            table[i] = crc;
        }

        return table;
    }

    private static uint UpdateCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    #endregion

    #region Win32 P/Invoke

    private const nint IDI_APPLICATION = 32512;

    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint ExtractIconExW(string lpszFile, int nIconIndex, out nint phiconLarge, out nint phiconSmall, uint nIcons);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(nint hIcon, out ICONINFO piconinfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    private static partial nint LoadIconW(nint hInstance, nint lpIconName);

    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static partial int GetObjectW(nint h, int c, ref BITMAP pv);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint hdc, nint hbm, uint start, uint cLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint ho);

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public int xHotspot;
        public int yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    #endregion
}
