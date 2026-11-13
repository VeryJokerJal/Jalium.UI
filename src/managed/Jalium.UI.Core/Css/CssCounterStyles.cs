using System.Runtime.CompilerServices;

namespace Jalium.UI.Styling;

/// <summary>Document-wide named counter styles share the existing layer and media cascade.</summary>
internal static class CssCounterStyles
{
    // CSS input replaces NUL, so this cannot collide with an escaped custom identifier.
    internal const char AnonymousPrefix = '\0';
    private sealed class Document
    {
        internal int Version = -1;
        internal bool Dirty = true;
        internal CssStyleSheet[] Sheets = [];
        internal Dictionary<string, CssCounterStyleRule> Selected = new(StringComparer.Ordinal);
        internal CssCounterStyleMap Styles = CssCounterStyleMap.Empty;
    }
    private static readonly ConditionalWeakTable<CssNode, Document> Documents = new();
    internal static bool IsActive;

    internal static CssCounterStyleMap For(CssNode element)
    {
        if (!IsActive) return CssCounterStyleMap.Empty;
        var root = CssMatcher.Root(element);
        var document = Documents.GetValue(root, static _ => new());
        if (document.Dirty || document.Version != CssEngine.CascadeVersion)
        {
            var sheets = new List<CssStyleSheet>();
            if (CssEngine.ApplicationStyleSheetsProvider?.Invoke() is { } application) sheets.AddRange(application);
            var pending = new Stack<CssNode>(); var visited = new HashSet<CssNode>(); pending.Push(root);
            while (pending.TryPop(out var node))
            {
                if (!visited.Add(node)) continue;
                if (node.CssRuntimeState?.ScopedStyleSheets is { } scoped) sheets.AddRange(scoped);
                foreach (var child in node.EnumerateChildren().Reverse()) pending.Push(child);
            }
            document.Sheets = sheets.ToArray(); document.Dirty = false; document.Version = CssEngine.CascadeVersion;
        }
        var rules = new Dictionary<string, CssCounterStyleRule>(StringComparer.Ordinal);
        var layers = new CssLayerOrder(); var selectedLayers = new Dictionary<string, int[]>(StringComparer.Ordinal);
        void Add(IEnumerable<CssStyleSheet> sheets)
        {
            foreach (var sheet in sheets)
            {
                foreach (var layer in sheet.Layers)
                    if (layer.Condition is null || layer.Condition.Evaluate(root)) layers.GetKey(layer.Name);
                foreach (var rule in sheet.CounterStyles)
                {
                    if (rule.Condition is not null && !rule.Condition.Evaluate(root)) continue;
                    var key = layers.GetKey(rule.LayerName);
                    if (selectedLayers.TryGetValue(rule.Name, out var previous) && CssLayerOrder.Compare(key, previous) < 0) continue;
                    rules[rule.Name] = rule; selectedLayers[rule.Name] = key;
                }
            }
        }
        // Conditions must be read on every pass: preferences can change without a
        // viewport/cascade version change, and evaluation refreshes their subscriptions.
        Add(document.Sheets);
        if (rules.Count != document.Selected.Count || rules.Any(pair =>
            !document.Selected.TryGetValue(pair.Key, out var previous) || !ReferenceEquals(previous, pair.Value)))
        { document.Styles = new(rules); document.Selected = rules; }
        return document.Styles;
    }

    internal static void Invalidate(CssNode element)
    {
        if (IsActive && Documents.TryGetValue(CssMatcher.Root(element), out var document)) document.Dirty = true;
    }

    internal static string Format(int value, string style) => CssCounterStyleMap.Empty.Format(value, style);

    // Only predefined names are case-folded. Author-defined identifiers stay case-sensitive.
    private static readonly HashSet<string> Predefined = new((
        "decimal decimal-leading-zero arabic-indic armenian upper-armenian lower-armenian bengali cambodian khmer cjk-decimal devanagari georgian gujarati gurmukhi hebrew kannada lao malayalam mongolian myanmar oriya persian lower-roman upper-roman tamil telugu thai tibetan " +
        "lower-alpha lower-latin upper-alpha upper-latin lower-greek hiragana hiragana-iroha katakana katakana-iroha disc circle square disclosure-open disclosure-closed cjk-earthly-branch cjk-heavenly-stem " +
        "japanese-informal japanese-formal korean-hangul-formal korean-hanja-informal korean-hanja-formal simp-chinese-informal simp-chinese-formal trad-chinese-informal trad-chinese-formal ethiopic-numeric").Split(' '), StringComparer.OrdinalIgnoreCase);
    internal static string NormalizeName(string name) => Predefined.Contains(name) ? name.ToLowerInvariant() : name;
    internal static IEnumerable<string> BuiltinNames => Predefined;

    internal static bool ReadReference(ref CssTokenReader reader, out string reference)
    {
        var probe = reader;
        if (CssCounterStyleRule.ReadName(ref probe, out reference)) { reader = probe; return true; }
        if (!reader.TryReadFunction(out var function, out var arguments) || !function.Equals("symbols", StringComparison.OrdinalIgnoreCase)) return false;
        var rawArgs = arguments.Remaining.ToString();
        var system = CssCounterSystem.Symbolic;
        var type = arguments;
        if (type.TryReadIdent(out var ident))
        {
            if (CssCounterStyleRule.ReadSystem(ident) is not { } specified || specified is CssCounterSystem.Extends or CssCounterSystem.Additive) return false;
            system = specified; arguments = type;
        }
        var imageSyntaxes = new Dictionary<char, string>();
        var symbols = CssCounterStyleRule.ReadSymbols(ref arguments, identifiers: false, imageSyntaxes);
        if (symbols is null || symbols.Length < (system is CssCounterSystem.Numeric or CssCounterSystem.Alphabetic ? 2 : 1)) return false;
        reference = AnonymousPrefix + "symbols(" + rawArgs + ")";
        return true;
    }

    internal static CssCounterStyleDefinition? Anonymous(string reference)
    {
        var reader = new CssTokenReader(reference);
        if (!reader.TryReadFunction(out var name, out var args) || !reader.AtEnd || !name.Equals("symbols", StringComparison.OrdinalIgnoreCase)) return null;
        var system = CssCounterSystem.Symbolic;
        var type = args;
        if (type.TryReadIdent(out var systemName) && CssCounterStyleRule.ReadSystem(systemName) is { } specified)
        { system = specified; args = type; }
        var imageSyntaxes = new Dictionary<char, string>();
        var symbols = CssCounterStyleRule.ReadSymbols(ref args, false, imageSyntaxes);
        return symbols is null ? null : new()
        {
            System = system,
            Symbols = symbols,
            Suffix = " ",
            ImageSyntaxes = imageSyntaxes.Count > 0 ? imageSyntaxes : null,
        };
    }

    internal static CssCounterStyleDefinition Builtin(string name)
    {
        if (name == "cjk-decimal")
            return new() { System = CssCounterSystem.Numeric, Symbols = Letters("〇一二三四五六七八九"), Ranges = [new(0, int.MaxValue)], Suffix = "、" };
        var numeric = name switch
        {
            "arabic-indic" => "٠١٢٣٤٥٦٧٨٩", "persian" => "۰۱۲۳۴۵۶۷۸۹", "bengali" => "০১২৩৪৫৬৭৮৯",
            "cambodian" or "khmer" => "០១២៣៤៥៦៧៨៩",
            "devanagari" => "०१२३४५६७८९", "gujarati" => "૦૧૨૩૪૫૬૭૮૯", "gurmukhi" => "੦੧੨੩੪੫੬੭੮੯",
            "kannada" => "೦೧೨೩೪೫೬೭೮೯", "lao" => "໐໑໒໓໔໕໖໗໘໙", "malayalam" => "൦൧൨൩൪൫൬൭൮൯",
            "mongolian" => "᠐᠑᠒᠓᠔᠕᠖᠗᠘᠙", "myanmar" => "၀၁၂၃၄၅၆၇၈၉", "oriya" => "୦୧୨୩୪୫୬୭୮୯",
            "tamil" => "௦௧௨௩௪௫௬௭௮௯", "telugu" => "౦౧౨౩౪౫౬౭౮౯", "thai" => "๐๑๒๓๔๕๖๗๘๙",
            "tibetan" => "༠༡༢༣༤༥༦༧༨༩", _ => null,
        };
        if (numeric is not null) return new() { System = CssCounterSystem.Numeric, Symbols = Letters(numeric) };
        var alphabet = name switch
        {
            "lower-alpha" or "lower-latin" => "abcdefghijklmnopqrstuvwxyz", "upper-alpha" or "upper-latin" => "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
            "lower-greek" => "αβγδεζηθικλμνξοπρστυφχψω",
            "hiragana" => "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわゐゑをん",
            "katakana" => "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヰヱヲン",
            "hiragana-iroha" => "いろはにほへとちりぬるをわかよたれそつねならむうゐのおくやまけふこえてあさきゆめみしゑひもせす",
            "katakana-iroha" => "イロハニホヘトチリヌルヲワカヨタレソツネナラムウヰノオクヤマケフコエテアサキユメミシヱヒモセス", _ => null,
        };
        if (alphabet is not null) return new() { System = CssCounterSystem.Alphabetic, Symbols = Letters(alphabet) };
        if (name is "lower-roman" or "upper-roman") return new() { System = CssCounterSystem.Roman, UpperRoman = name == "upper-roman" };
        if (name is "armenian" or "upper-armenian" or "lower-armenian")
            return new() { System = CssCounterSystem.Additive, Ranges = [new(1, 9999)], Additive = ArmenianAdditive(name == "lower-armenian") };
        if (name == "georgian")
            return new() { System = CssCounterSystem.Additive, Ranges = [new(1, 19999)], Additive = GeorgianAdditive };
        if (name == "hebrew")
            return new() { System = CssCounterSystem.Additive, Ranges = [new(1, 10999)], Additive = HebrewAdditive };
        var predefined = name switch
        {
            "japanese-informal" => (CssPredefinedCounterAlgorithm.JapaneseInformal, "マイナス", "、", -9999, 9999, "cjk-decimal"),
            "japanese-formal" => (CssPredefinedCounterAlgorithm.JapaneseFormal, "マイナス", "、", -9999, 9999, "cjk-decimal"),
            "korean-hangul-formal" => (CssPredefinedCounterAlgorithm.KoreanHangulFormal, "마이너스 ", ", ", -9999, 9999, "cjk-decimal"),
            "korean-hanja-informal" => (CssPredefinedCounterAlgorithm.KoreanHanjaInformal, "마이너스 ", ", ", -9999, 9999, "cjk-decimal"),
            "korean-hanja-formal" => (CssPredefinedCounterAlgorithm.KoreanHanjaFormal, "마이너스 ", ", ", -9999, 9999, "cjk-decimal"),
            "simp-chinese-informal" => (CssPredefinedCounterAlgorithm.SimpChineseInformal, "负", "、", -9999, 9999, "cjk-decimal"),
            "simp-chinese-formal" => (CssPredefinedCounterAlgorithm.SimpChineseFormal, "负", "、", -9999, 9999, "cjk-decimal"),
            "trad-chinese-informal" => (CssPredefinedCounterAlgorithm.TradChineseInformal, "負", "、", -9999, 9999, "cjk-decimal"),
            "trad-chinese-formal" => (CssPredefinedCounterAlgorithm.TradChineseFormal, "負", "、", -9999, 9999, "cjk-decimal"),
            "ethiopic-numeric" => (CssPredefinedCounterAlgorithm.EthiopicNumeric, "-", "፦ ", 1, int.MaxValue, "decimal"),
            _ => default,
        };
        if (predefined.Item1 != CssPredefinedCounterAlgorithm.None)
            return new()
            {
                System = CssCounterSystem.Predefined,
                PredefinedAlgorithm = predefined.Item1,
                Negative = [predefined.Item2],
                Suffix = predefined.Item3,
                Ranges = [new(predefined.Item4, predefined.Item5)],
                Fallback = predefined.Item6,
            };
        var symbol = name switch { "disc" => "•", "circle" => "◦", "square" => "▪", "disclosure-open" => "▾", "disclosure-closed" => "▸", _ => null };
        if (symbol is not null) return new() { System = CssCounterSystem.Cyclic, Symbols = [symbol], Suffix = " " };
        if (name is "cjk-earthly-branch" or "cjk-heavenly-stem") return new()
        { System = CssCounterSystem.Fixed, Symbols = Letters(name == "cjk-earthly-branch" ? "子丑寅卯辰巳午未申酉戌亥" : "甲乙丙丁戊己庚辛壬癸"), Suffix = "、" };
        return new() { System = CssCounterSystem.Numeric, Symbols = Letters("0123456789"), PadWidth = name == "decimal-leading-zero" ? 2 : 0, PadSymbol = "0" };
    }
    private static string[] Letters(string text) => text.EnumerateRunes().Select(rune => rune.ToString()).ToArray();

    private static CssAdditiveSymbol[] ArmenianAdditive(bool lower)
    {
        var baseChar = lower ? '\u0561' : '\u0531';
        var weights = new[]
        {
            9000, 8000, 7000, 6000, 5000, 4000, 3000, 2000, 1000,
            900, 800, 700, 600, 500, 400, 300, 200, 100,
            90, 80, 70, 60, 50, 40, 30, 20, 10,
            9, 8, 7, 6, 5, 4, 3, 2, 1,
        };
        var result = new CssAdditiveSymbol[36];
        for (var i = 0; i < 36; i++)
            result[i] = new(weights[i], ((char)(baseChar + (35 - i))).ToString());
        return result;
    }

    private static readonly CssAdditiveSymbol[] GeorgianAdditive =
    [
        new(10000, "\u10F5"), new(9000, "\u10F0"), new(8000, "\u10EF"), new(7000, "\u10F4"), new(6000, "\u10EE"),
        new(5000, "\u10ED"), new(4000, "\u10EC"), new(3000, "\u10EB"), new(2000, "\u10EA"), new(1000, "\u10E9"),
        new(900, "\u10E8"), new(800, "\u10E7"), new(700, "\u10E6"), new(600, "\u10E5"), new(500, "\u10E4"),
        new(400, "\u10F3"), new(300, "\u10E2"), new(200, "\u10E1"), new(100, "\u10E0"),
        new(90, "\u10DF"), new(80, "\u10DE"), new(70, "\u10DD"), new(60, "\u10F2"), new(50, "\u10DC"),
        new(40, "\u10DB"), new(30, "\u10DA"), new(20, "\u10D9"), new(10, "\u10D8"),
        new(9, "\u10D7"), new(8, "\u10F1"), new(7, "\u10D6"), new(6, "\u10D5"), new(5, "\u10D4"),
        new(4, "\u10D3"), new(3, "\u10D2"), new(2, "\u10D1"), new(1, "\u10D0"),
    ];

    private static readonly CssAdditiveSymbol[] HebrewAdditive =
    [
        new(10000, "\u05D9\u05F3"), new(9000, "\u05D8\u05F3"), new(8000, "\u05D7\u05F3"), new(7000, "\u05D6\u05F3"), new(6000, "\u05D5\u05F3"),
        new(5000, "\u05D4\u05F3"), new(4000, "\u05D3\u05F3"), new(3000, "\u05D2\u05F3"), new(2000, "\u05D1\u05F3"), new(1000, "\u05D0\u05F3"),
        new(400, "\u05EA"), new(300, "\u05E9"), new(200, "\u05E8"), new(100, "\u05E7"),
        new(90, "\u05E6"), new(80, "\u05E4"), new(70, "\u05E2"), new(60, "\u05E1"), new(50, "\u05E0"),
        new(40, "\u05DE"), new(30, "\u05DC"), new(20, "\u05DB"),
        new(19, "\u05D9\u05D8"), new(18, "\u05D9\u05D7"), new(17, "\u05D9\u05D6"), new(16, "\u05D8\u05D6"), new(15, "\u05D8\u05D5"),
        new(10, "\u05D9"), new(9, "\u05D8"), new(8, "\u05D7"), new(7, "\u05D6"), new(6, "\u05D5"),
        new(5, "\u05D4"), new(4, "\u05D3"), new(3, "\u05D2"), new(2, "\u05D1"), new(1, "\u05D0"),
    ];
}
