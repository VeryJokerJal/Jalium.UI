namespace Jalium.UI.Styling;

/// <summary>
/// Per-element CSS runtime state, carried by a single field on FrameworkElement.
/// Null until CSS first touches the element, so non-CSS apps pay one pointer per element.
/// </summary>
internal sealed class CssElementState
{
    public static readonly string[] EmptyClasses = Array.Empty<string>();

    /// <summary>Parsed Css.Class values (sorted, deduplicated).</summary>
    public string[] Classes = EmptyClasses;

    /// <summary>Compiled inline declarations from Css.Style, or null when unset.</summary>
    public CssCompiledDeclaration[]? InlineDeclarations;

    /// <summary>Source text of <see cref="InlineDeclarations"/>, kept so a mapping-table change can recompile it.</summary>
    public string? InlineText;

    /// <summary>Registry version <see cref="InlineDeclarations"/> was compiled against.</summary>
    public int InlineVersion;

    /// <summary>Element-scoped style sheets (Css.StyleSheets), applying to this element's subtree.</summary>
    public CssStyleSheetCollection? ScopedStyleSheets;

    /// <summary>The sheet parsed from <c>Css.StyleSheet</c>, tracked so a re-set can replace it.</summary>
    public CssStyleSheet? DeclaredStyleSheet;

    /// <summary>DP values the CSS engine currently owns on this element, by layer.</summary>
    public Dictionary<DependencyProperty, AppliedCssValue>? Applied;

    /// <summary>The layout-state snapshot the engine last applied (diff baseline).</summary>
    public CssLayoutState? AppliedLayout;

    /// <summary>Cascade version at the last evaluation (style-sheet hot-swap detection).</summary>
    public int CascadeVersion;

    /// <summary>Subject-position pseudo-class dependencies from the last evaluation.</summary>
    public CssStateMask SelfStates;

    /// <summary>Union of ancestor-position pseudo-classes across the sheets in scope.</summary>
    public CssStateMask ScopeAncestorStates;

    /// <summary>True when any sheet in scope uses #id selectors (Name changes then re-match).</summary>
    public bool ScopeUsesId;

    /// <summary>The PropertyChangedInternal handler wired for dynamic-state invalidation.</summary>
    public Action<DependencyProperty, object?, object?>? Handler;
}

internal readonly struct AppliedCssValue
{
    public readonly DependencyObject.LayerValueSource Layer;
    public readonly object? Value;

    public AppliedCssValue(DependencyObject.LayerValueSource layer, object? value)
    {
        Layer = layer;
        Value = value;
    }
}
