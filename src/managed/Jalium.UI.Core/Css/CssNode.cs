using System.Collections;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Styling;

/// <summary>A shared styling view of visual elements and nonvisual XAML document nodes.</summary>
internal sealed class CssNode
{
    private static readonly ConditionalWeakTable<DependencyObject, CssNode> s_nodes = new();
    private static readonly ConditionalWeakTable<DependencyObject, FrameworkContentElement> s_documentContainerParents = new();
    private CssElementState? _contentState;
    private CssNode(DependencyObject target) => Target = target;
    public DependencyObject Target { get; }
    public static CssNode Get(DependencyObject target) => s_nodes.GetValue(target, static value => new CssNode(value));
    internal static CssNode? Existing(DependencyObject target)
        => s_nodes.TryGetValue(target, out var node) ? node : null;
    internal static void SetDocumentContainerParent(DependencyObject? child, FrameworkContentElement? container)
    {
        if (child is null) return;
        s_documentContainerParents.Remove(child);
        if (container is not null) s_documentContainerParents.Add(child, container);
    }
    internal static FrameworkContentElement? DocumentContainerParent(DependencyObject target)
        => s_documentContainerParents.TryGetValue(target, out var container) ? container : null;
    internal static FrameworkContentElement? ContentInheritanceParent(FrameworkElement element)
        => DocumentContainerParent(element) ?? (CssGeneratedContent.Origin(Get(element))?.Target as FrameworkContentElement);
    public static implicit operator CssNode(FrameworkElement target) => Get(target);
    public static implicit operator CssNode(FrameworkContentElement target) => Get(target);
    public new Type GetType() => Target.GetType();
    internal CssExpandedName ExpandedName => Target is CssXmlElement xml ? new(xml.Source.Name.NamespaceName, xml.Source.Name.LocalName) : CssXmlIdentity.Name(Target);
    public Dispatcher Dispatcher => Target.Dispatcher;
    public CssElementState? CssRuntimeState
    {
        get => Target is FrameworkElement element ? element.CssRuntimeState : _contentState;
        set { if (Target is FrameworkElement element) element.CssRuntimeState = value; else _contentState = value; }
    }
    public CssLayoutState? CssLayout
    {
        get => Target is FrameworkElement element ? element.CssLayout : CssRuntimeState?.AppliedLayout;
        set { if (Target is FrameworkElement element) element.CssLayout = value; }
    }
    public CssNode? FrameworkParent
    {
        get
        {
            if (CssSelection.StyleParent(Target) is { } selectionParent) return Get(selectionParent);
            if (CssFirstLetter.StyleParent(Target) is { } textParent) return Get(textParent);
            if (CssFirstLine.StyleParent(Target) is { } lineParent) return Get(lineParent);
            if (CssGeneratedContent.Origin(this) is { } origin) return origin;
            if (DocumentContainerParent(Target) is { } docContainer) return Get(docContainer);
            var parent = Target switch
            {
                FrameworkElement element => (DependencyObject?)element.FrameworkParent,
                FrameworkContentElement content => content.Parent,
                CssXmlElement xml => xml.Parent,
                _ => null,
            };
            return parent is FrameworkElement or FrameworkContentElement or CssXmlElement ? Get(parent) : null;
        }
    }
    public object? TemplatedParent => Target switch
    { FrameworkElement e => e.TemplatedParent, FrameworkContentElement e => e.TemplatedParent, _ => null };
    public string Name => Target switch { FrameworkElement e => e.Name, FrameworkContentElement e => e.Name, CssXmlElement xml => xml.Source.Attribute("id")?.Value ?? string.Empty, _ => string.Empty };
    public double Width => Target is FrameworkElement element ? element.Width : double.NaN;
    public double Height => Target is FrameworkElement element ? element.Height : double.NaN;
    public double ActualWidth => Target is FrameworkElement element ? element.ActualWidth : 0;
    public double ActualHeight => Target is FrameworkElement element ? element.ActualHeight : 0;
    internal Visual? VisualOwner
    {
        get
        {
            Visual? owner = null;
            for (var node = this; node is not null; node = node.FrameworkParent)
            {
                if (node.Target is Visual visual)
                    return CssDocumentConsumers.Sharper(CssDocumentConsumers.Preferred(node.Target, owner), visual);
                owner = CssDocumentConsumers.Preferred(node.Target, owner);
            }
            return owner;
        }
    }
    public bool IsMouseOver => Target switch { FrameworkElement e => e.IsMouseOver, ContentElement e => e.IsMouseOver, _ => false };
    public bool IsFocused => Target switch { FrameworkElement e => e.IsFocused, ContentElement e => e.IsFocused, _ => false };
    public bool IsKeyboardFocused => Target switch { FrameworkElement e => e.IsKeyboardFocused, ContentElement e => e.IsKeyboardFocused, _ => false };
    public bool IsKeyboardFocusWithin => Target switch { FrameworkElement e => e.IsKeyboardFocusWithin, ContentElement e => e.IsKeyboardFocusWithin, _ => false };
    public bool IsEnabled => Target switch { FrameworkElement e => e.IsEnabled, ContentElement e => e.IsEnabled, _ => true };
    public object? GetValue(DependencyProperty property) => Target.GetValue(property);
    public object? ReadLocalValue(DependencyProperty property) => Target.ReadLocalValue(property);
    public bool HasLocalValue(DependencyProperty property) => Target.HasLocalValue(property);
    public bool HasLocalOrAnimatedValue(DependencyProperty property) => Target.HasLocalOrAnimatedValue(property);
    public void ClearLayerValue(DependencyProperty property, DependencyObject.LayerValueSource layer, bool allowAutoTransition)
        => Target.ClearLayerValue(property, layer, allowAutoTransition);
    public void SetLayerValue(DependencyProperty property, object? value, DependencyObject.LayerValueSource layer, bool allowAutoTransition)
        => Target.SetLayerValue(property, value, layer, allowAutoTransition);
    public bool TryApplyCssPropertyCore(string name, string value, in CssDeclarationSetter setter)
        => Target is FrameworkElement element && element.TryApplyCssPropertyCore(name, value, in setter);
    public void InvalidateMeasure()
    {
        if (Target is Jalium.UI.Documents.TextElement text) text.NotifyTextContentChanged();
        else if (Target is Jalium.UI.Documents.FlowDocument document) document.NotifyPaginationStyleChanged();
        for (var current = this; current is not null; current = current.FrameworkParent)
            if (current.Target is FrameworkElement element)
            {
                element.InvalidateMeasure();
                for (var parent = element.VisualParent as FrameworkElement; parent is not null; parent = parent.VisualParent as FrameworkElement)
                {
                    parent.InvalidateMeasure();
                    if (!CssDisplayLayout.IsFlow(parent) && !parent.CanFragmentCssInlineContent) break;
                }
                return;
            }
    }
    public event Action<DependencyProperty, object?, object?> PropertyChangedInternal
    {
        add => Target.PropertyChangedInternal += value;
        remove => Target.PropertyChangedInternal -= value;
    }
    public event SizeChangedEventHandler SizeChanged
    {
        add { if (Target is FrameworkElement element) element.SizeChanged += value; }
        remove { if (Target is FrameworkElement element) element.SizeChanged -= value; }
    }
    public IEnumerator LogicalChildren => EnumerateChildren().GetEnumerator();
    public IEnumerable<CssNode> EnumerateChildren()
    {
        if (Target is CssXmlElement xml)
        { foreach (var child in xml.Children) yield return Get(child); yield break; }
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        // Logical registration appends even when an authored collection inserts
        // earlier. Structural selectors must follow the actual document order.
        IEnumerable authored = Target switch
        {
            Panel panel => panel.Children,
            // A document proxy borrows these nodes; their CSS parent remains
            // the original Paragraph, which already enumerates them.
            TextBlock { IsDocumentParagraph: false, CssOriginalInlines: {} inlines } => inlines,
            FlowDocument document => document.Blocks,
            Section section => section.Blocks,
            Jalium.UI.Documents.List list => list.ListItems,
            ListItem item => item.Blocks,
            Table table => table.Columns.Cast<DependencyObject>().Concat(table.RowGroups),
            TableRowGroup group => group.Rows,
            TableRow row => row.Cells,
            TableCell cell => cell.Blocks,
            BlockUIContainer { Child: { } blockChild } => new[] { blockChild },
            Paragraph paragraph => paragraph.Inlines,
            Span span => span.Inlines,
            InlineUIContainer { Child: { } inlineChild } => new[] { inlineChild },
            _ => Array.Empty<object>(),
        };
        foreach (var value in authored)
            if (value is DependencyObject child && child is FrameworkElement or FrameworkContentElement && seen.Add(child))
                yield return Get(child);
        var logical = Target switch
        {
            FrameworkElement element => element.LogicalChildren,
            FrameworkContentElement content => content.LogicalChildren,
            _ => Array.Empty<object>().GetEnumerator(),
        };
        while (logical.MoveNext())
            if (logical.Current is DependencyObject child && child is FrameworkElement or FrameworkContentElement && seen.Add(child))
                yield return Get(child);
        if (Target is Visual visual)
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
                if (VisualTreeHelper.GetChild(visual, i) is FrameworkElement child &&
                    child is not ICssAnonymousBox and not ICssGeneratedContentBox and not ICssStyledBox and not CssGeneratedText && seen.Add(child)) yield return Get(child);
    }
}
