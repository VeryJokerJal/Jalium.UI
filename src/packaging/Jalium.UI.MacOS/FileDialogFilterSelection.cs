namespace Jalium.UI.MacOS;

// Kept free of AppKit so filter transitions can be tested on every platform.
internal sealed class FileDialogFilterSelection(
    (string Name, string Pattern)[] filters, int initialIndex, string? defaultExtension)
{
    public int Index { get; private set; } = Clamp(initialIndex, filters.Length);
    private static int Clamp(int index, int count) => count == 0 ? Math.Max(1, index) : Math.Clamp(index, 1, count);
    public void Select(int index) => Index = Clamp(index, filters.Length);
    public string[] Extensions
    {
        get
        {
            if (filters.Length == 0)
                return string.IsNullOrWhiteSpace(defaultExtension) ? [] : [defaultExtension.TrimStart('.')];
            var patterns = filters[Index - 1].Pattern.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (patterns.Any(value => value is "*" or "*.*")) return [];
            return patterns.Where(value => value.StartsWith("*.") && !value[2..].Contains('*') && !value.Contains('?'))
                .Select(value => value[2..]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
