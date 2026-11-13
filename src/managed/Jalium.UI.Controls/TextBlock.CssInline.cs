using System.Text;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private CssTextFlowLayout? _cssTextFlow;
    private CssTextFlowLayout? _cssInlineParagraph;
    private Dictionary<int, CssInlineFragment>? _cssInlineFragments;
    private CssInlineFragment[]? _cssInlineFragmentList;
    private IEnumerable<CssInlineFragment> CssInlineFragmentValues =>
        (IEnumerable<CssInlineFragment>?)_cssInlineFragmentList ?? _cssInlineFragments!.Values;
    private CssInlineBoxFragment[] _cssDecorationFragments = [];
    private readonly Dictionary<Rect, CssInlineBoxFragment> _cssDecorationLookup = [];
    internal CssInlineBoxFragment[] CssDecorationFragments => IsCssInlineBox ? _cssDecorationFragments : [];
    internal bool TryGetCssDecorationFragment(Rect rect, out CssInlineBoxFragment fragment)
        => _cssDecorationLookup.TryGetValue(rect, out fragment);
    private Size _cssInlineAllocation;
    private bool _cssInlineWhitespace;
    private bool _cssInlineBox;
    private UIElement? _cssInlineLayoutElement;
    private Visual? _cssInlineExpectedParent;
    private Rect? _cssClampHitRect;
    private int _cssClampHitEnd;

    internal CssTextFlowLayout CssTextFlow => _cssTextFlow ??= new(this);
    internal bool HasCssFlowParagraph => _cssInlineParagraph is { IsRun: true } paragraph && ReferenceEquals(paragraph.Owner, this);
    internal bool CanFormatCssInline => this is not CssGeneratedText && CssDisplayLayout.IsFlow(this) &&
        CssParentBox?.Floats is not { Regions.Length: > 0 } &&
        new CssPanelChildren(this).Enumerate().Any(static child => child is CssGeneratedText
            { CssInlineOuter: true, CssDisplayMode: not CssDisplayMode.FlowRoot } generated && generated.CssLayout?.IsOutOfFlow != true) &&
        new CssPanelChildren(this).Enumerate().All(static child => CssFloatProperties.ActiveSide(child) == CssFloatSide.None && !CssTextFlowLayout.HasNestedFloats(child));

    private bool HasCssInlineFragments => _cssInlineFragments is not null && !_layoutDirty &&
        (HasCssColumnText || _cssInlineParagraph?.IsActive == true && _cssInlineLayoutElement is { } element &&
        ReferenceEquals(element.VisualParent, _cssInlineExpectedParent) &&
        _cssInlineParagraph.IsSourceActive(element) &&
        (_cssInlineBox ? CssTextFlowLayout.IsFragmentable(this) : element is ICssAnonymousTextBox { Owner: var origin } && ReferenceEquals(origin, this)));
    internal bool IsCssInlineBox => _cssInlineBox && HasCssInlineFragments;
    internal bool CssInlineContains(Point point) => !IsCssInlineBox ||
        _cssClampHitRect?.Contains(point) == true ||
        CssInlineFragmentValues.Any(fragment => fragment.Rect.Contains(point) &&
            (CssBorderRadiusProperties.Get(this) is null || CssRoundedRectangleGeometry.Contains(
                fragment.Rect, InlineFragmentRadii(fragment), point)));
    internal void ReleaseCssInlineAllocation(FrameworkElement owner)
    {
        if (_cssInlineParagraph?.Owner == owner) ClearCssInlineFragments();
    }
    private CssInlineFragment? InlineFragment(in TextLayoutLine line) => !HasCssInlineFragments ? null :
        line.InlineFragmentIndex >= 0 && _cssInlineFragmentList is { } list && line.InlineFragmentIndex < list.Length
            ? list[line.InlineFragmentIndex]
            : _cssInlineFragments!.TryGetValue(line.StartIndex, out var fragment) ? fragment : null;
    private double TextLineTop(in TextLayoutLine line, double fallback) => InlineFragment(line)?.TextY ?? fallback;
    internal bool TryCssInlineAllocation(out Size size)
    {
        size = _cssInlineAllocation;
        return HasCssInlineFragments;
    }
    private void ClearCssInlineFragments()
    {
        SetSharedTransformContext(null, 0);
        SetSharedCapsContext(null, 0);
        if (_cssInlineFragments is not null) _cssInlineLayoutElement?.InvalidateVisual();
        if (_cssInlineLayoutElement is { } element && element.CssInlineLayoutOwner == _cssInlineParagraph?.Owner)
            element.CssInlineLayoutOwner = null;
        if (_cssInlineFragments is not null || _cssInlineWhitespace) _layoutDirty = true;
        _cssInlineFragments = null;
        _cssInlineFragmentList = null;
        _cssClampHitRect = null;
        _documentSharedMarker = null;
        _cssDecorationFragments = [];
        _cssDecorationLookup.Clear();
        _cssInlineParagraph = null;
        _cssInlineWhitespace = false;
        _cssInlineBox = false;
        _cssInlineLayoutElement = null;
        _cssInlineExpectedParent = null;
    }

    protected internal override void OnVisualParentChanged(DependencyObject? oldParent)
    {
        _cssColumns?.Release();
        if (this is not InlineSourceText)
        {
            ReleaseFirstLineContexts();
            CssFirstLinePainter.Set(this,null);
            _firstLineRevision = -1;
            _inlineBoxesDirty = true;
        }
        if (_cssInlineBox) ClearCssInlineFragments();
        base.OnVisualParentChanged(oldParent);
    }

    private bool TextContentContains(Point point)
    {
        if (CssLineClampLayout.SuppressedInTree(this)) return false;
        if (HasCssInlineBoxes) return InlineSourceContains(point);
        var offset = TextContentOffset;
        var local = new Point(point.X - offset.X, point.Y - offset.Y);
        return HasCssInlineFragments ? _cssClampHitRect?.Contains(local) == true || CssInlineFragmentValues.Any(fragment => fragment.Rect.Contains(local))
            : new Rect(PhysicalTextContentSize).Contains(local);
    }

    private void ClearInactiveCssInlineContent()
    {
        // Ordinary CSS flow also owns a paragraph for the anonymous text body.
        // Preparing the same content must not retire its already arranged fragments.
        if (CanFormatCssInline || HasCssFlowParagraph && _cssInlineParagraph!.IsActive ||
            CssContentsLayout.IsContents(this) && _cssInlineParagraph?.IsActive == true) return;
        if (_cssInlineParagraph is not null)
        { ClearCssInlineFragments(); _cssTextBody?.InvalidateCssAllocation(); }
        foreach (var child in new CssPanelChildren(this, includeInactive: true))
            if (child is CssGeneratedText generated && generated._cssInlineParagraph is not null)
            { generated.ClearCssInlineFragments(); generated.InvalidateCssAllocation(); }
    }

    private Thickness InlineFragmentEdges(CssInlineFragment fragment, Thickness edges)
        => new CssInlineBoxFragment(fragment.Rect, fragment.HasLeftEdge, fragment.HasRightEdge,
            WritingMode: TextWritingMode).Edges(edges);

    private CssUsedBorderRadii InlineFragmentRadii(CssInlineFragment fragment)
    {
        return new CssInlineBoxFragment(fragment.Rect, fragment.HasLeftEdge, fragment.HasRightEdge,
            WritingMode: TextWritingMode).Radii(this);
    }

    private void DrawTextBackground(DrawingContext context, Rect bounds, CssInlineFragment? fragment = null)
    {
        if (Background is null) return;
        var radius = CssBorderRadiusProperties.Get(this);
        var radii = fragment is null ? radius?.Resolve(bounds.Size) ?? default : InlineFragmentRadii(fragment);
        var rounded = radius is null ? null : new CssRoundedRectangleGeometry(bounds, radii);
        void Shape(Brush brush)
        {
            if (rounded is null) context.DrawRectangle(brush, null, bounds);
            else context.DrawGeometry(brush, null, rounded);
        }
        var (border, padding) = CssBoxMetrics.BackgroundInsets(this, CssLayout?.ContainingWidthCache ?? bounds.Width);
        if (fragment is not null)
        { border = InlineFragmentEdges(fragment, border); padding = InlineFragmentEdges(fragment, padding); }
        if (!CssBackgroundPainter.TryDraw(this, BackgroundProperty, Background, context,
                bounds, radii, border, padding, Shape)) Shape(Background);
    }

    private sealed class CssInlineFragment(CssInlineSource source, TextLayoutLine line)
    {
        internal CssInlineFragment Copy() => (CssInlineFragment)MemberwiseClone();
        internal CssInlineSource Source = source;
        internal TextLayoutLine Line = line;
        internal Rect Rect;
        internal Rect LogicalRect;
        internal double TextX, TextY, TabOrigin, Left, Right, MarginLeft, MarginRight;
        internal double OuterLeft, OuterRight, FlowLeft, FlowRight, MetricTop;
        internal double InitialAdvance, InitialShiftX;
        // Hanging characters retain their source and physical inline box. Only
        // the paragraph's fitting/alignment advance excludes this amount.
        internal double HangingWhitespace;
        internal JustifiedLine? Justification;
        internal CssInlineShape? Shape;
        internal FlowDirection? BidiDirection;
        internal double? ParagraphClipLeft, ParagraphClipRight;
        internal List<CssInlineFramePiece>? FramePieces;
        internal bool First, Last;
        internal bool HasLeftEdge => Source.Clone || (Source.RightToLeft ? Last : First);
        internal bool HasRightEdge => Source.Clone || (Source.RightToLeft ? First : Last);
        internal double BoxWidth => Line.Width + Line.StartPadding + Line.EndPadding + Left + Right;
        internal double Width => BoxWidth + OuterLeft + OuterRight + InitialAdvance;
    }

    private sealed class CssInlineSource(UIElement element, TextBlock? text, bool anonymous, Thickness insets, Thickness margin)
    {
        internal UIElement Element = element;
        internal TextBlock Text => text!;
        internal bool Anonymous = anonymous;
        internal Thickness Insets = insets, Margin = margin;
        internal readonly List<CssInlineFragment> Fragments = [];
        internal int LastUnit;
        internal TextTransformContextData? TransformContext;
        internal int TransformOffset;
        internal CapsContextData? CapsContext;
        internal int CapsOffset;
        internal bool Atomic;
        internal bool Block;
        internal Rect BlockRect;
        internal bool BlockPlaced;
        internal CssFloatEnvironment? BlockEnvironment;
        internal bool EmptyFrame;
        internal CssInlineFrame? Frame;
        internal bool AtomicWraps = true;
        internal bool ContextualAtomic;
        internal CssInlineFragment? ClampMarker;
        internal int ClampEnd;
        internal Rect ClampMarkerBounds
        {
            get
            {
                if (ClampMarker is not { } marker) return Rect.Empty;
                var left = Math.Max(marker.Rect.Left, marker.ParagraphClipLeft ?? marker.Rect.Left);
                var right = Math.Min(marker.Rect.Right, marker.ParagraphClipRight ?? marker.Rect.Right);
                return new(left, marker.Rect.Top, Math.Max(0, right - left), marker.Rect.Height);
            }
        }
        internal Size AtomicSize;
        internal double AtomicBaseline;
        internal InitialLetterPlacement? Initial;
        internal bool Clone => !Anonymous && CssBoxDecorationBreakProperties.Value(Element) == CssBoxDecorationBreak.Clone;
        internal bool RightToLeft => (Frame?.Element ?? (Element as FrameworkElement)?.FrameworkParent) is { } parent &&
            CssWritingAxes.UsedDirection(parent) == FlowDirection.RightToLeft;
        internal bool Wraps => EmptyFrame || (Atomic ? AtomicWraps : Text.EffectiveTextWrapping() != TextWrapping.NoWrap);
    }

    private readonly record struct CssInlineUnit(CssInlineSource Source, int Start, int End, bool Space, bool Collapse, bool HardBreak, int PaintOffset = 0, int PaintLength = -1);

    /// <summary>One paragraph shares line boxes across the original native text sources.</summary>
    internal sealed partial class CssTextFlowLayout(FrameworkElement owner)
    {
        internal FrameworkElement Owner => owner;
        private bool _run;
        internal bool IsRun => _run;
        internal bool IsActive => _run ? CssDisplayLayout.IsFlow(owner) : owner is TextBlock { CanFormatCssInline: true };
        internal static TextBlock? TextSource(UIElement element) => element as TextBlock ?? (element as ICssAnonymousTextBox)?.Owner as TextBlock;
        private static bool SharesWritingMode(UIElement element) => element is not FrameworkElement child ||
            CssContentsLayout.BoxParent(element) is not { } parent ||
            CssWritingModeProperties.GetWritingMode(child) == CssWritingModeProperties.GetWritingMode(parent);
        internal static bool IsFragmentable(UIElement element) => SharesWritingMode(element) &&
            (element is FrameworkElement { CssDisplayMode: CssDisplayMode.Ruby } || CssRubyRoles.IsPart(CssRubyRoles.Role(element)) || IsFragmentableText(element) || IsInlineFrame(element) ||
            element.GetValue(CssInitialLetterProperties.ValueProperty) is CssInitialLetter { Active: true });
        private static bool IsFragmentableText(UIElement element) => SharesWritingMode(element) && element is not TextBlock { HasCssInlineBoxes: true } && !CssReplacedContent.IsActive(element) && element is not CssGeneratedText { HasImageContent: true } && (element is ICssAnonymousTextBox { Owner: TextBlock } || element is TextBlock text && text.CssInlineOuter &&
            text.CssDisplayMode is CssDisplayMode.Native or CssDisplayMode.Block &&
            text.LayoutTransform is null && (text is CssGeneratedText || text.IsCssContentHost && !CssGeneratedContent.HasBoxes(text)));
        internal double FirstBaseline { get; private set; } = double.NaN;
        internal double LastBaseline { get; private set; } = double.NaN;
        private readonly List<(UIElement Element, Rect Rect)> _allocated = [];
        private Size _containing;
        private Thickness _insets;
        private double _y, _maximumWidth;
        private bool _intrinsic;
        private bool WrapsAroundAtomic => owner.HasLocalOrAnimatedValue(TextWrappingProperty)
            ? (TextWrapping)owner.GetValue(TextWrappingProperty)! != TextWrapping.NoWrap
            : (CssTextWrapMode)owner.GetValue(CssFlowProperties.TextWrapModeProperty)! != CssTextWrapMode.NoWrap;
        private readonly List<CssInlineSource> _sources = [];
        private readonly List<CssSharedLine> _lines = [];
        private Action<UIElement, Rect>? _allocationChanged;
        internal IReadOnlyList<CssSharedLine> Lines => _lines;
        internal double LastLineTop => _lines.Count > 0 ? _insets.Top + _lines[^1].Top : double.NaN;

        internal sealed class CssSharedLine(double free, CssFlowTextAlignment alignment, Action<double> shift)
        {
            internal double Free => free;
            internal CssFlowTextAlignment Alignment => alignment;
            internal void Shift(double offset) => shift(offset);
            internal double Top { get; init; }
            internal double Height { get; init; }
            internal (UIElement Element, int Start, int End)[] Sources { get; init; } = [];
        }

        internal (double Bottom, double FirstBaseline, double Baseline, double Width) FormatRun(
            IReadOnlyList<UIElement> elements, Size containing, double y, bool firstFormattedLine,
            bool useTextIndent, double? indentBasis, Action<UIElement, Rect> allocationChanged,
            CssFloatSpace? floatSpace = null, Func<UIElement, double, double, double, Rect>? placeFloat = null)
        {
            _run = true; _containing = _splitOptions?.ContainingBlock ?? containing; _insets = default; _y = y; _maximumWidth = 0;
            SplitFlow = null;
            ClearRuby(preserveVisibility: true);
            _sources.Clear(); _allocated.Clear(); _lines.Clear(); _frames.Clear();
            _sharedShapes.Clear(); _sharedShapeCount = 0;
            _allocationChanged = allocationChanged;
            BeginFloats(floatSpace, placeFloat);
            BeginInitialLetter(firstFormattedLine);
            BeginClamp();
            FirstBaseline = LastBaseline = double.NaN;
            _intrinsic = containing.Width == 0 || owner.CssIntrinsicMeasure;
            BeginFirstLine();
            var pending = new List<CssInlineUnit>(); var collapsedSpace = false;
            AppendElements(elements, pending, ref collapsedSpace);
            FinishRubyVisibility();
            RefreshFirstLineEnvironment();
            Paragraph(pending, firstFormattedLine, useTextIndent, indentBasis);
            SuppressUnplacedClampSources();
            InstallFrames();
            foreach (var source in _sources) Install(source);
            CommitFirstLine(firstFormattedLine);
            return (InitialFlowBottom, FirstBaseline, LastBaseline, _maximumWidth);
        }

        internal void ActivateAndArrange(Thickness insets, Action<FrameworkElement>? prepareBlock = null, Size? physicalSize = null)
        {
            _allocated.Clear();
            InstallFrames();
            foreach (var source in _sources)
            {
                if (source.EmptyFrame) continue;
                if (source.Atomic)
                {
                    source.Element.InvalidateCssAllocation();
                    MeasureInlineBox(source.Element, source.Block
                        ? new Size(source.BlockRect.Width, double.PositiveInfinity) : source.AtomicSize, floats: source.BlockEnvironment);
                }
                else
                {
                    var text = source.Text;
                    text.ClearCssInlineFragments(); text._cssInlineParagraph = this; text._cssInlineWhitespace = true;
                    text._inlineFontMeasurements.Clear();
                    text.RebuildLayout(double.PositiveInfinity, _intrinsic);
                    text.SetSharedTransformContext(source.TransformContext, source.TransformOffset);
                    text.SetSharedCapsContext(source.CapsContext, source.CapsOffset);
                }
                Install(source);
            }
            foreach (var item in _nestedFloatRects) _allocated.Add((item.Key, item.Value));
            InstallInlineSelectionGroups();
            ArrangeAllocated(insets, prepareBlock, physicalSize);
        }

        internal Size Measure(Size available)
        {
            var desired = Build(available);
            owner.MeasureCssLayoutAbsoluteChildren(available);
            return desired;
        }

        internal Size Arrange(Size size)
        {
            Build(size);
            ArrangeAllocated(new(_insets.Left, _insets.Top, _insets.Right, _insets.Bottom), physicalSize: size);
            owner.ArrangeCssLayoutAbsoluteChildren(size);
            return size;
        }

        private Size Build(Size available)
        {
            if (owner.CssDisplayMode == CssDisplayMode.Ruby) _run = true;
            ClearRuby(preserveVisibility: true);
            var physicalWidth = available.Width;
            available = Axes.Logical(available);
            BeginFloats(null, null);
            BeginInitialLetter(true);
            BeginClamp();
            if (owner is TextBlock rootText) rootText.ClearCssInlineFragments();
            foreach (var child in new CssPanelChildren(owner, includeInactive: true))
                if (child is CssGeneratedText generated && generated._cssInlineParagraph == this)
                {
                    generated.ClearCssInlineFragments();
                    generated.InvalidateCssAllocation();
                }
            foreach (var frame in _frames) frame.Element.ReleaseCssInlineFrame(owner);
            _allocated.Clear(); _sources.Clear(); _lines.Clear(); _frames.Clear(); _y = _maximumWidth = 0;
            _sharedShapes.Clear(); _sharedShapeCount = 0;
            FirstBaseline = LastBaseline = double.NaN;
            _intrinsic = available.Width == 0 || owner.CssIntrinsicMeasure;
            BeginFirstLine();
            _insets = Axes.Logical(CssBoxMetrics.ContentInsets(owner, CssWritingAxes.PercentageBasis(owner, physicalWidth)));
            _containing = CssBoxMetrics.InnerSize(available, _insets);
            var pending = new List<CssInlineUnit>();
            var collapsedSpace = false;
            if (owner.CssDisplayMode == CssDisplayMode.Ruby)
                AppendRuby(owner, new CssPanelChildren(owner).Enumerate().ToArray(), pending, ref collapsedSpace, null, root: true);
            var children = owner.CssDisplayMode == CssDisplayMode.Ruby ? [] : new CssPanelChildren(owner).Enumerate().ToArray();
            for (int childIndex = 0; childIndex < children.Length; childIndex++)
            {
                var element = children[childIndex];
                if (CssLineClampLayout.Excluded(element)) continue;
                if (element is FrameworkElement { CssLayout.IsOutOfFlow: true }) continue;
                if (CssRubyRoles.IsPart(CssRubyRoles.Role(element)))
                {
                    int first = childIndex;
                    while (childIndex + 1 < children.Length && CssRubyRoles.IsPart(CssRubyRoles.Role(children[childIndex + 1]))) childIndex++;
                    AppendRuby(owner, children[first..(childIndex + 1)], pending, ref collapsedSpace, null, root: true);
                    continue;
                }
                var anonymous = element is ICssAnonymousTextBox;
                var text = anonymous ? (element as ICssAnonymousTextBox)?.Owner as TextBlock : element as TextBlock;
                var fragmentable = anonymous || IsFragmentable(element);
                if (fragmentable)
                {
                    AppendElement(element, pending, ref collapsedSpace);
                }
                else
                {
                    var box = new CssLayoutBox(element, 0);
                    var framework = element as FrameworkElement;
                    var margin = framework is null ? default : Axes.Logical(CssBoxMetrics.Margin(framework, _containing.Width));
                    var width = Math.Max(0, _containing.Width - margin.Left - margin.Right);
                    MeasureInlineBox(element, new Size(width, double.PositiveInfinity));
                    if (framework is { CssInlineOuter: true } && text is not null)
                    {
                        var atomic = new CssInlineSource(element, text, false, default, margin)
                        {
                            Atomic = true, AtomicWraps = WrapsAroundAtomic, AtomicSize = Axes.Logical(element.DesiredSize),
                            AtomicBaseline = text.GetNativeBaselineOffset(element.DesiredSize.Height, last: true),
                            LastUnit = pending.Count,
                        };
                        if (!double.IsFinite(atomic.AtomicBaseline)) atomic.AtomicBaseline = atomic.AtomicSize.Height;
                        _sources.Add(atomic); pending.Add(new(atomic, 0, 0, false, false, false));
                        collapsedSpace = false; continue;
                    }
                    Paragraph(pending); pending.Clear(); collapsedSpace = false;
                    var rect = new Rect(margin.Left, _y + margin.Top,
                        double.IsFinite(width) ? width : Axes.Logical(element.DesiredSize).Width, Axes.Logical(element.DesiredSize).Height);
                    _allocated.Add((element, rect));
                    _clampContents.Add(new(Box: element));
                    if (text is not null)
                    {
                        var first = text.GetFirstBaselineOffset(rect.Height);
                        var last = text.GetLastBaselineOffset(rect.Height);
                        if (!double.IsFinite(FirstBaseline) && double.IsFinite(first)) FirstBaseline = rect.Y + first + _insets.Top;
                        if (double.IsFinite(last)) LastBaseline = rect.Y + last + _insets.Top;
                    }
                    _maximumWidth = Math.Max(_maximumWidth, rect.Right + margin.Right);
                    _y = rect.Bottom + margin.Bottom;
                }
            }
            FinishRubyVisibility();
            RefreshFirstLineEnvironment();
            Paragraph(pending);
            InstallFrames();
            foreach (var source in _sources) Install(source);
            CommitFirstLine(true);
            return Axes.Physical(new Size(Math.Max(0, _maximumWidth + _insets.Left + _insets.Right),
                Math.Max(0, InitialFlowBottom + _insets.Top + _insets.Bottom)));
        }

        private void AppendText(UIElement element, TextBlock text, bool anonymous,
            List<CssInlineUnit> pending, ref bool collapsedSpace)
        {
            text.ClearCssInlineFragments();
            element.InvalidateCssAllocation();
            // Resolve native percentage padding in the paragraph's containing block.
            if (!anonymous) MeasureInlineBox(element, new Size(_containing.Width, double.PositiveInfinity));
            var insets = anonymous ? default : Axes.LineLogical(CssBoxMetrics.ContentInsets(text, _containing.Width));
            var margin = anonymous ? default : Axes.LineLogical(CssBoxMetrics.Margin(text, _containing.Width));
            var source = new CssInlineSource(element, text, anonymous, insets, margin);
            _sources.Add(source);
            text._cssInlineParagraph = this;
            text._cssInlineWhitespace = true;
            // Source-index measurements from the standalone paragraph may have
            // omitted its trailing collapsed whitespace. It is now internal to
            // this shared line and must be measured with the rebuilt paint text.
            text._inlineFontMeasurements.Clear();
            text.RebuildLayout(double.PositiveInfinity, _intrinsic);
            EmitSourceUnits(source, pending, ref collapsedSpace);
            source.LastUnit = pending.Count - 1;
        }

        private static void EmitSourceUnits(CssInlineSource source, List<CssInlineUnit> pending, ref bool collapsedSpace)
        {
            var text = source.Text;
            var boundaries = text.TextLayoutBoundaries();
            var first = pending.Count;
            for (var index = 0; index + 1 < boundaries.Length; index++)
            {
                var start = boundaries[index]; var end = boundaries[index + 1];
                var combined = text.CombinedUnitAt(start) is { } composition && composition.End == end;
                var hard = !combined && IsTextLineBreak(text._visualText[start]);
                var painted = text.PaintSlice(start, end - start);
                if (painted.Length == 0 && !combined && !hard && text._visualText[start] != '\u00ad') continue;
                var space = !combined && painted.Length > 0 && painted.All(static c => c is ' ' or '\t');
                var collapse = space && text._whiteSpaceMode is CssWhiteSpaceCollapse.Collapse or CssWhiteSpaceCollapse.PreserveBreaks;
                if (collapse && collapsedSpace) continue;
                if (!combined && !hard && !space && text._visualText[start] != '\u00ad')
                {
                    var paintBoundaries = GraphemeClusters.GetBoundaries(painted);
                    if (paintBoundaries.Length > 2)
                    {
                        for (var p = 0; p + 1 < paintBoundaries.Length; p++)
                            pending.Add(new(source, start, end, false, false, false, paintBoundaries[p], paintBoundaries[p + 1] - paintBoundaries[p]));
                        collapsedSpace = false;
                        continue;
                    }
                }
                pending.Add(new(source, start, end, space, collapse, hard));
                collapsedSpace = collapse;
                if (hard) collapsedSpace = false;
            }
            // Empty generated inline boxes can still own padding and borders.
            if (pending.Count == first && !source.Anonymous && !text.IsDocumentParagraph)
                pending.Add(new(source, 0, 0, false, false, false));
        }

        private void Paragraph(List<CssInlineUnit> units, bool firstFormattedLine = true, bool useTextIndent = true, double? indentBasis = null)
        {
            PrepareTextTransformContext(ref units);
            var originalCount = units.Count;
            PrepareCapsContext(units);
            PrepareHyphenation(units);
            units = ClampUnits(units);
            _inlineBidi = CreateInlineBidi(units);
            if (_splitOptions is not null || units.Any(static unit => unit.Source.Block)) SplitParagraph(units, firstFormattedLine, useTextIndent, indentBasis);
            else ParagraphRange(units, 0, units.Count, ref firstFormattedLine, useTextIndent, indentBasis);
            SuppressUnplacedClampSources();
            DrainFloats();
            _clampUnitsBefore += originalCount;
        }

        private void ParagraphRange(List<CssInlineUnit> units, int start, int end, ref bool firstFormattedLine,
            bool useTextIndent, double? indentBasis)
        {
            if (start == end) return;
            ClearPreviousInitial();
            _paragraphEnd = end;
            var segment = units.GetRange(start, end - start);
            var joined = string.Concat(segment.Select(static unit => unit.Source.Atomic ? "\ufffc" :
                unit.PaintLength > 0 ? unit.Source.Text.PaintSlice(unit.Start, unit.End - unit.Start).Substring(unit.PaintOffset, unit.PaintLength) :
                unit.Source.Text._visualText.Substring(unit.Start, unit.End - unit.Start)));
            var boundaries = GraphemeClusters.GetBoundaries(joined);
            var splitAfter = new bool[units.Count]; var offset = 0;
            for (var i = start; i < end; i++)
            {
                offset += units[i].Source.Atomic ? 1 : units[i].PaintLength > 0 ? units[i].PaintLength : units[i].End - units[i].Start;
                splitAfter[i] = Array.BinarySearch(boundaries, offset) >= 0;
            }
            var endedWithBreak = false; CssInlineUnit? terminalBreak = null;
            var nativeNoWrap = owner.HasLocalOrAnimatedValue(TextWrappingProperty) && (TextWrapping)owner.GetValue(TextWrappingProperty)! == TextWrapping.NoWrap;
            nativeNoWrap |= segment.All(static unit => !unit.Source.Wraps);
            while (start < end)
            {
                _firstLineMetrics = firstFormattedLine;
                // Forced breaks delimit independent balancing groups. They do
                // not restart first-line indent or split the original sources.
                var groupEnd = start;
                while (groupEnd < end && !units[groupEnd].HardBreak) groupEnd++;
                if (groupEnd < end) groupEnd++;
                _wrapWidthReduction = ChooseWrapWidthReduction(units, splitAfter, start, groupEnd,
                    firstFormattedLine, endedWithBreak, useTextIndent, indentBasis, nativeNoWrap);
                var hyphenatedLineLimit = ((CssHyphenateLimitLines)owner.GetValue(
                    CssFlowProperties.HyphenateLimitLinesProperty)!).Maximum;
                var avoidHyphenOnLastFullLine = (CssHyphenateLimitLast)owner.GetValue(
                    CssFlowProperties.HyphenateLimitLastProperty)! == CssHyphenateLimitLast.Always;
                var consecutiveHyphenatedLines = 0;
                try
                {
                    while (start < groupEnd)
                    {
                        _firstLineMetrics = firstFormattedLine;
                        if (ClampExhausted) { start = end; endedWithBreak = false; break; }
                        while (start < groupEnd && units[start].Collapse) start++;
                        if (start == groupEnd) break;
                        var indent = ParagraphIndent(firstFormattedLine, endedWithBreak, useTextIndent, indentBasis);
                        _allowHyphenBreaks = hyphenatedLineLimit is null ||
                            consecutiveHyphenatedLines < hyphenatedLineLimit.Value;
                        var plan = PlanLine(units, splitAfter, start, indent, nativeNoWrap, groupEnd);
                        if (avoidHyphenOnLastFullLine && _allowHyphenBreaks &&
                            plan.Consumed < groupEnd && !plan.HardBreak &&
                            plan.Fragments.Any(static f => f.Line.SoftHyphenVisible))
                        {
                            var nextStart = Math.Max(start + 1, plan.Consumed);
                            while (nextStart < groupEnd && units[nextStart].Collapse) nextStart++;
                            if (nextStart < groupEnd)
                            {
                                var savedAllow = _allowHyphenBreaks;
                                _allowHyphenBreaks = false;
                                try
                                {
                                    var nextIndent = ParagraphIndent(false, false, useTextIndent, indentBasis);
                                    var nextPlan = PlanLine(units, splitAfter, nextStart, nextIndent, nativeNoWrap, groupEnd);
                                    if (nextPlan.Consumed >= groupEnd)
                                        plan = PlanLine(units, splitAfter, start, indent, nativeNoWrap, groupEnd);
                                }
                                finally
                                {
                                    _allowHyphenBreaks = savedAllow;
                                }
                            }
                        }
                        _allowHyphenBreaks = true;
                        plan = ClampLastLine(units, start, plan, indent);
                        if (plan.Fragments.Any(static f => f.Line.SoftHyphenVisible))
                            consecutiveHyphenatedLines++;
                        else
                            consecutiveHyphenatedLines = 0;
                        _clampLineEnd = plan.End;
                        var empty = FragmentMetrics(plan.Fragments).Empty;
                        if (_inlineBidi is { } bidi && plan.Fragments.All(fragment => !fragment.Source.Atomic && fragment.Line.Length == 0))
                            foreach (var fragment in plan.Fragments) fragment.BidiDirection = bidi.DirectionAt(start);
                        Place(plan.Fragments, indent, 0, plan.End == end || plan.HardBreak ||
                            CssLineClampLayout.Limit(ClampKey, true) is { } clamp && _lines.Count + 1 == clamp.Count);
                        _inlineBidi?.Commit(plan.End, plan.HardBreak, plan.Fragments);
                        if (!empty && !HasContinuingInitial(plan.Fragments)) firstFormattedLine = false;
                        endedWithBreak = plan.HardBreak;
                        terminalBreak = plan.HardBreak ? units[plan.End] : null;
                        start = Math.Max(start + 1, plan.Consumed);
                    }
                }
                finally
                {
                    _allowHyphenBreaks = true;
                    _wrapWidthReduction = 0;
                }
            }
            if (endedWithBreak && terminalBreak is { } last && !ClampExhausted)
            {
                var fragments = new List<CssInlineFragment> { new(last.Source, last.Source.Text.CreateTextLayoutLine(last.End, 0, 0, false)) };
                if (_inlineBidi is { } bidi) fragments[0].BidiDirection = bidi.DirectionAt(end);
                DecorateInlineFrames(fragments, end);
                Place(fragments, ParagraphIndent(firstFormattedLine, true, useTextIndent, indentBasis), 0, true);
            }
        }

        private bool _allowHyphenBreaks = true;

        private bool BreakBetween(CssInlineUnit previous, CssInlineUnit next, bool ignoreNoWrap = false)
        {
            var text = previous.Source.Text;
            if (previous.HardBreak || next.HardBreak) return true;
            if (!ReferenceEquals(previous.Source.Initial, next.Source.Initial) &&
                (previous.Source.Initial is { Inline: true } || next.Source.Initial is { Inline: true })) return true;
            if (previous.Source.Atomic || next.Source.Atomic)
                return previous.Source.Atomic ? previous.Source.Wraps : next.Source.Wraps;
            if (!ignoreNoWrap && text.EffectiveTextWrapping() == TextWrapping.NoWrap &&
                next.Source.Text.EffectiveTextWrapping() == TextWrapping.NoWrap) return false;
            if (previous.Space)
            {
                // pre-wrap breaks after the preserved sequence; break-spaces
                // additionally permits a break between each preserved space.
                return !next.Space || text._whiteSpaceMode != CssWhiteSpaceCollapse.Preserve ||
                    next.Source.Text._whiteSpaceMode != CssWhiteSpaceCollapse.Preserve;
            }
            if (next.Space) return false;
            if (text._layoutLineBreak == CssLineBreak.Anywhere) return true;
            if (previous.Start < text._visualText.Length && text._visualText[previous.Start] == '\u00AD')
                return _allowHyphenBreaks && AllowedInlineSoftHyphen(previous.Source, previous.Start);
            if (previous.End > previous.Start && AllowedInlineSoftHyphen(previous.Source, previous.End - 1))
                return _allowHyphenBreaks;
            if (previous.Source == next.Source)
            {
                if (previous.Start == next.Start && previous.PaintLength > 0 && next.PaintLength > 0)
                {
                    if (text._layoutWordBreak != CssWordBreak.BreakAll) return false;
                    var painted = text.PaintSlice(previous.Start, previous.End - previous.Start);
                    return Rune.TryGetRuneAt(painted, previous.PaintOffset, out var bRune) &&
                        Rune.TryGetRuneAt(painted, next.PaintOffset, out var aRune) &&
                        Rune.IsLetterOrDigit(bRune) && Rune.IsLetterOrDigit(aRune);
                }
                return text.IsStandardLineBreakOpportunity(next.Start, previous.Start, text._visualText.Length);
            }
            if (previous.Start >= text._visualText.Length || next.Start >= next.Source.Text._visualText.Length) return false;
            if (!Rune.TryGetRuneAt(text._visualText, previous.Start, out var before) ||
                !Rune.TryGetRuneAt(next.Source.Text._visualText, next.Start, out var after)) return false;
            if (before.Value is 0x00A0 or 0x202F or 0x2060 or 0xFEFF ||
                after.Value is 0x00A0 or 0x202F or 0x2060 or 0xFEFF) return false;
            if (text._layoutWordBreak == CssWordBreak.BreakAll && Rune.IsLetterOrDigit(before) && Rune.IsLetterOrDigit(after)) return true;
            if (text._layoutWordBreak == CssWordBreak.KeepAll && Rune.IsLetterOrDigit(before) && Rune.IsLetterOrDigit(after)) return false;
            return (IsCjkLineBreakRune(before) || IsCjkLineBreakRune(after) || before.Value is '-' or 0x200B) &&
                !IsProhibitedLineEnd(before) && !IsProhibitedLineStart(after);
        }

        private List<CssInlineFragment> Pieces(List<CssInlineUnit> units, int start, int end, double x, int consumed = -1, double? availableWidth = null, double? tabRulerOrigin = null, bool includeTrailingHyphen = true)
        {
            PrepareRubyLine(units, start, end);
            var bidiOrigin = tabRulerOrigin ?? x;
            var result = new List<CssInlineFragment>();
            var hasLeadBreak = TryGetLeadingHyphenBreak(units, start, out var leadBreak);
            for (var index = start; index < end;)
            {
                var lineStart = index == start;
                var first = units[index]; var source = first.Source;
                var last = first;
                while (++index < end && units[index].Source == source) last = units[index];
                var lineEnd = index == end;
                if (source.Atomic)
                {
                    var atomicFragment = new CssInlineFragment(source, new TextLayoutLine(0, 0, InitialLetterAdvance(source, units, index), false,
                        source.Initial is not { Inline: false } ? source.AtomicSize.Height + source.Margin.Top + source.Margin.Bottom : 0,
                        source.Initial is not { Inline: false } ? source.AtomicBaseline + source.Margin.Top : 0, source.Initial is not { Inline: false } ? source.AtomicSize.Height : 0,
                        startPadding: lineStart ? CssFlowProperties.UsedLinePadding(owner) : 0,
                        endPadding: lineEnd ? CssFlowProperties.UsedLinePadding(owner) : 0))
                        { MarginLeft = source.Margin.Left, MarginRight = source.Margin.Right };
                    result.Add(atomicFragment);
                    x += atomicFragment.Width + source.Margin.Left + source.Margin.Right;
                    continue;
                }
                var firstFragment = source.Fragments.Count == 0;
                var lastFragment = source.LastUnit < (consumed < 0 ? end : consumed);
                var leftEdge = source.RightToLeft ? lastFragment : firstFragment;
                var rightEdge = source.RightToLeft ? firstFragment : lastFragment;
                var left = source.Clone || leftEdge ? source.Insets.Left : 0;
                var right = source.Clone || rightEdge ? source.Insets.Right : 0;
                var marginLeft = source.Clone || leftEdge ? source.Margin.Left : 0;
                var marginRight = source.Clone || rightEdge ? source.Margin.Right : 0;
                var length = last.End - first.Start;
                var tabOrigin = x + marginLeft + left + (lineStart ? source.Text.GetLineStartPadding(first.Start, last.End) : 0);
                InlineHyphenBreak? tailBreak = null;
                var hyphen = includeTrailingHyphen && index == end && end < _paragraphEnd && !units[end].HardBreak &&
                    last.End > first.Start && TryGetInlineHyphen(source, last.End - 1, out tailBreak);
                var leadDelete = 0;
                var leadReplace = string.Empty;
                if (hasLeadBreak && ReferenceEquals(leadBreak.LeadSource, source) &&
                    leadBreak.LeadStartIndex == first.Start &&
                    (leadBreak.LeadDeleteLength > 0 || leadBreak.LeadReplaceText.Length > 0))
                {
                    leadDelete = Math.Clamp(leadBreak.LeadDeleteLength, 0, length);
                    leadReplace = leadBreak.LeadReplaceText;
                }
                var tailDelete = 0;
                var tailReplace = string.Empty;
                if (hyphen && tailBreak is not null)
                {
                    tailDelete = Math.Clamp(tailBreak!.TailDeleteLength, 0, length - leadDelete);
                    tailReplace = tailBreak.TailReplaceText;
                    if (tailBreak.PrevTailSource is not null && tailBreak.PrevTailDeleteLength > 0 &&
                        result.Count > 0 && ReferenceEquals(result[^1].Source, tailBreak.PrevTailSource))
                    {
                        var prevFrag = result[^1];
                        var prevLine = prevFrag.Line;
                        var prevTailDel = Math.Clamp(tailBreak.PrevTailDeleteLength, 0, prevLine.Length);
                        var prevChange = new LineHyphenChange(prevTailDel, string.Empty,
                            prevLine.HyphenChange?.LeadDeleteLength ?? 0,
                            prevLine.HyphenChange?.LeadReplaceText ?? string.Empty);
                        var prevWidth = prevFrag.Source.Text.MeasureLineWithHyphenChange(
                            prevLine.StartIndex, prevLine.Length, prevChange, prevLine.SoftHyphenVisible,
                            prevFrag.TabOrigin, includeTrailingWhitespace: true);
                        x -= prevFrag.Width;
                        prevFrag.Line = prevFrag.Source.Text.CreateTextLayoutLine(
                            prevLine.StartIndex, prevLine.Length, prevWidth, prevLine.HasLineBreakAfter,
                            softHyphenVisible: prevLine.SoftHyphenVisible,
                            availableWidth: availableWidth ?? _containing.Width,
                            lineStart: result.Count == 1, lineEnd: false, hyphenChange: prevChange);
                        x += prevFrag.Width;
                        tabOrigin = x + marginLeft + left + (lineStart ? source.Text.GetLineStartPadding(first.Start, last.End) : 0);
                    }
                }
                var hyphenChange = (leadDelete > 0 || leadReplace.Length > 0 || tailDelete > 0 || tailReplace.Length > 0)
                    ? new LineHyphenChange(tailDelete, tailReplace, leadDelete, leadReplace)
                    : null;
                var width = hyphen || hyphenChange is { HasChanges: true }
                    ? source.Text.MeasureLineWithHyphenChange(first.Start, length, hyphenChange, hyphen, tabOrigin, includeTrailingWhitespace: true)
                    : source.Text.MeasureInlineRange(first.Start, length, includeTrailingWhitespace: true, tabOrigin: tabOrigin);
                var line = source.Text.CreateTextLayoutLine(first.Start, length, width, false,
                    softHyphenVisible: hyphen, availableWidth: availableWidth ?? _containing.Width,
                    lineStart: lineStart, lineEnd: lineEnd, hyphenChange: hyphenChange);
                var fragment = new CssInlineFragment(source, line) { Left = left, Right = right,
                    MarginLeft = marginLeft, MarginRight = marginRight, First = firstFragment, Last = lastFragment, TabOrigin = tabOrigin };
                result.Add(fragment); x += fragment.Width + marginLeft + marginRight;
            }
            DecorateInlineFrames(result, consumed < 0 ? end : consumed);
            if (FramePieces(result).Count > 0)
            {
                var actualX = x - result.Sum(static fragment => fragment.BoxWidth + fragment.MarginLeft + fragment.MarginRight);
                for (var index = 0; index < result.Count; index++)
                {
                    var fragment = result[index]; var source = fragment.Source;
                    if (!source.Atomic)
                    {
                        var line = fragment.Line;
                        fragment.TabOrigin = actualX + fragment.OuterLeft + fragment.MarginLeft + fragment.Left +
                            (index == 0 ? source.Text.GetLineStartPadding(line.StartIndex, line.StartIndex + line.Length) : 0);
                        var width = line.SoftHyphenVisible || line.HyphenChange is { HasChanges: true }
                            ? source.Text.MeasureLineWithHyphenChange(line.StartIndex, line.Length, line.HyphenChange, line.SoftHyphenVisible, fragment.TabOrigin, includeTrailingWhitespace: true)
                            : source.Text.MeasureInlineRange(line.StartIndex, line.Length, includeTrailingWhitespace: true, tabOrigin: fragment.TabOrigin);
                        fragment.Line = source.Text.CreateTextLayoutLine(line.StartIndex, line.Length, width, line.HasLineBreakAfter,
                            softHyphenVisible: line.SoftHyphenVisible, availableWidth: availableWidth ?? _containing.Width,
                            lineStart: index == 0, lineEnd: index == result.Count - 1, hyphenChange: line.HyphenChange);
                    }
                    actualX += fragment.Width + fragment.MarginLeft + fragment.MarginRight;
                }
            }
            if (!TryInlineBidiPieces(result, units, start, end, consumed < 0 ? end : consumed, bidiOrigin)) ShapePieces(result);
            if (result.Any(static fragment => fragment.Shape is not null || fragment.BidiDirection is not null))
                DecorateInlineFrames(result, consumed < 0 ? end : consumed);
            ApplyHangingWhitespace(units, start, end, result, availableWidth ?? _containing.Width);
            return result;
        }

        private void ApplyHangingWhitespace(List<CssInlineUnit> units, int start, int end,
            List<CssInlineFragment> fragments, double available)
        {
            var tail = end;
            while (tail > start)
            {
                var unit = units[tail - 1];
                if (!unit.Source.Atomic && unit.Start == unit.End) { tail--; continue; }
                if (!unit.Space || !unit.Source.Wraps || unit.Source.Text._whiteSpaceMode != CssWhiteSpaceCollapse.Preserve) break;
                tail--;
            }
            if (tail == end) return;
            // A paragraph end is a forced break. It contributes preserved spaces
            // to max-content, but hangs them when they do not fit (including a
            // min-content constraint). A soft line end always hangs the sequence.
            var forced = end >= _paragraphEnd || units[end].HardBreak;
            var physicalWidth = FragmentWidth(fragments);
            if (forced && (!double.IsFinite(available) || physicalWidth <= available + .000001)) return;
            for (var index = tail; index < end;)
            {
                var unit = units[index]; var source = unit.Source;
                var first = unit.Start; var last = unit.End;
                while (++index < end && units[index].Source == source) last = units[index].End;
                if (source.Atomic || source.EmptyFrame || first == last) continue;
                foreach (var fragment in fragments.Where(f => ReferenceEquals(f.Source, source) &&
                    first < f.Line.StartIndex + f.Line.Length && last > f.Line.StartIndex))
                {
                    var line = fragment.Line;
                    if (fragment.Shape is { } shape)
                        fragment.HangingWhitespace = shape.Selection(first, last - first).Sum(static range => range.Width);
                    else
                    {
                        var full = source.Text.MeasureInlineRange(line.StartIndex, line.Length, includeTrailingWhitespace: true, tabOrigin: fragment.TabOrigin);
                        var prefix = source.Text.MeasureInlineRange(line.StartIndex, Math.Max(0, first - line.StartIndex), includeTrailingWhitespace: true, tabOrigin: fragment.TabOrigin);
                        fragment.HangingWhitespace = Math.Max(0, full - prefix);
                    }
                }
            }
            if (forced)
            {
                var remaining = Math.Max(0, physicalWidth - available);
                for (var index = fragments.Count - 1; index >= 0; index--)
                {
                    var fragment = fragments[index];
                    fragment.HangingWhitespace = Math.Min(fragment.HangingWhitespace, remaining);
                    remaining -= fragment.HangingWhitespace;
                }
            }
        }

        private void Place(List<CssInlineFragment> fragments, double indent, double padding, bool last)
        {
            CommitRubyLine(fragments);
            var (lineHeight, ascent, alignments, empty) = FragmentMetrics(fragments);
            foreach (var piece in FramePieces(fragments))
                if (piece.Initial is { Placement.Placed: false } initial)
                    _y += Math.Max(0, piece.Frame.Margin.Top - (_y + initial.Top));
            var band = FloatBandFor(fragments, lineHeight);
            var fittingWidth = FragmentWidth(fragments);
            var bandWidth = Math.Max(0, band.Right - band.Left);
            var free = double.IsFinite(bandWidth) ? bandWidth - indent - 2 * padding - fittingWidth : 0;
            var textAlign = CssFlowProperties.LineAlignment(owner, last);
            if (textAlign == CssFlowTextAlignment.Justify)
            {
                var native = owner.HasLocalOrAnimatedValue(TextAlignmentProperty);
                if (last && native) textAlign = CssFlowTextAlignment.Start;
                else if (!JustifyFragments(fragments, bandWidth - indent - 2 * padding))
                {
                    textAlign = native || free < -.000001 ? CssFlowTextAlignment.Start : CssFlowProperties.LineAlignment(owner, true);
                    if (textAlign == CssFlowTextAlignment.Justify) textAlign = CssFlowTextAlignment.Center;
                }
                (lineHeight, ascent, alignments, empty) = FragmentMetrics(fragments);
                fittingWidth = FragmentWidth(fragments);
                free = double.IsFinite(bandWidth) ? bandWidth - indent - 2 * padding - fittingWidth : 0;
            }
            var rtl = (fragments.FirstOrDefault()?.BidiDirection ?? UsedFlowDirection) == FlowDirection.RightToLeft;
            var offset = textAlign switch
            {
                CssFlowTextAlignment.Right => free,
                CssFlowTextAlignment.Start when rtl => free,
                CssFlowTextAlignment.End when !rtl => free,
                CssFlowTextAlignment.Center => free / 2,
                _ => 0,
            };
            var x = band.Left + (rtl ? 0 : indent) + padding + offset + (rtl ? fittingWidth : 0);
            for (var index = 0; index < fragments.Count; index++)
            {
                var fragment = fragments[index];
                var advance = fragment.Width + fragment.MarginLeft + fragment.MarginRight;
                if (rtl) x -= advance;
                fragment.FlowLeft = x; fragment.FlowRight = x + advance;
                var ownX = x + fragment.OuterLeft + fragment.MarginLeft;
                var top = _y + fragment.MetricTop;
                fragment.Rect = new Rect(ownX, top - fragment.Source.Insets.Top, fragment.BoxWidth,
                    (empty ? 0 : fragment.Line.Height) + fragment.Source.Insets.Top + fragment.Source.Insets.Bottom);
                var leftLinePadding = rtl ? fragment.Line.EndPadding : fragment.Line.StartPadding;
                if (fragment.Source.Atomic) fragment.Rect = new Rect(ownX + leftLinePadding, top + fragment.Source.Margin.Top,
                    fragment.Source.AtomicSize.Width, fragment.Source.AtomicSize.Height);
                if (fragment.Source.Initial is { Inline: false }) PlaceInitialLetter(fragment);
                fragment.Rect = new(fragment.Rect.X + fragment.InitialShiftX, fragment.Rect.Y, fragment.Rect.Width, fragment.Rect.Height);
                fragment.TextX = ownX + fragment.Left + leftLinePadding - fragment.Line.HangingStart + fragment.InitialShiftX;
                fragment.TextY = top;
                if (Axes.ReversedLineCross)
                {
                    fragment.Rect = Axes.LinePlacement(fragment.Rect, _y, lineHeight);
                    fragment.TextY = 2 * _y + lineHeight - top - fragment.Line.Height;
                }
                if (double.IsFinite(band.Right))
                { fragment.ParagraphClipLeft = band.Left; fragment.ParagraphClipRight = band.Right; }
                fragment.Source.Fragments.Add(fragment);
                if (!rtl) x += advance;
            }
            PlaceInlineFrames(fragments, empty, lineHeight);
            PlaceInlineInitial(fragments);
            RecordFirstLinePaint(fragments, ascent, empty);
            RecordClampLine(fragments, lineHeight, empty);
            _lines.Add(new CssSharedLine(free, textAlign, delta =>
            {
                foreach (var fragment in fragments)
                {
                    fragment.Rect = new(fragment.Rect.X + delta, fragment.Rect.Y, fragment.Rect.Width, fragment.Rect.Height);
                    fragment.TextX += delta;
                }
                foreach (var piece in FramePieces(fragments)) piece.Rect = new(piece.Rect.X + delta, piece.Rect.Y, piece.Rect.Width, piece.Rect.Height);
                foreach (var source in _sources) _allocationChanged?.Invoke(source.Element, Allocation(source));
                UpdateFrameAllocations();
                foreach (var frame in _frames) _allocationChanged?.Invoke(frame.Element, frame.Allocation);
            }) { Top = _y, Height = lineHeight, Sources = fragments.Where(fragment => !fragment.Source.Atomic)
                .Select(fragment => (fragment.Source.Element, fragment.Line.StartIndex, fragment.Line.StartIndex + fragment.Line.Length)).ToArray() });
            RecordInitialLetterLine(fragments, _lines[^1]);
            if (lineHeight > 0)
            {
                if (!double.IsFinite(FirstBaseline)) FirstBaseline = _y + ascent + _insets.Top;
                LastBaseline = _y + ascent + _insets.Top;
            }
            var intrinsicRight = double.IsPositiveInfinity(_containing.Width) ? _floatSpace?.IntrinsicRightWidth(_y, lineHeight) ?? 0 : 0;
            _maximumWidth = Math.Max(_maximumWidth, band.Left + fittingWidth + Math.Max(0, indent) + 2 * padding + intrinsicRight);
            _floatSpace?.NoteSourceTop(_y);
            _y += InitialLineAdvance(fragments, lineHeight);
        }

        private (double Height, double Ascent, List<(CssInlineAlignment Kind, double Shift)> Alignments, bool Empty) FragmentMetrics(List<CssInlineFragment> fragments)
        {
            if (FramePieces(fragments).Count > 0) return NestedFragmentMetrics(fragments);
            var metrics = InlineLineMetrics(FirstLineMetricsElement(owner));
            var ascent = metrics.Baseline; var descent = metrics.Height - metrics.Baseline;
            var alignments = new List<(CssInlineAlignment Kind, double Shift)>();
            var alignedHeight = 0d;
            foreach (var fragment in fragments)
            {
                if (fragment.Source.Initial is not null)
                { alignments.Add((CssInlineAlignment.Baseline, 0)); continue; }
                var alignment = (CssInlineVerticalAlign)fragment.Source.Element.GetValue(CssFlowProperties.VerticalAlignProperty)!;
                var shift = CssFlowProperties.InlineShift(owner, fragment.Source.Element, alignment,
                    fragment.Line.Baseline, fragment.Line.Height, metrics.Height, metrics.Ascent, metrics.Descent);
                alignments.Add((alignment.Kind, shift));
                if (alignment.Kind is CssInlineAlignment.Top or CssInlineAlignment.Bottom or CssInlineAlignment.Center)
                    alignedHeight = Math.Max(alignedHeight, fragment.Line.Height);
                else
                {
                    ascent = Math.Max(ascent, fragment.Line.Baseline + shift);
                    descent = Math.Max(descent, fragment.Line.Height - fragment.Line.Baseline - shift);
                }
            }
            var lineHeight = Math.Max(alignedHeight, ascent + descent);
            var empty = fragments.Count > 0 && fragments.All(static fragment => !fragment.Source.Atomic &&
                fragment.Line.Length == 0 && !fragment.Line.HasLineBreakAfter && fragment.Source.Insets == default && fragment.Source.Margin == default);
            if (empty) lineHeight = 0;
            for (var index = 0; index < fragments.Count; index++)
            {
                var fragment = fragments[index]; var alignment = alignments[index];
                fragment.MetricTop = alignment.Kind switch
                {
                    CssInlineAlignment.Top => 0,
                    CssInlineAlignment.Bottom => lineHeight - fragment.Line.Height,
                    CssInlineAlignment.Center => (lineHeight - fragment.Line.Height) / 2,
                    _ => ascent - fragment.Line.Baseline - alignment.Shift,
                };
            }
            return (lineHeight, ascent, alignments, empty);
        }

        private void Install(CssInlineSource source)
        {
            if (source.EmptyFrame) return;
            var text = source.Text;
            var union = Allocation(source);
            if (_run && source.Frame is null && source.Anonymous && source.Element is FrameworkElement { CssInlineOuter: false } &&
                double.IsFinite(_containing.Width) && !owner.CssIntrinsicMeasure)
                union = new Rect(0, union.Y, _containing.Width, union.Height);
            _allocationChanged?.Invoke(source.Element, union);
            if (source.Atomic) { _allocated.Add((source.Element, union)); return; }
            text._layoutLines = source.Fragments.Select(static (f, i) => f.Line.WithInlineFragmentIndex(i)).ToList();
            text._cssInlineFragments = new();
            var fragmentList = new CssInlineFragment[source.Fragments.Count];
            text._cssInlineFragmentList = fragmentList;
            text._cssInlineParagraph = this;
            text._cssInlineBox = !source.Anonymous;
            text._cssInlineLayoutElement = source.Element;
            text._cssInlineExpectedParent = source.Element.VisualParent;
            var markerBounds = source.ClampMarkerBounds;
            text._cssClampHitRect = markerBounds.IsEmpty ? null : Axes.Local(markerBounds, union);
            text._cssClampHitEnd = source.ClampEnd;
            if (text.IsDocumentParagraph && source.ClampMarker is { } documentMarker &&
                _clampMarkerSources.TryGetValue(documentMarker.Source, out var marker))
                text._documentSharedMarker = (marker, text._cssClampHitRect!.Value,
                    new(documentMarker.TextX - union.X, documentMarker.TextY - union.Y), documentMarker.Line.Baseline,
                    documentMarker.ParagraphClipLeft - union.X, documentMarker.ParagraphClipRight - union.X);
            if (!source.Anonymous) source.Element.CssInlineLayoutOwner = owner;
            for (var index = 0; index < source.Fragments.Count; index++)
            {
                var fragment = source.Fragments[index];
                var local = new CssInlineFragment(source, fragment.Line.WithInlineFragmentIndex(index))
                {
                    TextX = fragment.TextX - union.X, TextY = fragment.TextY - union.Y,
                    Rect = Axes.Local(fragment.Rect, union),
                    LogicalRect = new(fragment.Rect.X - union.X, fragment.Rect.Y - union.Y, fragment.Rect.Width, fragment.Rect.Height),
                    TabOrigin = fragment.TabOrigin, Left = fragment.Left, Right = fragment.Right,
                    MarginLeft = fragment.MarginLeft, MarginRight = fragment.MarginRight,
                    First = fragment.First, Last = fragment.Last,
                    Justification = fragment.Justification,
                    Shape = fragment.Shape,
                    ParagraphClipLeft = fragment.ParagraphClipLeft - union.X,
                    ParagraphClipRight = fragment.ParagraphClipRight - union.X,
                };
                // Raster coverage can extend beyond a glyph's fractional advance
                // at an internal source seam. Keep that antialiasing fringe in
                // dirty/culling bounds as well as in the shared paragraph clip.
                if (local.ParagraphClipLeft < 0 || local.ParagraphClipRight > union.Width)
                    text._cssLineInkPadding = Math.Max(text._cssLineInkPadding, 1);
                if (fragment.Shape is { } shape)
                {
                    double overhang = shape.InkOverhang;
                    if (overhang > 0) text._cssLineInkPadding = Math.Max(text._cssLineInkPadding, overhang + 1);
                }
                text._cssInlineFragments[fragment.Line.StartIndex] = local;
                fragmentList[index] = local;
            }
            text._cssDecorationFragments = CssInlineBoxFragment.Join(text.CssInlineFragmentValues
                .Select(fragment => new CssInlineBoxFragment(fragment.Rect, fragment.HasLeftEdge, fragment.HasRightEdge, WritingMode: WritingMode)).ToArray(),
                source.Clone, source.RightToLeft);
            text._cssDecorationLookup.Clear();
            foreach (var fragment in text._cssDecorationFragments) text._cssDecorationLookup[fragment.Rect] = fragment;
            if (!source.Anonymous && text._cssTextBody is not null)
            {
                text._cssTextBody.Visibility = Visibility.Collapsed;
                text._cssTextBody.Arrange(new Rect(0, 0, 0, 0));
            }
            text._cssInlineAllocation = Axes.Physical(union.Size);
            if (source.Element is FrameworkElement { CssLayout: { } layout }) layout.ContainingWidthCache = _containing.Width;
            text._layoutDirty = false;
            text._formattedLinesCacheDirty = true;
            source.Element.InvalidateCssAllocation();
            source.Element.Measure(Axes.Physical(union.Size));
            // Individual fragments can move without changing their envelope.
            // Retained paint commands must follow the new local coordinates.
            source.Element.InvalidateVisual();
            _allocated.Add((source.Element, union));
        }

        private static Rect Allocation(CssInlineSource source)
        {
            if (source.Block) return source.BlockRect;
            var union = Rect.Empty;
            foreach (var fragment in source.Fragments) union.Union(fragment.Rect);
            if (source.ClampMarker is not null) union.Union(source.ClampMarkerBounds);
            return union.IsEmpty ? new Rect(0, 0, 0, 0) : union;
        }
    }
}
