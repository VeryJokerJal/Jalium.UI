using System.Runtime.CompilerServices;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Input;

/// <summary>
/// Deferred keyboard-focus revalidation — the counterpart of WPF's
/// <c>KeyboardDevice.ReevaluateFocusAsync</c>.
/// <para/>
/// Keyboard focus is a process-wide pointer the visual tree knows nothing about, so nothing
/// would otherwise pull it off an element that has been removed from its window (page swap,
/// template rebuild, container recycling), collapsed, disabled or made non-focusable. Leaving it
/// there is not cosmetic: key events keep routing into the dead subtree, Tab cycles through
/// elements that are no longer on screen, and the focus visual stays painted at the old spot.
/// <para/>
/// The check runs deferred on the dispatcher (<see cref="DispatcherPriority.Input"/>) rather than
/// inline, because a subtree is routinely detached and re-attached within one dispatcher batch
/// (Popup content moving between the overlay and its own window, recycling panels) and moving
/// focus in the middle of that would be wrong. Detach hops are recorded so the deferred pass can
/// still walk past the detach point and hand focus to the nearest still-connected focusable
/// ancestor — the element the user would expect to keep the keyboard cursor — clearing it when
/// there is none.
/// </summary>
internal static class KeyboardFocusRevalidation
{
    private sealed class DispatcherState
    {
        /// <summary>
        /// Detached subtree roots that (transitively) held keyboard focus when they were removed,
        /// mapped to the parent they were removed from. Weak-keyed so an abandoned subtree is not
        /// kept alive by this bookkeeping while the pass is pending.
        /// </summary>
        public ConditionalWeakTable<UIElement, DependencyObject> DetachedParents = new();

        public bool Scheduled;
    }

    private const int MaxChainDepth = 4096;

    private static readonly ConditionalWeakTable<Dispatcher, DispatcherState> s_states = new();

    /// <summary>
    /// Raised by the deferred pass when focus stays on the same element although the tree
    /// around it changed (subtree moved, attached late, re-attached after a detach). Lets the
    /// focus visual manager put a ring back that could not be created at focus time (no adorner
    /// layer yet) or that layout pruned while the element was briefly out of its window.
    /// </summary>
    internal static event Action<UIElement>? FocusRetained;

    /// <summary>
    /// Called from <see cref="UIElement.OnVisualParentChanged(DependencyObject?)"/> for both
    /// attach and detach of <paramref name="element"/>.
    /// </summary>
    internal static void OnVisualParentChanged(UIElement element, DependencyObject? oldParent)
    {
        if (FocusService.FocusedElement is not UIElement focused)
            return;

        var state = ResolveStateIfSubtreeHoldsFocus(element, focused);
        if (state is null)
            return;

        if (element.VisualParent is null && oldParent is not null)
            state.DetachedParents.AddOrUpdate(element, oldParent);

        Schedule(element.Dispatcher, state);
    }

    /// <summary>
    /// Called when <paramref name="element"/> or one of its ancestors can no longer hold focus
    /// (effective <see cref="UIElement.IsEnabled"/> or <see cref="UIElement.IsVisible"/> turned false).
    /// </summary>
    internal static void OnFocusabilityChanged(UIElement element)
    {
        if (FocusService.FocusedElement is not UIElement focused)
            return;

        var state = ResolveStateIfSubtreeHoldsFocus(element, focused);
        if (state is null)
            return;

        Schedule(element.Dispatcher, state);
    }

    /// <summary>
    /// Called when a property that only matters on the focused element itself turned false:
    /// <see cref="UIElement.Focusable"/> (does not inherit) and <see cref="UIElement.IsVisible"/>
    /// (already effective — a collapsed ancestor reaches the focused element through
    /// <c>UpdateIsVisibleFromTree</c>, so reacting on every hidden descendant would be wasted work).
    /// </summary>
    internal static void OnSelfFocusabilityChanged(UIElement element)
    {
        if (!ReferenceEquals(FocusService.FocusedElement, element))
            return;

        Schedule(element.Dispatcher, s_states.GetValue(element.Dispatcher, static _ => new DispatcherState()));
    }

    private static DispatcherState? ResolveStateIfSubtreeHoldsFocus(UIElement subtreeRoot, UIElement focused)
    {
        var dispatcher = subtreeRoot.Dispatcher;

        if (ReferenceEquals(subtreeRoot, focused) || subtreeRoot.IsKeyboardFocusWithin)
            return s_states.GetValue(dispatcher, static _ => new DispatcherState());

        // IsKeyboardFocusWithin reflects the tree as it stood at the last focus change. While a
        // revalidation is in flight the tree may have moved underneath it (a focused subtree
        // detached and re-attached elsewhere), so fall back to walking up from the focused
        // element. When nothing is in flight the flags are trustworthy and the walk is skipped.
        if (!s_states.TryGetValue(dispatcher, out var state) || !state.Scheduled)
            return null;

        return IsAncestorOrSelf(subtreeRoot, focused) ? state : null;
    }

    private static bool IsAncestorOrSelf(UIElement candidate, UIElement element)
    {
        var depth = 0;
        for (Visual? current = element; current is not null && depth++ < MaxChainDepth; current = current.VisualParent)
        {
            if (ReferenceEquals(current, candidate))
                return true;
        }

        return false;
    }

    private static void Schedule(Dispatcher dispatcher, DispatcherState state)
    {
        if (state.Scheduled || dispatcher.HasShutdownStarted)
            return;

        state.Scheduled = true;
        dispatcher.BeginInvoke(DispatcherPriority.Input, () => Run(state));
    }

    private static void Run(DispatcherState state)
    {
        state.Scheduled = false;

        // Take ownership of the hops recorded so far; anything recorded by handlers that run
        // during this pass (a GotKeyboardFocus handler swapping content) belongs to the next one.
        var hops = state.DetachedParents;
        state.DetachedParents = new ConditionalWeakTable<UIElement, DependencyObject>();

        Revalidate(hops);
    }

    private static void Revalidate(ConditionalWeakTable<UIElement, DependencyObject> hops)
    {
        if (FocusService.FocusedElement is not UIElement focused)
            return;

        // Ancestor chain of the focused element (self first), bridged across the recorded detach
        // points. firstConnected is the index of the first entry above the last bridge — the part
        // of the chain that is still attached to the root the focus used to live under. It stays 0
        // when no bridge was needed, i.e. the focused element is still connected to that root.
        var chain = new List<UIElement>();
        var firstConnected = 0;
        var depth = 0;
        DependencyObject? current = focused;
        while (current is not null && depth++ < MaxChainDepth)
        {
            if (current is UIElement uiElement)
                chain.Add(uiElement);

            DependencyObject? next = (current as Visual)?.VisualParent;
            if (current is UIElement detached
                && hops.TryGetValue(detached, out var oldParent)
                && (next is null || (!IsHosted(next as Visual) && IsHosted(oldParent as Visual))))
            {
                // Bridge across the detach point: either the subtree is still detached, or it was
                // re-attached outside every window while the tree it left is window-hosted — a
                // page parked in an off-screen holder is as gone for focus purposes as a removed
                // one. A subtree that merely moved to another hosted spot keeps its actual chain.
                next = oldParent;
                firstConnected = chain.Count;
            }

            current = next;
        }

        var connected = firstConnected == 0;
        if (connected && IsFocusable(focused))
        {
            // Focus stays put; only the tree around it may have moved.
            SyncFocusWithinFlags(chain, hops, focused);
            FocusRetained?.Invoke(focused);
            return;
        }

        UIElement? target = null;
        for (var index = connected ? 1 : firstConnected; index < chain.Count; index++)
        {
            if (IsFocusable(chain[index]))
            {
                target = chain[index];
                break;
            }
        }

        if (target is not null)
        {
            FocusService.Focus(target);
        }
        else
        {
            FocusService.ClearFocus();
        }

        SyncFocusWithinFlags(chain, hops, FocusService.FocusedElement as UIElement);
    }

    private static bool IsFocusable(UIElement element) =>
        element.Focusable && element.IsEnabled && element.IsVisible &&
        !Jalium.UI.Styling.CssDisplayProperties.IsExitInert(element);

    /// <summary>True when the visual sits under a window (or popup window) root.</summary>
    private static bool IsHosted(Visual? visual)
    {
        var depth = 0;
        for (Visual? current = visual; current is not null && depth++ < MaxChainDepth; current = current.VisualParent)
        {
            if (current is IWindowHost)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The focus provider maintains <see cref="UIElement.IsKeyboardFocusWithin"/> from the visual
    /// chain as it stands at each focus change, so a tree move underneath an unchanged focus (or a
    /// detach that only the deferred pass reacts to) leaves the old ancestors flagged and the new
    /// ones not. Clear every old-chain entry that is not an ancestor-or-self of the element that
    /// holds focus now, then flag the live chain.
    /// </summary>
    private static void SyncFocusWithinFlags(
        List<UIElement> oldChain,
        ConditionalWeakTable<UIElement, DependencyObject> hops,
        UIElement? newFocused)
    {
        HashSet<UIElement>? live = null;
        if (newFocused is not null)
        {
            live = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);
            var depth = 0;
            for (Visual? current = newFocused; current is not null && depth++ < MaxChainDepth; current = current.VisualParent)
            {
                if (current is UIElement uiElement)
                    live.Add(uiElement);
            }
        }

        foreach (var element in oldChain)
        {
            if (live is null || !live.Contains(element))
                element.UpdateIsKeyboardFocusWithin(false);
        }

        // A subtree that was detached and re-attached elsewhere never needed its hop to build the
        // chain above, yet the ancestors it was detached from are still flagged from the last
        // focus change. Walk those recorded old parents too.
        foreach (var (_, oldParent) in hops)
        {
            var depth = 0;
            for (Visual? current = oldParent as Visual; current is not null && depth++ < MaxChainDepth; current = current.VisualParent)
            {
                if (current is UIElement uiElement && (live is null || !live.Contains(uiElement)))
                    uiElement.UpdateIsKeyboardFocusWithin(false);
            }
        }

        if (live is null)
            return;

        foreach (var element in live)
            element.UpdateIsKeyboardFocusWithin(true);
    }
}
