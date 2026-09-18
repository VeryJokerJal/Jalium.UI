using System.Collections;
using System.Runtime.CompilerServices;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Styling;

/// <summary>A shared styling view of visual elements and nonvisual XAML document nodes.</summary>
internal sealed class CssNode
{
    private static readonly ConditionalWeakTable<DependencyObject, CssNode> s_nodes = new();
    private CssElementState? _contentState;
    private CssNode(DependencyObject target) => Target = target;
    public DependencyObject Target { get; }
    public static CssNode Get(DependencyObject target) => s_nodes.GetValue(target, static value => new CssNode(value));
    public static implicit operator CssNode(FrameworkElement target) => Get(target);
    public static implicit operator CssNode(FrameworkContentElement target) => Get(target);
    public new Type GetType() => Target.GetType();
    internal CssExpandedName ExpandedName => CssXmlIdentity.Name(Target);
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
            var parent = Target switch
            {
                FrameworkElement element => (DependencyObject?)element.FrameworkParent,
                FrameworkContentElement content => content.Parent,
                _ => null,
            };
            return parent is FrameworkElement or FrameworkContentElement ? Get(parent) : null;
        }
    }
    public object? TemplatedParent => Target switch
    { FrameworkElement e => e.TemplatedParent, FrameworkContentElement e => e.TemplatedParent, _ => null };
    public string Name => Target switch { FrameworkElement e => e.Name, FrameworkContentElement e => e.Name, _ => string.Empty };
    public double Width => Target is FrameworkElement element ? element.Width : double.NaN;
    public double Height => Target is FrameworkElement element ? element.Height : double.NaN;
    public double ActualWidth => Target is FrameworkElement element ? element.ActualWidth : 0;
    public double ActualHeight => Target is FrameworkElement element ? element.ActualHeight : 0;
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
        for (var current = this; current is not null; current = current.FrameworkParent)
            if (current.Target is FrameworkElement element) { element.InvalidateMeasure(); return; }
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
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
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
                if (VisualTreeHelper.GetChild(visual, i) is FrameworkElement child && seen.Add(child)) yield return Get(child);
    }
}
