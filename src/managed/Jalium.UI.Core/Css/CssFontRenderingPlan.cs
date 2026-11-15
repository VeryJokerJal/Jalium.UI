using System.Text;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal sealed record CssRenderingFace(string? Family, int Weight, CssUnicodeRange[] Ranges, bool Waiting, bool Blocked, double Width = 100)
{
    internal CssFontFaceMetrics Metrics { get; init; } = CssFontFaceMetrics.Initial;
    internal FontFeature[] Features { get; init; } = [];
    internal FontVariation[] Variations { get; init; } = [];
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
            writer.Write((byte)5); writer.Write(RequestId); writer.Write(MetricsFace); writer.Write(Faces.Length);
            foreach (var face in Faces)
            {
                writer.Write(face.Family ?? ""); writer.Write(face.Weight);
                writer.Write(face.Width);
                writer.Write(face.Features.Length);
                foreach (var feature in face.Features) { writer.Write(feature.Tag); writer.Write(feature.Value); }
                writer.Write(face.Variations.Length);
                foreach (var axis in face.Variations) { writer.Write(axis.Tag); writer.Write(axis.Value); }
                foreach (double value in face.Metrics.Values) writer.Write(value);
                writer.Write(face.Waiting); writer.Write(face.Blocked); writer.Write(face.Ranges.Length);
                foreach (var range in face.Ranges) { writer.Write(range.Start); writer.Write(range.End); }
            }
        }
        return Prefix + Convert.ToBase64String(stream.ToArray());
    }

    internal static bool TryDecode(string source, out CssFontRenderingPlan plan)
    {
        plan = null!; source = CssFontFaces.Unblock(source);
        if (FontFeatureRenderingSource.TryUnwrap(source, out _, out var featureFamily)) source = featureFamily;
        if (FontWidthRenderingSource.TryUnwrap(source, out _, out var familySource)) source = familySource;
        if (!source.StartsWith(Prefix, StringComparison.Ordinal) || source.Length > 4 * 1024 * 1024) return false;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(source[Prefix.Length..]));
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            byte version = reader.ReadByte(); if (version is not (1 or 2 or 3 or 4 or 5)) return false;
            long request = reader.ReadInt64(); int metrics = reader.ReadInt32(), count = reader.ReadInt32();
            if (request < 0 || count is < 0 or > 4096 || metrics < -1 || metrics >= count) return false;
            var faces = new CssRenderingFace[count]; int totalRanges = 0;
            for (int i = 0; i < count; i++)
            {
                string family = reader.ReadString(); int weight = reader.ReadInt32();
                double width = version >= 2 ? reader.ReadDouble() : 100;
                FontFeature[] features = [];
                if (version >= 3)
                {
                    int featureCount = reader.ReadInt32();
                    if (featureCount is < 0 or > FontFeature.MaximumCount) return false;
                    features = new FontFeature[featureCount];
                    for (int k = 0; k < featureCount; k++)
                    {
                        uint tag = reader.ReadUInt32(), value = reader.ReadUInt32();
                        for (int shift = 0; shift < 32; shift += 8) if (((tag >> shift) & 255) is < 32 or > 126) return false;
                        features[k] = new(tag, value);
                    }
                }
                FontVariation[] variations = [];
                if (version >= 4)
                {
                    int axisCount = reader.ReadInt32();
                    if (axisCount is < 0 or > CssFontVariationSettings.MaximumCount) return false;
                    variations = new FontVariation[axisCount];
                    for (int k = 0; k < axisCount; k++)
                    {
                        uint tag = reader.ReadUInt32(); double value = reader.ReadDouble();
                        for (int shift = 0; shift < 32; shift += 8) if (((tag >> shift) & 255) is < 32 or > 126) return false;
                        if (!double.IsFinite(value)) return false;
                        variations[k] = new(tag, value);
                    }
                }
                var metricsValues = CssFontFaceMetrics.Initial;
                if (version >= 5)
                {
                    var values = new double[7];
                    for (int k = 0; k < values.Length; ++k) values[k] = reader.ReadDouble();
                    if (CssFontFaceMetrics.FromValues(values) is not { } parsed) return false;
                    metricsValues = parsed;
                }
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
                faces[i] = new(family.Length == 0 ? null : family, weight, ranges, waiting, blocked, width) { Features = features, Variations = variations, Metrics = metricsValues };
            }
            if (stream.Position != stream.Length || metrics >= 0 && faces[metrics].Waiting) return false;
            plan = new(request, metrics, faces); return true;
        }
        catch (Exception error) when (error is FormatException or IOException or ArgumentException) { return false; }
    }
}
