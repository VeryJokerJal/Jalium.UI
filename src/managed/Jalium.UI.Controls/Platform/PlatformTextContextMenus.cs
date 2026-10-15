using System.Runtime.CompilerServices;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Platform;

/// <summary>Standard text commands, in the same order as the native editing bridge.</summary>
public enum PlatformTextEditingCommand { Copy, Cut, Paste, SelectAll, Undo, Redo }

/// <summary>A synchronous context-menu request bound to one live editing target.</summary>
public sealed record PlatformTextContextMenuRequest(
    nint Owner, Point Position, Func<bool> IsCurrent,
    Func<PlatformTextEditingCommand, bool> CanExecute);

/// <summary>Supplies the default text menu through an OS-specific UI package.</summary>
public static class PlatformTextContextMenus
{
    private sealed class PointerState { internal MouseButton? SuppressRelease; }
    private static readonly ConditionalWeakTable<UIElement, PointerState> PointerStates = new();
    private static Func<PlatformTextContextMenuRequest, bool>? _show;
    [ThreadStatic] private static bool _opening;

    static PlatformTextContextMenus()
    {
        foreach (var type in new[] { typeof(TextBoxBase), typeof(EditControl), typeof(PasswordBox),
            typeof(Terminal), typeof(HexEditor) })
        {
            EventManager.RegisterClassHandler(type, UIElement.MouseDownEvent,
                new MouseButtonEventHandler(OnMouseDown));
            EventManager.RegisterClassHandler(type, UIElement.PreviewMouseUpEvent,
                new MouseButtonEventHandler(OnMouseUp), handledEventsToo: true);
            EventManager.RegisterClassHandler(type, UIElement.KeyDownEvent,
                new KeyEventHandler(OnKeyDown));
        }
    }

    /// <summary>Displays a menu synchronously; false means the native owner was unavailable.</summary>
    public static Func<PlatformTextContextMenuRequest, bool>? Show
    {
        get => _show;
        set => _show = value;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement owner) return;
        var state = PointerStates.GetValue(owner, static _ => new PointerState());
        state.SuppressRelease = null;
        if (e.Handled || !OperatingSystem.IsMacOS() || _show == null ||
            !(e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Left &&
                (e.KeyboardModifiers & ModifierKeys.Control) != 0)) return;
        // Template buttons keep their own actions and menus.
        for (var source = e.OriginalSource as Visual; source != null && source != owner; source = source.VisualParent)
            if (source is ButtonBase) return;

        var origin = e.OriginalSource as Visual;
        // Attached menus retain ContextMenuService's normal mouse-up pipeline.
        if (e.ChangedButton == MouseButton.Right && FindExplicitMenuOwner(origin ?? owner) != null) return;
        if (Open(owner, e.GetPosition(owner), origin))
        {
            state.SuppressRelease = e.ChangedButton;
            e.Handled = true;
        }
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement owner && PointerStates.TryGetValue(owner, out var state) &&
            state.SuppressRelease == e.ChangedButton)
        {
            state.SuppressRelease = null;
            e.Handled = true;
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || !OperatingSystem.IsMacOS() || _show == null || sender is not UIElement owner ||
            !(e.Key == Key.F10 && e.KeyboardModifiers == ModifierKeys.Shift ||
              e.Key == Key.Apps && e.KeyboardModifiers == ModifierKeys.None)) return;
        if (e.Key == Key.F10 && FindExplicitMenuOwner(owner) != null) return;
        e.Handled = Open(owner, null);
    }

    private static bool Open(UIElement owner, Point? position, Visual? source = null)
    {
        if (_opening) return false;
        _opening = true;
        try { return OpenCore(owner, position, source); }
        finally { _opening = false; }
    }

    private static UIElement? FindExplicitMenuOwner(Visual source)
    {
        for (Visual? current = source; current != null; current = current.VisualParent)
            if (current is UIElement owner && ContextMenuService.GetContextMenu(owner) != null) return owner;
        return null;
    }

    private static bool OpenCore(UIElement owner, Point? position, Visual? source)
    {
        // Explicit menus on this editor or its containers retain the existing
        // placement, ShowOnDisabled and cancellation pipeline.
        if (FindExplicitMenuOwner(source ?? owner) is { } menuOwner)
        {
            return position is { } point
                ? ContextMenuService.TryOpen(menuOwner, owner.TranslatePoint(point, null))
                : ContextMenuService.TryOpenAttachedForKeyboard(menuOwner);
        }
        if (!owner.IsEnabled || !owner.IsVisible || !ContextMenuService.GetIsEnabled(owner) ||
            Window.GetWindow(owner) is not { } window || !owner.Focus()) return false;

        Point? eventPosition = position is { } pointAt ? owner.TranslatePoint(pointAt, null) : null;
        var opening = eventPosition is { } at
            ? new ContextMenuEventArgs(owner, true, at.X, at.Y)
            : new ContextMenuEventArgs(owner, true);
        opening.RoutedEvent = ContextMenuService.ContextMenuOpeningEvent;
        owner.RaiseEvent(opening);
        if (opening.Handled) return false;
        var caret = owner is IImeSupport ime ? ime.GetImeCaretRectangle() : Rect.Empty;
        Point anchor = position ?? (!caret.IsEmpty
            ? new Point(caret.Left, caret.Bottom) : new Point(0, owner.RenderSize.Height));
        var request = window.CreateMacOSTextContextMenuRequest(owner, anchor);
        if (request == null || !request.IsCurrent()) return false;
        bool shown = false;
        try { shown = _show?.Invoke(request) == true; }
        finally
        {
            if (shown)
            {
                if (position.HasValue) window.MarkMacOSTextMenuPointerConsumed();
                var closing = eventPosition is { } end
                    ? new ContextMenuEventArgs(owner, false, end.X, end.Y)
                    : new ContextMenuEventArgs(owner, false);
                closing.RoutedEvent = ContextMenuService.ContextMenuClosingEvent;
                owner.RaiseEvent(closing);
            }
        }
        return shown;
    }
}
