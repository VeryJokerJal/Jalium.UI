using AppKit;
using Foundation;
using Jalium.UI.Controls.Platform;
using UniformTypeIdentifiers;
using System.Runtime.Versioning;

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
        using var panelDelegate = new FilePanelDelegate(filters, options.AddExtension);
        panel.Delegate = panelDelegate;
        using var selector = new NSPopUpButton(new CoreGraphics.CGRect(0, 0, 280, 26), false);
        void ApplyFilter()
        {
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
            if ((long)panel.RunModal() != (long)NSModalResponse.OK) return null;
            options.SelectedFilterIndex = filters.Index;
        }
        finally
        {
            selector.Activated -= changed;
            panel.AccessoryView = null;
            panel.Delegate = null;
        }
        var paths = panel is NSOpenPanel selectedOpen
            ? selectedOpen.Urls.Select(url => url.Path).OfType<string>().ToArray()
            : panel.Url?.Path is { } path ? new[] { path } : Array.Empty<string>();
        return paths;
    }

    private sealed class FilePanelDelegate(FileDialogFilterSelection filters, bool addExtension) : NSOpenSavePanelDelegate
    {
        public override string UserEnteredFilename(NSSavePanel panel, string filename, bool confirmed)
            => confirmed ? filters.AppendExtension(filename, addExtension) : filename;

        public override bool ShouldEnableUrl(NSSavePanel panel, NSUrl url)
            => !url.IsFileUrl || url.Path is not { } path || Directory.Exists(path)
                || filters.MatchesFileName(Path.GetFileName(path));
    }
}
