using Jalium.UI.Media;

namespace Jalium.UI.Controls.Shell;

/// <summary>
/// Represents a list of items and tasks displayed as a menu on a Windows 7 taskbar button.
/// </summary>
public sealed class JumpList
{
    private readonly List<JumpItem> _jumpItems = new();
    private static JumpList? _current;

    /// <summary>
    /// Gets or sets a value that indicates whether recently opened items are displayed in the Jump List.
    /// </summary>
    public bool ShowRecentCategory { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether frequently used items are displayed in the Jump List.
    /// </summary>
    public bool ShowFrequentCategory { get; set; }

    /// <summary>
    /// Gets the collection of JumpItem objects displayed in the Jump List.
    /// </summary>
    public IList<JumpItem> JumpItems => _jumpItems;

    /// <summary>
    /// Occurs when the Jump List is applied to the Windows shell.
    /// </summary>
    public event EventHandler<JumpItemsRemovedEventArgs>? JumpItemsRemovedByUser;

    /// <summary>
    /// Occurs when a jump item is rejected from the Jump List.
    /// </summary>
    public event EventHandler<JumpItemsRejectedEventArgs>? JumpItemsRejected;

    /// <summary>
    /// Sets the Jump List for the current application.
    /// </summary>
    /// <param name="application">The application to set the Jump List for.</param>
    /// <param name="value">The Jump List to set.</param>
    public static void SetJumpList(object application, JumpList value)
    {
        _current = value;
        value?.ApplyInternal();
    }

    /// <summary>
    /// Gets the Jump List for the current application.
    /// </summary>
    /// <param name="application">The application to get the Jump List for.</param>
    /// <returns>The Jump List, or null if not set.</returns>
    public static JumpList? GetJumpList(object application)
    {
        return _current;
    }

    /// <summary>
    /// Adds an item to the Recent category of the Jump List.
    /// </summary>
    /// <param name="itemPath">The path to the item.</param>
    public static void AddToRecentCategory(string itemPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(itemPath);
        AddToRecentCategoryInternal(itemPath);
    }

    /// <summary>
    /// Adds a jump path to the Recent category of the Jump List.
    /// </summary>
    /// <param name="jumpPath">The JumpPath to add.</param>
    public static void AddToRecentCategory(JumpPath jumpPath)
    {
        ArgumentNullException.ThrowIfNull(jumpPath);
        AddToRecentCategory(jumpPath.Path);
    }

    /// <summary>
    /// Applies the Jump List to the Windows shell.
    /// </summary>
    public void Apply()
    {
        ApplyInternal();
    }

    /// <summary>
    /// Begins asynchronous initialization of the Jump List.
    /// </summary>
    public void BeginInit()
    {
        // Used for ISupportInitialize
    }

    /// <summary>
    /// Ends asynchronous initialization of the Jump List.
    /// </summary>
    public void EndInit()
    {
        Apply();
    }

    #region Internal Methods

    /// <summary>
    /// Applies the Jump List to the Windows shell.
    /// </summary>
    private void ApplyInternal()
    {
        // Platform-specific implementation using ICustomDestinationList
        var removedItems = new List<JumpItem>();
        var rejectedItems = new List<Tuple<JumpItem, JumpItemRejectionReason>>();

        // Notify of any removed or rejected items
        if (removedItems.Count > 0)
        {
            JumpItemsRemovedByUser?.Invoke(this, new JumpItemsRemovedEventArgs(removedItems));
        }

        if (rejectedItems.Count > 0)
        {
            var items = rejectedItems.Select(t => t.Item1).ToList();
            var reasons = rejectedItems.Select(t => t.Item2).ToList();
            JumpItemsRejected?.Invoke(this, new JumpItemsRejectedEventArgs(items, reasons));
        }
    }

    private static void AddToRecentCategoryInternal(string itemPath)
    {
        // Platform-specific implementation using SHAddToRecentDocs
    }

    #endregion
}

/// <summary>
/// Represents the base class for items in a Jump List.
/// </summary>
public abstract class JumpItem
{
    /// <summary>
    /// Gets or sets the category the item is grouped with in the Jump List.
    /// </summary>
    public string? CustomCategory { get; set; }
}

/// <summary>
/// Represents a link to an application in the Jump List.
/// </summary>
public sealed class JumpTask : JumpItem
{
    /// <summary>
    /// Gets or sets the text displayed for the task in the Jump List.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the text displayed in the tooltip for the task.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the path to the application.
    /// </summary>
    public string? ApplicationPath { get; set; }

    /// <summary>
    /// Gets or sets the arguments to pass to the application.
    /// </summary>
    public string? Arguments { get; set; }

    /// <summary>
    /// Gets or sets the working directory for the application.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the path to the icon resource.
    /// </summary>
    public string? IconResourcePath { get; set; }

    /// <summary>
    /// Gets or sets the index of the icon resource.
    /// </summary>
    public int IconResourceIndex { get; set; }
}

/// <summary>
/// Represents a link to a file displayed in a Jump List.
/// </summary>
public sealed class JumpPath : JumpItem
{
    /// <summary>
    /// Gets or sets the path to the file.
    /// </summary>
    public string? Path { get; set; }
}

/// <summary>
/// Event arguments for when jump items are removed by the user.
/// </summary>
public sealed class JumpItemsRemovedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the items that were removed.
    /// </summary>
    public IList<JumpItem> RemovedItems { get; }

    /// <summary>
    /// Initializes a new instance of the JumpItemsRemovedEventArgs class.
    /// </summary>
    public JumpItemsRemovedEventArgs(IList<JumpItem> removedItems)
    {
        RemovedItems = removedItems;
    }
}

/// <summary>
/// Event arguments for when jump items are rejected.
/// </summary>
public sealed class JumpItemsRejectedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the items that were rejected.
    /// </summary>
    public IList<JumpItem> RejectedItems { get; }

    /// <summary>
    /// Gets the reasons for each rejection.
    /// </summary>
    public IList<JumpItemRejectionReason> RejectionReasons { get; }

    /// <summary>
    /// Initializes a new instance of the JumpItemsRejectedEventArgs class.
    /// </summary>
    public JumpItemsRejectedEventArgs(IList<JumpItem> rejectedItems, IList<JumpItemRejectionReason> rejectionReasons)
    {
        RejectedItems = rejectedItems;
        RejectionReasons = rejectionReasons;
    }
}

/// <summary>
/// Specifies reasons why a jump item was rejected.
/// </summary>
public enum JumpItemRejectionReason
{
    /// <summary>
    /// No reason specified.
    /// </summary>
    None,

    /// <summary>
    /// The item path is not valid.
    /// </summary>
    InvalidItem,

    /// <summary>
    /// The item was removed by the user.
    /// </summary>
    RemovedByUser,

    /// <summary>
    /// The file type is not registered with the application.
    /// </summary>
    NoRegisteredHandler
}

/// <summary>
/// Provides a way to register or unregister an application for file associations.
/// </summary>
public static class FileRegistrationHelper
{
    /// <summary>
    /// Sets the file association for the application.
    /// </summary>
    /// <param name="extension">The file extension (e.g., ".txt").</param>
    /// <param name="progId">The programmatic identifier.</param>
    /// <param name="description">The description for the file type.</param>
    /// <param name="iconPath">The path to the icon.</param>
    public static void SetFileAssociation(string extension, string progId, string description, string? iconPath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(extension);
        ArgumentException.ThrowIfNullOrEmpty(progId);

        // Platform-specific implementation using registry
    }

    /// <summary>
    /// Removes the file association for the application.
    /// </summary>
    /// <param name="extension">The file extension.</param>
    /// <param name="progId">The programmatic identifier.</param>
    public static void RemoveFileAssociation(string extension, string progId)
    {
        ArgumentException.ThrowIfNullOrEmpty(extension);
        ArgumentException.ThrowIfNullOrEmpty(progId);

        // Platform-specific implementation using registry
    }

    /// <summary>
    /// Notifies the shell that file associations have changed.
    /// </summary>
    public static void NotifyShellOfChange()
    {
        // Platform-specific implementation using SHChangeNotify
    }
}

/// <summary>
/// Provides system-related utilities for shell integration.
/// </summary>
public static class SystemParameters2
{
    /// <summary>
    /// Gets the height of a horizontal scrollbar.
    /// </summary>
    public static double HorizontalScrollBarHeight => 17;

    /// <summary>
    /// Gets the width of a vertical scrollbar.
    /// </summary>
    public static double VerticalScrollBarWidth => 17;

    /// <summary>
    /// Gets the height of the window caption.
    /// </summary>
    public static double WindowCaptionHeight => 30;

    /// <summary>
    /// Gets the size of the window resize border.
    /// </summary>
    public static Thickness WindowResizeBorderThickness => new Thickness(4);

    /// <summary>
    /// Gets the glass frame thickness for DWM composition.
    /// </summary>
    public static Thickness WindowNonClientFrameThickness => new Thickness(3, 3, 3, 3);

    /// <summary>
    /// Gets a value indicating whether DWM composition is enabled.
    /// </summary>
    public static bool IsGlassEnabled
    {
        get
        {
            // Platform-specific implementation using DwmIsCompositionEnabled
            return true;
        }
    }

    /// <summary>
    /// Gets the current DWM colorization color.
    /// </summary>
    public static Color WindowGlassColor
    {
        get
        {
            // Platform-specific implementation using DwmGetColorizationColor
            return Color.FromArgb(255, 100, 149, 237); // Cornflower blue as default
        }
    }

    /// <summary>
    /// Gets the DWM colorization color brush.
    /// </summary>
    public static SolidColorBrush WindowGlassBrush => new SolidColorBrush(WindowGlassColor);

    /// <summary>
    /// Gets the non-client button size.
    /// </summary>
    public static Size WindowCaptionButtonSize => new Size(46, 30);

    /// <summary>
    /// Gets the size of the small icon.
    /// </summary>
    public static Size SmallIconSize => new Size(16, 16);

    /// <summary>
    /// Occurs when the glass enabled state changes.
    /// </summary>
    public static event EventHandler? IsGlassEnabledChanged;

    /// <summary>
    /// Occurs when the window glass color changes.
    /// </summary>
    public static event EventHandler? WindowGlassColorChanged;

    internal static void NotifyGlassEnabledChanged()
    {
        IsGlassEnabledChanged?.Invoke(null, EventArgs.Empty);
    }

    internal static void NotifyWindowGlassColorChanged()
    {
        WindowGlassColorChanged?.Invoke(null, EventArgs.Empty);
    }
}
