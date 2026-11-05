using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal sealed partial class CssTableLayout
{
    private sealed class CellBox(FrameworkElement element, bool anonymous)
    {
        internal FrameworkElement Element = element;
        internal bool Anonymous = anonymous, Empty;
        internal CssLayoutBox Box = new(element, 0);
        internal CssTableTracks.Cell Track = null!;
        internal Rect Bounds;
        internal double ContentHeight;
    }
    private sealed class RowBox(FrameworkElement? source, FrameworkElement? group)
    {
        internal FrameworkElement? Source = source, Group = group;
        internal List<CellBox> Cells = [];
        internal Rect Bounds;
    }
    private sealed record ColumnBox(FrameworkElement Source, FrameworkElement? Group, int First, int Count);
    private sealed class CaptionBox(FrameworkElement element)
    {
        internal FrameworkElement Element = element;
        internal CssLayoutBox Box = new(element, 0);
        internal Thickness Margin;
        internal Rect Bounds;
        internal bool Bottom => (CssCaptionSide)Element.GetValue(CssTableProperties.CaptionSideProperty)! == CssCaptionSide.Bottom;
    }
    private sealed class Model
    {
        internal List<RowBox> Rows = [];
        internal List<ColumnBox> Columns = [];
        internal List<CellBox> Cells = [];
        internal List<CssTableTracks.MissingSlot> Missing = [];
        internal List<(int Row, int Column, Rect Bounds)> MissingRegions = [];
        internal List<CaptionBox> Captions = [];
        internal FrameworkElement? Header, Footer;
        internal Dictionary<FrameworkElement, int> GroupStarts = [];
        internal Dictionary<FrameworkElement, Rect> Slots = [];
        internal Dictionary<FrameworkElement, Rect[]> Regions = [];
        internal CssTableTracks.Axis Horizontal, Vertical;
        internal double Baseline = double.NaN;
        internal Thickness Insets;
        internal Rect GridBounds;
        internal double CaptionHeight;
        internal CssCollapsedTableBorders? Borders;
        internal CssTableVisibility Visibility = null!;
        internal CssTableTracks.Axis FullHorizontal, FullVertical;
        internal Size RemovedSize;
        internal Size Desired;
    }
    private readonly List<CssTableAnonymousBox> _anonymous = [];
    private readonly HashSet<CssTableAnonymousBox> _usedAnonymous = [];

    internal static IEnumerable<UIElement> RawChildren(FrameworkElement owner)
    {
        if (owner is CssTableAnonymousBox anonymous) return anonymous.Items;
        owner.PrepareCssContent();
        return new CssPanelChildren(owner, includeInactive: true).Enumerate().Where(child => child is not CssTableAnonymousBox);
    }
    private static IEnumerable<UIElement> InFlow(FrameworkElement owner) => RawChildren(owner)
        .Where(child => child.Visibility != Visibility.Collapsed && child is not FrameworkElement { CssLayout.IsOutOfFlow: true });

    private CssTableAnonymousBox Anonymous(FrameworkElement parent, UIElement[] items)
    {
        var result = _anonymous.FirstOrDefault(box => ReferenceEquals(box.Owner, parent) && box.Items.SequenceEqual(items));
        if (result is null) { result = new(parent, items, table: false); _anonymous.Add(result); }
        _usedAnonymous.Add(result); return result;
    }

    private Model BuildModel()
    {
        var model = new Model(); _usedAnonymous.Clear();
        void Row(FrameworkElement? source, FrameworkElement? group, FrameworkElement parent, IEnumerable<UIElement> children)
        {
            var row = new RowBox(source, group); model.Rows.Add(row);
            var pending = new List<UIElement>();
            void Flush()
            {
                if (pending.Count == 0) return;
                if (pending.All(IsWhitespace)) { pending.Clear(); return; }
                row.Cells.Add(new(Anonymous(parent, pending.ToArray()), true)); pending.Clear();
            }
            foreach (var child in children)
            {
                if (child is FrameworkElement element && Role(element) == CssDisplayMode.TableCell)
                { Flush(); row.Cells.Add(new(element, false)); }
                else pending.Add(child);
            }
            Flush();
        }
        var headers = new List<RowBox>(); var footers = new List<RowBox>();
        void Columns(FrameworkElement column, FrameworkElement? group)
        {
            var first = model.Columns.Sum(item => item.Count);
            model.Columns.Add(new(column, group, first, Math.Clamp(Grid.GetColumnSpan(column), 1, 1000)));
        }
        void Group(FrameworkElement group)
        {
            var start = model.Rows.Count; var pending = new List<UIElement>();
            model.GroupStarts[group] = start;
            void Flush()
            {
                if (pending.Count == 0) return;
                if (pending.All(IsWhitespace)) { pending.Clear(); return; }
                Row(null, group, group, pending.ToArray()); pending.Clear();
            }
            foreach (var child in InFlow(group))
            {
                if (child is FrameworkElement row && Role(row) == CssDisplayMode.TableRow)
                {
                    Flush();
                    if (row.IsCssContentHost) Row(row, group, row, InFlow(row));
                    else Row(null, group, group, [row]);
                }
                else pending.Add(child);
            }
            Flush();
            var kind = Role(group);
            List<RowBox>? selected = null;
            if (kind == CssDisplayMode.TableHeaderGroup && model.Header is null) { model.Header = group; selected = headers; }
            if (kind == CssDisplayMode.TableFooterGroup && model.Footer is null) { model.Footer = group; selected = footers; }
            if (selected is not null) { selected.AddRange(model.Rows.GetRange(start, model.Rows.Count - start)); model.Rows.RemoveRange(start, model.Rows.Count - start); }
        }
        var consecutive = new List<UIElement>();
        void FlushRoot()
        {
            if (consecutive.Count == 0) return;
            if (consecutive.All(IsWhitespace)) { consecutive.Clear(); return; }
            Row(null, null, StyleOwner, consecutive.ToArray()); consecutive.Clear();
        }
        var rootRole = Role(owner);
        if (rootRole == CssDisplayMode.TableRow) Row(null, null, StyleOwner, InFlow(owner));
        else if (IsGroup(rootRole)) Group(owner);
        else foreach (var child in InFlow(owner))
        {
            if (child is not FrameworkElement element) { consecutive.Add(child); continue; }
            var mode = Role(element);
            if (mode == CssDisplayMode.TableCaption) { FlushRoot(); model.Captions.Add(new(element)); }
            else if (mode == CssDisplayMode.TableRow)
            { FlushRoot(); if (element.IsCssContentHost) Row(element, null, element, InFlow(element)); else Row(null, null, StyleOwner, [element]); }
            else if (IsGroup(mode)) { FlushRoot(); if (element.IsCssContentHost) Group(element); else Row(null, null, StyleOwner, [element]); }
            else if (mode == CssDisplayMode.TableColumn) { FlushRoot(); Columns(element, null); }
            else if (mode == CssDisplayMode.TableColumnGroup)
            {
                FlushRoot(); var start = model.Columns.Count;
                foreach (var column in RawChildren(element).OfType<FrameworkElement>().Where(column => Role(column) == CssDisplayMode.TableColumn)) Columns(column, element);
                if (start == model.Columns.Count) Columns(element, element);
            }
            else consecutive.Add(child);
        }
        FlushRoot(); model.Rows.InsertRange(0, headers); model.Rows.AddRange(footers);
        foreach (var group in model.GroupStarts.Keys.ToArray())
            model.GroupStarts[group] = ReferenceEquals(group, model.Header) ? 0 : ReferenceEquals(group, model.Footer)
                ? model.Rows.Count - footers.Count : model.GroupStarts[group] + headers.Count;
        foreach (var row in model.Rows) model.Cells.AddRange(row.Cells);
        foreach (var unused in _anonymous.Where(box => !_usedAnonymous.Contains(box)).ToArray())
        { unused.Detach(); _anonymous.Remove(unused); }
        return model;
    }

    private static bool IsWhitespace(UIElement element) => element switch
    {
        CssGeneratedText generated => generated.CssInlineOuter && generated.CssDisplayMode == CssDisplayMode.Native && !generated.HasImageContent && CollapsedWhitespace(generated),
        ICssAnonymousTextBox { Owner: TextBlock text } => CollapsedWhitespace(text),
        TextBlock text => text.CssInlineOuter && text.CssDisplayMode == CssDisplayMode.Native && !text.HasCssInlineBoxes && CollapsedWhitespace(text),
        _ => false,
    };
    private static bool CollapsedWhitespace(TextBlock text)
    {
        if (text.Inlines.Any(inline => inline is not Jalium.UI.Documents.Run)) return false;
        var mode = (CssWhiteSpaceCollapse)text.GetValue(CssFlowProperties.WhiteSpaceProperty)!;
        return mode is CssWhiteSpaceCollapse.Collapse or CssWhiteSpaceCollapse.Discard && text.Text.All(c => c is ' ' or '\t' or '\r' or '\n' or '\f') ||
            mode == CssWhiteSpaceCollapse.PreserveBreaks && text.Text.All(c => c is ' ' or '\t');
    }
    private static bool IsEmpty(FrameworkElement element)
    {
        if (!element.IsCssContentHost) return false;
        return !RawChildren(element).Any(child => child.Visibility != Visibility.Collapsed && child is not FrameworkElement { CssLayout.IsOutOfFlow: true } &&
            !(child is ICssAnonymousTextBox { Owner: TextBlock text } && CollapsedWhitespace(text)) && !IsWhitespace(child));
    }
    internal static bool IsInternal(CssDisplayMode mode) => mode is CssDisplayMode.TableRow or CssDisplayMode.TableCell or
        CssDisplayMode.TableRowGroup or CssDisplayMode.TableHeaderGroup or CssDisplayMode.TableFooterGroup or CssDisplayMode.TableColumn or CssDisplayMode.TableColumnGroup;
    internal static bool IsPart(CssDisplayMode mode) => IsInternal(mode) || mode == CssDisplayMode.TableCaption;
    private static bool IsGroup(CssDisplayMode mode) => mode is CssDisplayMode.TableRowGroup or CssDisplayMode.TableHeaderGroup or CssDisplayMode.TableFooterGroup;
    internal static CssDisplayMode Role(FrameworkElement element)
    {
        var mode = element.CssDisplayMode;
        if (!IsPart(mode)) return mode;
        if (element.FrameworkParent is null || element.CssLayout?.IsOutOfFlow == true || (CssFloatSide)element.GetValue(CssFloatProperties.FloatProperty)! != CssFloatSide.None ||
            CssContentsLayout.BoxParent(element) is FrameworkElement { CssDisplayMode: CssDisplayMode.Flex or CssDisplayMode.Grid } or FlexPanel { CssDisplayMode: CssDisplayMode.Native }) return CssDisplayMode.Block;
        return mode;
    }
    internal static bool ColumnSuppressed(DependencyObject source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.VisualParent)
            if (visual is FrameworkElement element && Role(element) is CssDisplayMode.TableColumn or CssDisplayMode.TableColumnGroup) return true;
        return false;
    }
}
