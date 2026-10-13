using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jalium.UI.Css.Tooling;

public enum CssToolLanguage { Css, Scss, Less }
public sealed record CssToolDiagnostic(string? FilePath, string Message, int Line, int Column);
public sealed record CssToolResult(bool Success, string? Text, string? SourceMap, CssToolDiagnostic[] Diagnostics);
internal sealed record CssToolRequest(string Operation, string Language, string FilePath, string Source, int Start, int End);

/// <summary>Runs mature CSS processors in a cancellable helper process; project text stays on stdin.</summary>
public static class CssTooling
{
    public static Task<CssToolResult> CompileAsync(CssToolLanguage language, string filePath, string source, CancellationToken ct = default)
        => RunAsync(new("compile", language.ToString().ToLowerInvariant(), filePath, source, 0, source.Length), ct);
    public static Task<CssToolResult> FormatAsync(CssToolLanguage language, string filePath, string source, CancellationToken ct = default)
        => FormatRangeAsync(language, filePath, source, 0, source.Length, ct);
    public static Task<CssToolResult> FormatRangeAsync(CssToolLanguage language, string filePath, string source, int start, int length, CancellationToken ct = default)
    {
        if (start < 0 || length < 0 || start > source.Length - length) throw new ArgumentOutOfRangeException(nameof(start));
        return RunAsync(new("format", language.ToString().ToLowerInvariant(), filePath, source, start, start + length), ct);
    }

    private static async Task<CssToolResult> RunAsync(CssToolRequest request, CancellationToken ct)
    {
        if (request.Source.Length > 8 * 1024 * 1024) throw new IOException("CSS 输入超过 8 MB。");
        var directory = Path.Combine(AppContext.BaseDirectory, "css-tools");
        var node = Environment.GetEnvironmentVariable("JALIUM_CSS_NODE");
        if (string.IsNullOrWhiteSpace(node)) node = new[] { Path.Combine(directory, "node"), "/opt/homebrew/bin/node", "/usr/local/bin/node" }
            .FirstOrDefault(File.Exists) ?? "node";
        var start = new ProcessStartInfo(node) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(directory, "css-tool.cjs"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
            var output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var errors = ReadBoundedAsync(process.StandardError, timeout.Token);
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request, CssToolJsonContext.Default.CssToolRequest).AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var result = await output;
            var stderr = await errors;
            return process.ExitCode == 0
                ? JsonSerializer.Deserialize(result, CssToolJsonContext.Default.CssToolResult) ?? Failure("CSS 工具没有返回结果。")
                : Failure(stderr);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or JsonException)
        { return Failure(ex.Message); }
        finally
        { try { if (process.Id != 0 && !process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
    }

    private static CssToolResult Failure(string message) => new(false, null, null, [new(null, message, 1, 1)]);
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var result = new System.Text.StringBuilder();
        var buffer = new char[8192];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (result.Length + count > 16 * 1024 * 1024) throw new IOException("CSS 工具输出超过 16 MB。");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CssToolRequest))]
[JsonSerializable(typeof(CssToolResult))]
internal partial class CssToolJsonContext : JsonSerializerContext;
