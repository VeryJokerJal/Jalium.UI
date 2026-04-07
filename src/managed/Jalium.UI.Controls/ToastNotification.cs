using Jalium.UI.Media;
using Jalium.UI.Notifications;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a Windows toast notification.
/// </summary>
public sealed class ToastNotification
{
    private readonly string _content;
    private string? _tag;
    private string? _group;
    private DateTimeOffset? _expirationTime;
    private bool _suppressPopup;

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the ToastNotification class with XML content.
    /// </summary>
    /// <param name="content">The XML content for the toast.</param>
    public ToastNotification(string content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the XML content of the toast notification.
    /// </summary>
    public string Content => _content;

    /// <summary>
    /// Gets or sets a tag that uniquely identifies this notification.
    /// </summary>
    public string? Tag
    {
        get => _tag;
        set => _tag = value;
    }

    /// <summary>
    /// Gets or sets the group identifier for this notification.
    /// </summary>
    public string? Group
    {
        get => _group;
        set => _group = value;
    }

    /// <summary>
    /// Gets or sets the expiration time for this notification.
    /// </summary>
    public DateTimeOffset? ExpirationTime
    {
        get => _expirationTime;
        set => _expirationTime = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether to suppress the popup and only show in Action Center.
    /// </summary>
    public bool SuppressPopup
    {
        get => _suppressPopup;
        set => _suppressPopup = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the notification is mirrored.
    /// </summary>
    public NotificationMirroring NotificationMirroring { get; set; } = NotificationMirroring.Allowed;

    /// <summary>
    /// Gets or sets the remote ID for this notification (for universal dismiss scenarios).
    /// </summary>
    public string? RemoteId { get; set; }

    /// <summary>
    /// Gets or sets a priority hint for the notification.
    /// </summary>
    public ToastNotificationPriority Priority { get; set; } = ToastNotificationPriority.Default;

    /// <summary>
    /// Gets or sets a value indicating whether to play sound when the notification is shown.
    /// </summary>
    public bool PlaySound { get; set; } = true;

    /// <summary>
    /// Gets or sets additional data associated with this notification.
    /// </summary>
    public Dictionary<string, string> Data { get; } = new();

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the notification is activated (clicked).
    /// </summary>
    public event EventHandler<ToastActivatedEventArgs>? Activated;

    /// <summary>
    /// Occurs when the notification is dismissed.
    /// </summary>
    public event EventHandler<ToastDismissedEventArgs>? Dismissed;

    /// <summary>
    /// Occurs when an error occurs with the notification.
    /// </summary>
    public event EventHandler<ToastFailedEventArgs>? Failed;

    #endregion

    #region Event Raising

    /// <summary>
    /// Raises the Activated event.
    /// </summary>
    internal void RaiseActivated(string arguments)
    {
        Activated?.Invoke(this, new ToastActivatedEventArgs(arguments));
    }

    /// <summary>
    /// Raises the Dismissed event.
    /// </summary>
    internal void RaiseDismissed(ToastDismissalReason reason)
    {
        Dismissed?.Invoke(this, new ToastDismissedEventArgs(reason));
    }

    /// <summary>
    /// Raises the Failed event.
    /// </summary>
    internal void RaiseFailed(Exception error)
    {
        Failed?.Invoke(this, new ToastFailedEventArgs(error));
    }

    #endregion
}

/// <summary>
/// Provides methods for displaying toast notifications.
/// </summary>
public static class ToastNotificationManager
{
    private static string _appId = "Jalium.UI.App";

    /// <summary>
    /// Gets or sets the application ID used for notifications.
    /// </summary>
    public static string ApplicationId
    {
        get => _appId;
        set => _appId = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Creates a new toast notifier for the current application.
    /// </summary>
    /// <returns>A toast notifier.</returns>
    public static ToastNotifier CreateToastNotifier()
    {
        return new ToastNotifier(_appId);
    }

    /// <summary>
    /// Creates a new toast notifier for the specified application.
    /// </summary>
    /// <param name="applicationId">The application ID.</param>
    /// <returns>A toast notifier.</returns>
    public static ToastNotifier CreateToastNotifier(string applicationId)
    {
        return new ToastNotifier(applicationId);
    }

    /// <summary>
    /// Gets the history of toast notifications for the current application.
    /// </summary>
    /// <returns>The toast notification history.</returns>
    public static ToastNotificationHistory History => new();

    /// <summary>
    /// Gets the default content for a toast notification.
    /// </summary>
    /// <param name="template">The toast template type.</param>
    /// <returns>The XML content string.</returns>
    public static string GetTemplateContent(ToastTemplateType template)
    {
        return template switch
        {
            ToastTemplateType.ToastText01 =>
                "<toast><visual><binding template=\"ToastText01\"><text id=\"1\"></text></binding></visual></toast>",
            ToastTemplateType.ToastText02 =>
                "<toast><visual><binding template=\"ToastText02\"><text id=\"1\"></text><text id=\"2\"></text></binding></visual></toast>",
            ToastTemplateType.ToastText03 =>
                "<toast><visual><binding template=\"ToastText03\"><text id=\"1\"></text><text id=\"2\"></text></binding></visual></toast>",
            ToastTemplateType.ToastText04 =>
                "<toast><visual><binding template=\"ToastText04\"><text id=\"1\"></text><text id=\"2\"></text><text id=\"3\"></text></binding></visual></toast>",
            ToastTemplateType.ToastImageAndText01 =>
                "<toast><visual><binding template=\"ToastImageAndText01\"><image id=\"1\" src=\"\"/><text id=\"1\"></text></binding></visual></toast>",
            ToastTemplateType.ToastImageAndText02 =>
                "<toast><visual><binding template=\"ToastImageAndText02\"><image id=\"1\" src=\"\"/><text id=\"1\"></text><text id=\"2\"></text></binding></visual></toast>",
            ToastTemplateType.ToastImageAndText03 =>
                "<toast><visual><binding template=\"ToastImageAndText03\"><image id=\"1\" src=\"\"/><text id=\"1\"></text><text id=\"2\"></text></binding></visual></toast>",
            ToastTemplateType.ToastImageAndText04 =>
                "<toast><visual><binding template=\"ToastImageAndText04\"><image id=\"1\" src=\"\"/><text id=\"1\"></text><text id=\"2\"></text><text id=\"3\"></text></binding></visual></toast>",
            _ => throw new ArgumentOutOfRangeException(nameof(template))
        };
    }
}

/// <summary>
/// Provides methods for showing and hiding toast notifications.
/// </summary>
public sealed class ToastNotifier
{
    private readonly string _applicationId;

    /// <summary>
    /// Initializes a new instance of the ToastNotifier class.
    /// </summary>
    internal ToastNotifier(string applicationId)
    {
        _applicationId = applicationId;
    }

    /// <summary>
    /// Displays the specified toast notification.
    /// </summary>
    /// <param name="notification">The toast notification to display.</param>
    public void Show(ToastNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ShowInternal(notification);
    }

    /// <summary>
    /// Hides the specified toast notification.
    /// </summary>
    /// <param name="notification">The toast notification to hide.</param>
    public void Hide(ToastNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        HideInternal(notification);
    }

    /// <summary>
    /// Gets the setting that determines whether notifications can be shown.
    /// </summary>
    /// <returns>The notification setting.</returns>
    public NotificationSetting Setting => GetSettingInternal();

    /// <summary>
    /// Adds a scheduled toast notification.
    /// </summary>
    public void AddToSchedule(ScheduledToastNotification scheduledToast)
    {
        ArgumentNullException.ThrowIfNull(scheduledToast);
        AddToScheduleInternal(scheduledToast);
    }

    /// <summary>
    /// Removes a scheduled toast notification.
    /// </summary>
    public void RemoveFromSchedule(ScheduledToastNotification scheduledToast)
    {
        ArgumentNullException.ThrowIfNull(scheduledToast);
        RemoveFromScheduleInternal(scheduledToast);
    }

    /// <summary>
    /// Gets all scheduled toast notifications.
    /// </summary>
    public IReadOnlyList<ScheduledToastNotification> GetScheduledToastNotifications()
    {
        return GetScheduledToastNotificationsInternal();
    }

    #region Platform Implementation Hooks

    /// <summary>
    /// Shows the notification via the cross-platform <see cref="SystemNotificationManager"/>.
    /// </summary>
    private void ShowInternal(ToastNotification notification)
    {
        var svc = SystemNotificationManager.Current;
        if (!svc.IsSupported) return;

        try
        {
            var content = new NotificationContent
            {
                Title = notification.Content, // Content is XML; service will parse as title fallback
                Tag = notification.Tag,
                Group = notification.Group,
                Silent = !notification.PlaySound,
                Priority = notification.Priority == ToastNotificationPriority.High
                    ? NotificationPriority.High : NotificationPriority.Default
            };

            var handle = svc.Show(content);

            handle.Activated += (_, args) =>
                notification.RaiseActivated(args.ActionId ?? string.Empty);
            handle.Dismissed += (_, args) =>
                notification.RaiseDismissed((ToastDismissalReason)(int)args.Reason);
            handle.Failed += (_, ex) =>
                notification.RaiseFailed(ex);
        }
        catch (Exception ex)
        {
            notification.RaiseFailed(ex);
        }
    }

    /// <summary>
    /// Hides the notification via the cross-platform <see cref="NotificationService"/>.
    /// </summary>
    private void HideInternal(ToastNotification notification)
    {
        if (!string.IsNullOrEmpty(notification.Tag))
            SystemNotificationManager.Current.Remove(notification.Tag, notification.Group);
    }

    /// <summary>
    /// Gets the notification setting.
    /// </summary>
    private NotificationSetting GetSettingInternal()
    {
        return SystemNotificationManager.Current.IsSupported
            ? NotificationSetting.Enabled
            : NotificationSetting.DisabledForApplication;
    }

    /// <summary>
    /// Adds to schedule (platform-specific implementation).
    /// </summary>
    private void AddToScheduleInternal(ScheduledToastNotification notification)
    {
        // Scheduled notifications are not yet supported in the cross-platform backend.
    }

    /// <summary>
    /// Removes from schedule (platform-specific implementation).
    /// </summary>
    private void RemoveFromScheduleInternal(ScheduledToastNotification notification)
    {
        // Scheduled notifications are not yet supported in the cross-platform backend.
    }

    /// <summary>
    /// Gets scheduled notifications (platform-specific implementation).
    /// </summary>
    private IReadOnlyList<ScheduledToastNotification> GetScheduledToastNotificationsInternal()
    {
        return Array.Empty<ScheduledToastNotification>();
    }

    #endregion
}

/// <summary>
/// Represents a scheduled toast notification.
/// </summary>
public sealed class ScheduledToastNotification
{
    /// <summary>
    /// Gets the XML content of the notification.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Gets the delivery time.
    /// </summary>
    public DateTimeOffset DeliveryTime { get; }

    /// <summary>
    /// Gets or sets the snooze interval.
    /// </summary>
    public TimeSpan? SnoozeInterval { get; set; }

    /// <summary>
    /// Gets or sets the maximum snooze count.
    /// </summary>
    public int MaximumSnoozeCount { get; set; }

    /// <summary>
    /// Gets or sets the tag.
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    /// Gets or sets the group.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Gets or sets the ID.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to suppress popup.
    /// </summary>
    public bool SuppressPopup { get; set; }

    /// <summary>
    /// Initializes a new instance of the ScheduledToastNotification class.
    /// </summary>
    public ScheduledToastNotification(string content, DateTimeOffset deliveryTime)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        DeliveryTime = deliveryTime;
    }
}

/// <summary>
/// Provides access to the toast notification history.
/// </summary>
public sealed class ToastNotificationHistory
{
    /// <summary>
    /// Removes all notifications for the application.
    /// </summary>
    public void Clear()
    {
        ClearInternal();
    }

    /// <summary>
    /// Removes all notifications with the specified tag.
    /// </summary>
    public void Remove(string tag)
    {
        RemoveInternal(tag, null);
    }

    /// <summary>
    /// Removes all notifications with the specified tag and group.
    /// </summary>
    public void Remove(string tag, string group)
    {
        RemoveInternal(tag, group);
    }

    /// <summary>
    /// Removes all notifications in the specified group.
    /// </summary>
    public void RemoveGroup(string group)
    {
        RemoveGroupInternal(group);
    }

    #region Platform Implementation Hooks

    /// <summary>
    /// Clears all notifications via <see cref="NotificationService"/>.
    /// </summary>
    private void ClearInternal()
    {
        SystemNotificationManager.Current.ClearAll();
    }

    /// <summary>
    /// Removes notifications via <see cref="NotificationService"/>.
    /// </summary>
    private void RemoveInternal(string tag, string? group)
    {
        SystemNotificationManager.Current.Remove(tag, group);
    }

    /// <summary>
    /// Removes group notifications. Delegates to <see cref="NotificationService"/>.
    /// </summary>
    private void RemoveGroupInternal(string group)
    {
        // The cross-platform backend removes by tag+group; removing by group alone
        // is not directly supported. ClearAll is the closest fallback.
        SystemNotificationManager.Current.ClearAll();
    }

    #endregion
}

#region Event Args

/// <summary>
/// Event arguments for toast activation events.
/// </summary>
public sealed class ToastActivatedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the arguments passed when activated.
    /// </summary>
    public string Arguments { get; }

    /// <summary>
    /// Gets the user input from the toast.
    /// </summary>
    public IDictionary<string, string> UserInput { get; }

    /// <summary>
    /// Initializes a new instance of the ToastActivatedEventArgs class.
    /// </summary>
    public ToastActivatedEventArgs(string arguments)
    {
        Arguments = arguments;
        UserInput = new Dictionary<string, string>();
    }
}

/// <summary>
/// Event arguments for toast dismissed events.
/// </summary>
public sealed class ToastDismissedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the reason for dismissal.
    /// </summary>
    public ToastDismissalReason Reason { get; }

    /// <summary>
    /// Initializes a new instance of the ToastDismissedEventArgs class.
    /// </summary>
    public ToastDismissedEventArgs(ToastDismissalReason reason)
    {
        Reason = reason;
    }
}

/// <summary>
/// Event arguments for toast failed events.
/// </summary>
public sealed class ToastFailedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the error that occurred.
    /// </summary>
    public Exception Error { get; }

    /// <summary>
    /// Initializes a new instance of the ToastFailedEventArgs class.
    /// </summary>
    public ToastFailedEventArgs(Exception error)
    {
        Error = error;
    }
}

#endregion

#region Enums

/// <summary>
/// Specifies the reason for toast dismissal.
/// </summary>
public enum ToastDismissalReason
{
    /// <summary>
    /// The user dismissed the notification.
    /// </summary>
    UserCanceled,

    /// <summary>
    /// The application dismissed the notification.
    /// </summary>
    ApplicationHidden,

    /// <summary>
    /// The notification timed out.
    /// </summary>
    TimedOut
}

/// <summary>
/// Specifies the notification mirroring setting.
/// </summary>
public enum NotificationMirroring
{
    /// <summary>
    /// Mirroring is allowed.
    /// </summary>
    Allowed,

    /// <summary>
    /// Mirroring is disabled.
    /// </summary>
    Disabled
}

/// <summary>
/// Specifies the toast notification priority.
/// </summary>
public enum ToastNotificationPriority
{
    /// <summary>
    /// Default priority.
    /// </summary>
    Default,

    /// <summary>
    /// High priority (shows immediately).
    /// </summary>
    High
}

/// <summary>
/// Specifies the notification setting.
/// </summary>
public enum NotificationSetting
{
    /// <summary>
    /// Notifications are enabled.
    /// </summary>
    Enabled,

    /// <summary>
    /// Notifications are disabled by user.
    /// </summary>
    DisabledForUser,

    /// <summary>
    /// Notifications are disabled by application.
    /// </summary>
    DisabledForApplication,

    /// <summary>
    /// Notifications are disabled by group policy.
    /// </summary>
    DisabledByGroupPolicy,

    /// <summary>
    /// Notifications are disabled due to manifest.
    /// </summary>
    DisabledByManifest
}

/// <summary>
/// Specifies the toast template type.
/// </summary>
public enum ToastTemplateType
{
    /// <summary>
    /// Text-only toast with one string.
    /// </summary>
    ToastText01,

    /// <summary>
    /// Text-only toast with two strings.
    /// </summary>
    ToastText02,

    /// <summary>
    /// Text-only toast with two strings (wrapped).
    /// </summary>
    ToastText03,

    /// <summary>
    /// Text-only toast with three strings.
    /// </summary>
    ToastText04,

    /// <summary>
    /// Image and text toast with one string.
    /// </summary>
    ToastImageAndText01,

    /// <summary>
    /// Image and text toast with two strings.
    /// </summary>
    ToastImageAndText02,

    /// <summary>
    /// Image and text toast with two strings (wrapped).
    /// </summary>
    ToastImageAndText03,

    /// <summary>
    /// Image and text toast with three strings.
    /// </summary>
    ToastImageAndText04
}

#endregion
