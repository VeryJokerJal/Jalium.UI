using System.Text;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private sealed record CssInlineBidiEdge(int First, double Left, double Right);
    private sealed record CssInlineBidiRequest(int Start, int End, int Consumed, double Origin,
        CssInlineBidiEdge[] Edges, int[] ShapingBreaks, double? JustifyWidth);
    private sealed record CssInlineBidiExterior(SharedShapeExterior[] Before, SharedShapeExterior[] After);

    private static bool NeedsInlineAutospace(FrameworkElement owner, IEnumerable<TextBlock> sources)
    {
        var texts = sources.ToArray();
        bool ideograph = false, alpha = false, numeric = false, punctuation = false;
        foreach (var rune in texts.SelectMany(text => text._visualText.EnumerateRunes()))
        {
            ideograph |= IsAutospaceIdeograph(rune);
            alpha |= Rune.IsLetter(rune) && !IsEastAsianWideLetter(rune);
            numeric |= Rune.IsDigit(rune) && !CssUnicodeLineBreakData.IsEastAsianFullwidth(rune);
            punctuation |= rune.Value is ';' or '!' or '?' or ':' or 0x00ab or 0x00bb;
        }
        bool Enabled(DependencyObject element)
        {
            var mode = (CssTextAutospace)element.GetValue(CssFlowProperties.TextAutospaceProperty)!;
            return ideograph && (alpha && (mode & CssTextAutospace.IdeographAlpha) != 0 ||
                numeric && (mode & CssTextAutospace.IdeographNumeric) != 0) ||
                punctuation && (mode & CssTextAutospace.Punctuation) != 0 && UsesFrenchPunctuation(element);
        }
        return Enabled(owner) || texts.Any(text => text._usesAutospaceShaping || Enabled(text) ||
            text.GetInlineTextRanges().Any(range => Enabled(range.Run)));
    }

    private sealed partial class CssInlineBidiPlan
    {
        private sealed record AdvanceCell(int Index, int First, int End, CssInlineBidiInput Input,
            UIElement PaintOwner, double Left, double Right, bool Rtl, bool Tab, bool Empty)
        {
            internal double Width => Math.Max(0, Right - Left);
        }

        private CssInlineBidiNative? ApplyContextualAdvances(CssBidiProjection projection,
            NativeMethods.ParagraphAdvanceUnit[] units, NativeTextParagraph paragraph, CssInlineBidiRequest request,
            Func<NativeMethods.ParagraphAdvanceUnit[]?, NativeTextParagraph?> make)
        {
            var sources = _sources.Keys.Where(source => !source.Atomic && !source.EmptyFrame).Select(source => source.Text).Distinct();
            bool autospace = NeedsInlineAutospace(_owner, sources);
            bool tabs = _text.AsSpan(Inputs[request.Start].First,
                Inputs[request.End - 1].End - Inputs[request.Start].First).Contains('\t');
            bool zeroAtoms = _sources.Keys.Any(source => source.Atomic && !source.EmptyFrame && source.AtomicSize.Width == 0);
            if (!autospace && !tabs && !zeroAtoms && request.JustifyWidth is null)
                return new(this, projection, paragraph, request);

            var baseUnits=units.ToArray();
            var indices = units.Select((unit, index) => (unit.TextPosition, index)).ToDictionary(pair => pair.TextPosition, pair => pair.index);
            var metadata = new AutospaceUnit[units.Length];
            var raw = new List<(int Index, int First, int End, CssInlineBidiInput Input, UIElement PaintOwner)>();
            var boundaries = GraphemeClusters.GetBoundaries(_text);
            int firstRaw = Inputs[request.Start].First, lastRaw = Inputs[request.End - 1].End;
            for (int i = 0; i + 1 < boundaries.Length; i++)
            {
                int first = boundaries[i], end = boundaries[i + 1];
                if (first < firstRaw || end > lastRaw) continue;
                var range = projection.NativeRange(first, end - first);
                if (!indices.TryGetValue((uint)range.Start, out int index) || range.Length == 0) continue;
                var input = Inputs.First(input => input.End > first);
                var paintOwner = Inputs.First(input => input.End >= end).Unit.Source.Element;
                var source = input.Unit.Source;
                // An expanded source character can contain several displayed
                // spacing/justification cells while retaining one public range.
                for (; index < units.Length && units[index].TextPosition < range.Start + range.Length; index++)
                {
                    if ((units[index].Flags & 1) != 0 && !source.EmptyFrame) continue;
                    Rune.TryGetRuneAt(projection.Text, (int)units[index].TextPosition, out var rune);
                    metadata[index] = source.Atomic || source.EmptyFrame
                        ? new(source.Element, rune, 0, false)
                        : source.Text.AutospaceMetadata(SourcePosition(source, first), rune);
                    if (!source.Atomic && !source.EmptyFrame && source.Text is not InlineSourceText &&
                        InlineRunAt(source.Text.GetInlineTextRanges(), SourcePosition(source, first)) is null)
                        metadata[index] = metadata[index] with { Owner = AutospaceStyleSource(source.Element) };
                    raw.Add((index, first, end, input, paintOwner));
                }
            }
            if (raw.Count == 0) return new(this, projection, paragraph, request);

            // Resolve neighbors from unspaced glyphs in the same paragraph.
            // Retain neutral atomic widths; only text spacing is removed.
            var zero = units.ToArray();
            foreach (var cell in raw.Where(cell => !cell.Input.Unit.Source.Atomic ||
                !cell.Input.Unit.Source.EmptyFrame && cell.Input.Unit.Source.AtomicSize.Width == 0))
            {
                zero[cell.Index].Spacing = zero[cell.Index].Leading = zero[cell.Index].Trailing = 0;
                zero[cell.Index].Flags &= 3;
            }
            bool naturalAlready = !zeroAtoms && raw.Where(cell => !cell.Input.Unit.Source.Atomic).All(cell =>
                units[cell.Index].Spacing == 0 && units[cell.Index].Leading == 0 && units[cell.Index].Trailing == 0);
            var natural = naturalAlready ? paragraph : make(zero);
            using var naturalOwner = naturalAlready ? null : natural;
            if (natural is null) return null;
            var naturalView = new CssInlineBidiNative(this, projection, natural);
            uint row = Row(naturalView, firstRaw);
            var ordering = new Dictionary<int,CssInlineBidiOrder>();
            AdvanceCell Cell((int Index, int First, int End, CssInlineBidiInput Input, UIElement PaintOwner) item,
                NativeTextParagraph layout)
            {
                var unit = units[item.Index];
                double a = layout.Caret(row, (int)unit.TextPosition, false).X;
                double b = layout.Caret(row, (int)(unit.TextPosition + unit.Length), true).X;
                return new(item.Index, item.First, item.End, item.Input, item.PaintOwner,
                    Math.Min(a, b), Math.Max(a, b), ordering.GetValueOrDefault(item.First)?.Rtl ?? b < a,
                    _text[item.First] == '\t', item.Input.Unit.Source.EmptyFrame);
            }
            var cells = raw.Select(item => Cell(item, natural)).OrderBy(cell => cell.Left).ThenBy(cell => cell.First).ToArray();
            for (int i = 0; i < cells.Length; i++) ordering[cells[i].First] = new(i,cells[i].Rtl);
            // Measured neutral glyphs determine order, while CSS uses exact
            // object widths, including zero. No fictitious advance reaches paint.
            cells = cells.Select(cell => cell.Input.Unit.Source.Atomic && !cell.Empty
                ? cell with { Right = cell.Left + cell.Input.Unit.Source.AtomicSize.Width } : cell).ToArray();
            VisualAutoGap[] gaps = [];
            if (autospace && !_fontSource.ApplyVisualAutospace(cells.Where(cell => !cell.Empty)
                .Select(cell => (cell.Index, cell.Left, cell.Right, cell.Rtl)).ToList(), units, metadata, _owner, out gaps)) return null;

            if (tabs)
            {
                var edgeMap = request.Edges.ToDictionary(edge => edge.First);
                var displayGroups = cells.GroupBy(cell => cell.First).ToDictionary(group => group.Key, group =>
                    (Left: group.OrderBy(cell => cell.Left).ThenBy(cell => cell.Index).First().Index,
                     Right: group.OrderBy(cell => cell.Right).ThenBy(cell => cell.Index).Last().Index));
                CssInlineBidiEdge? Edge(AdvanceCell cell)
                {
                    if (!edgeMap.TryGetValue(cell.First, out var edge)) return null;
                    var group = displayGroups[cell.First];
                    return new(cell.First, cell.Index == group.Left ? edge.Left : 0, cell.Index == group.Right ? edge.Right : 0);
                }
                var left = new double[cells.Length]; var right = new double[cells.Length];
                bool Active(int i) => !cells[i].Empty && (!cells[i].Tab ||
                    cells[i].Input.Unit.Source.Text.GetPreservedTabAdvance(0, cells[i].Input.Unit.Start, _owner) > 0);
                var active = Enumerable.Range(0, cells.Length).Where(Active).ToArray();
                foreach (int i in active)
                {
                    var cell = cells[i]; var unit = units[cell.Index];
                    if(cell.Input.Unit.Source.Atomic){unit.Leading=unit.Trailing=unit.Spacing=0;}
                    double a = cell.Rtl ? unit.Trailing : unit.Leading, b = cell.Rtl ? unit.Leading : unit.Trailing;
                    if ((unit.Flags & 4) != 0 && cell.Width <= .000001) a = b = 0;
                    if (a + b < -cell.Width) { double scale = -cell.Width / (a + b); a *= scale; b *= scale; }
                    left[i] = a; right[i] = b;
                }
                var own = left.Zip(right, (a, b) => a + b).ToArray();
                for (int at = 1; at < active.Length; at++)
                {
                    int before = active[at - 1], after = active[at]; var a = cells[before]; var b = cells[after];
                    if ((units[a.Index].Flags & units[b.Index].Flags & 2) != 0) continue;
                    double requested = ((double)units[a.Index].Spacing + units[b.Index].Spacing) / 2;
                    double actual = Math.Max(a.Tab || b.Tab ? 0 : -Math.Min(a.Width + own[before], b.Width + own[after]), requested);
                    double scale = requested < 0 && actual != requested ? actual / requested : 1;
                    right[before] += units[a.Index].Spacing / 2.0 * scale;
                    left[after] += units[b.Index].Spacing / 2.0 * scale;
                }
                bool rtl = Direction(naturalView, firstRaw) == FlowDirection.RightToLeft;
                var reading = Enumerable.Range(0, cells.Length).OrderBy(i => rtl ? -i : i).ToArray();
                double position = request.Origin;
                for (int at = 0; at < reading.Length; at++)
                {
                    int i = reading[at]; var cell = cells[i]; var edge = Edge(cell);
                    double before = rtl ? edge?.Right ?? 0 : edge?.Left ?? 0;
                    double after = rtl ? edge?.Left ?? 0 : edge?.Right ?? 0;
                    if (cell.Tab)
                    {
                        double advance = 0;
                        if (Active(i))
                        {
                            double nextPad = 0;
                            for (int nextAt = at + 1; nextAt < reading.Length; nextAt++)
                            {
                                int next = reading[nextAt]; var nextEdge = Edge(cells[next]);
                                nextPad += rtl ? nextEdge?.Right ?? 0 : nextEdge?.Left ?? 0;
                                if (Active(next)) { nextPad += rtl ? right[next] : left[next]; break; }
                                nextPad += rtl ? nextEdge?.Left ?? 0 : nextEdge?.Right ?? 0;
                            }
                            advance = cell.Input.Unit.Source.Text.GetPreservedTabAdvance(
                                position + before + left[i] + right[i] + after + nextPad, cell.Input.Unit.Start, _owner);
                        }
                        if (rtl) left[i] += advance - cell.Width; else right[i] += advance - cell.Width;
                    }
                    double neutralPadding = cell.Empty
                        ? BidiAtomicAdvance(cell.Input.Unit.Source) - cell.Input.Unit.Source.AtomicSize.Width : 0;
                    position += before + cell.Width - neutralPadding + left[i] + right[i] + after;
                    int index = cell.Index;
                    units[index].Spacing = 0;
                    units[index].Leading = (float)(cell.Rtl ? right[i] : left[i])+(cell.Input.Unit.Source.Atomic?baseUnits[index].Leading:0);
                    units[index].Trailing = (float)(cell.Rtl ? left[i] : right[i])+(cell.Input.Unit.Source.Atomic?baseUnits[index].Trailing:0);
                    units[index].Flags = cell.Empty ? 1u : 0u;
                }
            }
            if (!tabs && gaps.Length == 0 && request.JustifyWidth is null && units.Zip(baseUnits).All(pair =>
                pair.First.Spacing == pair.Second.Spacing && pair.First.Leading == pair.Second.Leading &&
                pair.First.Trailing == pair.Second.Trailing && pair.First.Flags == pair.Second.Flags))
                return new(this, projection, paragraph, request,Ordering:ordering);
            var spaced = make(units);
            if (spaced is null) return null;
            paragraph.Dispose(); paragraph = spaced;
            bool expanded = false;
            if (request.JustifyWidth is { } target)
            {
                var visual = raw.Select(item => Cell(item, paragraph)).Where(cell => !cell.Empty)
                    .OrderBy(cell => cell.Left).ThenBy(cell => ordering[cell.First].Ordinal).ToArray();
                bool rtl = Direction(naturalView, firstRaw) == FlowDirection.RightToLeft;
                int lastTab = rtl ? Array.FindIndex(visual, cell => cell.Tab) : Array.FindLastIndex(visual, cell => cell.Tab);
                int visibleEnd = lastRaw;
                while (visibleEnd > firstRaw && IsBreakableWhitespace(_text[visibleEnd - 1])) visibleEnd--;
                var opportunities = new List<(AdvanceCell Left, AdvanceCell Right, bool Separator)>();
                for (int i = 0; i < visual.Length; i++)
                {
                    var cell = visual[i]; var rune = metadata[cell.Index].Rune;
                    if (cell.Tab || cell.First >= visibleEnd || (lastTab >= 0 && (rtl ? i >= lastTab : i <= lastTab))) continue;
                    var own = (CssTextJustification)metadata[cell.Index].Owner.GetValue(CssFlowProperties.TextJustifyProperty)!;
                    if (i > 0 && i + 1 < visual.Length && IsWordSeparator(rune) && cell.Width > .000001 &&
                        own is CssTextJustification.Auto or CssTextJustification.InterWord)
                        opportunities.Add((cell, cell, true));
                    if (i == 0) continue;
                    var previous = visual[i - 1]; var before = metadata[previous.Index].Rune;
                    if (previous.Input.Unit.Source.Atomic && cell.Input.Unit.Source.Atomic) continue;
                    if (previous.Tab || previous.First >= visibleEnd || IsProhibitedLineEnd(before) || IsProhibitedLineStart(rune) ||
                        (lastTab >= 0 && !rtl && i - 1 <= lastTab)) continue;
                    var owner = CommonAutospaceOwner(metadata[previous.Index].Owner, metadata[cell.Index].Owner, _owner);
                    var method = (CssTextJustification)owner.GetValue(CssFlowProperties.TextJustifyProperty)!;
                    if (method == CssTextJustification.InterCharacter && !IsCursiveJoiningPair(before, rune) ||
                        method == CssTextJustification.Auto && !Rune.IsWhiteSpace(before) && !Rune.IsWhiteSpace(rune) &&
                        (IsCjkLineBreakRune(before) || IsCjkLineBreakRune(rune)) && RubyAutoOpportunity(before, rune, owner)) opportunities.Add((previous, cell, false));
                }
                double slack = target - paragraph.Lines[row].Metrics.Line.Width;
                if (opportunities.Count > 0 && double.IsFinite(slack) && slack > .001 && slack / opportunities.Count <= 1000000)
                {
                    float half = (float)(slack / opportunities.Count / 2);
                    bool preserveCaret = tabs || visual.Any(cell => cell.Input.Unit.Source.Atomic);
                    foreach (var opportunity in opportunities)
                    {
                        var a = opportunity.Left; var b = opportunity.Right;
                        if (opportunity.Separator) { units[a.Index].Leading += half; units[a.Index].Trailing += half; }
                        else
                        {
                            // A tab ruler keeps the established source caret at the
                            // end of each reading-order character. Assign the gap
                            // to that character rather than splitting its caret.
                            float left = preserveCaret ? (rtl ? 0 : 2 * half) : half;
                            float right = preserveCaret ? (rtl ? 2 * half : 0) : half;
                            if (a.Rtl) units[a.Index].Leading += left; else units[a.Index].Trailing += left;
                            if (b.Rtl) units[b.Index].Trailing += right; else units[b.Index].Leading += right;
                        }
                    }
                    var justified = make(units);
                    if (justified is null) { paragraph.Dispose(); return null; }
                    paragraph.Dispose(); paragraph = justified; expanded = true;
                }
            }
            var exterior = new Dictionary<int, CssInlineBidiExterior>();
            var beforeGaps = raw.ToDictionary(cell => cell.Index, _ => new List<SharedShapeExterior>());
            var afterGaps = raw.ToDictionary(cell => cell.Index, _ => new List<SharedShapeExterior>());
            foreach (var gap in gaps)
            {
                if (gap.Replacement >= 0)
                {
                    var cell = raw.First(cell => cell.Index == gap.Replacement);
                    if (!ContainsAutospaceOwner(cell.PaintOwner, gap.Owner))
                    {
                        double width = Cell(cell, paragraph).Width / 2;
                        beforeGaps[cell.Index].Add(new(gap.Owner, width)); afterGaps[cell.Index].Add(new(gap.Owner, width));
                    }
                }
                else
                {
                    var a = raw.First(cell => cell.Index == gap.Left); var b = raw.First(cell => cell.Index == gap.Right);
                    if (!ContainsAutospaceOwner(a.PaintOwner, gap.Owner)) afterGaps[a.Index].Add(new(gap.Owner, gap.Width / 2));
                    if (!ContainsAutospaceOwner(b.PaintOwner, gap.Owner)) beforeGaps[b.Index].Add(new(gap.Owner, gap.Width / 2));
                }
            }
            foreach (var item in raw)
            {
                double requested = beforeGaps[item.Index].Sum(gap => gap.Width) + afterGaps[item.Index].Sum(gap => gap.Width);
                if (requested <= 0) continue;
                double scale = Math.Min(1, Cell(item, paragraph).Width / requested);
                var previous = exterior.GetValueOrDefault(item.First);
                exterior[item.First] = new([..previous?.Before??[], ..beforeGaps[item.Index].Select(gap => gap with { Width = gap.Width * scale })],
                    [..previous?.After??[], ..afterGaps[item.Index].Select(gap => gap with { Width = gap.Width * scale })]);
            }
            var atomicSpacing=new Dictionary<int,(double Left,double Right)>();
            foreach(var item in raw.Where(item=>item.Input.Unit.Source.Atomic))
            {
                var cell=Cell(item,paragraph);
                double leading=units[item.Index].Leading-baseUnits[item.Index].Leading;
                double trailing=units[item.Index].Trailing-baseUnits[item.Index].Trailing;
                atomicSpacing[item.First]=cell.Rtl?(trailing,leading):(leading,trailing);
            }
            return new(this, projection, paragraph, request, exterior, expanded,atomicSpacing,ordering);
        }
    }

    internal sealed partial class CssTextFlowLayout
    {
        private static CssInlineBidiEdge[] InlineBidiEdges(List<CssInlineFragment> fragments, List<CssInlineBidiCell> cells,CssInlineBidiNative native)
        {
            var edges = new Dictionary<int, (double Left, double Right)>();
            foreach (var fragment in fragments)
            {
                var own = cells.Where(cell => ReferenceEquals(cell.Map.Original.Source, fragment.Source) &&
                    (fragment.Shape is not CssInlineBidiPart part || cell.End > part.First && cell.First < part.End)).OrderBy(cell => cell.Left).ToArray();
                if (own.Length == 0) continue;
                double before = fragment.Left + fragment.MarginLeft + fragment.OuterLeft +
                    ((fragment.BidiDirection ?? (fragment.Source.RightToLeft?FlowDirection.RightToLeft:FlowDirection.LeftToRight)) == FlowDirection.RightToLeft ? fragment.Line.EndPadding : fragment.Line.StartPadding);
                double after = fragment.Right + fragment.MarginRight + fragment.OuterRight +
                    ((fragment.BidiDirection ?? (fragment.Source.RightToLeft?FlowDirection.RightToLeft:FlowDirection.LeftToRight)) == FlowDirection.RightToLeft ? fragment.Line.StartPadding : fragment.Line.EndPadding);
                if (fragment.Shape is CssInlineBidiPart shaped)
                { before -= shaped.Before.Sum(gap => gap.Width); after -= shaped.After.Sum(gap => gap.Width); }
                if(fragment.Source.Atomic&&native.AtomicSpacing?.TryGetValue(own[0].First,out var atomic)==true)
                {before-=atomic.Left;after-=atomic.Right;}
                var a = edges.GetValueOrDefault(own[0].First); edges[own[0].First] = (a.Left + before, a.Right);
                var b = edges.GetValueOrDefault(own[^1].First); edges[own[^1].First] = (b.Left, b.Right + after);
            }
            return edges.OrderBy(pair => pair.Key).Select(pair => new CssInlineBidiEdge(pair.Key, pair.Value.Left, pair.Value.Right)).ToArray();
        }

        private bool TryJustifyInlineBidiFragments(List<CssInlineFragment> fragments, double available, out bool expanded)
        {
            expanded = false;
            var parts = fragments.Select(fragment => fragment.Shape).OfType<CssInlineBidiPart>().ToArray();
            if (parts.Length == 0 || parts[0].Native.Request is not { } request ||
                parts.Any(part => !ReferenceEquals(part.Native, parts[0].Native))) return false;
            var originals = parts.Select(part => part.Map).Distinct().OrderBy(map => map.First).Select(map => map.Original).ToList();
            foreach(var fragment in fragments.Where(fragment=>fragment.Source.Atomic||fragment.Source.EmptyFrame))
            {
                var original=fragment.Copy();
                int raw=parts[0].Native.Plan.Inputs.First(input=>ReferenceEquals(input.Unit.Source,fragment.Source)).First;
                if(parts[0].Native.AtomicSpacing?.TryGetValue(raw,out var atomic)==true)
                {original.MarginLeft-=atomic.Left;original.MarginRight-=atomic.Right;}
                originals.Add(original);
            }
            double edges = fragments.Sum(fragment => fragment.Width - fragment.Line.Width + fragment.MarginLeft + fragment.MarginRight -
                (fragment.Shape is CssInlineBidiPart part ? part.Before.Sum(gap => gap.Width) + part.After.Sum(gap => gap.Width) : 0));
            foreach(var fragment in fragments.Where(fragment=>fragment.Source.Atomic))
            {
                int raw=parts[0].Native.Plan.Inputs.First(input=>ReferenceEquals(input.Unit.Source,fragment.Source)).First;
                if(parts[0].Native.AtomicSpacing?.TryGetValue(raw,out var atomic)==true)edges-=atomic.Left+atomic.Right;
            }
            double target = available - edges + fragments.Sum(fragment => fragment.HangingWhitespace) +
                fragments.Where(fragment => fragment.Source.EmptyFrame).Sum(fragment =>
                    BidiAtomicAdvance(fragment.Source) - fragment.Source.AtomicSize.Width);
            if (target <= 0 || !TryInlineBidiPieces(originals, _inlineBidi!.Units, request.Start, request.End, request.Consumed,
                request.Origin, target, request.Edges, request.ShapingBreaks)) return true;
            expanded = originals.Any(fragment => fragment.Shape is CssInlineBidiPart { Native.Expanded: true });
            if (expanded)
            {
                ApplyHangingWhitespace(_inlineBidi.Units, request.Start, request.End, originals, available);
                fragments.Clear(); fragments.AddRange(originals);
            }
            return true;
        }
    }
}
