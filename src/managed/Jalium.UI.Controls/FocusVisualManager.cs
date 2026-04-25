using System.Runtime.CompilerServices;
using Jalium.UI.Documents;
using Jalium.UI.Input;

namespace Jalium.UI.Controls;

/// <summary>
/// Tracks keyboard focus changes across the framework and materializes the configured
/// <see cref="FrameworkElement.FocusVisualStyle"/> as an <see cref="FocusVisualAdorner"/>
/// inside the nearest <see cref="AdornerLayer"/>. This replaces the legacy approach of
/// embedding per-template FocusBorder elements inside every control template.
/// <para/>
/// Mirrors the WinUI / WPF convention of only showing focus visuals while the user is
/// actively driving the UI from the keyboard. Mouse interaction suppresses the ring;
/// pressing Tab, the arrow keys, Home, End, PageUp or PageDown re-enables it.
/// </summary>
public static class FocusVisualManager
{
    // ConditionalWeakTable keeps the adorner alive only as long as the focused element
    // lives. It also makes the bookkeeping leak-safe when elements are removed from the
    // tree without a matching LostKeyboardFocus (unusual but defensive).
    private static readonly ConditionalWeakTable<UIElement, FocusVisualAdorner> s_activeAdorners = new();

    /// <summary>
    /// The element that currently has keyboard focus, cached so we can flip its adorner
    /// on or off when <see cref="ShowFocusCues"/> changes without having to re-query the
    /// focus manager.
    /// </summary>
    private static UIElement? s_currentFocusedElement;

    /// <summary>
    /// When false, focus visuals are suppressed regardless of which element has keyboard
    /// focus. Starts suppressed because apps typically launch in a mouse-first state and
    /// the first keyboard-navigation event flips it on. Mouse interaction flips it off.
    /// </summary>
    private static bool s_showFocusCues;

    private static bool s_initialized;
    private static readonly object s_syncRoot = new();

    /// <summary>
    /// Gets whether focus visuals are currently enabled. Transitions to <c>true</c> on
    /// the first keyboard navigation key (Tab / arrows / Home / End / PageUp / PageDown)
    /// and back to <c>false</c> on the next mouse press. Updates the active focus visual
    /// as it flips.
    /// </summary>
    public static bool ShowFocusCues => s_showFocusCues;

    /// <summary>
    /// Wires the manager into the global keyboard-focus hook. Idempotent.
    /// Consumers generally do not need to call this directly — the first construction
    /// of a <see cref="Window"/> ensures initialization.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (s_initialized) return;
        lock (s_syncRoot)
        {
            if (s_initialized) return;

            UIElement.IsKeyboardFocusedChangedStatic += OnKeyboardFocusedChanged;

            // PreviewKeyDown tunnels from the root, so a class handler on UIElement sees
            // every keystroke before the focused element's own handler decides whether to
            // consume it. That lets us switch into "keyboard mode" even when the key ends
            // up being handled as text input (e.g. arrow keys inside a TextBox). Doing it
            // this way matches WinUI, which considers an arrow-key press a hint that the
            // user is driving from the keyboard regardless of who ultimately handles it.
            EventManager.RegisterClassHandler(
                typeof(UIElement),
                UIElement.PreviewKeyDownEvent,
                new KeyEventHandler(OnGlobalPreviewKeyDown));

            EventManager.RegisterClassHandler(
                typeof(UIElement),
                UIElement.PreviewMouseDownEvent,
                new MouseButtonEventHandler(OnGlobalPreviewMouseDown));

            s_initialized = true;
        }
    }

    private static void OnGlobalPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsFocusNavigationKey(e.Key))
        {
            SetShowFocusCues(true);
        }
    }

    private static void OnGlobalPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        SetShowFocusCues(false);
    }

    private static bool IsFocusNavigationKey(Key key)
    {
        // Navigation keys per WinUI convention. Anything else (letter keys, function keys,
        // Escape, Enter, etc.) is treated as normal input and does not re-enable cues —
        // a stray letter press inside a TextBox must not suddenly grow a focus ring.
        return key switch
        {
            Key.Tab => true,
            Key.Left => true,
            Key.Right => true,
            Key.Up => true,
            Key.Down => true,
            Key.Home => true,
            Key.End => true,
            Key.PageUp => true,
            Key.PageDown => true,
            _ => false,
        };
    }

    private static void SetShowFocusCues(bool value)
    {
        if (s_showFocusCues == value) return;
        s_showFocusCues = value;

        // Bring the current focus visual in sync with the new mode. When we flip on, the
        // already-focused element (typically the one about to receive keyboard input)
        // gets its ring; when we flip off, the active ring is immediately hidden.
        var current = s_currentFocusedElement;
        if (current == null) return;

        if (value)
        {
            AttachAdorner(current);
        }
        else
        {
            DetachAdorner(current);
        }
    }

    private static void OnKeyboardFocusedChanged(UIElement element, bool isFocused)
    {
        if (isFocused)
        {
            s_currentFocusedElement = element;

            // Auto-scroll the focused element into view via the standard BringIntoView
            // routed-event pipeline. Runs unconditionally (not gated on ShowFocusCues)
            // because WPF behavior is to scroll whenever focus arrives, regardless of
            // whether it came from the keyboard or a mouse click on a partially clipped
            // item — ScrollViewer.MakeVisible is a no-op when the element is already
            // fully inside the viewport, so this is free in the common case. We do this
            // before AttachAdorner so the subsequent layout pass arranges both the
            // scrolled content and the adorner in one go.
            if (element is FrameworkElement focusedFe)
            {
                focusedFe.BringIntoView();
            }

            // Only surface the focus visual when the user is in keyboard mode. The
            // adorner still tracks every focused element via s_currentFocusedElement
            // so that a later Tab/arrow press can attach it without a focus change.
            if (s_showFocusCues)
            {
                AttachAdorner(element);
            }
        }
        else
        {
            if (ReferenceEquals(s_currentFocusedElement, element))
            {
                s_currentFocusedElement = null;
            }
            DetachAdorner(element);
        }
    }

    private static void AttachAdorner(UIElement element)
    {
        // Clear any stray adorner still tracked for this element — defensive against double-fire.
        DetachAdorner(element);

        if (element is not FrameworkElement fe)
            return;

        if (!fe.Focusable || !fe.IsEnabled || fe.Visibility != Visibility.Visible)
            return;

        var style = fe.ResolveFocusVisualStyle();
        if (style == null)
            return;

        var layer = AdornerLayer.GetAdornerLayer(fe);
        if (layer == null)
            return;

        var adorner = new FocusVisualAdorner(fe, style);
        layer.Add(adorner);
        s_activeAdorners.AddOrUpdate(element, adorner);
    }

    private static void DetachAdorner(UIElement element)
    {
        if (!s_activeAdorners.TryGetValue(element, out var adorner))
            return;

        s_activeAdorners.Remove(element);

        // Remove from whichever layer currently owns it. Prefer the direct visual parent
        // because the element may have been detached from the tree since focus arrived,
        // which would make AdornerLayer.GetAdornerLayer return null.
        var layer = adorner.VisualParent as AdornerLayer
                    ?? AdornerLayer.GetAdornerLayer(element)
                    ?? AdornerLayer.GetAdornerLayer(adorner);
        layer?.Remove(adorner);
    }
}
