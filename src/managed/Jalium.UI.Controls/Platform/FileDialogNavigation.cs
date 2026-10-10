namespace Jalium.UI.Controls.Platform;

// AppKit has no navigation-root or additional-place properties. Keep their
// filesystem policy shared by the native panel and managed result validation.
// This guides a chooser; it does not authorize access or provide a sandbox.
internal sealed class FileDialogNavigation(string? rootDirectory)
{
    public string? Root { get; } = ExistingDirectory(rootDirectory);

    public bool Contains(string? path)
    {
        string? candidate = Normalize(path);
        if (candidate == null) return false;
        if (Root == null) return true;
        return candidate == Root || candidate.StartsWith(
            Path.EndsInDirectorySeparator(Root) ? Root : Root + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);
    }

    public bool IsAncestor(string? path)
    {
        string? candidate = Normalize(path);
        return Root != null && candidate != null && (candidate == Root || Root.StartsWith(
            Path.EndsInDirectorySeparator(candidate) ? candidate : candidate + Path.DirectorySeparatorChar,
            StringComparison.Ordinal));
    }

    public string? InitialDirectory(string? initial, string? fileName, string? fallback, string? remembered)
    {
        string? fileDirectory = ExistingDirectory(fileName);
        if (fileDirectory == null && !string.IsNullOrWhiteSpace(fileName))
        {
            try { fileDirectory = ExistingDirectory(Path.GetDirectoryName(fileName)); }
            catch (ArgumentException) { }
        }
        foreach (string? candidate in new[] { initial, fileDirectory, remembered, fallback, Root })
        {
            string? path = ExistingDirectory(candidate);
            if (path != null && Contains(path)) return path;
        }
        return null;
    }

    public string[] Places(IEnumerable<(string? Path, Guid KnownFolder)> places)
    {
        var result = new List<string>();
        if (Root != null) result.Add(Root);
        foreach (var place in places)
        {
            string? path = ExistingDirectory(place.KnownFolder == Guid.Empty ? place.Path : KnownFolder(place.KnownFolder));
            if (path != null && Contains(path) && !result.Contains(path, StringComparer.Ordinal)) result.Add(path);
        }
        return result.ToArray();
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            string full = Path.GetFullPath(path);
            // AppKit resolves directory aliases, including /var -> /private/var,
            // before sending URL callbacks. Compare the same directory identity
            // on both sides, while preserving a file link's own leaf name.
            if (OperatingSystem.IsMacOS()) full = ResolveDirectoryAliases(full);
            return Path.TrimEndingDirectorySeparator(full);
        }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (PathTooLongException) { return null; }
    }

    private static string ResolveDirectoryAliases(string path, int depth = 0)
    {
        if (depth >= 40) return path;
        try
        {
            var suffix = new Stack<string>();
            string existing = path;
            while (!Directory.Exists(existing))
            {
                string? parent = Path.GetDirectoryName(existing);
                if (parent == null || parent == existing) return path;
                suffix.Push(Path.GetFileName(existing));
                existing = parent;
            }
            string resolved = Path.GetPathRoot(existing)!;
            foreach (string component in existing[resolved.Length..].Split(Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries))
            {
                resolved = Path.Combine(resolved, component);
                var directory = new DirectoryInfo(resolved);
                if (directory.LinkTarget != null)
                {
                    string? target = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
                    if (target != null) resolved = ResolveDirectoryAliases(target, depth + 1);
                }
            }
            foreach (string component in suffix) resolved = Path.Combine(resolved, component);
            return resolved;
        }
        catch (IOException) { return path; }
        catch (UnauthorizedAccessException) { return path; }
    }

    private static string? ExistingDirectory(string? path)
    {
        string? normalized = Normalize(path);
        return normalized != null && Directory.Exists(normalized) ? normalized : null;
    }

    private static string? KnownFolder(Guid id) => id.ToString("D").ToUpperInvariant() switch
    {
        "FDD39AD0-238F-46AF-ADB4-6C85480369C7" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "B4BFCC3A-DB2C-424C-B029-7FE99A87C641" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "374DE290-123F-4565-9164-39C4925E467B" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        "4BD8D571-6D19-48D3-BE97-422220080E43" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "33E28130-4E1E-4676-835A-98395C3BC3BB" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "18989B1D-99B5-455B-841C-AB7C74E4DDFC" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Movies"),
        "3EB685DB-65F9-4CF6-A03A-E3EF65729F3D" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "F1B32785-6FBA-4FCF-9D55-7B8E7F157091" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "A77F5D77-2E2B-44C3-A6A2-ABA601054A51" => "/Applications",
        "0AC0837C-BBF8-452A-850D-79D08E667CA7" => "/",
        "D20BEEC4-5CA8-4905-AE3B-BF251EA09B53" => "/Network",
        _ => null
    };
}
