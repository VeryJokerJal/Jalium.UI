using Jalium.UI.Automation.Peers;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private CssTextBody? _cssTextBody;
    private bool _cssTextLayoutActive;
    private bool _cssRubyAnonymousContent;
    internal bool UsesAnonymousRubyContent => _cssRubyAnonymousContent && VisualParent is FrameworkElement parent &&
        CssTextFlowLayout.IsAllocatedRubyContainer(parent);
    internal bool UsesNativeRubyContent => this is not CssGeneratedText && (CssRubyRoles.IsPart(CssDisplayMode) ||
        UsesAnonymousRubyContent || CssRubyBoxMetrics.IsFontArea(this)) &&
        !HasCssInlineBoxes && !CssGeneratedContent.HasBoxes(this);
    private bool UsesCssTextBody => !UsesNativeRubyContent && !IsCssInlineBox && _cssTextBody is not null &&
        (CssDisplayLayout.HasBoxFormatter(this) || CssContentsLayout.IsContents(this));
    private Size PhysicalTextContentSize => UsesCssTextBody ? _cssTextBody!.RenderSize : RenderSize;
    private Size TextContentSize => LogicalTextSize(PhysicalTextContentSize);
    private Point TextContentOffset => UsesCssTextBody ? _cssTextBody!.VisualBounds.Location : default;

    protected override bool SupportsCssContentLayout => this is not CssGeneratedText;
    protected override bool SupportsCssInlineContentLayout => GetType() == typeof(TextBlock);

    // A plain rb/rt already has an independent shaped IFC. Retain it on the
    // source TextBlock so ruby distribution, painting and hit testing use one
    // native paragraph, without an additional anonymous shared paragraph.
    internal Size MeasureNativeRubyContent(Size available)
    {
        if (_cssTextLayoutActive) { _cssTextLayoutActive = false; InvalidateCaches(); }
        if (_cssTextBody is not null) _cssTextBody.Visibility = Visibility.Collapsed;
        // The control's empty fast path does not rebuild its previous lines.
        // Drop those ranges before a ruby group reads their used metrics.
        if (_displayText.Length == 0) EnsureLayout(GetLayoutConstraintWidth(LogicalTextSize(available).Width));
        var measured = MeasureTextContent(available, cssBody: true);
        // The native control's fallback strut can exceed an inherited CSS
        // line-height. Ruby tracks use the resolved typographic line boxes.
        if (_displayText.Length > 0 && UsesCssLineMetrics && _layoutLines.Count > 0)
        {
            var logical = LogicalTextSize(measured); var insets = GetTextInsets();
            return PhysicalTextSize(new(logical.Width, _layoutLines.Sum(line => line.Height) + insets.Top + insets.Bottom));
        }
        return measured;
    }

    protected override void PrepareCssContentLayout()
    {
        if (_cssTextBody is null)
        {
            _cssTextBody = new CssTextBody(this);
            AddVisualChild(_cssTextBody);
            InvalidateCaches();
            // Properties such as line-height and inline font settings are owned
            // by the text source, while the anonymous box owns its measure cache.
            PropertyChangedInternal += (_, _, _) => { if (!IsCssInlineBox) _cssTextBody.InvalidateMeasure(); };
        }
        if (!_cssTextLayoutActive)
        {
            _cssTextLayoutActive = true;
            InvalidateCaches();
        }
        EnsureInlineSourceBoxes();
        // Keep the native anonymous block's paint/allocation contract when
        // surrounded by blocks. Its text still forms a shared inline paragraph.
        var surrounding = new CssPanelChildren(this).Enumerate().OfType<CssGeneratedText>()
            .Where(static text => text.CssLayout?.IsOutOfFlow != true && CssFloatProperties.ActiveSide(text) == CssFloatSide.None).ToArray();
        _cssTextBody.CssInlineOuter = CssDisplayLayout.IsFlow(this) && surrounding.Length > 0 && surrounding.All(static text => text.CssInlineOuter);
        var omit = !HasCssInlineBoxes && (_displayText.Length == 0 ||
            CssDisplayMode is CssDisplayMode.Flex or CssDisplayMode.Grid &&
            _displayText.All(static c => c is ' ' or '\t' or '\r' or '\n' or '\f'));
        var visibility = omit ? Visibility.Collapsed : Visibility.Visible;
        // This private body has no author-facing visibility source. Reasserting
        // the same value would invalidate table visibility during final arrange.
        if (_cssTextBody.Visibility != visibility) _cssTextBody.Visibility = visibility;
        if (!HasCssInlineBoxes && !IsCssInlineFrame) ClearInactiveCssInlineContent();
    }

    protected override int VisualChildrenCount => _cssTextBody is null ? 0 : 1;
    protected override Visual? GetVisualChild(int index) => index == 0 && _cssTextBody is not null
        ? _cssTextBody : throw new ArgumentOutOfRangeException(nameof(index));

    private sealed class CssTextBody(TextBlock owner) : FrameworkElement, ICssAnonymousTextBox
    {
        internal readonly List<UIElement> InlineChildren=[];
        internal void AppendInline(UIElement child){InlineChildren.Add(child);AddVisualChild(child);}
        internal void RemoveInline(UIElement child){InlineChildren.Remove(child);RemoveVisualChild(child);}
        protected override bool SupportsCssContentLayout=>owner.HasCssInlineBoxes;
        protected override bool SupportsCssInlineContentLayout=>owner.HasCssInlineBoxes;
        protected override int VisualChildrenCount=>InlineChildren.Count;
        protected override Visual? GetVisualChild(int index)=>InlineChildren[index];
        public FrameworkElement Owner => owner;
        public double BaselineOffset(double height, bool last) => owner.GetNativeBaselineOffset(height, last);
        public bool Contains(Point point) => !owner.HasCssInlineFragments ||
            owner.CssInlineFragmentValues.Any(fragment => fragment.Rect.Contains(point));

        protected override Size MeasureOverride(Size availableSize) => !CssIntrinsicMeasure && owner.TryCssInlineAllocation(out var size)
            ? size : owner.MeasureTextContent(availableSize, cssBody: true);
        // Selection, brushes and inline contents live on the owner. Reuse its
        // shaped-line caches, but never retain a second copy of its paint commands.
        protected override bool ParticipatesInRenderCache => false;
        internal override double CssTextInkPadding => Math.Max(base.CssTextInkPadding, System.Threading.Volatile.Read(ref owner._cssLineInkPadding));
        internal override double GetExtraDirtyPadding() => Math.Max(base.GetExtraDirtyPadding(), owner.CssBorderImageInkPadding());
        protected override void OnRender(DrawingContext context)
        {
            if (owner.HasCssInlineBoxes || !owner.UsesCssTextBody || Visibility != Visibility.Visible || owner._isRenderingText) return;
            owner._isRenderingText = true;
            try { owner.RenderTextContent(context); }
            finally { owner._isRenderingText = false; }
        }
        protected override AutomationPeer? OnCreateAutomationPeer() => null;
    }
}
