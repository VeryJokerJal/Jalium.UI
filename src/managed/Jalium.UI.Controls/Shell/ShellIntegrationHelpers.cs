using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using Jalium.UI.Media;
using Microsoft.Win32;

namespace Jalium.UI.Controls.Shell;

/// <summary>
/// Provides a way to register or unregister an application for file associations.
/// </summary>
public static class FileRegistrationHelper
{
    public static void SetFileAssociation(string extension, string progId, string description, string? iconPath = null)
    {
        extension = NormalizeExtension(extension);
        ValidateProgId(progId);
        ArgumentException.ThrowIfNullOrEmpty(description);

        if (OperatingSystem.IsWindows())
        {
            SetWindowsFileAssociation(extension, progId, description, iconPath);
            NotifyShellOfChange();
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            SetLinuxFileAssociation(extension, progId, description, iconPath);
            return;
        }

        throw new PlatformNotSupportedException("File association registration is supported on Windows and Linux.");
    }

    public static void RemoveFileAssociation(string extension, string progId)
    {
        extension = NormalizeExtension(extension);
        ValidateProgId(progId);

        if (OperatingSystem.IsWindows())
        {
            RemoveWindowsFileAssociation(extension, progId);
            NotifyShellOfChange();
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            RemoveLinuxFileAssociation(extension, progId);
            return;
        }

        throw new PlatformNotSupportedException("File association registration is supported on Windows and Linux.");
    }

    public static void NotifyShellOfChange()
    {
        if (OperatingSystem.IsWindows())
        {
            SHChangeNotify(0x08000000, 0, nint.Zero, nint.Zero); // SHCNE_ASSOCCHANGED
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            var dataHome = GetLinuxDataHome();
            RunOptionalTool("update-mime-database", Path.Combine(dataHome, "mime"));
            RunOptionalTool("update-desktop-database", Path.Combine(dataHome, "applications"));
        }
    }

    internal static string BuildLinuxDesktopEntry(
        string executablePath,
        string description,
        string mimeType,
        string? iconPath) =>
        BuildLinuxDesktopEntry(executablePath, launchAssemblyPath: null, description, mimeType, iconPath);

    internal static string BuildLinuxDesktopEntry(
        string executablePath,
        string? launchAssemblyPath,
        string description,
        string mimeType,
        string? iconPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(executablePath);
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentException.ThrowIfNullOrEmpty(mimeType);

        var builder = new StringBuilder()
            .AppendLine("[Desktop Entry]")
            .AppendLine("Type=Application")
            .Append("Name=").AppendLine(EscapeDesktopValue(description))
            .Append("Comment=").AppendLine(EscapeDesktopValue(description))
            .Append("Exec=\"").Append(EscapeExecArgument(executablePath)).Append('"');
        if (!string.IsNullOrWhiteSpace(launchAssemblyPath))
        {
            builder.Append(" \"")
                // The caller resolves the path on the target OS. Keeping the
                // builder path-neutral also lets package tooling author Linux
                // desktop entries while running on another host.
                .Append(EscapeExecArgument(launchAssemblyPath))
                .Append('"');
        }
        builder.AppendLine(" %f")
            .Append("MimeType=").Append(mimeType).AppendLine(";")
            .AppendLine("Terminal=false")
            .AppendLine("NoDisplay=false");
        if (!string.IsNullOrWhiteSpace(iconPath))
            builder.Append("Icon=").AppendLine(EscapeDesktopValue(Path.GetFullPath(iconPath)));
        return builder.ToString();
    }

    internal static string BuildLinuxMimePackage(
        string extension,
        string mimeType,
        string description)
    {
        extension = NormalizeExtension(extension);
        var escapedMime = SecurityElement.Escape(mimeType) ?? string.Empty;
        var escapedDescription = SecurityElement.Escape(description) ?? string.Empty;
        var escapedGlob = SecurityElement.Escape($"*{extension}") ?? string.Empty;
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
               "<mime-info xmlns=\"http://www.freedesktop.org/standards/shared-mime-info\">\n" +
               $"  <mime-type type=\"{escapedMime}\">\n" +
               $"    <comment>{escapedDescription}</comment>\n" +
               $"    <glob pattern=\"{escapedGlob}\"/>\n" +
               "  </mime-type>\n" +
               "</mime-info>\n";
    }

    internal static string BuildLinuxMimeType(string progId)
    {
        ValidateProgId(progId);
        var suffix = SanitizeDesktopIdentifier(progId).ToLowerInvariant();
        return $"application/x-{suffix}";
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindowsFileAssociation(
        string extension,
        string progId,
        string description,
        string? iconPath)
    {
        var executable = Environment.ProcessPath ??
            throw new InvalidOperationException("The current executable path is unavailable.");
        using var extensionKey = Registry.CurrentUser.CreateSubKey($"Software\\Classes\\{extension}", writable: true);
        extensionKey.SetValue(string.Empty, progId);

        using var progIdKey = Registry.CurrentUser.CreateSubKey($"Software\\Classes\\{progId}", writable: true);
        progIdKey.SetValue(string.Empty, description);
        using (var iconKey = progIdKey.CreateSubKey("DefaultIcon", writable: true))
            iconKey.SetValue(string.Empty, string.IsNullOrWhiteSpace(iconPath) ? executable : Path.GetFullPath(iconPath));
        using (var commandKey = progIdKey.CreateSubKey("shell\\open\\command", writable: true))
            commandKey.SetValue(string.Empty, $"\"{executable}\" \"%1\"");
    }

    [SupportedOSPlatform("windows")]
    private static void RemoveWindowsFileAssociation(string extension, string progId)
    {
        var ownsExtension = false;
        using (var extensionKey = Registry.CurrentUser.OpenSubKey(
                   $"Software\\Classes\\{extension}", writable: true))
        {
            ownsExtension = string.Equals(
                extensionKey?.GetValue(string.Empty) as string,
                progId,
                StringComparison.OrdinalIgnoreCase);
        }
        if (ownsExtension)
            Registry.CurrentUser.DeleteSubKeyTree($"Software\\Classes\\{extension}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($"Software\\Classes\\{progId}", throwOnMissingSubKey: false);
    }

    private static void SetLinuxFileAssociation(
        string extension,
        string progId,
        string description,
        string? iconPath)
    {
        var (executable, launchAssemblyPath) = ResolveLinuxLaunchCommand();
        var dataHome = GetLinuxDataHome();
        var applicationsDirectory = Path.Combine(dataHome, "applications");
        var packagesDirectory = Path.Combine(dataHome, "mime", "packages");
        Directory.CreateDirectory(applicationsDirectory);
        Directory.CreateDirectory(packagesDirectory);

        var identifier = SanitizeDesktopIdentifier(progId);
        var desktopFileName = $"{identifier}.desktop";
        var mimeType = BuildLinuxMimeType(progId);
        var desktopPath = Path.Combine(applicationsDirectory, desktopFileName);
        var packagePath = Path.Combine(packagesDirectory, $"{identifier}.xml");
        byte[]? previousDesktop = File.Exists(desktopPath) ? File.ReadAllBytes(desktopPath) : null;
        byte[]? previousPackage = File.Exists(packagePath) ? File.ReadAllBytes(packagePath) : null;

        try
        {
            WriteUtf8Atomically(
                desktopPath,
                BuildLinuxDesktopEntry(
                    executable,
                    launchAssemblyPath,
                    description,
                    mimeType,
                    iconPath));
            WriteUtf8Atomically(
                packagePath,
                BuildLinuxMimePackage(extension, mimeType, description));

            RunRequiredTool("update-mime-database", Path.Combine(dataHome, "mime"));
            RunOptionalTool("update-desktop-database", applicationsDirectory);
            RunRequiredTool("xdg-mime", "default", desktopFileName, mimeType);
        }
        catch
        {
            RestoreFile(desktopPath, previousDesktop);
            RestoreFile(packagePath, previousPackage);
            RunOptionalTool("update-mime-database", Path.Combine(dataHome, "mime"));
            RunOptionalTool("update-desktop-database", applicationsDirectory);
            throw;
        }
    }

    private static void RemoveLinuxFileAssociation(string extension, string progId)
    {
        var dataHome = GetLinuxDataHome();
        var identifier = SanitizeDesktopIdentifier(progId);
        var desktopFileName = $"{identifier}.desktop";
        var mimeType = BuildLinuxMimeType(progId);
        DeleteIfExists(Path.Combine(dataHome, "applications", desktopFileName));
        DeleteIfExists(Path.Combine(dataHome, "mime", "packages", $"{identifier}.xml"));

        RemoveMimeAppsAssociation(
            Path.Combine(GetLinuxConfigHome(), "mimeapps.list"),
            mimeType,
            desktopFileName);
        RemoveMimeAppsAssociation(
            Path.Combine(dataHome, "applications", "mimeapps.list"),
            mimeType,
            desktopFileName);
        RunOptionalTool("update-mime-database", Path.Combine(dataHome, "mime"));
        RunOptionalTool("update-desktop-database", Path.Combine(dataHome, "applications"));
    }

    private static void RemoveMimeAppsAssociation(string path, string mimeType, string desktopFileName)
    {
        if (!File.Exists(path))
            return;

        var lines = File.ReadAllLines(path).ToList();
        var changed = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var separator = line.IndexOf('=');
            if (separator <= 0 || !string.Equals(line[..separator].Trim(), mimeType, StringComparison.Ordinal))
                continue;

            var remaining = line[(separator + 1)..]
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.Equals(value, desktopFileName, StringComparison.Ordinal))
                .ToArray();
            if (remaining.Length == 0)
            {
                lines.RemoveAt(index--);
            }
            else
            {
                lines[index] = $"{mimeType}={string.Join(';', remaining)};";
            }
            changed = true;
        }

        if (changed)
            File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        extension = extension.Trim();
        if (!extension.StartsWith('.'))
            extension = "." + extension;
        if (extension.Length < 2 || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            extension.AsSpan(1).Contains('.') ||
            !extension.AsSpan(1).ToString().All(static c => char.IsLetterOrDigit(c) || c is '_' or '-' or '+'))
        {
            throw new ArgumentException("The file extension contains invalid characters.", nameof(extension));
        }
        return extension.ToLowerInvariant();
    }

    private static void ValidateProgId(string progId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(progId);
        if (progId.Length > 200 || progId.Any(static c => !(char.IsLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new ArgumentException("The program identifier contains invalid characters.", nameof(progId));
    }

    private static string SanitizeDesktopIdentifier(string progId) =>
        string.Concat(progId.Select(static c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-'));

    private static string GetLinuxDataHome()
    {
        var configured = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : Path.GetFullPath(configured);
    }

    private static string GetLinuxConfigHome()
    {
        var configured = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            : Path.GetFullPath(configured);
    }

    private static (string Executable, string? LaunchAssemblyPath) ResolveLinuxLaunchCommand()
    {
        var executable = Environment.ProcessPath ??
            throw new InvalidOperationException("The current executable path is unavailable.");
        // For a framework-dependent `dotnet app.dll` launch, .NET exposes the
        // managed entry path as argv[0]. This remains valid under trimming and
        // avoids Assembly.Location, which is empty for single-file apps.
        var entryAssembly = Environment.GetCommandLineArgs().FirstOrDefault();
        var executableName = Path.GetFileNameWithoutExtension(executable);
        if (string.Equals(executableName, "dotnet", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(entryAssembly) &&
            string.Equals(Path.GetExtension(entryAssembly), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            return (executable, Path.GetFullPath(entryAssembly));
        }
        return (executable, null);
    }

    private static string EscapeDesktopValue(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", string.Empty, StringComparison.Ordinal);

    private static string EscapeExecArgument(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("`", "\\`", StringComparison.Ordinal)
        .Replace("$", "\\$", StringComparison.Ordinal)
        // Percent introduces Desktop Entry field codes even inside quotes.
        .Replace("%", "%%", StringComparison.Ordinal);

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void WriteUtf8Atomically(string path, string contents)
    {
        string temporary = path + $".jalium-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporary,
                contents,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            DeleteIfExists(temporary);
        }
    }

    private static void RestoreFile(string path, byte[]? contents)
    {
        if (contents == null)
            DeleteIfExists(path);
        else
            File.WriteAllBytes(path, contents);
    }

    private static void RunRequiredTool(string fileName, params string[] arguments)
    {
        if (!TryRunTool(fileName, arguments, out var error))
            throw new InvalidOperationException(error);
    }

    private static void RunOptionalTool(string fileName, params string[] arguments) =>
        TryRunTool(fileName, arguments, out _);

    private static bool TryRunTool(string fileName, string[] arguments, out string error)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = false
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                error = $"Unable to start '{fileName}'.";
                return false;
            }
            var errorRead = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }
                error = $"'{fileName}' did not finish within 30 seconds.";
                return false;
            }
            var standardError = errorRead.GetAwaiter().GetResult();
            if (process.ExitCode == 0)
            {
                error = string.Empty;
                return true;
            }
            error = $"'{fileName}' exited with code {process.ExitCode}: {standardError.Trim()}";
            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            error = $"Unable to run '{fileName}': {ex.Message}";
            return false;
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}

/// <summary>
/// Provides system-related utilities for shell integration.
/// </summary>
public static class SystemParameters2
{
    public static double HorizontalScrollBarHeight => 17;
    public static double VerticalScrollBarWidth => 17;
    public static double WindowCaptionHeight => 30;
    public static Thickness WindowResizeBorderThickness => new(4);
    public static Thickness WindowNonClientFrameThickness => new(3, 3, 3, 3);
    public static bool IsGlassEnabled => true;
    public static Color WindowGlassColor => Color.FromArgb(255, 100, 149, 237);
    public static SolidColorBrush WindowGlassBrush => new(WindowGlassColor);
    public static Size WindowCaptionButtonSize => new(46, 30);
    public static Size SmallIconSize => new(16, 16);

    public static event EventHandler? IsGlassEnabledChanged;
    public static event EventHandler? WindowGlassColorChanged;

    internal static void NotifyGlassEnabledChanged() =>
        IsGlassEnabledChanged?.Invoke(null, EventArgs.Empty);

    internal static void NotifyWindowGlassColorChanged() =>
        WindowGlassColorChanged?.Invoke(null, EventArgs.Empty);
}
