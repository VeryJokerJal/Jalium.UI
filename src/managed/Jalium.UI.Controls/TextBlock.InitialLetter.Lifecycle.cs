using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    // A scope owns private used fonts, metric overrides, inline allocations and
    // an exclusion in its originating formatter. Retire those together before
    // a different layout path can measure or draw the same authored content.
    internal static void RetireInitialLetterScope(FrameworkElement element) => ClearInitialScope(element);

    private static bool InitialScopeIsCurrent(InitialLetterScope scope)
    {
        var root = scope.Root;
        if (root.Visibility == Visibility.Collapsed || !root.CssInlineOuter ||
            root.CssDisplayMode == CssDisplayMode.Contents || CssDisplayProperties.SuppressesBoxTree(root) ||
            root.CssLayout?.Position is not (null or CssPositionMode.Static) ||
            root.GetValue(CssFloatProperties.FloatProperty) is not CssFloatSide.None ||
            root.GetValue(CssInitialLetterProperties.ValueProperty) is not CssInitialLetter { Active: true } ||
            InitialIsAtomic(root) != scope.Atomic || scope.OriginOwner is null) return false;
        for (Visual? current = root.VisualParent; current is not null; current = current.VisualParent)
            if (ReferenceEquals(current, scope.OriginOwner)) return true;
        return false;
    }

    private sealed partial class InitialLetterScope
    {
        internal void StateChanged(DependencyProperty property, object? before, object? after)
        {
            if (property == CssDisplayProperties.SpecificationProperty || property == CssDisplayLayout.ModeProperty ||
                property == CssDisplayLayout.InlineProperty || property == CssFloatProperties.FloatProperty ||
                property == LayoutTransformProperty || property == FontSizeProperty ||
                property == VisibilityProperty && after is Visibility.Collapsed ||
                property == CssInitialLetterProperties.ValueProperty && after is CssInitialLetter { Active: false })
                ClearInitialScope(Root);
        }
    }

    internal sealed partial class CssTextFlowLayout
    {
        internal void RetireInitialAllocation(FrameworkElement root)
        {
            if (_initialLetter is not { } initial || !ReferenceEquals(initial.Scope.Root, root)) return;
            initial.Region = null; initial.Placed = false;
            if (initial.Frame is { } initialFrame)
                foreach (var frame in _frames)
                    if (ReferenceEquals(frame, initialFrame) || BelongsToFrame(frame, initialFrame))
                        frame.Element.ReleaseCssInlineFrame(owner);
            foreach (var source in _sources)
                if (ReferenceEquals(source.Initial, initial))
                {
                    TextSource(source.Element)?.ReleaseCssInlineAllocation(owner);
                    if (source.Element is FrameworkElement element && element.CssInlineLayoutOwner == owner)
                        element.CssInlineLayoutOwner = null;
                }
            _initialLetter = null;
        }
    }
}
