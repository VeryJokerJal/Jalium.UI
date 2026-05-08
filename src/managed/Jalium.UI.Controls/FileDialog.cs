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
        if (IsFolderPicker)
        {
            return ShowWindowsFolderPickerDialog(owner);
        }

        // Use Windows Common File Dialog via COM interop
        // This is a simplified implementation - in production, use proper COM interop

        var ofn = new OPENFILENAME();
        ofn.lStructSize = Marshal.SizeOf(ofn);
        ofn.hwndOwner = owner;
        ofn.lpstrTitle = Title;
        ofn.lpstrInitialDir = InitialDirectory;
        ofn.lpstrDefExt = DefaultExt;

        // Build filter string (null-separated, double-null terminated)
        if (!string.IsNullOrEmpty(Filter))
        {
            ofn.lpstrFilter = Filter.Replace('|', '\0') + "\0";
        }

        ofn.nFilterIndex = FilterIndex;

        // Allocate buffer for file name(s)
        var maxPath = Multiselect ? 32768 : 260;
        var fileBuffer = new char[maxPath];
        if (!string.IsNullOrEmpty(FileName))
        {
            FileName.CopyTo(0, fileBuffer, 0, Math.Min(FileName.Length, maxPath - 1));
        }

        var handle = GCHandle.Alloc(fileBuffer, GCHandleType.Pinned);
        try
        {
            ofn.lpstrFile = handle.AddrOfPinnedObject();
            ofn.nMaxFile = maxPath;

            // Set flags
            uint flags = OFN_EXPLORER | OFN_HIDEREADONLY;
            if (CheckFileExists) flags |= OFN_FILEMUSTEXIST;
            if (CheckPathExists) flags |= OFN_PATHMUSTEXIST;
            if (Multiselect) flags |= OFN_ALLOWMULTISELECT;
            if (!ValidateNames) flags |= OFN_NOVALIDATE;
            if (!DereferenceLinks) flags |= OFN_NODEREFERENCELINKS;
            ofn.Flags = flags;

            if (GetOpenFileName(ref ofn))
            {
                // Parse results
                var result = new string(fileBuffer).TrimEnd('\0');
                if (Multiselect && result.Contains('\0'))
                {
                    var parts = result.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                    var directory = parts[0];
                    FileNames = parts.Skip(1).Select(f => Path.Combine(directory, f)).ToArray();
                    FileName = FileNames.FirstOrDefault();
                }
                else
                {
                    FileName = result;
                    FileNames = new[] { result };
                }

                FilterIndex = ofn.nFilterIndex;
                OnFileOk();
                return true;
            }

            return false;
        }
        finally
        {
            handle.Free();
        }
    }

    private bool? ShowWindowsFolderPickerDialog(IntPtr owner)
    {
        var dialog = new FolderBrowserDialog
        {
            Title = Title,
            Description = Title,
            InitialDirectory = !string.IsNullOrWhiteSpace(FileName) ? FileName : InitialDirectory,
            SelectedPath = !string.IsNullOrWhiteSpace(FileName) ? FileName : InitialDirectory,
            Multiselect = Multiselect
        };

        if (dialog.ShowDialog(owner) != true)
        {
            return false;
        }

        FileNames = dialog.SelectedPaths.Length > 0
            ? dialog.SelectedPaths
            : string.IsNullOrWhiteSpace(dialog.SelectedPath)
                ? Array.Empty<string>()
                : [dialog.SelectedPath];
        FileName = FileNames.FirstOrDefault();
        OnFileOk();
        return true;
    }

    private bool? ShowFallbackDialog()
    {
        // Simple console-based fallback
        Console.WriteLine($"Open File Dialog: {Title ?? "Open"}");
        Console.Write("Enter file path: ");
        var path = Console.ReadLine();

        if (!string.IsNullOrEmpty(path))
        {
            if (CheckFileExists && !File.Exists(path))
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

    private const uint OFN_EXPLORER = 0x00080000;
    private const uint OFN_FILEMUSTEXIST = 0x00001000;
    private const uint OFN_PATHMUSTEXIST = 0x00000800;
    private const uint OFN_ALLOWMULTISELECT = 0x00000200;
    private const uint OFN_HIDEREADONLY = 0x00000004;
    private const uint OFN_NOVALIDATE = 0x00000100;
    private const uint OFN_NODEREFERENCELINKS = 0x00100000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string? lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public uint Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public uint FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileName(ref OPENFILENAME lpofn);

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
        var ofn = new OPENFILENAME();
        ofn.lStructSize = Marshal.SizeOf(ofn);
        ofn.hwndOwner = owner;
        ofn.lpstrTitle = Title;
        ofn.lpstrInitialDir = InitialDirectory;
        ofn.lpstrDefExt = DefaultExt;

        if (!string.IsNullOrEmpty(Filter))
        {
            ofn.lpstrFilter = Filter.Replace('|', '\0') + "\0";
        }

        ofn.nFilterIndex = FilterIndex;

        var maxPath = 260;
        var fileBuffer = new char[maxPath];
        if (!string.IsNullOrEmpty(FileName))
        {
            FileName.CopyTo(0, fileBuffer, 0, Math.Min(FileName.Length, maxPath - 1));
        }

        var handle = GCHandle.Alloc(fileBuffer, GCHandleType.Pinned);
        try
        {
            ofn.lpstrFile = handle.AddrOfPinnedObject();
            ofn.nMaxFile = maxPath;

            uint flags = OFN_EXPLORER;
            if (CheckPathExists) flags |= OFN_PATHMUSTEXIST;
            if (OverwritePrompt) flags |= OFN_OVERWRITEPROMPT;
            if (CreatePrompt) flags |= OFN_CREATEPROMPT;
            if (!ValidateNames) flags |= OFN_NOVALIDATE;
            ofn.Flags = flags;

            if (GetSaveFileName(ref ofn))
            {
                FileName = new string(fileBuffer).TrimEnd('\0');
                FileNames = new[] { FileName };
                FilterIndex = ofn.nFilterIndex;
                OnFileOk();
                return true;
            }

            return false;
        }
        finally
        {
            handle.Free();
        }
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

    private const uint OFN_EXPLORER = 0x00080000;
    private const uint OFN_PATHMUSTEXIST = 0x00000800;
    private const uint OFN_OVERWRITEPROMPT = 0x00000002;
    private const uint OFN_CREATEPROMPT = 0x00002000;
    private const uint OFN_NOVALIDATE = 0x00000100;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string? lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public uint Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public uint FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetSaveFileName(ref OPENFILENAME lpofn);

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
