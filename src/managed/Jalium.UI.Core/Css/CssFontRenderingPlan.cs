using System.Text;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal sealed record CssRenderingFace(string? Family, int Weight, CssUnicodeRange[] Ranges, bool Waiting, bool Blocked, double Width = 100)
{
    internal bool Contains(int scalar) => Ranges.Any(range => scalar >= range.Start && scalar <= range.End);
    internal bool Unrestricted => Ranges.Length == 1 && Ranges[0] == new CssUnicodeRange(0, 0x10ffff);
}

// This is an internal rendering key, never the public FontFamily.Source. A request
// id lets measurement/drawing resolve placeholders and captions using their actual
// text rather than guessing from an owner's Text property.
internal sealed record CssFontRenderingPlan(long RequestId, int MetricsFace, CssRenderingFace[] Faces)
{
    private const string Prefix = "\u0002css-font-plan:";
    internal string Encode()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write((byte)2); writer.Write(RequestId); writer.Write(MetricsFace); writer.Write(Faces.Length);
            foreach (var face in Faces)
            {
                writer.Write(face.Family ?? ""); writer.Write(face.Weight);
                writer.Write(face.Width);
                writer.Write(face.Waiting); writer.Write(face.Blocked); writer.Write(face.Ranges.Length);
                foreach (var range in face.Ranges) { writer.Write(range.Start); writer.Write(range.End); }
            }
        }
        return Prefix + Convert.ToBase64String(stream.ToArray());
    }

    internal static bool TryDecode(string source, out CssFontRenderingPlan plan)
    {
        plan = null!; source = CssFontFaces.Unblock(source);
        if (FontWidthRenderingSource.TryUnwrap(source, out _, out var familySource)) source = familySource;
        if (!source.StartsWith(Prefix, StringComparison.Ordinal) || source.Length > 4 * 1024 * 1024) return false;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(source[Prefix.Length..]));
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            byte version = reader.ReadByte(); if (version is not (1 or 2)) return false;
            long request = reader.ReadInt64(); int metrics = reader.ReadInt32(), count = reader.ReadInt32();
            if (request < 0 || count is < 0 or > 4096 || metrics < -1 || metrics >= count) return false;
            var faces = new CssRenderingFace[count]; int totalRanges = 0;
            for (int i = 0; i < count; i++)
            {
                string family = reader.ReadString(); int weight = reader.ReadInt32();
                double width = version == 2 ? reader.ReadDouble() : 100;
                bool waiting = reader.ReadBoolean(), blocked = reader.ReadBoolean(); int rangesCount = reader.ReadInt32();
                if (family.Length > 65536 || weight is < 1 or > 1000 || !double.IsFinite(width) || width < 0 || rangesCount < 0 ||
                    rangesCount > 65536 - totalRanges || waiting != (family.Length == 0) || blocked && !waiting) return false;
                totalRanges += rangesCount; var ranges = new CssUnicodeRange[rangesCount];
                for (int j = 0; j < ranges.Length; j++)
                {
                    int start = reader.ReadInt32(), end = reader.ReadInt32();
                    if (start < 0 || start > end || end > 0x10ffff) return false;
                    ranges[j] = new(start, end);
                }
                faces[i] = new(family.Length == 0 ? null : family, weight, ranges, waiting, blocked, width);
            }
            if (stream.Position != stream.Length || metrics >= 0 && faces[metrics].Waiting) return false;
            plan = new(request, metrics, faces); return true;
        }
        catch (Exception error) when (error is FormatException or IOException or ArgumentException) { return false; }
    }
}
