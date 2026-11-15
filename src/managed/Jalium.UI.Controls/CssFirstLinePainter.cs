using System.Runtime.CompilerServices;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Paints the fictional inline outside its real descendants. Layout and hit
/// testing continue to use the authored boxes and their existing fragments.
/// </summary>
internal static class CssFirstLinePainter
{
    internal sealed record Layer(Border Style, Rect Bounds);
    internal sealed class Allocation(Visual owner, Layer[] layers, Func<bool> active,
        Dictionary<Visual, bool> terminals, Dictionary<FrameworkElement, bool[]> frames)
    {
        internal readonly Visual Owner = owner;
        internal readonly Layer[] Layers = layers;
        internal readonly Func<bool> IsActive = active;
        internal readonly Dictionary<Visual, bool> Terminals = terminals;
        internal readonly Dictionary<FrameworkElement, bool[]> Frames = frames;
        internal readonly HashSet<Visual> FirstTree = [];
        internal Point Offset;

        internal void Prepare()
        {
            foreach (var terminal in Terminals.Where(pair => pair.Value))
                for (Visual? visual = terminal.Key; visual is not null; visual = visual.VisualParent)
                { FirstTree.Add(visual); if (ReferenceEquals(visual, Owner)) break; }
        }
    }

    private sealed record Pass(Allocation Allocation, bool FirstLine);
    private static readonly ConditionalWeakTable<Visual, Allocation> s_allocations = new();
    [ThreadStatic] private static Pass? s_pass;
    internal static bool Active => s_pass is not null;
    internal static bool HasAllocation(Visual owner) => s_allocations.TryGetValue(owner, out _);
    internal static bool CanCombineSelection(Visual owner) => !s_allocations.TryGetValue(owner, out var allocation) ||
        allocation.Layers.All(layer => CssInlineSelectionPainter.IsNeutral(layer.Style));

    internal static void Set(Visual owner, Allocation? allocation)
    {
        var removed = s_allocations.Remove(owner);
        if (allocation is not null)
        { allocation.Prepare(); s_allocations.Add(owner, allocation); }
        if ((removed || allocation is not null) && owner is UIElement element) element.InvalidateVisual();
    }

    internal static void Offset(Visual owner, Thickness insets)
    { if (s_allocations.TryGetValue(owner,out var allocation)) allocation.Offset = new(insets.Left,insets.Top); }

    private static bool? Terminal(Visual visual, Allocation allocation)
    {
        for (Visual? node = visual; node is not null && !ReferenceEquals(node, allocation.Owner); node = node.VisualParent)
            if (allocation.Terminals.TryGetValue(node, out var first)) return first;
        return null;
    }

    internal static bool Skip(Visual visual)
    {
        if (s_pass is not { } pass) return false;
        if (Terminal(visual, pass.Allocation) is { } first) return first != pass.FirstLine;
        return pass.FirstLine && !pass.Allocation.FirstTree.Contains(visual);
    }

    internal static bool SuppressOwn(Visual visual)
    {
        if (s_pass is not { } pass || Terminal(visual, pass.Allocation).HasValue) return false;
        if (visual is FrameworkElement element && pass.Allocation.Frames.TryGetValue(element, out var flags))
            return !flags.Contains(pass.FirstLine);
        return pass.FirstLine;
    }

    internal static IReadOnlyList<CssInlineBoxFragment> Fragments(FrameworkElement element, IReadOnlyList<CssInlineBoxFragment> fragments)
    {
        if (s_pass is not { } pass || !pass.Allocation.Frames.TryGetValue(element, out var flags) || flags.Length != fragments.Count)
            return fragments;
        return fragments.Where((_, index) => flags[index] == pass.FirstLine).ToArray();
    }

    internal static void Children(Visual owner, DrawingContext context, Action render)
    {
        if (!s_allocations.TryGetValue(owner, out var allocation) || ReferenceEquals(s_pass?.Allocation, allocation))
        { render(); return; }
        if (!allocation.IsActive())
        { Set(owner,null); render(); return; }
        var previous = s_pass;
        var opacity = 0;
        try
        {
            s_pass = new(allocation, true);
            foreach (var layer in allocation.Layers)
            {
                if (layer.Style.Opacity < 1 && context is IOpacityDrawingContext group)
                { group.PushOpacity(layer.Style.Opacity); opacity++; }
                if (layer.Style.Background is not { } background) continue;
                var bounds = layer.Bounds;
                var rect = new Rect(bounds.X + allocation.Offset.X,bounds.Y + allocation.Offset.Y,bounds.Width,bounds.Height);
                void Shape(Brush brush) => context.DrawRectangle(brush, null, rect);
                if (!CssBackgroundPainter.TryDraw(layer.Style, Border.BackgroundProperty, background,
                    context, rect, default, default, default, Shape, textClipOwner: owner as FrameworkElement)) Shape(background);
            }
            render();
            while (opacity > 0)
            { ((IOpacityDrawingContext)context).PopOpacity(); opacity--; }
            s_pass = new(allocation, false);
            render();
        }
        finally
        {
            while (opacity > 0)
            { ((IOpacityDrawingContext)context).PopOpacity(); opacity--; }
            s_pass = previous;
        }
    }
}
