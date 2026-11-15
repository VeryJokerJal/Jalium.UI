using Jalium.UI.Interop;
using System.Text;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool SupportedCssBidiPaint()
        => TryGetCssBidiPaint(out _);

    private sealed partial class CssBidiSpacingMetrics
    {
        private readonly Dictionary<(int Start,int End,double Origin),double> _autospaceExtras = [];
        private sealed record AutospaceLayout(double Extra,NativeMethods.ParagraphAdvanceUnit[] Units);

        private double AutospaceExtra(int start,int end,double origin)
        {
            if (_autospaceExtras.TryGetValue((start,end,origin),out var cached)) return cached;
            double value = AutospacePlan(start,end,origin).Extra;
            if (_autospaceExtras.Count >= 4096) _autospaceExtras.Clear();
            _autospaceExtras[(start,end,origin)] = value;return value;
        }

        // Used-row neighbors come from the paragraph's bidi resolution. Only
        // these source units participate: a virtual boundary never leaks over
        // a soft/forced break or changes the underlying author text.
        private AutospaceLayout AutospacePlan(int start,int end,double origin)
        {
            var output = new List<NativeMethods.ParagraphAdvanceUnit>();double extra = 0;
            int first = FirstAt(start),last = FirstAt(end);
            foreach (var row in _cells.AsSpan(first,last-first).ToArray().Where(cell=>cell.End<=end).GroupBy(cell=>cell.Row))
            {
                var cells = row.OrderBy(cell=>cell.Left).ToArray();
                var units = cells.Select(cell=>new NativeMethods.ParagraphAdvanceUnit
                {
                    TextPosition=(uint)cell.NativeFirst,Length=(uint)cell.NativeLength,Spacing=(float)cell.Letter,
                    Leading=(float)(cell.RequestedWord/2),Trailing=(float)(cell.RequestedWord/2),
                    Flags=(cell.Cursive?2u:0u)|(IsWordSeparator(cell.Autospace.Rune)||cell.Autospace.Replacement?4u:0u)
                }).ToArray();
                var metadata = cells.Select(cell=>cell.Autospace).ToArray();
                var ordered = cells.Select((cell,index)=>(Index:index,cell.Left,cell.Right,cell.Rtl)).ToList();
                if (!_block.ApplyVisualAutospace(ordered,units,metadata,_block,out _))
                    throw new InvalidOperationException("The CSS autospace advance is outside the supported range.");
                bool tabs = cells.Any(cell=>cell.Tab);
                var active = Enumerable.Range(0,cells.Length).Where(i=>!cells[i].Tab||_block.GetPreservedTabAdvance(0,cells[i].First)>0).ToArray();
                var left = new double[cells.Length];var right = new double[cells.Length];
                foreach(int i in active)
                {
                    double a=cells[i].Rtl?units[i].Trailing:units[i].Leading;
                    double b=cells[i].Rtl?units[i].Leading:units[i].Trailing;
                    if ((units[i].Flags&4)!=0&&cells[i].Natural<=.000001) a=b=0;
                    double requested=a+b;
                    if(requested < -cells[i].Natural){double scale=-cells[i].Natural/requested;a*=scale;b*=scale;}
                    left[i]=a;right[i]=b;
                }
                var own = left.Zip(right,(a,b)=>a+b).ToArray();
                for(int at=1;at<active.Length;at++)
                {
                    int before=active[at-1],after=active[at];var a=cells[before];var b=cells[after];
                    if(a.Cursive&&b.Cursive)continue;
                    double requested=((double)units[before].Spacing+units[after].Spacing)/2;
                    double limit=a.Tab||b.Tab?0:-Math.Min(a.Natural+own[before],b.Natural+own[after]);
                    double actual=Math.Max(limit,requested),scale=requested<0&&actual!=requested?actual/requested:1;
                    right[before]+=units[before].Spacing/2.0*scale;left[after]+=units[after].Spacing/2.0*scale;
                }
                bool rtl=tabs&&cells.First(cell=>cell.Tab).Rtl;double position=origin;
                for(int at=0;at<active.Length;at++)
                {
                    int activeIndex=rtl?active.Length-1-at:at;int i=active[activeIndex];var cell=cells[i];
                    if(cell.Tab)
                    {
                        int nextIndex=rtl?activeIndex-1:activeIndex+1;
                        int next=nextIndex>=0&&nextIndex<active.Length?active[nextIndex]:-1;
                        double nextPad=next>=0?rtl?right[next]:left[next]:0;
                        double advance=_block.GetPreservedTabAdvance(position+left[i]+right[i]+nextPad,cell.First);
                        if(rtl)left[i]+=advance-cell.Natural;else right[i]+=advance-cell.Natural;
                    }
                    position+=cell.Natural+left[i]+right[i];
                    if(tabs)
                    {
                        units[i].Spacing=0;units[i].Leading=(float)(cell.Rtl?right[i]:left[i]);
                        units[i].Trailing=(float)(cell.Rtl?left[i]:right[i]);units[i].Flags=0;
                    }
                }
                if(tabs)
                    for(int i=0;i<cells.Length;i++)
                        if(cells[i].Tab&&!active.Contains(i))units[i]=new(){TextPosition=units[i].TextPosition,Length=units[i].Length,Trailing=(float)-cells[i].Natural};
                extra+=position-origin-cells.Sum(cell=>cell.Natural);output.AddRange(units);
            }
            return new(extra,output.ToArray());
        }

        internal NativeMethods.ParagraphAdvanceUnit[] AutospaceUnits(NativeMethods.ParagraphAdvanceUnit[] units)
        {
            var indices=units.Select((unit,index)=>(unit.TextPosition,index)).ToDictionary(pair=>pair.TextPosition,pair=>pair.index);
            foreach(var line in _block._layoutLines)
            {
                double width=_block.GetContentWidth(_block.TextContentSize.Width);
                double origin=_block.ResolveTextIndent(line.IndentApplies,width)+_block.GetLineStartPadding(line.StartIndex,line.StartIndex+line.Length);
                foreach(var cell in AutospacePlan(line.StartIndex,line.StartIndex+line.Length,origin).Units)
                {
                    int at=indices[cell.TextPosition];uint length=units[at].Length;units[at]=cell;units[at].Length=length;
                }
            }
            return units;
        }
    }
}
