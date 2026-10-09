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

        var extensions = GetExtensions(options);
        if (!options.Directory && extensions.Length > 0)
        {
            var types = extensions.Select(extension => UTType.CreateFromExtension(extension))
                .Where(type => type != null).Cast<UTType>().ToArray();
            if (types.Length > 0)
            {
                panel.AllowedContentTypes = types;
                panel.AllowsOtherFileTypes = false;
            }
        }
        if ((long)panel.RunModal() != (long)NSModalResponse.OK) return null;
        var paths = panel is NSOpenPanel selectedOpen
            ? selectedOpen.Urls.Select(url => url.Path).OfType<string>().ToArray()
            : panel.Url?.Path is { } path ? new[] { path } : Array.Empty<string>();
        if (options.Save && options.AddExtension && paths.Length == 1 && string.IsNullOrEmpty(Path.GetExtension(paths[0])))
        {
            var extension = options.DefaultExtension?.TrimStart('.') ?? extensions.FirstOrDefault();
            if (!string.IsNullOrEmpty(extension)) paths[0] += "." + extension;
        }
        return paths;
    }

    private static string[] GetExtensions(PlatformFileDialogOptions options)
    {
        if (options.Filters.Length == 0)
            return string.IsNullOrWhiteSpace(options.DefaultExtension)
                ? [] : [options.DefaultExtension.TrimStart('.')];
        var pattern = options.Filters[Math.Clamp(options.FilterIndex - 1, 0, options.Filters.Length - 1)].Pattern;
        var patterns = pattern.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        // Native panels show all files for a wildcard filter; folder panels ignore file types.
        if (patterns.Any(value => value is "*" or "*.*")) return [];
        return patterns.Where(value => value.StartsWith("*.") && !value[2..].Contains('*') && !value.Contains('?'))
            .Select(value => value[2..]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
