namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    internal static readonly string[] MaskBorderNames = ["mask-border-source", "mask-border-slice", "mask-border-width",
        "mask-border-outset", "mask-border-repeat", "mask-border-mode"];
    private static readonly DependencyProperty[] MaskBorderProperties = [CssMaskBorderProperties.SourceProperty,
        CssMaskBorderProperties.SliceProperty, CssMaskBorderProperties.WidthProperty, CssMaskBorderProperties.OutsetProperty,
        CssMaskBorderProperties.RepeatProperty, CssMaskBorderProperties.ModeProperty];

    private static void RegisterMaskBorder()
    {
        for (var i = 0; i < MaskBorderNames.Length; i++)
        {
            var part = i;
            CssPropertyRegistry.Register(new CssPropertyDescriptor
            {
                Name = MaskBorderNames[part], Kind = CssPropertyKind.Longhand, StorageProperty = MaskBorderProperties[part],
                TransitionTargetDpName = MaskBorderProperties[part].Name,
                Parse = (ref CssTokenReader reader, CssCompileContext context) =>
                {
                    var value = ReadMaskBorderPart(ref reader, context, part);
                    return reader.AtEnd ? value : null;
                },
            });
        }
        RegisterShorthand("mask-border", static (ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output) =>
        {
            var values = new CssCompiledValue?[6];
            while (!reader.AtEnd)
            {
                var read = false;
                foreach (var part in new[] { 0, 1, 4, 5 })
                {
                    if (values[part] is not null) continue;
                    var probe = reader;
                    var value = ReadMaskBorderPart(ref probe, context, part);
                    if (value is null) continue;
                    values[part] = value; reader = probe; read = true;
                    if (part == 1 && reader.TryReadSlash())
                    {
                        var width = reader;
                        if (ReadMaskBorderPart(ref width, context, 2) is { } parsed) { values[2] = parsed; reader = width; }
                        else { var slash = reader; if (!slash.TryReadSlash()) return false; }
                        if (reader.TryReadSlash())
                        {
                            values[3] = ReadMaskBorderPart(ref reader, context, 3);
                            if (values[3] is null) return false;
                        }
                    }
                    break;
                }
                if (!read) return false;
            }
            if (values.All(value => value is null)) return false;
            for (var i = 0; i < values.Length; i++) output.Add(new(MaskBorderNames[i], values[i] ?? MaskBorderInitial(i), false));
            return true;
        });
    }

    private static CssCompiledValue MaskBorderInitial(int part) => part == 0 ? new MaskBorderSourceValue(null)
        : new CssImmediateValue(MaskBorderProperties[part], MaskBorderProperties[part].GetMetadata(typeof(FrameworkElement)).DefaultValue);

    private static CssCompiledValue? ReadMaskBorderPart(ref CssTokenReader reader, CssCompileContext context, int part)
    {
        if (part == 0)
            return TryReadBackgroundImage(ref reader, context, out var image) ? new MaskBorderSourceValue(image) : null;
        if (part != 5) return ReadBorderImagePart(ref reader, context, part, MaskBorderProperties[part]);
        var probe = reader;
        if (!ReadMaskMode(ref probe, out var mode) || mode == CssMaskMode.MatchSource) return null;
        reader = probe; return new CssImmediateValue(CssMaskBorderProperties.ModeProperty, mode);
    }

    internal static CssImageValue? MaskBorderDefinition(CssCompiledValue value) => (value as MaskBorderSourceValue)?.Definition;
    private sealed class MaskBorderSourceValue(CssImageValue? definition) : CssCompiledValue
    {
        internal CssImageValue? Definition => definition;
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        { sink.Set(CssMaskBorderProperties.SourceProperty, CssMaskBorderSource.Materialize(definition, context)); return true; }
    }
}
