using System.Runtime.InteropServices;
using System.Text;

namespace Jalium.UI.Controls;

/// <summary>
/// Displays the Win32 icon picker dialog.
/// </summary>
public sealed class PickIconDialog
{
    private const int DefaultBufferLength = 260;

    /// <summary>
    /// Gets or sets the icon resource path.
    /// </summary>
    public string IconPath { get; set; } = Environment.SystemDirectory + @"\shell32.dll";

    /// <summary>
    /// Gets or sets the selected icon index.
    /// </summary>
    public int IconIndex { get; set; }

    /// <summary>
    /// Shows the dialog.
    /// </summary>
    public bool ShowDialog()
    {
        return ShowDialog(DialogOwnerResolver.Resolve());
    }

    /// <summary>
    /// Shows the dialog with the specified owner window.
    /// </summary>
    public bool ShowDialog(IntPtr owner)
    {
        owner = DialogOwnerResolver.Resolve(owner);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ShowWindowsDialog(owner);
        }

        return ShowFallbackDialog();
    }

    private bool ShowWindowsDialog(IntPtr owner)
    {
        var buffer = new StringBuilder(string.IsNullOrWhiteSpace(IconPath) ? Environment.SystemDirectory + @"\shell32.dll" : IconPath, DefaultBufferLength);
        if (buffer.Capacity < DefaultBufferLength)
        {
            buffer.EnsureCapacity(DefaultBufferLength);
        }

        var iconIndex = IconIndex;
        if (PickIconDlg(owner, buffer, buffer.Capacity, ref iconIndex) == 0)
        {
            return false;
        }

        IconPath = buffer.ToString();
        IconIndex = iconIndex;
        return true;
    }

    private bool ShowFallbackDialog()
    {
        Console.Write($"Icon resource path [{IconPath}]: ");
        var path = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(path))
        {
            IconPath = path.Trim();
        }

        Console.Write($"Icon index [{IconIndex}]: ");
        var indexInput = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(indexInput) && int.TryParse(indexInput, out var parsedIndex))
        {
            IconIndex = parsedIndex;
        }

        return !string.IsNullOrWhiteSpace(IconPath);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int PickIconDlg(IntPtr hwnd, StringBuilder pszIconPath, int cchIconPath, ref int piIconIndex);
}

/// <summary>
/// Flags that control the Open With dialog.
/// </summary>
[Flags]
public enum OpenWithDialogFlags : uint
{
    /// <summary>
    /// No special behavior.
    /// </summary>
    None = 0,

    /// <summary>
    /// Enable the "always use this program" option.
    /// </summary>
    AllowRegistration = 0x00000001,

    /// <summary>
    /// Register the selected handler after the user confirms.
    /// </summary>
    RegisterExtension = 0x00000002,

    /// <summary>
    /// Execute the file after the user chooses an application.
    /// </summary>
    Execute = 0x00000004,

    /// <summary>
    /// Force the "always use this program" option to be checked.
    /// </summary>
    ForceRegistration = 0x00000008,

    /// <summary>
    /// Hide the registration option.
    /// </summary>
    HideRegistration = 0x00000020,

    /// <summary>
    /// Treat the class value as a URI protocol instead of a file extension.
    /// </summary>
    UrlProtocol = 0x00000040,

    /// <summary>
    /// Indicates that the file path is a URI.
    /// </summary>
    FileIsUri = 0x00000080
}

/// <summary>
/// Displays the Win32 Open With dialog.
/// </summary>
public sealed class OpenWithDialog
{
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    /// <summary>
    /// Gets or sets the file path to open with another application.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Gets or sets the file class or protocol name.
    /// </summary>
    public string? ClassName { get; set; }

    /// <summary>
    /// Gets or sets the dialog behavior flags.
    /// </summary>
    public OpenWithDialogFlags Flags { get; set; } = OpenWithDialogFlags.Execute;

    /// <summary>
    /// Shows the dialog.
    /// </summary>
    public bool ShowDialog()
    {
        return ShowDialog(DialogOwnerResolver.Resolve());
    }

    /// <summary>
    /// Shows the dialog with the specified owner window.
    /// </summary>
    public bool ShowDialog(IntPtr owner)
    {
        owner = DialogOwnerResolver.Resolve(owner);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ShowWindowsDialog(owner);
        }

        return ShowFallbackDialog();
    }

    private bool ShowWindowsDialog(IntPtr owner)
    {
        if (string.IsNullOrWhiteSpace(FileName))
        {
            throw new InvalidOperationException("FileName must be set before showing the dialog.");
        }

        var info = new OPENASINFO
        {
            pcszFile = FileName,
            pcszClass = ClassName,
            oaifInFlags = Flags
        };

        var hr = SHOpenWithDialog(owner, in info);
        if (hr == 0)
        {
            return true;
        }

        if (hr == ErrorCancelled)
        {
            return false;
        }

        Marshal.ThrowExceptionForHR(hr);
        return false;
    }

    private bool ShowFallbackDialog()
    {
        Console.WriteLine($"Open With is only implemented on Windows. Target: {FileName ?? "(none)"}");
        return false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENASINFO
    {
        public string pcszFile;
        public string? pcszClass;
        public OpenWithDialogFlags oaifInFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(IntPtr hwndParent, in OPENASINFO poainfo);
}

/// <summary>
/// Object types supported by the properties dialog.
/// </summary>
public enum OpenPropertiesObjectType : uint
{
    /// <summary>
    /// A printer friendly name.
    /// </summary>
    PrinterName = 0x00000001,

    /// <summary>
    /// A fully qualified file system path.
    /// </summary>
    FilePath = 0x00000002,

    /// <summary>
    /// A volume GUID path or drive root.
    /// </summary>
    VolumeGuid = 0x00000004
}

/// <summary>
/// Displays a Win32 properties dialog for shell objects.
/// </summary>
public sealed class OpenPropertiesDialog
{
    /// <summary>
    /// Gets or sets the object type to inspect.
    /// </summary>
    public OpenPropertiesObjectType ObjectType { get; set; } = OpenPropertiesObjectType.FilePath;

    /// <summary>
    /// Gets or sets the single object name.
    /// </summary>
    public string? ObjectName { get; set; }

    /// <summary>
    /// Gets or sets the object names for multi-file property sheets.
    /// </summary>
    public string[] ObjectNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the initial property page name.
    /// </summary>
    public string? PropertyPage { get; set; }

    /// <summary>
    /// Shows the dialog.
    /// </summary>
    public bool ShowDialog()
    {
        return ShowDialog(DialogOwnerResolver.Resolve());
    }

    /// <summary>
    /// Shows the dialog with the specified owner window.
    /// </summary>
    public bool ShowDialog(IntPtr owner)
    {
        owner = DialogOwnerResolver.Resolve(owner);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ShowWindowsDialog(owner);
        }

        return ShowFallbackDialog();
    }

    private bool ShowWindowsDialog(IntPtr owner)
    {
        if (ObjectType != OpenPropertiesObjectType.FilePath)
        {
            if (string.IsNullOrWhiteSpace(ObjectName))
            {
                throw new InvalidOperationException("ObjectName must be set before showing the dialog.");
            }

            return SHObjectProperties(owner, (uint)ObjectType, ObjectName, PropertyPage);
        }

        var paths = GetNormalizedPaths();
        return paths.Length switch
        {
            0 => throw new InvalidOperationException("ObjectName or ObjectNames must be set before showing the dialog."),
            1 => SHObjectProperties(owner, (uint)OpenPropertiesObjectType.FilePath, paths[0], PropertyPage),
            _ => ShowWindowsMultiFileDialog(paths)
        };
    }

    private bool ShowWindowsMultiFileDialog(string[] paths)
    {
        using var shellDataObject = CreateShellDataObject(paths);
        var hr = SHMultiFileProperties(shellDataObject.Handle, 0);
        if (hr != 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        return true;
    }

    private bool ShowFallbackDialog()
    {
        Console.WriteLine($"Properties dialog is only implemented on Windows. Target: {ObjectName ?? string.Join(", ", ObjectNames)}");
        return false;
    }

    private string[] GetNormalizedPaths()
    {
        if (ObjectNames.Length > 0)
        {
            return ObjectNames
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return string.IsNullOrWhiteSpace(ObjectName) ? Array.Empty<string>() : [Path.GetFullPath(ObjectName)];
    }

    private static ShellDataObjectHandle CreateShellDataObject(string[] paths)
    {
        var normalizedPaths = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedPaths.Length == 0)
        {
            throw new InvalidOperationException("At least one file path is required.");
        }

        var parentDirectory = Path.GetDirectoryName(normalizedPaths[0]);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new InvalidOperationException("The target path must have a parent directory.");
        }

        if (normalizedPaths.Any(path => !string.Equals(Path.GetDirectoryName(path), parentDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            throw new NotSupportedException("SHMultiFileProperties requires all target files to come from the same directory.");
        }

        var parentPidl = ILCreateFromPath(parentDirectory);
        if (parentPidl == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Unable to resolve a shell PIDL for '{parentDirectory}'.");
        }

        var absolutePidls = new List<IntPtr>(normalizedPaths.Length);
        var childPidls = new List<IntPtr>(normalizedPaths.Length);

        try
        {
            foreach (var path in normalizedPaths)
            {
                var absolutePidl = ILCreateFromPath(path);
                if (absolutePidl == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"Unable to resolve a shell PIDL for '{path}'.");
                }

                absolutePidls.Add(absolutePidl);

                var childPidl = ILClone(ILFindLastID(absolutePidl));
                if (childPidl == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"Unable to clone a child shell PIDL for '{path}'.");
                }

                childPidls.Add(childPidl);
            }

            var apidl = childPidls.ToArray();
            var iidIDataObject = typeof(System.Runtime.InteropServices.ComTypes.IDataObject).GUID;
            var hr = SHCreateDataObject(parentPidl, (uint)apidl.Length, apidl, IntPtr.Zero, in iidIDataObject, out var dataObject);
            if (hr != 0 || dataObject == IntPtr.Zero)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            return new ShellDataObjectHandle(dataObject);
        }
        finally
        {
            foreach (var childPidl in childPidls)
            {
                Marshal.FreeCoTaskMem(childPidl);
            }

            foreach (var absolutePidl in absolutePidls)
            {
                Marshal.FreeCoTaskMem(absolutePidl);
            }

            Marshal.FreeCoTaskMem(parentPidl);
        }
    }

    private sealed class ShellDataObjectHandle : IDisposable
    {
        public ShellDataObjectHandle(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                Marshal.Release(Handle);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHObjectProperties(IntPtr hwnd, uint shopObjectType, string pszObjectName, string? pszPropertyPage);

    [DllImport("shell32.dll")]
    private static extern int SHMultiFileProperties(IntPtr pdtobj, uint dwFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ILCreateFromPath(string pszPath);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILFindLastID(IntPtr pidl);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILClone(IntPtr pidl);

    [DllImport("shell32.dll")]
    private static extern int SHCreateDataObject(
        IntPtr pidlFolder,
        uint cidl,
        [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
        IntPtr pdtInner,
        in Guid riid,
        out IntPtr ppv);
}
