using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal sealed partial class CssTextFlowLayout
    {
        private static void PrepareCapsContext(List<CssInlineUnit> units)
        {
            if (!OperatingSystem.IsMacOS() || !units.Any(unit => !unit.Source.Atomic && !unit.Source.EmptyFrame &&
                (unit.Source.Text._hasCapsRequest || unit.Source.Text._hasPositionRequest || unit.Source.Text._hasEmojiRequest))) return;
            var joined = new StringBuilder(); var sources = new HashSet<CssInlineSource>();
            // Keep the original logical text before line fitting or clamping.
            // Styling and physical inline edges do not end Unicode context;
            // replaced objects and block boundaries do.
            foreach (var unit in units)
            {
                var source = unit.Source;
                if (!sources.Add(source) || source.EmptyFrame) continue;
                source.CapsOffset = joined.Length;
                if (source.Atomic) joined.Append(source.Block ? '\n' : '\ufffc');
                else joined.Append(source.Text._visualText);
            }
            var context = new CapsContextData(joined.ToString())
            { HasEmoji = sources.Any(source => !source.Atomic && !source.EmptyFrame && source.Text._hasEmojiRequest) };
            bool positions = sources.Any(source => !source.Atomic && !source.EmptyFrame && source.Text._hasPositionRequest);
            if (positions || sources.Any(source => !source.Atomic && !source.EmptyFrame && source.CapsOffset > 0 &&
                source.Text._visualText.Length > 0 &&
                Rune.TryGetRuneAt(source.Text._visualText, 0, out var first) &&
                (CssUnicodeCasingData.IsAbove(first.Value) || CssUnicodeCasingData.IsOtherCombining(first.Value))))
            {
                var spans = new List<NativeTextParagraph.Span>();
                foreach (var source in sources.OrderBy(source => source.CapsOffset))
                {
                    if (source.EmptyFrame) continue;
                    if (source.Atomic)
                    {
                        spans.Add(new(source.Block ? "\n" : "\ufffc", "Arial", 12, 400, 0, Colors.Black)); continue;
                    }
                    source.Text.AppendContextSpans(spans);
                }
                using var paragraph = NativeTextParagraph.TryCreate(spans, "Arial", 12, 1000000, 16,
                    TextAlignment.Left, FlowDirection.LeftToRight, noWrap:true);
                var operations = paragraph?.CapsOperations();
                if (operations?.Length == joined.Length) context.Operations = operations;
                if (positions && paragraph?.PositionModes() is { } modes && modes.Length == joined.Length)
                    context.PositionModes = modes;
            }
            foreach (var source in sources)
            {
                if (source.Atomic || source.EmptyFrame) continue;
                source.CapsContext = context;
                source.Text.SetSharedCapsContext(context, source.CapsOffset);
            }
        }
    }
}
