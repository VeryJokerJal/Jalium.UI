using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Jalium.UI.Media.TextFormatting;

namespace Jalium.UI.Styling;

/// <summary>Resolves extends separately from value-dependent fallback; both graphs can contain cycles.</summary>
internal sealed class CssCounterStyleMap(IReadOnlyDictionary<string, CssCounterStyleRule> rules)
{
    internal static readonly CssCounterStyleMap Empty = new(new Dictionary<string, CssCounterStyleRule>());
    private static readonly Dictionary<string, CssCounterStyleDefinition> Builtins = CssCounterStyles.BuiltinNames
        .ToDictionary(name => name, name => Defaults(CssCounterStyles.Builtin(name), null), StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<string, CssCounterStyleDefinition> Anonymous = new();
    private const int MaximumCharacters = 4096;
    private readonly Dictionary<string, CssCounterStyleDefinition> _resolved = new(StringComparer.Ordinal);

    internal string Format(int value, string reference) => StripPrivateUse(FormatCore(value, reference, out _));

    internal string FormatMarker(int value, string reference)
    {
        var definition = Definition(reference);
        return StripPrivateUse(definition.Prefix + FormatCore(value, reference, out _) + definition.Suffix);
    }

    internal string FormatWithImages(int value, string reference, bool includeAffixes, out CssResolvedContent[]? parts)
    {
        var outer = Definition(reference);
        var core = FormatCore(value, reference, out var used);
        var raw = includeAffixes ? outer.Prefix + core + outer.Suffix : core;
        if ((outer.ImageSymbols is null || outer.ImageSymbols.Count == 0) &&
            (used.ImageSymbols is null || used.ImageSymbols.Count == 0))
        {
            parts = null;
            return raw;
        }
        var segments = new List<CssResolvedContent>();
        var textRun = new StringBuilder();
        var hasImage = false;
        foreach (var ch in raw)
        {
            if (used.ImageSymbols is not null && used.ImageSymbols.TryGetValue(ch, out var img) ||
                outer.ImageSymbols is not null && outer.ImageSymbols.TryGetValue(ch, out img))
            {
                if (textRun.Length > 0) { segments.Add(new(Text: textRun.ToString())); textRun.Clear(); }
                segments.Add(new(Image: img));
                hasImage = true;
            }
            else textRun.Append(ch);
        }
        if (textRun.Length > 0) segments.Add(new(Text: textRun.ToString()));
        parts = hasImage ? segments.ToArray() : null;
        var stripped = StripPrivateUse(raw, " ");
        return stripped.Length > 0 ? stripped : " ";
    }

    internal string? FormatSpoken(int value, string reference, bool includeAffixes)
    {
        var outer = Definition(reference);
        if (!outer.HasExplicitSpeakAs && outer.ImageSymbols is not { Count: > 0 }) return null;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var currentRef = reference;
        var currentDef = outer;
        var mode = currentDef.SpeakAs ?? "auto";
        while (mode is not ("auto" or "bullets" or "numbers" or "words" or "spell-out"))
        {
            if (!visited.Add(mode)) { mode = "auto"; break; }
            currentRef = mode;
            currentDef = Definition(currentRef);
            mode = currentDef.SpeakAs ?? "auto";
        }
        if (mode == "auto")
        {
            mode = currentDef.System switch
            {
                CssCounterSystem.Cyclic => "bullets",
                CssCounterSystem.Alphabetic => "spell-out",
                _ => "numbers",
            };
        }
        var core = mode switch
        {
            "bullets" => "•",
            "numbers" => Format(value, "decimal"),
            "words" => FormatSpokenWords(value, currentRef),
            "spell-out" => string.Join(' ', Format(value, currentRef).EnumerateRunes().Select(r => r.ToString())),
            _ => Format(value, currentRef),
        };
        if (!includeAffixes) return core;
        if (outer.ImageSymbols is { Count: > 0 })
        {
            var imgPrefix = StripPrivateUse(outer.Prefix ?? string.Empty).Trim();
            var imgSuffix = StripPrivateUse(outer.Suffix ?? string.Empty).Trim();
            return (imgPrefix.Length > 0 ? imgPrefix + " " : string.Empty) + core + (imgSuffix.Length > 0 ? " " + imgSuffix : string.Empty);
        }
        var prefix = StripPrivateUse(outer.Prefix ?? string.Empty);
        var suffix = StripPrivateUse(outer.Suffix ?? string.Empty);
        return prefix + core + suffix;
    }

    private static string FormatSpokenWords(int value, string _)
    {
        if (value == 0) return "zero";
        if (value < 0) return "minus " + FormatSpokenWords(Math.Abs(value), _);
        string[] ones = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
        string[] tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];
        if (value < 20) return ones[value];
        if (value < 100) return value % 10 == 0 ? tens[value / 10] : $"{tens[value / 10]}-{ones[value % 10]}";
        if (value < 1000) return value % 100 == 0 ? $"{ones[value / 100]} hundred" : $"{ones[value / 100]} hundred {FormatSpokenWords(value % 100, _)}";
        if (value < 1_000_000) return value % 1000 == 0 ? $"{FormatSpokenWords(value / 1000, _)} thousand" : $"{FormatSpokenWords(value / 1000, _)} thousand {FormatSpokenWords(value % 1000, _)}";
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private string FormatCore(int value, string reference, out CssCounterStyleDefinition usedDefinition)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(reference))
        {
            var definition = Definition(reference);
            if (Representation(value, definition) is { } result)
            {
                usedDefinition = definition;
                return result;
            }
            reference = definition.Fallback!;
        }
        usedDefinition = Builtins["decimal"];
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string StripPrivateUse(string text, string replacement = "")
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is >= '\uE000' and <= '\uE0FF')
            {
                sb ??= new StringBuilder(text.Length).Append(text, 0, i);
                sb.Append(replacement);
            }
            else sb?.Append(text[i]);
        }
        return sb?.ToString() ?? text;
    }

    internal CssCounterStyleDefinition Definition(string reference)
    {
        if (reference.Length > 0 && reference[0] == CssCounterStyles.AnonymousPrefix)
            return Anonymous.GetValue(reference, static key => Defaults(CssCounterStyles.Anonymous(key[1..]) ?? CssCounterStyles.Builtin("decimal"), null));
        reference = CssCounterStyles.NormalizeName(reference);
        // Empty is shared across documents and threads; builtin resolution is read-only.
        if (rules.Count == 0 || !rules.ContainsKey(reference)) return Builtins.GetValueOrDefault(reference, Builtins["decimal"]);
        if (_resolved.TryGetValue(reference, out var cached)) return cached;
        var chain = new List<string>(); var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var current = reference;
        CssCounterStyleDefinition inherited;
        while (true)
        {
            if (_resolved.TryGetValue(current, out inherited!)) break;
            if (!rules.TryGetValue(current, out var rule)) { inherited = Builtins.GetValueOrDefault(current, Builtins["decimal"]); break; }
            if (indices.TryGetValue(current, out var cycleStart))
            {
                var decimalStyle = Builtins["decimal"];
                for (var i = cycleStart; i < chain.Count; i++)
                {
                    var cycleRule = rules[chain[i]];
                    _resolved[chain[i]] = Defaults(cycleRule.Definition, decimalStyle, cycleRule.BaseUri, cycleRule.ResourceResolver);
                }
                inherited = _resolved[current]; break;
            }
            if (rule.Definition.System != CssCounterSystem.Extends)
            { inherited = Defaults(rule.Definition, null, rule.BaseUri, rule.ResourceResolver); _resolved[current] = inherited; break; }
            indices[current] = chain.Count; chain.Add(current); current = rule.Definition.Extends!;
        }
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var name = chain[i];
            if (_resolved.TryGetValue(name, out var cycle)) { inherited = cycle; continue; }
            var chainRule = rules[name];
            inherited = Defaults(chainRule.Definition, inherited, chainRule.BaseUri, chainRule.ResourceResolver);
            _resolved[name] = inherited;
        }
        return _resolved[reference];
    }

    private static CssCounterStyleDefinition Defaults(CssCounterStyleDefinition specified, CssCounterStyleDefinition? inherited,
        Uri? baseUri = null, ICssResourceResolver? resolver = null)
    {
        var extends = specified.System == CssCounterSystem.Extends;
        Dictionary<char, CssImageValue>? imageSymbols = null;
        if (inherited?.ImageSymbols is { Count: > 0 } inheritedImages)
            imageSymbols = new Dictionary<char, CssImageValue>(inheritedImages);
        if (specified.ImageSyntaxes is { Count: > 0 } syntaxes)
        {
            imageSymbols ??= new Dictionary<char, CssImageValue>();
            var context = new CssCompileContext { BaseUri = baseUri, ResourceResolver = resolver };
            foreach (var (token, syntax) in syntaxes)
            {
                var reader = new CssTokenReader(syntax);
                if (CssImageValue.TryRead(ref reader, context, out var image) && image is not null)
                    imageSymbols[token] = image;
            }
        }
        return specified with
        {
            System = extends ? inherited!.System : specified.System ?? CssCounterSystem.Symbolic,
            PredefinedAlgorithm = extends ? inherited!.PredefinedAlgorithm : specified.PredefinedAlgorithm,
            First = extends ? inherited!.First : specified.First ?? 1,
            Symbols = extends ? inherited!.Symbols : specified.Symbols,
            Additive = extends ? inherited!.Additive : specified.Additive,
            UpperRoman = extends ? inherited!.UpperRoman : specified.UpperRoman,
            Negative = specified.Negative ?? inherited?.Negative ?? ["-"],
            Prefix = specified.Prefix ?? inherited?.Prefix ?? string.Empty,
            Suffix = specified.Suffix ?? inherited?.Suffix ?? ". ",
            Ranges = specified.Ranges ?? inherited?.Ranges ?? [],
            PadWidth = specified.PadWidth ?? inherited?.PadWidth ?? 0,
            PadSymbol = specified.PadSymbol ?? inherited?.PadSymbol ?? string.Empty,
            Fallback = specified.Fallback ?? inherited?.Fallback ?? "decimal",
            SpeakAs = specified.SpeakAs ?? inherited?.SpeakAs ?? "auto",
            HasExplicitSpeakAs = specified.HasExplicitSpeakAs || (inherited?.HasExplicitSpeakAs ?? false),
            ImageSymbols = imageSymbols,
        };
    }

    private static string? Representation(int value, CssCounterStyleDefinition definition)
    {
        if (!InRange(value, definition)) return null;
        var sign = definition.System is CssCounterSystem.Numeric or CssCounterSystem.Alphabetic or CssCounterSystem.Symbolic or CssCounterSystem.Additive or CssCounterSystem.Roman ||
            (definition.System == CssCounterSystem.Predefined && definition.PredefinedAlgorithm != CssPredefinedCounterAlgorithm.EthiopicNumeric);
        long number = sign && value < 0 ? -(long)value : value;
        var text = Initial(number, definition);
        if (text is null || text.Length > MaximumCharacters) return null;
        var negative = value < 0 && sign ? definition.Negative! : [];
        var pad = (long)definition.PadWidth! - Graphemes(text) - negative.Sum(Graphemes);
        var result = new StringBuilder();
        if (negative.Length > 0 && !Append(result, negative[0], 1) ||
            pad > 0 && !Append(result, definition.PadSymbol!, pad) || !Append(result, text, 1) ||
            negative.Length > 1 && !Append(result, negative[1], 1)) return null;
        return result.ToString();
    }

    private static bool InRange(int value, CssCounterStyleDefinition definition)
    {
        if (definition.Ranges is { Length: > 0 } ranges) return ranges.Any(range => value >= range.Minimum && value <= range.Maximum);
        return definition.System switch
        {
            CssCounterSystem.Alphabetic or CssCounterSystem.Symbolic => value >= 1,
            CssCounterSystem.Additive => value >= 0,
            CssCounterSystem.Roman => value is >= 1 and <= 3999,
            CssCounterSystem.Predefined => definition.PredefinedAlgorithm == CssPredefinedCounterAlgorithm.EthiopicNumeric ? value >= 1 : value is >= -9999 and <= 9999,
            _ => true,
        };
    }

    private static string? Initial(long number, CssCounterStyleDefinition definition)
    {
        var symbols = definition.Symbols ?? [];
        var output = new StringBuilder();
        switch (definition.System)
        {
            case CssCounterSystem.Cyclic:
                return symbols[(int)(((number - 1) % symbols.Length + symbols.Length) % symbols.Length)];
            case CssCounterSystem.Fixed:
                var index = number - definition.First!.Value;
                return index >= 0 && index < symbols.Length ? symbols[(int)index] : null;
            case CssCounterSystem.Symbolic:
                if (number < 1) return null;
                return Append(output, symbols[(int)((number - 1) % symbols.Length)], (number - 1) / symbols.Length + 1) ? output.ToString() : null;
            case CssCounterSystem.Numeric: case CssCounterSystem.Alphabetic:
                var alphabetic = definition.System == CssCounterSystem.Alphabetic;
                if (alphabetic && number < 1) return null;
                var digits = new List<string>();
                do
                {
                    if (alphabetic) number--;
                    digits.Add(symbols[(int)(number % symbols.Length)]); number /= symbols.Length;
                } while (number > 0);
                for (var i = digits.Count - 1; i >= 0; i--) if (!Append(output, digits[i], 1)) return null;
                return output.ToString();
            case CssCounterSystem.Additive:
                if (number == 0) return definition.Additive!.FirstOrDefault(entry => entry.Weight == 0).Symbol;
                foreach (var entry in definition.Additive!)
                {
                    if (entry.Weight == 0) continue;
                    if (!Append(output, entry.Symbol, number / entry.Weight)) return null;
                    number %= entry.Weight;
                }
                return number == 0 ? output.ToString() : null;
            case CssCounterSystem.Roman:
                return number is >= 1 and <= 3999 ? TextSimpleMarkerProperties.CounterRepresentation(
                    definition.UpperRoman ? TextMarkerStyle.UpperRoman : TextMarkerStyle.LowerRoman, (int)number) : null;
            case CssCounterSystem.Predefined:
                return FormatPredefined(number, definition.PredefinedAlgorithm);
            default: return null;
        }
    }

    private static string? FormatPredefined(long number, CssPredefinedCounterAlgorithm algorithm)
    {
        if (algorithm == CssPredefinedCounterAlgorithm.EthiopicNumeric)
        {
            if (number < 1) return null;
            if (number == 1) return "\u1369";
            var groups = new List<int>();
            var remaining = number;
            while (remaining > 0) { groups.Add((int)(remaining % 100)); remaining /= 100; }
            var sb = new StringBuilder();
            for (var g = groups.Count - 1; g >= 0; g--)
            {
                var pair = groups[g];
                var omitDigits = pair == 0 || (pair == 1 && (g == groups.Count - 1 || g % 2 == 1));
                if (!omitDigits)
                {
                    if (pair / 10 > 0) sb.Append((char)('\u1372' + (pair / 10 - 1)));
                    if (pair % 10 > 0) sb.Append((char)('\u1369' + (pair % 10 - 1)));
                }
                if (g > 0)
                {
                    if (g % 2 == 1) { if (pair > 0) sb.Append('\u137B'); }
                    else sb.Append('\u137C');
                }
            }
            return sb.ToString();
        }
        if (number is < 0 or > 9999) return null;
        var (digitChars, unitChars, chineseRules, informal) = algorithm switch
        {
            CssPredefinedCounterAlgorithm.JapaneseInformal => ("〇一二三四五六七八九", "十百千", false, true),
            CssPredefinedCounterAlgorithm.JapaneseFormal => ("零壱弐参四伍六七八九", "拾百阡", false, false),
            CssPredefinedCounterAlgorithm.KoreanHangulFormal => ("영일이삼사오육칠팔구", "십백천", false, false),
            CssPredefinedCounterAlgorithm.KoreanHanjaInformal => ("零一二三四五六七八九", "十百千", false, true),
            CssPredefinedCounterAlgorithm.KoreanHanjaFormal => ("零壹貳參四五六七八九", "拾百千", false, false),
            CssPredefinedCounterAlgorithm.SimpChineseInformal => ("零一二三四五六七八九", "十百千", true, true),
            CssPredefinedCounterAlgorithm.SimpChineseFormal => ("零壹贰叁肆伍陆柒捌玖", "拾佰仟", true, false),
            CssPredefinedCounterAlgorithm.TradChineseInformal => ("零一二三四五六七八九", "十百千", true, true),
            CssPredefinedCounterAlgorithm.TradChineseFormal => ("零壹貳參肆伍陸柒捌玖", "拾佰仟", true, false),
            _ => ("", "", false, false),
        };
        if (digitChars.Length == 0) return null;
        if (number == 0) return digitChars[0].ToString();
        var weights = new[] { 1000, 100, 10, 1 };
        var result = new StringBuilder();
        var pendingZero = false;
        for (var i = 0; i < 4; i++)
        {
            var d = (int)((number / weights[i]) % 10);
            if (d == 0)
            {
                if (chineseRules && result.Length > 0) pendingZero = true;
                continue;
            }
            if (pendingZero) { result.Append(digitChars[0]); pendingZero = false; }
            var dropOne = informal && (chineseRules ? weights[i] == 10 && number is >= 10 and <= 19 : weights[i] > 1 && d == 1);
            if (!dropOne) result.Append(digitChars[d]);
            if (weights[i] == 1000) result.Append(unitChars[2]);
            else if (weights[i] == 100) result.Append(unitChars[1]);
            else if (weights[i] == 10) result.Append(unitChars[0]);
        }
        return result.ToString();
    }

    private static int Graphemes(string text) => new StringInfo(text).LengthInTextElements;
    private static bool Append(StringBuilder output, string symbol, long count)
    {
        if (symbol.Length == 0 || count == 0) return true;
        if (count < 0 || count > (MaximumCharacters - output.Length) / symbol.Length) return false;
        for (var i = 0L; i < count; i++) output.Append(symbol);
        return true;
    }
}
