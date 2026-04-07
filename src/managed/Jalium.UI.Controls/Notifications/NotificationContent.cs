using Jalium.UI.Media;

namespace Jalium.UI.Notifications;

/// <summary>
/// Describes the content of a system notification.
/// </summary>
public sealed class NotificationContent
{
    /// <summary>
    /// Gets or sets the notification title (first line, bold).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the notification body text.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Gets or sets the optional application icon image.
    /// Accepts any <see cref="ImageSource"/> (e.g. <see cref="BitmapImage"/>, <see cref="RenderTargetBitmap"/>).
    /// The backend will extract pixel data or a temp file path as needed by each platform.
    /// </summary>
    public ImageSource? Icon { get; set; }

    /// <summary>
    /// Gets or sets the optional hero / inline image.
    /// Accepts any <see cref="ImageSource"/> (e.g. <see cref="BitmapImage"/>, <see cref="RenderTargetBitmap"/>).
    /// </summary>
    public ImageSource? Image { get; set; }

    /// <summary>
    /// Gets or sets a tag that uniquely identifies this notification within its group.
    /// Used for replacing / removing a specific notification.
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    /// Gets or sets the group identifier.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Gets or sets how long the notification stays visible before auto-dismissing.
    /// <c>null</c> means the platform default.
    /// </summary>
    public TimeSpan? Expiration { get; set; }

    /// <summary>
    /// Gets or sets whether the notification should play a sound.
    /// </summary>
    public bool Silent { get; set; }

    /// <summary>
    /// Gets or sets the notification priority / urgency.
    /// </summary>
    public NotificationPriority Priority { get; set; } = NotificationPriority.Default;

    /// <summary>
    /// Gets the list of action buttons displayed on the notification.
    /// </summary>
    public List<NotificationAction> Actions { get; } = new();

    /// <summary>
    /// Gets a bag of arbitrary key-value arguments delivered on activation.
    /// </summary>
    public Dictionary<string, string> Arguments { get; } = new();
}

/// <summary>
/// Describes an action button on a system notification.
/// </summary>
public sealed class NotificationAction
{
    /// <summary>
    /// Gets or sets a unique identifier for this action.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the display text of the button.
    /// </summary>
    public required string Label { get; set; }

    /// <summary>
    /// Gets or sets optional arguments delivered when this action is activated.
    /// </summary>
    public Dictionary<string, string>? Arguments { get; set; }

    public NotificationAction() { }

    public NotificationAction(string id, string label)
    {
        Id = id;
        Label = label;
    }
}

/// <summary>
/// Notification priority / urgency level.
/// </summary>
public enum NotificationPriority
{
    /// <summary>Platform default.</summary>
    Default = 0,

    /// <summary>Low urgency – may be delivered silently.</summary>
    Low = 1,

    /// <summary>Normal urgency.</summary>
    Normal = 2,

    /// <summary>High urgency – may bypass Do-Not-Disturb.</summary>
    High = 3
}

/// <summary>
/// Result of a notification activation (user clicked / tapped / pressed a button).
/// </summary>
public sealed class NotificationActivatedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the action ID that was activated, or <c>null</c> if the notification body was clicked.
    /// </summary>
    public string? ActionId { get; init; }

    /// <summary>
    /// Gets the arguments associated with the activation.
    /// </summary>
    public IReadOnlyDictionary<string, string> Arguments { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets any user-input values (e.g. text-box replies on Windows).
    /// </summary>
    public IReadOnlyDictionary<string, string> UserInput { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Reason a notification was dismissed.
/// </summary>
public enum NotificationDismissReason
{
    /// <summary>The user dismissed it.</summary>
    UserCanceled,

    /// <summary>The application hid it programmatically.</summary>
    ApplicationHidden,

    /// <summary>The notification timed out.</summary>
    TimedOut
}

/// <summary>
/// Result of a notification dismissal.
/// </summary>
public sealed class NotificationDismissedEventArgs : EventArgs
{
    public NotificationDismissReason Reason { get; init; }
}
