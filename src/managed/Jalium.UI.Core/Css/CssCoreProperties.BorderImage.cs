namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    private static readonly string[] BorderImageNames = ["border-image-source", "border-image-slice", "border-image-width", "border-image-outset", "border-image-repeat"];

    private static void RegisterBorderImage()
    {
        var properties = new[] { CssBorderImageProperties.SourceProperty, CssBorderImageProperties.SliceProperty,
            CssBorderImageProperties.WidthProperty, CssBorderImageProperties.OutsetProperty, CssBorderImageProperties.RepeatProperty };
        for (var index = 0; index < BorderImageNames.Length; index++)
        {
            var part = index;
            CssPropertyRegistry.Register(new CssPropertyDescriptor
            {
                Name = BorderImageNames[part], Kind = CssPropertyKind.Longhand, StorageProperty = properties[part],
                Parse = (ref CssTokenReader reader, CssCompileContext context) =>
                {
                    var value = ReadBorderImagePart(ref reader, context, part);
                    return reader.AtEnd ? value : null;
                },
            });
        }
        RegisterShorthand("border-image", static (ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output) =>
        {
            CssCompiledValue?[] values = new CssCompiledValue?[5];
            while (!reader.AtEnd)
            {
                var read = false;
                foreach (var part in new[] { 0, 1, 4 })
                {
                    if (values[part] is not null) continue;
                    var probe = reader;
                    var value = ReadBorderImagePart(ref probe, context, part);
                    if (value is null) continue;
                    values[part] = value; reader = probe; read = true;
                    if (part == 1 && reader.TryReadSlash())
                    {
                        var width = reader;
                        if (ReadBorderImagePart(ref width, context, 2) is { } parsed) { values[2] = parsed; reader = width; }
                        else { var slash = reader; if (!slash.TryReadSlash()) return false; }
                        if (reader.TryReadSlash())
                        {
                            values[3] = ReadBorderImagePart(ref reader, context, 3);
                            if (values[3] is null) return false;
                        }
                    }
                    break;
                }
                if (!read) return false;
            }
            if (values.All(value => value is null)) return false;
            for (var index = 0; index < values.Length; index++)
                output.Add(new(BorderImageNames[index], values[index] ?? BorderImageInitial(index), false));
            return true;
        });
    }

    private static CssCompiledValue BorderImageInitial(int part) => part switch
    {
        0 => new BorderImageSourceValue(null),
        1 => new CssImmediateValue(CssBorderImageProperties.SliceProperty, CssBorderImageMeasures.Uniform(100, CssUnit.Percent)),
        2 => new CssImmediateValue(CssBorderImageProperties.WidthProperty, CssBorderImageMeasures.Uniform(1, CssUnit.None, true)),
        3 => new CssImmediateValue(CssBorderImageProperties.OutsetProperty, CssBorderImageMeasures.Uniform(0, CssUnit.None, true)),
        _ => new CssImmediateValue(CssBorderImageProperties.RepeatProperty, new CssBorderImageTiling()),
    };

    private static CssCompiledValue? ReadBorderImagePart(ref CssTokenReader reader, CssCompileContext context, int part,
        DependencyProperty? storage = null)
    {
        if (part == 0)
        {
            var none = reader;
            if (none.TryReadIdent(out var keyword) && keyword.Equals("none", StringComparison.OrdinalIgnoreCase)) { reader = none; return new BorderImageSourceValue(null); }
            return CssImageValue.TryRead(ref reader, context, out var image) ? new BorderImageSourceValue(image) : null;
        }
        if (part == 4)
        {
            var probe = reader;
            if (!ReadBorderRepeat(ref probe, out var x)) return null;
            reader = probe;
            if (ReadBorderRepeat(ref probe, out var y)) reader = probe; else y = x;
            return new CssImmediateValue(storage ?? CssBorderImageProperties.RepeatProperty, new CssBorderImageTiling(x, y));
        }
        var values = new List<CssBorderImageMeasure>(); var fill = false;
        while (!reader.AtEnd)
        {
            var probe = reader;
            if (part == 1 && probe.TryReadIdent(out var keyword) && keyword.Equals("fill", StringComparison.OrdinalIgnoreCase))
            { if (fill) return null; fill = true; reader = probe; if (values.Count > 0) break; continue; }
            probe = reader;
            if (values.Count == 4 || !ReadBorderImageMeasure(ref probe, part, out var value)) break;
            reader = probe; values.Add(value);
        }
        if (values.Count == 0) return null;
        var expanded = new[] { values[0], values.Count > 1 ? values[1] : values[0], values.Count > 2 ? values[2] : values[0], values.Count > 3 ? values[3] : values.Count > 1 ? values[1] : values[0] };
        return new BorderImageMeasuresValue(part, expanded, fill, storage);
    }

    private static bool ReadBorderRepeat(ref CssTokenReader reader, out CssBorderImageRepeat repeat)
    {
        repeat = default; var probe = reader;
        if (!probe.TryReadIdent(out var keyword)) return false;
        var value = keyword.ToString().ToLowerInvariant() switch
        { "stretch" => CssBorderImageRepeat.Stretch, "repeat" => CssBorderImageRepeat.Repeat, "round" => CssBorderImageRepeat.Round, "space" => CssBorderImageRepeat.Space, _ => (CssBorderImageRepeat?)null };
        if (value is not { } valid) return false;
        repeat = valid; reader = probe; return true;
    }

    private static bool ReadBorderImageMeasure(ref CssTokenReader reader, int part, out CssBorderImageMeasure value)
    {
        value = default; var probe = reader;
        if (part == 2 && probe.TryReadIdent(out var keyword) && keyword.Equals("auto", StringComparison.OrdinalIgnoreCase))
        { value = new(default, Auto: true); reader = probe; return true; }
        probe = reader;
        if (probe.TryReadFunction(out var name, out var arguments))
        {
            var expression = CssMathExpression.Parse(name.ToString(), arguments.Remaining);
            if (expression is null || part == 1 && expression.Kind is not (CssNumericKind.Number or CssNumericKind.Percent) ||
                part == 3 && expression.Kind is not (CssNumericKind.Number or CssNumericKind.Length) ||
                part == 2 && expression.Kind is not (CssNumericKind.Number or CssNumericKind.Length or CssNumericKind.Percent or CssNumericKind.LengthPercent) ||
                part == 3 && expression.UsesPercent) return false;
            value = new(new(expression), expression.Kind == CssNumericKind.Number); reader = probe; return true;
        }
        if (!reader.TryReadNumber(out var number, out var unit) || number < 0 || !double.IsFinite(number)) return false;
        var length = new CssLength(number, unit);
        if (part == 1 ? unit is not (CssUnit.None or CssUnit.Percent) : part == 3 ? unit != CssUnit.None && (!length.IsLengthUnit || unit == CssUnit.Percent) : unit != CssUnit.None && unit != CssUnit.Percent && !length.IsLengthUnit) return false;
        value = new(length, unit == CssUnit.None); return true;
    }

    private sealed class BorderImageMeasuresValue(int part, CssBorderImageMeasure[] values, bool fill, DependencyProperty? storage = null) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            foreach (var value in values) value.Length.ObserveContainerDependencies(context.Lengths);
            sink.Set(storage ?? (part == 1 ? CssBorderImageProperties.SliceProperty : part == 2 ? CssBorderImageProperties.WidthProperty : CssBorderImageProperties.OutsetProperty),
                new CssBorderImageMeasures(values, context.Lengths, fill)); return true;
        }
    }

    private sealed class BorderImageSourceValue(CssImageValue? image) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            var resources = new List<CssBackgroundImageResource>();
            var source = image?.Select(context) is { } selected ? selected.Image.Border(context, selected.Resolution, resources) : null;
            sink.Set(CssBorderImageProperties.SourceProperty, source);
            sink.Set(CssBorderImageProperties.ResourcesProperty, resources.ToArray()); return true;
        }
    }
}
