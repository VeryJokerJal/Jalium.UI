using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    internal static readonly string[] MaskNames = ["mask-image", "mask-mode", "mask-repeat", "mask-position",
        "mask-origin", "mask-clip", "mask-size", "mask-composite"];
    private static readonly DependencyProperty[] MaskProperties = [CssMaskProperties.ImagesProperty,
        CssMaskProperties.ModesProperty, CssMaskProperties.RepeatsProperty, CssMaskProperties.PositionsProperty,
        CssMaskProperties.OriginsProperty, CssMaskProperties.ClipsProperty, CssMaskProperties.SizesProperty, CssMaskProperties.CompositesProperty];

    private static void RegisterMasking()
    {
        RegisterMaskBorder();
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "mask-type", Kind = CssPropertyKind.Longhand, StorageProperty = CssMaskProperties.TypeProperty,
            TransitionTargetDpName = CssMaskProperties.TypeProperty.Name,
            Parse = (ref CssTokenReader reader, CssCompileContext context) => ReadMaskMode(ref reader, out var mode) &&
                mode != CssMaskMode.MatchSource && reader.AtEnd ? new CssImmediateValue(CssMaskProperties.TypeProperty, mode) : null,
        });
        for (var i = 0; i < MaskNames.Length; i++)
        {
            var part = i;
            CssPropertyRegistry.Register(new CssPropertyDescriptor
            {
                Name = MaskNames[part], Kind = CssPropertyKind.Longhand, StorageProperty = MaskProperties[part],
                TransitionTargetDpName = MaskProperties[part].Name,
                Parse = (ref CssTokenReader reader, CssCompileContext context) => ParseMaskPart(ref reader, context, part),
            });
        }
        RegisterShorthand("mask", ExpandMask);
    }

    private delegate bool MaskReader<T>(ref CssTokenReader reader, CssCompileContext context, out T value);
    private static T[]? ReadMaskList<T>(ref CssTokenReader reader, CssCompileContext context, MaskReader<T> parse)
    {
        var values = new List<T>();
        do
        {
            if (!reader.TryReadUntilTopLevelComma(out var segment) || segment.IsEmpty) return null;
            var layer = new CssTokenReader(segment, reader.NumericContext);
            if (!parse(ref layer, context, out var value) || !layer.AtEnd) return null;
            values.Add(value);
            if (!reader.TryReadComma()) break;
            if (reader.AtEnd) return null;
        } while (true);
        return reader.AtEnd && values.Count > 0 ? values.ToArray() : null;
    }

    private static CssCompiledValue? ParseMaskPart(ref CssTokenReader reader, CssCompileContext context, int part)
    {
        switch (part)
        {
            case 0:
                var images = ReadMaskList(ref reader, context, static (ref CssTokenReader r, CssCompileContext c, out CssImageValue? v) => TryReadBackgroundImage(ref r, c, out v));
                return images is null ? null : new MaskImagesValue(images);
            case 1:
                var modes = ReadMaskList(ref reader, context, static (ref CssTokenReader r, CssCompileContext c, out CssMaskMode v) => ReadMaskMode(ref r, out v));
                return modes is null ? null : MaskListValue(part, modes);
            case 2:
                var repeats = ReadMaskList(ref reader, context, static (ref CssTokenReader r, CssCompileContext c, out CssBackgroundRepeat v) => TryReadBackgroundRepeat(ref r, out v));
                return repeats is null ? null : MaskListValue(part, repeats);
            case 3:
                var positions = ReadMaskList(ref reader, context, static (ref CssTokenReader r, CssCompileContext c, out CssBackgroundPosition v) => TryReadBackgroundPosition(ref r, out v));
                return positions is null ? null : new MaskPositionsValue(positions);
            case 4:
            case 5:
                var clip = part == 5;
                var boxes = ReadMaskList(ref reader, context, (ref CssTokenReader r, CssCompileContext c, out CssMaskBox v) => ReadMaskBox(ref r, clip, out v));
                return boxes is null ? null : MaskListValue(part, boxes);
            case 6:
                var sizes = ReadMaskList(ref reader, context, static (ref CssTokenReader r, CssCompileContext c, out CssBackgroundSize v) => TryReadBackgroundSize(ref r, out v));
                return sizes is null ? null : new MaskSizesValue(sizes);
            default:
                var composites = ReadMaskList(ref reader, context, static (ref CssTokenReader r, CssCompileContext c, out CssMaskComposite v) => ReadMaskComposite(ref r, out v));
                return composites is null ? null : MaskListValue(part, composites);
        }
    }

    private static CssCompiledValue MaskListValue<T>(int part, T[] values) => new CssImmediateValue(MaskProperties[part], new CssMaskList<T>(values));

    private static bool ReadMaskMode(ref CssTokenReader reader, out CssMaskMode value) =>
        ReadMaskKeyword(ref reader, new Dictionary<string, CssMaskMode>(StringComparer.OrdinalIgnoreCase)
        { ["alpha"] = CssMaskMode.Alpha, ["luminance"] = CssMaskMode.Luminance, ["match-source"] = CssMaskMode.MatchSource }, out value);
    private static bool ReadMaskComposite(ref CssTokenReader reader, out CssMaskComposite value) =>
        ReadMaskKeyword(ref reader, new Dictionary<string, CssMaskComposite>(StringComparer.OrdinalIgnoreCase)
        { ["add"] = CssMaskComposite.Add, ["subtract"] = CssMaskComposite.Subtract, ["intersect"] = CssMaskComposite.Intersect, ["exclude"] = CssMaskComposite.Exclude }, out value);
    private static bool ReadMaskBox(ref CssTokenReader reader, bool clip, out CssMaskBox value)
    {
        var probe = reader;
        if (!ReadMaskKeyword(ref probe, new Dictionary<string, CssMaskBox>(StringComparer.OrdinalIgnoreCase)
        {
            ["border-box"] = CssMaskBox.Border, ["padding-box"] = CssMaskBox.Padding, ["content-box"] = CssMaskBox.Content,
            ["fill-box"] = CssMaskBox.Fill, ["stroke-box"] = CssMaskBox.Stroke, ["view-box"] = CssMaskBox.View,
            ["no-clip"] = CssMaskBox.NoClip,
        }, out value) || !clip && value == CssMaskBox.NoClip) return false;
        reader = probe; return true;
    }
    private static bool ReadMaskKeyword<T>(ref CssTokenReader reader, Dictionary<string, T> keywords, out T value)
    {
        value = default!; var probe = reader;
        if (!probe.TryReadIdent(out var name) || !keywords.TryGetValue(name.ToString(), out value!)) return false;
        reader = probe; return true;
    }

    private static bool ExpandMask(ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output)
    {
        var images = new List<CssImageValue?>(); var modes = new List<CssMaskMode>();
        var repeats = new List<CssBackgroundRepeat>(); var positions = new List<CssBackgroundPosition>();
        var origins = new List<CssMaskBox>(); var clips = new List<CssMaskBox>();
        var sizes = new List<CssBackgroundSize>(); var composites = new List<CssMaskComposite>();
        do
        {
            if (!reader.TryReadUntilTopLevelComma(out var segment) || segment.IsEmpty) return false;
            var layer = new CssTokenReader(segment, reader.NumericContext);
            CssImageValue? image = null; var mode = CssMaskMode.MatchSource; var repeat = CssBackgroundRepeat.Both;
            var position = CssBackgroundPosition.Initial; var origin = CssMaskBox.Border; var clip = CssMaskBox.Border;
            var size = new CssBackgroundSize(CssBackgroundSizeMode.Auto); var composite = CssMaskComposite.Add;
            var hasImage = false; var hasMode = false; var hasRepeat = false; var hasPosition = false; var hasComposite = false;
            var boxCount = 0; var noClip = false; var saw = false;
            while (!layer.AtEnd)
            {
                var probe = layer;
                if (!hasImage && TryReadBackgroundImage(ref probe, context, out var parsedImage))
                { image = parsedImage; hasImage = true; layer = probe; saw = true; continue; }
                probe = layer;
                if (!hasMode && ReadMaskMode(ref probe, out var parsedMode))
                { mode = parsedMode; hasMode = true; layer = probe; saw = true; continue; }
                probe = layer;
                if (!hasComposite && ReadMaskComposite(ref probe, out var parsedComposite))
                { composite = parsedComposite; hasComposite = true; layer = probe; saw = true; continue; }
                probe = layer;
                if (!hasRepeat && TryReadBackgroundRepeat(ref probe, out var parsedRepeat))
                { repeat = parsedRepeat; hasRepeat = true; layer = probe; saw = true; continue; }
                probe = layer;
                if (!hasPosition && TryReadBackgroundPosition(ref probe, out var parsedPosition))
                {
                    position = parsedPosition; hasPosition = true; layer = probe; saw = true;
                    if (layer.TryReadSlash() && !TryReadBackgroundSize(ref layer, out size)) return false;
                    continue;
                }
                probe = layer;
                if (ReadMaskBox(ref probe, true, out var box))
                {
                    if (box == CssMaskBox.NoClip)
                    { if (noClip || boxCount > 1) return false; noClip = true; clip = box; }
                    else if (++boxCount == 1) { origin = box; if (!noClip) clip = box; }
                    else if (boxCount == 2 && !noClip) clip = box;
                    else return false;
                    layer = probe; saw = true; continue;
                }
                return false;
            }
            if (!saw) return false;
            images.Add(image); modes.Add(mode); repeats.Add(repeat); positions.Add(position);
            origins.Add(origin); clips.Add(clip); sizes.Add(size); composites.Add(composite);
            if (!reader.TryReadComma()) break;
            if (reader.AtEnd) return false;
        } while (true);
        CssCompiledValue[] values = [new MaskImagesValue(images.ToArray()), MaskListValue(1, modes.ToArray()),
            MaskListValue(2, repeats.ToArray()), new MaskPositionsValue(positions.ToArray()), MaskListValue(4, origins.ToArray()),
            MaskListValue(5, clips.ToArray()), new MaskSizesValue(sizes.ToArray()), MaskListValue(7, composites.ToArray())];
        for (var i = 0; i < values.Length; i++) output.Add(new(MaskNames[i], values[i], false));
        for (var i = 0; i < MaskBorderNames.Length; i++) output.Add(new(MaskBorderNames[i], MaskBorderInitial(i), false));
        return reader.AtEnd;
    }

    internal static CssImageValue?[]? MaskImageDefinitions(CssCompiledValue value) => (value as MaskImagesValue)?.Definitions;

    private sealed class MaskImagesValue(CssImageValue?[] images) : CssCompiledValue
    {
        internal CssImageValue?[] Definitions => images;
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        { sink.Set(CssMaskProperties.ImagesProperty, CssMaskImages.Materialize(images, context)); return true; }
    }
    private sealed class MaskSizesValue(CssBackgroundSize[] values) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            var resolved = new CssBackgroundSize[values.Length];
            for (var i = 0; i < values.Length; i++) { values[i].ObserveDependencies(context.Lengths); resolved[i] = values[i].ForElement(context.Lengths); }
            sink.Set(CssMaskProperties.SizesProperty, new CssMaskList<CssBackgroundSize>(resolved)); return true;
        }
    }
    private sealed class MaskPositionsValue(CssBackgroundPosition[] values) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            var resolved = new CssBackgroundPosition[values.Length];
            for (var i = 0; i < values.Length; i++) { values[i].ObserveDependencies(context.Lengths); resolved[i] = values[i].ForElement(context.Lengths); }
            sink.Set(CssMaskProperties.PositionsProperty, new CssMaskList<CssBackgroundPosition>(resolved)); return true;
        }
    }
}
