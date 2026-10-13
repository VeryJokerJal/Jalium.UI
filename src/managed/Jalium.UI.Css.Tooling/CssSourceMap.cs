using System.Text.Json;

namespace Jalium.UI.Css.Tooling;

public static class CssSourceMap
{
    /// <summary>Maps a generated UTF-16 offset through a version 3 source map.</summary>
    public static bool TryMap(string? json, string generated, int offset, string originalPath, out int line, out int column)
    {
        line = column = 0;
        if (string.IsNullOrWhiteSpace(json) || offset < 0 || offset > generated.Length) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var sources = root.GetProperty("sources").EnumerateArray().Select(s => s.GetString()!).ToArray();
            var targetLine = 0; var targetColumn = 0;
            for (var i = 0; i < offset; i++) { if (generated[i] == '\n') { targetLine++; targetColumn = 0; } else targetColumn++; }
            var source = 0; var sourceLine = 0; var sourceColumn = 0;
            var lines = root.GetProperty("mappings").GetString()!.Split(';');
            for (var row = 0; row <= targetLine && row < lines.Length; row++)
            {
                var generatedColumn = 0;
                foreach (var segment in lines[row].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var position = 0;
                    generatedColumn += Read(segment, ref position);
                    if (position == segment.Length) continue;
                    source += Read(segment, ref position);
                    sourceLine += Read(segment, ref position);
                    sourceColumn += Read(segment, ref position);
                    if (row != targetLine || generatedColumn > targetColumn || source < 0 || source >= sources.Length) continue;
                    var path = sources[source];
                    if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile) path = uri.LocalPath;
                    else if (!Path.IsPathRooted(path)) path = Path.GetFullPath(path, Path.GetDirectoryName(Path.GetFullPath(originalPath))!);
                    if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(originalPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                    line = sourceLine + 1; column = sourceColumn + 1;
                }
            }
            return line > 0;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or IndexOutOfRangeException or KeyNotFoundException) { return false; }
    }

    private static int Read(string value, ref int position)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var result = 0; var shift = 0; int digit;
        do
        {
            digit = alphabet.IndexOf(value[position++]);
            if (digit < 0 || shift > 25) throw new ArgumentException("Invalid source map VLQ.");
            result |= (digit & 31) << shift; shift += 5;
        } while ((digit & 32) != 0);
        return (result & 1) == 1 ? -(result >> 1) : result >> 1;
    }
}
