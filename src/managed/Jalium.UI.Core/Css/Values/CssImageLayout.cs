using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Resolves a generated CSS image at its actual concrete object size.</summary>
internal abstract class CssImageLayout
{
    private sealed record ResolvedEntry(Brush Template, Matrix Absolute, Matrix Relative, double Opacity, Brush Value);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Brush, ResolvedEntry> Resolved = new();
    private static readonly object ResolveGate = new();

    internal virtual bool IsValid => true;
    internal virtual Size? NaturalSize => null;
    internal virtual CssImageIntrinsicSize IntrinsicSize(Size fallback)
        => NaturalSize is { } size ? CssImageIntrinsicSize.FromSize(size) : default;
    internal abstract Brush? Resolve(double width, double height);

    internal static Brush? ResolveBrush(Brush brush, double width, double height)
    {
        if (brush.CssImageLayout is not { } layout) return brush;
        var resolved = layout.Resolve(width, height);
        if (resolved is null) return null;
        var absolute = brush.Transform?.Value ?? Matrix.Identity;
        var relative = brush.RelativeTransform?.Value ?? Matrix.Identity;
        var opacity = brush.Opacity;
        if (absolute.IsIdentity && relative.IsIdentity && opacity == 1)
            return resolved;
        lock (ResolveGate)
        {
            if (Resolved.TryGetValue(brush, out var entry) && ReferenceEquals(entry.Template, resolved) &&
                entry.Absolute == absolute && entry.Relative == relative && entry.Opacity == opacity)
                return entry.Value;
            var result = resolved.CloneCurrentValue();
            // Sharing the original mutable transform would keep every obsolete
            // resolved brush alive through Freezable's child Changed handler.
            result.Transform = new MatrixTransform(absolute);
            result.RelativeTransform = new MatrixTransform(relative);
            result.Opacity *= opacity;
            if (result.CanFreeze) result.Freeze();
            Resolved.AddOrUpdate(brush, new(resolved, absolute, relative, opacity, result));
            return result;
        }
    }
}

internal sealed class CssSolidImageLayout(Color color) : CssImageLayout
{
    internal override Brush Resolve(double width, double height) => new SolidColorBrush(color);
}
