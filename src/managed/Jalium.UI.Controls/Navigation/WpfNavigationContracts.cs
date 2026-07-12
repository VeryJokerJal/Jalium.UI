using System.ComponentModel;
using System.Net;
using System.Runtime.Serialization;

namespace Jalium.UI.Navigation;

/// <summary>
/// Specifies how a navigation was initiated.
/// </summary>
public enum NavigationMode : byte
{
    New,
    Back,
    Forward,
    Refresh
}

/// <summary>
/// Specifies which journal a <see cref="Controls.Frame"/> uses.
/// </summary>
[Serializable]
public enum JournalOwnership
{
    Automatic = 0,
    OwnsJournal,
    UsesParentJournal
}

/// <summary>
/// Specifies when a frame's navigation chrome is visible.
/// </summary>
public enum NavigationUIVisibility
{
    Automatic = 0,
    Visible,
    Hidden
}

public delegate void NavigatingCancelEventHandler(object sender, NavigatingCancelEventArgs e);
public delegate void NavigatedEventHandler(object sender, NavigationEventArgs e);
public delegate void NavigationFailedEventHandler(object sender, NavigationFailedEventArgs e);
public delegate void NavigationProgressEventHandler(object sender, NavigationProgressEventArgs e);
public delegate void LoadCompletedEventHandler(object sender, NavigationEventArgs e);
public delegate void NavigationStoppedEventHandler(object sender, NavigationEventArgs e);
public delegate void FragmentNavigationEventHandler(object sender, FragmentNavigationEventArgs e);

/// <summary>
/// Provides information about a completed, stopped, or loaded navigation.
/// </summary>
public class NavigationEventArgs : EventArgs
{
    internal NavigationEventArgs(
        Uri? uri,
        object? content,
        object? extraData,
        WebResponse? webResponse,
        object navigator,
        bool isNavigationInitiator)
    {
        Uri = uri;
        Content = content;
        ExtraData = extraData;
        WebResponse = webResponse;
        Navigator = navigator;
        IsNavigationInitiator = isNavigationInitiator;
    }

    public Uri? Uri { get; }
    public object? Content { get; }
    public bool IsNavigationInitiator { get; }
    public object? ExtraData { get; }
    public WebResponse? WebResponse { get; }
    public object Navigator { get; }
}

/// <summary>
/// Provides information for a cancellable navigation.
/// </summary>
public class NavigatingCancelEventArgs : CancelEventArgs
{
    internal NavigatingCancelEventArgs(
        Uri? uri,
        object? content,
        CustomContentState? targetContentState,
        object? extraData,
        NavigationMode navigationMode,
        WebRequest? webRequest,
        object navigator,
        bool isNavigationInitiator)
    {
        Uri = uri;
        Content = content;
        TargetContentState = targetContentState;
        ExtraData = extraData;
        NavigationMode = navigationMode;
        WebRequest = webRequest;
        Navigator = navigator;
        IsNavigationInitiator = isNavigationInitiator;
    }

    public Uri? Uri { get; }
    public object? Content { get; }
    public CustomContentState? TargetContentState { get; }
    public CustomContentState? ContentStateToSave { get; set; }
    public object? ExtraData { get; }
    public NavigationMode NavigationMode { get; }
    public WebRequest? WebRequest { get; }
    public bool IsNavigationInitiator { get; }
    public object Navigator { get; }
}

/// <summary>
/// Provides information for a failed navigation.
/// </summary>
public class NavigationFailedEventArgs : EventArgs
{
    internal NavigationFailedEventArgs(
        Uri? uri,
        object? extraData,
        object navigator,
        WebRequest? webRequest,
        WebResponse? webResponse,
        Exception exception)
    {
        Uri = uri;
        ExtraData = extraData;
        Navigator = navigator;
        WebRequest = webRequest;
        WebResponse = webResponse;
        Exception = exception;
    }

    public Uri? Uri { get; }
    public object? ExtraData { get; }
    public object Navigator { get; }
    public WebRequest? WebRequest { get; }
    public WebResponse? WebResponse { get; }
    public Exception Exception { get; }
    public bool Handled { get; set; }
}

/// <summary>
/// Provides byte progress for a URI navigation.
/// </summary>
public class NavigationProgressEventArgs : EventArgs
{
    internal NavigationProgressEventArgs(Uri? uri, long bytesRead, long maxBytes, object navigator)
    {
        Uri = uri;
        BytesRead = bytesRead;
        MaxBytes = maxBytes;
        Navigator = navigator;
    }

    public Uri? Uri { get; }
    public long BytesRead { get; }
    public long MaxBytes { get; }
    public object Navigator { get; }
}

/// <summary>
/// Provides information for navigation to a URI fragment.
/// </summary>
public class FragmentNavigationEventArgs : EventArgs
{
    internal FragmentNavigationEventArgs(string fragment, object navigator)
    {
        Fragment = fragment;
        Navigator = navigator;
    }

    public string Fragment { get; }
    public bool Handled { get; set; }
    public object Navigator { get; }
}

/// <summary>
/// Captures application-defined view state for a journal entry.
/// </summary>
[Serializable]
public abstract class CustomContentState
{
    public virtual string? JournalEntryName => null;

    public abstract void Replay(NavigationService navigationService, NavigationMode mode);
}

/// <summary>
/// Supplies view state when the current content is journaled.
/// </summary>
public interface IProvideCustomContentState
{
    CustomContentState? GetContentState();
}

#pragma warning disable SYSLIB0050 // WPF's public JournalEntry contract is formatter-serializable.

/// <summary>
/// Represents an entry in a navigation journal.
/// </summary>
[Serializable]
public class JournalEntry : DependencyObject, ISerializable
{
    public static readonly DependencyProperty NameProperty =
        DependencyProperty.RegisterAttached(
            "Name",
            typeof(string),
            typeof(JournalEntry),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty KeepAliveProperty =
        DependencyProperty.RegisterAttached(
            "KeepAlive",
            typeof(bool),
            typeof(JournalEntry),
            new PropertyMetadata(false));

    internal JournalEntry(Uri? source, object? content, object? extraData, bool isObjectNavigation)
    {
        Source = source;
        Content = content;
        ExtraData = extraData;
        IsObjectNavigation = isObjectNavigation;
    }

    /// <summary>
    /// Initializes a journal entry from serialized state.
    /// </summary>
    protected JournalEntry(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        Source = (Uri?)info.GetValue(nameof(Source), typeof(Uri));
        CustomContentState = (CustomContentState?)info.GetValue(nameof(CustomContentState), typeof(CustomContentState));
        Name = info.GetString(nameof(Name)) ?? string.Empty;
        IsObjectNavigation = info.GetBoolean(nameof(IsObjectNavigation));
    }

    public Uri? Source { get; set; }

    public CustomContentState? CustomContentState { get; internal set; }

    public string Name
    {
        get => (string?)GetValue(NameProperty) ?? string.Empty;
        set => SetValue(NameProperty, value);
    }

    internal object? Content { get; set; }
    internal object? ExtraData { get; set; }
    internal bool IsObjectNavigation { get; set; }

    public static string? GetName(DependencyObject? dependencyObject)
        => dependencyObject is null ? null : (string?)dependencyObject.GetValue(NameProperty);

    public static void SetName(DependencyObject dependencyObject, string? name)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        dependencyObject.SetValue(NameProperty, name);
    }

    public static bool GetKeepAlive(DependencyObject dependencyObject)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        return (bool)(dependencyObject.GetValue(KeepAliveProperty) ?? false);
    }

    public static void SetKeepAlive(DependencyObject dependencyObject, bool keepAlive)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        dependencyObject.SetValue(KeepAliveProperty, keepAlive);
    }

    public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        info.AddValue(nameof(Source), Source, typeof(Uri));
        info.AddValue(nameof(CustomContentState), CustomContentState, typeof(CustomContentState));
        info.AddValue(nameof(Name), Name);
        info.AddValue(nameof(IsObjectNavigation), IsObjectNavigation);
    }
}

#pragma warning restore SYSLIB0050
