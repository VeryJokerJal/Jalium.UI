using System.IO.Enumeration;

namespace Jalium.UI.MacOS;

// Kept free of AppKit so filter transitions can be tested on every platform.
internal sealed class FileDialogFilterSelection(
    (string Name, string Pattern)[] filters, int initialIndex, string? defaultExtension)
{
    public int Index { get; private set; } = Clamp(initialIndex, filters.Length);
    private static int Clamp(int index, int count) => count == 0 ? Math.Max(1, index) : Math.Clamp(index, 1, count);
    public void Select(int index) => Index = Clamp(index, filters.Length);
    private string[] Patterns => filters.Length == 0 ? [] : filters[Index - 1].Pattern.Split(
        ';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public string[] Extensions
    {
        get
        {
            var patterns = Patterns;
            if (patterns.Any(value => value is "*" or "*.*")) return [];
            return patterns.Where(value => value.StartsWith("*.") && IsExtension(value[2..]))
                .Select(value => value[2..]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public bool MatchesFileName(string fileName)
    {
        var patterns = Patterns;
        return filters.Length == 0 || patterns.Any(pattern => pattern is "*" or "*.*"
            || FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true));
    }

    public string AppendExtension(string fileName, bool addExtension)
    {
        if (!addExtension || string.IsNullOrEmpty(fileName) || Path.HasExtension(fileName)) return fileName;
        var extension = Extensions.FirstOrDefault() ?? defaultExtension?.Trim().TrimStart('.');
        return extension != null && IsExtension(extension)
            ? fileName.TrimEnd('.') + "." + extension : fileName;
    }

    private static bool IsExtension(string extension) => !string.IsNullOrWhiteSpace(extension)
        && extension[0] != '.' && extension[^1] != '.'
        && extension.IndexOfAny(['*', '?', '/', '\\', ':', '\0']) < 0;
}
