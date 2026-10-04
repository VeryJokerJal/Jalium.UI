using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    private static void RegisterImageObject()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "image-rendering", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssImageRenderingProperties.ValueProperty,
            TransitionTargetDpName = CssImageRenderingProperties.ValueProperty.Name,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var mode = ident.ToString().ToLowerInvariant() switch
                {
                    "auto" => CssImageRendering.Auto,
                    "smooth" or "optimizequality" => CssImageRendering.Smooth,
                    "high-quality" => CssImageRendering.HighQuality,
                    "crisp-edges" or "optimizespeed" => CssImageRendering.CrispEdges,
                    "pixelated" => CssImageRendering.Pixelated,
                    _ => (CssImageRendering?)null,
                };
                return mode is { } value
                    ? new CssImmediateValue(CssImageRenderingProperties.ValueProperty, value)
                    : null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "object-fit", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssImageObjectProperties.FitProperty,
            TransitionTargetDpName = CssImageObjectProperties.FitProperty.Name,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var fit = ident.ToString().ToLowerInvariant() switch
                {
                    "fill" => CssObjectFit.Fill,
                    "contain" => CssObjectFit.Contain,
                    "cover" => CssObjectFit.Cover,
                    "none" => CssObjectFit.None,
                    "scale-down" => CssObjectFit.ScaleDown,
                    _ => CssObjectFit.Unspecified,
                };
                return fit == CssObjectFit.Unspecified ? null
                    : new CssImmediateValue(CssImageObjectProperties.FitProperty, fit);
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "object-position", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssImageObjectProperties.PositionProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryReadBackgroundPosition(ref reader, out var position) && reader.AtEnd
                    ? new CssObjectPositionValue(position) : null,
        });
    }

    private sealed class CssObjectPositionValue(CssBackgroundPosition position) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            position.ObserveDependencies(context.Lengths);
            sink.Set(CssImageObjectProperties.PositionProperty, position.ForElement(context.Lengths));
            return true;
        }
    }
}
