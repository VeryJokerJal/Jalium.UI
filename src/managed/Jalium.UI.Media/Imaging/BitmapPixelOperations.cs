using System.Buffers.Binary;

namespace Jalium.UI.Media.Imaging;

/// <summary>
/// CPU bitmap operations shared by the WPF-compatible bitmap transform sources.
/// The implementation deliberately works from <see cref="BitmapSource.CopyPixels(Int32Rect, byte[], int, int)"/>
/// so it remains backend-neutral and also works for application-defined bitmap sources.
/// </summary>
internal static class BitmapPixelOperations
{
    internal readonly record struct PixelBuffer(byte[] Pixels, int Width, int Height, int Stride, PixelFormat Format, BitmapPalette? Palette);

    private readonly record struct Pixel(double Red, double Green, double Blue, double Alpha);

    public static int GetStride(int width, PixelFormat format)
    {
        if (width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        return checked((int)(((long)width * format.BitsPerPixel + 7) / 8));
    }

    public static PixelBuffer Read(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        if (width < 0 || height < 0)
        {
            throw new InvalidOperationException("A bitmap source cannot expose negative pixel dimensions.");
        }

        int stride = GetStride(width, source.Format);
        var pixels = new byte[checked(stride * height)];
        if (width > 0 && height > 0)
        {
            source.CopyPixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        }

        return new PixelBuffer(pixels, width, height, stride, source.Format, source.Palette);
    }

    public static void Copy(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        PixelFormat format,
        Int32Rect sourceRect,
        byte[] destination,
        int destinationStride,
        int destinationOffset)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        Int32Rect rect = NormalizeRect(sourceRect, sourceWidth, sourceHeight);
        int bitsPerPixel = format.BitsPerPixel;
        int rowBytes = checked((int)(((long)rect.Width * bitsPerPixel + 7) / 8));

        if (destinationOffset < 0 || destinationOffset > destination.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationOffset));
        }

        if (destinationStride < rowBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationStride), "Stride is smaller than the requested row width.");
        }

        int requiredLength = rect.Height == 0
            ? destinationOffset
            : checked(destinationOffset + ((rect.Height - 1) * destinationStride) + rowBytes);
        if (requiredLength > destination.Length)
        {
            throw new ArgumentException("The destination buffer is too small for the requested pixels.", nameof(destination));
        }

        if (rect.Width == 0 || rect.Height == 0)
        {
            return;
        }

        if ((bitsPerPixel & 7) == 0)
        {
            int bytesPerPixel = bitsPerPixel / 8;
            int sourceXOffset = checked(rect.X * bytesPerPixel);
            for (int row = 0; row < rect.Height; row++)
            {
                Buffer.BlockCopy(
                    source,
                    checked(((rect.Y + row) * sourceStride) + sourceXOffset),
                    destination,
                    checked(destinationOffset + (row * destinationStride)),
                    rowBytes);
            }

            return;
        }

        for (int row = 0; row < rect.Height; row++)
        {
            int destinationRow = checked(destinationOffset + (row * destinationStride));
            Array.Clear(destination, destinationRow, rowBytes);
            int sourceRow = checked((rect.Y + row) * sourceStride);
            for (int column = 0; column < rect.Width; column++)
            {
                int value = ReadPacked(source, sourceRow, rect.X + column, bitsPerPixel);
                WritePacked(destination, destinationRow, column, bitsPerPixel, value);
            }
        }
    }

    public static PixelBuffer Convert(
        BitmapSource source,
        PixelFormat destinationFormat,
        BitmapPalette? destinationPalette,
        double alphaThreshold)
    {
        PixelBuffer input = Read(source);
        int destinationStride = GetStride(input.Width, destinationFormat);
        var destination = new byte[checked(destinationStride * input.Height)];

        for (int y = 0; y < input.Height; y++)
        {
            for (int x = 0; x < input.Width; x++)
            {
                Pixel pixel = ReadPixel(input, x, y);
                WritePixel(destination, destinationStride, x, y, destinationFormat, destinationPalette, alphaThreshold, pixel);
            }
        }

        return new PixelBuffer(destination, input.Width, input.Height, destinationStride, destinationFormat, destinationPalette);
    }

    public static PixelBuffer Transform(BitmapSource source, Matrix matrix)
    {
        PixelBuffer input = Read(source);
        if (input.Width == 0 || input.Height == 0)
        {
            return new PixelBuffer(Array.Empty<byte>(), 0, 0, 0, input.Format, input.Palette);
        }

        if (!matrix.TryInvert(out Matrix inverse))
        {
            throw new InvalidOperationException("The bitmap transform must be invertible.");
        }

        Point first = matrix.Transform(new Point(0, 0));
        Point second = matrix.Transform(new Point(input.Width, 0));
        Point third = matrix.Transform(new Point(0, input.Height));
        Point fourth = matrix.Transform(new Point(input.Width, input.Height));
        double minimumX = SnapNearInteger(Math.Min(Math.Min(first.X, second.X), Math.Min(third.X, fourth.X)));
        double minimumY = SnapNearInteger(Math.Min(Math.Min(first.Y, second.Y), Math.Min(third.Y, fourth.Y)));
        double maximumX = SnapNearInteger(Math.Max(Math.Max(first.X, second.X), Math.Max(third.X, fourth.X)));
        double maximumY = SnapNearInteger(Math.Max(Math.Max(first.Y, second.Y), Math.Max(third.Y, fourth.Y)));
        int boundsX = checked((int)Math.Floor(minimumX));
        int boundsY = checked((int)Math.Floor(minimumY));
        int width = checked((int)Math.Ceiling(maximumX) - boundsX);
        int height = checked((int)Math.Ceiling(maximumY) - boundsY);
        int stride = GetStride(width, input.Format);
        var destination = new byte[checked(stride * height)];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Point sourcePoint = inverse.Transform(new Point(boundsX + x + 0.5, boundsY + y + 0.5));
                int sourceX = (int)Math.Floor(sourcePoint.X);
                int sourceY = (int)Math.Floor(sourcePoint.Y);
                if ((uint)sourceX >= (uint)input.Width || (uint)sourceY >= (uint)input.Height)
                {
                    continue;
                }

                Pixel pixel = ReadPixel(input, sourceX, sourceY);
                WritePixel(destination, stride, x, y, input.Format, input.Palette, 0, pixel);
            }
        }

        return new PixelBuffer(destination, width, height, stride, input.Format, input.Palette);
    }

    public static Int32Rect NormalizeRect(Int32Rect rect, int width, int height)
    {
        if (rect.IsEmpty)
        {
            rect = new Int32Rect(0, 0, width, height);
        }

        if (rect.X < 0 || rect.Y < 0 || rect.Width < 0 || rect.Height < 0 ||
            rect.X > width - rect.Width || rect.Y > height - rect.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), "The source rectangle is outside the bitmap bounds.");
        }

        return rect;
    }

    private static Pixel ReadPixel(PixelBuffer source, int x, int y)
    {
        PixelFormat format = source.Format;
        int row = checked(y * source.Stride);
        if (format == PixelFormat.BlackWhite || format == PixelFormat.Gray2 || format == PixelFormat.Gray4 ||
            format == PixelFormat.Indexed1 || format == PixelFormat.Indexed2 || format == PixelFormat.Indexed4)
        {
            int bits = format.BitsPerPixel;
            int value = ReadPacked(source.Pixels, row, x, bits);
            return format == PixelFormat.Indexed1 || format == PixelFormat.Indexed2 || format == PixelFormat.Indexed4
                ? PalettePixel(source.Palette, value, (1 << bits) - 1)
                : GrayPixel(value / (double)((1 << bits) - 1));
        }

        int offset = checked(row + (x * (format.BitsPerPixel / 8)));
        if (format == PixelFormat.Indexed8)
        {
            return PalettePixel(source.Palette, source.Pixels[offset], 255);
        }

        if (format == PixelFormat.Gray8)
        {
            return GrayPixel(source.Pixels[offset] / 255d);
        }

        if (format == PixelFormat.Gray16)
        {
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(source.Pixels.AsSpan(offset, 2));
            return GrayPixel(value / 65535d);
        }

        if (format == PixelFormat.Gray32Float)
        {
            return GrayPixel(ReadFloat(source.Pixels, offset));
        }

        if (format == PixelFormat.Bgr555)
        {
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(source.Pixels.AsSpan(offset, 2));
            return new Pixel(((value >> 10) & 31) / 31d, ((value >> 5) & 31) / 31d, (value & 31) / 31d, 1);
        }

        if (format == PixelFormat.Bgr565)
        {
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(source.Pixels.AsSpan(offset, 2));
            return new Pixel(((value >> 11) & 31) / 31d, ((value >> 5) & 63) / 63d, (value & 31) / 31d, 1);
        }

        if (format == PixelFormat.Bgr24)
        {
            return BytePixel(source.Pixels[offset + 2], source.Pixels[offset + 1], source.Pixels[offset], 255);
        }

        if (format == PixelFormat.Rgb24)
        {
            return BytePixel(source.Pixels[offset], source.Pixels[offset + 1], source.Pixels[offset + 2], 255);
        }

        if (format == PixelFormat.Bgr32)
        {
            return BytePixel(source.Pixels[offset + 2], source.Pixels[offset + 1], source.Pixels[offset], 255);
        }

        if (format == PixelFormat.Rgb32)
        {
            return BytePixel(source.Pixels[offset], source.Pixels[offset + 1], source.Pixels[offset + 2], 255);
        }

        if (format == PixelFormat.Bgra32)
        {
            return BytePixel(source.Pixels[offset + 2], source.Pixels[offset + 1], source.Pixels[offset], source.Pixels[offset + 3]);
        }

        if (format == PixelFormat.Rgba32)
        {
            return BytePixel(source.Pixels[offset], source.Pixels[offset + 1], source.Pixels[offset + 2], source.Pixels[offset + 3]);
        }

        if (format == PixelFormat.Pbgra32)
        {
            double alpha = source.Pixels[offset + 3] / 255d;
            return alpha <= 0
                ? new Pixel(0, 0, 0, 0)
                : new Pixel(
                    Clamp01((source.Pixels[offset + 2] / 255d) / alpha),
                    Clamp01((source.Pixels[offset + 1] / 255d) / alpha),
                    Clamp01((source.Pixels[offset] / 255d) / alpha),
                    alpha);
        }

        if (format == PixelFormat.Bgr101010)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(source.Pixels.AsSpan(offset, 4));
            return new Pixel(((value >> 20) & 1023) / 1023d, ((value >> 10) & 1023) / 1023d, (value & 1023) / 1023d, 1);
        }

        if (format == PixelFormat.Cmyk32)
        {
            double cyan = source.Pixels[offset] / 255d;
            double magenta = source.Pixels[offset + 1] / 255d;
            double yellow = source.Pixels[offset + 2] / 255d;
            double black = source.Pixels[offset + 3] / 255d;
            return new Pixel((1 - cyan) * (1 - black), (1 - magenta) * (1 - black), (1 - yellow) * (1 - black), 1);
        }

        if (format == PixelFormat.Rgb48)
        {
            return new Pixel(ReadUInt16(source.Pixels, offset) / 65535d, ReadUInt16(source.Pixels, offset + 2) / 65535d, ReadUInt16(source.Pixels, offset + 4) / 65535d, 1);
        }

        if (format == PixelFormat.Rgba64 || format == PixelFormat.Prgba64)
        {
            double alpha = ReadUInt16(source.Pixels, offset + 6) / 65535d;
            double red = ReadUInt16(source.Pixels, offset) / 65535d;
            double green = ReadUInt16(source.Pixels, offset + 2) / 65535d;
            double blue = ReadUInt16(source.Pixels, offset + 4) / 65535d;
            if (format == PixelFormat.Prgba64 && alpha > 0)
            {
                red = Clamp01(red / alpha);
                green = Clamp01(green / alpha);
                blue = Clamp01(blue / alpha);
            }

            return new Pixel(red, green, blue, alpha);
        }

        if (format == PixelFormat.Rgb128Float)
        {
            return new Pixel(ReadFloat(source.Pixels, offset), ReadFloat(source.Pixels, offset + 4), ReadFloat(source.Pixels, offset + 8), 1);
        }

        if (format == PixelFormat.Rgba128Float || format == PixelFormat.Prgba128Float)
        {
            double red = ReadFloat(source.Pixels, offset);
            double green = ReadFloat(source.Pixels, offset + 4);
            double blue = ReadFloat(source.Pixels, offset + 8);
            double alpha = ReadFloat(source.Pixels, offset + 12);
            if (format == PixelFormat.Prgba128Float && alpha > 0)
            {
                red /= alpha;
                green /= alpha;
                blue /= alpha;
            }

            return new Pixel(red, green, blue, alpha);
        }

        throw new NotSupportedException($"Pixel format '{format}' is not supported by bitmap conversion.");
    }

    private static void WritePixel(
        byte[] destination,
        int stride,
        int x,
        int y,
        PixelFormat format,
        BitmapPalette? palette,
        double alphaThreshold,
        Pixel pixel)
    {
        int row = checked(y * stride);
        pixel = new Pixel(Clamp01(pixel.Red), Clamp01(pixel.Green), Clamp01(pixel.Blue), Clamp01(pixel.Alpha));
        if (format == PixelFormat.BlackWhite || format == PixelFormat.Gray2 || format == PixelFormat.Gray4)
        {
            int bits = format.BitsPerPixel;
            int maximum = (1 << bits) - 1;
            int value = (int)Math.Round(Luminance(pixel) * maximum, MidpointRounding.AwayFromZero);
            WritePacked(destination, row, x, bits, value);
            return;
        }

        if (format == PixelFormat.Indexed1 || format == PixelFormat.Indexed2 || format == PixelFormat.Indexed4)
        {
            int bits = format.BitsPerPixel;
            WritePacked(destination, row, x, bits, FindPaletteIndex(palette, pixel, alphaThreshold, (1 << bits) - 1));
            return;
        }

        int offset = checked(row + (x * (format.BitsPerPixel / 8)));
        if (format == PixelFormat.Indexed8)
        {
            destination[offset] = (byte)FindPaletteIndex(palette, pixel, alphaThreshold, 255);
            return;
        }

        if (format == PixelFormat.Gray8)
        {
            destination[offset] = ToByte(Luminance(pixel));
            return;
        }

        if (format == PixelFormat.Gray16)
        {
            WriteUInt16(destination, offset, ToUInt16(Luminance(pixel)));
            return;
        }

        if (format == PixelFormat.Gray32Float)
        {
            WriteFloat(destination, offset, Luminance(pixel));
            return;
        }

        if (format == PixelFormat.Bgr555)
        {
            ushort value = (ushort)((ToBits(pixel.Red, 31) << 10) | (ToBits(pixel.Green, 31) << 5) | ToBits(pixel.Blue, 31));
            WriteUInt16(destination, offset, value);
            return;
        }

        if (format == PixelFormat.Bgr565)
        {
            ushort value = (ushort)((ToBits(pixel.Red, 31) << 11) | (ToBits(pixel.Green, 63) << 5) | ToBits(pixel.Blue, 31));
            WriteUInt16(destination, offset, value);
            return;
        }

        byte red = ToByte(pixel.Red);
        byte green = ToByte(pixel.Green);
        byte blue = ToByte(pixel.Blue);
        byte alpha = ToByte(pixel.Alpha);
        if (format == PixelFormat.Bgr24)
        {
            destination[offset] = blue; destination[offset + 1] = green; destination[offset + 2] = red;
            return;
        }

        if (format == PixelFormat.Rgb24)
        {
            destination[offset] = red; destination[offset + 1] = green; destination[offset + 2] = blue;
            return;
        }

        if (format == PixelFormat.Bgr32)
        {
            destination[offset] = blue; destination[offset + 1] = green; destination[offset + 2] = red; destination[offset + 3] = 0;
            return;
        }

        if (format == PixelFormat.Rgb32)
        {
            destination[offset] = red; destination[offset + 1] = green; destination[offset + 2] = blue; destination[offset + 3] = 0;
            return;
        }

        if (format == PixelFormat.Bgra32)
        {
            destination[offset] = blue; destination[offset + 1] = green; destination[offset + 2] = red; destination[offset + 3] = alpha;
            return;
        }

        if (format == PixelFormat.Rgba32)
        {
            destination[offset] = red; destination[offset + 1] = green; destination[offset + 2] = blue; destination[offset + 3] = alpha;
            return;
        }

        if (format == PixelFormat.Pbgra32)
        {
            destination[offset] = ToByte(pixel.Blue * pixel.Alpha);
            destination[offset + 1] = ToByte(pixel.Green * pixel.Alpha);
            destination[offset + 2] = ToByte(pixel.Red * pixel.Alpha);
            destination[offset + 3] = alpha;
            return;
        }

        if (format == PixelFormat.Bgr101010)
        {
            uint value = (uint)((ToBits(pixel.Red, 1023) << 20) | (ToBits(pixel.Green, 1023) << 10) | ToBits(pixel.Blue, 1023));
            BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset, 4), value);
            return;
        }

        if (format == PixelFormat.Cmyk32)
        {
            double black = 1 - Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue));
            double denominator = Math.Max(1 - black, double.Epsilon);
            destination[offset] = ToByte((1 - pixel.Red - black) / denominator);
            destination[offset + 1] = ToByte((1 - pixel.Green - black) / denominator);
            destination[offset + 2] = ToByte((1 - pixel.Blue - black) / denominator);
            destination[offset + 3] = ToByte(black);
            return;
        }

        if (format == PixelFormat.Rgb48)
        {
            WriteUInt16(destination, offset, ToUInt16(pixel.Red));
            WriteUInt16(destination, offset + 2, ToUInt16(pixel.Green));
            WriteUInt16(destination, offset + 4, ToUInt16(pixel.Blue));
            return;
        }

        if (format == PixelFormat.Rgba64 || format == PixelFormat.Prgba64)
        {
            double factor = format == PixelFormat.Prgba64 ? pixel.Alpha : 1;
            WriteUInt16(destination, offset, ToUInt16(pixel.Red * factor));
            WriteUInt16(destination, offset + 2, ToUInt16(pixel.Green * factor));
            WriteUInt16(destination, offset + 4, ToUInt16(pixel.Blue * factor));
            WriteUInt16(destination, offset + 6, ToUInt16(pixel.Alpha));
            return;
        }

        if (format == PixelFormat.Rgb128Float)
        {
            WriteFloat(destination, offset, pixel.Red);
            WriteFloat(destination, offset + 4, pixel.Green);
            WriteFloat(destination, offset + 8, pixel.Blue);
            WriteFloat(destination, offset + 12, 1);
            return;
        }

        if (format == PixelFormat.Rgba128Float || format == PixelFormat.Prgba128Float)
        {
            double factor = format == PixelFormat.Prgba128Float ? pixel.Alpha : 1;
            WriteFloat(destination, offset, pixel.Red * factor);
            WriteFloat(destination, offset + 4, pixel.Green * factor);
            WriteFloat(destination, offset + 8, pixel.Blue * factor);
            WriteFloat(destination, offset + 12, pixel.Alpha);
            return;
        }

        throw new NotSupportedException($"Pixel format '{format}' is not supported by bitmap conversion.");
    }

    private static Pixel PalettePixel(BitmapPalette? palette, int index, int maximum)
    {
        if (palette is not null && index < palette.Colors.Count)
        {
            Color color = palette.Colors[index];
            return BytePixel(color.R, color.G, color.B, color.A);
        }

        return GrayPixel(index / (double)Math.Max(maximum, 1));
    }

    private static int FindPaletteIndex(BitmapPalette? palette, Pixel pixel, double alphaThreshold, int maximumIndex)
    {
        if (palette is null || palette.Colors.Count == 0)
        {
            return Math.Clamp((int)Math.Round(Luminance(pixel) * maximumIndex, MidpointRounding.AwayFromZero), 0, maximumIndex);
        }

        int limit = Math.Min(palette.Colors.Count, maximumIndex + 1);
        bool preferTransparent = pixel.Alpha * 100 <= alphaThreshold;
        int bestIndex = 0;
        double bestDistance = double.PositiveInfinity;
        for (int index = 0; index < limit; index++)
        {
            Color color = palette.Colors[index];
            if (preferTransparent && color.A == 0)
            {
                return index;
            }

            double alpha = color.A / 255d;
            double red = color.R / 255d;
            double green = color.G / 255d;
            double blue = color.B / 255d;
            double distance = ((pixel.Alpha - alpha) * (pixel.Alpha - alpha) * 2) +
                              ((pixel.Red - red) * (pixel.Red - red)) +
                              ((pixel.Green - green) * (pixel.Green - green)) +
                              ((pixel.Blue - blue) * (pixel.Blue - blue));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private static int ReadPacked(byte[] buffer, int rowOffset, int x, int bitsPerPixel)
    {
        int bitOffset = checked(x * bitsPerPixel);
        int byteOffset = checked(rowOffset + (bitOffset >> 3));
        int shift = 8 - bitsPerPixel - (bitOffset & 7);
        return (buffer[byteOffset] >> shift) & ((1 << bitsPerPixel) - 1);
    }

    private static void WritePacked(byte[] buffer, int rowOffset, int x, int bitsPerPixel, int value)
    {
        int bitOffset = checked(x * bitsPerPixel);
        int byteOffset = checked(rowOffset + (bitOffset >> 3));
        int shift = 8 - bitsPerPixel - (bitOffset & 7);
        int mask = ((1 << bitsPerPixel) - 1) << shift;
        buffer[byteOffset] = (byte)((buffer[byteOffset] & ~mask) | ((value << shift) & mask));
    }

    private static Pixel BytePixel(byte red, byte green, byte blue, byte alpha)
        => new(red / 255d, green / 255d, blue / 255d, alpha / 255d);

    private static Pixel GrayPixel(double value) => new(value, value, value, 1);

    private static double Luminance(Pixel pixel) => (pixel.Red * 0.2126) + (pixel.Green * 0.7152) + (pixel.Blue * 0.0722);

    private static double Clamp01(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);

    private static double SnapNearInteger(double value)
    {
        double integer = Math.Round(value);
        return Math.Abs(value - integer) <= 1e-10 ? integer : value;
    }

    private static byte ToByte(double value) => (byte)Math.Round(Clamp01(value) * 255, MidpointRounding.AwayFromZero);

    private static ushort ToUInt16(double value) => (ushort)Math.Round(Clamp01(value) * 65535, MidpointRounding.AwayFromZero);

    private static int ToBits(double value, int maximum) => (int)Math.Round(Clamp01(value) * maximum, MidpointRounding.AwayFromZero);

    private static ushort ReadUInt16(byte[] buffer, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2));

    private static void WriteUInt16(byte[] buffer, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset, 2), value);

    private static float ReadFloat(byte[] buffer, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, 4)));

    private static void WriteFloat(byte[] buffer, int offset, double value)
        => BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), BitConverter.SingleToInt32Bits((float)value));
}
