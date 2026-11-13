using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Styling;

/// <summary>CSS natural lengths and ratio are independent; null is absent, zero is a real length.</summary>
internal readonly record struct CssImageIntrinsicSize(double? Width, double? Height, double Ratio = 0)
{
    internal static readonly Size ReplacedDefault = new(300, 150);
    internal bool HasAny => Width.HasValue || Height.HasValue || Ratio > 0;
    internal Size? CompleteSize => Width is { } w && Height is { } h ? new(w, h) : null;
    internal static CssImageIntrinsicSize FromSize(Size size) => new(size.Width, size.Height,
        size.Width > 0 && size.Height > 0 ? size.Width / size.Height : 0);

    internal static CssImageIntrinsicSize Mix(IEnumerable<(CssImageIntrinsicSize Intrinsic, double Weight)> parts, Size fallback)
    {
        double width = 0, height = 0, total = 0;
        foreach (var (intrinsic, weight) in parts)
        {
            if (weight <= 0 || !intrinsic.HasAny) continue;
            var size = intrinsic.DefaultSize(fallback);
            width += size.Width * weight; height += size.Height * weight; total += weight;
        }
        return total > 0 ? FromSize(new(width / total, height / total)) : default;
    }

    internal CssImageIntrinsicSize AtDensity(double density)
    {
        density = density > 0 && double.IsFinite(density) ? density : 1;
        return new(Width / density, Height / density, Ratio);
    }

    internal Size DefaultSize(Size fallback, double? specifiedWidth = null, double? specifiedHeight = null)
    {
        if (specifiedWidth is { } w && specifiedHeight is { } h) return new(w, h);
        if (specifiedWidth is { } width) return new(width, Ratio > 0 ? width / Ratio : Height ?? fallback.Height);
        if (specifiedHeight is { } height) return new(Ratio > 0 ? height * Ratio : Width ?? fallback.Width, height);
        if (Width.HasValue || Height.HasValue) return DefaultSize(fallback, Width, Height);
        return Constraint(fallback, cover: false);
    }

    internal Size Constraint(Size box, bool cover)
    {
        if (!(Ratio > 0) || !double.IsFinite(Ratio)) return box;
        var width = cover ? Math.Max(box.Width, box.Height * Ratio) : Math.Min(box.Width, box.Height * Ratio);
        return new(width, width / Ratio);
    }

    internal Size ObjectSize(Size box, CssObjectFit fit)
    {
        if (fit is CssObjectFit.Fill or CssObjectFit.Unspecified) return box;
        if (fit == CssObjectFit.Contain) return Constraint(box, false);
        if (fit == CssObjectFit.Cover) return Constraint(box, true);
        var natural = DefaultSize(box);
        if (fit == CssObjectFit.None) return natural;
        var contain = Constraint(box, false);
        return new(Math.Min(natural.Width, contain.Width), Math.Min(natural.Height, contain.Height));
    }

    internal static CssImageIntrinsicSize FromBrush(Brush? brush, Size fallback, double density = 1)
        => (brush?.CssImageLayout is { } generated ? generated.IntrinsicSize(fallback) :
            brush is ImageBrush { ImageSource: { } source } ? FromSource(source) : default).AtDensity(density);

    internal static CssImageIntrinsicSize FromSource(ImageSource source)
    {
        if (source.TryGetLoadFailure(out _, out var failure) && failure == ImageSourceFailureKind.Load) return default;
        if (source is SvgImage svg)
        {
            var intrinsic = svg.CssIntrinsic;
            return new(intrinsic.HasWidth ? intrinsic.Width : null, intrinsic.HasHeight ? intrinsic.Height : null, intrinsic.Ratio);
        }
        var width = source.Width; var height = source.Height;
        if (source is BitmapImage bitmap)
        {
            bitmap.RequestMetadata();
            if ((width <= 0 || height <= 0) && bitmap.TryGetIntrinsicMetadataSize(out var w, out var h)) { width = w; height = h; }
        }
        return width > 0 && height > 0 && double.IsFinite(width) && double.IsFinite(height)
            ? new(width, height, width / height) : default;
    }

    internal static bool IsAvailable(ImageSource source)
        => !(source.TryGetLoadFailure(out _, out var failure) && failure == ImageSourceFailureKind.Load) &&
            (source is SvgImage { Drawing: not null } || FromSource(source).CompleteSize is not null);
}
