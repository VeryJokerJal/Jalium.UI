using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Parses a subset of the <see href="https://mermaid.js.org/">mermaid</see> diagram syntax
/// into a <see cref="MermaidDocument"/>. Flowcharts (<c>flowchart</c> / <c>graph</c>) and pie
/// charts (<c>pie</c>) are supported; other diagram types return
/// <see cref="MermaidDiagramKind.Unknown"/> so callers can fall back to showing the source.
/// </summary>
public static class MermaidParser
{
    // Connector token immediately following a node reference, with an optional |label|.
    private static readonly Regex s_connector = new(
        @"\G\s*(?<tok>-\.+->|-\.+-|={2,}>|={2,}|-{2,}o|-{2,}x|<-->|-{2,}>|-{2,})\s*(?:\|(?<lbl>[^|]*)\|)?\s*",
        RegexOptions.Compiled);

    // Inline edge labels of the form "A -- text --> B"; normalized to the pipe form.
    private static readonly Regex s_inlineDotted = new(@"-\.\s+(.+?)\s+\.->", RegexOptions.Compiled);
    private static readonly Regex s_inlineThick = new(@"==\s+(.+?)\s+==>", RegexOptions.Compiled);
    private static readonly Regex s_inlineSolid = new(@"--\s+(.+?)\s+-->", RegexOptions.Compiled);

    /// <summary>
    /// Parses the supplied mermaid source.
    /// </summary>
    public static MermaidDocument Parse(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return new MermaidDocument { Error = "Empty mermaid source." };
        }

        var lines = SplitLines(source);
        var header = FirstMeaningfulLine(lines, out var headerIndex);
        if (header == null)
        {
            return new MermaidDocument { Error = "No mermaid content." };
        }

        var lower = header.TrimStart().ToLowerInvariant();
        if (lower.StartsWith("flowchart", StringComparison.Ordinal) ||
            lower.StartsWith("graph", StringComparison.Ordinal))
        {
            return ParseFlowchart(header, lines, headerIndex);
        }

        if (lower.StartsWith("pie", StringComparison.Ordinal))
        {
            return ParsePie(header, lines, headerIndex);
        }

        return new MermaidDocument { Error = $"Unsupported mermaid diagram: '{FirstWord(lower)}'." };
    }

    #region Shared helpers

    private static string[] SplitLines(string source)
        => source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static string? FirstMeaningfulLine(string[] lines, out int index)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = StripComment(lines[i]).Trim();
            if (trimmed.Length > 0)
            {
                index = i;
                return lines[i];
            }
        }

        index = -1;
        return null;
    }

    private static string StripComment(string line)
    {
        var idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line.Substring(0, idx) : line;
    }

    private static string FirstWord(string s)
    {
        var space = s.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? s : s.Substring(0, space);
    }

    #endregion

    #region Flowchart

    private static MermaidDocument ParseFlowchart(string header, string[] lines, int headerIndex)
    {
        var model = new MermaidFlowchartModel
        {
            Direction = ParseDirection(header)
        };

        var nodeLookup = new Dictionary<string, FlowchartNode>(StringComparer.Ordinal);

        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            var raw = StripComment(lines[i]).Trim();
            if (raw.Length == 0)
            {
                continue;
            }

            // Skip directives we do not render; keep the diagram robust against richer sources.
            if (IsFlowchartDirective(raw))
            {
                continue;
            }

            var statement = PreprocessInlineLabels(raw.TrimEnd(';'));
            ParseFlowStatement(statement, model, nodeLookup);
        }

        if (model.Nodes.Count == 0)
        {
            return new MermaidDocument { Error = "Flowchart contained no nodes." };
        }

        return new MermaidDocument { Kind = MermaidDiagramKind.Flowchart, Flowchart = model };
    }

    private static bool IsFlowchartDirective(string line)
    {
        var lower = line.ToLowerInvariant();
        return lower.StartsWith("subgraph", StringComparison.Ordinal)
            || lower == "end"
            || lower.StartsWith("direction ", StringComparison.Ordinal)
            || lower.StartsWith("classdef", StringComparison.Ordinal)
            || lower.StartsWith("class ", StringComparison.Ordinal)
            || lower.StartsWith("style ", StringComparison.Ordinal)
            || lower.StartsWith("linkstyle", StringComparison.Ordinal)
            || lower.StartsWith("click ", StringComparison.Ordinal);
    }

    private static FlowchartDirection ParseDirection(string header)
    {
        var parts = header.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return FlowchartDirection.TopToBottom;
        }

        return parts[1].ToUpperInvariant() switch
        {
            "LR" => FlowchartDirection.LeftToRight,
            "RL" => FlowchartDirection.RightToLeft,
            "BT" => FlowchartDirection.BottomToTop,
            "TB" or "TD" => FlowchartDirection.TopToBottom,
            _ => FlowchartDirection.TopToBottom
        };
    }

    private static string PreprocessInlineLabels(string line)
    {
        line = s_inlineDotted.Replace(line, "-.->|$1|");
        line = s_inlineThick.Replace(line, "==>|$1|");
        line = s_inlineSolid.Replace(line, "-->|$1|");
        return line;
    }

    private static void ParseFlowStatement(string line, MermaidFlowchartModel model,
        Dictionary<string, FlowchartNode> lookup)
    {
        var i = 0;
        if (!TryReadNodeRef(line, ref i, model, lookup, out var previous))
        {
            return;
        }

        while (i < line.Length)
        {
            var match = s_connector.Match(line, i);
            if (!match.Success || match.Index != i)
            {
                break;
            }

            i += match.Length;

            var tok = match.Groups["tok"].Value;
            var style = tok.Contains('.', StringComparison.Ordinal)
                ? FlowchartEdgeStyle.Dotted
                : tok.Contains('=', StringComparison.Ordinal)
                    ? FlowchartEdgeStyle.Thick
                    : FlowchartEdgeStyle.Solid;
            var hasArrow = tok.EndsWith('>') || tok.Contains('o', StringComparison.Ordinal) || tok.Contains('x', StringComparison.Ordinal);
            var label = match.Groups["lbl"].Success ? CleanLabel(match.Groups["lbl"].Value) : null;

            if (!TryReadNodeRef(line, ref i, model, lookup, out var next))
            {
                break;
            }

            model.Edges.Add(new FlowchartEdge(previous!.Id, next!.Id, label, style, hasArrow));
            previous = next;
        }
    }

    private static bool TryReadNodeRef(string line, ref int i, MermaidFlowchartModel model,
        Dictionary<string, FlowchartNode> lookup, out FlowchartNode? node)
    {
        node = null;

        while (i < line.Length && char.IsWhiteSpace(line[i]))
        {
            i++;
        }

        var start = i;
        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
        {
            i++;
        }

        if (i == start)
        {
            return false;
        }

        var id = line.Substring(start, i - start);
        var hasShape = false;
        var shape = FlowchartNodeShape.Rectangle;
        string? label = null;

        if (i < line.Length && (line[i] == '[' || line[i] == '(' || line[i] == '{' || line[i] == '>'))
        {
            if (TryReadShape(line, ref i, out shape, out var rawLabel))
            {
                hasShape = true;
                label = CleanLabel(rawLabel);
            }
        }

        node = Register(model, lookup, id, label, shape, hasShape);
        return true;
    }

    private static bool TryReadShape(string line, ref int i, out FlowchartNodeShape shape, out string label)
    {
        shape = FlowchartNodeShape.Rectangle;
        label = string.Empty;

        var open = line[i];
        int contentStart;
        string closer;

        switch (open)
        {
            case '>':
                shape = FlowchartNodeShape.Asymmetric;
                contentStart = i + 1;
                closer = "]";
                break;

            case '[':
                if (Peek(line, i + 1) == '[') { shape = FlowchartNodeShape.Subroutine; contentStart = i + 2; closer = "]]"; }
                else if (Peek(line, i + 1) == '(') { shape = FlowchartNodeShape.Cylinder; contentStart = i + 2; closer = ")]"; }
                else if (Peek(line, i + 1) == '/') { contentStart = i + 2; (shape, closer) = ResolveSlashShape(line, contentStart, slashOpen: true); }
                else if (Peek(line, i + 1) == '\\') { contentStart = i + 2; (shape, closer) = ResolveSlashShape(line, contentStart, slashOpen: false); }
                else { shape = FlowchartNodeShape.Rectangle; contentStart = i + 1; closer = "]"; }
                break;

            case '(':
                if (Peek(line, i + 1) == '(') { shape = FlowchartNodeShape.Circle; contentStart = i + 2; closer = "))"; }
                else if (Peek(line, i + 1) == '[') { shape = FlowchartNodeShape.Stadium; contentStart = i + 2; closer = "])"; }
                else { shape = FlowchartNodeShape.RoundedRectangle; contentStart = i + 1; closer = ")"; }
                break;

            case '{':
                if (Peek(line, i + 1) == '{') { shape = FlowchartNodeShape.Hexagon; contentStart = i + 2; closer = "}}"; }
                else { shape = FlowchartNodeShape.Rhombus; contentStart = i + 1; closer = "}"; }
                break;

            default:
                return false;
        }

        var closeIndex = FindCloser(line, contentStart, closer);
        if (closeIndex < 0)
        {
            return false;
        }

        label = line.Substring(contentStart, closeIndex - contentStart);
        i = closeIndex + closer.Length;
        return true;
    }

    private static (FlowchartNodeShape shape, string closer) ResolveSlashShape(string line, int contentStart, bool slashOpen)
    {
        // [/ ... /]  -> Parallelogram      [/ ... \] -> Trapezoid
        // [\ ... \]  -> Parallelogram      [\ ... /] -> Trapezoid
        var slashClose = FindCloser(line, contentStart, "/]");
        var backClose = FindCloser(line, contentStart, "\\]");

        if (slashOpen)
        {
            if (slashClose >= 0 && (backClose < 0 || slashClose <= backClose))
            {
                return (FlowchartNodeShape.Parallelogram, "/]");
            }
            return (FlowchartNodeShape.Trapezoid, "\\]");
        }

        if (backClose >= 0 && (slashClose < 0 || backClose <= slashClose))
        {
            return (FlowchartNodeShape.Parallelogram, "\\]");
        }
        return (FlowchartNodeShape.Trapezoid, "/]");
    }

    private static char Peek(string line, int index)
        => index >= 0 && index < line.Length ? line[index] : '\0';

    private static int FindCloser(string line, int from, string closer)
    {
        var inQuote = false;
        for (var j = from; j <= line.Length - closer.Length; j++)
        {
            var c = line[j];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && line.AsSpan(j, closer.Length).SequenceEqual(closer))
            {
                return j;
            }
        }

        return -1;
    }

    private static FlowchartNode Register(MermaidFlowchartModel model, Dictionary<string, FlowchartNode> lookup,
        string id, string? label, FlowchartNodeShape shape, bool hasShape)
    {
        if (lookup.TryGetValue(id, out var existing))
        {
            if (hasShape)
            {
                existing.Shape = shape;
            }
            if (!string.IsNullOrEmpty(label))
            {
                existing.Label = label;
            }
            return existing;
        }

        var node = new FlowchartNode(id, string.IsNullOrEmpty(label) ? null : label,
            hasShape ? shape : FlowchartNodeShape.Rectangle);
        lookup[id] = node;
        model.Nodes.Add(node);
        return node;
    }

    private static string CleanLabel(string raw)
    {
        var text = raw.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            text = text.Substring(1, text.Length - 2);
        }

        text = text.Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
                   .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
                   .Replace("&quot;", "\"", StringComparison.Ordinal)
                   .Replace("&amp;", "&", StringComparison.Ordinal)
                   .Replace("&lt;", "<", StringComparison.Ordinal)
                   .Replace("&gt;", ">", StringComparison.Ordinal);
        return text.Trim();
    }

    #endregion

    #region Pie

    private static MermaidDocument ParsePie(string header, string[] lines, int headerIndex)
    {
        var model = new MermaidPieModel();

        var headerRest = header.Trim().Substring(3).Trim(); // after "pie"
        if (headerRest.StartsWith("showData", StringComparison.OrdinalIgnoreCase))
        {
            model.ShowData = true;
            headerRest = headerRest.Substring("showData".Length).Trim();
        }
        if (headerRest.StartsWith("title", StringComparison.OrdinalIgnoreCase))
        {
            model.Title = headerRest.Substring("title".Length).Trim();
        }

        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            var raw = StripComment(lines[i]).Trim();
            if (raw.Length == 0)
            {
                continue;
            }

            if (raw.StartsWith("title", StringComparison.OrdinalIgnoreCase) &&
                (raw.Length == 5 || char.IsWhiteSpace(raw[5])))
            {
                model.Title = raw.Substring(5).Trim();
                continue;
            }

            if (raw.Equals("showData", StringComparison.OrdinalIgnoreCase))
            {
                model.ShowData = true;
                continue;
            }

            if (TryParsePieSlice(raw, out var slice))
            {
                model.Slices.Add(slice);
            }
        }

        if (model.Slices.Count == 0)
        {
            return new MermaidDocument { Error = "Pie chart contained no data." };
        }

        return new MermaidDocument { Kind = MermaidDiagramKind.Pie, Pie = model };
    }

    private static bool TryParsePieSlice(string line, out MermaidPieSlice slice)
    {
        slice = null!;

        var colon = line.LastIndexOf(':');
        if (colon <= 0 || colon >= line.Length - 1)
        {
            return false;
        }

        var labelPart = line.Substring(0, colon).Trim();
        var valuePart = line.Substring(colon + 1).Trim();

        if (labelPart.Length >= 2 && labelPart[0] == '"' && labelPart[^1] == '"')
        {
            labelPart = labelPart.Substring(1, labelPart.Length - 2);
        }

        if (!double.TryParse(valuePart, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        slice = new MermaidPieSlice(labelPart, value);
        return true;
    }

    #endregion
}
