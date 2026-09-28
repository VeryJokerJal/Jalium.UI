using System.Buffers.Binary;

namespace Jalium.UI.Styling;

internal static partial class CssFontData
{
    private sealed record Woff2GlyphData(byte[] Glyphs, byte[] Locations);

    private static Woff2GlyphData DecodeWoff2Glyphs(ReadOnlySpan<byte> data, int glyphCount, int indexFormat)
    {
        var header = new Woff2Reader(data);
        if (header.UInt16() != 0) throw InvalidWoff2();
        var options = header.UInt16(); var count = header.UInt16(); var format = header.UInt16();
        if (count != glyphCount || format != indexFormat || format > 1) throw InvalidWoff2();
        Span<int> lengths = stackalloc int[7];
        for (var i = 0; i < lengths.Length; i++) lengths[i] = checked((int)header.UInt32());
        var contours = new Woff2Reader(header.Read(lengths[0]));
        var counts = new Woff2Reader(header.Read(lengths[1]));
        var flags = new Woff2Reader(header.Read(lengths[2]));
        var coordinates = new Woff2Reader(header.Read(lengths[3]));
        var composites = new Woff2Reader(header.Read(lengths[4]));
        var bounds = new Woff2Reader(header.Read(lengths[5]));
        var instructions = new Woff2Reader(header.Read(lengths[6]));
        var bbox = bounds.Read(checked((glyphCount + 31) / 32 * 4));
        var overlap = (options & 1) == 0 ? ReadOnlySpan<byte>.Empty : header.Read((glyphCount + 7) / 8);
        if (!header.AtEnd || lengths[0] != glyphCount * 2) throw InvalidWoff2();
        using var glyphs = new Woff2Writer(); using var locations = new Woff2Writer();
        void Location()
        {
            if (format == 0)
            {
                if (glyphs.Length / 2 > ushort.MaxValue) throw InvalidWoff2();
                locations.UInt16(glyphs.Length / 2);
            }
            else locations.UInt32((uint)glyphs.Length);
        }
        for (var glyph = 0; glyph < glyphCount; glyph++)
        {
            Location();
            var number = contours.Int16();
            var explicitBounds = (bbox[glyph >> 3] & (0x80 >> (glyph & 7))) != 0;
            if (number == 0)
            {
                if (explicitBounds) throw InvalidWoff2();
                continue;
            }
            if (number < -1) throw InvalidWoff2();
            if (number == -1)
            {
                if (!explicitBounds) throw InvalidWoff2();
                glyphs.UInt16(-1); glyphs.Write(bounds.Read(8));
                var more = true; var hasInstructions = false;
                while (more)
                {
                    var componentFlags = composites.UInt16();
                    var extra = (componentFlags & 1) != 0 ? 6 : 4; // glyph index and arguments
                    if ((componentFlags & 8) != 0) extra += 2;
                    else if ((componentFlags & 64) != 0) extra += 4;
                    else if ((componentFlags & 128) != 0) extra += 8;
                    var component = composites.Read(extra);
                    if (BinaryPrimitives.ReadUInt16BigEndian(component) >= glyphCount) throw InvalidWoff2();
                    glyphs.UInt16(componentFlags); glyphs.Write(component);
                    hasInstructions |= (componentFlags & 256) != 0;
                    more = (componentFlags & 32) != 0;
                }
                if (hasInstructions)
                {
                    var length = coordinates.UInt255();
                    glyphs.UInt16(length); glyphs.Write(instructions.Read(length));
                }
            }
            else
            {
                var ends = new int[number]; var points = 0;
                for (var i = 0; i < number; i++)
                {
                    var n = counts.UInt255();
                    if (n == 0 || points + n > 65536) throw InvalidWoff2();
                    points += n; ends[i] = points - 1;
                }
                var pointFlags = flags.Read(points);
                var xs = new int[points]; var ys = new int[points];
                var dxs = new int[points]; var dys = new int[points];
                var x = 0; var y = 0;
                for (var i = 0; i < points; i++)
                {
                    DecodeWoff2Triplet(pointFlags[i] & 127, ref coordinates, out var dx, out var dy);
                    x = checked(x + dx); y = checked(y + dy);
                    xs[i] = x; ys[i] = y; dxs[i] = dx; dys[i] = dy;
                }
                var instructionCount = coordinates.UInt255();
                glyphs.UInt16(number);
                if (explicitBounds) glyphs.Write(bounds.Read(8));
                else
                {
                    glyphs.UInt16(xs.Min()); glyphs.UInt16(ys.Min());
                    glyphs.UInt16(xs.Max()); glyphs.UInt16(ys.Max());
                }
                foreach (var end in ends) glyphs.UInt16(end);
                glyphs.UInt16(instructionCount); glyphs.Write(instructions.Read(instructionCount));
                var hasOverlap = !overlap.IsEmpty && (overlap[glyph >> 3] & (0x80 >> (glyph & 7))) != 0;
                WriteWoff2Points(glyphs, pointFlags, dxs, dys, hasOverlap);
            }
            // Word alignment preserves short-loca capacity. Tables receive their
            // independent four-byte alignment when the sfnt is assembled.
            glyphs.Align2();
        }
        Location();
        if (!contours.AtEnd || !counts.AtEnd || !flags.AtEnd || !coordinates.AtEnd ||
            !composites.AtEnd || !bounds.AtEnd || !instructions.AtEnd) throw InvalidWoff2();
        return new(glyphs.Bytes(), locations.Bytes());
    }

    private static void DecodeWoff2Triplet(int flag, ref Woff2Reader bytes, out int dx, out int dy)
    {
        static int Signed(int sign, int value) => (sign & 1) == 0 ? -value : value;
        if (flag < 10) { dx = 0; dy = Signed(flag, (flag & 14) * 128 + bytes.Byte()); }
        else if (flag < 20) { dx = Signed(flag, ((flag - 10) & 14) * 128 + bytes.Byte()); dy = 0; }
        else if (flag < 84)
        {
            var group = flag - 20; var value = bytes.Byte();
            dx = Signed(flag, 1 + (group & 48) + (value >> 4));
            dy = Signed(flag >> 1, 1 + (group & 12) * 4 + (value & 15));
        }
        else if (flag < 120)
        {
            var group = flag - 84;
            dx = Signed(flag, 1 + group / 12 * 256 + bytes.Byte());
            dy = Signed(flag >> 1, 1 + (group % 12) / 4 * 256 + bytes.Byte());
        }
        else if (flag < 124)
        {
            var first = bytes.Byte(); var middle = bytes.Byte(); var last = bytes.Byte();
            dx = Signed(flag, first * 16 + (middle >> 4));
            dy = Signed(flag >> 1, (middle & 15) * 256 + last);
        }
        else { dx = Signed(flag, bytes.UInt16()); dy = Signed(flag >> 1, bytes.UInt16()); }
    }

    private static void WriteWoff2Points(Woff2Writer output, ReadOnlySpan<byte> original, int[] dx, int[] dy, bool overlap)
    {
        var flags = new byte[original.Length];
        for (var i = 0; i < flags.Length; i++)
        {
            var flag = (original[i] & 128) == 0 ? 1 : 0;
            if (i == 0 && overlap) flag |= 64;
            if (dx[i] == 0) flag |= 16;
            else if (dx[i] is > -256 and < 256) flag |= 2 | (dx[i] > 0 ? 16 : 0);
            if (dy[i] == 0) flag |= 32;
            else if (dy[i] is > -256 and < 256) flag |= 4 | (dy[i] > 0 ? 32 : 0);
            flags[i] = (byte)flag;
        }
        for (var i = 0; i < flags.Length;)
        {
            var repeat = 1;
            while (i + repeat < flags.Length && repeat < 256 && flags[i + repeat] == flags[i]) repeat++;
            output.Byte(flags[i] | (repeat > 1 ? 8 : 0));
            if (repeat > 1) output.Byte(repeat - 1);
            i += repeat;
        }
        for (var i = 0; i < flags.Length; i++)
            if ((flags[i] & 2) != 0) output.Byte(Math.Abs(dx[i])); else if (dx[i] != 0) output.UInt16(dx[i]);
        for (var i = 0; i < flags.Length; i++)
            if ((flags[i] & 4) != 0) output.Byte(Math.Abs(dy[i])); else if (dy[i] != 0) output.UInt16(dy[i]);
    }

    private static byte[] DecodeWoff2Metrics(ReadOnlySpan<byte> data, int glyphCount, int metricCount,
        int indexFormat, byte[] glyphs, byte[] locations)
    {
        if (indexFormat is not (0 or 1) || metricCount == 0 || metricCount > glyphCount ||
            locations.Length != (glyphCount + 1) * (indexFormat == 0 ? 2 : 4)) throw InvalidWoff2();
        var reader = new Woff2Reader(data); var flags = reader.Byte();
        if ((flags & 252) != 0 || (flags & 3) == 0) throw InvalidWoff2();
        var advances = new ushort[metricCount];
        for (var i = 0; i < metricCount; i++) advances[i] = reader.UInt16();
        var bearings = new short[glyphCount];
        uint Location(int index) => indexFormat == 0 ? (uint)U16(locations, index * 2) * 2 : U32(locations, index * 4);
        short XMinimum(int index)
        {
            var start = Location(index); var end = Location(index + 1);
            if (start > end || end > glyphs.Length) throw InvalidWoff2();
            if (start == end) return 0;
            if (end - start < 10) throw InvalidWoff2();
            return U16(glyphs, (int)start) == 0 ? (short)0 : unchecked((short)U16(glyphs, (int)start + 2));
        }
        for (var i = 0; i < metricCount; i++) bearings[i] = (flags & 1) == 0 ? reader.Int16() : XMinimum(i);
        for (var i = metricCount; i < glyphCount; i++) bearings[i] = (flags & 2) == 0 ? reader.Int16() : XMinimum(i);
        if (!reader.AtEnd) throw InvalidWoff2();
        using var output = new Woff2Writer();
        for (var i = 0; i < glyphCount; i++)
        {
            if (i < metricCount) output.UInt16(advances[i]);
            output.UInt16(bearings[i]);
        }
        return output.Bytes();
    }
}
