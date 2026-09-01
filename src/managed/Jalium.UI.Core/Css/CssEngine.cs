using System.Collections.Concurrent;

namespace Jalium.UI.Styling;

/// <summary>
/// The CSS cascade engine: collects the style sheets in scope for an element (application,
/// ancestor, own, plus the inline declaration block), matches selectors, orders the winners
/// (scope &lt; specificity &lt; document order, with !important protection), and applies the
/// merged declarations through the CssBase value layer.
/// </summary>
internal static class CssEngine
{
    private const int InlineCacheCapacity = 1024;

    private static readonly ConcurrentDictionary<string, CssCompiledDeclaration[]> s_inlineCache = new();

    /// <summary>True once any CSS input exists; keeps tree hooks at a single static read otherwise.</summary>
    internal static bool IsActive;

    /// <summary>Application-level sheets, injected by Jalium.UI.Controls (Core cannot see Application).</summary>
    internal static Func<CssStyleSheetCollection?>? ApplicationStyleSheetsProvider;

    /// <summary>Invalidates every open visual root; injected alongside the provider.</summary>
    internal static Action? CascadeRootsInvalidator;

    private static int s_cascadeVersion;

    internal static int CascadeVersion => s_cascadeVersion;

    internal static void MarkActive() => IsActive = true;

    /// <summary>
    /// The public mapping registry changed: compiled declarations (inline cache, per-rule
    /// expansions keyed on the registry version) are stale; refresh everything.
    /// </summary>
    internal static void OnMappingsChanged()
    {
        s_inlineCache.Clear();
        if (IsActive)
        {
            NotifyCascadeChanged();
        }
    }

    /// <summary>Style-sheet collections changed anywhere: bump the version and refresh all roots.</summary>
    internal static void NotifyCascadeChanged()
    {
        Interlocked.Increment(ref s_cascadeVersion);
        CascadeRootsInvalidator?.Invoke();
    }

    internal static CssElementState EnsureState(FrameworkElement element)
        => element.CssRuntimeState ??= new CssElementState();

    internal static void OnInlineStyleChanged(FrameworkElement element, string? text)
    {
        MarkActive();
        var state = EnsureState(element);
        if (string.IsNullOrWhiteSpace(text))
        {
            state.InlineDeclarations = null;
            state.InlineText = null;
        }
        else
        {
            state.InlineDeclarations = GetOrCompileInline(text);
            state.InlineText = text;
            state.InlineVersion = CssPropertyRegistry.Version;
        }

        EvaluateElement(element);
    }

    /// <summary>Compiles an inline declaration block, cached by exact text (list scenarios repeat it).</summary>
    internal static CssCompiledDeclaration[] GetOrCompileInline(string text)
    {
        if (s_inlineCache.TryGetValue(text, out var cached))
        {
            return cached;
        }

        var declarations = CssParser.ParseInlineDeclarations(text, null);
        var compiled = CompileDeclarations(declarations);
        if (s_inlineCache.Count >= InlineCacheCapacity)
        {
            s_inlineCache.Clear();
        }

        s_inlineCache[text] = compiled;
        return compiled;
    }

    // ── Cascade evaluation ─────────────────────────────────────────────────────────────

    private readonly record struct MatchedRule(
        int ScopeRank, int Specificity, int OrderKey, CssRule Rule, bool FromState);

    private readonly record struct MergedDeclaration(CssCompiledDeclaration Declaration, bool FromState);

    private struct ScopeInfo
    {
        public CssStateMask SelfStates;
        public CssStateMask AncestorStates;
        public CssStateMask AnyStates;
        public bool UsesId;
    }

    /// <summary>Re-evaluates one element: match → cascade-sort → merge → apply diff.</summary>
    internal static void EvaluateElement(FrameworkElement element)
    {
        var state = element.CssRuntimeState;
        if (state?.InlineText is { } inlineText && state.InlineVersion != CssPropertyRegistry.Version)
        {
            state.InlineDeclarations = GetOrCompileInline(inlineText);
            state.InlineVersion = CssPropertyRegistry.Version;
        }

        var inline = state?.InlineDeclarations;

        List<MatchedRule>? matched = null;
        var scopeInfo = default(ScopeInfo);
        if (CssMatcher.IsCssMatchable(element))
        {
            matched = CollectMatchedRules(element, ref scopeInfo);
        }

        if ((matched is null || matched.Count == 0) && inline is not { Length: > 0 })
        {
            // Nothing applies; clear whatever CSS previously owned (DP layers AND layout state).
            if (state?.Applied is { Count: > 0 } || state?.AppliedLayout is not null)
            {
                ApplyMergedDeclarations(element, Array.Empty<MergedDeclaration>());
            }

            if (state is not null)
            {
                state.CascadeVersion = s_cascadeVersion;
                UpdateDependencyTracking(element, state, in scopeInfo);
            }
            else if (NeedsTracking(in scopeInfo))
            {
                UpdateDependencyTracking(element, EnsureState(element), in scopeInfo);
            }

            return;
        }

        var merged = new List<MergedDeclaration>();
        var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (matched is { Count: > 0 })
        {
            // Ascending order: later entries overwrite earlier ones during the merge.
            matched.Sort(static (a, b) =>
            {
                var byScope = a.ScopeRank.CompareTo(b.ScopeRank);
                if (byScope != 0)
                {
                    return byScope;
                }

                var bySpecificity = a.Specificity.CompareTo(b.Specificity);
                return bySpecificity != 0 ? bySpecificity : a.OrderKey.CompareTo(b.OrderKey);
            });

            foreach (var match in matched)
            {
                MergeDeclarations(merged, indexByName, GetCompiledDeclarations(match.Rule), match.FromState);
            }
        }

        if (inline is { Length: > 0 })
        {
            // Inline is the highest cascade origin short of !important.
            MergeDeclarations(merged, indexByName, inline, fromState: false);
        }

        ApplyMergedDeclarations(element, merged);
        var finalState = EnsureState(element);
        finalState.CascadeVersion = s_cascadeVersion;
        UpdateDependencyTracking(element, finalState, in scopeInfo);
    }

    private static void MergeDeclarations(
        List<MergedDeclaration> merged,
        Dictionary<string, int> indexByName,
        IReadOnlyList<CssCompiledDeclaration> declarations,
        bool fromState)
    {
        foreach (var declaration in declarations)
        {
            if (indexByName.TryGetValue(declaration.Name, out var existing))
            {
                if (merged[existing].Declaration.Important && !declaration.Important)
                {
                    continue;
                }

                merged[existing] = new MergedDeclaration(declaration, fromState);
            }
            else
            {
                indexByName[declaration.Name] = merged.Count;
                merged.Add(new MergedDeclaration(declaration, fromState));
            }
        }
    }

    private static List<MatchedRule>? CollectMatchedRules(FrameworkElement element, ref ScopeInfo scopeInfo)
    {
        List<MatchedRule>? matched = null;
        var scopeRank = 0;

        var applicationSheets = ApplicationStyleSheetsProvider?.Invoke();
        if (applicationSheets is { Count: > 0 })
        {
            MatchSheets(element, applicationSheets, scopeRank, ref matched, ref scopeInfo);
        }

        scopeRank++;

        // Ancestor-scoped sheets: farthest first so nearer scopes rank higher.
        List<FrameworkElement>? scopedAncestors = null;
        for (var current = element; current is not null; current = CssMatcher.CssAncestor(current))
        {
            if (current.CssRuntimeState?.ScopedStyleSheets is { Count: > 0 })
            {
                (scopedAncestors ??= new List<FrameworkElement>()).Add(current);
            }
        }

        if (scopedAncestors is not null)
        {
            for (var i = scopedAncestors.Count - 1; i >= 0; i--)
            {
                MatchSheets(element, scopedAncestors[i].CssRuntimeState!.ScopedStyleSheets!, scopeRank++,
                    ref matched, ref scopeInfo);
            }
        }

        return matched;
    }

    private static void MatchSheets(
        FrameworkElement element,
        IReadOnlyList<CssStyleSheet> sheets,
        int scopeRank,
        ref List<MatchedRule>? matched,
        ref ScopeInfo scopeInfo)
    {
        for (var sheetIndex = 0; sheetIndex < sheets.Count; sheetIndex++)
        {
            var sheet = sheets[sheetIndex];
            scopeInfo.AncestorStates |= sheet.AncestorStateUnion;
            scopeInfo.AnyStates |= sheet.AnyStateUnion;
            scopeInfo.UsesId |= sheet.UsesId;
            foreach (var rule in sheet.Rules)
            {
                var bestSpecificity = -1;
                var bestFromState = false;
                foreach (var selector in rule.Selectors)
                {
                    if (!selector.HasAnyState)
                    {
                        if (selector.Specificity > bestSpecificity &&
                            CssMatcher.Matches(element, selector, evaluateStates: false))
                        {
                            bestSpecificity = selector.Specificity;
                            bestFromState = false;
                        }

                        continue;
                    }

                    // A structural match registers the dependency whether or not the state is
                    // currently active — the element must re-evaluate when it flips.
                    if (!CssMatcher.Matches(element, selector, evaluateStates: false))
                    {
                        continue;
                    }

                    scopeInfo.SelfStates |= selector.RightmostStates;
                    if (selector.Specificity > bestSpecificity &&
                        CssMatcher.Matches(element, selector, evaluateStates: true))
                    {
                        bestSpecificity = selector.Specificity;
                        bestFromState = true;
                    }
                }

                if (bestSpecificity >= 0)
                {
                    (matched ??= new List<MatchedRule>()).Add(new MatchedRule(
                        scopeRank, bestSpecificity, (sheetIndex << 20) | rule.RuleIndex, rule, bestFromState));
                }
            }
        }
    }

    // ── Dynamic-state dependency tracking ──────────────────────────────────────────────

    private static void UpdateDependencyTracking(
        FrameworkElement element, CssElementState state, in ScopeInfo scopeInfo)
    {
        state.SelfStates = scopeInfo.SelfStates;
        state.ScopeAncestorStates = scopeInfo.AncestorStates;
        state.ScopeUsesId = scopeInfo.UsesId;

        var needed = NeedsTracking(in scopeInfo);
        if (needed)
        {
            if (state.Handler is null)
            {
                var handler = CreateStateHandler(element);
                state.Handler = handler;
                element.PropertyChangedInternal += handler;
            }
        }
        else if (state.Handler is not null)
        {
            element.PropertyChangedInternal -= state.Handler;
            state.Handler = null;
        }
    }

    /// <summary>
    /// Whether the element needs a PropertyChangedInternal subscription. Effective IsEnabled
    /// reaches descendants without their own DP notifications, so a subject-position
    /// :disabled/:enabled anywhere in scope requires the subscription on every element
    /// (the ancestor whose IsEnabled flips then refreshes its subtree).
    /// </summary>
    private static bool NeedsTracking(in ScopeInfo scopeInfo)
        => scopeInfo.SelfStates != CssStateMask.None ||
           scopeInfo.AncestorStates != CssStateMask.None ||
           (scopeInfo.AnyStates & CssStateMask.Enabled) != 0 ||
           scopeInfo.UsesId;

    private static Action<DependencyProperty, object?, object?> CreateStateHandler(FrameworkElement element)
        => (dp, _, _) => OnTrackedPropertyChanged(element, dp);

    private static void OnTrackedPropertyChanged(FrameworkElement element, DependencyProperty dp)
    {
        var state = element.CssRuntimeState;
        if (state is null)
        {
            return;
        }

        if (ReferenceEquals(dp, FrameworkElement.NameProperty))
        {
            if (state.ScopeUsesId)
            {
                CssEvaluationScheduler.InvalidateSubtree(element);
            }

            return;
        }

        var mask = MapStateMask(dp);
        if (mask == CssStateMask.None)
        {
            return;
        }

        // Effective IsEnabled propagates down the tree without per-descendant DP change
        // notifications, so :disabled/:enabled anywhere requires a subtree refresh.
        if (ReferenceEquals(dp, UIElement.IsEnabledProperty))
        {
            CssEvaluationScheduler.InvalidateSubtree(element);
            return;
        }

        var affectsSubtree = (state.ScopeAncestorStates & mask) != 0;
        var affectsSelf = (state.SelfStates & mask) != 0;
        if (affectsSubtree)
        {
            CssEvaluationScheduler.InvalidateSubtree(element);
        }
        else if (affectsSelf)
        {
            CssEvaluationScheduler.InvalidateElement(element);
        }
    }

    private static CssStateMask MapStateMask(DependencyProperty dp)
    {
        if (ReferenceEquals(dp, UIElement.IsMouseOverProperty))
        {
            return CssStateMask.Hover;
        }

        if (ReferenceEquals(dp, UIElement.IsPressedProperty))
        {
            return CssStateMask.Active;
        }

        if (ReferenceEquals(dp, UIElement.IsFocusedProperty))
        {
            return CssStateMask.Focus;
        }

        if (ReferenceEquals(dp, UIElement.IsKeyboardFocusedProperty))
        {
            return CssStateMask.KeyboardFocus;
        }

        if (ReferenceEquals(dp, UIElement.IsKeyboardFocusWithinProperty))
        {
            return CssStateMask.FocusWithin;
        }

        if (ReferenceEquals(dp, UIElement.IsEnabledProperty))
        {
            return CssStateMask.Enabled;
        }

        if (dp.Name == "IsChecked")
        {
            return CssStateMask.Checked;
        }

        return CssStateMask.None;
    }

    private static CssCompiledDeclaration[] GetCompiledDeclarations(CssRule rule)
    {
        if (rule.ExpandedDeclarations is CssCompiledDeclaration[] cached &&
            rule.ExpandedVersion == CssPropertyRegistry.Version)
        {
            return cached;
        }

        var compiled = CompileDeclarations(new List<CssDeclaration>(rule.Declarations));
        rule.ExpandedDeclarations = compiled;
        rule.ExpandedVersion = CssPropertyRegistry.Version;
        return compiled;
    }

    /// <summary>
    /// Compiles syntax-level declarations: registry lookup, shorthand expansion, then a
    /// per-longhand merge (later wins; !important is not displaced by a later normal value).
    /// </summary>
    internal static CssCompiledDeclaration[] CompileDeclarations(List<CssDeclaration> declarations)
    {
        if (declarations.Count == 0)
        {
            return Array.Empty<CssCompiledDeclaration>();
        }

        var expanded = new List<CssCompiledDeclaration>(declarations.Count);
        List<CssCompiledDeclaration>? shorthandBuffer = null;
        foreach (var declaration in declarations)
        {
            var descriptor = CssPropertyRegistry.LookupForCompile(declaration.PropertyName, out var canonicalName);
            if (descriptor is null)
            {
                CssDiagnostics.Report(
                    declaration.PropertyName, CssDiagnosticReason.UnknownProperty, null,
                    "no CSS mapping and no kebab-case dependency-property form; declaration skipped");
                continue;
            }

            if (descriptor.Kind == CssPropertyKind.Unsupported)
            {
                CssDiagnostics.Report(
                    declaration.PropertyName, CssDiagnosticReason.UnsupportedProperty, null,
                    descriptor.UnsupportedReason ?? "not supported");
                continue;
            }

            var reader = new CssTokenReader(declaration.RawValue);
            if (descriptor.Kind == CssPropertyKind.Shorthand)
            {
                shorthandBuffer ??= new List<CssCompiledDeclaration>(8);
                shorthandBuffer.Clear();
                if (!descriptor.Expand!(ref reader, CssCompileContext.Default, shorthandBuffer))
                {
                    ReportInvalidValue(declaration);
                    continue;
                }

                foreach (var longhand in shorthandBuffer)
                {
                    expanded.Add(longhand.WithImportant(declaration.Important));
                }
            }
            else
            {
                var value = descriptor.Parse!(ref reader, CssCompileContext.Default);
                if (value is null)
                {
                    ReportInvalidValue(declaration);
                    continue;
                }

                expanded.Add(new CssCompiledDeclaration(canonicalName, value, declaration.Important));
            }
        }

        // Per-longhand merge within one declaration block: later wins, !important protected.
        var indexByName = new Dictionary<string, int>(expanded.Count, StringComparer.OrdinalIgnoreCase);
        var merged = new List<CssCompiledDeclaration>(expanded.Count);
        foreach (var declaration in expanded)
        {
            if (indexByName.TryGetValue(declaration.Name, out var existing))
            {
                if (merged[existing].Important && !declaration.Important)
                {
                    continue;
                }

                merged[existing] = declaration;
            }
            else
            {
                indexByName[declaration.Name] = merged.Count;
                merged.Add(declaration);
            }
        }

        return merged.ToArray();
    }

    private static void ReportInvalidValue(CssDeclaration declaration)
    {
        var preview = declaration.RawValue.Length <= 64
            ? declaration.RawValue
            : declaration.RawValue[..64] + "…";
        CssDiagnostics.Report(
            declaration.PropertyName, CssDiagnosticReason.InvalidValue, null,
            $"value '{preview}' could not be parsed; declaration dropped");
    }

    // ── Application of merged declarations ─────────────────────────────────────────────

    private static void ApplyMergedDeclarations(FrameworkElement element, IReadOnlyList<MergedDeclaration> declarations)
    {
        var collector = new CssSetterCollector();
        if (declarations.Count > 0)
        {
            var slots = new CssSlotAccumulator();
            var lengths = BuildLengthContext(element);

            // Pre-pass: a font-size declaration in this set establishes the em basis for the
            // other declarations (line-height: 1.5 next to font-size: 16px must use 16).
            foreach (var merged in declarations)
            {
                if (!merged.Declaration.Name.Equals("font-size", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var probeSink = new CssSetterCollector();
                var probeContext = new CssApplyContext(element, lengths, slots);
                if (merged.Declaration.Value.TryApply(in probeContext, probeSink))
                {
                    foreach (var applied in probeSink.Values.Values)
                    {
                        if (applied.Value is double newSize && newSize > 0)
                        {
                            lengths = lengths.WithElementFontSize(newSize);
                        }
                    }
                }

                break;
            }

            var context = new CssApplyContext(element, lengths, slots);
            foreach (var merged in declarations)
            {
                collector.CurrentValueIsState = merged.FromState;
                slots.CurrentContributionIsState = merged.FromState;
                merged.Declaration.Value.TryApply(in context, collector);
            }

            collector.CurrentValueIsState = false;
            slots.CurrentContributionIsState = false;
            slots.Flush(in context, collector);
        }

        ApplyDiff(element, collector.Values, collector.LayoutState);
    }

    internal static CssLengthContext BuildLengthContext(FrameworkElement element)
    {
        var elementFontSize = ReadFontSize(element);
        var inheritedFontSize = element.FrameworkParent is { } parent
            ? ReadFontSize(parent)
            : CssLengthContext.DefaultFontSize;

        var root = element;
        while (root.FrameworkParent is { } ancestor)
        {
            root = ancestor;
        }

        var rootFontSize = ReferenceEquals(root, element) ? elementFontSize : ReadFontSize(root);
        return new CssLengthContext(elementFontSize, inheritedFontSize, rootFontSize, 0, 0);
    }

    private static double ReadFontSize(FrameworkElement element)
    {
        var dp = CssDependencyPropertyLookup.Find(element.GetType(), "FontSize");
        if (dp is not null && dp.PropertyType == typeof(double) &&
            element.GetValue(dp) is double size && size > 0 && !double.IsNaN(size))
        {
            return size;
        }

        return CssLengthContext.DefaultFontSize;
    }

    /// <summary>
    /// Diffs the freshly computed value set against what CSS currently owns on the element:
    /// stale properties are cleared (the layer removal lets the store fall back), new or
    /// changed values are written, identical values are skipped entirely.
    /// </summary>
    private static void ApplyDiff(
        FrameworkElement element,
        Dictionary<DependencyProperty, AppliedCssValue> newValues,
        CssLayoutState? newLayout)
    {
        var state = element.CssRuntimeState;

        // Layout-state snapshot diff: whole-object equality; any change re-enters layout.
        var oldLayout = state?.AppliedLayout;
        if (!Equals(oldLayout, newLayout))
        {
            element.CssLayout = newLayout;
            EnsureState(element).AppliedLayout = newLayout;
            state = element.CssRuntimeState;
            element.InvalidateMeasure();
        }

        var oldApplied = state?.Applied;
        var hasNew = newValues.Count > 0;
        if (!hasNew && oldApplied is not { Count: > 0 })
        {
            return;
        }

        if (oldApplied is { Count: > 0 })
        {
            foreach (var (dp, applied) in oldApplied)
            {
                if (!newValues.ContainsKey(dp))
                {
                    element.ClearLayerValue(dp, applied.Layer, allowAutoTransition: true);
                }
            }
        }

        Dictionary<DependencyProperty, AppliedCssValue>? newApplied = null;
        if (hasNew)
        {
            newApplied = new Dictionary<DependencyProperty, AppliedCssValue>(newValues.Count);
            foreach (var (dp, incoming) in newValues)
            {
                if (oldApplied is not null && oldApplied.TryGetValue(dp, out var previous))
                {
                    if (previous.Layer == incoming.Layer && Equals(previous.Value, incoming.Value))
                    {
                        newApplied[dp] = previous;
                        continue;
                    }

                    if (previous.Layer != incoming.Layer)
                    {
                        // Layer migration (Base ↔ State): remove the old layer first so the
                        // stale value cannot shadow or linger under the new one.
                        element.ClearLayerValue(dp, previous.Layer, allowAutoTransition: true);
                    }
                }

                element.SetLayerValue(dp, incoming.Value, incoming.Layer, allowAutoTransition: true);
                newApplied[dp] = incoming;
            }
        }

        EnsureState(element).Applied = newApplied;
    }

    private sealed class CssSetterCollector : ICssSetterSink
    {
        public readonly Dictionary<DependencyProperty, AppliedCssValue> Values = new();

        public CssLayoutState? LayoutState;

        public bool CurrentValueIsState { get; set; }

        public void Set(DependencyProperty property, object? value)
            => Values[property] = new AppliedCssValue(
                CurrentValueIsState
                    ? DependencyObject.LayerValueSource.CssState
                    : DependencyObject.LayerValueSource.CssBase,
                value);

        public void SetLayoutState(CssLayoutState state) => LayoutState = state;
    }
}
