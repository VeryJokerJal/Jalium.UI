using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal sealed partial class CssTextFlowLayout
    {
        private bool _hasContextualRuby;
        private static string RubyTextContent(IEnumerable<UIElement> elements)
        {
            var text = new StringBuilder();
            foreach (var element in elements) Append(element);
            return text.ToString();
            void InlineText(Inline inline)
            {
                if (inline is Run run) text.Append(run.Text);
                else if (inline is Span span) foreach (var child in span.Inlines) InlineText(child);
                else if (inline is InlineUIContainer { Child: { } child }) Append(child);
            }
            void Append(UIElement element)
            {
                if (element is CssGeneratedText or ICssAnonymousBox) return;
                if (element is TextBlock block)
                {
                    if (block._inlines is { Count: > 0 } inlines) foreach (var inline in inlines) InlineText(inline);
                    else text.Append(block.Text);
                }
                else if (element is ContentControl { Content: string content }) text.Append(content);
                else if (element is FrameworkElement host)
                    foreach (var child in new CssPanelChildren(host, includeInactive: true, expandContents: false)) Append(child);
            }
        }

        private sealed partial class RubyGroup
        {
            internal bool IsMerged;
            internal static RubyGroup MergeGroups(FrameworkElement container, IEnumerable<RubyGroup> groups)
            {
                var input = groups.ToArray(); var merged = new RubyGroup(container, input.SelectMany(group => group.Items).ToArray(), container) { IsMerged = true };
                foreach (var group in input) merged.Pieces.AddRange(group.Pieces);
                merged.Width = merged.Pieces.Sum(piece => piece.Size.Width);
                merged.Baseline = merged.Pieces.Select(piece => piece.Baseline).DefaultIfEmpty().Max();
                merged.Height = merged.Baseline + merged.Pieces.Select(piece => piece.Size.Height - piece.Baseline).DefaultIfEmpty().Max();
                return merged;
            }

            private IEnumerable<(UIElement Element, Rect Rect)> PlaceMerged(Rect area)
            {
                var align = CssWritingModeProperties.GetRubyAlign(Style);
                var gaps = new int[Pieces.Count]; var between = new bool[Pieces.Count];
                bool distribute = align is CssRubyAlign.SpaceBetween or CssRubyAlign.SpaceAround;
                for (int i = 0; i < Pieces.Count; i++)
                {
                    var piece = Pieces[i];
                    if (piece.Element is TextBlock text)
                    {
                        text.SetRubyJustification(distribute);
                        if (distribute && text.UsesNativeRubyContent && text._layoutLines.Count == 1 &&
                            text.TryGetJustifiedLine(text._layoutLines[0], text.GetContentWidth(piece.Size.Width) + 1, out var plan)) gaps[i] = plan.Gaps.Length;
                        else text.SetRubyJustification(false);
                    }
                    if (distribute && i > 0)
                    {
                        var before = RubyTextContent([Pieces[i - 1].Element]).EnumerateRunes().LastOrDefault();
                        var after = RubyTextContent([piece.Element]).EnumerateRunes().FirstOrDefault();
                        var method = (CssTextJustification)Style.GetValue(CssFlowProperties.TextJustifyProperty)!;
                        between[i] = before.Value != 0 && after.Value != 0 && !IsProhibitedLineEnd(before) && !IsProhibitedLineStart(after) &&
                            (method == CssTextJustification.InterCharacter && !IsCursiveJoiningPair(before, after) || method == CssTextJustification.Auto &&
                                !Rune.IsWhiteSpace(before) && !Rune.IsWhiteSpace(after) && (IsCjkLineBreakRune(before) || IsCjkLineBreakRune(after)) && RubyAutoOpportunity(before, after, Style, annotationContext: true));
                    }
                }
                int opportunities = gaps.Sum() + between.Count(value => value);
                double free = Math.Max(0, area.Width - Width);
                double each = distribute && opportunities > 0 ? free / (opportunities + (align == CssRubyAlign.SpaceAround ? 1 : 0)) : 0;
                double at = area.X + (align == CssRubyAlign.Start ? 0 : each == 0 ? free / 2 : align == CssRubyAlign.SpaceAround ? each / 2 : 0);
                bool rtl = CssWritingAxes.UsedDirection(Style) == FlowDirection.RightToLeft;
                for (int i = 0; i < Pieces.Count; i++)
                {
                    if (between[i]) at += each;
                    var piece = Pieces[i]; double width = piece.Size.Width + gaps[i] * each;
                    if (piece.Element is TextBlock text) text.SetRubyJustification(each > .000001 && gaps[i] > 0);
                    yield return (piece.Element, new(rtl ? area.Right - (at - area.X) - width : at,
                        area.Y + Baseline - piece.Baseline, width, piece.Size.Height));
                    at += width;
                }
            }
        }

        private sealed record RubyMergedRun(RubyGroup Group, int First, int Last, int Level);
        private sealed record RubyMergedLine(RubyCellBox[] Cells, RubyMergedRun[] Runs, Dictionary<int, (double Top, double Baseline)> Rows, double Height);

        // A segment supplies context-dependent advances to the ordinary line
        // fitter. It never joins the bases into one unbreakable synthetic word.
        private sealed class RubyMergeSegment(CssTextFlowLayout flow, FrameworkElement host, RubyGroup[] bases, RubyAnnotation[] annotations)
        {
            internal readonly List<RubyCellBox> Cells = [];
            internal readonly List<RubyMergedLine> Lines = [];

            internal void Layout(RubyCellBox[] cells, bool commit = false)
            {
                if (cells.Length == 0) return;
                int first = cells.Min(cell => cell.FirstBase), last = cells.Max(cell => cell.LastBase);
                var all = annotations.Where(note => note.First >= first && note.Last <= last).ToArray();
                var notes = all.Where(note => !note.Hidden).ToArray();
                var widths = Enumerable.Range(first, last - first + 1).ToDictionary(i => i, i => bases[i].Width);
                var runs = new List<RubyMergedRun>(); var merged = new HashSet<RubyAnnotation>();

                void AddRun(IEnumerable<RubyAnnotation> sequence)
                {
                    var row = sequence.ToArray(); if (row.Length == 0) return;
                    runs.Add(new(RubyGroup.MergeGroups(row[0].Group.AnnotationContainer ?? row[0].Group.Style, row.Select(note => note.Group)),
                        row[0].First, row[^1].Last, row[0].Level));
                    foreach (var note in row) merged.Add(note);
                }
                double SpanWidth(int a, int b) => Enumerable.Range(a, b-a+1).Sum(i => widths[i]) +
                    notes.Where(side => side.Group.InterCharacter && side.Last >= a && side.Last < b).Sum(side => side.Group.HostSize.Width);
                void Widen(int a, int b, double width)
                {
                    double extra = Math.Max(0, width - SpanWidth(a,b)) / (b-a+1);
                    for (int i = a; i <= b; i++) widths[i] += extra;
                }
                foreach (var row in notes.Where(note => !note.Group.InterCharacter && note.Merge == CssRubyMerge.Merge).GroupBy(note => note.Level)) AddRun(row);
                var constraints = notes.Where(note => !note.Group.InterCharacter && note.Merge == CssRubyMerge.Separate)
                    .Select(note => (note.First, note.Last, note.Group.Width)).Concat(runs.Select(run => (run.First,run.Last,run.Group.Width)));
                foreach (var constraint in constraints.OrderBy(c => c.Last-c.First)) Widen(constraint.First,constraint.Last,constraint.Width);

                foreach (var row in all.Where(note => !note.Group.InterCharacter && note.Merge == CssRubyMerge.Auto).GroupBy(note => note.Level))
                {
                    var sequence = new List<RubyAnnotation>();
                    void Flush()
                    {
                        if (sequence.Count == 0) return;
                        if (sequence.Any(note => note.Group.Width > SpanWidth(note.First,note.Last) + .000001))
                        {
                            AddRun(sequence); var run = runs[^1]; Widen(run.First,run.Last,run.Group.Width);
                        }
                        sequence.Clear();
                    }
                    foreach (var note in row) { if (note.Hidden) Flush(); else sequence.Add(note); }
                    Flush();
                }

                var interlinear = notes.Where(note => !note.Group.InterCharacter).GroupBy(note => note.Level).OrderBy(row => row.Key).ToArray();
                var rowMetrics = new Dictionary<int,(double Height,double Baseline)>();
                foreach (var row in interlinear)
                {
                    var groups = row.Where(note => !merged.Contains(note)).Select(note => note.Group).Concat(runs.Where(run => run.Level == row.Key).Select(run => run.Group)).ToArray();
                    double baseline = groups.Select(group => group.Baseline).DefaultIfEmpty().Max();
                    rowMetrics[row.Key] = (baseline + groups.Select(group => group.Height-group.Baseline).DefaultIfEmpty().Max(), baseline);
                }
                double above = 0, below = 0; bool? previousUnder = null;
                var rows = new Dictionary<int,(bool Under,double Offset)>();
                foreach (var row in interlinear)
                {
                    var position = CssWritingModeProperties.GetRubyPosition(row.First().Group.AnnotationContainer ?? row.First().Group.Style);
                    bool under = position is CssRubyPosition.Alternate or CssRubyPosition.AlternateUnder
                        ? previousUnder is { } previous ? !previous : position == CssRubyPosition.AlternateUnder : position == CssRubyPosition.Under;
                    previousUnder = under; rows[row.Key] = (under,under?below:above);
                    if (under) below += rowMetrics[row.Key].Height; else above += rowMetrics[row.Key].Height;
                }
                double baseBaseline = bases[first..(last+1)].Max(group => group.Baseline);
                double baseHeight = baseBaseline + bases[first..(last+1)].Max(group => group.Height-group.Baseline);
                var sides = notes.Where(note => note.Group.InterCharacter).OrderBy(note => note.Level).ToArray();
                (double Height,double Top) Side(RubyAnnotation note)
                {
                    var paired = bases[note.Last]; var height = note.Group.HostSize.Height;
                    if (note.Group.AutomaticPhysicalHeight) height = Math.Max(height,paired.Height);
                    return (height,baseBaseline-paired.Baseline + (CssWritingModeProperties.GetRubyAlign(note.Group.Style)==CssRubyAlign.Start?0:(paired.Height-height)/2));
                }
                double sideAbove=0,sideBelow=0,sideAscent=double.NegativeInfinity,sideDescent=double.NegativeInfinity;
                foreach (var note in sides)
                {
                    var (height,top)=Side(note);sideAbove=Math.Max(sideAbove,-top);sideBelow=Math.Max(sideBelow,top+height-baseHeight);
                    // Side annotations are inline blocks in the base level;
                    // their entire used box contributes even when it happens
                    // to equal the base font area and adds no outer overflow.
                    sideAscent=Math.Max(sideAscent,baseBaseline-top);
                    sideDescent=Math.Max(sideDescent,top+height-baseBaseline);
                }
                var line = flow.RubyLineBounds(host, bases[first..(last+1)], baseHeight, above+sideAbove, below+sideBelow,sideAscent,sideDescent);
                double totalHeight = line.Height, baseTop = line.Baseline-baseBaseline;
                var finalRows = rows.ToDictionary(pair => pair.Key, pair =>
                    (Top:pair.Value.Under?baseTop+baseHeight+sideBelow+pair.Value.Offset:baseTop-sideAbove-pair.Value.Offset-rowMetrics[pair.Key].Height,
                     Baseline:rowMetrics[pair.Key].Baseline));

                foreach (var cell in cells)
                {
                    cell.Tracks.Clear(); cell.Columns.Clear(); double x=0;
                    for(int i=cell.FirstBase;i<=cell.LastBase;i++)
                    {
                        cell.Columns[i]=new(x,0,widths[i],totalHeight);
                        cell.Tracks.Add((bases[i],new(x,line.Baseline-bases[i].Baseline,widths[i],bases[i].Height))); x+=widths[i];
                        foreach(var note in sides.Where(note=>note.Last==i))
                        {var (height,top)=Side(note);cell.Tracks.Add((note.Group,new(x,baseTop+top,note.Group.HostSize.Width,height)));x+=note.Group.HostSize.Width;}
                    }
                    foreach(var note in notes.Where(note=>!note.Group.InterCharacter&&!merged.Contains(note)&&note.First>=cell.FirstBase&&note.Last<=cell.LastBase))
                    {
                        var row=finalRows[note.Level];double left=cell.Columns[note.First].Left;
                        cell.Tracks.Add((note.Group,new(left,row.Top+row.Baseline-note.Group.Baseline,cell.Columns[note.Last].Right-left,note.Group.Height)));
                    }
                    cell.LogicalSize=new(x,totalHeight);cell.Baseline=line.Baseline;
                    cell.BaseLevelArea=new(0,baseTop-sideAbove,x,sideAbove+baseHeight+sideBelow);
                    if(cell.Source is {} source){source.AtomicSize=cell.LogicalSize;source.AtomicBaseline=cell.Baseline;}
                }
                if(commit) Lines.Add(new(cells,runs.ToArray(),finalRows,totalHeight));
            }
        }

        private void EmitRubyCells(RubyModel model,RubyGroup[] bases,RubyAnnotation[] notes,List<CssInlineUnit> pending,CssInlineFrame? frame,ref bool space)
        {
            _hasContextualRuby = true;
            var segment=new RubyMergeSegment(this,model.Host,bases,notes);model.MergeSegments.Add(segment);
            for(int first=0;first<bases.Length;)
            {
                int last=first;
                for(bool changed=true;changed;)
                {changed=false;foreach(var note in notes.Where(note=>!note.Hidden&&note.First<=last&&note.Last>=first))if(note.Last>last){last=note.Last;changed=true;}}
                var cell=new RubyCellBox(model.Host,Axes){IsHitTestVisible=false,Focusable=false,Frame=frame,FirstBase=first,LastBase=last,MergeSegment=segment};
                model.Host.AttachCssGeneratedChild(cell);model.Cells.Add(cell);segment.Cells.Add(cell);first=last+1;
            }
            segment.Layout(segment.Cells.ToArray());
            foreach(var cell in segment.Cells)
            {cell.Measure(Axes.Physical(cell.LogicalSize));AppendAtomic(cell,pending,ref space,frame);cell.Source=_sources[^1];cell.Source.ContextualAtomic=true;}
            if(notes.Any(note=>!note.Hidden&&note.Group.InterCharacter))
            {
                var side=new RubySegment();model.SideSegments.Add(side);side.Cells.AddRange(segment.Cells);
                foreach(var group in bases)if(group.BaseContainer is {} container)side.Containers.Add(container);
                foreach(var note in notes.Where(note=>!note.Hidden&&note.Group.InterCharacter))
                    if(note.Group.AnnotationContainer is {CssDisplayMode:CssDisplayMode.RubyTextContainer} container)side.Containers.Add(container);
            }
        }

        private static void PrepareRubyCells(IEnumerable<CssInlineSource> sources,bool commit)
        {
            foreach(var group in sources.Where(source=>source.ContextualAtomic).Select(source=>(RubyCellBox)source.Element).Distinct().GroupBy(cell=>cell.MergeSegment!))
                group.Key.Layout(group.OrderBy(cell=>cell.FirstBase).ToArray(),commit);
        }
        private void PrepareRubyLine(List<CssInlineUnit> units,int start,int end)
        { if (_hasContextualRuby) PrepareRubyCells(units.GetRange(start,end-start).Select(unit=>unit.Source),false); }
        private void CommitRubyLine(List<CssInlineFragment> fragments)
        { if (_hasContextualRuby) PrepareRubyCells(fragments.Select(fragment=>fragment.Source),true); }

        private static void ArrangeMergedRuby(RubyModel model,Dictionary<UIElement,Rect> placements)
        {
            foreach(var segment in model.MergeSegments)
            foreach(var line in segment.Lines)
            foreach(var run in line.Runs)
            {
                var bounds=Rect.Empty;
                foreach(var cell in line.Cells)
                {
                    if(cell.Source is not {Fragments.Count:>0}||!model.Host.GetRenderMatrix().TryInvert(out var inverse))continue;
                    var origin=Matrix.Multiply(cell.GetRenderMatrix(),inverse).Transform(new Point(0,0));
                    foreach(var (index,column) in cell.Columns)
                    {
                        if(index<run.First||index>run.Last)continue;
                        var rect=model.Axes.Physical(model.Axes.LinePlacement(column,0,line.Height),cell.RenderSize);rect.Offset(origin.X,origin.Y);bounds.Union(rect);
                    }
                }
                if(bounds.IsEmpty)continue;
                var row=line.Rows[run.Level];double width=model.Axes.Vertical?bounds.Height:bounds.Width;
                foreach(var (element,local) in run.Group.Place(new(0,row.Top+row.Baseline-run.Group.Baseline,width,run.Group.Height)))
                {
                    var rect=model.Axes.Physical(model.Axes.LinePlacement(local,0,line.Height),bounds.Size);rect.Offset(bounds.X,bounds.Y);placements[element]=rect;
                }
            }
        }
    }
}
