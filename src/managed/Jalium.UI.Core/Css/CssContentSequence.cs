namespace Jalium.UI.Styling;

internal enum CssContentKind : byte { Text, Image, Counter, Counters, OpenQuote, CloseQuote, NoOpenQuote, NoCloseQuote }
internal readonly record struct CssContentPart(CssContentKind Kind, string Text = "", string Style = "decimal", string Separator = "", CssImageValue? Image = null);
internal readonly record struct CssResolvedContent(string Text = "", CssImageValue? Image = null);

/// <summary>Immutable computed content tokens. Value equality keeps deferred attr()/var() stable.</summary>
internal sealed class CssContentSequence(CssContentPart[] parts) : IEquatable<CssContentSequence>
{
    private readonly CssContentPart[] _parts = parts;
    internal IReadOnlyList<CssContentPart> Parts => _parts;
    internal bool IsDynamic => _parts.Any(p => p.Kind != CssContentKind.Text);
    internal string Literal => string.Concat(_parts.Select(p => p.Text));
    public bool Equals(CssContentSequence? other) => other is not null && _parts.AsSpan().SequenceEqual(other._parts);
    public override bool Equals(object? obj) => obj is CssContentSequence other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode(); foreach (var part in _parts) hash.Add(part); return hash.ToHashCode();
    }

    internal static bool Read(ref CssTokenReader reader, bool alternative, out CssContentSequence sequence, CssCompileContext? context = null)
    {
        sequence = new([]);
        var parts = new List<CssContentPart>();
        while (!reader.AtEnd && !(reader.TryPeekChar(out var next) && next == '/'))
        {
            if (reader.TryReadString(out var text)) { parts.Add(new(CssContentKind.Text, text)); continue; }
            var imageReader = reader;
            if (!alternative && CssImageValue.TryRead(ref imageReader, context ?? CssCompileContext.Default, out var image))
            { reader = imageReader; parts.Add(new(CssContentKind.Image, Image: image)); continue; }
            if (reader.TryReadFunction(out var function, out var arguments))
            {
                var kind = function.ToString().ToLowerInvariant() switch
                {
                    "counter" => CssContentKind.Counter, "counters" => CssContentKind.Counters, _ => CssContentKind.Text,
                };
                if (kind == CssContentKind.Text || !arguments.TryReadIdent(out var name) || !CssCounters.IsName(name)) return false;
                var counter = name.ToString(); var separator = string.Empty; var style = "decimal";
                if (kind == CssContentKind.Counters && (!arguments.TryReadComma() || !arguments.TryReadString(out separator))) return false;
                if (arguments.TryReadComma())
                {
                    if (!CssCounterStyles.ReadReference(ref arguments, out style)) return false;
                }
                if (!arguments.AtEnd) return false;
                parts.Add(new(kind, counter, style, separator)); continue;
            }
            if (!alternative && reader.TryReadIdent(out var keyword))
            {
                var kind = keyword.ToString().ToLowerInvariant() switch
                {
                    "open-quote" => CssContentKind.OpenQuote, "close-quote" => CssContentKind.CloseQuote,
                    "no-open-quote" => CssContentKind.NoOpenQuote, "no-close-quote" => CssContentKind.NoCloseQuote,
                    _ => CssContentKind.Text,
                };
                if (kind == CssContentKind.Text) return false;
                parts.Add(new(kind)); continue;
            }
            return false;
        }
        sequence = new(parts.ToArray());
        return parts.Count > 0 && (reader.AtEnd || reader.TryPeekChar(out var end) && end == '/');
    }
}
