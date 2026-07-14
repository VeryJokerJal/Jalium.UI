using System.Runtime.InteropServices;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Controls;

/// <summary>
/// Base class for file dialogs.
/// </summary>
internal abstract class FileDialog
{
    /// <summary>
    /// Gets whether the current Linux session exposes the desktop-neutral file chooser portal.
    /// </summary>
    public static bool IsPortalAvailable =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
        LinuxDesktopPortal.IsInterfaceAvailable("org.freedesktop.portal.FileChooser");

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

    /// <summary>
    /// Gets or sets the maximum time to wait for a Linux desktop portal response.
    /// </summary>
    public TimeSpan PortalTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets a token that can cancel a pending Linux desktop portal request.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }

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
        PortalTimeout = TimeSpan.FromMinutes(2);
        CancellationToken = default;
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
internal sealed class OpenFileDialog : FileDialog
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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return ShowLinuxDialog(owner);
        }

        return false;
    }

    private bool? ShowWindowsDialog(nint owner)
    {
        // NativeAOT has no built-in COM interop, so the dialog is driven entirely through
        // CoCreateInstance + raw vtable dispatch (see ShellComInterop) instead of a classic
        // [ComImport] runtime-callable wrapper. Every COM object is held as a raw nint and
        // released exactly once in the finally.
        nint dialog = 0;
        nint initialDirectoryItem = 0;
        var customPlaceItems = new List<nint>();
        nint filterBlock = 0;
        int filterCount = 0;

        try
        {
            dialog = ShellComInterop.CreateOpenDialog();

            if (!string.IsNullOrWhiteSpace(Title))
            {
                ShellComInterop.SetTitle(dialog, Title);
            }

            if (!string.IsNullOrWhiteSpace(DefaultExt))
            {
                ShellComInterop.SetDefaultExtension(dialog, DefaultExt);
            }

            var filters = BuildFilterSpecs();
            filterBlock = ShellComInterop.AllocFilterSpecs(filters, out filterCount);
            if (filterCount > 0)
            {
                ShellComInterop.SetFileTypes(dialog, (uint)filterCount, filterBlock);
                ShellComInterop.SetFileTypeIndex(dialog, (uint)Math.Max(FilterIndex, 1));
            }

            ShellComInterop.SetOptions(dialog, BuildDialogOptions());

            var initialPath = GetInitialDialogPath();
            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                initialDirectoryItem = ShellComInterop.CreateShellItem(initialPath);
                ShellComInterop.SetFolder(dialog, initialDirectoryItem);
                ShellComInterop.SetDefaultFolder(dialog, initialDirectoryItem);
            }

            foreach (var customPlace in CustomPlaces)
            {
                var customPlaceItem = ShellComInterop.TryCreateCustomPlaceItem(customPlace);
                if (customPlaceItem == 0)
                {
                    continue;
                }

                customPlaceItems.Add(customPlaceItem);
                ShellComInterop.AddPlace(dialog, customPlaceItem, ShellComInterop.FDAP_BOTTOM);
            }

            if (!string.IsNullOrWhiteSpace(FileName))
            {
                ShellComInterop.SetFileName(dialog, FileName);
            }

            var showResult = ShellComInterop.Show(dialog, owner);
            if (showResult == ShellComInterop.HResultErrorCancelled)
            {
                return false;
            }

            ShellComInterop.CheckHResult(showResult);

            if (Multiselect)
            {
                var results = ShellComInterop.GetResults(dialog);
                try
                {
                    FileNames = ShellComInterop.GetItemArrayPaths(results);
                    FileName = FileNames.FirstOrDefault();
                }
                finally
                {
                    ShellComInterop.Release(results);
                }
            }
            else
            {
                var result = ShellComInterop.GetResult(dialog);
                try
                {
                    FileName = ShellComInterop.GetItemPath(result);
                    FileNames = string.IsNullOrWhiteSpace(FileName) ? Array.Empty<string>() : [FileName];
                }
                finally
                {
                    ShellComInterop.Release(result);
                }
            }

            if (filterCount > 0)
            {
                FilterIndex = (int)ShellComInterop.GetFileTypeIndex(dialog);
            }

            OnFileOk();
            return true;
        }
        finally
        {
            ShellComInterop.FreeFilterSpecs(filterBlock, filterCount);
            foreach (var customPlaceItem in customPlaceItems)
            {
                ShellComInterop.Release(customPlaceItem);
            }

            ShellComInterop.Release(initialDirectoryItem);
            ShellComInterop.Release(dialog);
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

    private bool? ShowLinuxDialog(nint owner)
    {
        var currentFolder = InitialDirectory;
        if (string.IsNullOrWhiteSpace(currentFolder) && !string.IsNullOrWhiteSpace(FileName))
            currentFolder = Directory.Exists(FileName) ? FileName : Path.GetDirectoryName(FileName);

        var response = LinuxDesktopPortal.ShowFileChooser(
            owner,
            new LinuxPortalFileChooserOptions(
                Title ?? (IsFolderPicker ? "Select Folder" : "Open File"),
                Save: false,
                Multiple: Multiselect,
                Directory: IsFolderPicker,
                CurrentFolder: currentFolder,
                CurrentName: null,
                Filters: ParseFilter(),
                FilterIndex: FilterIndex,
                Timeout: PortalTimeout),
            CancellationToken);

        if (response.Status != LinuxPortalResponseStatus.Success)
            return false;

        var selectedPaths = response.Values
            .Where(path => IsFolderPicker
                ? !CheckPathExists || Directory.Exists(path)
                : !CheckFileExists || File.Exists(path))
            .Take(Multiselect ? int.MaxValue : 1)
            .ToArray();

        if (selectedPaths.Length == 0)
            return false;

        FileNames = selectedPaths;
        FileName = selectedPaths[0];
        OnFileOk();
        return true;
    }

    #region Dialog options

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

    private (string Name, string Pattern)[] BuildFilterSpecs()
    {
        var parsedFilters = ParseFilter();
        if (parsedFilters.Length == 0 || IsFolderPicker)
        {
            return Array.Empty<(string, string)>();
        }

        return parsedFilters;
    }

    #endregion
}

/// <summary>
/// Represents a save file dialog.
/// </summary>
internal sealed class SaveFileDialog : FileDialog
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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return ShowLinuxDialog(owner);
        }

        return false;
    }

    private bool? ShowWindowsDialog(nint owner)
    {
        // See OpenFileDialog.ShowWindowsDialog: raw-nint COM dispatch via ShellComInterop so
        // the save dialog works under NativeAOT (no built-in COM interop). The save dialog only
        // ever calls GetResult (a single item) — never GetResults, which is an IFileOpenDialog
        // member occupying the vtable slot that IFileSaveDialog reuses for SetSaveAsItem.
        nint dialog = 0;
        nint initialDirectoryItem = 0;
        var customPlaceItems = new List<nint>();
        nint filterBlock = 0;
        int filterCount = 0;

        try
        {
            dialog = ShellComInterop.CreateSaveDialog();

            if (!string.IsNullOrWhiteSpace(Title))
            {
                ShellComInterop.SetTitle(dialog, Title);
            }

            if (!string.IsNullOrWhiteSpace(DefaultExt))
            {
                ShellComInterop.SetDefaultExtension(dialog, DefaultExt);
            }

            var filters = BuildFilterSpecs();
            filterBlock = ShellComInterop.AllocFilterSpecs(filters, out filterCount);
            if (filterCount > 0)
            {
                ShellComInterop.SetFileTypes(dialog, (uint)filterCount, filterBlock);
                ShellComInterop.SetFileTypeIndex(dialog, (uint)Math.Max(FilterIndex, 1));
            }

            ShellComInterop.SetOptions(dialog, BuildDialogOptions());

            var initialPath = GetInitialDialogPath();
            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                initialDirectoryItem = ShellComInterop.CreateShellItem(initialPath);
                ShellComInterop.SetFolder(dialog, initialDirectoryItem);
                ShellComInterop.SetDefaultFolder(dialog, initialDirectoryItem);
            }

            foreach (var customPlace in CustomPlaces)
            {
                var customPlaceItem = ShellComInterop.TryCreateCustomPlaceItem(customPlace);
                if (customPlaceItem == 0)
                {
                    continue;
                }

                customPlaceItems.Add(customPlaceItem);
                ShellComInterop.AddPlace(dialog, customPlaceItem, ShellComInterop.FDAP_BOTTOM);
            }

            if (!string.IsNullOrWhiteSpace(FileName))
            {
                ShellComInterop.SetFileName(dialog, FileName);
            }

            var showResult = ShellComInterop.Show(dialog, owner);
            if (showResult == ShellComInterop.HResultErrorCancelled)
            {
                return false;
            }

            ShellComInterop.CheckHResult(showResult);

            var result = ShellComInterop.GetResult(dialog);
            try
            {
                FileName = ShellComInterop.GetItemPath(result);
                FileNames = string.IsNullOrWhiteSpace(FileName) ? Array.Empty<string>() : [FileName];
            }
            finally
            {
                ShellComInterop.Release(result);
            }

            if (filterCount > 0)
            {
                FilterIndex = (int)ShellComInterop.GetFileTypeIndex(dialog);
            }

            OnFileOk();
            return true;
        }
        finally
        {
            ShellComInterop.FreeFilterSpecs(filterBlock, filterCount);
            foreach (var customPlaceItem in customPlaceItems)
            {
                ShellComInterop.Release(customPlaceItem);
            }

            ShellComInterop.Release(initialDirectoryItem);
            ShellComInterop.Release(dialog);
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

    private bool? ShowLinuxDialog(nint owner)
    {
        var currentFolder = InitialDirectory;
        var currentName = string.IsNullOrWhiteSpace(FileName) ? null : Path.GetFileName(FileName);
        if (string.IsNullOrWhiteSpace(currentFolder) && !string.IsNullOrWhiteSpace(FileName))
            currentFolder = Directory.Exists(FileName) ? FileName : Path.GetDirectoryName(FileName);

        var response = LinuxDesktopPortal.ShowFileChooser(
            owner,
            new LinuxPortalFileChooserOptions(
                Title ?? "Save File",
                Save: true,
                Multiple: false,
                Directory: false,
                CurrentFolder: currentFolder,
                CurrentName: currentName,
                Filters: ParseFilter(),
                FilterIndex: FilterIndex,
                Timeout: PortalTimeout),
            CancellationToken);

        if (response.Status != LinuxPortalResponseStatus.Success || response.Values.Count == 0)
            return false;

        var path = response.Values[0];
        if (AddExtension && string.IsNullOrEmpty(Path.GetExtension(path)) &&
            !string.IsNullOrWhiteSpace(DefaultExt))
        {
            path += "." + DefaultExt.TrimStart('.');
        }

        var parent = Path.GetDirectoryName(path);
        if (CheckPathExists && (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)))
            return false;

        FileName = path;
        FileNames = [path];
        OnFileOk();
        return true;
    }

    #region Dialog options

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

    private (string Name, string Pattern)[] BuildFilterSpecs()
    {
        return ParseFilter();
    }

    #endregion
}

/// <summary>
/// NativeAOT-safe Windows Shell interop for the common item dialogs (IFileOpenDialog /
/// IFileSaveDialog / IShellItem / IShellItemArray).
/// </summary>
/// <remarks>
/// <para>
/// NativeAOT ships with no built-in COM interop: instantiating a classic <c>[ComImport]</c>
/// coclass (e.g. <c>new FileOpenDialogCom()</c>) and dispatching through a runtime-synthesised
/// RCW throws <see cref="NotSupportedException"/> ("Built-in COM has been disabled"). That is the
/// same wall the UI Automation stack hit before it moved to source-generated COM. Rather than pull
/// in <c>[GeneratedComInterface]</c> (whose marshaller has no support for the
/// <c>COMDLG_FILTERSPEC*</c> array-of-struct-with-string-pointers that <c>SetFileTypes</c> takes),
/// this consumer uses the pattern already shipping elsewhere in the framework — <see
/// cref="OleDropTarget"/>, <c>OleDragSource</c>, and the Windows notification backend — namely
/// <c>CoCreateInstance</c> to a raw <see langword="nint"/> followed by
/// <c>delegate* unmanaged[Stdcall]</c> vtable-slot calls. Every method here is <c>PreserveSig</c>
/// (returns the raw HRESULT) exactly as the underlying IDL, and every COM reference is released
/// with <see cref="Marshal.Release(nint)"/> (never <see cref="Marshal.ReleaseComObject"/>, which is
/// a silent no-op under AOT because <see cref="Marshal.IsComObject"/> is always false there).
/// </para>
/// <para>
/// Vtable slots below are zero-based and count from IUnknown (QueryInterface=0, AddRef=1,
/// Release=2). IModalWindow::Show occupies slot 3; IFileDialog then runs 4..26 in IDL order;
/// IFileOpenDialog appends GetResults=27; IShellItem starts its own members at slot 3
/// (GetDisplayName=5); IShellItemArray at slot 3 (GetCount=7, GetItemAt=8). The order matches the
/// Windows SDK ShObjIdl_core.h vtables.
/// </para>
/// </remarks>
internal static unsafe class ShellComInterop
{
    #region Constants

    private const uint CLSCTX_INPROC_SERVER = 1;

    /// <summary>HRESULT_FROM_WIN32(ERROR_CANCELLED) — returned by Show when the user cancels.</summary>
    internal const int HResultErrorCancelled = unchecked((int)0x800704C7);

    /// <summary>SIGDN_FILESYSPATH — request the full file-system path from IShellItem::GetDisplayName.</summary>
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    /// <summary>FDAP_BOTTOM — add a custom place at the bottom of the places list.</summary>
    internal const int FDAP_BOTTOM = 0;

    private static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid CLSID_FileSaveDialog = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");
    private static readonly Guid IID_IFileOpenDialog = new("D57C7288-D4AD-4768-BE02-9D969532D960");
    private static readonly Guid IID_IFileSaveDialog = new("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB");
    private static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    // IFileDialog / IFileOpenDialog vtable slots.
    private const int VT_Show = 3;
    private const int VT_SetFileTypes = 4;
    private const int VT_SetFileTypeIndex = 5;
    private const int VT_GetFileTypeIndex = 6;
    private const int VT_SetOptions = 9;
    private const int VT_SetDefaultFolder = 11;
    private const int VT_SetFolder = 12;
    private const int VT_SetFileName = 15;
    private const int VT_SetTitle = 17;
    private const int VT_GetResult = 20;
    private const int VT_AddPlace = 21;
    private const int VT_SetDefaultExtension = 22;
    private const int VT_GetResults = 27; // IFileOpenDialog only.

    // IShellItem vtable slots.
    private const int VT_ShellItem_GetDisplayName = 5;

    // IShellItemArray vtable slots.
    private const int VT_ShellItemArray_GetCount = 7;
    private const int VT_ShellItemArray_GetItemAt = 8;

    #endregion

    #region Activation

    /// <summary>Creates an IFileOpenDialog; the returned pointer is owned and must be released.</summary>
    internal static nint CreateOpenDialog()
    {
        Guid clsid = CLSID_FileOpenDialog;
        Guid iid = IID_IFileOpenDialog;
        CheckHResult(CoCreateInstance(in clsid, 0, CLSCTX_INPROC_SERVER, in iid, out var dialog));
        return dialog;
    }

    /// <summary>Creates an IFileSaveDialog; the returned pointer is owned and must be released.</summary>
    internal static nint CreateSaveDialog()
    {
        Guid clsid = CLSID_FileSaveDialog;
        Guid iid = IID_IFileSaveDialog;
        CheckHResult(CoCreateInstance(in clsid, 0, CLSCTX_INPROC_SERVER, in iid, out var dialog));
        return dialog;
    }

    /// <summary>Creates an IShellItem for a file-system path; the returned pointer is owned.</summary>
    internal static nint CreateShellItem(string path)
    {
        Guid iid = IID_IShellItem;
        CheckHResult(SHCreateItemFromParsingName(path, 0, in iid, out var item));
        return item;
    }

    /// <summary>Resolves a custom place to an owned IShellItem, or 0 when it cannot be created.</summary>
    internal static nint TryCreateCustomPlaceItem(FileDialogCustomPlace customPlace)
    {
        if (!string.IsNullOrWhiteSpace(customPlace.Path))
        {
            return CreateShellItem(customPlace.Path);
        }

        if (customPlace.KnownFolderGuid != Guid.Empty)
        {
            Guid knownFolder = customPlace.KnownFolderGuid;
            if (SHGetKnownFolderPath(in knownFolder, 0, 0, out var knownFolderPathPointer) == 0 &&
                knownFolderPathPointer != 0)
            {
                try
                {
                    var knownFolderPath = Marshal.PtrToStringUni(knownFolderPathPointer);
                    return string.IsNullOrWhiteSpace(knownFolderPath) ? 0 : CreateShellItem(knownFolderPath);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(knownFolderPathPointer);
                }
            }
        }

        return 0;
    }

    #endregion

    #region IFileDialog members

    internal static void SetTitle(nint dialog, string? title) => CheckHResult(InvokeStr(dialog, VT_SetTitle, title));

    internal static void SetDefaultExtension(nint dialog, string? ext) => CheckHResult(InvokeStr(dialog, VT_SetDefaultExtension, ext));

    internal static void SetFileName(nint dialog, string? fileName) => CheckHResult(InvokeStr(dialog, VT_SetFileName, fileName));

    internal static void SetFileTypes(nint dialog, uint count, nint filterSpecs) => CheckHResult(InvokeUIntNint(dialog, VT_SetFileTypes, count, filterSpecs));

    internal static void SetFileTypeIndex(nint dialog, uint index) => CheckHResult(InvokeUInt(dialog, VT_SetFileTypeIndex, index));

    internal static uint GetFileTypeIndex(nint dialog)
    {
        CheckHResult(InvokeOutUInt(dialog, VT_GetFileTypeIndex, out var index));
        return index;
    }

    internal static void SetOptions(nint dialog, uint options) => CheckHResult(InvokeUInt(dialog, VT_SetOptions, options));

    internal static void SetFolder(nint dialog, nint shellItem) => CheckHResult(InvokeNint(dialog, VT_SetFolder, shellItem));

    internal static void SetDefaultFolder(nint dialog, nint shellItem) => CheckHResult(InvokeNint(dialog, VT_SetDefaultFolder, shellItem));

    internal static void AddPlace(nint dialog, nint shellItem, int placement) => CheckHResult(InvokeNintInt(dialog, VT_AddPlace, shellItem, placement));

    /// <summary>Shows the dialog. Returns the raw HRESULT (caller handles the cancel HRESULT).</summary>
    internal static int Show(nint dialog, nint owner) => InvokeNint(dialog, VT_Show, owner);

    /// <summary>IFileDialog::GetResult — returns an owned IShellItem for the single selection.</summary>
    internal static nint GetResult(nint dialog)
    {
        CheckHResult(InvokeOutNint(dialog, VT_GetResult, out var item));
        return item;
    }

    /// <summary>IFileOpenDialog::GetResults — returns an owned IShellItemArray of selections.</summary>
    internal static nint GetResults(nint dialog)
    {
        CheckHResult(InvokeOutNint(dialog, VT_GetResults, out var array));
        return array;
    }

    #endregion

    #region IShellItem / IShellItemArray members

    /// <summary>Reads the file-system path from an IShellItem.</summary>
    internal static string GetItemPath(nint shellItem)
    {
        CheckHResult(InvokeUIntOutNint(shellItem, VT_ShellItem_GetDisplayName, SIGDN_FILESYSPATH, out var displayNamePointer));
        try
        {
            return Marshal.PtrToStringUni(displayNamePointer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(displayNamePointer);
        }
    }

    /// <summary>Enumerates an IShellItemArray, returning the file-system path of each item.</summary>
    internal static string[] GetItemArrayPaths(nint shellItemArray)
    {
        CheckHResult(InvokeOutUInt(shellItemArray, VT_ShellItemArray_GetCount, out var count));
        var paths = new List<string>((int)count);
        for (uint i = 0; i < count; i++)
        {
            CheckHResult(InvokeUIntOutNint(shellItemArray, VT_ShellItemArray_GetItemAt, i, out var shellItem));
            try
            {
                var path = GetItemPath(shellItem);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    paths.Add(path);
                }
            }
            finally
            {
                Release(shellItem);
            }
        }

        return paths.ToArray();
    }

    #endregion

    #region Filter spec marshalling

    // COMDLG_FILTERSPEC is { LPCWSTR pszName; LPCWSTR pszSpec; } — two pointers. Modelled with
    // blittable nint fields so the array can be passed to SetFileTypes without runtime marshalling.
    [StructLayout(LayoutKind.Sequential)]
    private struct COMDLG_FILTERSPEC
    {
        public nint pszName;
        public nint pszSpec;
    }

    /// <summary>
    /// Allocates a native COMDLG_FILTERSPEC array from the parsed filter list. The returned block
    /// and every string it points at must be released with <see cref="FreeFilterSpecs"/>.
    /// </summary>
    internal static nint AllocFilterSpecs((string Name, string Pattern)[] filters, out int count)
    {
        count = filters.Length;
        if (count == 0)
        {
            return 0;
        }

        var stride = sizeof(COMDLG_FILTERSPEC);
        var block = Marshal.AllocCoTaskMem(count * stride);

        // Zero every entry first so that if a string allocation below throws (OOM), FreeFilterSpecs
        // walks only real-or-null pointers, never uninitialized memory.
        for (var i = 0; i < count; i++)
        {
            var entry = (COMDLG_FILTERSPEC*)(block + i * stride);
            entry->pszName = 0;
            entry->pszSpec = 0;
        }

        try
        {
            for (var i = 0; i < count; i++)
            {
                var entry = (COMDLG_FILTERSPEC*)(block + i * stride);
                entry->pszName = Marshal.StringToCoTaskMemUni(filters[i].Name ?? string.Empty);
                entry->pszSpec = Marshal.StringToCoTaskMemUni(filters[i].Pattern ?? string.Empty);
            }
        }
        catch
        {
            // On a mid-build throw the caller never receives the block pointer (its filterBlock
            // stays 0), so its finally cannot free it — release the block and any strings written
            // so far here before propagating.
            FreeFilterSpecs(block, count);
            throw;
        }

        return block;
    }

    /// <summary>Frees a COMDLG_FILTERSPEC array previously produced by <see cref="AllocFilterSpecs"/>.</summary>
    internal static void FreeFilterSpecs(nint block, int count)
    {
        if (block == 0)
        {
            return;
        }

        var stride = sizeof(COMDLG_FILTERSPEC);
        for (var i = 0; i < count; i++)
        {
            var entry = (COMDLG_FILTERSPEC*)(block + i * stride);
            if (entry->pszName != 0)
            {
                Marshal.FreeCoTaskMem(entry->pszName);
            }

            if (entry->pszSpec != 0)
            {
                Marshal.FreeCoTaskMem(entry->pszSpec);
            }
        }

        Marshal.FreeCoTaskMem(block);
    }

    #endregion

    #region Vtable dispatch primitives

    /// <summary>Releases a COM interface pointer via IUnknown::Release (slot 2).</summary>
    internal static void Release(nint pUnk)
    {
        if (pUnk != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(pUnk, 2))(pUnk);
        }
    }

    internal static void CheckHResult(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    private static nint Slot(nint pThis, int index) => ((nint*)*(nint*)pThis)[index];

    private static int InvokeNint(nint pThis, int slot, nint arg)
        => ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(pThis, slot))(pThis, arg);

    private static int InvokeUInt(nint pThis, int slot, uint arg)
        => ((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(pThis, slot))(pThis, arg);

    private static int InvokeUIntNint(nint pThis, int slot, uint arg0, nint arg1)
        => ((delegate* unmanaged[Stdcall]<nint, uint, nint, int>)Slot(pThis, slot))(pThis, arg0, arg1);

    private static int InvokeNintInt(nint pThis, int slot, nint arg0, int arg1)
        => ((delegate* unmanaged[Stdcall]<nint, nint, int, int>)Slot(pThis, slot))(pThis, arg0, arg1);

    private static int InvokeOutUInt(nint pThis, int slot, out uint result)
    {
        uint local = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(pThis, slot))(pThis, &local);
        result = local;
        return hr;
    }

    private static int InvokeOutNint(nint pThis, int slot, out nint result)
    {
        nint local = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(pThis, slot))(pThis, &local);
        result = local;
        return hr;
    }

    private static int InvokeUIntOutNint(nint pThis, int slot, uint arg0, out nint result)
    {
        nint local = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(pThis, slot))(pThis, arg0, &local);
        result = local;
        return hr;
    }

    private static int InvokeStr(nint pThis, int slot, string? value)
    {
        var pStr = Marshal.StringToCoTaskMemUni(value ?? string.Empty);
        try
        {
            return InvokeNint(pThis, slot, pStr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pStr);
        }
    }

    #endregion

    #region Native methods

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        nint pbc,
        in Guid riid,
        out nint ppv);

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        in Guid rfid,
        uint dwFlags,
        nint hToken,
        out nint ppszPath);

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

    /// <summary>
    /// Gets or sets the maximum time to wait for a Linux desktop portal response.
    /// </summary>
    public TimeSpan PortalTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets a token that can cancel a pending Linux desktop portal request.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }

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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return ShowLinuxDialog(owner);
        }

        return false;
    }

    private bool? ShowWindowsDialog(IntPtr owner)
    {
        // SHBrowseForFolder is a plain (non-COM) shell export, so this path is already
        // NativeAOT-safe: blittable BROWSEINFO + PIDL handling, no [ComImport] activation.
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

    private bool? ShowLinuxDialog(nint owner)
    {
        var response = LinuxDesktopPortal.ShowFileChooser(
            owner,
            new LinuxPortalFileChooserOptions(
                Title ?? Description ?? "Select Folder",
                Save: false,
                Multiple: Multiselect,
                Directory: true,
                CurrentFolder: InitialDirectory ?? SelectedPath,
                CurrentName: null,
                Filters: Array.Empty<(string Name, string Pattern)>(),
                FilterIndex: 1,
                Timeout: PortalTimeout),
            CancellationToken);

        if (response.Status != LinuxPortalResponseStatus.Success)
            return false;

        var paths = response.Values
            .Where(Directory.Exists)
            .Take(Multiselect ? int.MaxValue : 1)
            .ToArray();
        if (paths.Length == 0)
            return false;

        SelectedPaths = paths;
        SelectedPath = paths[0];
        return true;
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
internal sealed class FileDialogCustomPlace
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
