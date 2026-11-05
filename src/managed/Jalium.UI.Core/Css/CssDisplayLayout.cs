using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssDisplayMode { Native, Flex, Grid, Block, FlowRoot, Table, TableRow, TableRowGroup, TableHeaderGroup, TableFooterGroup, TableCell, TableColumn, TableColumnGroup, TableCaption, Contents, Ruby, RubyBase, RubyText, RubyBaseContainer, RubyTextContainer }

/// <summary>Shares formatting algorithms across containers without reparenting their content.</summary>
internal static class CssDisplayLayout
{
    private static readonly ConditionalWeakTable<FrameworkElement, FlexPanel> s_flexLayouts = new();
    private static readonly ConditionalWeakTable<FrameworkElement, CssGridLayout> s_gridLayouts = new();
    private static readonly ConditionalWeakTable<FrameworkElement, CssFlowLayout> s_flowLayouts = new();
    private static readonly ConditionalWeakTable<FrameworkElement, CssTableLayout> s_tableLayouts = new();
    private static readonly ConditionalWeakTable<FrameworkElement, TextBlock.CssTextFlowLayout> s_rubyLayouts = new();
    internal static TextBlock.CssTextFlowLayout RubyFor(FrameworkElement owner) => s_rubyLayouts.GetValue(owner, static element => new(element));
    internal static CssTableLayout TableFor(FrameworkElement owner) => s_tableLayouts.GetValue(owner, static element => new(element));
    internal static bool TryTableFor(FrameworkElement owner, out CssTableLayout? layout) => s_tableLayouts.TryGetValue(owner, out layout);
    internal static CssFlowLayout FlowFor(FrameworkElement owner) => s_flowLayouts.GetValue(owner, static panel => new CssFlowLayout(panel));
    internal static bool TryGetBaseline(UIElement element, double borderHeight, bool last, out double baseline)
    {
        if (element is TextBlock { UsesNativeRubyContent: true } rubyText)
        { baseline = last ? rubyText.GetLastBaselineOffset(borderHeight) : rubyText.GetFirstBaselineOffset(borderHeight); return double.IsFinite(baseline); }
        if (element is FrameworkElement { CssDisplayMode: CssDisplayMode.Ruby } ruby)
        { baseline = last ? RubyFor(ruby).LastBaseline : RubyFor(ruby).FirstBaseline; return double.IsFinite(baseline); }
        if (element is FrameworkElement clampOwner && CssLineClampProperties.Active(clampOwner) && CssLineClampLayout.For(clampOwner).IsApplied)
        {
            var clamp = CssLineClampLayout.For(clampOwner);
            baseline = last ? clamp.LastBaseline : clamp.FirstBaseline;
            return double.IsFinite(baseline);
        }
        if (element is FrameworkElement table && table.CssDisplayMode == CssDisplayMode.Table)
        { baseline = TableFor(table).Baseline; return double.IsFinite(baseline); }
        if (CssReplacedContent.IsActive(element) || element is CssGeneratedText { IsImageReplacement: true })
        { baseline = borderHeight; return true; }
        if (element is FrameworkElement columns && CssMultiColumnProperties.Active(columns) && CssMultiColumnLayout.Try(columns, out var multicolumn))
        { baseline = last ? multicolumn!.LastBaseline : multicolumn!.FirstBaseline; return double.IsFinite(baseline); }
        if (element is FrameworkElement { IsCssInlineFrame: true, CssInlineBox: { } fragments })
        {
            baseline = last ? fragments.LastBaseline : fragments.FirstBaseline;
            return double.IsFinite(baseline);
        }
        if (element is TextBlock { CanFormatCssInline: true, HasCssFlowParagraph: false } textFlow)
        {
            baseline = last ? textFlow.CssTextFlow.LastBaseline : textFlow.CssTextFlow.FirstBaseline;
            return double.IsFinite(baseline);
        }
        if (element is ICssAnonymousBox anonymous)
        {
            baseline = anonymous.BaselineOffset(borderHeight, last);
            return double.IsFinite(baseline);
        }
        if (element is FrameworkElement panel && IsFlow(panel))
        {
            var flow = FlowFor(panel);
            baseline = last ? flow.LastBaseline : flow.FirstBaseline;
            return double.IsFinite(baseline);
        }
        if (element is TextBlock nativeText && !HasBoxFormatter(nativeText) && CssTableLayout.InCell(nativeText))
        {
            baseline = last ? nativeText.GetLastBaselineOffset(borderHeight) : nativeText.GetFirstBaselineOffset(borderHeight);
            return double.IsFinite(baseline);
        }
        if (element is FrameworkElement gridOwner && gridOwner.CssDisplayMode == CssDisplayMode.Grid &&
            s_gridLayouts.TryGetValue(gridOwner, out var grid))
        {
            baseline = grid.BaselineOffset(borderHeight, last);
            return double.IsFinite(baseline);
        }
        if (element is FlexPanel flex && flex.CssDisplayMode is CssDisplayMode.Native or CssDisplayMode.Flex)
        {
            baseline = flex.BaselineOffset(borderHeight, last);
            return double.IsFinite(baseline);
        }
        if (element is FrameworkElement flexOwner && flexOwner.CssDisplayMode == CssDisplayMode.Flex &&
            s_flexLayouts.TryGetValue(flexOwner, out var layout))
        {
            baseline = layout.BaselineOffset(borderHeight, last);
            return double.IsFinite(baseline);
        }
        baseline = double.NaN;
        return false;
    }
    internal static bool IsImplicitHost(FrameworkElement element) => element is TextBlock { HasCssInlineBoxes: true } or TextBlock { UsesAnonymousRubyContent: true } ||
        element is ContentControl { NeedsCssContentAdapter: true } ||
        element.CssInlineOuter && CssInitialLetterProperties.Metrics(element) is not null ||
        CssLineClampProperties.Active(element) || CssTableLayout.HasMisparentedParts(element) || element is not Panel &&
        (element.HasCssGeneratedChildren && CssGeneratedContent.HasBoxes(element) || element is CssGeneratedText { HasImageContent: true, IsImageReplacement: false });
    internal static bool IsFlow(FrameworkElement element) => !CssReplacedContent.IsActive(element) && element.IsCssContentHost &&
        (CssContentsLayout.IsRoot(element) || CssTableLayout.Role(element) is CssDisplayMode.Block or CssDisplayMode.FlowRoot or CssDisplayMode.TableCell or CssDisplayMode.TableCaption ||
         CssRubyRoles.IsBox(element.CssDisplayMode) || element.CssDisplayMode == CssDisplayMode.Native && (element.CssInlineOuter || IsImplicitHost(element) || CssMultiColumnProperties.Active(element)));
    internal static bool HasBoxFormatter(FrameworkElement element) => !CssReplacedContent.IsActive(element) && element.IsCssContentHost &&
        (CssContentsLayout.IsRoot(element) || element is FlexPanel || element.CssDisplayMode is CssDisplayMode.Flex or CssDisplayMode.Grid or CssDisplayMode.Block or CssDisplayMode.FlowRoot or CssDisplayMode.Table || CssRubyRoles.IsBox(element.CssDisplayMode) || CssTableLayout.IsPart(element.CssDisplayMode) || IsImplicitHost(element) || CssMultiColumnProperties.Active(element));
    internal static CssGridLayout GridFor(FrameworkElement owner) => s_gridLayouts.GetValue(owner, static panel => new CssGridLayout(panel));
    internal static bool IsSubgridded(FrameworkElement element, bool row) =>
        element.CssDisplayMode == CssDisplayMode.Grid && element.IsCssContentHost &&
        s_gridLayouts.TryGetValue(element, out var layout) && layout.IsSubgridded(row);

    internal static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
        "DisplayMode", typeof(CssDisplayMode), typeof(CssDisplayLayout),
        new PropertyMetadata(CssDisplayMode.Native, static (target, args) =>
        {
            if (target is FrameworkElement element)
            {
                if ((CssDisplayMode)args.OldValue! == CssDisplayMode.Ruby && s_rubyLayouts.TryGetValue(element, out var ruby))
                { ruby.ReleaseRubyFormatting(); s_rubyLayouts.Remove(element); }
                element.CssDisplayMode = CssLineClampProperties.UsedMode(element, (CssDisplayMode)args.NewValue!);
                CssFirstLine.StructureChanged(element);
                if ((CssDisplayMode)args.OldValue! == CssDisplayMode.Contents || element.CssDisplayMode == CssDisplayMode.Contents)
                    CssContentsLayout.Changed(element);
                CssTableLayout.DisplayChanged(element);
                InvalidateFormattingAncestors(element);
                if (element is Panel panel) panel.InvalidateCssFlowOrder();
                if (element.IsCssContentHost)
                    foreach (var child in new CssPanelChildren(element, includeInactive: true))
                    {
                        child.InvalidateMeasure();
                        child.InvalidateArrange();
                        if (CssDisplayProperties.HasVisibilityDeclarations)
                            child.UpdateIsVisibleFromTree(forceDescendants: true);
                    }
            }
        }));

    internal static readonly DependencyProperty InlineProperty = DependencyProperty.RegisterAttached(
        "InlineOuter", typeof(bool), typeof(CssDisplayLayout), new PropertyMetadata(false, static (target, args) =>
        {
            if (target is not FrameworkElement element) return;
            element.CssInlineOuter = (bool)args.NewValue!;
            CssFirstLine.StructureChanged(element);
            InvalidateFormattingAncestors(element);
            // Inline and block children occupy different CSS paint phases.
            // A stylesheet can change them after a panel cached its order;
            // preserve source order when the children become inline peers.
            for (var ancestor = element.VisualParent; ancestor is not null; ancestor = ancestor.VisualParent)
                if (ancestor is Panel panel) panel.InvalidateCssFlowOrder();
        }));

    private static void InvalidateFormattingAncestors(FrameworkElement element)
    {
        // A changed formatter can alter every containing block's intrinsic size.
        // Offscreen roots have no LayoutManager to promote a descendant request.
        for (Visual? current = element; current is not null; current = current.VisualParent)
            if (current is UIElement owner) owner.InvalidateMeasure();
    }

    public static bool TryMeasure(FrameworkElement element, Size available, out Size result)
        => TryMeasure(element, available, element.CssParentBox?.ContainingBlock ?? element.CssNativeBox?.ContainingBlock ?? available, out result);

    internal static bool TryMeasure(FrameworkElement element, Size available, Size containingBlock, out Size result)
    {
        if (TextBlock.CssTextFlowLayout.IsAllocatedRubyContainer(element))
        { result = element.RenderSize; return true; }
        if (element is TextBlock { UsesNativeRubyContent: true } rubyText)
        { result = rubyText.MeasureNativeRubyContent(available); return true; }
        if (CssMultiColumnLayout.TryMeasureApplying(element, available, out result)) return true;
        if (CssTableLayout.TryMeasureAssigned(element, available, out result)) return true;
        if (!element.IsCssContentHost) { result = default; return false; }
        element.PrepareCssContent();
        if (element.CssDisplayMode == CssDisplayMode.Ruby)
        { result = RubyFor(element).Measure(available); return true; }
        if (CssLineClampProperties.Active(element) && !CssMultiColumnProperties.Active(element))
        { result = CssLineClampLayout.For(element).Measure(available); return true; }
        if (CssMultiColumnProperties.Active(element))
        { result = CssMultiColumnLayout.For(element).Measure(available); return true; }
        if (element.CssDisplayMode == CssDisplayMode.Table)
        { result = TableFor(element).Measure(available, containingBlock); return true; }
        if (CssTableLayout.Role(element) == CssDisplayMode.TableCell)
        { result = CssTableLayout.MeasureCell(element, available); return true; }
        if (CssTableLayout.IsInternal(CssTableLayout.Role(element)))
        { result = TableFor(element).Measure(available, containingBlock); return true; }
        if (element is TextBlock { CanFormatCssInline: true } text)
        {
            result = text.CssTextFlow.Measure(available);
            return true;
        }
        if (IsFlow(element) && element is FrameworkElement flowOwner)
        {
            result = FlowFor(flowOwner).Measure(available);
            return true;
        }
        if (element.CssDisplayMode == CssDisplayMode.Grid && element is FrameworkElement gridOwner)
        {
            result = GridFor(gridOwner).Measure(available);
            return true;
        }
        if (element.CssDisplayMode == CssDisplayMode.Flex && element is FrameworkElement panel and not FlexPanel)
        {
            result = s_flexLayouts.GetValue(panel, static owner => new FlexPanel(owner)).MeasureCssLayout(available);
            return true;
        }
        result = default;
        return false;
    }

    public static bool TryArrange(FrameworkElement element, Size finalSize, out Size result)
        => TryArrange(element, finalSize, element.CssParentBox?.ContainingBlock ?? element.CssNativeBox?.ContainingBlock ?? finalSize, out result);

    internal static bool TryArrange(FrameworkElement element, Size finalSize, Size containingBlock, out Size result)
    {
        // The outer Ruby formatter owns these structural containers and the
        // original source slots below them. A later LayoutManager pass must
        // not start an independent flow that overwrites those allocations.
        if (TextBlock.CssTextFlowLayout.IsAllocatedRubyContainer(element))
        { result = finalSize; return true; }
        if (element is TextBlock { UsesNativeRubyContent: true })
        { result = finalSize; return true; }
        if (element.IsCssContentHost && element.CssDisplayMode == CssDisplayMode.Ruby)
        { result = RubyFor(element).Arrange(finalSize); return true; }
        if (CssMultiColumnLayout.TryArrangeApplying(element, finalSize, out result)) return true;
        if (CssTableLayout.TryArrangeAssigned(element, finalSize, out result)) return true;
        if (!element.IsCssContentHost) { result = default; return false; }
        if (CssLineClampProperties.Active(element) && !CssMultiColumnProperties.Active(element))
        { result = CssLineClampLayout.For(element).Arrange(finalSize); return true; }
        if (CssMultiColumnProperties.Active(element))
        { result = CssMultiColumnLayout.For(element).Arrange(finalSize); return true; }
        if (element.CssDisplayMode == CssDisplayMode.Table)
        { result = TableFor(element).Arrange(finalSize, containingBlock); return true; }
        if (CssTableLayout.Role(element) == CssDisplayMode.TableCell)
        { result = FlowFor(element).Arrange(finalSize); return true; }
        if (CssTableLayout.IsInternal(CssTableLayout.Role(element)))
        { result = TableFor(element).Arrange(finalSize, containingBlock); return true; }
        if (element is TextBlock { CanFormatCssInline: true } text)
        {
            result = text.CssTextFlow.Arrange(finalSize);
            return true;
        }
        if (IsFlow(element) && element is FrameworkElement flowOwner)
        {
            result = FlowFor(flowOwner).Arrange(finalSize);
            return true;
        }
        if (element.CssDisplayMode == CssDisplayMode.Grid && element is FrameworkElement gridOwner)
        {
            result = GridFor(gridOwner).Arrange(finalSize);
            return true;
        }
        if (element.CssDisplayMode == CssDisplayMode.Flex && element is FrameworkElement panel and not FlexPanel)
        {
            result = s_flexLayouts.GetValue(panel, static owner => new FlexPanel(owner)).ArrangeCssLayout(finalSize);
            return true;
        }
        result = default;
        return false;
    }
}
