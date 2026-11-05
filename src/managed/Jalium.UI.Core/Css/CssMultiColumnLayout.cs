using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Column fragments share source visuals, shaped text and automation identity.</summary>
internal sealed partial class CssMultiColumnLayout(FrameworkElement owner) : ICssColumnLayout
{
    private FrameworkElement Owner => owner;
    private static readonly ConditionalWeakTable<FrameworkElement, CssMultiColumnLayout> s_layouts = new();
    [ThreadStatic] private static CssMultiColumnLayout? s_building;
    [ThreadStatic] private static CssMultiColumnLayout? s_applying;
    private readonly List<Node> _nodes = [];
    private readonly List<TextBlock.CssColumnTextSnapshot> _texts = [];
    private readonly List<Slice> _slices = [];
    private readonly List<Rect> _rules = [];
    private readonly HashSet<FrameworkElement> _fragmentedElements = [];
    private readonly HashSet<FrameworkElement> _demotedSpanners = [];
    private readonly Dictionary<UIElement, double> _floatTopOverrides = [];
    private readonly Dictionary<UIElement, double> _floatWrapBottomLimits = [];
    private CssColumnFragmentation _breaks = new();
    private Thickness _insets;
    private double _width, _columnWidth, _gap, _height, _minimumHeight, _columnContainingHeight = double.PositiveInfinity;
    private int _count;
    private bool _applied, _spannerDemoted, _floatConstraintsChanged;
    internal double FirstBaseline { get; private set; } = double.NaN;
    internal double LastBaseline { get; private set; } = double.NaN;
    internal bool IsApplied => _applied && CssMultiColumnProperties.Active(owner);
    bool ICssColumnLayout.IsApplied => IsApplied;
    bool ICssColumnLayout.Contains(FrameworkElement source) => Contains(source);
    internal static CssMultiColumnLayout For(FrameworkElement owner) => s_layouts.GetValue(owner, static element => new(element));
    internal static bool Try(FrameworkElement owner, out CssMultiColumnLayout? layout) => s_layouts.TryGetValue(owner, out layout);
    internal IReadOnlyList<Rect> Rules => _rules;
    internal void SetProjectedRules(IEnumerable<Rect> rules) { _rules.Clear(); _rules.AddRange(rules); }
    internal int ColumnCount => _count;
    internal double ColumnWidth => _columnWidth;
    internal double UsedHeight => _height;
    internal static bool IsCapturing(FrameworkElement element) => s_building is { } building && building.Contains(element);
    internal static bool TryMeasureApplying(FrameworkElement element, Size available, out Size result)
    {
        if (s_applying is { } applying && applying._fragmentedElements.Contains(element))
        {
            result = available;
            return true;
        }
        result = default;
        return false;
    }
    internal static bool TryArrangeApplying(FrameworkElement element, Size finalSize, out Size result)
    {
        if (s_applying is { } applying && applying._fragmentedElements.Contains(element))
        {
            element.ArrangeCssLayoutAbsoluteChildren(finalSize);
            result = finalSize;
            return true;
        }
        result = default;
        return false;
    }
    internal static bool TryColumnContainingHeight(FrameworkElement element, out double height)
    {
        if (s_building is { } building && ReferenceEquals(building.Owner, element) && double.IsFinite(building._columnContainingHeight))
        {
            height = building._columnContainingHeight;
            return true;
        }
        if (Try(element, out var layout) && layout is not null && double.IsFinite(layout._columnContainingHeight))
        {
            height = layout._columnContainingHeight;
            return true;
        }
        height = 0;
        return false;
    }
    internal static void AdjustFloatTop(FrameworkElement flowOwner, UIElement floatElement, ref double y)
    {
        if (s_building is { } building && building._floatTopOverrides.TryGetValue(floatElement, out var minTop))
        {
            var offset = flowOwner == building.Owner ? building._insets.Top : flowOwner.TransformToAncestor(building.Owner).Y;
            var localMinTop = minTop - offset;
            if (y < localMinTop) y = localMinTop;
        }
    }
    internal static double AdjustFloatRegionHeight(FrameworkElement flowOwner, UIElement floatElement, double y, double height)
    {
        if (s_building is { } building && building._floatWrapBottomLimits.TryGetValue(floatElement, out var maxBottom))
        {
            var offset = flowOwner == building.Owner ? building._insets.Top : flowOwner.TransformToAncestor(building.Owner).Y;
            var localMaxBottom = maxBottom - offset;
            if (y < localMaxBottom && y + height > localMaxBottom)
                return Math.Max(0, localMaxBottom - y);
        }
        return height;
    }
    internal bool Contains(FrameworkElement source)
    {
        for (Visual? current = source; current is not null; current = current.VisualParent)
            if (ReferenceEquals(current, owner)) return true;
        return false;
    }

    internal static void PropertiesChanged(FrameworkElement element)
    {
        if (Try(element, out var layout)) layout!.Clear();
        element.InvalidateMeasure(); element.InvalidateVisual();
        for (var parent = element.VisualParent as FrameworkElement; parent is not null; parent = parent.VisualParent as FrameworkElement)
        {
            parent.InvalidateMeasure();
            if (CssMultiColumnProperties.Active(parent)) break;
        }
    }

    internal void Clear()
    {
        ClearPassState();
        _demotedSpanners.Clear();
        _floatTopOverrides.Clear();
        _floatWrapBottomLimits.Clear();
        _columnContainingHeight = double.PositiveInfinity;
    }

    private void ClearPassState()
    {
        _applied = false;
        FirstBaseline = LastBaseline = double.NaN; owner.CssColumnInkPadding = 0;
        foreach (var text in _texts.AsEnumerable().Reverse()) text.Release();
        foreach (var node in _nodes)
        {
            if (node.Element.CssColumnBox?.Layout == this) node.Element.CssColumnBox = null;
            if (Try(node.Element, out var nested) && nested is not null && !ReferenceEquals(nested, this)) nested.Clear();
            node.Element.InvalidateCssAllocation();
        }
        _nodes.Clear(); _texts.Clear(); _slices.Clear(); _rules.Clear(); _fragmentedElements.Clear();
    }

    internal Size Measure(Size available)
    {
        Build(available.Width, available.Height, apply: false);
        return new(_width + _insets.Left + _insets.Right, _height + _insets.Top + _insets.Bottom);
    }

    internal Size Arrange(Size size)
    {
        Build(size.Width, size.Height, apply: true);
        return size;
    }

    private double DefiniteHeight(double fallback)
    {
        var basis = owner.CssParentBox?.ContainingBlock.Height ?? owner.PreviousAvailableSize.Height;
        var preferred = owner.CssPreferredHeight(basis); var bounds = owner.CssHeightBounds(basis);
        var height = double.IsFinite(preferred) ? preferred : bounds.Max;
        if (!double.IsFinite(height) && CssContentsLayout.BoxParent(owner) is FrameworkElement { CssDisplayMode: CssDisplayMode.Flex or CssDisplayMode.Grid }) height = fallback;
        return double.IsFinite(height) ? Math.Max(0, height - _insets.Top - _insets.Bottom) : double.PositiveInfinity;
    }

    private void Build(double availableWidth, double limit, bool apply)
    {
        Clear();
        _insets = CssTableLayout.ContentInsets(owner, CssBoxMetrics.ContentInsets(owner, owner.CssLayout?.ContainingWidthCache ?? availableWidth));
        var basis = owner.CssParentBox?.ContainingBlock.Height ?? owner.PreviousAvailableSize.Height;
        _minimumHeight = Math.Max(0, owner.CssHeightBounds(basis).Min - _insets.Top - _insets.Bottom);
        // Insets may have changed with percentage padding since Measure.
        limit = DefiniteHeight(limit);
        _columnContainingHeight = limit;
        var flow = CssDisplayLayout.FlowFor(owner);
        if (!double.IsFinite(availableWidth))
        {
            var natural = flow.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            var hint = ((CssLayoutLength)owner.GetValue(CssMultiColumnProperties.WidthProperty)!).Resolve(0, 0);
            var count = Math.Clamp((int)owner.GetValue(CssMultiColumnProperties.CountProperty)!, 1, 1000);
            availableWidth = Math.Max(hint, Math.Max(0, natural.Width - _insets.Left - _insets.Right)) * count +
                CssMultiColumnProperties.Gap(owner, 0) * (count - 1) + _insets.Left + _insets.Right;
        }
        _width = Math.Max(0, availableWidth - _insets.Left - _insets.Right);
        (_count, _columnWidth, _gap) = CssMultiColumnProperties.Columns(owner, _width);
        Size naturalSize = default;
        for (var pass = 0; pass < 4; pass++)
        {
            if (pass > 0)
            {
                ClearPassState();
                owner.InvalidateMeasure();
            }
            _spannerDemoted = false;
            _floatConstraintsChanged = false;
            var old = s_building; s_building = this;
            try
            {
                var narrow = new Size(_columnWidth + _insets.Left + _insets.Right, double.PositiveInfinity);
                naturalSize = flow.Measure(narrow);
                flow.Arrange(new(narrow.Width, naturalSize.Height));
                Capture();
            }
            finally { s_building = old; }
            Plan(Math.Max(_insets.Top, naturalSize.Height - _insets.Bottom), limit);
            if (!_spannerDemoted && !_floatConstraintsChanged) break;
        }
        FirstBaseline = double.IsFinite(flow.FirstBaseline) && _slices.Count > 0 ? Project(new(_insets.Left, flow.FirstBaseline)).Y : double.NaN;
        LastBaseline = double.IsFinite(flow.LastBaseline) && _slices.Count > 0 ? Project(new(_insets.Left, flow.LastBaseline)).Y : double.NaN;
        var overflow = _slices.Count == 0 ? 0 : _slices.Max(slice => Math.Max(0, Math.Max(_insets.Left - slice.Bounds.Left, slice.Bounds.Right - _width - _insets.Left)));
        owner.CssColumnInkPadding = (int)Math.Min(int.MaxValue, Math.Ceiling(overflow));
        if (apply) Apply();
    }

    internal static double? SpannerWidth(FrameworkElement element)
    {
        if (s_building is not { } layout || layout._demotedSpanners.Contains(element) || !(bool)element.GetValue(CssMultiColumnProperties.SpanProperty)! ||
            element.CssInlineOuter || element.CssLayout?.IsOutOfFlow == true || CssFloatProperties.ActiveSide(element) != CssFloatSide.None) return null;
        for (var parent = CssContentsLayout.BoxParent(element); parent is not null; parent = CssContentsLayout.BoxParent(parent))
        {
            if (ReferenceEquals(parent, layout.Owner)) return layout._width;
            if (!parent.CanFragmentCssColumns || parent.CssDisplayMode != CssDisplayMode.Block) return null;
        }
        return null;
    }

    internal static Geometry? Clip(Visual visual, Geometry? original)
    {
        if (visual is not FrameworkElement { IsCssColumnFrame: true, CssColumnBox: { } box }) return original;
        return original is null ? box.Clip : Geometry.Combine(original, box.Clip, GeometryCombineMode.Intersect, null);
    }
}
