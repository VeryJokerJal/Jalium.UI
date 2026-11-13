using System.Globalization;
using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private sealed record CssInlineBidiInput(int Index, CssInlineUnit Unit, int First, int End);
    private static double BidiAtomicAdvance(CssInlineSource source) => source.EmptyFrame
        ? Math.Max(.01, source.AtomicSize.Width) : source.AtomicSize.Width;

    // Source positions remain local to their original TextBlock. Only this
    // immutable paragraph view joins them, including neutral atomic objects.
    private sealed partial class CssInlineBidiPlan
    {
        internal readonly List<CssInlineUnit> Units;
        internal readonly CssInlineBidiInput[] Inputs;
        private readonly FrameworkElement _owner;
        private readonly TextBlock _fontSource;
        private bool UsesVerticalTypography => CssWritingModeProperties.GetWritingMode(_owner) is CssWritingMode.VerticalRl or CssWritingMode.VerticalLr;
        private bool UsesUprightTypography => UsesVerticalTypography && CssWritingModeProperties.GetTextOrientation(_owner) == CssTextOrientation.Upright;
        private FlowDirection UsedDirection => UsesUprightTypography ? FlowDirection.LeftToRight : _owner.FlowDirection;
        private readonly string _text;
        private readonly Dictionary<int, string> _hiddenHyphens;
        private readonly Dictionary<int, string> _displayReplacements = [];
        private readonly CssBidiProjection.Scope[] _scopes;
        private readonly List<int> _committed = [];
        private readonly Dictionary<int, string> _committedHyphens = [];
        private readonly Dictionary<string, CssInlineBidiNative> _native = [];
        private readonly Dictionary<int, double> _atomicNativeWidths = [];
        private readonly Dictionary<CssInlineSource, CssInlineBidiInput[]> _sources;
        private readonly bool _hasContextualAtomic;
        private CssBidiProjection? _directions;

        internal CssInlineBidiPlan(FrameworkElement owner, List<CssInlineUnit> units, TextBlock fontSource)
        {
            Units = units; _owner = owner; _fontSource = fontSource;
            _hasContextualAtomic = units.Any(unit => unit.Source.ContextualAtomic);
            var text = new StringBuilder(); var inputs = new List<CssInlineBidiInput>();
            var paintMaps = new Dictionary<CssInlineSource, Dictionary<int, string>?>();
            for(int i=0;i<units.Count;i++)
            {
                var unit=units[i]; int first=text.Length;
                if(unit.Source.Block)text.Append('\n');
                // A measurable ON glyph supplies bidi order even for a zero-width
                // object. Its advance is replaced by the actual box width and
                // its ink never belongs to a text source's paint range.
                else if(unit.Source.Atomic)text.Append(unit.Source.EmptyFrame?'\uFFFC':'\u25A1');
                else if(unit.Source.EmptyFrame)text.Append('\u2060');
                else
                {
                    var source = unit.Source;
                    if (!paintMaps.TryGetValue(source, out var map))
                    {
                        source.Text.TryCssBidiPaintMap(out _, out map, allowConditionalHyphens: true);
                        paintMaps[source] = map;
                    }
                    if (unit.PaintLength > 0)
                    {
                        text.Append(source.Text.PaintSlice(unit.Start, unit.End - unit.Start).AsSpan(unit.PaintOffset, unit.PaintLength));
                    }
                    else
                    {
                        text.Append(source.Text.RestoreShapedAutospaceSpaces(
                            source.Text._visualText.Substring(unit.Start,unit.End-unit.Start),unit.Start));
                        if (map is not null)
                            for (int position = unit.Start; position < unit.End; position++)
                                if (map.TryGetValue(position, out var display))
                                    _displayReplacements[first + position - unit.Start] = display;
                    }
                }
                inputs.Add(new(i,unit,first,text.Length));
            }
            _text=text.ToString();Inputs=inputs.ToArray();
            _hiddenHyphens=Enumerable.Range(0,_text.Length).Where(i=>_text[i]=='\u00AD').ToDictionary(i=>i,_=>string.Empty);
            _sources=Inputs.GroupBy(input=>input.Unit.Source).ToDictionary(group=>group.Key,group=>group.ToArray());
            var scopes=new List<CssBidiProjection.Scope>{new(0,_text.Length,CssUnicodeBidiProperties.Value(owner),UsedDirection,false)};
            var boxes=new Dictionary<DependencyObject,(int First,int End)>();
            void Box(DependencyObject element,int first,int end)
            {
                element=AutospaceStyleSource(element);
                if(ReferenceEquals(element,owner))return;
                if(boxes.TryGetValue(element,out var previous))boxes[element]=(Math.Min(first,previous.First),Math.Max(end,previous.End));
                else boxes[element]=(first,end);
            }
            foreach(var input in Inputs)
            {
                for(var frame=input.Unit.Source.Frame;frame is not null;frame=frame.Parent)Box(frame.Element,input.First,input.End);
                if(!input.Unit.Source.Atomic)Box(input.Unit.Source.Element,input.First,input.End);
            }
            foreach(var (element,range) in boxes)
            {
                var mode=CssUnicodeBidiProperties.Value(element);
                if(mode!=CssUnicodeBidi.Normal)scopes.Add(new(range.First,range.End-range.First,mode,(FlowDirection)element.GetValue(FlowDirectionProperty)!));
            }
            foreach(var source in _sources.Keys.Where(source=>!source.Atomic&&!source.EmptyFrame))
            {
                int offset=0;
                if(source.Text._inlines is { } inlines)
                    foreach(var inline in inlines)Collect(inline,source,ref offset);
            }
            _scopes=scopes.ToArray();
            void Collect(Inline inline,CssInlineSource source,ref int offset)
            {
                int first=offset;
                if(inline is Run run)offset+=run.Text?.Length??0;
                else if(inline is Span span)foreach(var child in span.Inlines)Collect(child,source,ref offset);
                else if(inline is LineBreak or InlineUIContainer)offset++;
                var mode=CssUnicodeBidiProperties.Value(inline);
                if(offset>first&&mode!=CssUnicodeBidi.Normal)
                {
                    int a=Position(source,first),b=Position(source,offset);
                    if(b>a)scopes.Add(new(a,b-a,mode,(FlowDirection)inline.GetValue(FlowDirectionProperty)!));
                }
            }
        }
        internal int Position(CssInlineSource source,int local)
        {
            if(!_sources.TryGetValue(source,out var inputs)||inputs.Length==0)return 0;
            for(int i=0;i<inputs.Length;i++)
            {
                var input=inputs[i];
                if(local<=input.Unit.Start)return input.First;
                if(local<input.Unit.End)return input.First+Math.Min(local-input.Unit.Start,input.End-input.First);
                if(local==input.Unit.End)
                {
                    while(i+1<inputs.Length&&inputs[i+1].Unit.Start==input.Unit.Start&&inputs[i+1].Unit.End==input.Unit.End)
                        input=inputs[++i];
                    return input.End;
                }
            }
            return inputs[^1].End;
        }
        internal int SourcePosition(CssInlineSource source,int raw) => SourceStartPosition(source,raw);
        internal int SourceStartPosition(CssInlineSource source,int raw)
        {
            var inputs=_sources[source];
            foreach(var input in inputs)
            {
                if(raw<=input.First)return input.Unit.Start;
                if(raw<input.End)return input.Unit.PaintLength>0?input.Unit.Start:Math.Min(input.Unit.End,input.Unit.Start+raw-input.First);
            }
            return inputs[^1].Unit.End;
        }
        internal int SourceEndPosition(CssInlineSource source,int raw)
        {
            var inputs=_sources[source];
            foreach(var input in inputs)
            {
                if(raw<=input.First)return input.Unit.Start;
                if(raw<=input.End)return input.Unit.PaintLength>0?input.Unit.End:Math.Min(input.Unit.End,input.Unit.Start+raw-input.First);
            }
            return inputs[^1].Unit.End;
        }
        internal Dictionary<int,string> VisibleHyphens(IReadOnlyList<CssInlineFragment> pieces)
        {
            var result = new Dictionary<int,string>(_committedHyphens);
            foreach (var piece in pieces)
            {
                var original = (piece.Shape as CssInlineBidiPart)?.Map.Original ?? piece;
                var source = original.Source;
                var line = original.Line;
                if (source.Atomic || source.EmptyFrame || !line.SoftHyphenVisible || line.Length == 0) continue;
                int local = line.StartIndex + line.Length - 1, raw = Position(source, local);
                if (raw < _text.Length && _text[raw] == '\u00AD') result[raw] = source.Text.GetHyphenationStringAt(local);
            }
            return result;
        }
        internal void Commit(int end,bool hard,IReadOnlyList<CssInlineFragment> pieces)
        {
            foreach (var (position, text) in VisibleHyphens(pieces)) _committedHyphens[position] = text;
            if(!hard&&end>0&&end<Inputs.Length&&Inputs[end].First>0&&Inputs[end].First<_text.Length)
                _committed.Add(Inputs[end].First);
        }
        internal uint Row(CssInlineBidiNative native,int raw)
        {
            int at=native.Projection.NativePosition(raw);
            var lines=native.Paragraph.Lines;
            int length=raw<_text.Length&&Rune.TryGetRuneAt(_text,raw,out var rune)?rune.Utf16SequenceLength:1;
            bool emptyDisplay=raw<_text.Length&&native.Projection.NativeRange(raw,length).Length==0;
            for(int i=0;i<lines.Length;i++)
                if(at>=lines[i].Metrics.Line.TextPosition&&
                    (at<lines[i].Metrics.Line.TextPosition+lines[i].Metrics.Line.Length||
                     emptyDisplay&&at==lines[i].Metrics.Line.TextPosition+lines[i].Metrics.Line.Length))return(uint)i;
            return(uint)Math.Max(0,lines.Length-1);
        }
        internal FlowDirection Direction(CssInlineBidiNative native,int first)
            =>UsesUprightTypography ? FlowDirection.LeftToRight : native.Projection.LineDirection(first,UsedDirection);
        internal FlowDirection DirectionAt(int unit)
            =>UsesUprightTypography ? FlowDirection.LeftToRight :
                (_directions??=new(_text,_scopes)).LineDirection(unit<Inputs.Length?Inputs[unit].First:_text.Length,UsedDirection);

        internal CssInlineBidiNative? Shape(int start,int end,int consumed,IReadOnlyList<int>? shapingBreaks=null,
            double origin=0,CssInlineBidiEdge[]? edges=null,double? justifyWidth=null,
            IReadOnlyDictionary<int,string>? hyphens=null)
        {
            var breaks=new HashSet<int>(_committed);
            if(start>0&&!Inputs[start-1].Unit.HardBreak&&Inputs[start].First>0&&Inputs[start].First<_text.Length)breaks.Add(Inputs[start].First);
            if(consumed<Inputs.Length&&consumed>0&&!Inputs[consumed-1].Unit.HardBreak&&
                !Inputs[consumed].Unit.HardBreak&&Inputs[consumed].First<_text.Length)breaks.Add(Inputs[consumed].First);
            string key=start+":"+end+":"+consumed+";"+string.Join(',',breaks.Order())+";"+string.Join(',',shapingBreaks??[]);
            // A Ruby level changes box advances and cross-axis metrics for
            // each candidate line. Keep retained bidi paragraphs tied to those
            // advances, including during balance/float/clamp probes.
            if (_hasContextualAtomic)
                key += ";ruby:" + string.Join(',', Inputs.Where(input => input.Unit.Source.ContextualAtomic)
                    .Select(input => input.Unit.Source.AtomicSize.Width.ToString("R", CultureInfo.InvariantCulture) + "/" +
                        input.Unit.Source.AtomicSize.Height.ToString("R", CultureInfo.InvariantCulture) + "/" +
                        input.Unit.Source.AtomicBaseline.ToString("R", CultureInfo.InvariantCulture)));
            // Remove unused discretionary markers from shaping as well as
            // paint. Some providers otherwise keep a hidden glyph boundary
            // which changes kerning or joining inside an unbroken word.
            var displayedHyphens=new Dictionary<int,string>(_hiddenHyphens);
            foreach(var (position,text) in hyphens??_committedHyphens)displayedHyphens[position]=text;
            hyphens=displayedHyphens;
            key += ";hyphens:"+string.Join(';',hyphens.OrderBy(pair=>pair.Key).Select(pair=>pair.Key+":"+pair.Value.Length+":"+pair.Value));
            key += ";"+origin.ToString("R",CultureInfo.InvariantCulture)+";"+justifyWidth?.ToString("R",CultureInfo.InvariantCulture)+";"+
                string.Join(';',(edges??[]).Select(edge=>edge.First+":"+edge.Left.ToString("R",CultureInfo.InvariantCulture)+":"+edge.Right.ToString("R",CultureInfo.InvariantCulture)));
            if(_native.TryGetValue(key,out var cached))return cached;
            // CoreText needs ZWNJ to stop kerning/joining. The shared native
            // provider snaps ZWNJ to its preceding grapheme; ZWSP supplies
            // an independent zero-width shaping boundary for that provider.
            var projection=new CssBidiProjection(_text,_scopes,breaks.Order().ToArray(),shapingBreaks,
                OperatingSystem.IsMacOS()?'\u200C':'\u200B',hyphens,_displayReplacements);
            var spans=new List<NativeTextParagraph.Span>();
            bool suppressLigatures=(CssFlowProperties.LineAlignment(_owner,false)==CssFlowTextAlignment.Justify||
                CssFlowProperties.LineAlignment(_owner,true)==CssFlowTextAlignment.Justify)&&SuppressJustificationLigatures(_text,
                _sources.Keys.Where(source=>!source.Atomic&&!source.EmptyFrame).SelectMany(source=>
                    source.Text.GetInlineTextRanges().Select(range=>(DependencyObject)range.Run).Prepend(source.Element)).Prepend(_owner));
            foreach(var input in Inputs)
            {
                var source=input.Unit.Source;
                var text=source.Atomic||source.EmptyFrame?_fontSource:source.Text;
                if(!source.Atomic&&!source.EmptyFrame&&input.Unit.PaintLength==0)
                {
                    int localFirst=input.Unit.Start,localLast=input.Unit.End,localCursor=localFirst;
                    foreach(var range in text.GetInlineTextRanges())
                    {
                        int from=Math.Max(localCursor,range.Start),to=Math.Min(localLast,range.End);
                        if(to<=from)continue;
                        if(from>localCursor)AddInputSlice(text,null,source.Element,localCursor,from,input.First+localCursor-localFirst);
                        AddInputSlice(text,range.Run,source.Element,from,to,input.First+from-localFirst);localCursor=to;
                    }
                    if(localCursor<localLast)AddInputSlice(text,null,source.Element,localCursor,localLast,input.First+localCursor-localFirst);
                    continue;
                }
                var run=source.Atomic||source.EmptyFrame?null:InlineRunAt(text.GetInlineTextRanges(),input.Unit.Start);
                AddRawSlice(text,run,source.Element,input.Unit.Start,input.First,input.End-input.First);
            }
            void AddInputSlice(TextBlock text,Run? run,DependencyObject styleOwner,int localFrom,int localTo,int rawStart)
                =>AddRawSlice(text,run,styleOwner,localFrom,rawStart,localTo-localFrom);
            void AddRawSlice(TextBlock text,Run? run,DependencyObject styleOwner,int localStart,int rawStart,int rawLength)
            {
                if(rawLength<=0)return;
                string family=run is null?text.FontFamily.GetRenderingSource(text):text.GetRunFontFamily(run);
                if(suppressLigatures)family=JustificationFont(family,run??styleOwner);
                if(text._usesCapsShaping)family=FontFeatureRenderingSource.WithCapsFragment(family,0,0,text.PositionRunAt(localStart),0);
                var range=projection.NativeStyleRange(rawStart,rawLength);
                var span=new NativeTextParagraph.Span(projection.Text.Substring(range.Start,range.Length),family,
                    run is null?text.FontSize:text.GetRunFontSize(run),run?.FontWeight.ToOpenTypeWeight()??text.FontWeight.ToOpenTypeWeight(),
                    run?.FontStyle.ToOpenTypeStyle()??text.FontStyle.ToOpenTypeStyle(),Colors.Black,
                    TextOrientation:UsesVerticalTypography?(int)CssWritingModeProperties.GetTextOrientation(run??styleOwner):null);
                if(spans.Count>0&&(spans[^1]with{Text=""})==(span with{Text=""}))spans[^1]=spans[^1]with{Text=spans[^1].Text+span.Text};
                else spans.Add(span);
            }
            var units=new List<NativeMethods.ParagraphAdvanceUnit>();int cursor=0;
            var boundaries=GraphemeClusters.GetBoundaries(_text);
            for(int i=0;i+1<boundaries.Length;i++)
            {
                int first=boundaries[i],last=boundaries[i+1];var input=Inputs.First(value=>value.End>first);
                var source=input.Unit.Source;var mapped=projection.NativeRange(first,last-first);
                if(mapped.Start>cursor)units.Add(new(){TextPosition=(uint)cursor,Length=(uint)(mapped.Start-cursor),Flags=1});
                if(mapped.Length==0){cursor=mapped.Start;continue;}
                bool ignored=true;
                foreach(var rune in _text.AsSpan(first,last-first).EnumerateRunes())
                    if(Rune.GetUnicodeCategory(rune)!=UnicodeCategory.Format&&rune.Value is not ('\r' or '\n' or 0x85 or 0x2028 or 0x2029)) { ignored=false;break; }
                if(input.Index>=end&&input.Index<consumed&&input.Unit.Collapse)ignored=true;
                double letter=0,word=0;bool cursive=false,separator=false;
                if(!source.Atomic&&!source.EmptyFrame)
                {
                    int local=SourcePosition(source,first);letter=source.Text.ComputedLetterSpacingAt(local);
                    Rune.TryGetRuneAt(_text,first,out var rune);separator=IsWordSeparator(rune);
                    if(separator)word=source.Text.ComputedWordSpacingAt(local);
                    cursive=Rune.IsLetter(rune)&&IsCursiveJoiningPair(rune,rune);
                }
                if(!double.IsFinite(letter)||!double.IsFinite(word)||Math.Abs(letter)>1000000||Math.Abs(word)>1000000)return null;
                if(hyphens.ContainsKey(first) || HasDisplayReplacement(first,last))
                {
                    // Native advance units describe displayed graphemes. A
                    // multi-character hyphen still has one source selection,
                    // but needs each glyph boundary for native caret/selection
                    // geometry and its own styled letter/word spacing.
                    string displayed=projection.Text.Substring(mapped.Start,mapped.Length);
                    var displayBoundaries=GraphemeClusters.GetBoundaries(displayed);
                    for(int j=0;j+1<displayBoundaries.Length;j++)
                    {
                        int a=displayBoundaries[j],b=displayBoundaries[j+1];Rune.TryGetRuneAt(displayed,a,out var rune);
                        bool space=IsWordSeparator(rune),hidden=Rune.GetUnicodeCategory(rune)==UnicodeCategory.Format;
                        double gap=space?source.Text.ComputedWordSpacingAt(SourcePosition(source,first)):0;
                        units.Add(new(){TextPosition=(uint)(mapped.Start+a),Length=(uint)(b-a),Spacing=(float)letter,
                            Leading=(float)(gap/2),Trailing=(float)(gap/2),Flags=hidden?1u:space?4u:0u});
                    }
                    cursor=mapped.Start+mapped.Length;continue;
                }
                units.Add(new(){TextPosition=(uint)mapped.Start,Length=(uint)mapped.Length,Spacing=(float)letter,
                    Leading=(float)(word/2),Trailing=(float)(word/2),Flags=ignored?1u:(cursive?2u:0u)|(separator?4u:0u)});
                cursor=mapped.Start+mapped.Length;
            }
            if(cursor<projection.Text.Length)units.Add(new(){TextPosition=(uint)cursor,Length=(uint)(projection.Text.Length-cursor),Flags=1});
            // Tracking can contribute a neighbour's half to the neutral cell.
            // Only cache its intrinsic advance when that contribution is absent.
            bool cacheAtomicWidths = units.All(unit => unit.Spacing == 0);
            if (cacheAtomicWidths)
                foreach (var input in Inputs.Where(input => input.Unit.Source.Atomic && !input.Unit.Source.Block))
                    if (_atomicNativeWidths.TryGetValue(input.Index, out double objectAdvance))
                    {
                        int index = units.FindIndex(unit => unit.TextPosition == projection.NativePosition(input.First));
                        if (index < 0) continue;
                        var unit = units[index]; unit.Trailing += (float)(BidiAtomicAdvance(input.Unit.Source) - objectAdvance);
                        units[index] = unit;
                    }
            var combined = new List<NativeMethods.ParagraphCombineUnit>();
            foreach (var input in Inputs)
            {
                if (input.Unit.Source.Atomic || input.Unit.Source.EmptyFrame ||
                    input.Unit.Source.Text.CombinedUnitAt(input.Unit.Start) is not { } composition ||
                    composition.End != input.Unit.End) continue;
                var range = projection.NativeRange(input.First, input.End - input.First);
                if (range.Length > 0) combined.Add(new() { TextPosition = (uint)range.Start, Length = (uint)range.Length,
                    Em = (float)composition.Em, Direction = composition.Direction == FlowDirection.RightToLeft ? 1 : 0 });
            }
            // The native contract requires a composition to own one advance
            // cell. Internal glyph tracking/word spacing belongs to its
            // horizontal contents, not the surrounding vertical paragraph.
            foreach (var composition in combined)
            {
                int first = units.FindIndex(unit => unit.TextPosition == composition.TextPosition);
                int last = units.FindIndex(unit => unit.TextPosition + unit.Length == composition.TextPosition + composition.Length);
                if (first < 0 || last < first) return null;
                var cell = units[first]; cell.Length = composition.Length;
                cell.Leading = cell.Trailing = 0; cell.Flags = 0;
                units[first] = cell;
                if (last > first) units.RemoveRange(first + 1, last - first);
            }
            NativeTextParagraph? Make(NativeMethods.ParagraphAdvanceUnit[]? requested=null)=>NativeTextParagraph.TryCreateAdvanced(spans,_fontSource.FontFamily.GetRenderingSource(_fontSource),_fontSource.FontSize,
                10000000,1,UsesUprightTypography?FlowDirection.LeftToRight:_owner.FlowDirection,requested??units.ToArray(),contextPaint:true,naturalDirection:projection.NaturalParagraphDirection,textOptionsOwner:_owner,
                verticalForms:UsesVerticalTypography,uprightForms:UsesUprightTypography,combined:combined);
            var paragraph=Make();
            if(paragraph is null)return null;
            bool objects=false;
            var initial=new CssInlineBidiNative(this,projection,paragraph);
            foreach(var input in Inputs.Where(input=>input.Unit.Source.Atomic&&!input.Unit.Source.Block&&(!cacheAtomicWidths||!_atomicNativeWidths.ContainsKey(input.Index))))
            {
                var range=projection.NativeRange(input.First,input.End-input.First);uint row=Row(initial,input.First);
                double width=paragraph.Selection(row,range.Start,range.Length).Sum(rect=>(double)rect.Width);
                if (cacheAtomicWidths) _atomicNativeWidths[input.Index] = width;
                int index=units.FindIndex(unit=>unit.TextPosition==range.Start);
                if(index<0)continue;
                // A measurable neutral slot preserves visual order even for a
                // zero-width object. CSS still uses the object's actual box.
                double desired=BidiAtomicAdvance(input.Unit.Source);
                var unit=units[index];unit.Trailing+=(float)(desired-width);units[index]=unit;objects=true;
            }
            if(objects)
            {
                var adjusted=Make();paragraph.Dispose();if(adjusted is null)return null;paragraph=adjusted;
            }
            var request=new CssInlineBidiRequest(start,end,consumed,origin,edges??[],shapingBreaks?.ToArray()??[],justifyWidth);
            var native=ApplyContextualAdvances(projection,units.ToArray(),paragraph,request,Make);
            if(native is null){paragraph.Dispose();return null;}
            if(_native.Count>=256)_native.Clear(); // Old fragments retain their own immutable paragraph views.
            _native[key]=native;return native;
        }
        private bool HasDisplayReplacement(int first,int end)
        {
            for(int i=first;i<end;i++)if(_displayReplacements.ContainsKey(i))return true;
            return false;
        }
    }

    internal sealed partial class CssTextFlowLayout
    {
        private CssInlineBidiPlan? _inlineBidi;
        private void DecorateInlineBidiFrames(List<CssInlineFragment> fragments,int consumed)
        {
            var pieces=new List<CssInlineFramePiece>();
            bool rtl=(fragments[0].BidiDirection??UsedFlowDirection)==FlowDirection.RightToLeft;
            foreach(var frame in _frames)
            {
                bool initial=_initialLetter is{Inline:true,Frame:{} initialFrame}&&BelongsToFrame(initialFrame,frame);
                bool Member(int index)=>BelongsTo(fragments[index].Source,frame)&&(!initial||fragments[index].Source.Initial is null);
                var groups=new List<(int First,int Last)>();
                for(int i=0;i<fragments.Count;)
                {
                    if(!Member(i)){i++;continue;}int first=i++;
                    while(i<fragments.Count&&Member(i))i++;
                    groups.Add((first,i-1));
                }
                bool startFromFirst=rtl==frame.RightToLeft;
                for(int i=0;i<groups.Count;i++)
                {
                    var group=groups[i];
                    var piece=new CssInlineFramePiece(frame,rtl?group.Last:group.First,rtl?group.First:group.Last,
                        frame.Fragments.Count==0&&i==(startFromFirst?0:groups.Count-1),
                        frame.LastUnit<consumed&&i==(startFromFirst?groups.Count-1:0));
                    pieces.Add(piece);
                    fragments[piece.LeftIndex].OuterLeft+=piece.Left+piece.MarginLeft;
                    fragments[piece.RightIndex].OuterRight+=piece.Right+piece.MarginRight;
                }
            }
            fragments[0].FramePieces=pieces;
        }
        private CssInlineBidiPlan? CreateInlineBidi(List<CssInlineUnit> units)
        {
            if(RenderContext.Current is not{IsValid:true}||units.Count==0)return null;
            var texts=units.Where(unit=>!unit.Source.Atomic&&!unit.Source.EmptyFrame).Select(unit=>unit.Source.Text).Distinct().ToArray();
            if(texts.Length==0)return null;
            bool enabled=Axes.Vertical||texts.Any(text=>text.TryCssBidiPaintMap(out _,out var map,texts.Length>1)&&map is {Count:>0})||
                CssUnicodeBidiProperties.Value(owner)!=CssUnicodeBidi.Normal||CssDirection(owner)||
                texts.Any(text=>NeedsBidiParagraph(text._visualText)||CssUnicodeBidiProperties.Value(text)!=CssUnicodeBidi.Normal);
            foreach(var unit in units)
                for(var frame=unit.Source.Frame;!enabled&&frame is not null;frame=frame.Parent)
                    enabled=CssUnicodeBidiProperties.Value(frame.Element)!=CssUnicodeBidi.Normal;
            // Ordinary LTR spacing keeps its existing shared native planner.
            // Conditional hyphens also need the whole-paragraph display map.
            if(!enabled)return null;
            if(texts.Any(text=>!text.TryGetCssBidiPaint(out _,allowConditionalHyphens:true)||text.FontSize<=0||(text.FontStretch.ToOpenTypeStretch()!=5 && !OperatingSystem.IsMacOS())||
                !SupportedInk(text.Foreground)||!text.SupportedCssBidiFonts()))return null;
            return new(owner,units,texts[0]);
            static bool CssDirection(FrameworkElement element)
            {
                for(FrameworkElement? at=element;at is not null;at=at.FrameworkParent ?? (at.VisualParent as FrameworkElement) ?? (at is ICssAnonymousBox anonymous && !ReferenceEquals(anonymous.Owner, at) ? anonymous.Owner : null))
                {
                    if(at is ICssStyledBox { StyleSource: DependencyObject styleSource } && !ReferenceEquals(styleSource, at))
                    {
                        var styleLayer=styleSource.GetEffectiveValueLayer(FlowDirectionProperty);
                        if(styleLayer is DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState)return true;
                        if(styleLayer is not null && styleSource is not TextElement)return false;
                        continue;
                    }
                    if(at is InlineSourceText projected)
                    {
                        var projLayer=projected.ProjectedStyleSource?.GetEffectiveValueLayer(FlowDirectionProperty);
                        if(projLayer is DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState)return true;
                        continue;
                    }
                    var layer=at.GetEffectiveValueLayer(FlowDirectionProperty);
                    if(layer is DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState)return true;
                    if(layer is not null)return false;
                }
                return false;
            }
        }
        private bool TryInlineBidiPieces(List<CssInlineFragment> pieces,List<CssInlineUnit> units,int start,int end,int consumed,double origin=0,double? justifyWidth=null,
            CssInlineBidiEdge[]? initialEdges=null,IReadOnlyList<int>? initialBreaks=null)
        {
            if(_inlineBidi is not{} plan||!ReferenceEquals(plan.Units,units)||pieces.Count==0||end<=start)return false;
            if(pieces.Any(piece=>piece.Line.PaintWrap is not null || piece.Line.HyphenChange is { HasChanges: true } ||
                (piece.Line.SoftHyphenVisible && !piece.Source.Atomic && !piece.Source.EmptyFrame &&
                 (piece.Line.Length == 0 || piece.Source.Text._visualText[piece.Line.StartIndex + piece.Line.Length - 1] != '\u00AD'))))return false;
            var hyphens=plan.VisibleHyphens(pieces);
            var native=plan.Shape(start,end,consumed,initialBreaks,origin,initialEdges,justifyWidth,hyphens);if(native is null)return false;
            int first=plan.Inputs[start].First,last=plan.Inputs[end-1].End;
            var maps=pieces.Select(piece=>new CssInlineBidiMap(piece,
                piece.Source.Atomic||piece.Source.EmptyFrame?plan.Inputs.First(input=>ReferenceEquals(input.Unit.Source,piece.Source)).First:Math.Max(first,plan.Position(piece.Source,piece.Line.StartIndex)),
                piece.Source.Atomic||piece.Source.EmptyFrame?plan.Inputs.First(input=>ReferenceEquals(input.Unit.Source,piece.Source)).End:Math.Min(last,plan.Position(piece.Source,piece.Line.StartIndex+piece.Line.Length)))).ToArray();
            var cells=new List<CssInlineBidiCell>();var boundaries=GraphemeClusters.GetBoundaries(native.Projection.Source);
            var result=new List<CssInlineFragment>();
            var shapingBreaks=new SortedSet<int>(initialBreaks??[]);
            for(int pass=0;pass<2;pass++)
            {
            cells.Clear();result.Clear();
            for(int i=0;i+1<boundaries.Length;i++)
            {
                int a=boundaries[i],b=boundaries[i+1];if(a<first||b>last)continue;
                var map=maps.FirstOrDefault(map=>b>map.First&&b<=map.End);if(map is null)continue;
                uint row=plan.Row(native,a);var projected=native.Projection.NativeRange(a,b-a);
                double x=native.Paragraph.Caret(row,projected.Start,projected.Length==0).X;
                double y=projected.Length==0?x:native.Paragraph.Caret(row,projected.Start+projected.Length,true).X;
                var exterior=native.Exterior?.GetValueOrDefault(a);
                var before=exterior?.Before??[];var after=exterior?.After??[];
                cells.Add(new(a,b,Math.Min(x,y)+before.Sum(gap=>gap.Width),Math.Max(x,y)-after.Sum(gap=>gap.Width),
                    native.Ordering?.GetValueOrDefault(a)?.Rtl??y<x,map,before,after));
            }
            var visual=cells.OrderBy(cell=>cell.Left).ThenBy(cell=>native.Ordering?.GetValueOrDefault(cell.First)?.Ordinal??cell.First).ToArray();
            for(int i=0;i<visual.Length;)
            {
                var a=visual[i++];int from=a.First,to=a.End;double left=a.Left,right=a.Right;var previous=a;
                while(i<visual.Length&&previous.After.Length==0&&visual[i].Before.Length==0&&ReferenceEquals(visual[i].Map,a.Map)&&visual[i].Rtl==a.Rtl&&
                    (a.Rtl?visual[i].End==from:visual[i].First==to))
                {var next=visual[i++];from=Math.Min(from,next.First);to=Math.Max(to,next.End);right=Math.Max(right,next.Right);previous=next;}
                Add(a.Map,from,to,left,right,a.Before,previous.After);
            }
            foreach(var map in maps)
                if(!result.Any(piece=>ReferenceEquals((piece.Shape as CssInlineBidiPart)?.Map,map)||ReferenceEquals(piece.Source,map.Original.Source)&&piece.Shape is null))
                {double x=native.Paragraph.Caret(plan.Row(native,map.First),native.Projection.NativePosition(map.First),false).X;Add(map,map.First,map.End,x,x,[],[]);}
            var direction=plan.Direction(native,first);bool rtl=direction==FlowDirection.RightToLeft;
            foreach(var map in maps)
            {
                var parts=result.Where(piece=>ReferenceEquals((piece.Shape as CssInlineBidiPart)?.Map,map)||ReferenceEquals(piece.Source,map.Original.Source)&&piece.Shape is null).OrderBy(piece=>(piece.Shape as CssInlineBidiPart)?.Left??0).ToArray();
                for(int i=0;i<parts.Length;i++)
                {
                    var piece=parts[i];bool left=i==0,right=i==parts.Length-1;
                    piece.BidiDirection=direction;
                    if(!map.Original.Source.Clone)
                    {
                        piece.Left=left?map.Original.Left:0;piece.Right=right?map.Original.Right:0;
                        var atomic = piece.Source.Atomic ? native.AtomicSpacing?.GetValueOrDefault(map.First) : null;
                        piece.MarginLeft=(left?map.Original.MarginLeft:0)+(piece.Shape is CssInlineBidiPart lp?lp.Before.Sum(gap=>gap.Width):atomic?.Left??0);
                        piece.MarginRight=(right?map.Original.MarginRight:0)+(piece.Shape is CssInlineBidiPart rp?rp.After.Sum(gap=>gap.Width):atomic?.Right??0);
                    }
                    piece.First=map.Original.First&&(map.Original.Source.RightToLeft?right:left);
                    piece.Last=map.Original.Last&&(map.Original.Source.RightToLeft?left:right);
                }
            }
            result.Sort((a,b)=>rtl?CompareNative(b,a):CompareNative(a,b));
            DecorateInlineFrames(result,consumed);
            if(pass!=0)break;
            // Discover shaping barriers after resolving physical bidi edges.
            // Markup boundaries with no physical edge remain transparent.
            var logical=cells.OrderBy(cell=>cell.First).ToArray();
            for(int i=1;i<logical.Length;i++)
            {
                var before=logical[i-1];var after=logical[i];
                if(before.End!=after.First||before.Rtl!=after.Rtl||ReferenceEquals(before.Map,after.Map)||
                    before.Map.Original.Source.Atomic||after.Map.Original.Source.Atomic||
                    before.Map.Original.Source.EmptyFrame||after.Map.Original.Source.EmptyFrame)continue;
                var a=result.FirstOrDefault(piece=>piece.Shape is CssInlineBidiPart p&&ReferenceEquals(p.Map,before.Map)&&p.End==before.End);
                var b=result.FirstOrDefault(piece=>piece.Shape is CssInlineBidiPart p&&ReferenceEquals(p.Map,after.Map)&&p.First==after.First);
                if(a is null||b is null)continue;
                bool edge=before.Rtl?a.Left!=0||PhysicalMargin(a,true)!=0||a.OuterLeft!=0||b.Right!=0||PhysicalMargin(b,false)!=0||b.OuterRight!=0:
                    a.Right!=0||PhysicalMargin(a,false)!=0||a.OuterRight!=0||b.Left!=0||PhysicalMargin(b,true)!=0||b.OuterLeft!=0;
                if(edge||Shifted(a.Source,b.Source)||!ReferenceEquals(a.Source.Initial,b.Source.Initial))shapingBreaks.Add(after.First);
            }
            var physical=InlineBidiEdges(result,cells,native);
            if(shapingBreaks.Count==0&&physical.All(edge=>edge.Left==0&&edge.Right==0))break;
            var reshaped=plan.Shape(start,end,consumed,shapingBreaks.ToArray(),origin,physical,justifyWidth,hyphens);if(reshaped is null)return false;native=reshaped;
            }
            pieces.Clear();pieces.AddRange(result);return true;
            int CompareNative(CssInlineFragment a,CssInlineFragment b)
            {
                int compare=NativeLeft(a).CompareTo(NativeLeft(b));
                if(compare!=0)return compare;
                int Ordinal(CssInlineFragment piece)
                {
                    int raw=piece.Shape is CssInlineBidiPart part
                        ?(part.Native.Ordering?.GetValueOrDefault(part.First)?.Rtl==true
                            ?GraphemeClusters.PreviousBoundary(part.Native.Projection.Source,part.End):part.First)
                        :plan.Inputs.First(input=>ReferenceEquals(input.Unit.Source,piece.Source)).First;
                    return native.Ordering?.GetValueOrDefault(raw)?.Ordinal??raw;
                }
                return Ordinal(a).CompareTo(Ordinal(b));
            }
            double NativeLeft(CssInlineFragment piece)=>piece.Shape is CssInlineBidiPart part?part.Left:
                cells.FirstOrDefault(cell=>ReferenceEquals(cell.Map.Original.Source,piece.Source))?.Left??0;
            void Add(CssInlineBidiMap map,int a,int b,double left,double right,SharedShapeExterior[] before,SharedShapeExterior[] after)
            {
                var original=map.Original;
                if(original.Source.Atomic||original.Source.EmptyFrame)
                {
                    var slot=original.Copy();
                    if(original.Source.Atomic&&native.AtomicSpacing?.TryGetValue(a,out var atomic)==true)
                    {slot.MarginLeft+=atomic.Left;slot.MarginRight+=atomic.Right;}
                    result.Add(slot);return;
                }
                var part=new CssInlineBidiPart(native,map,a,b,left,right,before,after);var old=original.Line;
                var line=new TextLayoutLine(part.SourceStart,Math.Max(0,part.SourceEnd-part.SourceStart),Math.Max(0,right-left),old.HasLineBreakAfter,
                    old.Height,old.Baseline,old.GlyphHeight,old.FormattedText,old.CanJustify,old.IndentApplies,old.PaintWrap,old.SoftHyphenVisible,
                    a<=map.First?old.StartPadding:0,b>=map.End?old.EndPadding:0,hyphenChange:old.HyphenChange);
                var piece=original.Copy();piece.Line=line;piece.Shape=part;
                piece.MarginLeft+=before.Sum(gap=>gap.Width);piece.MarginRight+=after.Sum(gap=>gap.Width);result.Add(piece);
            }
            static double PhysicalMargin(CssInlineFragment piece,bool left)=>
                (left?piece.MarginLeft:piece.MarginRight)-(piece.Shape is CssInlineBidiPart part?
                    (left?part.Before:part.After).Sum(gap=>gap.Width):0);
            static bool Shifted(CssInlineSource a,CssInlineSource b)
            {
                if((CssInlineVerticalAlign)a.Element.GetValue(CssFlowProperties.VerticalAlignProperty)! !=default||
                    (CssInlineVerticalAlign)b.Element.GetValue(CssFlowProperties.VerticalAlignProperty)! !=default)return true;
                return Ancestor(a.Frame,b.Frame)||Ancestor(b.Frame,a.Frame);
                static bool Ancestor(CssInlineFrame? frame,CssInlineFrame? other)
                {
                    for(;frame is not null;frame=frame.Parent)
                    {
                        bool common=false;
                        for(var at=other;at is not null;at=at.Parent)if(ReferenceEquals(at,frame)){common=true;break;}
                        if(!common&&(CssInlineVerticalAlign)frame.Element.GetValue(CssFlowProperties.VerticalAlignProperty)! !=default)return true;
                    }
                    return false;
                }
            }
        }
    }
}
