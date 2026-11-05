using System.Runtime.CompilerServices;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Styles a displayed hint without changing the input or its document tree.</summary>
internal static class CssPlaceholder
{
    private sealed class Target
    {
        internal bool Potential;
        internal readonly List<WeakReference<FrameworkElement>> Parts = [];
    }
    private sealed class Alias(Control owner) { internal readonly Control Owner = owner; }
    private sealed class Cache(Control owner)
    {
        internal readonly View Text = new(CssNode.Get(owner));
        internal bool Preparing;
    }
    private static readonly ConditionalWeakTable<Control, Target> Targets = new();
    private static readonly ConditionalWeakTable<FrameworkElement, Alias> Aliases = new();
    private static readonly ConditionalWeakTable<FrameworkElement, Cache> Views = new();

    internal sealed class View(CssNode origin) : CssGeneratedText(origin, CssPseudoElement.Placeholder)
    {
        protected override AutomationPeer? OnCreateAutomationPeer() => null;
    }

    internal static bool Applies(string name) => CssFirstLine.Applies(name) &&
        name is not ("line-height" or "vertical-align");

    internal static void Update(CssNode origin, CssPseudoElement targets)
    {
        if (origin.Target is not Control control) return;
        var selected = (targets & CssPseudoElement.Placeholder) != 0;
        if (!selected && !Targets.TryGetValue(control, out _)) return;
        var target = Targets.GetOrCreateValue(control); target.Potential = selected;
        control.InvalidateMeasure(); control.InvalidateVisual();
        target.Parts.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in target.Parts)
            if (reference.TryGetTarget(out var part)) InvalidatePart(part);
    }

    internal static void Bind(FrameworkElement? target, Control owner)
    {
        if (target is null) return;
        Unbind(target); Aliases.Add(target, new(owner));
        Targets.GetOrCreateValue(owner).Parts.Add(new(target));
        InvalidatePart(target);
    }
    internal static void Unbind(FrameworkElement? target)
    {
        if (target is null) return;
        if (Aliases.TryGetValue(target, out var alias) && Targets.TryGetValue(alias.Owner, out var owner))
            owner.Parts.RemoveAll(reference => !reference.TryGetTarget(out var part) || ReferenceEquals(part, target));
        Aliases.Remove(target); InvalidatePart(target);
    }

    private static void InvalidatePart(DependencyObject source)
    {
        if (source is UIElement visual) { visual.InvalidateMeasure(); visual.InvalidateVisual(); }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++)
            InvalidatePart(VisualTreeHelper.GetChild(source, index));
    }

    internal static Control? AliasOwner(FrameworkElement source)
    {
        for (FrameworkElement? current = source; current is not null; current = current.FrameworkParent)
            if (Aliases.TryGetValue(current, out var alias)) return alias.Owner;
        return null;
    }

    internal static Control? TextOwner(TextBlock source)
    {
        if (source is CssGeneratedText || !CssEngine.IsActive) return null;
        var owner = AliasOwner(source);
        if (owner is null || !CssPlaceholderState.IsShown(owner)) return null;
        var property = CssDependencyPropertyLookup.Find(owner.GetType(), "PlaceholderText");
        return property is not null && string.Equals(owner.GetValue(property) as string, source.Text, StringComparison.Ordinal) ? owner : null;
    }

    internal static View? Prepare(FrameworkElement source, Control owner, string text, Brush fallback)
    {
        if (!CssEngine.IsActive || text.Length == 0 ||
            !Targets.TryGetValue(owner, out var target) || !target.Potential) return null;
        var cache = Views.GetValue(source, _ => new(owner));
        if (!ReferenceEquals(cache.Text.Origin.Target, owner))
        { Views.Remove(source); cache = Views.GetValue(source, _ => new(owner)); }
        var view = cache.Text;
        if (cache.Preparing) return view;
        cache.Preparing = true;
        try
        {
            foreach (var property in CssPropertyRegistry.GetInheritedStorageProperties()
                .Concat(new[] { TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.FontWeightProperty,
                    TextElement.FontStyleProperty, TextElement.FontStretchProperty, FrameworkElement.FlowDirectionProperty }).Distinct())
            {
                if (!property.ReadOnly && property != TextElement.ForegroundProperty)
                    view.SetLayerValue(property, source.GetValue(property), DependencyObject.LayerValueSource.StyleSetter);
            }
            view.SetLayerValue(TextElement.ForegroundProperty, fallback, DependencyObject.LayerValueSource.StyleSetter);
            if (source is TextBlock block)
                view.SetLayerValue(TextElement.BackgroundProperty, block.Background, DependencyObject.LayerValueSource.StyleSetter);
            view.SetRootDpi(source.DpiScale);
            view.Select(true);
            if (view.Text != text) view.SetValue(CssGeneratedContent.ContentProperty, new CssGeneratedContentValue(text));
            CssEngine.EvaluateElement(view);
            return CssNode.Get(view).CssRuntimeState?.Applied is { Count: > 0 } ? view : null;
        }
        finally { cache.Preparing = false; }
    }
}
