using System.Globalization;
using System.Runtime.InteropServices;
using Jalium.UI.Styling;

namespace Jalium.UI.Media;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct FontFeature(uint Tag, uint Value)
{
    // A selected face combines descriptor, variant and property layers.
    internal const int MaximumCount = 16384;
    internal static uint TagOf(string tag) => (uint)tag[0] << 24 | (uint)tag[1] << 16 | (uint)tag[2] << 8 | tag[3];
}

// A private rendering identity keeps measurement, drawing, paragraphs and hits
// on the same feature set without changing the authored FontFamily.Source.
internal static class FontFeatureRenderingSource
{
    internal readonly record struct CapsContext(string? Language = null, bool DisableSynthesis = false, byte BoundaryFlags = 0,
        byte Continuation = 0, byte Position = 0, bool DisablePositionSynthesis = false, byte PositionRun = 0,
        byte Emoji = 0, byte EmojiAfter = 0, FontVariation[]? Variations = null, byte OpticalSizing = 0, CssFontSizeAdjust? SizeAdjust = null, CssFontFaceMetrics? FaceMetrics = null);
    private const string FaceMetricsPrefix = "\u0004font-face-metrics:";
    private const string SizeAdjustPrefix = "\u0004font-size-adjust:";
    private const string OpticalPrefix = "\u0004font-optical:";
    private const string VariationsPrefix = "\u0004font-variations:";
    private const string Prefix = "\u0004font-features:";
    private const string CapsPrefix = "\u0004font-features-caps:";
    private const string ContextPrefix = "\u0004font-features-caps-context:";
    private const string BoundaryPrefix = "\u0004font-features-caps-boundaries:";
    private const string ContinuationPrefix = "\u0004font-features-caps-continuation:";
    private const string PositionPrefix = "\u0004font-features-position:";
    private const string TextVariantsPrefix = "\u0004font-features-text-variants:";
    internal static string ForOwner(string source, DependencyObject owner)
        => Wrap(source, CssFontFeatureSettings.ForRendering(owner), (CssCapsVariant)owner.GetValue(CssFontCaps.VariantProperty)!,
            new((owner.GetValue(FrameworkElement.LanguageProperty) as Jalium.UI.Markup.XmlLanguage)?.IetfLanguageTag,
                !(bool)owner.GetValue(CssFontCaps.SynthesisProperty)!, Position:(byte)(CssPositionVariant)owner.GetValue(CssFontPosition.VariantProperty)!,
                DisablePositionSynthesis:!(bool)owner.GetValue(CssFontPosition.SynthesisProperty)!,
                Emoji:(byte)(CssEmojiVariant)owner.GetValue(CssFontEmoji.VariantProperty)!,
                Variations:((CssFontVariationSettings)owner.GetValue(CssFontVariationSettings.SettingsProperty)!).Axes,
                OpticalSizing:CssFontOpticalSizing.ForOwner(owner), SizeAdjust:(CssFontSizeAdjust)owner.GetValue(CssFontSizeAdjust.ValueProperty)!));

    internal static FontFeature[] Merge(FontFeature[] first, FontFeature[] last)
    {
        if (first.Length == 0) return last;
        if (last.Length == 0) return first;
        var values = new Dictionary<uint, uint>();
        foreach (var feature in first) values[feature.Tag] = feature.Value;
        foreach (var feature in last) values[feature.Tag] = feature.Value;
        return values.OrderBy(p => p.Key).Select(p => new FontFeature(p.Key, p.Value)).ToArray();
    }

    internal static string Wrap(string source, FontFeature[] features)
    {
        if (features.Length == 0) return source;
        return WrapCore(source, features, null);
    }

    internal static string Wrap(string source, FontFeature[] features, CssCapsVariant caps)
        => WrapCore(source, features, caps);

    internal static string Wrap(string source, FontFeature[] features, CssCapsVariant caps, CapsContext context)
        => WrapCore(source, features, caps, context);

    internal static string WithCapsBoundaries(string source, byte boundaries)
    {
        if (boundaries > 63) throw new ArgumentOutOfRangeException(nameof(boundaries));
        return TryUnwrap(source, out _, out _, out var caps, out var context) && caps != CssCapsVariant.Normal
            ? WrapCore(source, [], caps, context with { BoundaryFlags = boundaries }) : source;
    }

    internal static string WithCapsFragment(string source, byte boundaries, byte continuation, byte positionRun = 0, byte emojiAfter = 0)
    {
        if (boundaries > 63 || continuation > 2 || positionRun > 3 || emojiAfter > 2) throw new ArgumentOutOfRangeException(nameof(continuation));
        bool encoded = TryUnwrap(source, out _, out _, out var caps, out var context);
        return emojiAfter != 0 || encoded && (caps != CssCapsVariant.Normal || context.Position != 0 || context.Emoji != 0 || context.EmojiAfter != 0)
            ? WrapCore(source, [], caps, context with { BoundaryFlags = boundaries, Continuation = continuation,
                PositionRun = context.Position == 0 ? (byte)0 : positionRun, EmojiAfter = emojiAfter }) : source;
    }

    private static string WrapCore(string source, FontFeature[] features, CssCapsVariant? caps, CapsContext? context = null)
    {
        bool blocked = CssFontFaces.IsBlocked(source); source = CssFontFaces.Unblock(source);
        CssCapsVariant previousCaps = CssCapsVariant.Normal;
        if (TryUnwrap(source, out var previous, out var family, out previousCaps, out var previousContext))
        { source = family; features = Merge(previous, features); }
        var variant = caps ?? previousCaps;
        var options = context ?? previousContext;
        if (context is not null && options.FaceMetrics is null) options = options with { FaceMetrics = previousContext.FaceMetrics };
        if (options.BoundaryFlags > 63 || options.Continuation > 2 || options.Position > 2 || options.PositionRun > 3 || options.Emoji > 3 || options.EmojiAfter > 2)
            throw new ArgumentOutOfRangeException(nameof(context));
        if (options.FaceMetrics is { IsInitial: false } metrics)
            return (blocked ? CssFontFaces.BlockPrefix : "") + FaceMetricsPrefix + metrics.Encode() + ":" +
                WrapCore(source, features, variant, options with { FaceMetrics = null });
        if (options.SizeAdjust is { Metric: > 0 } adjustment)
        {
            if (adjustment.Metric > 5 || !double.IsFinite(adjustment.Aspect) || adjustment.Aspect < 0) throw new ArgumentOutOfRangeException(nameof(context));
            return (blocked ? CssFontFaces.BlockPrefix : "") + SizeAdjustPrefix + adjustment.Metric.ToString(CultureInfo.InvariantCulture) + "=" +
                adjustment.Aspect.ToString("R", CultureInfo.InvariantCulture) + ":" + WrapCore(source, features, variant, options with { SizeAdjust = null });
        }
        if (options.OpticalSizing != 0)
        {
            if (options.OpticalSizing > 2) throw new ArgumentOutOfRangeException(nameof(context));
            return (blocked ? CssFontFaces.BlockPrefix : "") + OpticalPrefix + options.OpticalSizing.ToString(CultureInfo.InvariantCulture) + ":" +
                WrapCore(source, features, variant, options with { OpticalSizing = 0 });
        }
        if (options.Variations is { Length: > 0 } axes)
        {
            if (axes.Length > CssFontVariationSettings.MaximumCount || axes.Any(axis => !double.IsFinite(axis.Value)))
                throw new ArgumentOutOfRangeException(nameof(context));
            string coordinates = string.Join(',', axes.Select(axis => axis.Tag.ToString("X8", CultureInfo.InvariantCulture) + "=" + axis.Value.ToString("R", CultureInfo.InvariantCulture)));
            string inner = WrapCore(source, features, variant, options with { Variations = null });
            return (blocked ? CssFontFaces.BlockPrefix : "") + VariationsPrefix + coordinates + ":" + inner;
        }
        string language = string.IsNullOrEmpty(options.Language) ? "en-us" : options.Language.ToLowerInvariant();
        bool continuation = options.Continuation != 0;
        bool boundary = continuation || options.BoundaryFlags != 0;
        bool contextual = boundary || options.DisableSynthesis || language != "en-us";
        if (features.Length == 0 && variant == CssCapsVariant.Normal && options.Position == 0 && options.Emoji == 0 && options.EmojiAfter == 0)
            return (blocked ? CssFontFaces.BlockPrefix : "") + source;
        if (options.Emoji != 0 || options.EmojiAfter != 0)
            return (blocked ? CssFontFaces.BlockPrefix : "") + TextVariantsPrefix + ((int)variant).ToString(CultureInfo.InvariantCulture) + ":" +
                (options.DisableSynthesis ? "0:" : "1:") + language + ":" + options.BoundaryFlags.ToString(CultureInfo.InvariantCulture) + ":" +
                options.Continuation.ToString(CultureInfo.InvariantCulture) + ":" + options.Position.ToString(CultureInfo.InvariantCulture) + ":" +
                (options.DisablePositionSynthesis ? "0:" : "1:") + options.PositionRun.ToString(CultureInfo.InvariantCulture) + ":" +
                options.Emoji.ToString(CultureInfo.InvariantCulture) + ":" + options.EmojiAfter.ToString(CultureInfo.InvariantCulture) + ":" +
                string.Join(',', features.Select(f => f.Tag.ToString("X8", CultureInfo.InvariantCulture) + "=" + f.Value.ToString(CultureInfo.InvariantCulture))) + ":" + source;
        if (options.Position != 0)
            return (blocked ? CssFontFaces.BlockPrefix : "") + PositionPrefix + ((int)variant).ToString(CultureInfo.InvariantCulture) + ":" +
                (options.DisableSynthesis ? "0:" : "1:") + language + ":" + options.BoundaryFlags.ToString(CultureInfo.InvariantCulture) + ":" +
                options.Continuation.ToString(CultureInfo.InvariantCulture) + ":" + options.Position.ToString(CultureInfo.InvariantCulture) + ":" +
                (options.DisablePositionSynthesis ? "0:" : "1:") + options.PositionRun.ToString(CultureInfo.InvariantCulture) + ":" +
                string.Join(',', features.Select(f => f.Tag.ToString("X8", CultureInfo.InvariantCulture) + "=" + f.Value.ToString(CultureInfo.InvariantCulture))) + ":" + source;
        return (blocked ? CssFontFaces.BlockPrefix : "") +
            (variant == CssCapsVariant.Normal ? Prefix :
                (continuation ? ContinuationPrefix : boundary ? BoundaryPrefix : contextual ? ContextPrefix : CapsPrefix) + ((int)variant).ToString(CultureInfo.InvariantCulture) + ":" +
                (contextual ? (options.DisableSynthesis ? "0:" : "1:") + language + ":" : "") +
                (boundary ? options.BoundaryFlags.ToString(CultureInfo.InvariantCulture) + ":" : "") +
                (continuation ? options.Continuation.ToString(CultureInfo.InvariantCulture) + ":" : "")) +
            string.Join(',', features.Select(f => f.Tag.ToString("X8", CultureInfo.InvariantCulture) + "=" + f.Value.ToString(CultureInfo.InvariantCulture))) + ":" + source;
    }

    internal static bool TryUnwrap(string source, out FontFeature[] features, out string family)
        => TryUnwrap(source, out features, out family, out _);

    internal static bool TryUnwrap(string source, out FontFeature[] features, out string family, out CssCapsVariant caps)
        => TryUnwrap(source, out features, out family, out caps, out _);

    internal static bool TryUnwrap(string source, out FontFeature[] features, out string family, out CssCapsVariant caps, out CapsContext context)
    {
        features = []; family = source; caps = CssCapsVariant.Normal; context = default;
        var text = CssFontFaces.Unblock(source);
        if (text.StartsWith(FaceMetricsPrefix, StringComparison.Ordinal))
        {
            int separator = text.IndexOf(':', FaceMetricsPrefix.Length);
            if (separator < FaceMetricsPrefix.Length || separator - FaceMetricsPrefix.Length > 1024 ||
                CssFontFaceMetrics.Decode(text.AsSpan(FaceMetricsPrefix.Length, separator - FaceMetricsPrefix.Length)) is not { } metrics) return false;
            string inner = text[(separator + 1)..];
            if (inner.StartsWith(FaceMetricsPrefix, StringComparison.Ordinal)) return false;
            if (!TryUnwrap(inner, out var f, out var familyValue, out var capsValue, out var innerContext))
            { f = []; familyValue = inner; capsValue = CssCapsVariant.Normal; innerContext = default; }
            features = f; family = familyValue; caps = capsValue; context = innerContext with { FaceMetrics = metrics }; return true;
        }
        if (text.StartsWith(SizeAdjustPrefix, StringComparison.Ordinal))
        {
            int at = SizeAdjustPrefix.Length, separator = text.IndexOf(':', at);
            if (separator < at + 3 || text[at] is < '1' or > '5' || text[at + 1] != '=' ||
                !double.TryParse(text.AsSpan(at + 2, separator - at - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out double aspect) || !double.IsFinite(aspect) || aspect < 0) return false;
            string inner = text[(separator + 1)..];
            if (inner.StartsWith(SizeAdjustPrefix, StringComparison.Ordinal) || inner.StartsWith(FaceMetricsPrefix, StringComparison.Ordinal)) return false;
            if (!TryUnwrap(inner, out var nestedFeatures, out var nestedFamily, out var nestedCaps, out var nestedContext))
            { nestedFeatures = []; nestedFamily = inner; nestedCaps = CssCapsVariant.Normal; nestedContext = default; }
            features = nestedFeatures; family = nestedFamily; caps = nestedCaps;
            context = nestedContext with { SizeAdjust = new CssFontSizeAdjust((byte)(text[at] - '0'), aspect) }; return true;
        }
        if (text.StartsWith(OpticalPrefix, StringComparison.Ordinal))
        {
            int at = OpticalPrefix.Length;
            if (at + 2 > text.Length || text[at] is not ('1' or '2') || text[at + 1] != ':') return false;
            string inner = text[(at + 2)..];
            if (inner.StartsWith(OpticalPrefix, StringComparison.Ordinal) || inner.StartsWith(SizeAdjustPrefix, StringComparison.Ordinal) || inner.StartsWith(FaceMetricsPrefix, StringComparison.Ordinal)) return false;
            if (!TryUnwrap(inner, out var nestedFeatures, out var nestedFamily, out var nestedCaps, out var nestedContext))
            { nestedFeatures = []; nestedFamily = inner; nestedCaps = CssCapsVariant.Normal; nestedContext = default; }
            features = nestedFeatures; family = nestedFamily; caps = nestedCaps;
            context = nestedContext with { OpticalSizing = (byte)(text[at] - '0') }; return true;
        }
        if (text.StartsWith(VariationsPrefix, StringComparison.Ordinal))
        {
            int separator = text.IndexOf(':', VariationsPrefix.Length);
            if (separator <= VariationsPrefix.Length || separator - VariationsPrefix.Length > 262144) return false;
            var axes = new List<FontVariation>();
            foreach (var item in text[VariationsPrefix.Length..separator].Split(','))
            {
                int at = item.IndexOf('=');
                if (at != 8 || axes.Count == CssFontVariationSettings.MaximumCount ||
                    !uint.TryParse(item.AsSpan(0, at), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var tag) ||
                    !double.TryParse(item.AsSpan(at + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) return false;
                for (int shift = 0; shift < 32; shift += 8) if (((tag >> shift) & 255) is < 32 or > 126) return false;
                axes.Add(new(tag, value));
            }
            string inner = text[(separator + 1)..];
            if (inner.StartsWith(VariationsPrefix, StringComparison.Ordinal) || inner.StartsWith(OpticalPrefix, StringComparison.Ordinal) || inner.StartsWith(SizeAdjustPrefix, StringComparison.Ordinal) || inner.StartsWith(FaceMetricsPrefix, StringComparison.Ordinal)) return false;
            if (!TryUnwrap(inner, out var parsedFeatures, out var parsedFamily, out var parsedCaps, out var parsedContext))
            { parsedFeatures = []; parsedFamily = inner; parsedCaps = CssCapsVariant.Normal; parsedContext = default; }
            features = parsedFeatures; family = parsedFamily; caps = parsedCaps;
            context = parsedContext with { Variations = axes.ToArray() }; return true;
        }
        int start = Prefix.Length;
        CssCapsVariant variant = CssCapsVariant.Normal;
        bool continuation = text.StartsWith(ContinuationPrefix, StringComparison.Ordinal);
        bool boundary = continuation || text.StartsWith(BoundaryPrefix, StringComparison.Ordinal);
        bool contextual = boundary || text.StartsWith(ContextPrefix, StringComparison.Ordinal);
        CapsContext options = default;
        if (text.StartsWith(TextVariantsPrefix, StringComparison.Ordinal))
        {
            var fields=text[TextVariantsPrefix.Length..].Split(':',12);
            if (fields.Length!=12 || !byte.TryParse(fields[0],NumberStyles.None,CultureInfo.InvariantCulture,out var mode) || mode>6 ||
                fields[1] is not ("0" or "1") || fields[2].Length is <1 or >128 || fields[2].Any(ch=>!char.IsAsciiLetterOrDigit(ch)&&ch!='-') ||
                !byte.TryParse(fields[3],NumberStyles.None,CultureInfo.InvariantCulture,out var flags) || flags>63 ||
                !byte.TryParse(fields[4],NumberStyles.None,CultureInfo.InvariantCulture,out var leading) || leading>2 ||
                !byte.TryParse(fields[5],NumberStyles.None,CultureInfo.InvariantCulture,out var position) || position>2 ||
                fields[6] is not ("0" or "1") || !byte.TryParse(fields[7],NumberStyles.None,CultureInfo.InvariantCulture,out var run) || run>3 ||
                position==0&&run!=0 || !byte.TryParse(fields[8],NumberStyles.None,CultureInfo.InvariantCulture,out var emoji) || emoji>3 ||
                !byte.TryParse(fields[9],NumberStyles.None,CultureInfo.InvariantCulture,out var after) || after>2) return false;
            if (!TryUnwrap(Prefix+fields[10]+":"+fields[11],out var parsedFeatures,out var parsedFamily))
            {
                if(fields[10].Length!=0)return false;
                parsedFeatures=[];parsedFamily=fields[11];
            }
            features=parsedFeatures;family=parsedFamily;caps=(CssCapsVariant)mode;
            context=new(fields[2],fields[1]=="0",flags,leading,position,fields[6]=="0",run,emoji,after);return true;
        }
        if (text.StartsWith(PositionPrefix, StringComparison.Ordinal))
        {
            var fields = text[PositionPrefix.Length..].Split(':', 10);
            if (fields.Length != 10 || !byte.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var mode) || mode > 6 ||
                fields[1] is not ("0" or "1") || fields[2].Length is < 1 or > 128 || fields[2].Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-') ||
                !byte.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var flags) || flags > 63 ||
                !byte.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var leading) || leading > 2 ||
                !byte.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var position) || position is < 1 or > 2 ||
                fields[6] is not ("0" or "1") || !byte.TryParse(fields[7], NumberStyles.None, CultureInfo.InvariantCulture, out var run) || run > 3) return false;
            // Reuse the existing feature/family decoder so malformed identities
            // still leave every output at its original default.
            if (!TryUnwrap(Prefix + fields[8] + ":" + fields[9], out var parsedFeatures, out var parsedFamily))
            {
                if (fields[8].Length != 0) return false;
                parsedFeatures = []; parsedFamily = fields[9];
            }
            features = parsedFeatures; family = parsedFamily; caps = (CssCapsVariant)mode;
            context = new(fields[2], fields[1] == "0", flags, leading, position, fields[6] == "0", run); return true;
        }
        if (contextual || text.StartsWith(CapsPrefix, StringComparison.Ordinal))
        {
            int prefixLength = continuation ? ContinuationPrefix.Length : boundary ? BoundaryPrefix.Length : contextual ? ContextPrefix.Length : CapsPrefix.Length;
            int separator = text.IndexOf(':', prefixLength);
            if (separator < 0 || !int.TryParse(text.AsSpan(prefixLength, separator - prefixLength),
                NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value is < 1 or > 6) return false;
            variant = (CssCapsVariant)value; start = separator + 1;
            if (contextual)
            {
                if (start + 2 > text.Length || text[start] is not ('0' or '1') || text[start + 1] != ':') return false;
                bool disabled = text[start] == '0'; start += 2;
                separator = text.IndexOf(':', start);
                if (separator <= start || separator - start > 128) return false;
                string language = text[start..separator];
                if (language.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-')) return false;
                options = new(language, disabled); start = separator + 1;
                if (boundary)
                {
                    separator = text.IndexOf(':', start);
                    if (separator <= start || !byte.TryParse(text.AsSpan(start, separator - start),
                        NumberStyles.None, CultureInfo.InvariantCulture, out var flags) || flags > 63) return false;
                    options = options with { BoundaryFlags = flags }; start = separator + 1;
                    if (continuation)
                    {
                        separator = text.IndexOf(':', start);
                        if (separator <= start || !byte.TryParse(text.AsSpan(start, separator - start),
                            NumberStyles.None, CultureInfo.InvariantCulture, out var operation) || operation > 2) return false;
                        options = options with { Continuation = operation }; start = separator + 1;
                    }
                }
            }
        }
        else if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        int end = text.IndexOf(':', start);
        if (end < 0 || end - start > 524288) return false;
        var result = new List<FontFeature>();
        foreach (var item in text[start..end].Split(','))
        {
            if (end == start && variant != CssCapsVariant.Normal) break;
            int at = item.IndexOf('=');
            if (at != 8 || result.Count == FontFeature.MaximumCount || !uint.TryParse(item.AsSpan(0, at), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var tag) ||
                !uint.TryParse(item.AsSpan(at + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return false;
            for (int shift = 0; shift < 32; shift += 8) if (((tag >> shift) & 255) is < 32 or > 126) return false;
            result.Add(new(tag, value));
        }
        features = result.ToArray(); family = text[(end + 1)..]; caps = variant; context = options; return true;
    }
}
