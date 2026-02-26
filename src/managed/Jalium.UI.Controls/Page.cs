using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a page that can be navigated to within a Frame or NavigationView.
/// </summary>
public class Page : ContentControl
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(Page),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the NavigationCacheMode dependency property.
    /// </summary>
    public static readonly DependencyProperty NavigationCacheModeProperty =
        DependencyProperty.Register(nameof(NavigationCacheMode), typeof(NavigationCacheMode), typeof(Page),
            new PropertyMetadata(NavigationCacheMode.Disabled));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the title of the page.
    /// </summary>
    public string Title
    {
        get => (string)(GetValue(TitleProperty) ?? string.Empty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the navigation cache mode.
    /// </summary>
    public NavigationCacheMode NavigationCacheMode
    {
        get => (NavigationCacheMode)(GetValue(NavigationCacheModeProperty) ?? NavigationCacheMode.Disabled);
        set => SetValue(NavigationCacheModeProperty, value);
    }

    /// <summary>
    /// Gets the Frame that contains this page, if any.
    /// </summary>
    public Frame? Frame { get; internal set; }

    /// <summary>
    /// Gets the navigation parameter passed when navigating to this page.
    /// </summary>
    public object? NavigationParameter { get; internal set; }

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="Page"/> class.
    /// </summary>
    public Page()
    {
        Background = new SolidColorBrush(Color.Transparent);
    }

    #region Navigation Events

    /// <summary>
    /// Called when the page is navigated to.
    /// </summary>
    /// <param name="e">The navigation event arguments.</param>
    protected internal virtual void OnNavigatedTo(NavigationEventArgs e)
    {
        NavigatedTo?.Invoke(this, e);
    }

    /// <summary>
    /// Called when the page is navigated from.
    /// </summary>
    /// <param name="e">The navigation event arguments.</param>
    protected internal virtual void OnNavigatedFrom(NavigationEventArgs e)
    {
        NavigatedFrom?.Invoke(this, e);
    }

    /// <summary>
    /// Called when the page is about to be navigated from.
    /// </summary>
    /// <param name="e">The navigating cancel event arguments.</param>
    protected internal virtual void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        NavigatingFrom?.Invoke(this, e);
    }

    /// <summary>
    /// Occurs when the page is navigated to.
    /// </summary>
    public event EventHandler<NavigationEventArgs>? NavigatedTo;

    /// <summary>
    /// Occurs when the page is navigated from.
    /// </summary>
    public event EventHandler<NavigationEventArgs>? NavigatedFrom;

    /// <summary>
    /// Occurs when the page is about to be navigated from.
    /// </summary>
    public event EventHandler<NavigatingCancelEventArgs>? NavigatingFrom;

    #endregion
}

/// <summary>
/// Specifies whether a page is cached.
/// </summary>
public enum NavigationCacheMode
{
    /// <summary>
    /// The page is not cached and a new instance is created on each visit.
    /// </summary>
    Disabled,

    /// <summary>
    /// The page is cached and the cached instance is reused for every visit.
    /// </summary>
    Enabled,

    /// <summary>
    /// The page is cached, but the cache is discarded when the cache size limit is exceeded.
    /// </summary>
    Required
}

/// <summary>
/// Provides data for navigation events.
/// </summary>
public sealed class NavigationEventArgs : EventArgs
{
    /// <summary>
    /// Gets the content of the target page.
    /// </summary>
    public object? Content { get; }

    /// <summary>
    /// Gets the navigation parameter.
    /// </summary>
    public object? Parameter { get; }

    /// <summary>
    /// Gets the type of navigation that occurred.
    /// </summary>
    public NavigationMode NavigationMode { get; }

    /// <summary>
    /// Gets the type of the source page.
    /// </summary>
    public Type? SourcePageType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationEventArgs"/> class.
    /// </summary>
    public NavigationEventArgs(object? content, object? parameter, NavigationMode mode, Type? sourcePageType)
    {
        Content = content;
        Parameter = parameter;
        NavigationMode = mode;
        SourcePageType = sourcePageType;
    }
}

/// <summary>
/// Provides data for the NavigatingFrom event.
/// </summary>
public sealed class NavigatingCancelEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets a value indicating whether the navigation should be canceled.
    /// </summary>
    public bool Cancel { get; set; }

    /// <summary>
    /// Gets the navigation parameter.
    /// </summary>
    public object? Parameter { get; }

    /// <summary>
    /// Gets the type of navigation that is occurring.
    /// </summary>
    public NavigationMode NavigationMode { get; }

    /// <summary>
    /// Gets the type of the source page.
    /// </summary>
    public Type? SourcePageType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigatingCancelEventArgs"/> class.
    /// </summary>
    public NavigatingCancelEventArgs(object? parameter, NavigationMode mode, Type? sourcePageType)
    {
        Parameter = parameter;
        NavigationMode = mode;
        SourcePageType = sourcePageType;
    }
}

/// <summary>
/// Specifies the type of navigation.
/// </summary>
public enum NavigationMode
{
    /// <summary>
    /// Navigation is to a new instance of a page.
    /// </summary>
    New,

    /// <summary>
    /// Navigation is going back to a page in the back stack.
    /// </summary>
    Back,

    /// <summary>
    /// Navigation is going forward to a page in the forward stack.
    /// </summary>
    Forward,

    /// <summary>
    /// Navigation is refreshing the current page.
    /// </summary>
    Refresh
}
