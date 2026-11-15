using System.Xml.Linq;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>An isolated XML styling tree; it never attaches to a live UI tree or scheduler.</summary>
internal sealed class CssXmlElement(XElement source, CssXmlElement? parent) : DependencyObject
{
    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register("FontSize", typeof(double), typeof(CssXmlElement), new PropertyMetadata(16d));
    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register("FontFamily", typeof(FontFamily), typeof(CssXmlElement), new PropertyMetadata(new FontFamily("Segoe UI")));
    public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register("FontWeight", typeof(FontWeight), typeof(CssXmlElement), new PropertyMetadata(FontWeights.Normal));
    public static readonly DependencyProperty FontStyleProperty = DependencyProperty.Register("FontStyle", typeof(FontStyle), typeof(CssXmlElement), new PropertyMetadata(FontStyles.Normal));
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register("Foreground", typeof(Brush), typeof(CssXmlElement), new PropertyMetadata(Brushes.Black));
    private sealed class MatchBudget { internal int Remaining = 1_000_000; }
    private readonly MatchBudget _budget = parent?._budget ?? new();
    internal void MatchStep()
    { if (--_budget.Remaining < 0) throw new InvalidDataException("XML CSS selector matching exceeds its work budget"); }
    internal XElement Source { get; } = source;
    internal CssXmlElement? Parent { get; } = parent;
    internal List<CssXmlElement> Children { get; } = [];
    internal double InitialFontSize { get; init; } = CssLengthContext.DefaultFontSize;
    internal CssLengthContext Lengths { get; set; } = CssLengthContext.Default;
    internal Func<string, string, bool>? SupportsProperty { get; set; }
    internal IReadOnlyDictionary<string, string> Computed { get; set; } = new Dictionary<string, string>();
    internal string? InheritedAttribute(string name, XNamespace? ns = null)
    {
        for (var node = this; node is not null; node = node.Parent)
            if (node.Source.Attribute((ns ?? XNamespace.None) + name)?.Value is { } value) return value;
        return null;
    }
    internal IEnumerable<(CssExpandedName Name, string Value)> Attributes(string? ns, string local)
    {
        foreach (var attribute in Source.Attributes())
            if (!attribute.IsNamespaceDeclaration && attribute.Name.LocalName == local &&
                (ns is null || attribute.Name.NamespaceName == ns))
                yield return (new(attribute.Name.NamespaceName, local), attribute.Value);
    }
}
