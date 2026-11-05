using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssClampParagraphKind { Native, Shared, Atomic }
internal readonly record struct CssClampParagraphKey(UIElement Element, CssClampParagraphKind Kind = CssClampParagraphKind.Native);
internal readonly record struct CssClampLine(CssClampParagraphKey Key, FrameworkElement Owner, int Index,
    double Top, double Height, int End);
internal readonly record struct CssClampContent(UIElement? Box = null, TextBlock? Text = null,
    TextBlock.CssTextFlowLayout? Inline = null, CssClampLine? Line = null, bool Floating = false);
internal sealed class CssClampParagraphLimit(int count)
{
    internal int Count = count;
    internal int? End;
    internal bool IsClampPoint = true;
}

/// <summary>Plans suppression over the original block formatting context, then lays out its retained content.</summary>
internal sealed class CssLineClampLayout(FrameworkElement owner)
{
    private FrameworkElement Owner => owner;
    private static readonly ConditionalWeakTable<FrameworkElement, CssLineClampLayout> s_layouts = new();
    private static readonly ConditionalWeakTable<UIElement, WeakReference<CssLineClampLayout>> s_suppression = new();
    private static readonly ConditionalWeakTable<UIElement, WeakReference<CssLineClampLayout>> s_floatClips = new();
    [ThreadStatic] private static Pass? s_pass;
    private static int s_stamp;
    private sealed record Pass(CssLineClampLayout Layout, Plan? Plan, int Stamp, bool Balanced,
        IReadOnlySet<FrameworkElement>? DocumentParagraphs = null);
    private sealed class Plan
    {
        internal readonly Dictionary<CssClampParagraphKey, CssClampParagraphLimit> Paragraphs = [];
        internal readonly HashSet<UIElement> Suppressed = new(ReferenceEqualityComparer.Instance);
        internal bool FrozenSource;
    }
    private readonly record struct Unit(CssClampLine? Line, double Bottom);
    private readonly record struct BoxRange(UIElement Element, int Start, int End, bool Floating);
    private readonly List<Unit> _units = [];
    private readonly List<BoxRange> _boxes = [];
    private readonly HashSet<TextBlock> _texts = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<UIElement> _suppressed = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<UIElement> _floats = new(ReferenceEqualityComparer.Instance);
    private readonly List<TextBlock.CssTextFlowLayout> _shared = [];
    private bool _applied;
    private Thickness _insets;
    private double _height;
    internal double FirstBaseline { get; private set; } = double.NaN;
    internal double LastBaseline { get; private set; } = double.NaN;
    internal bool IsApplied => _applied && CssLineClampProperties.Active(owner);
    internal static CssLineClampLayout For(FrameworkElement element) => s_layouts.GetValue(element, static value => new(value));
    internal static int PassStamp => s_pass?.Stamp ?? 0;
    internal static bool HasDocumentPass => s_pass?.DocumentParagraphs is not null;
    internal static bool UseEndpoint => s_pass is { Balanced: true } or { Plan.FrozenSource: true };
    internal static CssClampParagraphLimit? Limit(UIElement key, bool shared = false)
    {
        if (key is TextBlock { IsDocumentParagraph: true, CssIntrinsicMeasure: true }) return null;
        var value = s_pass?.Plan?.Paragraphs.GetValueOrDefault(new(key,
            shared ? CssClampParagraphKind.Shared : CssClampParagraphKind.Native));
        return value ?? (HasDocumentPass || key is not TextBlock text ? null : text.DocumentClampLimit);
    }
    internal static IDisposable BeginDocumentPass(FrameworkElement owner,
        IReadOnlyDictionary<TextBlock, CssClampParagraphLimit> limits, IReadOnlySet<FrameworkElement> paragraphs,
        bool balanced, bool frozen = false)
        => BeginDocumentPass(owner, limits, paragraphs, balanced, frozen, null);

    internal static IDisposable BeginDocumentPass(FrameworkElement owner,
        IReadOnlyDictionary<TextBlock, CssClampParagraphLimit> limits, IReadOnlySet<FrameworkElement> paragraphs,
        bool balanced, bool frozen, IReadOnlyDictionary<TextBlock, CssClampParagraphLimit>? shared)
    {
        var plan = new Plan { FrozenSource = frozen };
        foreach (var pair in limits) plan.Paragraphs.Add(new(pair.Key), pair.Value);
        if (shared is not null)
            foreach (var pair in shared) plan.Paragraphs.Add(new(pair.Key, CssClampParagraphKind.Shared), pair.Value);
        var previous = s_pass;
        s_pass = new(new(owner), plan, System.Threading.Interlocked.Increment(ref s_stamp), balanced, paragraphs);
        return new RestorePass(previous);
    }
    private sealed class RestorePass(Pass? previous) : IDisposable
    {
        public void Dispose() => s_pass = previous;
    }
    internal static bool Participates(FrameworkElement element)
    {
        if (s_pass is not { } pass) return false;
        if (pass.DocumentParagraphs is { } paragraphs) return paragraphs.Contains(element);
        if (element is TextBlock { IsDocumentParagraph: true }) return false;
        for (Visual? current = element; current is not null; current = current.VisualParent)
        {
            if (ReferenceEquals(current, pass.Layout.Owner)) return true;
            // A paragraph's native text box can participate without being a
            // CSS flow host. Independent block containers start their own BFC.
            if (current is FrameworkElement box && CssDisplayLayout.IsFlow(box) && CssFloatProperties.IsIndependentBox(box)) return false;
        }
        return false;
    }
    internal static bool Stable(FrameworkElement element) => s_pass is { Balanced: false } && Participates(element);
    internal static bool Excluded(UIElement element) => s_pass?.Plan?.Suppressed.Contains(element) == true;
    internal static void Register(TextBlock text)
    { if (s_pass is { } pass && pass.Layout.Contains(text)) pass.Layout._texts.Add(text); }
    internal static void Suppress(UIElement element)
    { if (s_pass is { Plan: { } plan }) plan.Suppressed.Add(element); }

    private bool Contains(Visual element)
    {
        for (Visual? current = element; current is not null; current = current.VisualParent)
            if (ReferenceEquals(current, owner)) return true;
        return false;
    }

    internal static bool VisuallySuppressed(UIElement element) => s_suppression.TryGetValue(element, out var reference) &&
        reference.TryGetTarget(out var layout) && layout.IsApplied && layout._suppressed.Contains(element) && layout.Contains(element);

    internal static bool SuppressedInTree(Visual visual)
    {
        for (Visual? current = visual; current is not null; current = current.VisualParent)
            if (current is UIElement element && VisuallySuppressed(element)) return true;
        return false;
    }

    internal static bool HitSuppressed(UIElement element)
    {
        if (SuppressedInTree(element)) return true;
        // A zero-area rectangle still contains its origin in the geometry
        // API. An empty clamped float must not acquire that one-point target.
        return Clip(element, null) is RectangleGeometry rectangle &&
            (rectangle.Rect.Width <= 0 || rectangle.Rect.Height <= 0);
    }

    internal static Geometry? Clip(Visual visual, Geometry? original)
    {
        if (visual is not UIElement element) return original;
        if (VisuallySuppressed(element)) return new RectangleGeometry(default(Rect));
        if (!s_floatClips.TryGetValue(element, out var reference) || !reference.TryGetTarget(out var layout) ||
            !layout.IsApplied || !layout.Contains(element)) return original;
        var top = element is FrameworkElement box ? box.TransformToAncestor(layout.Owner).Y : 0;
        var bottom = Math.Max(0, layout.Owner.RenderSize.Height - layout._insets.Bottom - top);
        var clip = new RectangleGeometry(new Rect(0, 0, element.RenderSize.Width, Math.Min(bottom, element.RenderSize.Height)));
        return original is null ? clip : Geometry.Combine(original, clip, GeometryCombineMode.Intersect, null);
    }

    internal static void PropertiesChanged(FrameworkElement element)
    {
        for (FrameworkElement? current = element; current is not null; current = current.VisualParent as FrameworkElement)
            if (s_layouts.TryGetValue(current, out var layout)) layout.Clear();
        element.InvalidateMeasure(); element.InvalidateVisual();
    }

    internal void Clear()
    {
        _applied = false;
        foreach (var element in _suppressed)
        {
            if (s_suppression.TryGetValue(element, out var reference) && reference.TryGetTarget(out var current) && ReferenceEquals(current, this))
                s_suppression.Remove(element);
            element.InvalidateVisual();
        }
        foreach (var text in _texts)
        {
            text.InvalidateCssTextLayout();
            // Once clamping is removed the same containing block can be
            // measured again. Its cached desired size still reflects the old
            // prefix even if a range query has rebuilt the full text layout.
            text.InvalidateMeasure();
        }
        foreach (var element in _floats)
        {
            if (s_floatClips.TryGetValue(element, out var reference) && reference.TryGetTarget(out var current) && ReferenceEquals(current, this))
                s_floatClips.Remove(element);
            element.InvalidateVisual();
        }
        _floats.Clear();
        _texts.Clear(); _suppressed.Clear(); _units.Clear(); _boxes.Clear(); _shared.Clear();
    }

    internal Size Measure(Size available) => Build(available.Width);
    internal Size Arrange(Size size) { Build(size.Width); return size; }

    private Size Build(double width)
    {
        Clear();
        _insets = CssTableLayout.ContentInsets(owner, CssBoxMetrics.ContentInsets(owner, owner.CssLayout?.ContainingWidthCache ?? width));
        var flow = CssDisplayLayout.FlowFor(owner);
        var previous = s_pass;
        Size Layout(Plan? plan, bool balanced)
        {
            s_pass = new(this, plan, System.Threading.Interlocked.Increment(ref s_stamp), balanced);
            void Invalidate(Visual node)
            {
                if (node is UIElement element) element.InvalidateMeasure();
                foreach (var child in node.SnapshotAttachedVisualChildren()) Invalidate(child);
            }
            Invalidate(owner);
            var natural = flow.Measure(new(width, double.PositiveInfinity));
            flow.Arrange(new(double.IsFinite(width) ? width : natural.Width, natural.Height));
            return natural;
        }
        try
        {
            var full = Layout(null, false);
            Capture();
            var floatingBoxes = _boxes.Where(static box => box.Floating).Select(static box => box.Element).ToArray();
            var maxLines = (CssMaxLines)owner.GetValue(CssLineClampProperties.MaxLinesProperty)!;
            var limit = HeightLimit();
            var cut = -1; var count = 0;
            for (var i = 0; i < _units.Count; i++)
            {
                if (_units[i].Line is not null) count++;
                if (maxLines.Auto && _units[i].Bottom + _insets.Bottom > limit + .000001) { cut = i - 1; break; }
                if (maxLines.Count > 0 && count == maxLines.Count && i + 1 < _units.Count) { cut = i; break; }
            }
            var hasCut = cut >= 0 || _units.Count > 0 && maxLines.Auto && _units[0].Bottom + _insets.Bottom > limit + .000001;
            Plan? plan = hasCut ? MakePlan(cut) : null;
            var needsReflow = _units.Any(unit => unit.Line?.Owner.GetValue(CssFlowProperties.TextWrapStyleProperty) is
                CssTextWrapStyle.Balance or CssTextWrapStyle.Pretty or CssTextWrapStyle.AvoidShortLastLine);
            var visible = plan is null ? full : Layout(plan, false);
            // The retained source endpoint is recorded by the stable pass.
            // Balancing can now consider that prefix and its anonymous marker.
            if (needsReflow) visible = Layout(plan, true);
            // Moving a taller inline to an earlier line can increase the block
            // size after balancing. Re-evaluate the auto clamp point against
            // those actual line boxes. Rebalancing the shorter prefix is optional.
            if (needsReflow && maxLines.Auto && visible.Height > limit + .000001)
            {
                Capture();
                var earlier = -1;
                while (earlier + 1 < _units.Count && _units[earlier + 1].Bottom + _insets.Bottom <= limit + .000001) earlier++;
                var shorter = MakePlan(earlier, frozen: true);
                if (plan is not null)
                {
                    shorter.Suppressed.UnionWith(plan.Suppressed);
                    foreach (var pair in plan.Paragraphs) shorter.Paragraphs.TryAdd(pair.Key, new(0));
                }
                plan = shorter; visible = Layout(plan, false);
            }
            _height = visible.Height;
            FirstBaseline = flow.FirstBaseline; LastBaseline = flow.LastBaseline;
            _shared.Clear(); CollectShared(owner);
            if (plan is not null)
                foreach (var element in plan.Suppressed)
                {
                    _suppressed.Add(element); s_suppression.Remove(element); s_suppression.Add(element, new(this)); element.InvalidateVisual();
                }
            _applied = true;
            foreach (var element in floatingBoxes)
                if (!_suppressed.Contains(element))
                {
                    _floats.Add(element); s_floatClips.Remove(element); s_floatClips.Add(element, new(this)); element.InvalidateVisual();
                }
            UIElement.InvalidateHitTestCache(); owner.InvalidateVisual();
            return new(full.Width, _height);
        }
        finally { s_pass = previous; }
    }

    private double HeightLimit()
    {
        if (owner.CssIntrinsicMeasure) return double.PositiveInfinity;
        var basis = owner.CssParentBox?.ContainingBlock.Height ?? owner.PreviousAvailableSize.Height;
        var preferred = owner.CssPreferredHeight(basis); var maximum = owner.CssHeightBounds(basis).Max;
        return double.IsFinite(preferred) ? Math.Min(preferred, maximum) : maximum;
    }

    private void Capture()
    {
        _units.Clear(); _boxes.Clear();
        var seen = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);
        void Text(TextBlock text)
        {
            if (!seen.Add(text)) return;
            _texts.Add(text);
            foreach (var line in text.CssClampLines()) AddLine(line);
        }
        void AddLine(CssClampLine line)
        {
            var at = line.Owner.TransformToAncestor(owner);
            _units.Add(new(line, at.Y + line.Top + line.Height));
        }
        void Contents(IEnumerable<CssClampContent> contents)
        {
            foreach (var content in contents)
            {
                if (content.Line is { } line) AddLine(line);
                else if (content.Text is { } text) Text(text);
                else if (content.Inline is { } inline) Contents(inline.ClampContents);
                else if (content.Box is FrameworkElement box)
                {
                    var start = _units.Count;
                    if (!content.Floating)
                    {
                        if (box is TextBlock native && !CssDisplayLayout.HasBoxFormatter(native)) Text(native);
                        else if (box is TextBlock { CanFormatCssInline: true, HasCssFlowParagraph: false } textFlow &&
                            !CssFloatProperties.IsIndependentBox(textFlow)) Contents(textFlow.CssTextFlow.ClampContents);
                        else if (CssDisplayLayout.IsFlow(box) && !CssFloatProperties.IsIndependentBox(box)) Contents(CssDisplayLayout.FlowFor(box).ClampContents);
                        else _units.Add(new(null, box.TransformToAncestor(owner).Y + box.RenderSize.Height));
                        // An empty in-flow block is still a possible clamp
                        // boundary and can intervene between a line and the point.
                        if (_units.Count == start) _units.Add(new(null, box.TransformToAncestor(owner).Y + box.RenderSize.Height));
                    }
                    _boxes.Add(new(box, start, _units.Count, content.Floating));
                }
            }
        }
        Contents(CssDisplayLayout.FlowFor(owner).ClampContents);
    }

    private Plan MakePlan(int cut, bool frozen = false)
    {
        var plan = new Plan { FrozenSource = frozen };
        foreach (var unit in _units.Skip(cut + 1))
            if (unit.Line is { } line) plan.Paragraphs.TryAdd(line.Key, new(0));
        // One inline paragraph can resume after an intervening block. A later
        // discarded segment must not erase its earlier retained lines. Such a
        // prefix is not the clamp point: it gets no marker or source endpoint.
        foreach (var unit in _units.Take(cut + 1))
            if (unit.Line is { } retained && plan.Paragraphs.TryGetValue(retained.Key, out var prefix))
            {
                prefix.Count = Math.Max(prefix.Count, retained.Index + 1);
                prefix.IsClampPoint = false;
            }
        if (cut >= 0 && _units[cut].Line is { } last)
            plan.Paragraphs[last.Key] = new(last.Index + 1) { End = frozen ? last.End : null };
        foreach (var box in _boxes)
            if (box.Start > cut && (box.End > box.Start || box.Floating)) plan.Suppressed.Add(box.Element);
        return plan;
    }

    private void CollectShared(FrameworkElement element)
    {
        void Contents(IEnumerable<CssClampContent> contents)
        {
            foreach (var content in contents)
            {
                if (content.Inline is { } inline)
                { if (!_shared.Contains(inline)) { _shared.Add(inline); Contents(inline.ClampContents); } }
                else if (content.Box is FrameworkElement box && !CssFloatProperties.IsIndependentBox(box))
                {
                    if (box is TextBlock { CanFormatCssInline: true, HasCssFlowParagraph: false } text)
                    { _shared.Add(text.CssTextFlow); Contents(text.CssTextFlow.ClampContents); }
                    else if (CssDisplayLayout.IsFlow(box)) CollectShared(box);
                }
            }
        }
        Contents(CssDisplayLayout.FlowFor(element).ClampContents);
    }

    internal static void PaintMarkers(Visual visual, DrawingContext context)
    {
        if (visual is not FrameworkElement element || !s_layouts.TryGetValue(element, out var layout) || !layout.IsApplied) return;
        foreach (var inline in layout._shared)
        {
            var at = inline.Owner.TransformToAncestor(element);
            var insets = CssBoxMetrics.ContentInsets(inline.Owner, inline.Owner.CssLayout?.ContainingWidthCache ?? inline.Owner.RenderSize.Width);
            inline.PaintClampMarkers(context, new(at.X + insets.Left, at.Y + insets.Top));
        }
    }
}
