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
    private static bool s_structuralSelectors;

    internal static void InvalidateSelectorDependents(CssNode element)
    {
        if (!IsActive) return;
        CssSelectorDependencies.TreeChanged(element);
        var scope = element;
        if (s_structuralSelectors)
        {
            for (var current = element; current is not null; current = CssMatcher.CssAncestor(current))
                if (current.CssRuntimeState?.ScopedStyleSheets is { } sheets &&
                    sheets.Any(sheet => sheet.Rules.Any(rule => rule.HasStructuralDependencies)))
                    scope = current;
            if (ApplicationStyleSheetsProvider?.Invoke() is { } application &&
                application.Any(sheet => sheet.Rules.Any(rule => rule.HasStructuralDependencies)))
                scope = CssMatcher.Root(element);
        }
        CssEvaluationScheduler.InvalidateSubtree(scope);
    }

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

    internal static CssElementState EnsureState(CssNode element)
        => element.CssRuntimeState ??= new CssElementState();

    internal static void OnInlineStyleChanged(CssNode element, string? text)
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
        CssEvaluationScheduler.InvalidateSubtree(element);
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
        int ScopeRank, int Specificity, long OrderKey, CssRule Rule, bool FromState, int[] LayerKey, int Proximity = int.MaxValue);

    private sealed record CascadeCandidate(CssCompiledDeclaration Declaration, bool FromState,
        int ScopeRank, int Specificity, long Order, int[] Layer, bool Inline, int Proximity = int.MaxValue);

    private readonly record struct MergedDeclaration(CssCompiledDeclaration Declaration, bool FromState);

    private struct ScopeInfo
    {
        public CssStateMask SelfStates;
        public CssStateMask AncestorStates;
        public CssStateMask AnyStates;
        public bool UsesId;
        public bool Structural;
        public CssLayerOrder? Layers;
        public HashSet<string>? AttributeNames;
    }

    /// <summary>Re-evaluates one element: match → cascade-sort → merge → apply diff.</summary>
    internal static void EvaluateElement(CssNode element)
    {
        var state = element.CssRuntimeState;
        state?.ContainerDependent?.Reset();
        state?.FontDependency?.Reset();
        state?.SelectorDependent?.Reset();
        var registrations = CssRegisteredProperties.For(element);
        if (registrations.Count > 0 || state?.RegisteredProperties is { Count: > 0 })
        {
            state ??= EnsureState(element);
            state.RegisteredProperties = registrations;
        }
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
        state = element.CssRuntimeState;

        if ((matched is null || matched.Count == 0) && inline is not { Length: > 0 })
        {
            // Nothing applies; clear whatever CSS previously owned (DP layers AND layout state).
            if (state?.Applied is { Count: > 0 } || state?.AppliedLayout is not null ||
                state?.CustomProperties is { Count: > 0 } || registrations.Count > 0 ||
                CssMatcher.CssAncestor(element)?.CssRuntimeState?.CustomProperties is { Count: > 0 })
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

        var candidates = new Dictionary<string, List<CascadeCandidate>>(StringComparer.Ordinal);
        void AddCandidate(CascadeCandidate candidate)
        {
            if (!candidates.TryGetValue(candidate.Declaration.Name, out var group)) candidates[candidate.Declaration.Name] = group = [];
            group.Add(candidate);
        }

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
                foreach (var declaration in GetCompiledDeclarations(match.Rule))
                    AddCandidate(new CascadeCandidate(declaration, match.FromState, match.ScopeRank,
                        match.Specificity, match.OrderKey, match.LayerKey, false, match.Proximity));
            }
        }

        if (inline is { Length: > 0 })
        {
            // Inline is the highest cascade origin short of !important.
            foreach (var declaration in inline)
                AddCandidate(new CascadeCandidate(declaration, false, int.MaxValue, int.MaxValue, long.MaxValue, [], true));
        }

        var merged = new List<MergedDeclaration>();
        foreach (var group in candidates.Values)
        {
            group.Sort(static (a, b) => CompareCandidates(b, a));
            while (group.Count > 0)
            {
                var winner = group[0];
                if (winner.Declaration.Value is CssRevertValue revert)
                {
                    if (!revert.Layer) break;
                    group.RemoveAll(candidate => candidate.Layer.SequenceEqual(winner.Layer));
                    continue;
                }
                merged.Add(new MergedDeclaration(winner.Declaration, winner.FromState));
                break;
            }
        }

        ApplyMergedDeclarations(element, merged);
        var finalState = EnsureState(element);
        finalState.CascadeVersion = s_cascadeVersion;
        UpdateDependencyTracking(element, finalState, in scopeInfo);
    }


    private static int CompareCandidates(CascadeCandidate a, CascadeCandidate b)
    {
        var comparison = a.Declaration.Important.CompareTo(b.Declaration.Important);
        if (comparison != 0) return comparison;
        comparison = a.Inline.CompareTo(b.Inline);
        if (comparison != 0) return comparison;
        comparison = CssLayerOrder.Compare(a.Layer, b.Layer);
        if (comparison != 0) return a.Declaration.Important ? -comparison : comparison;
        comparison = a.ScopeRank.CompareTo(b.ScopeRank);
        if (comparison != 0) return comparison;
        comparison = a.Specificity.CompareTo(b.Specificity);
        if (comparison != 0) return comparison;
        comparison = b.Proximity.CompareTo(a.Proximity);
        return comparison != 0 ? comparison : a.Order.CompareTo(b.Order);
    }

    private static List<MatchedRule>? CollectMatchedRules(CssNode element, ref ScopeInfo scopeInfo)
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
        List<CssNode>? scopedAncestors = null;
        for (var current = element; current is not null; current = CssMatcher.CssAncestor(current))
        {
            if (current.CssRuntimeState?.ScopedStyleSheets is { Count: > 0 })
            {
                (scopedAncestors ??= new List<CssNode>()).Add(current);
            }
        }

        if (scopedAncestors is not null)
        {
            for (var i = scopedAncestors.Count - 1; i >= 0; i--)
            {
                MatchSheets(element, scopedAncestors[i].CssRuntimeState!.ScopedStyleSheets!, scopeRank++,
                    ref matched, ref scopeInfo, scopedAncestors[i]);
            }
        }

        return matched;
    }

    private static void MatchSheets(
        CssNode element,
        IReadOnlyList<CssStyleSheet> sheets,
        int scopeRank,
        ref List<MatchedRule>? matched,
        ref ScopeInfo scopeInfo, CssNode? scopeRoot = null)
    {
        CssMatchSession? session = null;
        for (var sheetIndex = 0; sheetIndex < sheets.Count; sheetIndex++)
        {
            var sheet = sheets[sheetIndex];
            var layerOrder = scopeInfo.Layers ??= new CssLayerOrder();
            foreach (var layer in sheet.Layers)
                if (layer.Condition is null || layer.Condition.Evaluate(element)) layerOrder.GetKey(layer.Name);
            scopeInfo.AncestorStates |= sheet.AncestorStateUnion;
            scopeInfo.AnyStates |= sheet.AnyStateUnion;
            scopeInfo.UsesId |= sheet.UsesId;
            foreach (var rule in sheet.Rules)
            {
                if (rule.HasStructuralDependencies)
                {
                    scopeInfo.Structural = true;
                    s_structuralSelectors = true;
                    foreach (var selector in rule.DependencySelectors)
                        foreach (var name in selector.AttributeNames()) (scopeInfo.AttributeNames ??= new(StringComparer.Ordinal)).Add(name);
                }
                var bestSpecificity = -1;
                var bestFromState = false;
                var bestProximity = int.MaxValue;
                if (rule.Scope is not null || rule.Selectors.Any(s => s.ContainsNesting)) session ??= new();
                var observer = rule.HasStructuralDependencies || rule.Selectors.Any(s => s.HasAnyState) ? CssSelectorDependencies.For(element) : null;
                var baseContext = new CssMatchContext(scopeRoot, scopeRoot, Session: session, Observer: observer);
                var scopeStates = rule.Scope?.Selectors().Any(s => s.HasAnyState) == true;
                IEnumerable<CssMatchContext> contexts = rule.Scope is null ? [baseContext]
                    : rule.Scope.Bindings(element, scopeRoot, true, session!, observer).Select(binding => baseContext with { Scope = binding.Root, Bindings = binding });
                foreach (var context in contexts)
                {
                    var proximity = rule.Scope is null ? int.MaxValue : CssScopeRule.Distance(element, context.Scope!);
                    foreach (var selector in rule.Selectors)
                    {
                        var canImprove = selector.Specificity > bestSpecificity || selector.Specificity == bestSpecificity && proximity < bestProximity;
                        if (!selector.HasAnyState)
                        {
                            if (canImprove && CssMatcher.Matches(element, selector, evaluateStates: false, context))
                            {
                                bestSpecificity = selector.Specificity;
                                bestFromState = scopeStates; bestProximity = proximity;
                            }
                            continue;
                        }

                        // Track subject states independently of their current truth value.
                        if (!CssMatcher.Matches(element, selector, evaluateStates: false, context)) continue;
                        scopeInfo.SelfStates |= selector.RightmostStates;
                        if (canImprove && CssMatcher.Matches(element, selector, evaluateStates: true, context))
                        {
                            bestSpecificity = selector.Specificity;
                            bestFromState = true; bestProximity = proximity;
                        }
                    }
                }

                if (bestSpecificity >= 0 && (rule.Condition is null || rule.Condition.Evaluate(element)))
                {
                    (matched ??= new List<MatchedRule>()).Add(new MatchedRule(
                        scopeRank, bestSpecificity, ((long)sheetIndex << 32) | (uint)rule.RuleIndex, rule, bestFromState, layerOrder.GetKey(rule.LayerName), bestProximity));
                }
            }
        }
    }

    // ── Dynamic-state dependency tracking ──────────────────────────────────────────────

    private static void UpdateDependencyTracking(
        CssNode element, CssElementState state, in ScopeInfo scopeInfo)
    {
        state.SelfStates = scopeInfo.SelfStates;
        state.ScopeAncestorStates = scopeInfo.AncestorStates;
        state.ScopeUsesId = scopeInfo.UsesId;
        state.StructuralDependencies = scopeInfo.Structural;
        state.NativeAttributeNames = scopeInfo.AttributeNames;
        if (scopeInfo.AttributeNames is { Count: > 0 } attributes)
        {
            state.NativeAttributeValues ??= new(StringComparer.Ordinal);
            foreach (var name in attributes)
                if (CssDependencyPropertyLookup.Find(element.GetType(), name) is { } property)
                    state.NativeAttributeValues[name] = element.HasLocalValue(property) ? element.GetValue(property) : DependencyProperty.UnsetValue;
        }

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
           scopeInfo.UsesId || scopeInfo.Structural;

    private static Action<DependencyProperty, object?, object?> CreateStateHandler(CssNode element)
        => (dp, _, _) => OnTrackedPropertyChanged(element, dp);

    private static void OnTrackedPropertyChanged(CssNode element, DependencyProperty dp)
    {
        var state = element.CssRuntimeState;
        if (state is null)
        {
            return;
        }

        if (state.StructuralDependencies)
        {
            if (MapStateMask(dp) != CssStateMask.None || dp.Name == "Name") InvalidateSelectorDependents(element);
            else if (state.NativeAttributeNames?.Contains(dp.Name) == true)
            {
                var current = element.HasLocalValue(dp) ? element.GetValue(dp) : DependencyProperty.UnsetValue;
                if (state.NativeAttributeValues is null || !state.NativeAttributeValues.TryGetValue(dp.Name, out var previous) || !Equals(current, previous))
                {
                    (state.NativeAttributeValues ??= new(StringComparer.Ordinal))[dp.Name] = current;
                    InvalidateSelectorDependents(element);
                }
            }
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

        var compiled = CompileDeclarations(new List<CssDeclaration>(rule.Declarations), new CssCompileContext { BaseUri = rule.BaseUri });
        rule.ExpandedDeclarations = compiled;
        rule.ExpandedVersion = CssPropertyRegistry.Version;
        return compiled;
    }

    /// <summary>
    /// Compiles syntax-level declarations: registry lookup, shorthand expansion, then a
    /// per-longhand merge (later wins; !important is not displaced by a later normal value).
    /// </summary>
    internal static CssCompiledDeclaration[] CompileDeclarations(List<CssDeclaration> declarations, CssCompileContext? compileContext = null)
    {
        compileContext ??= CssCompileContext.Default;
        if (declarations.Count == 0)
        {
            return Array.Empty<CssCompiledDeclaration>();
        }

        var expanded = new List<CssCompiledDeclaration>(declarations.Count);
        List<CssCompiledDeclaration>? shorthandBuffer = null;
        foreach (var declaration in declarations)
        {
            if (declaration.PropertyName.StartsWith("--", StringComparison.Ordinal))
            {
                var keyword = CssPropertyMetadata.WideKeyword(declaration.RawValue) ?? declaration.RawValue.Trim();
                expanded.Add(new CssCompiledDeclaration(declaration.PropertyName,
                    keyword.Equals("revert", StringComparison.OrdinalIgnoreCase) || keyword.Equals("revert-layer", StringComparison.OrdinalIgnoreCase)
                        ? new CssRevertValue(keyword.Equals("revert-layer", StringComparison.OrdinalIgnoreCase))
                        : new CssCustomPropertyValue(declaration.RawValue, compileContext.BaseUri), declaration.Important));
                continue;
            }
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

            if (CssPropertyMetadata.WideKeyword(declaration.RawValue) is { } wideKeyword)
            {
                foreach (var longhand in CssPropertyMetadata.Longhands(canonicalName))
                    expanded.Add(new CssCompiledDeclaration(longhand,
                        wideKeyword.StartsWith("revert", StringComparison.Ordinal)
                            ? new CssRevertValue(wideKeyword == "revert-layer")
                            : new CssWideValue(longhand, wideKeyword), declaration.Important));
                continue;
            }
            if (CssCustomProperties.ContainsVariable(declaration.RawValue))
            {
                foreach (var longhand in CssPropertyMetadata.Longhands(canonicalName))
                    expanded.Add(new CssCompiledDeclaration(longhand,
                        new CssPendingSubstitution(canonicalName, declaration.RawValue, longhand, compileContext), declaration.Important));
                continue;
            }

            var numericContext = new CssNumericReadContext(compileContext.NumericLengths);
            var reader = new CssTokenReader(declaration.RawValue, numericContext);
            if (descriptor.Kind == CssPropertyKind.Shorthand)
            {
                shorthandBuffer ??= new List<CssCompiledDeclaration>(8);
                shorthandBuffer.Clear();
                if (!descriptor.Expand!(ref reader, compileContext, shorthandBuffer))
                {
                    ReportInvalidValue(declaration);
                    continue;
                }

                foreach (var longhand in shorthandBuffer)
                {
                    expanded.Add(numericContext.RequiresRuntime
                        ? new CssCompiledDeclaration(longhand.Name,
                            new CssNumericDeclarationValue(canonicalName, declaration.RawValue, longhand.Name, compileContext.BaseUri), declaration.Important)
                        : longhand.WithImportant(declaration.Important));
                }
            }
            else
            {
                var value = descriptor.Parse!(ref reader, compileContext);
                if (value is null)
                {
                    ReportInvalidValue(declaration);
                    continue;
                }

                expanded.Add(new CssCompiledDeclaration(canonicalName, numericContext.RequiresRuntime
                    ? new CssNumericDeclarationValue(canonicalName, declaration.RawValue, canonicalName, compileContext.BaseUri) : value, declaration.Important));
            }
        }

        // Per-longhand merge within one declaration block: later wins, !important protected.
        var indexByName = new Dictionary<string, int>(expanded.Count, StringComparer.Ordinal);
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

    private static void ApplyMergedDeclarations(CssNode element, IReadOnlyList<MergedDeclaration> declarations)
    {
        var custom = new Dictionary<string, CssCustomPropertyValue>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
            if (declaration.Declaration.Value is CssCustomPropertyValue property)
                custom[declaration.Declaration.Name] = property;
        var parent = CssMatcher.CssAncestor(element);
        var state = EnsureState(element);
        var previousCustomProperties = state.CustomProperties;
        var lengths = declarations.Count > 0 || state.RegisteredProperties is { Count: > 0 } ? BuildLengthContext(element) : CssLengthContext.Default;
        CssRegisteredComputation? computation = null;
        if (state.RegisteredProperties is { Count: > 0 } registry)
        {
            state.CustomProperties = computation = new(element, parent?.CssRuntimeState?.CustomProperties, custom, registry, lengths,
                declarations.FirstOrDefault(d => d.Declaration.Name == "font-size").Declaration.Value,
                declarations.FirstOrDefault(d => d.Declaration.Name == "color").Declaration.Value,
                declarations.FirstOrDefault(d => d.Declaration.Name == "line-height").Declaration.Value,
                declarations.Where(d => d.Declaration.Name is "font-family" or "font-weight" or "font-style")
                    .ToDictionary(d => d.Declaration.Name, d => d.Declaration.Value));
            state.CustomProperties = computation.Compute();
            state.RegisteredValues = computation.TypedValues;
        }
        else
        {
            state.CustomProperties = CssCustomProperties.Compute(parent?.CssRuntimeState?.CustomProperties,
                custom.ToDictionary(pair => pair.Key, pair => pair.Value.RawValue, StringComparer.Ordinal));
            state.RegisteredValues = null;
        }
        CssContainerQueries.CustomPropertiesChanged(element, previousCustomProperties, state.CustomProperties);
        var collector = new CssSetterCollector();
        if (declarations.Count > 0)
        {
            var slots = new CssSlotAccumulator();

            // Font-family/weight/style establish the rulers for ex/cap/ch/ic.
            // Probe CSS layers without disturbing native local values or bindings.
            var fontContext = lengths.Fonts ?? CssFontContext.Initial;
            foreach (var fontName in new[] { "font-family", "font-weight", "font-style" })
            {
                var merged = declarations.FirstOrDefault(d => d.Declaration.Name == fontName);
                if (merged.Declaration.Value is null) continue;
                var nativeName = fontName == "font-family" ? "FontFamily" : fontName == "font-weight" ? "FontWeight" : "FontStyle";
                if (CssDependencyPropertyLookup.Find(element.GetType(), nativeName) is { } native && element.HasLocalOrAnimatedValue(native)) continue;
                var fontSink = new CssSetterCollector();
                var faceValue = computation?.FontPropertyCycle(fontName) == true ? new CssWideValue(fontName, "unset") : merged.Declaration.Value;
                faceValue.TryApply(new CssApplyContext(element, lengths, slots), fontSink);
                foreach (var entry in fontSink.Values)
                    fontContext = fontContext with { Element = fontContext.Element.With(entry.Key.Name, entry.Value.Value) };
                if (fontContext.IsRoot) fontContext = fontContext with { Root = fontContext.Element };
                lengths = lengths.WithFonts(fontContext);
            }

            // Pre-pass: a font-size declaration in this set establishes the em basis for the
            // other declarations (line-height: 1.5 next to font-size: 16px must use 16).
            foreach (var merged in declarations)
            {
                if (!merged.Declaration.Name.Equals("font-size", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (CssDependencyPropertyLookup.Find(element.GetType(), "FontSize") is { } fontDp &&
                    element.HasLocalOrAnimatedValue(fontDp)) break;

                var probeSink = new CssSetterCollector();
                var probeContext = new CssApplyContext(element, lengths, slots);
                var fontValue = computation?.FontCycle == true ? new CssWideValue("font-size", "unset") : merged.Declaration.Value;
                if (fontValue.TryApply(in probeContext, probeSink))
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

            var lineDeclaration = declarations.FirstOrDefault(d => d.Declaration.Name == "line-height").Declaration.Value;
            if (lineDeclaration is not null && !(CssDependencyPropertyLookup.Find(element.GetType(), "LineHeight") is { } lineProperty &&
                element.HasLocalOrAnimatedValue(lineProperty)))
            {
                var lineSink = new CssSetterCollector();
                var lineValueSource = computation?.LineCycle == true ? new CssWideValue("line-height", "unset") : lineDeclaration;
                lineValueSource.TryApply(new CssApplyContext(element, lengths, slots), lineSink);
                if (lineSink.Values.TryGetValue(CssComputedLineHeightValue.Property, out var line) && line.Value is CssLineHeight lineValue)
                {
                    fontContext = (lengths.Fonts ?? CssFontContext.Initial) with { ElementLine = lineValue };
                    if (fontContext.IsRoot) fontContext = fontContext with { RootLine = lineValue };
                    lengths = lengths.WithFonts(fontContext);
                }
            }

            foreach (var merged in declarations)
            {
                if (merged.Declaration.Name != "color") continue;
                var foreground = CssDependencyPropertyLookup.Find(element.GetType(), "Foreground");
                if (foreground is not null && element.HasLocalOrAnimatedValue(foreground)) break;
                var colorSink = new CssSetterCollector();
                var colorContext = new CssApplyContext(element, lengths, slots);
                var colorValue = computation?.ColorCycle == true ? new CssWideValue("color", "unset") : merged.Declaration.Value;
                colorValue.TryApply(in colorContext, colorSink);
                foreach (var applied in colorSink.Values.Values)
                    if (applied.Value is Jalium.UI.Media.Brush brush) slots.ForegroundBrush = brush;
                break;
            }

            var context = new CssApplyContext(element, lengths, slots);
            foreach (var merged in declarations)
            {
                collector.CurrentValueIsState = merged.FromState;
                slots.CurrentContributionIsState = merged.FromState;
                if (computation?.FontCycle == true && merged.Declaration.Name == "font-size" ||
                    computation?.ColorCycle == true && merged.Declaration.Name == "color" ||
                    computation?.LineCycle == true && merged.Declaration.Name == "line-height" ||
                    computation?.FontPropertyCycle(merged.Declaration.Name) == true)
                    new CssWideValue(merged.Declaration.Name, "unset").TryApply(context, collector);
                else merged.Declaration.Value.TryApply(in context, collector);
            }

            collector.CurrentValueIsState = false;
            slots.CurrentContributionIsState = false;
            slots.Flush(in context, collector);
        }

        ApplyDiff(element, collector.Values, collector.LayoutState);
    }

    internal static CssLengthContext BuildLengthContext(CssNode element, CssNode? dependent = null)
    {
        for (var current = element; current is not null; current = current.FrameworkParent)
        {
            var inheritedState = EnsureState(current);
            if (inheritedState.ObservesTypography) continue;
            inheritedState.ObservesTypography = true;
            var observed = current;
            current.PropertyChangedInternal += (property, _, _) =>
            {
                if (property.Name is "FontSize" or "FontFamily" or "FontWeight" or "FontStyle" or "FontStretch" or "LineHeight" or "CssLineHeight" or "Foreground" or "Visibility")
                    CssEvaluationScheduler.InvalidateSubtree(observed);
            };
        }
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
        var rootState = EnsureState(root);
        rootState.ObservesViewport = true;
        var viewports = element.GetValue(Css.ViewportMetricsProperty) as CssViewportMetrics ?? CssViewportMetrics.Uniform(ViewportSize(root));
        var viewportWidth = viewports.Large.Width;
        var viewportHeight = viewports.Large.Height;
        var inherited = element.FrameworkParent;
        var fonts = new CssFontContext(CssFontInfo.Read(element), inherited is null ? CssFontInfo.Initial : CssFontInfo.Read(inherited),
            CssFontInfo.Read(root), CssComputedLineHeightValue.Inherited(element),
            inherited is null ? default : CssComputedLineHeightValue.Inherited(inherited), CssComputedLineHeightValue.Inherited(root),
            ReferenceEquals(root, element), EnsureState(dependent ?? element).FontDependency ??= new(dependent ?? element),
            Jalium.UI.Interop.TextMeasurement.MetricsCacheEpoch);
        return new CssLengthContext(elementFontSize, inheritedFontSize, rootFontSize, viewportWidth, viewportHeight,
            CssContainerUnitContext.Create(element, dependent ?? element, viewports.Small.Width, viewports.Small.Height), fonts, viewports);
    }

    internal static Size ViewportSize(CssNode root) => root.CssRuntimeState?.ViewportAllocation ?? new Size(
        root.ActualWidth > 0 ? root.ActualWidth : double.IsFinite(root.Width) ? root.Width : 0,
        root.ActualHeight > 0 ? root.ActualHeight : double.IsFinite(root.Height) ? root.Height : 0);

    internal static void RecordViewportAllocation(FrameworkElement root, Size size)
    {
        if (root.FrameworkParent is not null || root.CssRuntimeState is not { ObservesViewport: true } state ||
            !double.IsFinite(size.Width) || !double.IsFinite(size.Height) || state.ViewportAllocation == size) return;
        state.ViewportAllocation = size;
        CssEvaluationScheduler.InvalidateSubtree(root);
    }

    private static double ReadFontSize(CssNode element)
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
        CssNode element,
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
            foreach (var (dp, incoming) in newValues.OrderBy(pair => CssTransitions.IsConfiguration(pair.Key) ? 0 : 1))
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
        {
            try
            {
                if (!property.IsValidType(value) || !property.IsValidValue(value))
                {
                    CssDiagnostics.Report(property.Name, CssDiagnosticReason.InvalidValue, null,
                        "CSS value was rejected by the target property validation");
                    return;
                }
            }
            catch (Exception)
            {
                CssDiagnostics.Report(property.Name, CssDiagnosticReason.InvalidValue, null,
                    "target property validation failed; CSS declaration skipped");
                return;
            }
            Values[property] = new AppliedCssValue(CurrentValueIsState
                ? DependencyObject.LayerValueSource.CssState : DependencyObject.LayerValueSource.CssBase, value);
        }

        public void SetLayoutState(CssLayoutState state) => LayoutState = state;
    }
}
