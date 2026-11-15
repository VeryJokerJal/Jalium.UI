using System.Text;

namespace Jalium.UI.Styling;

/// <summary>CSS control codes belong to layout, never to the author's source string.</summary>
internal sealed class CssBidiProjection
{
    internal readonly record struct Scope(int Start, int Length, CssUnicodeBidi Value,
        FlowDirection Direction, bool Inline = true);
    private sealed record Segment(int Start, int End, string Open, string Close, int Order);
    internal string Source { get; }
    internal string Text { get; }
    internal bool NaturalParagraphDirection { get; }
    private readonly int[] _leading, _trailing, _source, _sourceEnds;
    private readonly (int Start, FlowDirection? Direction)[] _paragraphDirections;
    private readonly Dictionary<(int Source, int DisplayOffset), (int Leading, int Trailing)>? _displayBreaks;

    internal FlowDirection LineDirection(int source, FlowDirection fallback)
    {
        if (!NaturalParagraphDirection) return fallback;
        int first = 0, last = _paragraphDirections.Length - 1;
        while (first < last)
        {
            int middle = first + (last - first + 1) / 2;
            if (_paragraphDirections[middle].Start <= source) first = middle;
            else last = middle - 1;
        }
        return _paragraphDirections[first].Direction ?? fallback;
    }
    internal int NativePosition(int source, bool trailing = false)
    {
        source = Math.Clamp(source, 0, Source.Length);
        return (trailing ? _trailing : _leading)[source];
    }
    internal int NativeDisplayPosition(int source, int displayOffset, bool trailing = false)
    {
        if (displayOffset <= 0) return NativePosition(source, trailing);
        if (_displayBreaks is not null && _displayBreaks.TryGetValue((source, displayOffset), out var edge))
            return trailing ? edge.Trailing : edge.Leading;
        int length = source >= 0 && source < Source.Length && Rune.TryGetRuneAt(Source, source, out var rune) ? rune.Utf16SequenceLength : 1;
        int start = NativePosition(source);
        int end = NativePosition(Math.Min(Source.Length, source + length), true);
        int extraBreaks = _displayBreaks is null ? 0 : _displayBreaks.Keys.Count(k => k.Source == source && k.DisplayOffset < displayOffset);
        return Math.Clamp(start + displayOffset + extraBreaks, start, end);
    }
    internal int SourcePosition(int native) => _source[Math.Clamp(native, 0, Text.Length)];
    internal (int Start, int Length) SourceRange(int start, int length)
    {
        start = Math.Clamp(start, 0, Text.Length); length = Math.Clamp(length, 0, Text.Length - start);
        int first = SourcePosition(start);
        return (first, length == 0 ? 0 : Math.Max(0,
            Math.Max(_sourceEnds[start + length - 1], SourcePosition(start + length)) - first));
    }
    internal (int Start, int Length) NativeRange(int start, int length)
    {
        start = Math.Clamp(start, 0, Source.Length);length = Math.Clamp(length, 0, Source.Length - start);
        int first = NativePosition(start);
        return (first, length == 0 ? 0 : Math.Max(0, NativePosition(start + length, true) - first));
    }

    // Font spans must also cover layout-only controls, unlike visible source
    // slices. Adjacent source spans partition the projected string exactly.
    internal (int Start, int Length) NativeStyleRange(int start, int length)
    {
        if (start < 0 || length < 0 || start > Source.Length || length > Source.Length - start ||
            SplitsScalar(Source, start) || SplitsScalar(Source, start + length))
            throw new ArgumentOutOfRangeException(nameof(start));
        int first = NativePosition(start, true);
        int last = start + length == Source.Length ? Text.Length : NativePosition(start + length, true);
        return (first, length == 0 ? 0 : last - first);
    }

    internal CssBidiProjection(string source, IReadOnlyList<Scope> scopes, IReadOnlyList<int>? softBreaks = null,
        IReadOnlyList<int>? shapingBreaks = null, char shapingSeparator = '\u200C',
        IReadOnlyDictionary<int, string>? discretionaryHyphens = null,
        IReadOnlyDictionary<int, string>? displayReplacements = null,
        IReadOnlyList<(int SourcePosition, int DisplayOffset)>? softDisplayBreaks = null)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(scopes);
        Source = source;_leading = new int[source.Length + 1];_trailing = new int[source.Length + 1];
        var segments = new List<Segment>();
        var paragraphs = new List<(int Start,int End)>();int paragraphStart=0;
        for(int i=0;i<source.Length;i++)
        {
            if(!IsParagraphBreak(source[i]))continue;
            paragraphs.Add((paragraphStart,i));
            if(source[i]=='\r'&&i+1<source.Length&&source[i+1]=='\n')i++;
            paragraphStart=i+1;
        }
        paragraphs.Add((paragraphStart,source.Length));
        var ordered = scopes.Select((scope,index)=>(scope,index)).OrderBy(x=>x.scope.Start)
            .ThenByDescending(x=>x.scope.Length).ThenBy(x=>x.index).ToArray();
        var stack = new Stack<int>();
        foreach(var (scope,order) in ordered)
        {
            if(scope.Start<0||scope.Length<0||scope.Start>source.Length||scope.Length>source.Length-scope.Start)
                throw new ArgumentOutOfRangeException(nameof(scopes));
            int end=scope.Start+scope.Length;
            if(SplitsScalar(source,scope.Start)||SplitsScalar(source,end))throw new ArgumentException("A bidi scope cannot divide a Unicode scalar.",nameof(scopes));
            while(stack.TryPeek(out int enclosing)&&scope.Start>=enclosing)stack.Pop();
            if(stack.TryPeek(out int outer)&&end>outer)throw new ArgumentException("CSS bidi scopes must be nested or disjoint.",nameof(scopes));
            if(scope.Length>0)stack.Push(end);
            if(!scope.Inline&&scope.Start==0&&scope.Length==source.Length&&scope.Value==CssUnicodeBidi.Plaintext)
                NaturalParagraphDirection=true;
            var controls=CssUnicodeBidiProperties.Controls(scope.Value,scope.Direction,scope.Inline);
            if(controls.Open.Length==0||scope.Length==0)continue;
            foreach(var paragraph in paragraphs)
            {
                int first=Math.Max(scope.Start,paragraph.Start),last=Math.Min(end,paragraph.End);
                if(last>first)segments.Add(new(first,last,controls.Open,controls.Close,order));
            }
        }
        var opens=segments.GroupBy(x=>x.Start).ToDictionary(x=>x.Key,x=>x.OrderByDescending(v=>v.End).ThenBy(v=>v.Order).ToArray());
        var closes=segments.GroupBy(x=>x.End).ToDictionary(x=>x.Key,x=>x.OrderByDescending(v=>v.Start).ThenByDescending(v=>v.Order).ToArray());
        var breaks = softBreaks is null ? [] : softBreaks.ToHashSet();
        foreach (int position in breaks)
            if (position <= 0 || position >= source.Length || SplitsScalar(source, position))
                throw new ArgumentException($"A soft break must be an interior Unicode scalar boundary (position {position}, source length {source.Length}).", nameof(softBreaks));
        var shaping = shapingBreaks is null ? [] : shapingBreaks.ToHashSet();
        if(shapingSeparator is not ('\u200B' or '\u200C'))throw new ArgumentOutOfRangeException(nameof(shapingSeparator));
        foreach (int position in shaping)
            if (position <= 0 || position >= source.Length || SplitsScalar(source, position))
                throw new ArgumentException("A shaping break must be an interior Unicode scalar boundary.", nameof(shapingBreaks));
        if (discretionaryHyphens is not null)
            foreach (var (position, value) in discretionaryHyphens)
                if (position < 0 || position >= source.Length || source[position] != '\u00AD' || value is null)
                    throw new ArgumentException("A displayed conditional hyphen must belong to a source soft hyphen.", nameof(discretionaryHyphens));
        if (displayReplacements is not null)
            foreach (var (position, value) in displayReplacements)
                if (position < 0 || position >= source.Length || SplitsScalar(source, position) || value is null)
                    throw new ArgumentException("A display replacement must start at a source Unicode scalar.", nameof(displayReplacements));
        Dictionary<int, int[]>? internalDisplayBreaks = null;
        if (softDisplayBreaks is { Count: > 0 })
        {
            foreach (var (position, displayOffset) in softDisplayBreaks)
                if (displayReplacements is null || !displayReplacements.TryGetValue(position, out var replacement) ||
                    displayOffset <= 0 || displayOffset >= replacement.Length || SplitsScalar(replacement, displayOffset))
                    throw new ArgumentException("A soft display break must be an interior display scalar boundary of a display replacement.", nameof(softDisplayBreaks));
            internalDisplayBreaks = softDisplayBreaks.GroupBy(item => item.SourcePosition)
                .ToDictionary(group => group.Key, group => group.Select(item => item.DisplayOffset).Distinct().OrderBy(offset => offset).ToArray());
            _displayBreaks = [];
        }
        var text=new StringBuilder(source.Length);var map=new List<int>(source.Length+1);var ends=new List<int>(source.Length);
        void Append(string value,int position,int end){foreach(char c in value){text.Append(c);map.Add(position);ends.Add(end);}}
        for(int i=0;i<=source.Length;i++)
        {
            _trailing[i]=text.Length;
            if(closes.TryGetValue(i,out var ending))foreach(var segment in ending)Append(segment.Close,i,i);
            // U+2028 ends a layout line while retaining the paragraph's UAX #9
            // context. It is never copied back to the author's source string.
            if(breaks.Contains(i))Append("\u2028", i,i);
            if(opens.TryGetValue(i,out var starting))foreach(var segment in starting)Append(segment.Open,i,i);
            // Both separators are bidi BN. The native provider selects the
            // one which stops its shaper without consuming a source caret.
            if(shaping.Contains(i))Append(shapingSeparator.ToString(), i,i);
            _leading[i]=text.Length;
            // A conditional hyphen owns all of its displayed glyphs, including
            // a multi-character string. The author's single UTF-16 slot and
            // surrounding bidi/style boundaries remain in source coordinates.
            if(i<source.Length)
            {
                if(discretionaryHyphens?.TryGetValue(i,out var hyphen)==true)Append(hyphen,i,i+1);
                else if(displayReplacements?.TryGetValue(i,out var display)==true)
                {
                    int length=Rune.TryGetRuneAt(source,i,out var rune)?rune.Utf16SequenceLength:1;
                    int first=text.Length;
                    if(internalDisplayBreaks?.TryGetValue(i,out var displayOffsets)==true)
                    {
                        int sliceStart=0;
                        foreach(int displayOffset in displayOffsets)
                        {
                            Append(display.Substring(sliceStart,displayOffset-sliceStart),i,i+length);
                            int breakTrailing=text.Length;
                            Append("\u2028",i,i+length);
                            _displayBreaks![(i,displayOffset)]=(text.Length,breakTrailing);
                            sliceStart=displayOffset;
                        }
                        Append(display.Substring(sliceStart),i,i+length);
                    }
                    else Append(display,i,i+length);
                    // Surrogate interiors remain source positions, never extra
                    // displayed glyphs or valid scope/font boundaries.
                    for(int offset=1;offset<length;offset++)_leading[i+offset]=_trailing[i+offset]=first;
                    i+=length-1;
                }
                else {text.Append(source[i]);map.Add(i);ends.Add(i+1);}
            }
        }
        map.Add(source.Length);Text=text.ToString();_source=map.ToArray();_sourceEnds=ends.ToArray();
        _paragraphDirections = new (int, FlowDirection?)[NaturalParagraphDirection ? paragraphs.Count : 0];
        FlowDirection? previous = null;
        for (int i = 0; i < _paragraphDirections.Length; i++)
        {
            var paragraph = paragraphs[i];
            int start = _trailing[paragraph.Start], end = _leading[paragraph.End];
            previous = CssBidiDirection.FirstStrong(Text.AsSpan(start, end - start)) ?? previous;
            _paragraphDirections[i] = (paragraph.Start, previous);
        }
    }
    private static bool SplitsScalar(string source,int offset) => offset>0&&offset<source.Length&&char.IsHighSurrogate(source[offset-1])&&char.IsLowSurrogate(source[offset]);
    private static bool IsParagraphBreak(char value) => value is '\r' or '\n' or '\u0085' or '\u2029' or '\u001c' or '\u001d' or '\u001e';
}
