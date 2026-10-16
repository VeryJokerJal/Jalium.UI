using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Controls.Platform;
using ObjCRuntime;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class MacOSTextContextMenus
{
    private static readonly (PlatformTextEditingCommand Command, string Title, string Selector)[] Commands = [
        (PlatformTextEditingCommand.Undo, "Undo", "undo:"),
        (PlatformTextEditingCommand.Redo, "Redo", "redo:"),
        (PlatformTextEditingCommand.Cut, "Cut", "cut:"),
        (PlatformTextEditingCommand.Copy, "Copy", "copy:"),
        (PlatformTextEditingCommand.Paste, "Paste", "paste:"),
        (PlatformTextEditingCommand.SelectAll, "Select All", "selectAll:")];

    internal static bool Show(PlatformTextContextMenuRequest request)
    {
        if (!NSThread.IsMain || !request.IsCurrent()) return false;
        var view = Runtime.GetNSObject<NSView>(request.Owner);
        if (view?.Window is not { IsVisible: true, IsMiniaturized: false } window) return false;
        using var menu = new NSMenu();
        using var target = new EditingTarget(request, view);
        foreach (var (command, title, _) in Commands)
        {
            // Keep editing shortcuts in the main menu only. Writing Tools
            // below uses the SDK's own localized submenu and operation tags.
            bool hideUnavailable = command is PlatformTextEditingCommand.Undo or PlatformTextEditingCommand.Redo or
                PlatformTextEditingCommand.SelectAll;
            if (hideUnavailable && !request.CanExecute(command)) continue;
            if (command is PlatformTextEditingCommand.Cut or PlatformTextEditingCommand.SelectAll && menu.Items.Length > 0)
                menu.AddItem(NSMenuItem.SeparatorItem);
            string label = NSBundle.FromClass(new Class(typeof(NSApplication))).GetLocalizedString(title, title, null);
            menu.AddItem(new NSMenuItem(label, new Selector("performEditingCommand:"), string.Empty)
            { Target = target, Tag = (nint)command });
        }
        if (OperatingSystem.IsMacOSVersionAtLeast(15, 2) &&
            NSWritingToolsCoordinator.IsWritingToolsAvailable && view.WritingToolsCoordinator is not null)
        {
            var items = NSMenuItem.WritingToolsItems;
            if (items.Length != 0 && menu.Items.Length != 0) menu.AddItem(NSMenuItem.SeparatorItem);
            foreach (var item in items)
            {
                target.GuardWritingToolsItem(item);
                menu.AddItem(item);
            }
        }
        if (!request.IsCurrent()) return false;
        NSMenu? previous = view.Menu;
        view.Menu = menu;
        try
        {
            var point = new CGPoint(request.Position.X,
                view.IsFlipped ? request.Position.Y : view.Bounds.Height - request.Position.Y);
            var inWindow = view.ConvertPointToView(point, null);
            using var trigger = NSEvent.MouseEvent(NSEventType.RightMouseDown, inWindow, 0,
                NSProcessInfo.ProcessInfo.SystemUptime, window.WindowNumber, null, 0, 1, 1);
            NSMenu.PopUpContextMenu(menu, trigger, view);
            return true;
        }
        finally { if (view.Menu?.Handle == menu.Handle) view.Menu = previous; }
    }

    [Register("JaliumTextContextMenuTarget")]
    private sealed class EditingTarget : NSObject, INSMenuItemValidation
    {
        private readonly PlatformTextContextMenuRequest _request;
        private readonly NSView _view;
        private readonly nint _writingToolsCoordinator;
        private readonly Dictionary<nint, (Selector Action, NSObject? Target)> _writingToolsCommands = new();

        internal EditingTarget(PlatformTextContextMenuRequest request, NSView view)
        {
            _request = request; _view = view;
            if (OperatingSystem.IsMacOSVersionAtLeast(15, 2)) _writingToolsCoordinator = view.WritingToolsCoordinator?.Handle ?? 0;
        }

        internal void GuardWritingToolsItem(NSMenuItem item)
        {
            if (item.Submenu is { } submenu)
            {
                foreach (var child in submenu.Items) GuardWritingToolsItem(child);
                return;
            }
            if (item.IsSeparatorItem || item.Action is not { } action) return;
            _writingToolsCommands[(nint)item.Handle] = (action, item.Target);
            item.Target = this; item.Action = new Selector("performWritingToolsCommand:");
        }

        private bool IsWritingToolsCurrent() => OperatingSystem.IsMacOSVersionAtLeast(15, 2) &&
            _writingToolsCoordinator != 0 && _request.IsCurrent() &&
            _view.WritingToolsCoordinator?.Handle == _writingToolsCoordinator && NSWritingToolsCoordinator.IsWritingToolsAvailable;

        public bool ValidateMenuItem(NSMenuItem item)
        {
            try
            {
                return _writingToolsCommands.ContainsKey((nint)item.Handle) ? IsWritingToolsCurrent()
                    : _request.CanExecute((PlatformTextEditingCommand)(int)item.Tag);
            }
            catch { return false; } // Application callbacks must not escape into AppKit.
        }

        [Export("performWritingToolsCommand:")]
        public void PerformWritingToolsCommand(NSMenuItem item)
        {
            if (!ValidateMenuItem(item) || !_writingToolsCommands.TryGetValue((nint)item.Handle, out var command)) return;
            // Preserve AppKit's standard item tags (proofread, rewrite, tone,
            // summary/list/table, compose) and the captured native responder.
            NSApplication.SharedApplication.SendAction(command.Action, command.Target ?? _view, item);
        }

        [Export("performEditingCommand:")]
        public void PerformEditingCommand(NSMenuItem item)
        {
            var command = (PlatformTextEditingCommand)(int)item.Tag;
            if (!ValidateMenuItem(item) || !_request.IsCurrent()) return;
            var entry = Commands.First(value => value.Command == command);
            NSApplication.SharedApplication.SendAction(new Selector(entry.Selector), _view, item);
        }
    }
}
