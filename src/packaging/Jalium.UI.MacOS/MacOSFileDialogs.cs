using AppKit;
using Foundation;
using Jalium.UI.Controls.Platform;
using UniformTypeIdentifiers;
using System.Runtime.Versioning;
using System.Runtime.ExceptionServices;
using ObjCRuntime;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class MacOSFileDialogs
{
    public static string[]? Show(PlatformFileDialogOptions options)
    {
        if (!NSThread.IsMain) throw new InvalidOperationException("File panels must be shown on the AppKit main thread.");
        using NSSavePanel panel = options.Save ? NSSavePanel.SavePanel : NSOpenPanel.OpenPanel;
        panel.Title = options.Title ?? (options.Directory ? "选择文件夹" : options.Save ? "保存文件" : "打开文件");
        panel.CanCreateDirectories = options.CreateDirectories;
        var directory = options.InitialDirectory;
        if (string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(options.FileName))
            directory = Directory.Exists(options.FileName) ? options.FileName : Path.GetDirectoryName(options.FileName);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            panel.DirectoryUrl = NSUrl.FromFilename(directory);
        if (!string.IsNullOrWhiteSpace(options.FileName) && !options.Directory)
            panel.NameFieldStringValue = Path.GetFileName(options.FileName);

        if (panel is NSOpenPanel open)
        {
            open.CanChooseDirectories = options.Directory;
            open.CanChooseFiles = !options.Directory;
            open.AllowsMultipleSelection = options.Multiple;
            open.ResolvesAliases = options.DereferenceLinks;
        }

        var filters = new FileDialogFilterSelection(options.Filters, options.FilterIndex, options.DefaultExtension);
        using var panelDelegate = new FilePanelDelegate(filters, options);
        panel.Delegate = panelDelegate;
        using var selector = new NSPopUpButton(new CoreGraphics.CGRect(0, 0, 280, 26), false);
        void ApplyFilter()
        {
            panelDelegate.ResetSelection();
            // A save panel appends its first content type even with AllowsOtherFileTypes.
            // The delegate owns AddExtension and runs before AppKit's overwrite prompt.
            UTType[] types = options.Save ? [] : filters.Extensions.Select(extension => UTType.CreateFromExtension(extension))
                .Where(type => type != null).Cast<UTType>().ToArray();
            panel.AllowedContentTypes = types;
            panel.AllowsOtherFileTypes = options.Save || types.Length == 0;
            panel.ValidateVisibleColumns();
        }
        EventHandler changed = (_, _) =>
        {
            filters.Select((int)selector.IndexOfSelectedItem + 1);
            ApplyFilter();
        };
        if (!options.Directory)
        {
            ApplyFilter();
            if (options.Filters.Length > 1)
            {
                selector.AddItems(options.Filters.Select(filter => filter.Name).ToArray());
                selector.SelectItem(filters.Index - 1);
                selector.Activated += changed;
                panel.AccessoryView = selector;
            }
        }
        try
        {
            var response = options.Owner == 0 ? (NSModalResponse)(long)panel.RunModal() : RunSheet(panel, options.Owner);
            panelDelegate.Failure?.Throw();
            if (response != NSModalResponse.OK) return null;
            options.SelectedFilterIndex = filters.Index;
        }
        finally
        {
            selector.Activated -= changed;
            panel.AccessoryView = null;
            panel.Delegate = null!;
        }
        return GetPaths(panel);
    }

    private static string[] GetPaths(NSSavePanel panel) => panel is NSOpenPanel selectedOpen
            ? selectedOpen.Urls.Select(url => url.Path).OfType<string>().ToArray()
            : panel.Url?.Path is { } path ? new[] { path } : Array.Empty<string>();

    private static NSModalResponse RunSheet(NSSavePanel panel, nint handle)
    {
        var owner = Runtime.GetNSObject(handle) switch
        {
            NSView view => view.Window,
            NSWindow window => window,
            _ => null
        };
        if (owner == null || !owner.IsVisible)
            throw new InvalidOperationException("A file panel requires a visible native owner window.");
        if (owner.AttachedSheet != null)
            throw new InvalidOperationException("The owner window already has an attached sheet.");

        bool ownerClosed = false;
        using var closing = NSNotificationCenter.DefaultCenter.AddObserver(NSWindow.WillCloseNotification, _ =>
        {
            ownerClosed = true;
            ScheduleCancel(panel);
        }, owner);
        try
        {
            // Use AppKit's synchronous file-panel session. Waiting on an async
            // completion handler deadlocks when ShowDialog is entered from a
            // main dispatch-queue callback (including accessibility activation).
            owner.BeginSheet(panel, _ => { });
            var response = (NSModalResponse)(long)panel.RunModal();
            return ownerClosed ? NSModalResponse.Cancel : response;
        }
        finally
        {
            if (panel.SheetParent is { } parent) parent.EndSheet(panel, NSModalResponse.Cancel);
            panel.OrderOut(null);
        }
    }

    private static void ScheduleCancel(NSSavePanel panel)
    {
        // Run-loop work can execute inside a nested modal session entered from
        // the main dispatch queue. Another main-queue block cannot run there.
        NSRunLoop.Main.Perform([NSRunLoopMode.Common, NSRunLoopMode.ModalPanel], () =>
        {
            if ((nint)panel.Handle != 0 && (panel.IsVisible ||
                NSApplication.SharedApplication.ModalWindow is { } modal && modal.Handle == panel.Handle))
                panel.Cancel(panel);
        });
    }

    private sealed class FilePanelDelegate(FileDialogFilterSelection filters, PlatformFileDialogOptions options) : NSOpenSavePanelDelegate
    {
        private string[]? _openPaths;
        public ExceptionDispatchInfo? Failure { get; private set; }

        public void ResetSelection() => _openPaths = null;
        public override void SelectionDidChange(NSSavePanel panel) => ResetSelection();
        public override void DidChangeToDirectory(NSSavePanel panel, NSUrl newDirectoryUrl) => ResetSelection();

        public override string UserEnteredFilename(NSSavePanel panel, string filename, bool confirmed)
            => confirmed ? filters.AppendExtension(filename, options.AddExtension) : filename;

        public override bool ShouldEnableUrl(NSSavePanel panel, NSUrl url)
            => !url.IsFileUrl || url.Path is not { } path || Directory.Exists(path)
                || filters.MatchesFileName(Path.GetFileName(path));

        public override bool ValidateUrl(NSSavePanel panel, NSUrl url, out NSError outError)
        {
            outError = null!;
            if (Failure != null || url.Path is not { } path) return false;
            // Cache the native batch while AppKit validates each URL; copying
            // the complete selection on every callback would be quadratic.
            var paths = panel is NSOpenPanel ? _openPaths ??= GetPaths(panel) : [path];
            if (paths.Length == 0) paths = [path];
            // AppKit validates each selected URL. The last selected path is the
            // single confirmation point, independent of callback ordering.
            if (path != paths[^1]) return true;
            try { return options.ValidateSelection?.Invoke(paths, filters.Index) ?? true; }
            catch (Exception error)
            {
                Failure = ExceptionDispatchInfo.Capture(error);
                ScheduleCancel(panel);
                return false;
            }
            finally { ResetSelection(); }
        }
    }
}
