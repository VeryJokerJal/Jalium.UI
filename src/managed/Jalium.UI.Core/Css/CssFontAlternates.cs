using Jalium.UI.Media;

namespace Jalium.UI.Styling;

// Function order is canonical; argument order is retained because two names can
// address the same character-variant tag with different selector values.
internal sealed class CssFontAlternates : IEquatable<CssFontAlternates>
{
    internal static readonly string[] FunctionNames = ["stylistic", "styleset", "character-variant", "swash", "ornaments", "annotation"];
    internal static readonly CssFontAlternates Normal = new(false, new string[6][]);
    internal static readonly DependencyProperty VariantProperty = DependencyProperty.RegisterAttached(
        "CssFontAlternates", typeof(CssFontAlternates), typeof(CssFontAlternates),
        new FrameworkPropertyMetadata(Normal, FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => FontWidthRenderingSource.Invalidate(CssNode.Get(owner))));

    internal bool Historical { get; }
    internal string[][] Arguments { get; }
    internal bool HasNamedValues => Arguments.Any(values => values is { Length: > 0 });
    private readonly string _key;
    private CssFontAlternates(bool historical, string[][] arguments)
    {
        Historical = historical; Arguments = arguments;
        var key = new System.Text.StringBuilder(historical ? "1" : "0");
        foreach (var values in arguments)
        {
            key.Append((values?.Length ?? -1).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':');
            foreach (string name in values ?? [])
                key.Append(name.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(name);
        }
        _key = key.ToString();
    }

    internal static CssCompiledValue? Parse(ref CssTokenReader reader)
    {
        var arguments = new string[6][]; bool historical = false, any = false;
        while (!reader.AtEnd)
        {
            var probe = reader;
            if (probe.TryReadFunction(out var function, out var values))
            {
                int index = -1;
                for (int i = 0; i < FunctionNames.Length; i++)
                    if (function.Equals(FunctionNames[i], StringComparison.OrdinalIgnoreCase)) { index = i; break; }
                if (index < 0 || arguments[index] is not null) return null;
                var names = new List<string>();
                do
                {
                    if (!values.TryReadIdent(out var name) || names.Count == 4096) return null;
                    names.Add(name.ToString());
                } while (values.TryReadComma());
                if (!values.AtEnd || names.Count == 0 || index is not (1 or 2) && names.Count != 1) return null;
                arguments[index] = names.ToArray(); reader = probe; any = true; continue;
            }
            if (!reader.TryReadIdent(out var word)) return null;
            if (word.Equals("normal", StringComparison.OrdinalIgnoreCase))
                return !any && reader.AtEnd ? new CssImmediateValue(VariantProperty, Normal) : null;
            if (!word.Equals("historical-forms", StringComparison.OrdinalIgnoreCase) || historical) return null;
            historical = any = true;
        }
        return any ? new CssImmediateValue(VariantProperty, new CssFontAlternates(historical, arguments)) : null;
    }

    internal static CssFontAlternates For(DependencyObject owner) => (CssFontAlternates)owner.GetValue(VariantProperty)!;
    internal static FontFeature[] GlobalFeatures(DependencyObject owner) => For(owner).Historical ? [new(FontFeature.TagOf("hist"), 1)] : [];

    internal FontFeature[] Resolve(Func<string, string, int[]?> definition)
    {
        var features = new Dictionary<uint, uint>();
        for (int i = 0; i < Arguments.Length; i++)
            foreach (var name in Arguments[i] ?? [])
            {
                if (definition(FunctionNames[i], name) is not { Length: > 0 } values) continue;
                if (i == 1)
                {
                    foreach (int index in values) if (index is >= 1 and <= 99) Set("ss" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), 1);
                }
                else if (i == 2)
                {
                    if (values[0] is >= 1 and <= 99) Set("cv" + values[0].ToString("D2", System.Globalization.CultureInfo.InvariantCulture), values.Length == 1 ? 1 : values[1]);
                }
                else if (i == 3) { Set("swsh", values[0]); Set("cswh", values[0]); }
                else Set(i switch { 0 => "salt", 4 => "ornm", _ => "nalt" }, values[0]);
            }
        return features.OrderBy(pair => pair.Key).Select(pair => new FontFeature(pair.Key, pair.Value)).ToArray();
        void Set(string tag, int value) => features[FontFeature.TagOf(tag)] = (uint)value;
    }

    public bool Equals(CssFontAlternates? other) => other is not null && _key == other._key;
    public override bool Equals(object? other) => other is CssFontAlternates value && Equals(value);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_key);
}
