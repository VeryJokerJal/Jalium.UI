using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Documents;

namespace Jalium.UI.Markup;

/// <summary>
/// Runtime entry point for JALXAML hot reload patching.
/// </summary>
public static class HotReloadRuntime
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, List<WeakReference<FrameworkElement>>> ComponentsByClass = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<DependencyProperty>> DependencyPropertyCache = new();

    // Per-instance record of the DPs the last patch declared on each live component. ResetDeletedProperties
    // uses it to revert ONLY our previously-set DPs when a later patch drops them — never code-behind /
    // runtime-set values. ConditionalWeakTable keys weakly, so it never roots a component past its lifetime.
    private static readonly ConditionalWeakTable<DependencyObject, HashSet<DependencyProperty>> PatchBaseline = new();

    public static void RegisterComponent(object component)
    {
        // Lazily start the IDE hot-reload pipe agent on the first component registration —
        // no-op unless the JALIUM_HOTRELOAD_PIPE environment variable was injected by the IDE.
        HotReloadAgent.EnsureStarted();

        if (component is not FrameworkElement element)
        {
            return;
        }

        var xClass = element.GetType().FullName;
        if (string.IsNullOrWhiteSpace(xClass))
        {
            return;
        }

        lock (SyncRoot)
        {
            if (!ComponentsByClass.TryGetValue(xClass, out var entries))
            {
                entries = [];
                ComponentsByClass[xClass] = entries;
            }

            CleanupDeadEntries(entries);

            if (entries.Any(wr => wr.TryGetTarget(out var current) && ReferenceEquals(current, element)))
            {
                return;
            }

            entries.Add(new WeakReference<FrameworkElement>(element));
        }
    }

    /// <summary>
    /// Applies a JALXAML patch to all active instances of the specified x:Class.
    /// </summary>
    /// <remarks>
    /// Each active instance is patched against its OWN freshly-parsed source tree. Re-parsing
    /// per instance (instead of broadcasting one shared object graph) is what keeps multi-instance
    /// reload correct: grafting a source element into instance #2 would otherwise reparent/steal it
    /// out of instance #1 (Jalium collections auto-detach on Add), corrupting all-but-last instance.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Hot-reload reflectively mirrors DPs and CLR properties between the patched and current trees.")]
    public static HotReloadPatchResult ApplyPatch(string xClass, string filePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xClass);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        // Parse once up-front purely to validate the payload (fail fast on a malformed patch / wrong
        // root type before touching any live instance). NOT reused for patching — each instance
        // re-parses below so element grafts stay per-instance and inline event handlers bind to each
        // instance's own code-behind.
        object firstParsed;
        try
        {
            firstParsed = XamlReader.Parse(content);
        }
        catch (Exception ex)
        {
            return new HotReloadPatchResult(0, 0, 1, $"Failed to parse JALXAML patch: {ex.Message}");
        }

        if (firstParsed is not FrameworkElement)
        {
            return new HotReloadPatchResult(0, 0, 1, "JALXAML patch root is not a FrameworkElement.");
        }

        List<FrameworkElement> activeInstances;
        lock (SyncRoot)
        {
            if (!ComponentsByClass.TryGetValue(xClass, out var entries))
            {
                return new HotReloadPatchResult(0, 0, 0, "No active instances for target x:Class.");
            }

            CleanupDeadEntries(entries);
            activeInstances = entries
                .Select(static wr => wr.TryGetTarget(out var target) ? target : null)
                .Where(static target => target != null)
                .Cast<FrameworkElement>()
                .ToList();
        }

        if (activeInstances.Count == 0)
        {
            return new HotReloadPatchResult(0, 0, 0, "No active instances for target x:Class.");
        }

        var updated = 0;
        var fallback = 0;
        var failed = 0;
        var patchedRoots = new List<FrameworkElement>(activeInstances.Count);
        var failureMessages = new List<string>();

        for (var i = 0; i < activeInstances.Count; i++)
        {
            var targetRoot = activeInstances[i];

            // Each instance gets its OWN fresh source tree: element grafts never move objects between
            // live instances, and inline event handlers (Click=…) are wired to THIS instance's
            // code-behind via ParseForHotReload, so grafted new elements stay interactive (gap B4).
            FrameworkElement incomingRoot;
            try
            {
                incomingRoot = (FrameworkElement)XamlReader.ParseForHotReload(content, targetRoot);
            }
            catch (Exception ex)
            {
                failed++;
                failureMessages.Add($"ParseForHotReload failed for '{targetRoot.GetType().FullName}': {ex.Message}");
                continue;
            }

            try
            {
                var counters = new PatchCounters();
                ApplyElementPatch(targetRoot, incomingRoot, counters);
                updated += counters.UpdatedElements;
                fallback += counters.FallbackReplacements;
                // Surface elements skipped as type-incompatible — otherwise an unpatched element
                // is invisible to the IDE's success gate and the stale edit is reported as success.
                failed += counters.FailedElements;
                patchedRoots.Add(targetRoot);
            }
            catch (Exception ex)
            {
                failed++;
                failureMessages.Add($"Patch failed for '{targetRoot.GetType().FullName}': {ex.Message}");
            }
        }

        // A successful in-place patch mutates DP/CLR values and grafts visual children
        // directly, but — unlike a normal property set carrying AffectsMeasure/Render
        // metadata, or a window resize — it neither marks the tree dirty nor requests a
        // frame. Without this the page stays visually stale (typically blank) until some
        // unrelated event (e.g. a manual resize) forces a relayout + full present. Force
        // that relayout + full repaint here for every root we patched.
        foreach (var root in patchedRoots)
        {
            InvalidatePatchedRoot(root);
        }

        var message = failed > 0
            ? $"{failed} element(s)/instance(s) could not be patched in place. {string.Join(" | ", failureMessages)}".TrimEnd()
            : string.Empty;
        return new HotReloadPatchResult(updated, fallback, failed, message);
    }

    /// <summary>
    /// Forces a relayout + full repaint of a freshly hot-reloaded root. This mirrors what
    /// Window.OnSizeChanged does — which is exactly why a manual window resize "fixes" an
    /// otherwise-stale hot reload: re-measure + re-record render, then push the next frame
    /// down the FULL replay path so a FLIP_SEQUENTIAL partial present cannot leave
    /// stale/blank pixels in the alternate back buffer.
    /// </summary>
    /// <remarks>
    /// Deliberately uses InvalidateVisual() (which flips the render-dirty flag so
    /// retained-mode visual caches re-record OnRender), NOT InvalidateComposition — the
    /// latter only marks the composition subtree dirty and would replay the stale command
    /// list, leaving patched content blank. Runs on the UI thread (ApplyPatch is already
    /// marshalled there by HotReloadAgent).
    /// </remarks>
    private static void InvalidatePatchedRoot(FrameworkElement root)
    {
        root.InvalidateMeasure();
        root.InvalidateVisual();

        var host = FindWindowHost(root);
        host?.RequestFullInvalidation();
        host?.InvalidateWindow();
    }

    /// <summary>Walks the visual ancestry from <paramref name="visual"/> up to its hosting IWindowHost, if any.</summary>
    private static IWindowHost? FindWindowHost(Visual? visual)
    {
        for (var current = visual; current != null; current = current.VisualParent)
        {
            if (current is IWindowHost host)
            {
                return host;
            }
        }

        return null;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Hot-reload mirrors DPs and CLR properties via reflection.")]
    private static void ApplyElementPatch(FrameworkElement target, FrameworkElement source, PatchCounters counters)
    {
        if (!AreTypesCompatible(target.GetType(), source.GetType()))
        {
            counters.FailedElements++;
            return;
        }

        CopyDependencyProperties(target, source);
        CopyClrProperties(target, source);
        counters.UpdatedElements++;

        if (target is Panel targetPanel && source is Panel sourcePanel)
        {
            PatchPanelChildren(targetPanel, sourcePanel, counters);
            return;
        }

        if (target is ContentControl targetContent && source is ContentControl sourceContent)
        {
            PatchContentControl(targetContent, sourceContent, counters);
            return;
        }

        if (target is ItemsControl targetItems && source is ItemsControl sourceItems)
        {
            PatchItemsControl(targetItems, sourceItems, counters);
            return;
        }

        if (target is Border targetBorder && source is Border sourceBorder)
        {
            PatchSingleChildContainer(
                targetBorder,
                sourceBorder,
                static border => border.Child,
                static (border, child) => border.Child = child,
                counters);
            return;
        }

        if (target is ScrollViewer targetScroll && source is ScrollViewer sourceScroll)
        {
            // ScrollViewer : Control (NOT ContentControl) — its Content is a CLR UIElement?
            // property, invisible to both copy paths, so without this branch the entire scrolled
            // subtree (the common page-root wrapper) never updates on hot reload.
            PatchSingleChildContainer(
                targetScroll,
                sourceScroll,
                static scroll => scroll.Content,
                static (scroll, child) => scroll.Content = child,
                counters);
            return;
        }

        if (target is Decorator targetDecorator && source is Decorator sourceDecorator)
        {
            // Decorator base family (AdornerDecorator / InkPresenter / PopupRoot / PopupWindow):
            // Child is a UIElement DP, skipped by CopyDependencyProperties' UIElement guard.
            PatchSingleChildContainer(
                targetDecorator,
                sourceDecorator,
                static decorator => decorator.Child,
                static (decorator, child) => decorator.Child = child,
                counters);
            return;
        }

        if (target is Popup targetPopup && source is Popup sourcePopup)
        {
            PatchSingleChildContainer(
                targetPopup,
                sourcePopup,
                static popup => popup.Child,
                static (popup, child) => popup.Child = child,
                counters);
            return;
        }

        if (target is Viewbox targetViewbox && source is Viewbox sourceViewbox)
        {
            PatchSingleChildContainer(
                targetViewbox,
                sourceViewbox,
                static viewbox => viewbox.Child,
                static (viewbox, child) => viewbox.Child = child,
                counters);
        }
    }

    /// <summary>
    /// Patches a Grid RowDefinition / ColumnDefinition list. Same count → mirror each definition's DPs
    /// in place (Height/Width/Min/Max are all DPs); count change → rebuild from the source definitions
    /// (they are layout structure, not visual children, so grafting the per-instance source objects is
    /// safe). Without this, editing a track count or a Height="*" is silently dropped on hot reload.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Mirrors DefinitionBase DependencyProperties via reflection.")]
    private static void PatchDefinitionList<T>(IList<T> targetList, IList<T> sourceList, PatchCounters counters)
        where T : DependencyObject
    {
        if (targetList.Count == sourceList.Count)
        {
            for (var i = 0; i < targetList.Count; i++)
            {
                CopyDependencyProperties(targetList[i], sourceList[i]);
                counters.UpdatedElements++;
            }

            return;
        }

        targetList.Clear();
        foreach (var definition in sourceList)
        {
            targetList.Add(definition);
        }

        counters.FallbackReplacements++;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Recurses into ApplyElementPatch which mirrors DPs/CLR properties via reflection.")]
    private static void PatchPanelChildren(Panel targetPanel, Panel sourcePanel, PatchCounters counters)
    {
        // Grid's RowDefinitions/ColumnDefinitions are declarative layout structure (IEnumerable CLR
        // properties skipped by the copy paths). Patch them before the children so a row/column edit
        // (added track, changed Height="*") takes effect in the same reload.
        if (targetPanel is Grid targetGrid && sourcePanel is Grid sourceGrid)
        {
            PatchDefinitionList(targetGrid.RowDefinitions, sourceGrid.RowDefinitions, counters);
            PatchDefinitionList(targetGrid.ColumnDefinitions, sourceGrid.ColumnDefinitions, counters);
        }

        var existingChildren = targetPanel.Children.ToList();
        var sourceChildren = sourcePanel.Children.ToList();
        var used = new HashSet<UIElement>();
        var merged = new List<UIElement>(sourceChildren.Count);
        var grafted = new HashSet<UIElement>();

        for (var i = 0; i < sourceChildren.Count; i++)
        {
            var sourceChild = sourceChildren[i];
            var matched = FindMatchingChild(existingChildren, used, sourceChild, i);

            if (matched == null)
            {
                merged.Add(sourceChild);
                grafted.Add(sourceChild);
                counters.FallbackReplacements++;
                continue;
            }

            used.Add(matched);
            if (matched is FrameworkElement targetChildFe && sourceChild is FrameworkElement sourceChildFe)
            {
                ApplyElementPatch(targetChildFe, sourceChildFe, counters);
                merged.Add(matched);
            }
            else
            {
                merged.Add(sourceChild);
                grafted.Add(sourceChild);
                counters.FallbackReplacements++;
            }
        }

        // Only rebuild the visual children when the set/order actually changed. Re-adding the same
        // children in the same order would still fire detach/attach side effects (DynamicResource
        // re-resolve, focus loss, per-element interaction reset) on children that did not change.
        if (merged.Count == existingChildren.Count
            && merged.Where((child, idx) => !ReferenceEquals(child, existingChildren[idx])).Count() == 0)
        {
            return;
        }

        // A visual detach alone is insufficient: sourcePanel.Children also owns the logical-parent
        // relationship, and adding such an element to targetPanel would throw "logical child already
        // has a parent". Remove only the source elements selected for grafting from their disposable
        // source collection before rebuilding the live target collection.
        foreach (var child in grafted)
        {
            sourcePanel.Children.Remove(child);
            ReleaseSourceElementForGraft(child);
        }

        targetPanel.Children.Clear();
        foreach (var child in merged)
        {
            targetPanel.Children.Add(child);
        }
    }

    private static UIElement? FindMatchingChild(
        List<UIElement> existingChildren,
        HashSet<UIElement> used,
        UIElement sourceChild,
        int indexHint)
    {
        if (sourceChild is FrameworkElement sourceFe && !string.IsNullOrWhiteSpace(sourceFe.Name))
        {
            var named = existingChildren.FirstOrDefault(candidate =>
                !used.Contains(candidate)
                && candidate is FrameworkElement candidateFe
                && AreTypesCompatible(candidate.GetType(), sourceChild.GetType())
                && string.Equals(candidateFe.Name, sourceFe.Name, StringComparison.Ordinal));

            if (named != null)
            {
                return named;
            }
        }

        // Positional fallback for unnamed children. NOTE: reordering unnamed same-type siblings
        // pairs by slot, so an element can receive a sibling's properties — name children (x:Name)
        // to make in-place reload identity-stable.
        if (indexHint >= 0 && indexHint < existingChildren.Count)
        {
            var indexed = existingChildren[indexHint];
            if (!used.Contains(indexed) && AreTypesCompatible(indexed.GetType(), sourceChild.GetType()))
            {
                return indexed;
            }
        }

        return null;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Recurses into ApplyElementPatch which mirrors DPs/CLR properties via reflection.")]
    private static void PatchItemsControl(ItemsControl target, ItemsControl source, PatchCounters counters)
    {
        // Data-bound lists are owned by the binding (the ItemsSource DP itself is transferred by
        // CopyDependencyProperties); only merge inline-declared Items. A HeaderedItemsControl's
        // Header is patched regardless (handled below, after the items).
        if (target.ItemsSource == null && source.ItemsSource == null)
        {
            PatchObjectCollection(target.Items, source.Items, counters);
        }

        PatchHeaderIfPresent(target, source, counters);
    }

    /// <summary>
    /// Merges a source content-item collection into the target in place: items are matched (by x:Name,
    /// then position) and patched recursively; the collection is only rebuilt when the set/order
    /// actually changed (rebuilding would lose selection / scroll / focus). Works on any
    /// <see cref="IList{Object}"/> content collection — ItemsControl.Items (ItemCollection) and
    /// NavigationView.MenuItems (ObservableCollection&lt;object&gt;) alike; neither's Add/Clear performs
    /// AddVisualChild, so grafting per-instance source items across live instances is safe.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Recurses into ApplyElementPatch which mirrors DPs/CLR properties via reflection.")]
    private static void PatchObjectCollection(IList<object> targetList, IList<object> sourceList, PatchCounters counters)
    {
        var existingItems = new List<object>(targetList);
        var sourceItems = new List<object>(sourceList);

        var used = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var merged = new List<object>(sourceItems.Count);
        var grafted = new HashSet<object>(ReferenceEqualityComparer.Instance);

        for (var i = 0; i < sourceItems.Count; i++)
        {
            var sourceItem = sourceItems[i];
            var matched = FindMatchingItem(existingItems, used, sourceItem, i);

            if (matched == null)
            {
                merged.Add(sourceItem);
                grafted.Add(sourceItem);
                counters.FallbackReplacements++;
                continue;
            }

            used.Add(matched);
            if (matched is FrameworkElement targetItemFe && sourceItem is FrameworkElement sourceItemFe)
            {
                ApplyElementPatch(targetItemFe, sourceItemFe, counters);
                merged.Add(matched);
            }
            else
            {
                // Non-element item (data object / string): no in-place patch possible — take source.
                merged.Add(sourceItem);
                grafted.Add(sourceItem);
                counters.FallbackReplacements++;
            }
        }

        // No-op when the set/order is reference-identical — avoids a Reset that would tear down and
        // regenerate every item container (losing selection / scroll position / focus).
        if (merged.Count == existingItems.Count
            && merged.Where((item, idx) => !ReferenceEquals(item, existingItems[idx])).Count() == 0)
        {
            return;
        }

        // Inline UIElement items acquire the source ItemsControl as their logical parent during
        // parsing. Release the disposable source collection's ownership before grafting them into
        // the live target; otherwise collection-change realization reports a parent conflict.
        foreach (var item in grafted)
        {
            sourceList.Remove(item);
            if (item is UIElement sourceElement)
            {
                ReleaseSourceElementForGraft(sourceElement);
            }
        }

        targetList.Clear();
        foreach (var item in merged)
        {
            targetList.Add(item);
        }
    }

    private static object? FindMatchingItem(
        List<object> existingItems,
        HashSet<object> used,
        object sourceItem,
        int indexHint)
    {
        if (sourceItem is FrameworkElement sourceFe && !string.IsNullOrWhiteSpace(sourceFe.Name))
        {
            var named = existingItems.FirstOrDefault(candidate =>
                candidate != null
                && !used.Contains(candidate)
                && candidate is FrameworkElement candidateFe
                && AreTypesCompatible(candidate.GetType(), sourceItem.GetType())
                && string.Equals(candidateFe.Name, sourceFe.Name, StringComparison.Ordinal));

            if (named != null)
            {
                return named;
            }
        }

        // Positional fallback for unnamed items. Same identity caveat as FindMatchingChild:
        // reordering unnamed same-type items pairs by slot — name items (x:Name) for stability.
        if (indexHint >= 0 && indexHint < existingItems.Count)
        {
            var indexed = existingItems[indexHint];
            if (indexed != null && !used.Contains(indexed) && AreTypesCompatible(indexed.GetType(), sourceItem.GetType()))
            {
                return indexed;
            }
        }

        return null;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Recurses into ApplyElementPatch which mirrors DPs/CLR properties via reflection.")]
    private static void PatchContentControl(ContentControl target, ContentControl source, PatchCounters counters)
    {
        PatchObjectContent(
            target.Content,
            source.Content,
            value => target.Content = value,
            () => source.Content = null,
            counters);

        // ContentControl-derived controls carry a second element-bearing surface beyond Content:
        PatchHeaderIfPresent(target, source, counters);

        if (target is NavigationView targetNav && source is NavigationView sourceNav)
        {
            PatchObjectCollection(targetNav.MenuItems, sourceNav.MenuItems, counters);
            PatchObjectCollection(targetNav.FooterMenuItems, sourceNav.FooterMenuItems, counters);
        }

        if (target is InfoBar targetInfoBar && source is InfoBar sourceInfoBar)
        {
            // InfoBar.ActionButton is an element-valued (ButtonBase) DP — skipped by both copy paths.
            PatchObjectContent(
                targetInfoBar.ActionButton,
                sourceInfoBar.ActionButton,
                value => targetInfoBar.ActionButton = value as ButtonBase,
                () => sourceInfoBar.ActionButton = null,
                counters);
        }
    }

    /// <summary>
    /// Patches an object-typed content slot (ContentControl.Content, *.Header, …) in place: element
    /// content is recursed when type-compatible, otherwise the source element is detached from its
    /// per-instance source tree and grafted; non-element content (string / view-model) is assigned
    /// directly. The <paramref name="setContent"/> setter abstracts the target slot, while
    /// <paramref name="clearSourceContent"/> releases the disposable source owner's normal
    /// visual/logical ownership before the element is grafted.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Recurses into ApplyElementPatch which mirrors DPs/CLR properties via reflection.")]
    private static void PatchObjectContent(
        object? targetContent,
        object? sourceContent,
        Action<object?> setContent,
        Action clearSourceContent,
        PatchCounters counters)
    {
        if (sourceContent is not UIElement sourceElement)
        {
            // Non-element content (string / view-model object).
            setContent(sourceContent);
            return;
        }

        if (targetContent is FrameworkElement targetFe && sourceElement is FrameworkElement sourceFe
            && AreTypesCompatible(targetFe.GetType(), sourceFe.GetType()))
        {
            ApplyElementPatch(targetFe, sourceFe, counters);
            return;
        }

        // Clear the owning source slot first so its normal property callback removes both logical
        // and visual ownership. The fallback cleanup covers slots whose setter manages only one
        // tree (or whose visual was realized under an internal presenter).
        ReleaseSourceElementForGraft(sourceElement, clearSourceContent);
        setContent(sourceElement);
        counters.FallbackReplacements++;
    }

    /// <summary>
    /// Patches an element-valued <c>Header</c> (HeaderedContentControl / Expander / GroupBox /
    /// HeaderedItemsControl / …) in place. Reflection-based because these controls share no common
    /// "headered" base type. String / scalar headers are already handled by the Header DP in
    /// CopyDependencyProperties; only element-valued headers need the recurse / graft treatment here.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Reflects the Header property and recurses into ApplyElementPatch.")]
    private static void PatchHeaderIfPresent(object target, object source, PatchCounters counters)
    {
        var headerProperty = target.GetType().GetProperty("Header", BindingFlags.Instance | BindingFlags.Public);
        if (headerProperty is null
            || headerProperty.PropertyType != typeof(object)
            || !headerProperty.CanRead
            || !headerProperty.CanWrite
            || headerProperty.GetIndexParameters().Length != 0)
        {
            return;
        }

        var targetHeader = headerProperty.GetValue(target);
        var sourceHeader = headerProperty.GetValue(source);
        if (targetHeader is not UIElement && sourceHeader is not UIElement)
        {
            return;
        }

        PatchObjectContent(
            targetHeader,
            sourceHeader,
            value => headerProperty.SetValue(target, value),
            () => headerProperty.SetValue(source, null),
            counters);
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Recurses into ApplyElementPatch which mirrors DPs/CLR properties via reflection.")]
    private static void PatchSingleChildContainer<TContainer>(
        TContainer target,
        TContainer source,
        Func<TContainer, UIElement?> getChild,
        Action<TContainer, UIElement?> setChild,
        PatchCounters counters)
        where TContainer : FrameworkElement
    {
        var sourceChild = getChild(source);
        if (sourceChild == null)
        {
            setChild(target, null);
            return;
        }

        var targetChild = getChild(target);
        if (targetChild is FrameworkElement targetFe && sourceChild is FrameworkElement sourceFe
            && AreTypesCompatible(targetFe.GetType(), sourceFe.GetType()))
        {
            ApplyElementPatch(targetFe, sourceFe, counters);
            return;
        }

        // Detach from the source tree before grafting — Border/Viewbox single-child setters throw
        // on an already-parented child (no auto-detach, unlike Panel.Children.Add).
        ReleaseSourceElementForGraft(sourceChild, () => setChild(source, null));
        setChild(target, sourceChild);
        counters.FallbackReplacements++;
    }

    /// <summary>
    /// Releases an element from the disposable parse tree before grafting it into a live tree.
    /// Clearing the owning property/collection is the primary path because it preserves owner
    /// invariants; the explicit cleanup is a backstop for controls that manage only one tree.
    /// </summary>
    private static void ReleaseSourceElementForGraft(UIElement sourceElement, Action? clearSourceSlot = null)
    {
        clearSourceSlot?.Invoke();

        sourceElement.DetachFromVisualParent();

        // Once the visual parent is gone, FrameworkElement.Parent exposes only a remaining logical
        // parent. RemoveLogicalChild is internal to Core and intentionally available to Xaml via IVT.
        if (sourceElement is FrameworkElement { Parent: FrameworkElement logicalParent } frameworkElement)
        {
            logicalParent.RemoveLogicalChild(frameworkElement);
        }
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Hot-reload mirrors DependencyProperty static fields between matched types via reflection.")]
    private static void CopyDependencyProperties(DependencyObject target, DependencyObject source)
    {
        var dps = DependencyPropertyCache.GetOrAdd(target.GetType(), static type =>
        {
            var result = new List<DependencyProperty>();
            for (var current = type; current != null; current = current.BaseType)
            {
                var fields = current.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (var field in fields)
                {
                    if (!typeof(DependencyProperty).IsAssignableFrom(field.FieldType))
                    {
                        continue;
                    }

                    if (field.GetValue(null) is DependencyProperty dp && !result.Contains(dp))
                    {
                        result.Add(dp);
                    }
                }
            }

            return result;
        });

        var sourceFe = source as FrameworkElement;
        var targetFe = target as FrameworkElement;

        foreach (var dp in dps)
        {
            if (dp == FrameworkElement.NameProperty)
            {
                continue;
            }

            // Read-only DPs hold runtime-computed values (e.g. IsMouseOver, Thumb.IsDragging); a
            // parse-time local value must never clobber them. WPF throws on SetValue of a read-only
            // DP — here we simply skip so the control's own logic keeps owning the value.
            if (dp.ReadOnly)
            {
                continue;
            }

            // {DynamicResource} in the patch: re-register a LIVE subscription on the target (copying
            // the resolved snapshot alone would leave the value frozen and not theme-reactive).
            if (sourceFe != null && targetFe != null
                && DynamicResourceBindingOperations.TryGetDynamicResourceKey(sourceFe, dp, out var resourceKey)
                && resourceKey != null)
            {
                DynamicResourceBindingOperations.SetDynamicResource(targetFe, dp, resourceKey);
                continue;
            }

            // {Binding} in the patch: transfer the binding itself. ReadLocalValue cannot see bindings
            // (they live outside _localValues), so a literal copy would silently drop new/changed bindings.
            // Only plain {Binding} exposes a ParentBinding — MultiBinding/PriorityBinding/TemplateBinding
            // do not, and are not re-attached here (known limitation, rare in page-body markup).
            if (source.GetBindingExpression(dp) is BindingExpression sourceBinding && sourceBinding.ParentBinding != null)
            {
                if (targetFe != null)
                {
                    DynamicResourceBindingOperations.ClearDynamicResource(targetFe, dp);
                }
                target.SetBinding(dp, sourceBinding.ParentBinding);
                continue;
            }

            var sourceValue = source.ReadLocalValue(dp);
            if (ReferenceEquals(sourceValue, DependencyProperty.UnsetValue))
            {
                continue;
            }

            // Element-valued DPs (ContentControl.Content, Viewbox.Child, …) are visual children, not
            // scalar properties — leave them to the dedicated child-patch handlers. Blindly SetValue'ing
            // them here would call AddVisualChild on an already-parented source element and throw,
            // which is what made every ContentControl/Viewbox root fall back to a full restart.
            if (sourceValue is UIElement)
            {
                continue;
            }

            // Replacing a {DynamicResource}-bound value with a literal: drop the stale subscription so
            // the next theme/resource change does not revert the hot-reloaded value.
            if (targetFe != null)
            {
                DynamicResourceBindingOperations.ClearDynamicResource(targetFe, dp);
            }

            target.SetValue(dp, sourceValue);
        }

        // Attached properties (Grid.Row/Column/Span, Canvas.Left/Top, DockPanel.Dock, …) are keyed by
        // the OWNER type's DP and stored in the child's own _localValues — the target-type field scan
        // above never enumerates them, so editing Grid.Row etc. on a reused child would otherwise be
        // silently dropped. Mirror them straight from the source's local-value entries, which DO
        // include attached DPs.
        var ownDependencyProperties = new HashSet<DependencyProperty>(dps);
        foreach (var entry in source.GetLocalValueEntriesInternal())
        {
            var dp = entry.Key;
            if (ownDependencyProperties.Contains(dp) || dp == FrameworkElement.NameProperty || dp.ReadOnly)
            {
                continue;
            }

            var value = entry.Value;
            if (ReferenceEquals(value, DependencyProperty.UnsetValue) || value is UIElement)
            {
                continue;
            }

            target.SetValue(dp, value);
        }

        ResetDeletedProperties(target, source, targetFe, dps);
    }

    /// <summary>
    /// Declarative "delete reverts": resets DPs that a PREVIOUS patch of this instance set but the
    /// current patch no longer sets, so removing an attribute/binding in the .jalxaml reverts the live
    /// value to its default. A per-instance baseline of the DPs WE set is kept in <see cref="PatchBaseline"/>;
    /// only those DPs are ever cleared, so code-behind- and runtime-set values are never touched — which
    /// is exactly why a naive "clear every local value the source omits" pass would be unsafe.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Reads bindings reflectively to determine the set of source-declared DPs.")]
    private static void ResetDeletedProperties(DependencyObject target, DependencyObject source, FrameworkElement? targetFe, IReadOnlyList<DependencyProperty> dps)
    {
        // The DPs this patch declared on the source: its locally-set values (incl. attached DPs) plus
        // any property carrying a {Binding}. Element-valued DPs are owned by the child-patch handlers.
        var declared = new HashSet<DependencyProperty>();
        foreach (var entry in source.GetLocalValueEntriesInternal())
        {
            if (entry.Value is not UIElement)
            {
                declared.Add(entry.Key);
            }
        }

        foreach (var dp in dps)
        {
            if (source.GetBindingExpression(dp) is BindingExpression sb && sb.ParentBinding != null)
            {
                declared.Add(dp);
            }
        }

        if (PatchBaseline.TryGetValue(target, out var previouslyDeclared))
        {
            foreach (var dp in previouslyDeclared)
            {
                if (declared.Contains(dp) || dp == FrameworkElement.NameProperty || dp.ReadOnly)
                {
                    continue;
                }

                if (targetFe != null)
                {
                    DynamicResourceBindingOperations.ClearDynamicResource(targetFe, dp);
                }

                target.ClearValue(dp);
            }
        }

        PatchBaseline.AddOrUpdate(target, declared);
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Hot-reload mirrors public CLR properties between matched types via reflection — opt-in development feature.")]
    private static void CopyClrProperties(object target, object source)
    {
        var targetType = target.GetType();
        var sourceType = source.GetType();
        if (!AreTypesCompatible(targetType, sourceType))
        {
            return;
        }

        // Enumerates the SOURCE type's properties. When the target is an x:Class subtype and the source
        // is the parsed base type, subtype-only CLR properties are not mirrored — uncommon for XAML
        // roots, and all DPs (including inherited) are still covered by CopyDependencyProperties.
        var properties = sourceType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (var property in properties)
        {
            if (!property.CanRead || !property.CanWrite)
            {
                continue;
            }

            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (property.Name is "Name" or "Parent" or "VisualParent" or "TemplatedParent")
            {
                continue;
            }

            var propertyType = property.PropertyType;
            if (typeof(DependencyObject).IsAssignableFrom(propertyType)
                || typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType) && propertyType != typeof(string))
            {
                continue;
            }

            try
            {
                var value = property.GetValue(source);

                // Element-valued CLR properties (e.g. a Header holding a panel) are visual content,
                // not scalars — they are patched in place by the dedicated handlers (PatchHeaderIfPresent
                // etc.). Copying the reference here would reparent/steal the source element and may throw.
                if (value is UIElement)
                {
                    continue;
                }

                property.SetValue(target, value);
            }
            catch
            {
                // Ignore non-copyable CLR properties.
            }
        }
    }

    private static void CleanupDeadEntries(List<WeakReference<FrameworkElement>> entries)
    {
        entries.RemoveAll(static wr => !wr.TryGetTarget(out _));
    }

    private static bool AreTypesCompatible(Type targetType, Type sourceType)
    {
        return targetType == sourceType
               || sourceType.IsAssignableFrom(targetType)
               || targetType.IsAssignableFrom(sourceType);
    }

    private sealed class PatchCounters
    {
        public int UpdatedElements;
        public int FallbackReplacements;
        public int FailedElements;
    }
}

/// <summary>
/// Result for runtime JALXAML patch apply.
/// </summary>
public sealed record HotReloadPatchResult(
    int UpdatedElements,
    int FallbackReplacements,
    int FailedElements,
    string Message);
