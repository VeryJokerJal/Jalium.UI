using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal sealed partial class CssTextFlowLayout
    {
        private CssInlineSplitOptions? _splitOptions;
        private Func<UIElement, double, CssMarginStrut, bool, CssInlineBlockPlacement>? _placeBlock;
        private int _paragraphEnd;
        internal CssInlineSplitFlow? SplitFlow { get; private set; }
        internal IEnumerable<FrameworkElement> BlockElements => _sources.Where(static source => source.Block)
            .Select(static source => source.Element).OfType<FrameworkElement>();

        internal void SetBlockFlow(CssInlineSplitOptions options,
            Func<UIElement, double, CssMarginStrut, bool, CssInlineBlockPlacement> place)
        { _splitOptions = options; _placeBlock = place; }

        internal static bool HasNestedBlocks(UIElement element)
        {
            if (!IsInlineFrame(element)) return false;
            foreach (var child in new CssPanelChildren((FrameworkElement)element))
            {
                if (child.Visibility == Visibility.Collapsed || child is FrameworkElement { CssLayout.IsOutOfFlow: true } ||
                    CssFloatProperties.ActiveSide(child) != CssFloatSide.None || child is ICssAnonymousTextBox { Owner: TextBlock }) continue;
                if (child is not FrameworkElement { CssInlineOuter: true } || HasNestedBlocks(child)) return true;
            }
            return false;
        }

        private void AppendFrameMarker(CssInlineFrame frame, List<CssInlineUnit> pending)
        {
            var host = frame.Element;
            var strut = new TextBlock
            {
                _wordSpacingStyleSource = host,
                IsDocumentBoxContent = true,
                FontFamily = (FontFamily)host.GetValue(TextElement.FontFamilyProperty)!,
                FontSize = (double)host.GetValue(TextElement.FontSizeProperty)!,
                FontWeight = (FontWeight)host.GetValue(TextElement.FontWeightProperty)!,
                FontStyle = (FontStyle)host.GetValue(TextElement.FontStyleProperty)!,
                LineHeight = (double)host.GetValue(LineHeightProperty)!,
            };
            strut.SetValue(CssComputedLineHeightValue.Property, CssComputedLineHeightValue.Inherited(CssNode.Get(host)));
            var source = new CssInlineSource(host, strut, true, default, default)
                { Frame = frame, EmptyFrame = true, LastUnit = pending.Count };
            _sources.Add(source); pending.Add(new(source, 0, 0, false, false, false));
        }

        private void AppendBlock(UIElement element, CssInlineFrame frame, List<CssInlineUnit> pending)
        {
            var source = new CssInlineSource(element, element as TextBlock, false, default, default)
                { Frame = frame, Atomic = true, Block = true, LastUnit = pending.Count };
            _sources.Add(source); pending.Add(new(source, 0, 0, false, false, false));
        }

        private bool SegmentHasLine(List<CssInlineUnit> units, int start, int end)
        {
            var consumed = end;
            while (start < end && units[start].Collapse) start++;
            while (end > start && units[end - 1].Collapse) end--;
            if (start == end) return false;
            // A preserved forced break creates a line even without ink. This
            // emptiness probe can span several lines; only ParagraphRange may
            // request shaping, one physical line at a time.
            for (int index = start; index < end; index++)
                if (units[index].HardBreak) return true;
            return !FragmentMetrics(Pieces(units, start, end, 0, consumed)).Empty;
        }

        private void SplitParagraph(List<CssInlineUnit> units, bool firstFormattedLine, bool useTextIndent, double? indentBasis)
        {
            var options = _splitOptions ?? new CssInlineSplitOptions(_containing, default, false);
            var pending = options.Pending; var before = default(CssMarginStrut); var atStart = true; var lastClearEmpty = false;
            var start = 0;
            for (var index = 0; index <= units.Count; index++)
            {
                if (index != units.Count && !units[index].Source.Block) continue;
                var hasLine = SegmentHasLine(units, start, index);
                var oldY = _y;
                if (hasLine)
                {
                    if (atStart && options.TopEscapes) before = before.Merge(pending);
                    else _y += pending.Value;
                    pending = default; atStart = false; lastClearEmpty = false;
                }
                else if (!(atStart && options.TopEscapes)) _y += pending.Value;
                ParagraphRange(units, start, index, ref firstFormattedLine, useTextIndent, indentBasis);
                PlaceFloatsThrough(index, 0, 0);
                if (!hasLine) _y = oldY;
                if (index == units.Count) break;
                var source = units[index].Source;
                if (CssLineClampLayout.Excluded(source.Element)) { start = index + 1; continue; }
                if (_placeBlock is null) throw new InvalidOperationException("Split inline blocks require their outer block formatting context.");
                var placed = _placeBlock(source.Element, _y, pending, atStart && options.TopEscapes);
                source.BlockPlaced = true;
                _clampContents.Add(new(Box: source.Element));
                source.BlockRect = placed.Rect; source.AtomicSize = placed.Rect.Size;
                source.BlockEnvironment = placed.Environment;
                var adjoining = pending.Merge(placed.Before);
                if (placed.Through && !placed.HasClearance) pending = adjoining.Merge(placed.After);
                else
                {
                    if (atStart && options.TopEscapes && !placed.HasClearance) before = before.Merge(adjoining);
                    _y = placed.Rect.Bottom; pending = placed.After; atStart = false;
                    lastClearEmpty = placed.HasClearance && placed.Rect.Height == 0;
                    firstFormattedLine = false;
                }
                if (!double.IsFinite(FirstBaseline) && double.IsFinite(placed.FirstBaseline))
                    FirstBaseline = placed.Rect.Y + placed.FirstBaseline + _insets.Top;
                if (double.IsFinite(placed.Baseline)) LastBaseline = placed.Rect.Y + placed.Baseline + _insets.Top;
                _maximumWidth = Math.Max(_maximumWidth, placed.OuterWidth);
                start = index + 1;
            }
            SplitFlow = new(before, pending, atStart, lastClearEmpty);
        }
    }
}
