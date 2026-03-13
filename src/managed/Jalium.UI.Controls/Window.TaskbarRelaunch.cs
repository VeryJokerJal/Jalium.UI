using System.Runtime.InteropServices;
using System.Text;

namespace Jalium.UI.Controls;

public partial class Window
{
    private const ushort VT_LPWSTR = 31;
    private const int MaxAppUserModelIdLength = 128;
    private const string AppUserModelIdPropertyName = "System.AppUserModel.ID";
    private const string RelaunchCommandPropertyName = "System.AppUserModel.RelaunchCommand";
    private const string RelaunchDisplayNamePropertyName = "System.AppUserModel.RelaunchDisplayNameResource";
    private const string RelaunchIconPropertyName = "System.AppUserModel.RelaunchIconResource";
    private static readonly Guid IPropertyStoreGuid = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly object s_taskbarIdentityLock = new();
    private static string? s_processAppUserModelId;

    private void PrepareTaskbarRelaunchIdentity()
    {
        var info = BuildTaskbarRelaunchInfo(Environment.ProcessPath, Environment.GetCommandLineArgs(), Title);
        if (string.IsNullOrWhiteSpace(info.AppUserModelId))
        {
            return;
        }

        EnsureProcessAppUserModelId(info.AppUserModelId);
    }

    private void ApplyTaskbarRelaunchProperties()
    {
        if (Handle == nint.Zero)
        {
            return;
        }

        var info = BuildTaskbarRelaunchInfo(Environment.ProcessPath, Environment.GetCommandLineArgs(), Title);
        if (string.IsNullOrWhiteSpace(info.AppUserModelId) ||
            string.IsNullOrWhiteSpace(info.Command) ||
            string.IsNullOrWhiteSpace(info.DisplayName) ||
            !TryGetWindowPropertyStore(Handle, out var propertyStore))
        {
            return;
        }

        using (propertyStore)
        {
            if (!TrySetTaskbarProperty(propertyStore, AppUserModelIdPropertyName, info.AppUserModelId))
            {
                return;
            }

            var hasRelaunchCommand = TrySetTaskbarProperty(propertyStore, RelaunchCommandPropertyName, info.Command);
            var hasRelaunchDisplayName = TrySetTaskbarProperty(propertyStore, RelaunchDisplayNamePropertyName, info.DisplayName);
            if (!hasRelaunchCommand || !hasRelaunchDisplayName)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(info.IconResource))
            {
                _ = TrySetTaskbarProperty(propertyStore, RelaunchIconPropertyName, info.IconResource);
            }

            _ = propertyStore.Commit();
        }
    }

    private static void EnsureProcessAppUserModelId(string appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            return;
        }

        lock (s_taskbarIdentityLock)
        {
            if (string.Equals(s_processAppUserModelId, appUserModelId, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(s_processAppUserModelId))
            {
                return;
            }

            if (SetCurrentProcessExplicitAppUserModelID(appUserModelId) >= 0)
            {
                s_processAppUserModelId = appUserModelId;
            }
        }
    }

    internal static TaskbarRelaunchInfo BuildTaskbarRelaunchInfo(
        string? processPath,
        IReadOnlyList<string>? commandLineArgs,
        string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return default;
        }

        var launcherPath = NormalizeLaunchPath(processPath)!;
        var entryPath = ResolveEntryPointPath(launcherPath, commandLineArgs);
        var displayNameSource = entryPath ?? launcherPath;
        var displayName = BuildTaskbarDisplayName(displayNameSource, windowTitle);

        List<string> parts =
        [
            QuoteCommandLineArgument(launcherPath)
        ];

        if (!string.IsNullOrWhiteSpace(entryPath))
        {
            parts.Add(QuoteCommandLineArgument(entryPath));
        }

        if (commandLineArgs != null)
        {
            for (int i = 1; i < commandLineArgs.Count; i++)
            {
                parts.Add(QuoteCommandLineArgument(commandLineArgs[i] ?? string.Empty));
            }
        }

        return new TaskbarRelaunchInfo(
            BuildTaskbarAppUserModelId(displayName),
            string.Join(" ", parts),
            displayName,
            BuildTaskbarIconResource(displayNameSource));
    }

    private static bool TryGetWindowPropertyStore(nint hwnd, out PropertyStoreHandle propertyStore)
    {
        propertyStore = default;
        if (hwnd == nint.Zero)
        {
            return false;
        }

        var propertyStoreGuid = IPropertyStoreGuid;
        if (SHGetPropertyStoreForWindow(hwnd, ref propertyStoreGuid, out var propertyStorePointer) < 0 ||
            propertyStorePointer == nint.Zero)
        {
            return false;
        }

        propertyStore = new PropertyStoreHandle(propertyStorePointer);
        return true;
    }

    private static bool TrySetTaskbarProperty(PropertyStoreHandle propertyStore, string propertyName, string value)
    {
        if (propertyStore.IsInvalid ||
            string.IsNullOrWhiteSpace(propertyName) ||
            string.IsNullOrWhiteSpace(value) ||
            PSGetPropertyKeyFromName(propertyName, out var propertyKey) < 0)
        {
            return false;
        }

        var propVariant = PROPVARIANT.FromString(value);
        try
        {
            return propertyStore.SetValue(ref propertyKey, ref propVariant) >= 0;
        }
        finally
        {
            _ = PropVariantClear(ref propVariant);
        }
    }

    private static string BuildTaskbarDisplayName(string path, string? windowTitle)
    {
        var fileName = string.Empty;
        try
        {
            fileName = Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            return windowTitle;
        }

        return "Application";
    }

    private static string BuildTaskbarAppUserModelId(string displayName)
    {
        StringBuilder builder = new(Math.Min(displayName.Length + 4, MaxAppUserModelIdLength));
        builder.Append("App.");

        var previousWasSeparator = true;
        foreach (var c in displayName)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append('.');
                previousWasSeparator = true;
            }

            if (builder.Length >= MaxAppUserModelIdLength)
            {
                break;
            }
        }

        while (builder.Length > 4 && builder[^1] == '.')
        {
            builder.Length--;
        }

        if (builder.Length == 4)
        {
            builder.Append("Application");
        }

        return builder.ToString();
    }

    private static string? BuildTaskbarIconResource(string displayNameSource)
    {
        if (HasExecutableExtension(displayNameSource))
        {
            return $"{displayNameSource},0";
        }

        return null;
    }

    private static string? ResolveEntryPointPath(string launcherPath, IReadOnlyList<string>? commandLineArgs)
    {
        if (commandLineArgs == null || commandLineArgs.Count == 0 || string.IsNullOrWhiteSpace(commandLineArgs[0]))
        {
            return null;
        }

        var candidate = NormalizeLaunchPath(commandLineArgs[0]);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (PathsEqual(candidate, launcherPath))
        {
            return null;
        }

        return HasExecutableExtension(candidate) || candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }

    private static string? NormalizeLaunchPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExecutableExtension(string path)
    {
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    internal static string QuoteCommandLineArgument(string argument)
    {
        argument ??= string.Empty;
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        var requiresQuotes = false;
        foreach (var c in argument)
        {
            if (char.IsWhiteSpace(c) || c == '"')
            {
                requiresQuotes = true;
                break;
            }
        }

        if (!requiresQuotes)
        {
            return argument;
        }

        StringBuilder builder = new(argument.Length + 2);
        builder.Append('"');

        var backslashCount = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashCount++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', (backslashCount * 2) + 1);
                builder.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount);
                backslashCount = 0;
            }

            builder.Append(c);
        }

        if (backslashCount > 0)
        {
            builder.Append('\\', backslashCount * 2);
        }

        builder.Append('"');
        return builder.ToString();
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetPropertyStoreForWindow(nint hwnd, ref Guid riid, out nint ppv);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appID);

    [LibraryImport("propsys.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PSGetPropertyKeyFromName(string pszCanonicalName, out PROPERTYKEY propkey);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PROPVARIANT pvar);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)]
        public ushort vt;

        [FieldOffset(2)]
        public ushort wReserved1;

        [FieldOffset(4)]
        public ushort wReserved2;

        [FieldOffset(6)]
        public ushort wReserved3;

        [FieldOffset(8)]
        public nint pszVal;

        public static PROPVARIANT FromString(string value)
        {
            return new PROPVARIANT
            {
                vt = VT_LPWSTR,
                pszVal = Marshal.StringToCoTaskMemUni(value)
            };
        }
    }

    private readonly unsafe struct PropertyStoreHandle : IDisposable
    {
        private readonly nint _instance;

        public PropertyStoreHandle(nint instance)
        {
            _instance = instance;
        }

        public bool IsInvalid => _instance == nint.Zero;

        public int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value)
        {
            var vtable = *(nint**)_instance;
            var setValue = (delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, PROPVARIANT*, int>)vtable[6];
            fixed (PROPERTYKEY* keyPtr = &key)
            fixed (PROPVARIANT* valuePtr = &value)
            {
                return setValue(_instance, keyPtr, valuePtr);
            }
        }

        public int Commit()
        {
            var vtable = *(nint**)_instance;
            var commit = (delegate* unmanaged[Stdcall]<nint, int>)vtable[7];
            return commit(_instance);
        }

        public void Dispose()
        {
            if (_instance == nint.Zero)
            {
                return;
            }

            var vtable = *(nint**)_instance;
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)vtable[2];
            _ = release(_instance);
        }
    }
}

internal readonly record struct TaskbarRelaunchInfo(
    string AppUserModelId,
    string Command,
    string DisplayName,
    string? IconResource);
