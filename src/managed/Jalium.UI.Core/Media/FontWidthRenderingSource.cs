using System.Globalization;
using Jalium.UI.Styling;

namespace Jalium.UI.Media;

// Internal rendering identity shared by measurement, hit testing and drawing.
// Public family names remain unchanged. Including width here also separates
// every existing font/metrics/paragraph cache without widening their public APIs.
internal static class FontWidthRenderingSource
{
    private const string Prefix = "\u0003font-width:";

    internal static void Invalidate(CssNode root)
    {
        // A percentage may change without changing the nine-class DP value.
        // Inherited descendants and retained inline layout still need new metrics.
        foreach (var child in root.EnumerateChildren()) CssEvaluationScheduler.InvalidateSubtree(child);
        var pending = new Stack<CssNode>(); pending.Push(root);
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node.Target)) continue;
            if (node.Target is FrameworkElement visual) visual.OnFontResourcesChanged();
            foreach (var child in node.EnumerateChildren()) pending.Push(child);
        }
        if (root.Target is not FrameworkElement)
            for (var parent = root.FrameworkParent; parent is not null; parent = parent.FrameworkParent)
                if (parent.Target is FrameworkElement visual) { visual.OnFontResourcesChanged(); break; }
    }

    internal static double Percentage(int stretch) => stretch switch
    {
        1 => 50, 2 => 62.5, 3 => 75, 4 => 87.5, 6 => 112.5, 7 => 125, 8 => 150, 9 => 200, _ => 100
    };

    internal static string ForOwner(string source, DependencyObject owner) => OperatingSystem.IsMacOS() &&
        owner is FrameworkElement or FrameworkContentElement
        ? Wrap(source, CssFontStretchValue.Computed(CssNode.Get(owner))) : source;

    internal static string ForFormattedText(string source, int stretch) => !OperatingSystem.IsMacOS() ||
        stretch == 5 || TryUnwrap(source, out _, out _) || CssFontRenderingPlan.TryDecode(source, out _)
        ? source : Wrap(source, Percentage(stretch));

    internal static string Wrap(string source, double percentage)
    {
        if (!OperatingSystem.IsMacOS() || percentage == 100) return source;
        if (!double.IsFinite(percentage) || percentage < 0) throw new ArgumentOutOfRangeException(nameof(percentage));
        bool blocked = CssFontFaces.IsBlocked(source); source = CssFontFaces.Unblock(source);
        if (TryUnwrap(source, out _, out var family)) source = family;
        return (blocked ? CssFontFaces.BlockPrefix : "") + Prefix + percentage.ToString("R", CultureInfo.InvariantCulture) + ":" + source;
    }

    internal static bool TryUnwrap(string source, out double percentage, out string family)
    {
        percentage = 100; family = source;
        string unblocked = CssFontFaces.Unblock(source);
        if (!unblocked.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        int separator = unblocked.IndexOf(':', Prefix.Length);
        if (separator < 0 || !double.TryParse(unblocked.AsSpan(Prefix.Length, separator - Prefix.Length),
                NumberStyles.Float, CultureInfo.InvariantCulture, out percentage) || !double.IsFinite(percentage) || percentage < 0)
        { percentage = 100; return false; }
        family = unblocked[(separator + 1)..]; return true;
    }
}
