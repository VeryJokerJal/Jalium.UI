using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Jalium.UI.Styling;

// Produces a private, valid sfnt family for each loaded resource. Rewriting the
// family names avoids collisions with native fonts and other XAML documents.
internal static class CssFontData
{
    internal const int MaximumBytes = 512 * 1024 * 1024;
    private const uint NameTag = 0x6e616d65, HeadTag = 0x68656164;
    private sealed record Table(uint Tag, byte[] Bytes);

    internal static bool TryPrepare(byte[] source, string family, out byte[] prepared)
    {
        prepared = [];
        try
        {
            if (source.Length is < 12 or > MaximumBytes) return false;
            var flavor = U32(source, 0);
            List<Table>? tables;
            if (flavor == 0x774f4646) tables = Woff(source, out flavor);
            else tables = Sfnt(source, ref flavor);
            if (tables is null || flavor is not (0x00010000 or 0x4f54544f or 0x74727565)) return false;
            var names = tables.FindIndex(t => t.Tag == NameTag);
            if (names < 0 || !Rename(tables[names].Bytes, family, out var renamed)) return false;
            tables[names] = new(NameTag, renamed);
            prepared = Assemble(flavor, tables);
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or OverflowException)
        { return false; }
    }

    private static List<Table>? Sfnt(byte[] data, ref uint flavor)
    {
        var start = 0;
        if (flavor == 0x74746366)
        {
            // Collection fragments/face selection can be added without changing
            // registration; the first sfnt is the collection's default face.
            if (data.Length < 16 || U32(data, 8) == 0) return null;
            start = checked((int)U32(data, 12)); flavor = U32(data, start);
        }
        var count = U16(data, start + 4);
        if (count is 0 or > 4096 || start + 12L + count * 16L > data.Length) return null;
        var result = new List<Table>(count); var tags = new HashSet<uint>();
        for (var i = 0; i < count; i++)
        {
            var at = start + 12 + i * 16;
            var tag = U32(data, at); var offset = U32(data, at + 8); var length = U32(data, at + 12);
            if (!tags.Add(tag) || (ulong)offset + length > (ulong)data.Length) return null;
            result.Add(new(tag, data.AsSpan(checked((int)offset), checked((int)length)).ToArray()));
        }
        return result;
    }

    private static List<Table>? Woff(byte[] data, out uint flavor)
    {
        flavor = U32(data, 4);
        if (data.Length < 44 || U32(data, 8) != data.Length) return null;
        var count = U16(data, 12); var total = U32(data, 16);
        if (count is 0 or > 4096 || 44L + count * 20L > data.Length || total > MaximumBytes) return null;
        var tables = new List<Table>(count); var tags = new HashSet<uint>();
        long expanded = 12 + count * 16;
        for (var i = 0; i < count; i++)
        {
            var at = 44 + i * 20;
            var tag = U32(data, at); var offset = U32(data, at + 4);
            var compressed = U32(data, at + 8); var length = U32(data, at + 12);
            expanded += (length + 3L) & ~3L;
            if (!tags.Add(tag) || compressed > length || expanded > total ||
                offset < 44 + count * 20 || (ulong)offset + compressed > (ulong)data.Length) return null;
            var bytes = new byte[checked((int)length)];
            if (compressed == length) data.AsSpan((int)offset, (int)length).CopyTo(bytes);
            else
            {
                using var input = new MemoryStream(data, (int)offset, (int)compressed, false);
                using var stream = new ZLibStream(input, CompressionMode.Decompress);
                stream.ReadExactly(bytes);
                if (stream.ReadByte() != -1) return null;
            }
            if (Checksum(bytes) != U32(data, at + 16)) return null;
            tables.Add(new(tag, bytes));
        }
        return expanded == total ? tables : null;
    }

    private static bool Rename(byte[] data, string family, out byte[] output)
    {
        output = [];
        if (data.Length < 6) return false;
        var count = U16(data, 2); var storage = U16(data, 4);
        if (6L + count * 12L > data.Length || storage > data.Length) return false;
        var records = new List<(ushort Platform, ushort Encoding, ushort Language, ushort Name, byte[] Text)>();
        for (var i = 0; i < count; i++)
        {
            var at = 6 + i * 12;
            var platform = U16(data, at); var encoding = U16(data, at + 2);
            var language = U16(data, at + 4); var name = U16(data, at + 6);
            var length = U16(data, at + 8); var offset = U16(data, at + 10);
            if (storage + (long)offset + length > data.Length) return false;
            // Format-1 language-tag records are not needed for the private name.
            if (language >= 0x8000) continue;
            var text = data.AsSpan(storage + offset, length).ToArray();
            if (name is 1 or 3 or 4 or 16 or 18 or 21)
            {
                if (platform is 0 or 3) text = Encoding.BigEndianUnicode.GetBytes(family);
                else if (platform == 1) text = Encoding.ASCII.GetBytes(family);
                else continue;
            }
            records.Add((platform, encoding, language, name, text));
        }
        // Always provide a Windows Unicode family entry, even in unusual name tables.
        if (!records.Any(r => r.Platform == 3 && r.Name == 1))
            records.Add((3, 1, 0x0409, 1, Encoding.BigEndianUnicode.GetBytes(family)));
        var header = checked(6 + records.Count * 12);
        var size = checked(header + records.Sum(r => r.Text.Length));
        if (size > ushort.MaxValue) return false;
        output = new byte[size]; W16(output, 2, records.Count); W16(output, 4, header);
        var position = header;
        for (var i = 0; i < records.Count; i++)
        {
            var entry = records[i]; var at = 6 + i * 12;
            W16(output, at, entry.Platform); W16(output, at + 2, entry.Encoding); W16(output, at + 4, entry.Language);
            W16(output, at + 6, entry.Name); W16(output, at + 8, entry.Text.Length); W16(output, at + 10, position - header);
            entry.Text.CopyTo(output, position); position += entry.Text.Length;
        }
        return true;
    }

    private static byte[] Assemble(uint flavor, List<Table> tables)
    {
        tables.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        var count = tables.Count; var position = 12 + count * 16;
        var size = checked(position + tables.Sum(t => (t.Bytes.Length + 3) & ~3));
        if (size > MaximumBytes) throw new InvalidDataException();
        var output = new byte[size]; W32(output, 0, flavor); W16(output, 4, count);
        var power = 1; var selector = 0;
        while (power * 2 <= count) { power *= 2; selector++; }
        W16(output, 6, power * 16); W16(output, 8, selector); W16(output, 10, count * 16 - power * 16);
        var head = -1;
        for (var i = 0; i < count; i++)
        {
            var table = tables[i]; var bytes = table.Bytes;
            if (table.Tag == HeadTag)
            {
                if (bytes.Length < 12) throw new InvalidDataException();
                bytes = (byte[])bytes.Clone(); W32(bytes, 8, 0); head = position;
            }
            var at = 12 + i * 16;
            W32(output, at, table.Tag); W32(output, at + 4, Checksum(bytes));
            W32(output, at + 8, (uint)position); W32(output, at + 12, (uint)bytes.Length);
            bytes.CopyTo(output, position); position += (bytes.Length + 3) & ~3;
        }
        if (head < 0) throw new InvalidDataException();
        W32(output, head + 8, unchecked(0xb1b0afba - Checksum(output)));
        return output;
    }

    internal static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        uint sum = 0;
        for (var i = 0; i < bytes.Length; i += 4)
        {
            uint word = 0;
            for (var j = 0; j < 4; j++) word = (word << 8) | (i + j < bytes.Length ? bytes[i + j] : 0u);
            sum = unchecked(sum + word);
        }
        return sum;
    }
    private static ushort U16(byte[] b, int p) => BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(p, 2));
    private static uint U32(byte[] b, int p) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p, 4));
    private static void W16(byte[] b, int p, int v) => BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(p, 2), checked((ushort)v));
    private static void W32(byte[] b, int p, uint v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(p, 4), v);
}
