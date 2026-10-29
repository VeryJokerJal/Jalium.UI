using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Jalium.UI.Tooling;

public enum CssToolLanguage { Css, Scss, Less }

public sealed record CssToolDiagnostic(string Message, string? FilePath, int Line, int Column);

public sealed record CssToolResult(string? Text, IReadOnlyList<string> Imports,
    string? SourceMap, IReadOnlyList<CssToolDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.Count == 0 && Text is not null;
}

/// <summary>Runs the framework-bundled CSS, Sass, Less and formatting tools.</summary>
public static class CssTooling
{
    public static Task<CssToolResult> CompileAsync(CssToolLanguage language, string sourcePath,
        string text, CancellationToken cancellationToken = default)
        => RunAsync("compile", language, sourcePath, text, cancellationToken);

    public static Task<CssToolResult> FormatAsync(CssToolLanguage language, string sourcePath,
        string text, CancellationToken cancellationToken = default)
        => RunAsync("format", language, sourcePath, text, cancellationToken);

    public static Task<CssToolResult> FormatRangeAsync(CssToolLanguage language, string sourcePath,
        string text, int start, int length, CancellationToken cancellationToken = default)
        => RunAsync("format", language, sourcePath, text, cancellationToken, start, start + length);

    public static string HostRid
    {
        get
        {
            var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" :
                OperatingSystem.IsLinux() ? "linux" : throw new PlatformNotSupportedException("CSS tooling supports Windows, macOS and Linux.");
            var architecture = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException("CSS tooling supports x64 and arm64 hosts."),
            };
            return os + "-" + architecture;
        }
    }

    private static async Task<CssToolResult> RunAsync(string operation, CssToolLanguage language,
        string sourcePath, string text, CancellationToken cancellationToken,
        int? rangeStart = null, int? rangeEnd = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(text);
        var root = Path.Combine(Path.GetDirectoryName(typeof(CssTooling).Assembly.Location)!, "tools", "css-tooling");
        var executable = Path.Combine(root, "node", HostRid, OperatingSystem.IsWindows() ? "node.exe" : "node");
        var worker = Path.Combine(root, "index.cjs");
        if (!File.Exists(executable) || !File.Exists(worker))
            throw new FileNotFoundException($"Framework CSS tooling for {HostRid} is missing at '{root}'.");
        executable = ExecutableNode(executable);

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!,
        };
        start.ArgumentList.Add(worker);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start framework CSS tooling.");
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var request = JsonSerializer.Serialize(new { operation, language = language.ToString().ToLowerInvariant(),
            path = Path.GetFullPath(sourcePath), text, rangeStart, rangeEnd });
        await process.StandardInput.WriteAsync(request).ConfigureAwait(false);
        process.StandardInput.Close();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException($"CSS tooling exited with code {process.ExitCode}: {error}");
        var response = JsonSerializer.Deserialize<WorkerResponse>(output,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("Empty CSS tooling response.");
        return new CssToolResult(response.Text, response.Imports ?? [], response.SourceMap,
            response.Diagnostics ?? []);
    }

    private static string ExecutableNode(string path)
    {
        if (OperatingSystem.IsWindows()) return path;
        const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        const UnixFileMode readable = UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        try
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & execute) == execute) return path;
            File.SetUnixFileMode(path, mode | execute | readable);
            return path;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // Published application directories may be read-only. Run a local copy with
            // executable permissions; this remains offline and uses the bundled binary.
            var directory = Path.Combine(Path.GetTempPath(), "jalium-css-tooling", HostRid);
            Directory.CreateDirectory(directory);
            var copy = Path.Combine(directory, "node-" + new FileInfo(path).Length);
            if (!File.Exists(copy)) File.Copy(path, copy);
            File.SetUnixFileMode(copy, File.GetUnixFileMode(copy) | execute | readable);
            return copy;
        }
    }

    private sealed record WorkerResponse(string? Text, string[]? Imports,
        string? SourceMap, CssToolDiagnostic[]? Diagnostics);
}
