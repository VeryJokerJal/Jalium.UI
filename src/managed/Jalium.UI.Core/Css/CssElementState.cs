namespace Jalium.UI.Styling;

/// <summary>
/// Per-element CSS runtime state, carried by a single field on FrameworkElement.
/// Null until CSS first touches the element, so non-CSS apps pay one pointer per element.
/// </summary>
internal sealed class CssElementState
{
    // Computed token streams, including inherited custom properties. Names are case-sensitive.
    public IReadOnlyDictionary<string, string>? CustomProperties;
    // Attribute origin survives typed custom-property computation and inheritance.
    public IReadOnlySet<string>? AttrTaintedProperties;
    public IReadOnlyDictionary<string, CssPropertyRegistration>? RegisteredProperties;
    public IReadOnlyDictionary<string, CssTypedValue>? RegisteredValues;
    // CurrentColor-dependent computed color expressions stay symbolic so an
    // explicit inherit can resolve them against the child's own color.
    public Dictionary<string, CssContextualColorValue>? ContextualColors;
    public double? FontStretchPercentage;
    public CssComputedFontStyle? FontStyleComputed;
    public CssComputedFontFamily? FontFamilyComputed;
    public Dictionary<string, CssInheritedColorObserver>? InheritedColorObservers;
    public HashSet<string>? ObservedInheritedColors;
    public Dictionary<string, CssInheritedBoxObserver>? InheritedBoxObservers;
    public HashSet<string>? ObservedInheritedBoxes;
    public List<CssColorBrushObserver>? ColorBrushObservers;
    public CssContainerDependent? ContainerDependent;
    public CssFontDependency? FontDependency;
    public CssMediaPreferenceDependency? MediaPreferenceDependency;
    public CssSelectorDependent? SelectorDependent;
    public CssQueryContainer? QueryContainer;
    public bool ObservesViewport;
    public Size? ViewportAllocation;
    public bool ObservesTypography;
    public bool ObservesOwnSize;
    public bool ObservesTransformInsets;
    public string? TransformSource;
    public Size TransformReferenceSize;
    public CssLengthContext TransformLengths;
    public Jalium.UI.Media.Transform? ContextTransform;
    public System.Collections.Specialized.NotifyCollectionChangedEventHandler? AttributesChanged;
    public bool StructuralDependencies;
    public HashSet<string>? NativeAttributeNames;
    public Dictionary<string, object?>? NativeAttributeValues;
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
    public Action? StyleSheetsChangedHandler;

    /// <summary>The sheet parsed from <c>Css.StyleSheet</c>, tracked so a re-set can replace it.</summary>
    public CssStyleSheet? DeclaredStyleSheet;

    /// <summary>DP values the CSS engine currently owns on this element, by layer.</summary>
    public Dictionary<DependencyProperty, AppliedCssValue>? Applied;
    public CssAnimationRunner? AnimationRunner;

    /// <summary>Whether the engine has applied a computed style for this element.</summary>
    public bool HasAppliedStyle;

    /// <summary>Whether the previous computed style rendered this element.</summary>
    public bool HadRenderedStyle;

    // The animated display specification drives these native presentation mirrors.
    public bool AnimatedDisplayModeMirror;
    public bool AnimatedDisplayInlineMirror;
    public bool AnimatedDisplayVisibilityMirror;

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
    public readonly bool Important;

    public AppliedCssValue(DependencyObject.LayerValueSource layer, object? value, bool important = false)
    {
        Layer = layer;
        Value = value;
        Important = important;
    }
}
