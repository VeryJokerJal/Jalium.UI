using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jalium.UI.Markup;

namespace Jalium.UI.Shell;

/// <summary>
/// Describes why a jump item could not be added to the operating-system jump list.
/// </summary>
public enum JumpItemRejectionReason
{
    None = 0,
    InvalidItem = 1,
    NoRegisteredHandler = 2,
    RemovedByUser = 3,
}

/// <summary>
/// Provides data for <see cref="JumpList.JumpItemsRejected"/>.
/// </summary>
public sealed class JumpItemsRejectedEventArgs : EventArgs
{
    public JumpItemsRejectedEventArgs()
        : this(null, null)
    {
    }

    public JumpItemsRejectedEventArgs(
        IList<JumpItem>? rejectedItems,
        IList<JumpItemRejectionReason>? reasons)
    {
        if ((rejectedItems is null) != (reasons is null) ||
            (rejectedItems is not null && reasons is not null && rejectedItems.Count != reasons.Count))
        {
            throw new ArgumentException("The rejected item and rejection reason collections must have the same count.");
        }

        RejectedItems = rejectedItems is null
            ? Array.Empty<JumpItem>()
            : new List<JumpItem>(rejectedItems).AsReadOnly();
        RejectionReasons = reasons is null
            ? Array.Empty<JumpItemRejectionReason>()
            : new List<JumpItemRejectionReason>(reasons).AsReadOnly();
    }

    public IList<JumpItem> RejectedItems { get; }

    public IList<JumpItemRejectionReason> RejectionReasons { get; }
}

/// <summary>
/// Provides data for <see cref="JumpList.JumpItemsRemovedByUser"/>.
/// </summary>
public sealed class JumpItemsRemovedEventArgs : EventArgs
{
    public JumpItemsRemovedEventArgs()
        : this(null)
    {
    }

    public JumpItemsRemovedEventArgs(IList<JumpItem>? removedItems)
    {
        RemovedItems = removedItems is null
            ? Array.Empty<JumpItem>()
            : new List<JumpItem>(removedItems).AsReadOnly();
    }

    public IList<JumpItem> RemovedItems { get; }
}

/// <summary>
/// Represents a list of items and tasks displayed from an application's taskbar button.
/// </summary>
[Jalium.UI.Markup.ContentProperty(nameof(JumpItems))]
public sealed class JumpList : ISupportInitialize
{
    private const uint ShardPathW = 0x00000003;
    private static readonly object ApplicationMapLock = new();
    private static readonly ConditionalWeakTable<Application, Association> ApplicationMap = new();

    private List<JumpItem> _jumpItems;
    private Application? _application;
    private bool? _initializing;

    public JumpList()
        : this(null, false, false)
    {
        // A default-constructed list may participate in the ISupportInitialize protocol.
        _initializing = null;
    }

    public JumpList(IEnumerable<JumpItem>? items, bool showFrequent, bool showRecent)
    {
        _jumpItems = items is null ? new List<JumpItem>() : new List<JumpItem>(items);
        ShowFrequentCategory = showFrequent;
        ShowRecentCategory = showRecent;
        // The parameterized constructor is already fully initialized, matching WPF.
        _initializing = false;
    }

    public bool ShowRecentCategory { get; set; }

    public bool ShowFrequentCategory { get; set; }

    public List<JumpItem> JumpItems => _jumpItems;

    public event EventHandler<JumpItemsRemovedEventArgs>? JumpItemsRemovedByUser;

    public event EventHandler<JumpItemsRejectedEventArgs>? JumpItemsRejected;

    public static void SetJumpList(Application application, JumpList? value)
    {
        ArgumentNullException.ThrowIfNull(application);

        lock (ApplicationMapLock)
        {
            if (ApplicationMap.TryGetValue(application, out var oldAssociation))
            {
                oldAssociation.Value._application = null;
                ApplicationMap.Remove(application);
            }

            if (value is not null)
            {
                if (value._application is { } oldApplication && !ReferenceEquals(oldApplication, application))
                {
                    ApplicationMap.Remove(oldApplication);
                }

                value._application = application;
                ApplicationMap.Add(application, new Association(value));
            }
        }

        value?.ApplyFromApplication();
    }

    public static JumpList? GetJumpList(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        lock (ApplicationMapLock)
        {
            return ApplicationMap.TryGetValue(application, out var association)
                ? association.Value
                : null;
        }
    }

    public static void AddToRecentCategory(string itemPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemPath);
        string fullPath = Path.GetFullPath(itemPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The item added to the Recent category must exist.", fullPath);
        }

        if (OperatingSystem.IsWindows())
        {
            SHAddToRecentDocs(ShardPathW, fullPath);
        }
    }

    public static void AddToRecentCategory(JumpPath jumpPath)
    {
        ArgumentNullException.ThrowIfNull(jumpPath);
        AddToRecentCategory(jumpPath.Path!);
    }

    public static void AddToRecentCategory(JumpTask jumpTask)
    {
        ArgumentNullException.ThrowIfNull(jumpTask);

        // SHAddToRecentDocs accepts a shell link for the full WPF behavior. Jalium's
        // portable fallback records the task's executable path where the path form is
        // supported, while leaving unsupported platforms side-effect free.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string? applicationPath = string.IsNullOrWhiteSpace(jumpTask.ApplicationPath)
            ? Environment.ProcessPath
            : jumpTask.ApplicationPath;
        if (string.IsNullOrWhiteSpace(applicationPath))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(applicationPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if (File.Exists(fullPath))
        {
            SHAddToRecentDocs(ShardPathW, fullPath);
        }
    }

    public void BeginInit()
    {
        if (!IsUnmodified)
        {
            throw new InvalidOperationException("BeginInit cannot be nested or called after the jump list was modified.");
        }

        _initializing = true;
    }

    public void EndInit()
    {
        if (_initializing != true)
        {
            throw new NotSupportedException("EndInit must be paired with BeginInit.");
        }

        _initializing = false;
        ApplyFromApplication();
    }

    public void Apply()
    {
        if (_initializing == true)
        {
            throw new InvalidOperationException("A jump list cannot be applied before EndInit is called.");
        }

        _initializing = false;
        ApplyCore();
    }

    private bool IsUnmodified =>
        _initializing is null &&
        _jumpItems.Count == 0 &&
        !ShowRecentCategory &&
        !ShowFrequentCategory;

    private void ApplyFromApplication()
    {
        if (_initializing != true && !IsUnmodified)
        {
            _initializing = false;
        }

        if (ReferenceEquals(_application, Application.Current) && _initializing == false)
        {
            ApplyCore();
        }
    }

    private void ApplyCore()
    {
        var acceptedItems = new List<JumpItem>(_jumpItems.Count);
        var rejectedItems = new List<JumpItem>();
        var rejectionReasons = new List<JumpItemRejectionReason>();

        foreach (JumpItem? item in _jumpItems)
        {
            JumpItemRejectionReason reason = GetRejectionReason(item);
            if (reason == JumpItemRejectionReason.None)
            {
                acceptedItems.Add(item!);
            }
            else
            {
                rejectedItems.Add(item!);
                rejectionReasons.Add(reason);
            }
        }

        _jumpItems = acceptedItems;
        if (rejectedItems.Count != 0)
        {
            JumpItemsRejected?.Invoke(
                this,
                new JumpItemsRejectedEventArgs(rejectedItems, rejectionReasons));
        }

        // User-removed items are supplied by a native destination-list backend. The
        // portable implementation has no removed-item snapshot to publish here.
    }

    /// <summary>Publishes the removed-item snapshot supplied by a future native destination-list backend.</summary>
    internal void ReportItemsRemovedByUser(IList<JumpItem> removedItems)
    {
        ArgumentNullException.ThrowIfNull(removedItems);
        JumpItemsRemovedByUser?.Invoke(this, new JumpItemsRemovedEventArgs(removedItems));
    }

    private static JumpItemRejectionReason GetRejectionReason(JumpItem? item)
    {
        if (item is JumpPath jumpPath)
        {
            string? itemPath = jumpPath.Path;
            if (string.IsNullOrWhiteSpace(itemPath))
            {
                return JumpItemRejectionReason.InvalidItem;
            }

            try
            {
                return File.Exists(Path.GetFullPath(itemPath))
                    ? JumpItemRejectionReason.None
                    : JumpItemRejectionReason.InvalidItem;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return JumpItemRejectionReason.InvalidItem;
            }
        }

        if (item is JumpTask jumpTask)
        {
            if (string.IsNullOrEmpty(jumpTask.Title) && !string.IsNullOrEmpty(jumpTask.CustomCategory))
            {
                return JumpItemRejectionReason.InvalidItem;
            }

            if (jumpTask.IconResourcePath?.Length >= 260)
            {
                return JumpItemRejectionReason.InvalidItem;
            }

            return JumpItemRejectionReason.None;
        }

        return JumpItemRejectionReason.InvalidItem;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHAddToRecentDocs(uint flags, string path);

    private sealed class Association(JumpList value)
    {
        public JumpList Value { get; } = value;
    }
}

/// <summary>
/// Base class for entries in a <see cref="JumpList"/>.
/// </summary>
public abstract class JumpItem
{
    internal JumpItem()
    {
    }

    public string? CustomCategory { get; set; }
}

/// <summary>
/// Represents an application shortcut in a jump list.
/// </summary>
public sealed class JumpTask : JumpItem
{
    public JumpTask()
    {
    }

    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ApplicationPath { get; set; }
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? IconResourcePath { get; set; }
    public int IconResourceIndex { get; set; } = -1;
}

/// <summary>
/// Represents a file-system path in a jump list.
/// </summary>
public sealed class JumpPath : JumpItem
{
    public JumpPath()
    {
    }

    public string? Path { get; set; }
}
