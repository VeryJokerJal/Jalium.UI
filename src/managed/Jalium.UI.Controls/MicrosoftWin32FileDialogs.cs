using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using LegacyFileDialog = Jalium.UI.Controls.FileDialog;
using LegacyOpenFileDialog = Jalium.UI.Controls.OpenFileDialog;
using LegacySaveFileDialog = Jalium.UI.Controls.SaveFileDialog;

namespace Microsoft.Win32;

/// <summary>
/// Provides the WPF-compatible base contract for native common dialogs.
/// </summary>
public abstract class CommonDialog
{
    private readonly Thread _creatingThread = Thread.CurrentThread;

    protected CommonDialog()
    {
    }

    public object? Tag { get; set; }

    public abstract void Reset();

    public virtual bool? ShowDialog()
    {
        CheckPermissionsToShowDialog();
        return RunDialog(DialogOwnerResolver.Resolve());
    }

    public bool? ShowDialog(Window owner)
    {
        CheckPermissionsToShowDialog();
        if (owner is null)
        {
            return ShowDialog();
        }

        if (owner.Handle == nint.Zero)
        {
            throw new InvalidOperationException("The owner window has not created a native handle.");
        }

        return RunDialog(owner.Handle);
    }

    protected virtual nint HookProc(nint hwnd, int msg, nint wParam, nint lParam) => nint.Zero;

    protected abstract bool RunDialog(nint hwndOwner);

    protected virtual void CheckPermissionsToShowDialog()
    {
        if (_creatingThread != Thread.CurrentThread)
        {
            throw new InvalidOperationException("A dialog must be shown on the thread that created it.");
        }
    }
}

/// <summary>
/// Provides the WPF-compatible common state shared by file and folder dialogs.
/// </summary>
public abstract class CommonItemDialog : CommonDialog
{
    private string? _defaultDirectory;
    private string? _initialDirectory;
    private string? _rootDirectory;
    private string? _title;
    private string[]? _itemNames;

    private protected CommonItemDialog()
    {
        Initialize();
    }

    public bool AddToRecent { get; set; }

    public Guid? ClientGuid { get; set; }

    public string DefaultDirectory
    {
        get => _defaultDirectory ?? string.Empty;
        set => _defaultDirectory = value;
    }

    public bool DereferenceLinks { get; set; }

    public string InitialDirectory
    {
        get => _initialDirectory ?? string.Empty;
        set => _initialDirectory = value;
    }

    public string RootDirectory
    {
        get => _rootDirectory ?? string.Empty;
        set => _rootDirectory = value;
    }

    public bool ShowHiddenItems { get; set; }

    public string Title
    {
        get => _title ?? string.Empty;
        set => _title = value;
    }

    public bool ValidateNames { get; set; }

    public IList<FileDialogCustomPlace> CustomPlaces { get; set; } = null!;

    private protected string CriticalItemName =>
        _itemNames is { Length: > 0 } ? _itemNames[0] : string.Empty;

    private protected string[]? MutableItemNames
    {
        get => _itemNames;
        set => _itemNames = value;
    }

    public override void Reset() => Initialize();

    public override string ToString() => base.ToString() + ": Title: " + Title;

    protected virtual void OnItemOk(CancelEventArgs e)
    {
    }

    protected override bool RunDialog(nint hwndOwner)
    {
        string[]? previousNames = _itemNames is null ? null : (string[])_itemNames.Clone();
        if (!RunItemDialog(hwndOwner))
        {
            return false;
        }

        var args = new CancelEventArgs();
        OnItemOk(args);
        if (args.Cancel)
        {
            _itemNames = previousNames;
            return false;
        }

        return true;
    }

    private protected abstract bool RunItemDialog(nint hwndOwner);

    private protected string[] CloneItemNames() =>
        _itemNames is null ? Array.Empty<string>() : (string[])_itemNames.Clone();

    private protected void ApplyCommonOptions(LegacyFileDialog dialog)
    {
        dialog.Title = Title;
        dialog.InitialDirectory = !string.IsNullOrEmpty(InitialDirectory)
            ? InitialDirectory
            : !string.IsNullOrEmpty(DefaultDirectory)
                ? DefaultDirectory
                : RootDirectory;
        dialog.DereferenceLinks = DereferenceLinks;
        dialog.ValidateNames = ValidateNames;
        dialog.CustomPlaces.Clear();

        if (CustomPlaces is not null)
        {
            foreach (FileDialogCustomPlace place in CustomPlaces)
            {
                dialog.CustomPlaces.Add(place.KnownFolder != Guid.Empty
                    ? new Jalium.UI.Controls.FileDialogCustomPlace(place.KnownFolder)
                    : new Jalium.UI.Controls.FileDialogCustomPlace(place.Path ?? string.Empty));
            }
        }
    }

    private void Initialize()
    {
        AddToRecent = true;
        ClientGuid = null;
        _defaultDirectory = null;
        DereferenceLinks = true;
        _initialDirectory = null;
        _rootDirectory = null;
        ShowHiddenItems = false;
        _title = null;
        ValidateNames = true;
        CustomPlaces = new List<FileDialogCustomPlace>();
        _itemNames = null;
    }
}

/// <summary>
/// WPF-compatible canonical file-dialog base class backed by Jalium's cross-platform dialogs.
/// </summary>
public abstract class FileDialog : CommonItemDialog
{
    private string? _defaultExtension;
    private string? _filter;
    private int _filterIndex;

    private protected FileDialog()
    {
        Initialize();
    }

    /// <summary>
    /// Gets whether the current Linux session exposes the desktop-neutral file chooser portal.
    /// </summary>
    public static bool IsPortalAvailable => LegacyFileDialog.IsPortalAvailable;

    public string SafeFileName => Path.GetFileName(CriticalItemName) ?? string.Empty;

    public string[] SafeFileNames => CloneItemNames()
        .Select(fileName => Path.GetFileName(fileName) ?? string.Empty)
        .ToArray();

    public string FileName
    {
        get => CriticalItemName;
        set => MutableItemNames = value is null ? null : [value];
    }

    public string[] FileNames => CloneItemNames();

    public bool AddExtension { get; set; }

    public bool CheckFileExists { get; set; }

    public bool CheckPathExists { get; set; }

    public string DefaultExt
    {
        get => _defaultExtension ?? string.Empty;
        set => _defaultExtension = string.IsNullOrEmpty(value)
            ? null
            : value.StartsWith('.') ? value[1..] : value;
    }

    public string Filter
    {
        get => _filter ?? string.Empty;
        set
        {
            if (string.Equals(value, _filter, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrEmpty(value) && value.Count(character => character == '|') % 2 == 0)
            {
                throw new ArgumentException("The file dialog filter must contain description and pattern pairs.", nameof(value));
            }

            _filter = string.IsNullOrEmpty(value) ? null : value;
        }
    }

    public int FilterIndex
    {
        get => _filterIndex;
        set => _filterIndex = value;
    }

    public bool RestoreDirectory { get; set; }

    /// <summary>
    /// Gets or sets the maximum time to wait for a Linux desktop portal response.
    /// </summary>
    public TimeSpan PortalTimeout { get; set; }

    /// <summary>
    /// Gets or sets a token that can cancel a pending Linux desktop portal request.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// Shows the file dialog with the specified native owner handle.
    /// </summary>
    /// <param name="owner">The owner window handle.</param>
    /// <returns><see langword="true"/> if the user selected a file; otherwise <see langword="false"/>.</returns>
    public bool? ShowDialog(IntPtr owner)
    {
        CheckPermissionsToShowDialog();
        return RunDialog(owner);
    }

    public event CancelEventHandler? FileOk;

    public override void Reset()
    {
        base.Reset();
        Initialize();
    }

    public override string ToString() => base.ToString() + ", FileName: " + FileName;

    protected override void OnItemOk(CancelEventArgs e) => FileOk?.Invoke(this, e);

    private protected void ApplyFileOptions(LegacyFileDialog dialog)
    {
        ApplyCommonOptions(dialog);
        dialog.FileName = FileName;
        dialog.AddExtension = AddExtension;
        dialog.CheckFileExists = CheckFileExists;
        dialog.CheckPathExists = CheckPathExists;
        dialog.DefaultExt = DefaultExt;
        dialog.Filter = Filter;
        dialog.FilterIndex = FilterIndex;
        dialog.RestoreDirectory = RestoreDirectory;
        dialog.PortalTimeout = PortalTimeout;
        dialog.CancellationToken = CancellationToken;
    }

    private protected void CaptureFileResults(LegacyFileDialog dialog)
    {
        MutableItemNames = dialog.FileNames.Length > 0
            ? (string[])dialog.FileNames.Clone()
            : string.IsNullOrEmpty(dialog.FileName) ? null : [dialog.FileName];
        FilterIndex = dialog.FilterIndex;
    }

    private void Initialize()
    {
        AddExtension = true;
        CheckFileExists = false;
        CheckPathExists = true;
        _defaultExtension = null;
        _filter = null;
        _filterIndex = 1;
        RestoreDirectory = false;
        PortalTimeout = TimeSpan.FromMinutes(2);
        CancellationToken = default;
    }
}

public sealed class OpenFileDialog : FileDialog
{
    public OpenFileDialog()
    {
        Initialize();
    }

    public bool ForcePreviewPane { get; set; }

    public bool Multiselect { get; set; }

    public bool ReadOnlyChecked { get; set; }

    public bool ShowReadOnly { get; set; }

    public Stream OpenFile()
    {
        if (string.IsNullOrEmpty(FileName))
        {
            throw new InvalidOperationException("No file has been selected.");
        }

        return new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public Stream[] OpenFiles() => FileNames
        .Select(fileName => (Stream)new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
        .ToArray();

    public override void Reset()
    {
        base.Reset();
        Initialize();
    }

    private protected override bool RunItemDialog(nint hwndOwner)
    {
        var dialog = new LegacyOpenFileDialog();
        ApplyFileOptions(dialog);
        dialog.Multiselect = Multiselect;
        dialog.ReadOnlyChecked = ReadOnlyChecked;
        dialog.ShowReadOnly = ShowReadOnly;
        bool accepted = dialog.ShowDialog(hwndOwner) == true;
        if (accepted)
        {
            CaptureFileResults(dialog);
        }

        return accepted;
    }

    private void Initialize()
    {
        CheckFileExists = true;
        ForcePreviewPane = false;
        Multiselect = false;
        ReadOnlyChecked = false;
        ShowReadOnly = false;
    }
}

public sealed class SaveFileDialog : FileDialog
{
    public SaveFileDialog()
    {
        Initialize();
    }

    public bool CreatePrompt { get; set; }

    public bool CreateTestFile { get; set; }

    public bool OverwritePrompt { get; set; }

    public Stream OpenFile()
    {
        if (string.IsNullOrEmpty(FileName))
        {
            throw new InvalidOperationException("No file has been selected.");
        }

        return new FileStream(FileName, FileMode.Create, FileAccess.ReadWrite);
    }

    public override void Reset()
    {
        base.Reset();
        Initialize();
    }

    private protected override bool RunItemDialog(nint hwndOwner)
    {
        var dialog = new LegacySaveFileDialog();
        ApplyFileOptions(dialog);
        dialog.CreatePrompt = CreatePrompt;
        dialog.OverwritePrompt = OverwritePrompt;
        bool accepted = dialog.ShowDialog(hwndOwner) == true;
        if (accepted)
        {
            CaptureFileResults(dialog);
        }

        return accepted;
    }

    private void Initialize()
    {
        CreatePrompt = false;
        CreateTestFile = true;
        OverwritePrompt = true;
    }
}

public sealed class OpenFolderDialog : CommonItemDialog
{
    public OpenFolderDialog()
    {
        Initialize();
    }

    public string SafeFolderName => Path.GetFileName(CriticalItemName) ?? string.Empty;

    public string[] SafeFolderNames => CloneItemNames()
        .Select(folderName => Path.GetFileName(folderName) ?? string.Empty)
        .ToArray();

    public string FolderName
    {
        get => CriticalItemName;
        set => MutableItemNames = value is null ? null : [value];
    }

    public string[] FolderNames => CloneItemNames();

    public bool Multiselect { get; set; }

    public event CancelEventHandler? FolderOk;

    public override void Reset()
    {
        base.Reset();
        Initialize();
    }

    public override string ToString() => base.ToString() + ", FolderName: " + FolderName;

    protected override void OnItemOk(CancelEventArgs e) => FolderOk?.Invoke(this, e);

    private protected override bool RunItemDialog(nint hwndOwner)
    {
        var dialog = new LegacyOpenFileDialog
        {
            IsFolderPicker = true,
            Multiselect = Multiselect,
            FileName = FolderName,
            CheckFileExists = false,
            CheckPathExists = true,
        };
        ApplyCommonOptions(dialog);
        bool accepted = dialog.ShowDialog(hwndOwner) == true;
        if (accepted)
        {
            MutableItemNames = dialog.FileNames.Length > 0
                ? (string[])dialog.FileNames.Clone()
                : string.IsNullOrEmpty(dialog.FileName) ? null : [dialog.FileName];
        }

        return accepted;
    }

    private void Initialize() => Multiselect = false;
}

public sealed class FileDialogCustomPlace
{
    public FileDialogCustomPlace(Guid knownFolder)
    {
        KnownFolder = knownFolder;
    }

    public FileDialogCustomPlace(string path)
    {
        Path = path ?? string.Empty;
    }

    public Guid KnownFolder { get; private set; }

    public string? Path { get; private set; }
}

public static class FileDialogCustomPlaces
{
    public static FileDialogCustomPlace RoamingApplicationData => Create("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D");
    public static FileDialogCustomPlace LocalApplicationData => Create("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
    public static FileDialogCustomPlace Cookies => Create("2B0F765D-C0E9-4171-908E-08A611B84FF6");
    public static FileDialogCustomPlace Contacts => Create("56784854-C6CB-462B-8169-88E350ACB882");
    public static FileDialogCustomPlace Favorites => Create("1777F761-68AD-4D8A-87BD-30B759FA33DD");
    public static FileDialogCustomPlace Programs => Create("A77F5D77-2E2B-44C3-A6A2-ABA601054A51");
    public static FileDialogCustomPlace Music => Create("4BD8D571-6D19-48D3-BE97-422220080E43");
    public static FileDialogCustomPlace Pictures => Create("33E28130-4E1E-4676-835A-98395C3BC3BB");
    public static FileDialogCustomPlace SendTo => Create("8983036C-27C0-404B-8F08-102D10DCFD74");
    public static FileDialogCustomPlace StartMenu => Create("625B53C3-AB48-4EC1-BA1F-A1EF4146FC19");
    public static FileDialogCustomPlace Startup => Create("B97D20BB-F46A-4C97-BA10-5E3608430854");
    public static FileDialogCustomPlace System => Create("1AC14E77-02E7-4E5D-B744-2EB1AE5198B7");
    public static FileDialogCustomPlace Templates => Create("A63293E8-664E-48DB-A079-DF759E0509F7");
    public static FileDialogCustomPlace Desktop => Create("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    public static FileDialogCustomPlace Documents => Create("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
    public static FileDialogCustomPlace ProgramFiles => Create("905E63B6-C1BF-494E-B29C-65B732D3D21A");
    public static FileDialogCustomPlace ProgramFilesCommon => Create("F7F1ED05-9F6D-47A2-AAAE-29D317C6F066");

    private static FileDialogCustomPlace Create(string value) => new(new Guid(value));
}
