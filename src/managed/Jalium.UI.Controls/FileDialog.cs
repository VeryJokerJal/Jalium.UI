using System.Runtime.InteropServices;

namespace Jalium.UI.Controls;

/// <summary>
/// Base class for file dialogs.
/// </summary>
public abstract class FileDialog
{
    #region Properties

    /// <summary>
    /// Gets or sets the file dialog title.
    /// </summary>
    /// <remarks>
    /// Leave this value <see langword="null"/> to let the platform use its native default caption.
    /// </remarks>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the initial directory.
    /// </summary>
    public string? InitialDirectory { get; set; }

    /// <summary>
    /// Gets or sets the default file extension.
    /// </summary>
    public string? DefaultExt { get; set; }

    /// <summary>
    /// Gets or sets the filter string.
    /// </summary>
    /// <remarks>
    /// Format: "Description|Pattern|Description|Pattern"
    /// Example: "Text files (*.txt)|*.txt|All files (*.*)|*.*"
    /// </remarks>
    public string? Filter { get; set; }

    /// <summary>
    /// Gets or sets the selected filter index (1-based).
    /// </summary>
    public int FilterIndex { get; set; } = 1;

    /// <summary>
    /// Gets or sets the selected file name (full path).
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Gets the selected file names (full paths).
    /// </summary>
    public string[] FileNames { get; protected set; } = Array.Empty<string>();

    /// <summary>
    /// Gets the safe file name (without path).
    /// </summary>
    public string SafeFileName => string.IsNullOrEmpty(FileName) ? string.Empty : Path.GetFileName(FileName);

    /// <summary>
    /// Gets the safe file names (without paths).
    /// </summary>
    public string[] SafeFileNames => FileNames.Select(p => Path.GetFileName(p) ?? string.Empty).ToArray();

    /// <summary>
    /// Gets or sets whether to check if the file exists.
    /// </summary>
    public bool CheckFileExists { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to check if the path exists.
    /// </summary>
    public bool CheckPathExists { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to add extension automatically.
    /// </summary>
    public bool AddExtension { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to validate names.
    /// </summary>
    public bool ValidateNames { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to dereference links.
    /// </summary>
    public bool DereferenceLinks { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to restore directory after dialog closes.
    /// </summary>
    public bool RestoreDirectory { get; set; }

    /// <summary>
    /// Gets or sets custom places to show in the dialog.
    /// </summary>
    public IList<FileDialogCustomPlace> CustomPlaces { get; } = new List<FileDialogCustomPlace>();

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the user clicks OK.
    /// </summary>
    public event EventHandler? FileOk;

    #endregion

    #region Methods

    /// <summary>
    /// Shows the file dialog.
    /// </summary>
    /// <returns>True if the user selected a file, false if canceled.</returns>
    public bool? ShowDialog()
    {
        return ShowDialog(DialogOwnerResolver.Resolve());
    }

    /// <summary>
    /// Shows the file dialog with the specified owner window.
    /// </summary>
    /// <param name="owner">The owner window handle.</param>
    /// <returns>True if the user selected a file, false if canceled.</returns>
    public abstract bool? ShowDialog(IntPtr owner);

    /// <summary>
    /// Resets the dialog to its default state.
    /// </summary>
    public virtual void Reset()
    {
        Title = null;
        InitialDirectory = null;
        DefaultExt = null;
        Filter = null;
        FilterIndex = 1;
        FileName = null;
        FileNames = Array.Empty<string>();
        CheckFileExists = true;
        CheckPathExists = true;
        AddExtension = true;
        ValidateNames = true;
        DereferenceLinks = true;
        RestoreDirectory = false;
        CustomPlaces.Clear();
    }

    /// <summary>
    /// Raises the FileOk event.
    /// </summary>
    protected virtual void OnFileOk()
    {
        FileOk?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Parses the filter string into filter specifications.
    /// </summary>
    protected (string Name, string Pattern)[] ParseFilter()
    {
        if (string.IsNullOrEmpty(Filter))
            return Array.Empty<(string, string)>();

        var parts = Filter.Split('|');
        var result = new List<(string, string)>(parts.Length);

        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            result.Add((parts[i], parts[i + 1]));
        }

        return result.ToArray();
    }

    #endregion
}

/// <summary>
/// Represents an open file dialog.
/// </summary>
public sealed class OpenFileDialog : FileDialog
{
    #region Properties

    /// <summary>
    /// Gets or sets whether the dialog should pick folders instead of files.
    /// </summary>
    public bool IsFolderPicker { get; set; }

    /// <summary>
    /// Gets or sets whether multiple files can be selected.
    /// </summary>
    public bool Multiselect { get; set; }

    /// <summary>
    /// Gets or sets whether read-only files can be selected.
    /// </summary>
    public bool ShowReadOnly { get; set; }

    /// <summary>
    /// Gets or sets the read-only checked state.
    /// </summary>
    public bool ReadOnlyChecked { get; set; }

    #endregion

    /// <inheritdoc />
    public override bool? ShowDialog(IntPtr owner)
    {
        owner = DialogOwnerResolver.Resolve(owner);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ShowWindowsDialog(owner);
        }

        // Fallback for non-Windows platforms
        return ShowFallbackDialog();
    }

    private bool? ShowWindowsDialog(IntPtr owner)
    {
        IFileOpenDialog? dialog = null;
        IShellItem? initialDirectoryItem = null;
        var customPlaceItems = new List<IShellItem>();

        try
        {
            dialog = (IFileOpenDialog)new FileOpenDialogCom();

            if (!string.IsNullOrWhiteSpace(Title))
            {
                CheckHResult(dialog.SetTitle(Title));
            }

            if (!string.IsNullOrWhiteSpace(DefaultExt))
            {
                CheckHResult(dialog.SetDefaultExtension(DefaultExt));
            }

            var filters = BuildFilterSpecs();
            if (filters.Length > 0)
            {
                CheckHResult(dialog.SetFileTypes((uint)filters.Length, filters));
                CheckHResult(dialog.SetFileTypeIndex((uint)Math.Max(FilterIndex, 1)));
            }

            var dialogOptions = BuildDialogOptions();
            CheckHResult(dialog.SetOptions(dialogOptions));

            var initialPath = GetInitialDialogPath();
            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                initialDirectoryItem = CreateShellItemFromPath(initialPath);
                CheckHResult(dialog.SetFolder(initialDirectoryItem));
                CheckHResult(dialog.SetDefaultFolder(initialDirectoryItem));
            }

            foreach (var customPlace in CustomPlaces)
            {
                var customPlaceItem = TryCreateCustomPlaceShellItem(customPlace);
                if (customPlaceItem == null)
                {
                    continue;
                }

                customPlaceItems.Add(customPlaceItem);
                CheckHResult(dialog.AddPlace(customPlaceItem, FileDialogAddPlace.Bottom));
            }

            if (!string.IsNullOrWhiteSpace(FileName))
            {
                CheckHResult(dialog.SetFileName(FileName));
            }

            var showResult = dialog.Show(owner);
            if (showResult == HResultErrorCancelled)
            {
                return false;
            }

            CheckHResult(showResult);

            if (Multiselect)
            {
                CheckHResult(dialog.GetResults(out var results));
                try
                {
                    FileNames = GetShellItemArrayPaths(results);
                    FileName = FileNames.FirstOrDefault();
                }
                finally
                {
                    ReleaseComObject(results);
                }
            }
            else
            {
                CheckHResult(dialog.GetResult(out var result));
                try
                {
                    FileName = GetShellItemPath(result);
                    FileNames = string.IsNullOrWhiteSpace(FileName) ? Array.Empty<string>() : [FileName];
                }
                finally
                {
                    ReleaseComObject(result);
                }
            }

            if (filters.Length > 0)
            {
                CheckHResult(dialog.GetFileTypeIndex(out var selectedFilterIndex));
                FilterIndex = (int)selectedFilterIndex;
            }

            OnFileOk();
            return true;
        }
        finally
        {
            foreach (var customPlaceItem in customPlaceItems)
            {
                ReleaseComObject(customPlaceItem);
            }

            ReleaseComObject(initialDirectoryItem);
            ReleaseComObject(dialog);
        }
    }

    private string? GetInitialDialogPath()
    {
        var preferredPath = !string.IsNullOrWhiteSpace(FileName)
            ? FileName
            : InitialDirectory;

        if (string.IsNullOrWhiteSpace(preferredPath))
        {
            return null;
        }

        if (!IsFolderPicker && !Directory.Exists(preferredPath) && File.Exists(preferredPath))
        {
            return Path.GetDirectoryName(preferredPath);
        }

        return preferredPath;
    }

    private bool? ShowFallbackDialog()
    {
        // Simple console-based fallback
        Console.WriteLine($"Open File Dialog: {Title ?? (IsFolderPicker ? "Select Folder" : "Open")}");
        Console.Write(IsFolderPicker ? "Enter folder path: " : "Enter file path: ");
        var path = Console.ReadLine();

        if (!string.IsNullOrEmpty(path))
        {
            if (IsFolderPicker)
            {
                if (CheckPathExists && !Directory.Exists(path))
                {
                    Console.WriteLine("Directory does not exist.");
                    return false;
                }
            }
            else if (CheckFileExists && !File.Exists(path))
            {
                Console.WriteLine("File does not exist.");
                return false;
            }

            FileName = path;
            FileNames = new[] { path };
            OnFileOk();
            return true;
        }

        return false;
    }

    #region Native Methods

    private const int HResultErrorCancelled = unchecked((int)0x800704C7);
    private const uint FOS_OVERWRITEPROMPT = 0x00000002;
    private const uint FOS_STRICTFILETYPES = 0x00000004;
    private const uint FOS_NOCHANGEDIR = 0x00000008;
    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_ALLNONSTORAGEITEMS = 0x00000080;
    private const uint FOS_NOVALIDATE = 0x00000100;
    private const uint FOS_ALLOWMULTISELECT = 0x00000200;
    private const uint FOS_PATHMUSTEXIST = 0x00000800;
    private const uint FOS_FILEMUSTEXIST = 0x00001000;
    private const uint FOS_CREATEPROMPT = 0x00002000;
    private const uint FOS_NODEREFERENCELINKS = 0x00100000;

    private uint BuildDialogOptions()
    {
        var options = FOS_FORCEFILESYSTEM;
        if (IsFolderPicker) options |= FOS_PICKFOLDERS;
        if (Multiselect) options |= FOS_ALLOWMULTISELECT;
        if (CheckPathExists) options |= FOS_PATHMUSTEXIST;
        if (CheckFileExists && !IsFolderPicker) options |= FOS_FILEMUSTEXIST;
        if (!ValidateNames) options |= FOS_NOVALIDATE;
        if (!DereferenceLinks) options |= FOS_NODEREFERENCELINKS;
        if (RestoreDirectory) options |= FOS_NOCHANGEDIR;
        return options;
    }

    private COMDLG_FILTERSPEC[] BuildFilterSpecs()
    {
        var parsedFilters = ParseFilter();
        if (parsedFilters.Length == 0 || IsFolderPicker)
        {
            return Array.Empty<COMDLG_FILTERSPEC>();
        }

        return parsedFilters
            .Select(filter => new COMDLG_FILTERSPEC
            {
                pszName = filter.Name,
                pszSpec = filter.Pattern
            })
            .ToArray();
    }

    private static string[] GetShellItemArrayPaths(IShellItemArray shellItemArray)
    {
        CheckHResult(shellItemArray.GetCount(out var count));
        var paths = new string[(int)count];
        for (uint i = 0; i < count; i++)
        {
            CheckHResult(shellItemArray.GetItemAt(i, out var shellItem));
            try
            {
                paths[i] = GetShellItemPath(shellItem);
            }
            finally
            {
                ReleaseComObject(shellItem);
            }
        }

        return paths.Where(static path => !string.IsNullOrWhiteSpace(path)).ToArray();
    }

    private static string GetShellItemPath(IShellItem shellItem)
    {
        CheckHResult(shellItem.GetDisplayName(ShellItemDisplayName.FileSystemPath, out var displayNamePointer));
        try
        {
            return Marshal.PtrToStringUni(displayNamePointer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(displayNamePointer);
        }
    }

    private static IShellItem CreateShellItemFromPath(string path)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out var shellItemObject);
        return (IShellItem)shellItemObject;
    }

    private static IShellItem? TryCreateCustomPlaceShellItem(FileDialogCustomPlace customPlace)
    {
        if (!string.IsNullOrWhiteSpace(customPlace.Path))
        {
            return CreateShellItemFromPath(customPlace.Path);
        }

        if (customPlace.KnownFolderGuid != Guid.Empty &&
            SHGetKnownFolderPath(customPlace.KnownFolderGuid, 0, IntPtr.Zero, out var knownFolderPathPointer) == 0 &&
            knownFolderPathPointer != IntPtr.Zero)
        {
            try
            {
                var knownFolderPath = Marshal.PtrToStringUni(knownFolderPathPointer);
                return string.IsNullOrWhiteSpace(knownFolderPath) ? null : CreateShellItemFromPath(knownFolderPath);
            }
            finally
            {
                Marshal.FreeCoTaskMem(knownFolderPathPointer);
            }
        }

        return null;
    }

    private static void CheckHResult(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject != null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialogCom
    {
    }

    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        [PreserveSig] int SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        [PreserveSig] int SetFileTypeIndex(uint iFileType);
        [PreserveSig] int GetFileTypeIndex(out uint piFileType);
        [PreserveSig] int Advise(IntPtr pfde, out uint pdwCookie);
        [PreserveSig] int Unadvise(uint dwCookie);
        [PreserveSig] int SetOptions(uint fos);
        [PreserveSig] int GetOptions(out uint pfos);
        [PreserveSig] int SetDefaultFolder(IShellItem psi);
        [PreserveSig] int SetFolder(IShellItem psi);
        [PreserveSig] int GetFolder(out IShellItem ppsi);
        [PreserveSig] int GetCurrentSelection(out IShellItem ppsi);
        [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        [PreserveSig] int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        [PreserveSig] int GetResult(out IShellItem ppsi);
        [PreserveSig] int AddPlace(IShellItem psi, FileDialogAddPlace fdap);
        [PreserveSig] int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        [PreserveSig] int Close(int hr);
        [PreserveSig] int SetClientGuid(in Guid guid);
        [PreserveSig] int ClearClientData();
        [PreserveSig] int SetFilter(IntPtr pFilter);
    }

    [ComImport]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog : IFileDialog
    {
        [PreserveSig] new int Show(IntPtr parent);
        [PreserveSig] new int SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        [PreserveSig] new int SetFileTypeIndex(uint iFileType);
        [PreserveSig] new int GetFileTypeIndex(out uint piFileType);
        [PreserveSig] new int Advise(IntPtr pfde, out uint pdwCookie);
        [PreserveSig] new int Unadvise(uint dwCookie);
        [PreserveSig] new int SetOptions(uint fos);
        [PreserveSig] new int GetOptions(out uint pfos);
        [PreserveSig] new int SetDefaultFolder(IShellItem psi);
        [PreserveSig] new int SetFolder(IShellItem psi);
        [PreserveSig] new int GetFolder(out IShellItem ppsi);
        [PreserveSig] new int GetCurrentSelection(out IShellItem ppsi);
        [PreserveSig] new int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        [PreserveSig] new int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        [PreserveSig] new int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        [PreserveSig] new int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        [PreserveSig] new int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        [PreserveSig] new int GetResult(out IShellItem ppsi);
        [PreserveSig] new int AddPlace(IShellItem psi, FileDialogAddPlace fdap);
        [PreserveSig] new int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        [PreserveSig] new int Close(int hr);
        [PreserveSig] new int SetClientGuid(in Guid guid);
        [PreserveSig] new int ClearClientData();
        [PreserveSig] new int SetFilter(IntPtr pFilter);
        [PreserveSig] int GetResults(out IShellItemArray ppenum);
        [PreserveSig] int GetSelectedItems(out IShellItemArray ppsai);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, in Guid bhid, in Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(ShellItemDisplayName sigdnName, out IntPtr ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, in Guid bhid, in Guid riid, out IntPtr ppvOut);
        [PreserveSig] int GetPropertyStore(int flags, in Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPropertyDescriptionList(IntPtr keyType, in Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributes(ShellItemArrayGetAttributesFlags dwAttribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int GetCount(out uint pdwNumItems);
        [PreserveSig] int GetItemAt(uint dwIndex, out IShellItem ppsi);
        [PreserveSig] int EnumItems(out IntPtr ppenumShellItems);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pszName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pszSpec;
    }

    private enum FileDialogAddPlace
    {
        Bottom = 0,
        Top = 1
    }

    private enum ShellItemDisplayName : uint
    {
        FileSystemPath = 0x80058000
    }

    [Flags]
    private enum ShellItemArrayGetAttributesFlags : uint
    {
        And = 0x00000001,
        Or = 0x00000002,
        AppCompat = 0x00000003,
        Mask = 0x00000003,
        AllItems = 0x00004000
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        in Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);

    #endregion
}

/// <summary>
/// Represents a save file dialog.
/// </summary>
public sealed class SaveFileDialog : FileDialog
{
    #region Properties

    /// <summary>
    /// Gets or sets whether to create a prompt when the file exists.
    /// </summary>
    public bool OverwritePrompt { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to prompt to create a new file.
    /// </summary>
    public bool CreatePrompt { get; set; }

    #endregion

    /// <summary>
    /// Opens the file stream for writing.
    /// </summary>
    public Stream OpenFile()
    {
        if (string.IsNullOrEmpty(FileName))
            throw new InvalidOperationException("No file has been selected.");

        return new FileStream(FileName, FileMode.Create, FileAccess.Write);
    }

    /// <inheritdoc />
    public override bool? ShowDialog(IntPtr owner)
    {
        owner = DialogOwnerResolver.Resolve(owner);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ShowWindowsDialog(owner);
        }

        return ShowFallbackDialog();
    }

    private bool? ShowWindowsDialog(IntPtr owner)
    {
        IFileSaveDialog? dialog = null;
        IShellItem? initialDirectoryItem = null;
        var customPlaceItems = new List<IShellItem>();

        try
        {
            dialog = (IFileSaveDialog)new FileSaveDialogCom();

            if (!string.IsNullOrWhiteSpace(Title))
            {
                CheckHResult(dialog.SetTitle(Title));
            }

            if (!string.IsNullOrWhiteSpace(DefaultExt))
            {
                CheckHResult(dialog.SetDefaultExtension(DefaultExt));
            }

            var filters = BuildFilterSpecs();
            if (filters.Length > 0)
            {
                CheckHResult(dialog.SetFileTypes((uint)filters.Length, filters));
                CheckHResult(dialog.SetFileTypeIndex((uint)Math.Max(FilterIndex, 1)));
            }

            CheckHResult(dialog.SetOptions(BuildDialogOptions()));

            var initialPath = GetInitialDialogPath();
            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                initialDirectoryItem = CreateShellItemFromPath(initialPath);
                CheckHResult(dialog.SetFolder(initialDirectoryItem));
                CheckHResult(dialog.SetDefaultFolder(initialDirectoryItem));
            }

            foreach (var customPlace in CustomPlaces)
            {
                var customPlaceItem = TryCreateCustomPlaceShellItem(customPlace);
                if (customPlaceItem == null)
                {
                    continue;
                }

                customPlaceItems.Add(customPlaceItem);
                CheckHResult(dialog.AddPlace(customPlaceItem, FileDialogAddPlace.Bottom));
            }

            if (!string.IsNullOrWhiteSpace(FileName))
            {
                CheckHResult(dialog.SetFileName(FileName));
            }

            var showResult = dialog.Show(owner);
            if (showResult == HResultErrorCancelled)
            {
                return false;
            }

            CheckHResult(showResult);
            CheckHResult(dialog.GetResult(out var result));
            try
            {
                FileName = GetShellItemPath(result);
                FileNames = string.IsNullOrWhiteSpace(FileName) ? Array.Empty<string>() : [FileName];
            }
            finally
            {
                ReleaseComObject(result);
            }

            if (filters.Length > 0)
            {
                CheckHResult(dialog.GetFileTypeIndex(out var selectedFilterIndex));
                FilterIndex = (int)selectedFilterIndex;
            }

            OnFileOk();
            return true;
        }
        finally
        {
            foreach (var customPlaceItem in customPlaceItems)
            {
                ReleaseComObject(customPlaceItem);
            }

            ReleaseComObject(initialDirectoryItem);
            ReleaseComObject(dialog);
        }
    }

    private string? GetInitialDialogPath()
    {
        var preferredPath = !string.IsNullOrWhiteSpace(FileName)
            ? FileName
            : InitialDirectory;

        if (string.IsNullOrWhiteSpace(preferredPath))
        {
            return null;
        }

        if (!Directory.Exists(preferredPath) && File.Exists(preferredPath))
        {
            return Path.GetDirectoryName(preferredPath);
        }

        return preferredPath;
    }

    private bool? ShowFallbackDialog()
    {
        Console.WriteLine($"Save File Dialog: {Title ?? "Save As"}");
        Console.Write("Enter file path: ");
        var path = Console.ReadLine();

        if (!string.IsNullOrEmpty(path))
        {
            if (OverwritePrompt && File.Exists(path))
            {
                Console.Write($"File '{path}' already exists. Overwrite? (y/n): ");
                var response = Console.ReadLine();
                if (!response?.Equals("y", StringComparison.OrdinalIgnoreCase) ?? true)
                {
                    return false;
                }
            }

            FileName = path;
            FileNames = new[] { path };
            OnFileOk();
            return true;
        }

        return false;
    }

    #region Native Methods

    private const int HResultErrorCancelled = unchecked((int)0x800704C7);
    private const uint FOS_OVERWRITEPROMPT = 0x00000002;
    private const uint FOS_STRICTFILETYPES = 0x00000004;
    private const uint FOS_NOCHANGEDIR = 0x00000008;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_NOVALIDATE = 0x00000100;
    private const uint FOS_PATHMUSTEXIST = 0x00000800;
    private const uint FOS_CREATEPROMPT = 0x00002000;
    private const uint FOS_NODEREFERENCELINKS = 0x00100000;

    private uint BuildDialogOptions()
    {
        var options = FOS_FORCEFILESYSTEM;
        if (CheckPathExists) options |= FOS_PATHMUSTEXIST;
        if (OverwritePrompt) options |= FOS_OVERWRITEPROMPT;
        if (CreatePrompt) options |= FOS_CREATEPROMPT;
        if (!ValidateNames) options |= FOS_NOVALIDATE;
        if (!DereferenceLinks) options |= FOS_NODEREFERENCELINKS;
        if (RestoreDirectory) options |= FOS_NOCHANGEDIR;
        return options;
    }

    private COMDLG_FILTERSPEC[] BuildFilterSpecs()
    {
        var parsedFilters = ParseFilter();
        if (parsedFilters.Length == 0)
        {
            return Array.Empty<COMDLG_FILTERSPEC>();
        }

        return parsedFilters
            .Select(filter => new COMDLG_FILTERSPEC
            {
                pszName = filter.Name,
                pszSpec = filter.Pattern
            })
            .ToArray();
    }

    private static string GetShellItemPath(IShellItem shellItem)
    {
        CheckHResult(shellItem.GetDisplayName(ShellItemDisplayName.FileSystemPath, out var displayNamePointer));
        try
        {
            return Marshal.PtrToStringUni(displayNamePointer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(displayNamePointer);
        }
    }

    private static IShellItem CreateShellItemFromPath(string path)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out var shellItemObject);
        return (IShellItem)shellItemObject;
    }

    private static IShellItem? TryCreateCustomPlaceShellItem(FileDialogCustomPlace customPlace)
    {
        if (!string.IsNullOrWhiteSpace(customPlace.Path))
        {
            return CreateShellItemFromPath(customPlace.Path);
        }

        if (customPlace.KnownFolderGuid != Guid.Empty &&
            SHGetKnownFolderPath(customPlace.KnownFolderGuid, 0, IntPtr.Zero, out var knownFolderPathPointer) == 0 &&
            knownFolderPathPointer != IntPtr.Zero)
        {
            try
            {
                var knownFolderPath = Marshal.PtrToStringUni(knownFolderPathPointer);
                return string.IsNullOrWhiteSpace(knownFolderPath) ? null : CreateShellItemFromPath(knownFolderPath);
            }
            finally
            {
                Marshal.FreeCoTaskMem(knownFolderPathPointer);
            }
        }

        return null;
    }

    private static void CheckHResult(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject != null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    [ComImport]
    [Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
    private class FileSaveDialogCom
    {
    }

    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        [PreserveSig] int SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        [PreserveSig] int SetFileTypeIndex(uint iFileType);
        [PreserveSig] int GetFileTypeIndex(out uint piFileType);
        [PreserveSig] int Advise(IntPtr pfde, out uint pdwCookie);
        [PreserveSig] int Unadvise(uint dwCookie);
        [PreserveSig] int SetOptions(uint fos);
        [PreserveSig] int GetOptions(out uint pfos);
        [PreserveSig] int SetDefaultFolder(IShellItem psi);
        [PreserveSig] int SetFolder(IShellItem psi);
        [PreserveSig] int GetFolder(out IShellItem ppsi);
        [PreserveSig] int GetCurrentSelection(out IShellItem ppsi);
        [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        [PreserveSig] int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        [PreserveSig] int GetResult(out IShellItem ppsi);
        [PreserveSig] int AddPlace(IShellItem psi, FileDialogAddPlace fdap);
        [PreserveSig] int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        [PreserveSig] int Close(int hr);
        [PreserveSig] int SetClientGuid(in Guid guid);
        [PreserveSig] int ClearClientData();
        [PreserveSig] int SetFilter(IntPtr pFilter);
    }

    [ComImport]
    [Guid("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileSaveDialog : IFileDialog
    {
        [PreserveSig] new int Show(IntPtr parent);
        [PreserveSig] new int SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        [PreserveSig] new int SetFileTypeIndex(uint iFileType);
        [PreserveSig] new int GetFileTypeIndex(out uint piFileType);
        [PreserveSig] new int Advise(IntPtr pfde, out uint pdwCookie);
        [PreserveSig] new int Unadvise(uint dwCookie);
        [PreserveSig] new int SetOptions(uint fos);
        [PreserveSig] new int GetOptions(out uint pfos);
        [PreserveSig] new int SetDefaultFolder(IShellItem psi);
        [PreserveSig] new int SetFolder(IShellItem psi);
        [PreserveSig] new int GetFolder(out IShellItem ppsi);
        [PreserveSig] new int GetCurrentSelection(out IShellItem ppsi);
        [PreserveSig] new int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        [PreserveSig] new int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        [PreserveSig] new int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        [PreserveSig] new int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        [PreserveSig] new int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        [PreserveSig] new int GetResult(out IShellItem ppsi);
        [PreserveSig] new int AddPlace(IShellItem psi, FileDialogAddPlace fdap);
        [PreserveSig] new int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        [PreserveSig] new int Close(int hr);
        [PreserveSig] new int SetClientGuid(in Guid guid);
        [PreserveSig] new int ClearClientData();
        [PreserveSig] new int SetFilter(IntPtr pFilter);
        [PreserveSig] int SetSaveAsItem(IShellItem psi);
        [PreserveSig] int SetProperties(IntPtr pStore);
        [PreserveSig] int SetCollectedProperties(IntPtr pList, [MarshalAs(UnmanagedType.Bool)] bool fAppendDefault);
        [PreserveSig] int GetProperties(out IntPtr ppStore);
        [PreserveSig] int ApplyProperties(IShellItem psi, IntPtr pStore, IntPtr hwnd, IntPtr pSink);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pszName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pszSpec;
    }

    private enum FileDialogAddPlace
    {
        Bottom = 0,
        Top = 1
    }

    private enum ShellItemDisplayName : uint
    {
        FileSystemPath = 0x80058000
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, in Guid bhid, in Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(ShellItemDisplayName sigdnName, out IntPtr ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        in Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);

    #endregion
}

/// <summary>
/// Represents a folder browser dialog.
/// </summary>
public sealed class FolderBrowserDialog
{
    #region Properties

    /// <summary>
    /// Gets or sets the dialog title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the selected path.
    /// </summary>
    public string? SelectedPath { get; set; }

    /// <summary>
    /// Gets or sets the root folder.
    /// </summary>
    public Environment.SpecialFolder RootFolder { get; set; } = Environment.SpecialFolder.Desktop;

    /// <summary>
    /// Gets or sets whether the new folder button is shown.
    /// </summary>
    public bool ShowNewFolderButton { get; set; } = true;

    /// <summary>
    /// Gets or sets the initial directory.
    /// </summary>
    public string? InitialDirectory { get; set; }

    /// <summary>
    /// Gets or sets whether multiple folders can be selected.
    /// </summary>
    public bool Multiselect { get; set; }

    /// <summary>
    /// Gets the selected paths when Multiselect is true.
    /// </summary>
    public string[] SelectedPaths { get; private set; } = Array.Empty<string>();

    #endregion

    /// <summary>
    /// Shows the folder browser dialog.
    /// </summary>
    public bool? ShowDialog()
    {
        return ShowDialog(DialogOwnerResolver.Resolve());
    }

    /// <summary>
    /// Shows the folder browser dialog with the specified owner.
    /// </summary>
    public bool? ShowDialog(IntPtr owner)
    {
        owner = DialogOwnerResolver.Resolve(owner);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ShowWindowsDialog(owner);
        }

        return ShowFallbackDialog();
    }

    private bool? ShowWindowsDialog(IntPtr owner)
    {
        // Use modern IFileDialog interface for Windows Vista+
        // For simplicity, using shell browse for folder
        var bi = new BROWSEINFO();
        bi.hwndOwner = owner;
        bi.lpszTitle = Description ?? Title;
        bi.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;
        if (!ShowNewFolderButton) bi.ulFlags |= BIF_NONEWFOLDERBUTTON;

        var pidl = SHBrowseForFolder(ref bi);
        if (pidl != IntPtr.Zero)
        {
            var path = new char[260];
            if (SHGetPathFromIDList(pidl, path))
            {
                SelectedPath = new string(path).TrimEnd('\0');
                SelectedPaths = new[] { SelectedPath };
                Marshal.FreeCoTaskMem(pidl);
                return true;
            }
            Marshal.FreeCoTaskMem(pidl);
        }

        return false;
    }

    private bool? ShowFallbackDialog()
    {
        Console.WriteLine($"Folder Browser: {Title ?? Description ?? "Select Folder"}");
        Console.Write("Enter folder path: ");
        var path = Console.ReadLine();

        if (!string.IsNullOrEmpty(path))
        {
            if (!Directory.Exists(path))
            {
                Console.WriteLine("Directory does not exist.");
                return false;
            }

            SelectedPath = path;
            SelectedPaths = new[] { path };
            return true;
        }

        return false;
    }

    #region Native Methods

    private const uint BIF_RETURNONLYFSDIRS = 0x00000001;
    private const uint BIF_NEWDIALOGSTYLE = 0x00000040;
    private const uint BIF_NONEWFOLDERBUTTON = 0x00000200;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public string? lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, [MarshalAs(UnmanagedType.LPArray)] char[] pszPath);

    #endregion
}

/// <summary>
/// Represents a custom place in a file dialog.
/// </summary>
public sealed class FileDialogCustomPlace
{
    /// <summary>
    /// Gets or sets the path of the custom place.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the known folder GUID.
    /// </summary>
    public Guid KnownFolderGuid { get; set; }

    /// <summary>
    /// Creates a custom place from a path.
    /// </summary>
    public FileDialogCustomPlace(string path)
    {
        Path = path;
    }

    /// <summary>
    /// Creates a custom place from a known folder GUID.
    /// </summary>
    public FileDialogCustomPlace(Guid knownFolderGuid)
    {
        KnownFolderGuid = knownFolderGuid;
    }
}

/// <summary>
/// Provides known folder GUIDs for file dialog custom places.
/// </summary>
public static class KnownFolders
{
    /// <summary>Documents folder.</summary>
    public static readonly Guid Documents = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");

    /// <summary>Desktop folder.</summary>
    public static readonly Guid Desktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");

    /// <summary>Downloads folder.</summary>
    public static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>Music folder.</summary>
    public static readonly Guid Music = new("4BD8D571-6D19-48D3-BE97-422220080E43");

    /// <summary>Pictures folder.</summary>
    public static readonly Guid Pictures = new("33E28130-4E1E-4676-835A-98395C3BC3BB");

    /// <summary>Videos folder.</summary>
    public static readonly Guid Videos = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC");

    /// <summary>Computer/This PC folder.</summary>
    public static readonly Guid Computer = new("0AC0837C-BBF8-452A-850D-79D08E667CA7");

    /// <summary>Network folder.</summary>
    public static readonly Guid Network = new("D20BEEC4-5CA8-4905-AE3B-BF251EA09B53");
}
