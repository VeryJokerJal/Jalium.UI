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
    // DirectoryUrl on a newly created panel can already be Documents. Track
    // directories from completed sessions rather than mistaking that default
    // for the user's remembered location. Show is confined to the main thread.
    private static readonly Dictionary<(bool Save, bool Directory), string> LastDirectories = [];

    public static string[]? Show(PlatformFileDialogOptions options)
    {
        if (!NSThread.IsMain) throw new InvalidOperationException("File panels must be shown on the AppKit main thread.");
        var navigation = new FileDialogNavigation(options.RootDirectory);
        var directory = navigation.InitialDirectory(options.InitialDirectory, options.FileName,
            options.DefaultDirectory, LastDirectories.GetValueOrDefault((options.Save, options.Directory)));
        var filters = new FileDialogFilterSelection(options.Filters, options.FilterIndex, options.DefaultExtension);
        string name = Path.GetFileName(options.FileName ?? string.Empty);
        bool readOnly = options.ReadOnlyChecked;
        bool showHidden = options.ShowHiddenItems;
        while (true)
        {
            var result = ShowPanel(options, navigation, filters, directory, name, readOnly, showHidden);
            if (result.RequestedDirectory is { } nextDirectory)
            {
                if (!Directory.Exists(nextDirectory)) return null;
                directory = nextDirectory;
                name = result.Name;
                readOnly = result.ReadOnly;
                showHidden = result.ShowHidden;
                continue;
            }
            if (result.Directory is { } visited && Directory.Exists(visited) && navigation.Contains(visited))
                LastDirectories[(options.Save, options.Directory)] = visited;
            if (result.Response != NSModalResponse.OK) return null;
            options.SelectedFilterIndex = filters.Index;
            options.ReadOnlyChecked = result.ReadOnly;
            return result.Paths;
        }
    }

    private readonly record struct PanelResult(NSModalResponse Response, string[]? Paths,
        string? Directory, string? RequestedDirectory, string Name, bool ReadOnly, bool ShowHidden);

    private static PanelResult ShowPanel(PlatformFileDialogOptions options, FileDialogNavigation navigation,
        FileDialogFilterSelection filters, string? directory, string name, bool readOnly, bool showHidden)
    {
        // directoryURL is configuration-only. Reusing a dismissed panel can
        // restore its previous remote browser location despite the new getter
        // value, so navigation creates a fresh panel with retained user choices.
        using NSSavePanel panel = options.Save ? NSSavePanel.SavePanel : NSOpenPanel.OpenPanel;
        panel.Title = options.Title ?? (options.Directory ? "选择文件夹" : options.Save ? "保存文件" : "打开文件");
        panel.CanCreateDirectories = options.CreateDirectories;
        panel.ShowsHiddenFiles = showHidden;
        if (directory != null)
            panel.DirectoryUrl = NSUrl.FromFilename(directory);
        if (!string.IsNullOrWhiteSpace(name) && !options.Directory)
            panel.NameFieldStringValue = name;

        if (panel is NSOpenPanel open)
        {
            open.CanChooseDirectories = options.Directory;
            open.CanChooseFiles = !options.Directory;
            open.AllowsMultipleSelection = options.Multiple;
            open.ResolvesAliases = options.DereferenceLinks;
        }

        FilePanelDelegate? currentDelegate = null;
        void ApplyFilter()
        {
            currentDelegate?.ResetSelection();
            // A save panel appends its first content type even with AllowsOtherFileTypes.
            // The delegate owns AddExtension and runs before AppKit's overwrite prompt.
            UTType[] types = options.Save ? [] : filters.Extensions.Select(extension => UTType.CreateFromExtension(extension))
                .Where(type => type != null).Cast<UTType>().ToArray();
            panel.AllowedContentTypes = types;
            panel.AllowsOtherFileTypes = options.Save || types.Length == 0;
            panel.ValidateVisibleColumns();
        }
        var session = new FilePanelNavigationSession(panel);
        using var accessory = new FilePanelAccessory(options, filters, navigation, ApplyFilter, session.Request, readOnly);
        using var panelDelegate = new FilePanelDelegate(filters, options, navigation, () => accessory.ReadOnlyChecked, session);
        currentDelegate = panelDelegate;
        panel.Delegate = panelDelegate;
        if (!options.Directory)
        {
            ApplyFilter();
        }
        panel.AccessoryView = accessory.View;
        try
        {
            var response = options.Owner == 0 ? (NSModalResponse)(long)panel.RunModal() : RunSheet(panel, options.Owner, session);
            panelDelegate.Failure?.Throw();
            return new PanelResult(response, response == NSModalResponse.OK ? GetPaths(panel) : null,
                panel.DirectoryUrl?.Path, session.TakeRequest(), options.Save ? panel.NameFieldStringValue : name,
                accessory.ReadOnlyChecked, panel.ShowsHiddenFiles);
        }
        finally
        {
            panel.AccessoryView = null;
            panel.Delegate = null!;
        }
    }

    private static string[] GetPaths(NSSavePanel panel) => panel is NSOpenPanel selectedOpen
            ? selectedOpen.Urls.Select(url => url.Path).OfType<string>().ToArray()
            : panel.Url?.Path is { } path ? new[] { path } : Array.Empty<string>();

    private static NSModalResponse RunSheet(NSSavePanel panel, nint handle, FilePanelNavigationSession session)
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
            session.Stop();
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

    private static void ScheduleCancel(NSSavePanel panel, Func<bool>? stillPending = null)
    {
        // Run-loop work can execute inside a nested modal session entered from
        // the main dispatch queue. Another main-queue block cannot run there.
        NSRunLoop.Main.Perform([NSRunLoopMode.Common, NSRunLoopMode.ModalPanel], () =>
        {
            if (stillPending?.Invoke() == false) return;
            if ((nint)panel.Handle != 0 && (panel.IsVisible ||
                NSApplication.SharedApplication.ModalWindow is { } modal && modal.Handle == panel.Handle))
                panel.Cancel(panel);
        });
    }

    private sealed class FilePanelNavigationSession(NSSavePanel panel)
    {
        private string? _requestedDirectory;
        private int _directoryVersion;
        private int _requestVersion;
        private bool _stopped;
        public bool IsNavigationPending => _requestedDirectory != null;

        public void Request(string directory)
        {
            if (_stopped) return;
            _requestedDirectory = directory;
            int version = ++_requestVersion;
            ScheduleCancel(panel, () => version == _requestVersion && _requestedDirectory != null);
        }

        public string? TakeRequest()
        {
            string? requested = _requestedDirectory;
            _requestedDirectory = null;
            _directoryVersion++;
            _requestVersion++;
            return requested;
        }

        public void Stop()
        {
            _stopped = true;
            _requestedDirectory = null;
            _directoryVersion++;
            _requestVersion++;
        }

        public void DirectoryChanged(FileDialogNavigation navigation, string? directory)
        {
            int version = ++_directoryVersion;
            if (_stopped || navigation.Root is not { } root || navigation.Contains(directory)) return;
            NSRunLoop.Main.Perform([NSRunLoopMode.Common, NSRunLoopMode.ModalPanel], () =>
            {
                // A later directory callback or session end invalidates work
                // queued for the previous directory.
                if (version != _directoryVersion || (nint)panel.Handle == 0 || !panel.IsVisible) return;
                Request(root);
            });
        }
    }

    private sealed class FilePanelAccessory : IDisposable
    {
        private readonly PlatformFileDialogOptions _options;
        private readonly List<NSView> _controls = [];
        private readonly NSPopUpButton? _filter;
        private readonly NSPopUpButton? _places;
        private readonly NSButton? _readOnly;
        private readonly EventHandler? _filterChanged;
        private readonly EventHandler? _placeChanged;
        public NSView? View { get; }
        public bool ReadOnlyChecked => _readOnly == null ? _options.ReadOnlyChecked : _readOnly.State == NSCellStateValue.On;

        public FilePanelAccessory(PlatformFileDialogOptions options,
            FileDialogFilterSelection filters, FileDialogNavigation navigation, Action applyFilter, Action<string> navigate, bool readOnly)
        {
            _options = options;
            string[] paths = navigation.Places(options.CustomPlaces);
            int rows = (options.Filters.Length > 1 && !options.Directory ? 1 : 0) +
                (paths.Length > 0 ? 1 : 0) + (!options.Save && !options.Directory && options.ShowReadOnly ? 1 : 0);
            if (rows == 0) return;
            bool standaloneFilter = rows == 1 && options.Filters.Length > 1 && !options.Directory;
            View = standaloneFilter ? null : new NSView(new CoreGraphics.CGRect(0, 0, 300, rows * 34));
            int row = rows;
            CoreGraphics.CGRect NextFrame() => new(0, --row * 34 + 4, 280, 26);
            if (options.Filters.Length > 1 && !options.Directory)
            {
                _filter = new NSPopUpButton(NextFrame(), false);
                ((NSView)_filter).AccessibilityLabel = "文件类型";
                _filter.AddItems(options.Filters.Select(filter => filter.Name).ToArray());
                _filter.SelectItem(filters.Index - 1);
                _filterChanged = (_, _) => { filters.Select((int)_filter.IndexOfSelectedItem + 1); applyFilter(); };
                _filter.Activated += _filterChanged;
                _controls.Add(_filter);
                if (standaloneFilter) { _filter.Frame = new CoreGraphics.CGRect(0, 0, 280, 26); View = _filter; }
                else View!.AddSubview(_filter);
            }
            if (paths.Length > 0)
            {
                _places = new NSPopUpButton(NextFrame(), false);
                ((NSView)_places).AccessibilityLabel = "位置快捷方式";
                string[] labels = paths.Select(path => Path.GetFileName(path) is { Length: > 0 } name ? name : path).ToArray();
                _places.AddItems(new[] { "位置快捷方式" }.Concat(labels.Select((label, index) =>
                    labels.Count(other => other == label) > 1 ? paths[index] : label)).ToArray());
                _placeChanged = (_, _) =>
                {
                    int index = (int)_places.IndexOfSelectedItem - 1;
                    if ((uint)index < paths.Length && Directory.Exists(paths[index]))
                        navigate(paths[index]);
                    _places.SelectItem(0);
                };
                _places.Activated += _placeChanged;
                _controls.Add(_places); View!.AddSubview(_places);
            }
            if (!options.Save && !options.Directory && options.ShowReadOnly)
            {
                _readOnly = new NSButton(NextFrame()) { Title = "只读打开" };
                _readOnly.SetButtonType(NSButtonType.Switch);
                _readOnly.State = readOnly ? NSCellStateValue.On : NSCellStateValue.Off;
                _controls.Add(_readOnly); View!.AddSubview(_readOnly);
            }
        }

        public void Dispose()
        {
            if (_filter != null) _filter.Activated -= _filterChanged;
            if (_places != null) _places.Activated -= _placeChanged;
            foreach (var control in _controls) control.Dispose();
            if (View != _filter) View?.Dispose();
        }
    }

    private sealed class FilePanelDelegate(FileDialogFilterSelection filters, PlatformFileDialogOptions options,
        FileDialogNavigation navigation, Func<bool> readOnlyChecked, FilePanelNavigationSession session) : NSOpenSavePanelDelegate
    {
        private string[]? _openPaths;
        public ExceptionDispatchInfo? Failure { get; private set; }

        public void ResetSelection() => _openPaths = null;
        public override void SelectionDidChange(NSSavePanel panel) => ResetSelection();
        public override void DidChangeToDirectory(NSSavePanel panel, NSUrl newDirectoryUrl)
        {
            ResetSelection();
            session.DirectoryChanged(navigation, newDirectoryUrl?.Path);
        }

        public override string UserEnteredFilename(NSSavePanel panel, string filename, bool confirmed)
            => confirmed ? filters.AppendExtension(filename, options.AddExtension) : filename;

        public override bool ShouldEnableUrl(NSSavePanel panel, NSUrl url)
            => url.IsFileUrl && url.Path is { } path
                ? Directory.Exists(path) ? navigation.Contains(path) || navigation.IsAncestor(path)
                    : navigation.Contains(path) && filters.MatchesFileName(Path.GetFileName(path))
                : navigation.Root == null;

        public override bool ValidateUrl(NSSavePanel panel, NSUrl url, out NSError outError)
        {
            outError = null!;
            if (Failure != null || session.IsNavigationPending || url.Path is not { } path) return false;
            // Cache the native batch while AppKit validates each URL; copying
            // the complete selection on every callback would be quadratic.
            var paths = panel is NSOpenPanel ? _openPaths ??= GetPaths(panel) : [path];
            if (paths.Length == 0) paths = [path];
            if (!navigation.Contains(path) || !paths.All(navigation.Contains))
            {
                ResetSelection();
                outError = new NSError(new NSString("Jalium.FileDialog"), 1,
                    NSDictionary.FromObjectAndKey(new NSString("请选择指定根目录内的文件或文件夹。"), NSError.LocalizedDescriptionKey));
                return false;
            }
            // AppKit validates each selected URL. The last selected path is the
            // single confirmation point, independent of callback ordering.
            if (path != paths[^1]) return true;
            try
            {
                options.ReadOnlyChecked = readOnlyChecked();
                return options.ValidateSelection?.Invoke(paths, filters.Index) ?? true;
            }
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
