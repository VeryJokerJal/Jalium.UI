using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

public static partial class Css
{
    [ThreadStatic]
    private static int s_printScopeDepth;

    public static readonly DependencyProperty MediaTypeProperty =
        DependencyProperty.RegisterAttached(
            "MediaType", typeof(string), typeof(Css),
            new PropertyMetadata("screen", OnMediaTypeChanged, null, inherits: true));

    public static string GetMediaType(DependencyObject element)
        => (string)(element.GetValue(MediaTypeProperty) ?? "screen");

    public static void SetMediaType(DependencyObject element, string value)
        => element.SetValue(MediaTypeProperty, value);

    private static void OnMediaTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement or FrameworkContentElement)
        {
            CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(d));
        }

        if (d is FlowDocument doc)
        {
            doc.NotifyPaginationStyleChanged();
        }
        else
        {
            CssDocumentConsumers.InvalidateConsumersInSubtree(d);
        }
    }

    internal static bool IsPrintMedia(CssNode element)
    {
        if (s_printScopeDepth > 0) return true;
        for (var current = element; current is not null; current = current.FrameworkParent)
        {
            if (current.Target.GetValueSourceInternal(MediaTypeProperty).BaseValueSource != BaseValueSource.Default &&
                current.GetValue(MediaTypeProperty) is string localMedia)
            {
                return localMedia.Equals("print", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (element.GetValue(MediaTypeProperty) is string inherited &&
            inherited.Equals("print", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var root = CssMatcher.Root(element);
        if (root.Target is FlowDocument document)
        {
            foreach (var consumer in CssDocumentConsumers.EnumerateConsumers(document))
            {
                if (GetMediaType(consumer).Equals("print", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static IDisposable EnterPrintMedia(DependencyObject? target = null)
    {
        s_printScopeDepth++;
        if (target is FrameworkElement or FrameworkContentElement)
            CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(target));
        if (target is FlowDocument doc)
            doc.NotifyPaginationStyleChanged();
        return new PrintMediaScope(target);
    }

    private sealed class PrintMediaScope(DependencyObject? target) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            s_printScopeDepth = Math.Max(0, s_printScopeDepth - 1);
            if (target is FrameworkElement or FrameworkContentElement)
                CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(target));
            if (target is FlowDocument doc)
                doc.NotifyPaginationStyleChanged();
        }
    }
}

internal enum CssPageOrientation : byte
{
    Upright,
    RotateLeft,
    RotateRight,
}

internal enum CssPageSizeKind : byte
{
    Auto,
    Explicit,
    Portrait,
    Landscape,
}

internal readonly record struct CssPageSize(CssPageSizeKind Kind, CssLength Width, CssLength Height)
{
    internal static readonly CssPageSize Auto = new(CssPageSizeKind.Auto, default, default);

    internal static bool TryRead(ref CssTokenReader reader, out CssPageSize value)
    {
        value = Auto;
        var probe = reader;
        if (probe.TryReadIdent(out var firstIdent))
        {
            var first = firstIdent.ToString().ToLowerInvariant();
            if (first == "auto" && probe.AtEnd)
            {
                reader = probe;
                value = Auto;
                return true;
            }

            if (first is "portrait" or "landscape")
            {
                var orientation = first;
                if (probe.AtEnd)
                {
                    reader = probe;
                    value = new(orientation == "portrait" ? CssPageSizeKind.Portrait : CssPageSizeKind.Landscape, default, default);
                    return true;
                }

                if (probe.TryReadIdent(out var namedAfter) && probe.AtEnd &&
                    TryGetStandardPageSize(namedAfter, out var w1, out var h1))
                {
                    reader = probe;
                    var (rw, rh) = Orient(w1, h1, orientation == "landscape");
                    value = new(CssPageSizeKind.Explicit, new CssLength(rw, CssUnit.Px), new CssLength(rh, CssUnit.Px));
                    return true;
                }

                return false;
            }

            if (TryGetStandardPageSize(first, out var w2, out var h2))
            {
                var landscape = false;
                if (!probe.AtEnd)
                {
                    if (!probe.TryReadIdent(out var orientIdent) || !probe.AtEnd) return false;
                    var orient = orientIdent.ToString().ToLowerInvariant();
                    if (orient is not ("portrait" or "landscape")) return false;
                    landscape = orient == "landscape";
                }

                reader = probe;
                var (rw, rh) = Orient(w2, h2, landscape);
                value = new(CssPageSizeKind.Explicit, new CssLength(rw, CssUnit.Px), new CssLength(rh, CssUnit.Px));
                return true;
            }

            return false;
        }

        if (!reader.TryReadLength(out var width) || !IsValidPageLength(width)) return false;
        var height = width;
        if (!reader.AtEnd)
        {
            if (!reader.TryReadLength(out height) || !IsValidPageLength(height) || !reader.AtEnd)
                return false;
        }

        value = new(CssPageSizeKind.Explicit, width, height);
        return true;
    }

    private static bool IsValidPageLength(CssLength length)
    {
        if (length.Unit is CssUnit.Percent or CssUnit.Auto) return false;
        if (length.Expression is { UsesPercent: true }) return false;
        if (length.Expression is null && length.Value <= 0) return false;
        if (length.IsAbsolute && length.ToPxAbsolute() <= 0) return false;
        return true;
    }

    private static (double Width, double Height) Orient(double width, double height, bool landscape)
    {
        var min = Math.Min(width, height);
        var max = Math.Max(width, height);
        return landscape ? (max, min) : (min, max);
    }

    private static bool TryGetStandardPageSize(ReadOnlySpan<char> ident, out double widthPx, out double heightPx)
    {
        const double mm = 96.0 / 25.4;
        const double inch = 96.0;
        switch (ident.ToString().ToLowerInvariant())
        {
            case "a5": widthPx = 148 * mm; heightPx = 210 * mm; return true;
            case "a4": widthPx = 210 * mm; heightPx = 297 * mm; return true;
            case "a3": widthPx = 297 * mm; heightPx = 420 * mm; return true;
            case "b5": widthPx = 176 * mm; heightPx = 250 * mm; return true;
            case "b4": widthPx = 250 * mm; heightPx = 353 * mm; return true;
            case "jis-b5": widthPx = 182 * mm; heightPx = 257 * mm; return true;
            case "jis-b4": widthPx = 257 * mm; heightPx = 364 * mm; return true;
            case "letter": widthPx = 8.5 * inch; heightPx = 11.0 * inch; return true;
            case "legal": widthPx = 8.5 * inch; heightPx = 14.0 * inch; return true;
            case "ledger": widthPx = 11.0 * inch; heightPx = 17.0 * inch; return true;
            default: widthPx = 0; heightPx = 0; return false;
        }
    }

    internal Size Resolve(Size defaultSize, in CssLengthContext lengths)
    {
        var fallbackW = defaultSize.Width > 0 && double.IsFinite(defaultSize.Width) ? defaultSize.Width : 816.0;
        var fallbackH = defaultSize.Height > 0 && double.IsFinite(defaultSize.Height) ? defaultSize.Height : 1056.0;
        return Kind switch
        {
            CssPageSizeKind.Portrait => new Size(Math.Min(fallbackW, fallbackH), Math.Max(fallbackW, fallbackH)),
            CssPageSizeKind.Landscape => new Size(Math.Max(fallbackW, fallbackH), Math.Min(fallbackW, fallbackH)),
            CssPageSizeKind.Explicit => new Size(
                Width.TryResolve(lengths, CssPercentBasis.NotSupported, out var w) && w > 0 ? w : fallbackW,
                Height.TryResolve(lengths, CssPercentBasis.NotSupported, out var h) && h > 0 ? h : fallbackH),
            _ => new Size(fallbackW, fallbackH),
        };
    }
}

internal static class CssPageProperties
{
    internal static readonly DependencyProperty PageNameProperty =
        DependencyProperty.RegisterAttached(
            "CssPageName", typeof(string), typeof(CssPageProperties),
            new PropertyMetadata(string.Empty, Changed));

    internal static readonly DependencyProperty SizeProperty =
        DependencyProperty.RegisterAttached(
            "CssPageSize", typeof(CssPageSize), typeof(CssPageProperties),
            new PropertyMetadata(CssPageSize.Auto, Changed));

    internal static readonly DependencyProperty PageOrientationProperty =
        DependencyProperty.RegisterAttached(
            "CssPageOrientation", typeof(CssPageOrientation), typeof(CssPageProperties),
            new PropertyMetadata(CssPageOrientation.Upright, Changed));

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        if (target is FlowDocument document) document.NotifyPaginationStyleChanged();
        else if (target is TextElement text) text.NotifyTextContentChanged();
        else if (target is UIElement visual) { visual.InvalidateMeasure(); visual.InvalidateVisual(); }
    }

    internal static string Page(DependencyObject? source) => PageName(source);

    internal static string PageName(DependencyObject? source)
    {
        for (var current = source; current is not null; current = ParentOf(current))
        {
            if (current.GetValue(PageNameProperty) is string { Length: > 0 } name)
                return name;
        }
        return string.Empty;
    }

    private static DependencyObject? ParentOf(DependencyObject current) => current switch
    {
        FrameworkContentElement fce => fce.Parent,
        FrameworkElement fe => fe.FrameworkParent,
        _ => null,
    };

    internal static void Register()
    {
        CssPropertyRegistry.Register(new()
        {
            Name = "page",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = PageNameProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var text = ident.ToString();
                if (CssPropertyMetadata.IsWideKeyword(text)) return null;
                if (text.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    return new CssImmediateValue(PageNameProperty, string.Empty);
                return new CssImmediateValue(PageNameProperty, text);
            },
        });

        CssPropertyRegistry.Register(new()
        {
            Name = "size",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = SizeProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
                CssPageSize.TryRead(ref reader, out var size) && reader.AtEnd
                    ? new CssImmediateValue(SizeProperty, size)
                    : null,
        });

        CssPropertyRegistry.Register(new()
        {
            Name = "page-orientation",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = PageOrientationProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                CssPageOrientation? orientation = ident.ToString().ToLowerInvariant() switch
                {
                    "upright" => CssPageOrientation.Upright,
                    "rotate-left" => CssPageOrientation.RotateLeft,
                    "rotate-right" => CssPageOrientation.RotateRight,
                    _ => null,
                };
                return orientation is { } value ? new CssImmediateValue(PageOrientationProperty, value) : null;
            },
        });
    }
}

[Flags]
internal enum CssPagePseudo : byte
{
    None = 0,
    First = 1 << 0,
    Left = 1 << 1,
    Right = 1 << 2,
    Blank = 1 << 3,
}

internal sealed record CssPageSelector(string? PageName, CssPagePseudo Pseudos)
{
    internal int Specificity
    {
        get
        {
            var f = !string.IsNullOrEmpty(PageName) ? 1 : 0;
            var g = ((Pseudos & CssPagePseudo.First) != 0 ? 1 : 0) + ((Pseudos & CssPagePseudo.Blank) != 0 ? 1 : 0);
            var h = ((Pseudos & CssPagePseudo.Left) != 0 ? 1 : 0) + ((Pseudos & CssPagePseudo.Right) != 0 ? 1 : 0);
            return (f << 16) | (g << 8) | h;
        }
    }

    internal bool Matches(int pageIndex, string? pageName, bool isLeft, bool isBlank)
    {
        if (!string.IsNullOrEmpty(PageName) && !string.Equals(PageName, pageName ?? string.Empty, StringComparison.Ordinal))
            return false;
        if ((Pseudos & CssPagePseudo.First) != 0 && pageIndex != 0)
            return false;
        if ((Pseudos & CssPagePseudo.Left) != 0 && !isLeft)
            return false;
        if ((Pseudos & CssPagePseudo.Right) != 0 && isLeft)
            return false;
        if ((Pseudos & CssPagePseudo.Blank) != 0 && !isBlank)
            return false;
        return true;
    }

    internal static CssPageSelector[]? ParseList(ReadOnlySpan<char> prelude)
    {
        var stripped = CssParser.StripComments(prelude).Trim();
        if (stripped.IsEmpty)
            return [new CssPageSelector(null, CssPagePseudo.None)];

        var list = new List<CssPageSelector>();
        var start = 0;
        while (start <= stripped.Length)
        {
            var comma = stripped[start..].IndexOf(',');
            var segment = (comma < 0 ? stripped[start..] : stripped.Slice(start, comma)).Trim();
            if (segment.IsEmpty || !TryParseSingle(segment, out var selector) || selector is null)
                return null;
            list.Add(selector);
            if (comma < 0) break;
            start += comma + 1;
            if (start == stripped.Length) return null;
        }

        return list.Count > 0 ? list.ToArray() : null;
    }

    private static bool TryParseSingle(ReadOnlySpan<char> text, out CssPageSelector? selector)
    {
        selector = null;
        var pos = 0;
        string? pageName = null;
        if (pos < text.Length && text[pos] != ':')
        {
            var colonIdx = text.IndexOf(':');
            var identSpan = colonIdx < 0 ? text : text[..colonIdx];
            for (var i = 0; i < identSpan.Length; i++)
            {
                if (char.IsWhiteSpace(identSpan[i])) return false;
            }
            var reader = new CssTokenReader(identSpan);
            if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return false;
            var name = ident.ToString();
            if (CssPropertyMetadata.IsWideKeyword(name) || name.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return false;
            pageName = name;
            pos = identSpan.Length;
        }

        var pseudos = CssPagePseudo.None;
        while (pos < text.Length)
        {
            if (text[pos] != ':') return false;
            pos++;
            var nextColon = text[pos..].IndexOf(':');
            var pseudoSpan = nextColon < 0 ? text[pos..] : text.Slice(pos, nextColon);
            if (pseudoSpan.IsEmpty) return false;
            for (var i = 0; i < pseudoSpan.Length; i++)
            {
                if (char.IsWhiteSpace(pseudoSpan[i])) return false;
            }
            var pseudo = pseudoSpan.ToString().ToLowerInvariant() switch
            {
                "first" => CssPagePseudo.First,
                "left" or "verso" => CssPagePseudo.Left,
                "right" or "recto" => CssPagePseudo.Right,
                "blank" => CssPagePseudo.Blank,
                _ => CssPagePseudo.None,
            };
            if (pseudo == CssPagePseudo.None) return false;
            pseudos |= pseudo;
            pos += pseudoSpan.Length;
        }

        selector = new CssPageSelector(pageName, pseudos);
        return true;
    }
}

internal enum CssPageMarginBoxKind : byte
{
    TopLeftCorner,
    TopLeft,
    TopCenter,
    TopRight,
    TopRightCorner,
    BottomLeftCorner,
    BottomLeft,
    BottomCenter,
    BottomRight,
    BottomRightCorner,
    LeftTop,
    LeftMiddle,
    LeftBottom,
    RightTop,
    RightMiddle,
    RightBottom,
}

internal sealed record CssPageMarginRule(CssPageMarginBoxKind Kind, CssDeclaration[] Declarations);

internal sealed record CssPageRule(
    CssPageSelector[] Selectors,
    CssDeclaration[] Declarations,
    CssPageMarginRule[] MarginRules,
    int Offset,
    CssCondition? Condition = null,
    string? LayerName = null,
    int RuleOrder = 0)
{
    internal static CssPageRule? Parse(
        ReadOnlySpan<char> header,
        ReadOnlySpan<char> body,
        int offset,
        int bodyLine = 1,
        List<CssParseDiagnostic>? diagnostics = null)
    {
        var selectors = CssPageSelector.ParseList(header);
        if (selectors is null) return null;

        var declarations = new List<CssDeclaration>();
        var marginRules = new List<CssPageMarginRule>();
        var declBuffer = new StringBuilder();
        var pos = 0;
        var line = bodyLine;

        void FlushDeclarations()
        {
            if (declBuffer.Length == 0) return;
            var chunk = declBuffer.ToString();
            declBuffer.Clear();
            declarations.AddRange(CssParser.ParseInlineDeclarations(chunk, diagnostics));
        }

        while (pos < body.Length)
        {
            var c = body[pos];
            if (c == '\n') { line++; pos++; declBuffer.Append('\n'); continue; }
            if (char.IsWhiteSpace(c)) { pos++; declBuffer.Append(c); continue; }
            if (c == '/' && pos + 1 < body.Length && body[pos + 1] == '*')
            {
                pos += 2;
                while (pos + 1 < body.Length && !(body[pos] == '*' && body[pos + 1] == '/'))
                {
                    if (body[pos] == '\n') line++;
                    pos++;
                }
                if (pos + 1 < body.Length) pos += 2;
                continue;
            }

            if (c == '@')
            {
                FlushDeclarations();
                var atLine = line;
                pos++;
                var nameStart = pos;
                while (pos < body.Length && (char.IsLetterOrDigit(body[pos]) || body[pos] == '-'))
                    pos++;
                var name = body[nameStart..pos].ToString();
                var headerStart = pos;
                var brace = -1;
                var depth = 0;
                var inQuote = '\0';
                while (pos < body.Length)
                {
                    var ch = body[pos];
                    if (ch == '\n') line++;
                    if (inQuote != '\0')
                    {
                        if (ch == '\\' && pos + 1 < body.Length) { pos += 2; continue; }
                        if (ch == inQuote) inQuote = '\0';
                        pos++;
                        continue;
                    }
                    if (ch is '"' or '\'') { inQuote = ch; pos++; continue; }
                    if (ch == '/' && pos + 1 < body.Length && body[pos + 1] == '*')
                    {
                        pos += 2;
                        while (pos + 1 < body.Length && !(body[pos] == '*' && body[pos + 1] == '/'))
                        {
                            if (body[pos] == '\n') line++;
                            pos++;
                        }
                        if (pos + 1 < body.Length) pos += 2;
                        continue;
                    }
                    if (ch == '{')
                    {
                        if (depth == 0) brace = pos;
                        depth++;
                        pos++;
                        continue;
                    }
                    if (ch == '}')
                    {
                        depth--;
                        pos++;
                        if (depth <= 0) break;
                        continue;
                    }
                    if (ch == ';' && depth == 0)
                    {
                        pos++;
                        break;
                    }
                    pos++;
                }

                if (brace >= headerStart && pos > brace && body[pos - 1] == '}' &&
                    CssParser.StripComments(body[headerStart..brace]).Trim().IsEmpty &&
                    TryParseMarginBoxKind(name, out var marginKind))
                {
                    var marginDecls = CssParser.ParseInlineDeclarations(body[(brace + 1)..(pos - 1)].ToString(), diagnostics).ToArray();
                    marginRules.Add(new CssPageMarginRule(marginKind, marginDecls));
                }
                else
                {
                    diagnostics?.Add(new(CssDiagnosticSeverity.Warning, $"invalid @{name} rule inside @page", atLine));
                }
                continue;
            }

            // Read a single top-level declaration up to ';' (respecting quotes/parens)
            var inStr = '\0';
            var parens = 0;
            while (pos < body.Length)
            {
                var ch = body[pos];
                if (ch == '\n') line++;
                declBuffer.Append(ch);
                pos++;
                if (inStr != '\0')
                {
                    if (ch == '\\' && pos < body.Length) { declBuffer.Append(body[pos]); pos++; continue; }
                    if (ch == inStr) inStr = '\0';
                    continue;
                }
                if (ch is '"' or '\'') { inStr = ch; continue; }
                if (ch is '(' or '[') { parens++; continue; }
                if (ch is ')' or ']') { if (parens > 0) parens--; continue; }
                if (ch == ';' && parens == 0) break;
            }
        }

        FlushDeclarations();
        return new CssPageRule(selectors, declarations.ToArray(), marginRules.ToArray(), offset);
    }

    internal static bool TryParseMarginBoxKind(string name, out CssPageMarginBoxKind kind)
    {
        switch (name.ToLowerInvariant())
        {
            case "top-left-corner": kind = CssPageMarginBoxKind.TopLeftCorner; return true;
            case "top-left": kind = CssPageMarginBoxKind.TopLeft; return true;
            case "top-center": kind = CssPageMarginBoxKind.TopCenter; return true;
            case "top-right": kind = CssPageMarginBoxKind.TopRight; return true;
            case "top-right-corner": kind = CssPageMarginBoxKind.TopRightCorner; return true;
            case "bottom-left-corner": kind = CssPageMarginBoxKind.BottomLeftCorner; return true;
            case "bottom-left": kind = CssPageMarginBoxKind.BottomLeft; return true;
            case "bottom-center": kind = CssPageMarginBoxKind.BottomCenter; return true;
            case "bottom-right": kind = CssPageMarginBoxKind.BottomRight; return true;
            case "bottom-right-corner": kind = CssPageMarginBoxKind.BottomRightCorner; return true;
            case "left-top": kind = CssPageMarginBoxKind.LeftTop; return true;
            case "left-middle": kind = CssPageMarginBoxKind.LeftMiddle; return true;
            case "left-bottom": kind = CssPageMarginBoxKind.LeftBottom; return true;
            case "right-top": kind = CssPageMarginBoxKind.RightTop; return true;
            case "right-middle": kind = CssPageMarginBoxKind.RightMiddle; return true;
            case "right-bottom": kind = CssPageMarginBoxKind.RightBottom; return true;
            default: kind = default; return false;
        }
    }
}

internal sealed class CssComputedMarginBox
{
    public CssPageMarginBoxKind Kind { get; init; }
    public string Text { get; set; } = string.Empty;
    public Brush? Foreground { get; set; }
    public Brush? Background { get; set; }
    public Brush? BorderBrush { get; set; }
    public Thickness BorderThickness { get; set; }
    public Thickness Padding { get; set; }
    public double FontSize { get; set; } = 12.0;
    public FontFamily? FontFamily { get; set; }
    public FontWeight FontWeight { get; set; } = FontWeights.Normal;
    public FontStyle FontStyle { get; set; } = FontStyles.Normal;
    public TextAlignment? TextAlignment { get; set; }
}

internal sealed class CssComputedPageStyle
{
    public bool HasMatchedRules { get; init; }
    public Size Size { get; init; }
    public CssPageOrientation Orientation { get; init; }
    public Thickness Margin { get; init; }
    public Thickness Padding { get; init; }
    public Thickness BorderThickness { get; init; }
    public Brush? BorderBrush { get; init; }
    public Brush? Background { get; init; }
    public Brush? Foreground { get; init; }
    public double FontSize { get; init; } = 12.0;
    public FontFamily? FontFamily { get; init; }
    public IReadOnlyDictionary<CssPageMarginBoxKind, CssComputedMarginBox> MarginBoxes { get; init; }
        = new Dictionary<CssPageMarginBoxKind, CssComputedMarginBox>();

    public Thickness ContentInsets => new(
        Margin.Left + BorderThickness.Left + Padding.Left,
        Margin.Top + BorderThickness.Top + Padding.Top,
        Margin.Right + BorderThickness.Right + Padding.Right,
        Margin.Bottom + BorderThickness.Bottom + Padding.Bottom);
}

internal static class CssPageRules
{
    internal static bool IsActive;

    internal static List<(CssPageRule Rule, int[] LayerKey, int Order)> CollectRules(DependencyObject documentOrRoot)
    {
        var result = new List<(CssPageRule Rule, int[] LayerKey, int Order)>();

        var element = CssNode.Get(documentOrRoot);
        var root = CssMatcher.Root(element);
        var sheets = new List<CssStyleSheet>();
        var seenSheets = new HashSet<CssStyleSheet>(ReferenceEqualityComparer.Instance);

        void AddSheet(CssStyleSheet sheet)
        {
            if (seenSheets.Add(sheet)) sheets.Add(sheet);
        }

        if (CssEngine.ApplicationStyleSheetsProvider?.Invoke() is { } application)
        {
            foreach (var s in application) AddSheet(s);
        }

        if (root.Target is FlowDocument document)
        {
            foreach (var consumer in CssDocumentConsumers.EnumerateConsumers(document))
            {
                var consumerRoot = CssMatcher.Root(CssNode.Get(consumer));
                CollectScopedSheets(consumerRoot, AddSheet);
            }
        }

        CollectScopedSheets(root, AddSheet);

        var layers = new CssLayerOrder();
        var order = 0;
        foreach (var sheet in sheets)
        {
            foreach (var layer in sheet.Layers)
                if (layer.Condition is null || layer.Condition.Evaluate(root))
                    layers.GetKey(layer.Name);
            foreach (var rule in sheet.PageRules)
            {
                if (rule.Condition is not null && !rule.Condition.Evaluate(root))
                    continue;
                result.Add((rule, layers.GetKey(rule.LayerName), order++));
            }
        }

        return result;
    }

    private static void CollectScopedSheets(CssNode root, Action<CssStyleSheet> addSheet)
    {
        var pending = new Stack<CssNode>();
        var visited = new HashSet<CssNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node)) continue;
            if (node.Target.GetValue(Css.StyleSheetsProperty) is CssStyleSheetCollection direct)
            {
                foreach (var s in direct) addSheet(s);
            }
            else if (node.CssRuntimeState?.ScopedStyleSheets is { } scoped)
            {
                foreach (var s in scoped) addSheet(s);
            }
            foreach (var child in node.EnumerateChildren().Reverse())
                pending.Push(child);
        }
    }

    internal static CssComputedPageStyle ResolvePageStyle(
        DependencyObject documentOrRoot,
        int pageIndex,
        int pageCount,
        string? pageName,
        bool isLeft,
        bool isBlank,
        Size defaultSize,
        Thickness defaultMargin,
        in CssLengthContext lengths)
    {
        var collected = CollectRules(documentOrRoot);
        if (collected.Count == 0)
        {
            return new CssComputedPageStyle
            {
                HasMatchedRules = false,
                Size = new Size(
                    defaultSize.Width > 0 && double.IsFinite(defaultSize.Width) ? defaultSize.Width : 816.0,
                    defaultSize.Height > 0 && double.IsFinite(defaultSize.Height) ? defaultSize.Height : 1056.0),
                Orientation = CssPageOrientation.Upright,
                Margin = defaultMargin,
            };
        }

        var matched = new List<(CssPageRule Rule, int Specificity, int[] LayerKey, int Order)>();
        foreach (var (rule, layerKey, order) in collected)
        {
            var bestSpec = -1;
            foreach (var selector in rule.Selectors)
            {
                if (selector.Matches(pageIndex, pageName, isLeft, isBlank) && selector.Specificity > bestSpec)
                    bestSpec = selector.Specificity;
            }
            if (bestSpec >= 0)
                matched.Add((rule, bestSpec, layerKey, order));
        }

        if (matched.Count == 0)
        {
            return new CssComputedPageStyle
            {
                HasMatchedRules = false,
                Size = new Size(
                    defaultSize.Width > 0 && double.IsFinite(defaultSize.Width) ? defaultSize.Width : 816.0,
                    defaultSize.Height > 0 && double.IsFinite(defaultSize.Height) ? defaultSize.Height : 1056.0),
                Orientation = CssPageOrientation.Upright,
                Margin = defaultMargin,
            };
        }

        matched.Sort(static (a, b) =>
        {
            var layerCmp = CssLayerOrder.Compare(a.LayerKey, b.LayerKey);
            if (layerCmp != 0) return layerCmp;
            var specCmp = a.Specificity.CompareTo(b.Specificity);
            if (specCmp != 0) return specCmp;
            return a.Order.CompareTo(b.Order);
        });

        var pageSize = CssPageSize.Auto;
        var orientation = CssPageOrientation.Upright;
        double marginL = defaultMargin.Left, marginT = defaultMargin.Top, marginR = defaultMargin.Right, marginB = defaultMargin.Bottom;
        double padL = 0, padT = 0, padR = 0, padB = 0;
        double borderL = 0, borderT = 0, borderR = 0, borderB = 0;
        Brush? borderBrush = null;
        Brush? background = null;
        Brush? foreground = null;
        var fontSize = lengths.ElementFontSize > 0 ? lengths.ElementFontSize : 12.0;
        FontFamily? fontFamily = null;

        foreach (var passImportant in new[] { false, true })
        {
            foreach (var (rule, _, _, _) in matched)
            {
                foreach (var decl in rule.Declarations)
                {
                    if (decl.Important != passImportant) continue;
                    ApplyPageDeclaration(
                        decl.PropertyName,
                        decl.RawValue,
                        lengths,
                        ref pageSize,
                        ref orientation,
                        ref marginL, ref marginT, ref marginR, ref marginB,
                        ref padL, ref padT, ref padR, ref padB,
                        ref borderL, ref borderT, ref borderR, ref borderB,
                        ref borderBrush,
                        ref background,
                        ref foreground,
                        ref fontSize,
                        ref fontFamily);
                }
            }
        }

        var resolvedSize = pageSize.Resolve(defaultSize, lengths);
        if (orientation is CssPageOrientation.RotateLeft or CssPageOrientation.RotateRight)
        {
            resolvedSize = new Size(resolvedSize.Height, resolvedSize.Width);
        }

        var marginBoxes = new Dictionary<CssPageMarginBoxKind, CssComputedMarginBox>();
        var counterStyles = CssCounterStyles.For(CssNode.Get(documentOrRoot));

        foreach (var passImportant in new[] { false, true })
        {
            foreach (var (rule, _, _, _) in matched)
            {
                foreach (var marginRule in rule.MarginRules)
                {
                    if (!marginBoxes.TryGetValue(marginRule.Kind, out var box))
                    {
                        box = new CssComputedMarginBox
                        {
                            Kind = marginRule.Kind,
                            Foreground = foreground,
                            FontSize = fontSize,
                            FontFamily = fontFamily,
                        };
                        marginBoxes[marginRule.Kind] = box;
                    }

                    foreach (var decl in marginRule.Declarations)
                    {
                        if (decl.Important != passImportant) continue;
                        ApplyMarginBoxDeclaration(
                            box,
                            decl.PropertyName,
                            decl.RawValue,
                            pageIndex + 1,
                            Math.Max(1, pageCount),
                            counterStyles,
                            lengths);
                    }
                }
            }
        }

        // Remove margin boxes with empty content
        var emptyKeys = marginBoxes.Where(kv => string.IsNullOrEmpty(kv.Value.Text)).Select(kv => kv.Key).ToList();
        foreach (var k in emptyKeys) marginBoxes.Remove(k);

        return new CssComputedPageStyle
        {
            HasMatchedRules = true,
            Size = resolvedSize,
            Orientation = orientation,
            Margin = new Thickness(marginL, marginT, marginR, marginB),
            Padding = new Thickness(padL, padT, padR, padB),
            BorderThickness = new Thickness(borderL, borderT, borderR, borderB),
            BorderBrush = borderBrush,
            Background = background,
            Foreground = foreground,
            FontSize = fontSize,
            FontFamily = fontFamily,
            MarginBoxes = marginBoxes,
        };
    }

    private static void ApplyPageDeclaration(
        string name,
        string rawValue,
        in CssLengthContext lengths,
        ref CssPageSize pageSize,
        ref CssPageOrientation orientation,
        ref double marginL, ref double marginT, ref double marginR, ref double marginB,
        ref double padL, ref double padT, ref double padR, ref double padB,
        ref double borderL, ref double borderT, ref double borderR, ref double borderB,
        ref Brush? borderBrush,
        ref Brush? background,
        ref Brush? foreground,
        ref double fontSize,
        ref FontFamily? fontFamily)
    {
        var reader = new CssTokenReader(rawValue);
        switch (name.ToLowerInvariant())
        {
            case "size":
                if (CssPageSize.TryRead(ref reader, out var parsedSize) && reader.AtEnd)
                    pageSize = parsedSize;
                break;
            case "page-orientation":
                if (reader.TryReadIdent(out var orientIdent) && reader.AtEnd)
                {
                    orientation = orientIdent.ToString().ToLowerInvariant() switch
                    {
                        "rotate-left" => CssPageOrientation.RotateLeft,
                        "rotate-right" => CssPageOrientation.RotateRight,
                        _ => CssPageOrientation.Upright,
                    };
                }
                break;
            case "margin":
                if (TryReadBoxLengths(ref reader, lengths, out var mt, out var mr, out var mb, out var ml))
                { marginT = mt; marginR = mr; marginB = mb; marginL = ml; }
                break;
            case "margin-top":
            case "margin-block-start":
                if (TryReadSingleLength(ref reader, lengths, out var vmt)) marginT = vmt;
                break;
            case "margin-right":
            case "margin-inline-end":
                if (TryReadSingleLength(ref reader, lengths, out var vmr)) marginR = vmr;
                break;
            case "margin-bottom":
            case "margin-block-end":
                if (TryReadSingleLength(ref reader, lengths, out var vmb)) marginB = vmb;
                break;
            case "margin-left":
            case "margin-inline-start":
                if (TryReadSingleLength(ref reader, lengths, out var vml)) marginL = vml;
                break;
            case "margin-inline":
                if (TryReadPairLengths(ref reader, lengths, out var mis, out var mie)) { marginL = mis; marginR = mie; }
                break;
            case "margin-block":
                if (TryReadPairLengths(ref reader, lengths, out var mbs, out var mbe)) { marginT = mbs; marginB = mbe; }
                break;
            case "padding":
                if (TryReadBoxLengths(ref reader, lengths, out var pt, out var pr, out var pb, out var pl))
                { padT = pt; padR = pr; padB = pb; padL = pl; }
                break;
            case "padding-top":
            case "padding-block-start":
                if (TryReadSingleLength(ref reader, lengths, out var vpt)) padT = vpt;
                break;
            case "padding-right":
            case "padding-inline-end":
                if (TryReadSingleLength(ref reader, lengths, out var vpr)) padR = vpr;
                break;
            case "padding-bottom":
            case "padding-block-end":
                if (TryReadSingleLength(ref reader, lengths, out var vpb)) padB = vpb;
                break;
            case "padding-left":
            case "padding-inline-start":
                if (TryReadSingleLength(ref reader, lengths, out var vpl)) padL = vpl;
                break;
            case "border-width":
                if (TryReadBoxLengths(ref reader, lengths, out var bt, out var br, out var bb, out var bl))
                { borderT = bt; borderR = br; borderB = bb; borderL = bl; }
                break;
            case "border-color":
                if (TryReadBrush(ref reader, out var bc)) borderBrush = bc;
                break;
            case "border":
                TryReadBorderShorthand(ref reader, lengths, ref borderL, ref borderT, ref borderR, ref borderB, ref borderBrush);
                break;
            case "background":
            case "background-color":
                if (TryReadBrush(ref reader, out var bg)) background = bg;
                break;
            case "color":
                if (TryReadBrush(ref reader, out var fg)) foreground = fg;
                break;
            case "font-size":
                if (TryReadSingleLength(ref reader, lengths, out var fs) && fs > 0) fontSize = fs;
                break;
            case "font-family":
                if (!string.IsNullOrWhiteSpace(rawValue))
                    fontFamily = new FontFamily(rawValue.Trim('"', '\'', ' '));
                break;
        }
    }

    private static void ApplyMarginBoxDeclaration(
        CssComputedMarginBox box,
        string name,
        string rawValue,
        int pageNumber,
        int pageCount,
        CssCounterStyleMap counterStyles,
        in CssLengthContext lengths)
    {
        var reader = new CssTokenReader(rawValue);
        switch (name.ToLowerInvariant())
        {
            case "content":
                box.Text = EvaluateMarginBoxContent(rawValue, pageNumber, pageCount, counterStyles);
                break;
            case "color":
                if (TryReadBrush(ref reader, out var fg)) box.Foreground = fg;
                break;
            case "background":
            case "background-color":
                if (TryReadBrush(ref reader, out var bg)) box.Background = bg;
                break;
            case "border-color":
                if (TryReadBrush(ref reader, out var bc)) box.BorderBrush = bc;
                break;
            case "border":
                double bl = box.BorderThickness.Left, bt = box.BorderThickness.Top, br = box.BorderThickness.Right, bb = box.BorderThickness.Bottom;
                Brush? bBrush = box.BorderBrush;
                TryReadBorderShorthand(ref reader, lengths, ref bl, ref bt, ref br, ref bb, ref bBrush);
                box.BorderThickness = new Thickness(bl, bt, br, bb);
                box.BorderBrush = bBrush;
                break;
            case "padding":
                if (TryReadBoxLengths(ref reader, lengths, out var pt, out var pr, out var pb, out var pl))
                    box.Padding = new Thickness(pl, pt, pr, pb);
                break;
            case "font-size":
                if (TryReadSingleLength(ref reader, lengths, out var fs) && fs > 0) box.FontSize = fs;
                break;
            case "font-weight":
                if (reader.TryReadIdent(out var fwIdent))
                {
                    box.FontWeight = fwIdent.ToString().ToLowerInvariant() switch
                    {
                        "bold" or "bolder" => FontWeights.Bold,
                        "light" or "lighter" => FontWeights.Light,
                        _ => FontWeights.Normal,
                    };
                }
                else if (reader.TryReadInteger(out var fwNum))
                {
                    box.FontWeight = fwNum >= 600 ? FontWeights.Bold : FontWeights.Normal;
                }
                break;
            case "font-style":
                if (reader.TryReadIdent(out var fsIdent))
                {
                    box.FontStyle = fsIdent.ToString().ToLowerInvariant() switch
                    {
                        "italic" or "oblique" => FontStyles.Italic,
                        _ => FontStyles.Normal,
                    };
                }
                break;
            case "font-family":
                if (!string.IsNullOrWhiteSpace(rawValue))
                    box.FontFamily = new FontFamily(rawValue.Trim('"', '\'', ' '));
                break;
            case "text-align":
                if (reader.TryReadIdent(out var taIdent))
                {
                    box.TextAlignment = taIdent.ToString().ToLowerInvariant() switch
                    {
                        "left" or "start" => TextAlignment.Left,
                        "right" or "end" => TextAlignment.Right,
                        "center" => TextAlignment.Center,
                        "justify" => TextAlignment.Justify,
                        _ => box.TextAlignment,
                    };
                }
                break;
        }
    }

    private static string EvaluateMarginBoxContent(
        string rawValue,
        int pageNumber,
        int pageCount,
        CssCounterStyleMap counterStyles)
    {
        var reader = new CssTokenReader(rawValue);
        var probe = reader;
        if (probe.TryReadIdent(out var kw) && probe.AtEnd &&
            kw.ToString().ToLowerInvariant() is "none" or "normal")
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        while (!reader.AtEnd)
        {
            if (reader.TryReadString(out var str))
            {
                sb.Append(str);
                continue;
            }

            if (reader.TryReadFunction(out var fn, out var args))
            {
                if (fn.Equals("counter", StringComparison.OrdinalIgnoreCase) ||
                    fn.Equals("counters", StringComparison.OrdinalIgnoreCase))
                {
                    if (args.TryReadIdent(out var counterName))
                    {
                        var style = "decimal";
                        if (fn.Equals("counters", StringComparison.OrdinalIgnoreCase) && args.TryReadComma())
                        {
                            args.TryReadString(out _);
                        }
                        if (args.TryReadComma())
                        {
                            if (!CssCounterStyles.ReadReference(ref args, out style))
                                style = "decimal";
                        }

                        var cname = counterName.ToString();
                        var val = cname.Equals("page", StringComparison.OrdinalIgnoreCase) ? pageNumber :
                                  cname.Equals("pages", StringComparison.OrdinalIgnoreCase) ? pageCount : 0;
                        sb.Append(counterStyles.Format(val, style));
                    }
                    continue;
                }
            }

            // Skip unrecognized token safely so we don't infinite loop
            break;
        }

        return sb.ToString();
    }

    private static bool TryResolvePageLength(CssLength len, in CssLengthContext lengths, out double px)
    {
        if (len.Unit == CssUnit.Percent)
        {
            px = len.Value / 100.0 * 816.0;
            return double.IsFinite(px);
        }
        return len.TryResolve(lengths, CssPercentBasis.NotSupported, out px);
    }

    private static bool TryReadSingleLength(ref CssTokenReader reader, in CssLengthContext lengths, out double px)
    {
        px = 0;
        if (!reader.TryReadLength(out var len) || !reader.AtEnd) return false;
        return TryResolvePageLength(len, lengths, out px);
    }

    private static bool TryReadPairLengths(ref CssTokenReader reader, in CssLengthContext lengths, out double first, out double second)
    {
        first = 0; second = 0;
        if (!reader.TryReadLength(out var l1) || !TryResolvePageLength(l1, lengths, out first))
            return false;
        second = first;
        if (!reader.AtEnd)
        {
            if (!reader.TryReadLength(out var l2) || !reader.AtEnd || !TryResolvePageLength(l2, lengths, out second))
                return false;
        }
        return true;
    }

    private static bool TryReadBoxLengths(
        ref CssTokenReader reader,
        in CssLengthContext lengths,
        out double top, out double right, out double bottom, out double left)
    {
        top = right = bottom = left = 0;
        Span<double> parts = stackalloc double[4];
        var count = 0;
        while (!reader.AtEnd)
        {
            if (count == 4 || !reader.TryReadLength(out var len) ||
                !TryResolvePageLength(len, lengths, out parts[count]))
                return false;
            count++;
        }
        if (count == 0) return false;
        (top, right, bottom, left) = count switch
        {
            1 => (parts[0], parts[0], parts[0], parts[0]),
            2 => (parts[0], parts[1], parts[0], parts[1]),
            3 => (parts[0], parts[1], parts[2], parts[1]),
            _ => (parts[0], parts[1], parts[2], parts[3]),
        };
        return true;
    }

    private static bool TryReadBrush(ref CssTokenReader reader, out Brush? brush)
    {
        brush = null;
        if (!CssColorParser.TryParse(ref reader, out var color, out _) || !reader.AtEnd) return false;
        var solid = new SolidColorBrush(color);
        if (solid.CanFreeze) solid.Freeze();
        brush = solid;
        return true;
    }

    private static void TryReadBorderShorthand(
        ref CssTokenReader reader,
        in CssLengthContext lengths,
        ref double left, ref double top, ref double right, ref double bottom,
        ref Brush? brush)
    {
        while (!reader.AtEnd)
        {
            var probe = reader;
            if (probe.TryReadLength(out var len) && len.TryResolve(lengths, CssPercentBasis.NotSupported, out var w))
            {
                left = top = right = bottom = Math.Max(0, w);
                reader = probe;
                continue;
            }
            probe = reader;
            if (CssColorParser.TryParse(ref probe, out var color, out _))
            {
                var solid = new SolidColorBrush(color);
                if (solid.CanFreeze) solid.Freeze();
                brush = solid;
                reader = probe;
                continue;
            }
            probe = reader;
            if (probe.TryReadIdent(out var ident))
            {
                var kw = ident.ToString().ToLowerInvariant();
                if (kw == "thin") { left = top = right = bottom = 1; reader = probe; continue; }
                if (kw == "medium") { left = top = right = bottom = 3; reader = probe; continue; }
                if (kw == "thick") { left = top = right = bottom = 5; reader = probe; continue; }
                if (kw is "none" or "hidden") { left = top = right = bottom = 0; reader = probe; continue; }
                reader = probe;
                continue;
            }
            break;
        }
    }
}
