using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// A control that supports navigation to and from pages.
/// </summary>
public class Frame : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.FrameAutomationPeer(this);
    }

    #region Fields

    private readonly Stack<PageStackEntry> _backStack = new();
    private readonly Stack<PageStackEntry> _forwardStack = new();
    private readonly Dictionary<Type, Page> _pageCache = new();
    private Page? _currentPage;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the SourcePageType dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SourcePageTypeProperty =
        DependencyProperty.Register(nameof(SourcePageType), typeof(Type), typeof(Frame),
            new PropertyMetadata(null));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the type of the current source page.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Type? SourcePageType
    {
        get => GetValue(SourcePageTypeProperty) as Type;
        set => SetValue(SourcePageTypeProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether there is at least one entry in back navigation history.
    /// </summary>
    public bool CanGoBack => _backStack.Count > 0;

    /// <summary>
    /// Gets a value indicating whether there is at least one entry in forward navigation history.
    /// </summary>
    public bool CanGoForward => _forwardStack.Count > 0;

    /// <summary>
    /// Gets the number of entries in the back stack.
    /// </summary>
    public int BackStackDepth => _backStack.Count;

    /// <summary>
    /// Gets the current page.
    /// </summary>
    public Page? CurrentPage => _currentPage;

    /// <summary>
    /// Gets or sets the cache size (number of pages to keep in cache).
    /// </summary>
    public int CacheSize { get; set; } = 10;

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="Frame"/> class.
    /// </summary>
    public Frame()
    {
    }

    #region Navigation Methods

    /// <summary>
    /// Navigates to the specified page type.
    /// </summary>
    /// <param name="sourcePageType">The type of page to navigate to.</param>
    /// <returns>True if navigation was successful; otherwise, false.</returns>
    public bool Navigate(Type sourcePageType)
    {
        return Navigate(sourcePageType, null);
    }

    /// <summary>
    /// Navigates to the specified page type with a parameter.
    /// </summary>
    /// <param name="sourcePageType">The type of page to navigate to.</param>
    /// <param name="parameter">The navigation parameter.</param>
    /// <returns>True if navigation was successful; otherwise, false.</returns>
    public bool Navigate(Type sourcePageType, object? parameter)
    {
        if (sourcePageType == null || !typeof(Page).IsAssignableFrom(sourcePageType))
        {
            return false;
        }

        // Check if navigation can proceed
        if (_currentPage != null)
        {
            var cancelArgs = new NavigatingCancelEventArgs(parameter, NavigationMode.New, sourcePageType);
            _currentPage.OnNavigatingFrom(cancelArgs);
            Navigating?.Invoke(this, cancelArgs);

            if (cancelArgs.Cancel)
            {
                return false;
            }
        }

        // Save current page to back stack
        if (_currentPage != null)
        {
            _backStack.Push(new PageStackEntry(_currentPage.GetType(), _currentPage.NavigationParameter));
            _currentPage.OnNavigatedFrom(new NavigationEventArgs(_currentPage, parameter, NavigationMode.New, sourcePageType));
        }

        // Clear forward stack on new navigation
        _forwardStack.Clear();

        // Get or create the page
        var page = GetOrCreatePage(sourcePageType);
        if (page == null)
        {
            return false;
        }

        // Set up the new page
        page.Frame = this;
        page.NavigationParameter = parameter;
        _currentPage = page;
        Content = page;
        SourcePageType = sourcePageType;

        // Notify the page
        page.OnNavigatedTo(new NavigationEventArgs(page, parameter, NavigationMode.New, sourcePageType));
        Navigated?.Invoke(this, new NavigationEventArgs(page, parameter, NavigationMode.New, sourcePageType));

        return true;
    }

    /// <summary>
    /// Navigates to the most recent entry in the back navigation history.
    /// </summary>
    /// <returns>True if navigation was successful; otherwise, false.</returns>
    public bool GoBack()
    {
        if (!CanGoBack)
        {
            return false;
        }

        var entry = _backStack.Pop();

        // Check if navigation can proceed
        if (_currentPage != null)
        {
            var cancelArgs = new NavigatingCancelEventArgs(entry.Parameter, NavigationMode.Back, entry.SourcePageType);
            _currentPage.OnNavigatingFrom(cancelArgs);
            Navigating?.Invoke(this, cancelArgs);

            if (cancelArgs.Cancel)
            {
                _backStack.Push(entry);
                return false;
            }
        }

        // Save current page to forward stack
        if (_currentPage != null)
        {
            _forwardStack.Push(new PageStackEntry(_currentPage.GetType(), _currentPage.NavigationParameter));
            _currentPage.OnNavigatedFrom(new NavigationEventArgs(_currentPage, entry.Parameter, NavigationMode.Back, entry.SourcePageType));
        }

        // Get or create the page
        var page = GetOrCreatePage(entry.SourcePageType);
        if (page == null)
        {
            return false;
        }

        // Set up the page
        page.Frame = this;
        page.NavigationParameter = entry.Parameter;
        _currentPage = page;
        Content = page;
        SourcePageType = entry.SourcePageType;

        // Notify the page
        page.OnNavigatedTo(new NavigationEventArgs(page, entry.Parameter, NavigationMode.Back, entry.SourcePageType));
        Navigated?.Invoke(this, new NavigationEventArgs(page, entry.Parameter, NavigationMode.Back, entry.SourcePageType));

        return true;
    }

    /// <summary>
    /// Navigates to the most recent entry in the forward navigation history.
    /// </summary>
    /// <returns>True if navigation was successful; otherwise, false.</returns>
    public bool GoForward()
    {
        if (!CanGoForward)
        {
            return false;
        }

        var entry = _forwardStack.Pop();

        // Check if navigation can proceed
        if (_currentPage != null)
        {
            var cancelArgs = new NavigatingCancelEventArgs(entry.Parameter, NavigationMode.Forward, entry.SourcePageType);
            _currentPage.OnNavigatingFrom(cancelArgs);
            Navigating?.Invoke(this, cancelArgs);

            if (cancelArgs.Cancel)
            {
                _forwardStack.Push(entry);
                return false;
            }
        }

        // Save current page to back stack
        if (_currentPage != null)
        {
            _backStack.Push(new PageStackEntry(_currentPage.GetType(), _currentPage.NavigationParameter));
            _currentPage.OnNavigatedFrom(new NavigationEventArgs(_currentPage, entry.Parameter, NavigationMode.Forward, entry.SourcePageType));
        }

        // Get or create the page
        var page = GetOrCreatePage(entry.SourcePageType);
        if (page == null)
        {
            return false;
        }

        // Set up the page
        page.Frame = this;
        page.NavigationParameter = entry.Parameter;
        _currentPage = page;
        Content = page;
        SourcePageType = entry.SourcePageType;

        // Notify the page
        page.OnNavigatedTo(new NavigationEventArgs(page, entry.Parameter, NavigationMode.Forward, entry.SourcePageType));
        Navigated?.Invoke(this, new NavigationEventArgs(page, entry.Parameter, NavigationMode.Forward, entry.SourcePageType));

        return true;
    }

    private Page? GetOrCreatePage(Type pageType)
    {
        // Check cache first
        if (_pageCache.TryGetValue(pageType, out var cachedPage))
        {
            return cachedPage;
        }

        // Create new instance
        try
        {
            var page = Activator.CreateInstance(pageType) as Page;
            if (page != null && page.NavigationCacheMode != NavigationCacheMode.Disabled)
            {
                // Manage cache size
                while (_pageCache.Count >= CacheSize)
                {
                    var oldest = _pageCache.First();
                    if (oldest.Value.NavigationCacheMode != NavigationCacheMode.Required)
                    {
                        _pageCache.Remove(oldest.Key);
                    }
                    else
                    {
                        break;
                    }
                }

                _pageCache[pageType] = page;
            }

            return page;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when navigation is about to occur.
    /// </summary>
    public event EventHandler<NavigatingCancelEventArgs>? Navigating;

    /// <summary>
    /// Occurs when navigation has completed.
    /// </summary>
    public event EventHandler<NavigationEventArgs>? Navigated;

    #endregion

    #region Nested Types

    private readonly struct PageStackEntry
    {
        [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
        public Type SourcePageType { get; }
        public object? Parameter { get; }

        public PageStackEntry(Type sourcePageType, object? parameter)
        {
            SourcePageType = sourcePageType;
            Parameter = parameter;
        }
    }

    #endregion
}
