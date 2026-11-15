using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool _hasPositionRequest;

    private void AppendContextSpans(List<NativeTextParagraph.Span> spans)
    {
        int at = 0;
        foreach (var range in GetInlineTextRanges())
        {
            int start = Math.Clamp(range.Start, at, _visualText.Length);
            int end = Math.Clamp(range.End, start, _visualText.Length);
            if (start > at) Add(null, at, start);
            Add(range.Run, start, end); at = end;
        }
        if (at < _visualText.Length) Add(null, at, _visualText.Length);
        void Add(Run? run, int start, int end)
        {
            if (end <= start) return;
            spans.Add(new(_visualText.Substring(start, end - start),
                run is null ? FontFamily.GetRenderingSource(this) : GetRunFontFamily(run),
                run is null ? FontSize : GetRunFontSize(run),
                run?.FontWeight.ToOpenTypeWeight() ?? FontWeight.ToOpenTypeWeight(),
                run?.FontStyle.ToOpenTypeStyle() ?? FontStyle.ToOpenTypeStyle(), Colors.Black));
        }
    }

    private byte PositionRunAt(int position)
    {
        if (!_hasPositionRequest || position >= _visualText.Length) return 0;
        if (_sharedCapsContext?.PositionModes is { } shared) return shared[_sharedCapsOffset + position];
        if (_capsContext is not { } context) return 0;
        if (!context.PositionResolved && RenderContext.Current is { IsValid: true })
        {
            var spans = new List<NativeTextParagraph.Span>(); AppendContextSpans(spans);
            using var paragraph = NativeTextParagraph.TryCreate(spans, FontFamily.GetRenderingSource(this), FontSize,
                1000000, 16, TextAlignment.Left, FlowDirection.LeftToRight, noWrap:true);
            context.PositionModes = paragraph?.PositionModes(); context.PositionResolved = true;
        }
        return context.PositionModes is { } modes ? modes[position] : (byte)0;
    }
}
