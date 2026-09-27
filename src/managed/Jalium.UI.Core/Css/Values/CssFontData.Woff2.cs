using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Jalium.UI.Styling;

internal static partial class CssFontData
{
    private const uint GlyfTag = 0x676c7966, LocaTag = 0x6c6f6361, HmtxTag = 0x686d7478,
        HheaTag = 0x68686561, MaxpTag = 0x6d617870;
    private static readonly string[] Woff2Tags =
    [
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep", "CFF ", "VORG", "EBDT",
        "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE", "GDEF", "GPOS", "GSUB", "EBSC", "JSTF", "MATH",
        "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt", "avar", "bdat", "bloc", "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar",
        "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop", "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill"
    ];

    private sealed record Woff2Table(uint Tag, uint OriginalLength, int Length, int Offset, bool Transformed);
    private sealed record Woff2Face(uint Flavor, int[] Tables);

    internal static byte[] DecodeWoff2(byte[] data)
    {
        var tables = Woff2(data, out var flavor, null);
        return Assemble(flavor, tables);
    }

    private static List<Table> Woff2(byte[] data, out uint flavor, string? postScriptName)
    {
        if (data.Length is < 48 or > MaximumBytes || U32(data, 0) != 0x774f4632 || U32(data, 8) != data.Length) throw InvalidWoff2();
        flavor = U32(data, 4);
        var count = U16(data, 12);
        if (count is 0 or > 4095) throw InvalidWoff2();
        // reserved and totalSfntSize are not validity checks: the reconstructed
        // outlines may use a different valid byte encoding and therefore size.
        var reader = new Woff2Reader(data.AsSpan(48));
        var tables = new Woff2Table[count];
        var total = 0;
        for (var i = 0; i < count; i++)
        {
            var flags = reader.Byte(); var index = flags & 63; var version = flags >> 6;
            var tag = index == 63 ? reader.UInt32() : FontTag(Woff2Tags[index]);
            var outline = tag is GlyfTag or LocaTag;
            if (outline ? version is not (0 or 3) : tag == HmtxTag ? version is not (0 or 1) : version != 0)
                throw InvalidWoff2();
            var transformed = outline ? version == 0 : version != 0;
            var original = reader.Base128();
            var length = transformed ? reader.Base128() : original;
            if (length > MaximumBytes || tag == LocaTag && transformed && length != 0 || total + (long)length > MaximumBytes)
                throw InvalidWoff2();
            tables[i] = new(tag, original, (int)length, total, transformed);
            total += (int)length;
        }

        var faces = new List<Woff2Face>();
        if (flavor == 0x74746366)
        {
            var version = reader.UInt32(); var fontCount = reader.UInt255();
            if (version is not (0x00010000 or 0x00020000) || fontCount == 0) throw InvalidWoff2();
            for (var f = 0; f < fontCount; f++)
            {
                var number = reader.UInt255(); var faceFlavor = reader.UInt32();
                if (number == 0 || number > count) throw InvalidWoff2();
                var indices = new int[number];
                for (var i = 0; i < number; i++)
                { indices[i] = reader.UInt255(); if (indices[i] >= count) throw InvalidWoff2(); }
                faces.Add(new(faceFlavor, indices));
            }
        }
        else faces.Add(new(flavor, Enumerable.Range(0, count).ToArray()));
        foreach (var face in faces) ValidateWoff2Face(face, tables, flavor == 0x74746366);

        var start = checked(48 + reader.Position);
        var compressed = U32(data, 20);
        if (compressed == 0 || compressed > data.Length - start) throw InvalidWoff2();
        ValidateWoff2Blocks(data, checked(start + (int)compressed));
        var decoded = new byte[checked(total + 1)];
        using (var decoder = new BrotliDecoder())
        {
            var status = decoder.Decompress(data.AsSpan(start, (int)compressed), decoded, out var consumed, out var written);
            if (status != OperationStatus.Done || consumed != compressed || written != total) throw InvalidWoff2();
        }

        // The fragment names one face in the collection. Keep the directory's
        // default order when no fragment was supplied.
        var selected = faces[0];
        if (postScriptName is not null)
        {
            selected = faces.FirstOrDefault(face => face.Tables.Any(index =>
                tables[index].Tag == NameTag &&
                HasPostScriptName(decoded.AsSpan(tables[index].Offset, tables[index].Length), postScriptName)))
                ?? throw InvalidWoff2();
        }
        flavor = selected.Flavor;
        var entries = selected.Tables.Select(i => tables[i]).ToDictionary(t => t.Tag);
        var result = new Dictionary<uint, byte[]>();
        foreach (var entry in entries.Values)
            if (!entry.Transformed) result[entry.Tag] = decoded.AsSpan(entry.Offset, entry.Length).ToArray();

        if (entries.TryGetValue(GlyfTag, out var glyphs) && glyphs.Transformed)
        {
            if (!result.TryGetValue(HeadTag, out var head) || head.Length < 54 ||
                !result.TryGetValue(MaxpTag, out var maxp) || maxp.Length < 6) throw InvalidWoff2();
            var reconstructed = DecodeWoff2Glyphs(decoded.AsSpan(glyphs.Offset, glyphs.Length), U16(maxp, 4), U16(head, 50));
            if (entries[LocaTag].OriginalLength != reconstructed.Locations.Length) throw InvalidWoff2();
            result[GlyfTag] = reconstructed.Glyphs; result[LocaTag] = reconstructed.Locations;
        }
        if (entries.TryGetValue(HmtxTag, out var hmtx) && hmtx.Transformed)
        {
            if (!result.TryGetValue(HheaTag, out var hhea) || hhea.Length < 36 ||
                !result.TryGetValue(MaxpTag, out var maxp) || maxp.Length < 6 ||
                !result.TryGetValue(HeadTag, out var head) || head.Length < 54 ||
                !result.TryGetValue(GlyfTag, out var glyf) || !result.TryGetValue(LocaTag, out var loca)) throw InvalidWoff2();
            result[HmtxTag] = DecodeWoff2Metrics(decoded.AsSpan(hmtx.Offset, hmtx.Length),
                U16(maxp, 4), U16(hhea, 34), U16(head, 50), glyf, loca);
        }
        long size = 12 + result.Count * 16;
        foreach (var bytes in result.Values) size += (bytes.Length + 3L) & ~3L;
        if (size > MaximumBytes) throw InvalidWoff2();
        return result.Select(pair => new Table(pair.Key, pair.Value)).ToList();
    }

    private static void ValidateWoff2Face(Woff2Face face, Woff2Table[] tables, bool collection)
    {
        if (face.Flavor is not (0x00010000 or 0x4f54544f or 0x74727565)) throw InvalidWoff2();
        var names = new Dictionary<uint, int>();
        foreach (var index in face.Tables)
            if (!names.TryAdd(tables[index].Tag, index)) throw InvalidWoff2();
        var hasGlyf = names.TryGetValue(GlyfTag, out var glyf);
        var hasLoca = names.TryGetValue(LocaTag, out var loca);
        if (hasGlyf != hasLoca || hasGlyf && (tables[glyf].Transformed != tables[loca].Transformed ||
            loca <= glyf || collection && loca != glyf + 1)) throw InvalidWoff2();
    }

    private static void ValidateWoff2Blocks(byte[] data, int end)
    {
        var metadata = U32(data, 28); var metadataLength = U32(data, 32);
        var privateOffset = U32(data, 40); var privateLength = U32(data, 44);
        void Block(uint offset, uint length)
        {
            if (offset == 0) { if (length != 0) throw InvalidWoff2(); return; }
            var aligned = (end + 3L) & ~3L;
            if (offset != aligned || (ulong)offset + length > (ulong)data.Length || length == 0) throw InvalidWoff2();
            for (var i = end; i < offset; i++) if (data[i] != 0) throw InvalidWoff2();
            end = checked((int)(offset + length));
        }
        Block(metadata, metadataLength); Block(privateOffset, privateLength);
        if (data.Length - end > 3) throw InvalidWoff2();
        for (var i = end; i < data.Length; i++) if (data[i] != 0) throw InvalidWoff2();
    }

    private static uint FontTag(string tag) => (uint)tag[0] << 24 | (uint)tag[1] << 16 | (uint)tag[2] << 8 | tag[3];
    private static InvalidDataException InvalidWoff2() => new("Invalid WOFF 2 font data.");

    private ref struct Woff2Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        internal int Position;
        internal bool AtEnd => Position == _data.Length;
        internal byte Byte() { if (Position >= _data.Length) throw InvalidWoff2(); return _data[Position++]; }
        internal ushort UInt16() => BinaryPrimitives.ReadUInt16BigEndian(Read(2));
        internal short Int16() => unchecked((short)UInt16());
        internal uint UInt32() => BinaryPrimitives.ReadUInt32BigEndian(Read(4));
        internal ReadOnlySpan<byte> Read(int length)
        {
            if (length < 0 || length > _data.Length - Position) throw InvalidWoff2();
            var result = _data.Slice(Position, length); Position += length; return result;
        }
        internal uint Base128()
        {
            uint result = 0;
            for (var i = 0; i < 5; i++)
            {
                var value = Byte();
                if (i == 0 && value == 0x80 || (result & 0xfe000000) != 0) throw InvalidWoff2();
                result = result << 7 | (uint)(value & 127);
                if ((value & 128) == 0) return result;
            }
            throw InvalidWoff2();
        }
        internal int UInt255()
        {
            var code = Byte();
            return code switch { 253 => UInt16(), 254 => Byte() + 506, 255 => Byte() + 253, _ => code };
        }
    }

    private sealed class Woff2Writer : IDisposable
    {
        private readonly MemoryStream _stream = new();
        internal int Length => checked((int)_stream.Length);
        private void Reserve(int length) { if (_stream.Length + length > MaximumBytes) throw InvalidWoff2(); }
        internal void Byte(int value) { Reserve(1); _stream.WriteByte((byte)value); }
        internal void UInt16(int value) { Reserve(2); _stream.WriteByte((byte)(value >> 8)); _stream.WriteByte((byte)value); }
        internal void UInt32(uint value) { Reserve(4); UInt16((int)(value >> 16)); UInt16((int)value); }
        internal void Write(ReadOnlySpan<byte> bytes) { Reserve(bytes.Length); _stream.Write(bytes); }
        internal void Align2() { if ((Length & 1) != 0) Byte(0); }
        internal byte[] Bytes() => _stream.ToArray();
        public void Dispose() => _stream.Dispose();
    }
}
