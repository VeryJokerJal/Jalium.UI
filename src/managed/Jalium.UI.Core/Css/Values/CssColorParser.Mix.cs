using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssColorParser
{
    private readonly record struct MixItem(CssColorData Color, double? Percentage);

    private static bool TryParseColorMix(ref CssTokenReader args, int depth,
        out Color color, out CssColorData data)
    {
        color = default;
        data = default;
        var segments = new List<string>();
        // Split after replacing comments: a comma inside /* ... */ is not an
        // argument separator, and comments may separate a color from its weight.
        var parts = new CssTokenReader(CssParser.StripComments(args.Remaining));
        while (parts.TryReadUntilTopLevelComma(out var segment))
        {
            if (segment.IsEmpty || segments.Count >= 32768) return false;
            segments.Add(segment.ToString());
            if (parts.AtEnd) break;
            if (!parts.TryReadComma() || parts.AtEnd) return false;
        }
        if (segments.Count == 0) return false;

        var method = new CssInterpolationMethod(CssInterpolationSpace.Oklab);
        var first = new CssTokenReader(segments[0]);
        var probe = first;
        var start = 0;
        if (probe.TryReadIdent(out var keyword) && keyword.Equals("in", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadInterpolationMethod(ref first, out method) || !first.AtEnd) return false;
            start = 1;
        }
        if (start == segments.Count) return false;

        var items = new List<MixItem>(segments.Count - start);
        var specifiedSum = 0.0;
        var omitted = 0;
        for (var i = start; i < segments.Count; i++)
        {
            if (!TryParseMixItem(segments[i], depth, out var item)) return false;
            items.Add(item);
            if (item.Percentage is { } percentage) specifiedSum += percentage;
            else omitted++;
        }

        // CSS Values 5 distributes any unused percentage to omitted entries.
        // With no omitted entries, an under-100% total multiplies the result's
        // alpha after the interpolation weights have been normalized.
        var unspecified = omitted == 0 ? 0 : Math.Max(0, 100 - Math.Min(100, specifiedSum)) / omitted;
        var total = specifiedSum + omitted * unspecified;
        if (total <= 0)
        {
            color = Color.FromArgb(0, 0, 0, 0);
            data = new(color, InterpolationSpaceName(method.Space), 0, 0, 0, 0, 0, false);
            return true;
        }

        var combined = InterpolateData(items[0].Color, items[0].Color, method, 0);
        var combinedWeight = items[0].Percentage ?? unspecified;
        for (var i = 1; i < items.Count; i++)
        {
            var weight = items[i].Percentage ?? unspecified;
            var sum = combinedWeight + weight;
            combined = InterpolateData(combined, items[i].Color, method,
                sum > 0 ? weight / sum : .5);
            combinedWeight = sum;
        }

        var alphaMultiplier = omitted == 0 && total < 100 ? total / 100 : 1;
        var alpha = Math.Clamp(combined.Alpha * alphaMultiplier, 0, 1);
        color = Color.FromScRgb((float)alpha,
            combined.Rendered.ScR, combined.Rendered.ScG, combined.Rendered.ScB);
        data = combined with
        {
            Rendered = color,
            Alpha = alpha,
            Missing = alphaMultiplier == 1 ? combined.Missing : (byte)(combined.Missing & ~8),
        };
        return true;
    }

    private static bool TryParseMixItem(string segment, int depth, out MixItem item)
    {
        item = default;
        var reader = new CssTokenReader(segment);
        double? percentage = null;
        var probe = reader;
        if (TryReadMixPercentage(ref probe, out var before))
        {
            percentage = before;
            reader = probe;
        }
        if (!TryParseWithData(ref reader, out _, out var isCurrentColor, out var data, depth) ||
            isCurrentColor) return false;
        if (percentage is null)
        {
            probe = reader;
            if (TryReadMixPercentage(ref probe, out var after))
            {
                percentage = after;
                reader = probe;
            }
        }
        if (!reader.AtEnd) return false;
        item = new MixItem(data, percentage);
        return true;
    }

    private static bool TryReadMixPercentage(ref CssTokenReader reader, out double percentage)
    {
        percentage = 0;
        var probe = reader;
        if (!probe.TryReadNumber(out var value, out var unit) || unit != CssUnit.Percent ||
            !double.IsFinite(value) || value is < 0 or > 100) return false;
        percentage = value;
        reader = probe;
        return true;
    }

    internal static bool TryReadInterpolationMethod(ref CssTokenReader reader,
        out CssInterpolationMethod method)
    {
        method = default;
        var probe = reader;
        if (!probe.TryReadIdent(out var keyword) || !keyword.Equals("in", StringComparison.OrdinalIgnoreCase) ||
            !probe.TryReadIdent(out var name)) return false;
        var space = name.ToString().ToLowerInvariant() switch
        {
            "srgb" => CssInterpolationSpace.Srgb,
            "srgb-linear" => CssInterpolationSpace.SrgbLinear,
            "display-p3" => CssInterpolationSpace.DisplayP3,
            "display-p3-linear" => CssInterpolationSpace.DisplayP3Linear,
            "a98-rgb" => CssInterpolationSpace.A98Rgb,
            "prophoto-rgb" => CssInterpolationSpace.ProPhotoRgb,
            "rec2020" => CssInterpolationSpace.Rec2020,
            "xyz" or "xyz-d65" => CssInterpolationSpace.XyzD65,
            "xyz-d50" => CssInterpolationSpace.XyzD50,
            "lab" => CssInterpolationSpace.Lab,
            "lch" => CssInterpolationSpace.Lch,
            "oklab" => CssInterpolationSpace.Oklab,
            "oklch" => CssInterpolationSpace.Oklch,
            "hsl" => CssInterpolationSpace.Hsl,
            "hwb" => CssInterpolationSpace.Hwb,
            _ => (CssInterpolationSpace)(-1),
        };
        if ((int)space < 0) return false;
        var polar = space is CssInterpolationSpace.Hsl or CssInterpolationSpace.Hwb or
            CssInterpolationSpace.Lch or CssInterpolationSpace.Oklch;
        var hue = CssHueMethod.Shorter;
        var hueProbe = probe;
        if (hueProbe.TryReadIdent(out var modifier))
        {
            var requested = modifier.ToString().ToLowerInvariant() switch
            {
                "shorter" => CssHueMethod.Shorter,
                "longer" => CssHueMethod.Longer,
                "increasing" => CssHueMethod.Increasing,
                "decreasing" => CssHueMethod.Decreasing,
                _ => (CssHueMethod)(-1),
            };
            if ((int)requested >= 0)
            {
                if (!polar || !hueProbe.TryReadIdent(out var hueKeyword) ||
                    !hueKeyword.Equals("hue", StringComparison.OrdinalIgnoreCase)) return false;
                hue = requested;
                probe = hueProbe;
            }
        }
        method = new(space, hue);
        reader = probe;
        return true;
    }
}
