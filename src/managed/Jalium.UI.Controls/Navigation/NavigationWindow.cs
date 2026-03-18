using Jalium.UI.Media;

namespace Jalium.UI.Controls.Navigation;

/// <summary>
/// A Window that supports content navigation within a single window.
/// </summary>
public class NavigationWindow : Window
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.NavigationWindowAutomationPeer(this);
    }

    private NavigationService? _navigationService;
    private Frame? _frame;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Source dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(Uri), typeof(NavigationWindow),
            new PropertyMetadata(null, OnSourceChanged));

    /// <summary>
    /// Identifies the ShowsNavigationUI dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ShowsNavigationUIProperty =
        DependencyProperty.Register(nameof(ShowsNavigationUI), typeof(bool), typeof(NavigationWindow),
            new PropertyMetadata(true, OnShowsNavigationUIChanged));

    /// <summary>
    /// Identifies the SandboxExternalContent dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SandboxExternalContentProperty =
        DependencyProperty.Register(nameof(SandboxExternalContent), typeof(bool), typeof(NavigationWindow),
            new PropertyMetadata(false));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the uniform resource identifier (URI) of the current content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether navigation UI is displayed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool ShowsNavigationUI
    {
        get => (bool)GetValue(ShowsNavigationUIProperty)!;
        set => SetValue(ShowsNavigationUIProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether external content is sandboxed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool SandboxExternalContent
    {
        get => (bool)GetValue(SandboxExternalContentProperty)!;
        set => SetValue(SandboxExternalContentProperty, value);
    }

    /// <summary>
    /// Gets the navigation service that is used by this navigation window.
    /// </summary>
    public NavigationService NavigationService => _navigationService ??= CreateNavigationService();

    /// <summary>
    /// Gets a value that indicates whether there is at least one entry in back navigation history.
    /// </summary>
    public bool CanGoBack => NavigationService.CanGoBack;

    /// <summary>
    /// Gets a value that indicates whether there is at least one entry in forward navigation history.
    /// </summary>
    public bool CanGoForward => NavigationService.CanGoForward;

    /// <summary>
    /// Gets an IEnumerable that can be used to enumerate the entries in back navigation history.
    /// </summary>
    public IEnumerable<JournalEntry> BackStack => NavigationService.BackStack;

    /// <summary>
    /// Gets an IEnumerable that can be used to enumerate the entries in forward navigation history.
    /// </summary>
    public IEnumerable<JournalEntry> ForwardStack => NavigationService.ForwardStack;

    /// <summary>
    /// Gets the URI of the content that was last navigated to.
    /// </summary>
    public Uri? CurrentSource => NavigationService.CurrentSource;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationWindow"/> class.
    /// </summary>
    public NavigationWindow()
    {
        InitializeNavigationWindow();
    }

    private void InitializeNavigationWindow()
    {
        // Create internal frame for content hosting
        _frame = new Frame();

        // Set the frame as the window content
        base.Content = CreateNavigationChrome(_frame);
    }

    #endregion

    #region Content Override

    /// <summary>
    /// Gets or sets the content to display.
    /// This is redirected to navigate to the content.
    /// </summary>
    public new object? Content
    {
        get => _frame?.Content;
        set
        {
            if (value != null)
            {
                NavigationService.Navigate(value);
            }
        }
    }

    #endregion

    #region Navigation Methods

    /// <summary>
    /// Navigates to the content specified by the URI.
    /// </summary>
    /// <param name="source">The URI of the content to navigate to.</param>
    /// <returns>True if navigation is not canceled; otherwise, false.</returns>
    public bool Navigate(Uri source)
    {
        return NavigationService.Navigate(source);
    }

    /// <summary>
    /// Navigates to the content specified by the URI with extra data.
    /// </summary>
    /// <param name="source">The URI of the content to navigate to.</param>
    /// <param name="extraData">Additional data for use by the target.</param>
    /// <returns>True if navigation is not canceled; otherwise, false.</returns>
    public bool Navigate(Uri source, object? extraData)
    {
        return NavigationService.Navigate(source, extraData);
    }

    /// <summary>
    /// Navigates to the specified content object.
    /// </summary>
    /// <param name="content">The object to navigate to.</param>
    /// <returns>True if navigation is not canceled; otherwise, false.</returns>
    public bool Navigate(object content)
    {
        return NavigationService.Navigate(content);
    }

    /// <summary>
    /// Navigates to the specified content object with extra data.
    /// </summary>
    /// <param name="content">The object to navigate to.</param>
    /// <param name="extraData">Additional data for use by the target.</param>
    /// <returns>True if navigation is not canceled; otherwise, false.</returns>
    public bool Navigate(object content, object? extraData)
    {
        return NavigationService.Navigate(content, extraData);
    }

    /// <summary>
    /// Navigates to the most recent entry in back navigation history.
    /// </summary>
    public void GoBack()
    {
        NavigationService.GoBack();
    }

    /// <summary>
    /// Navigates to the most recent entry in forward navigation history.
    /// </summary>
    public void GoForward()
    {
        NavigationService.GoForward();
    }

    /// <summary>
    /// Reloads the current page.
    /// </summary>
    public void Refresh()
    {
        NavigationService.Refresh();
    }

    /// <summary>
    /// Stops any pending navigation.
    /// </summary>
    public void StopLoading()
    {
        NavigationService.StopLoading();
    }

    /// <summary>
    /// Adds an entry to back navigation history that contains a CustomContentState object.
    /// </summary>
    /// <param name="state">The state to add.</param>
    public void AddBackEntry(CustomContentState state)
    {
        NavigationService.AddBackEntry(state);
    }

    /// <summary>
    /// Removes the most recent journal entry from back history.
    /// </summary>
    /// <returns>The most recent JournalEntry in back navigation history.</returns>
    public JournalEntry? RemoveBackEntry()
    {
        return NavigationService.RemoveBackEntry();
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when a new navigation is requested.
    /// </summary>
    public event EventHandler<NavigatingCancelEventArgs>? Navigating
    {
        add => NavigationService.Navigating += value;
        remove => NavigationService.Navigating -= value;
    }

    /// <summary>
    /// Occurs when the content that is being navigated to has been found.
    /// </summary>
    public event EventHandler<NavigationEventArgs>? Navigated
    {
        add => NavigationService.Navigated += value;
        remove => NavigationService.Navigated -= value;
    }

    /// <summary>
    /// Occurs when an error is raised while navigating to the requested content.
    /// </summary>
    public event EventHandler<NavigationFailedEventArgs>? NavigationFailed
    {
        add => NavigationService.NavigationFailed += value;
        remove => NavigationService.NavigationFailed -= value;
    }

    /// <summary>
    /// Occurs when the content that was navigated to has been loaded.
    /// </summary>
    public event EventHandler<NavigationEventArgs>? LoadCompleted
    {
        add => NavigationService.LoadCompleted += value;
        remove => NavigationService.LoadCompleted -= value;
    }

    /// <summary>
    /// Occurs when the StopLoading method is called.
    /// </summary>
    public event EventHandler<NavigationEventArgs>? NavigationStopped
    {
        add => NavigationService.NavigationStopped += value;
        remove => NavigationService.NavigationStopped -= value;
    }

    /// <summary>
    /// Occurs periodically during download of content.
    /// </summary>
    public event EventHandler<NavigationProgressEventArgs>? NavigationProgress
    {
        add => NavigationService.NavigationProgress += value;
        remove => NavigationService.NavigationProgress -= value;
    }

    /// <summary>
    /// Occurs when navigation to a content fragment begins.
    /// </summary>
    public event EventHandler<FragmentNavigationEventArgs>? FragmentNavigation
    {
        add => NavigationService.FragmentNavigation += value;
        remove => NavigationService.FragmentNavigation -= value;
    }

    #endregion

    #region Private Methods

    private NavigationService CreateNavigationService()
    {
        var service = new NavigationService();

        // Wire up navigation events
        service.Navigated += OnNavigationServiceNavigated;

        return service;
    }

    private void OnNavigationServiceNavigated(object? sender, NavigationEventArgs e)
    {
        // Update the frame content
        if (_frame != null)
        {
            _frame.Content = e.Content;
        }

        // Update window title from page title
        if (e.Content is Page page && !string.IsNullOrEmpty(page.Title))
        {
            Title = page.Title;
        }
    }

    private FrameworkElement CreateNavigationChrome(Frame frame)
    {
        if (!ShowsNavigationUI)
        {
            return frame;
        }

        // Create navigation chrome with back/forward buttons
        var grid = new Grid();

        // Navigation bar row
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        // Content row
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Create navigation bar
        var navigationBar = CreateNavigationBar();
        Grid.SetRow(navigationBar, 0);
        grid.Children.Add(navigationBar);

        // Add frame
        Grid.SetRow(frame, 1);
        grid.Children.Add(frame);

        return grid;
    }

    private FrameworkElement CreateNavigationBar()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 44,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Helper to create a compact chrome button with consistent styling.
        Button CreateChromeButton(string glyph, Thickness margin)
        {
            return new Button
            {
                Content = glyph,
                Width = 36,
                Height = 36,
                MinWidth = 36,
                MinHeight = 36,
                Padding = new Thickness(0),
                Margin = margin,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 16
            };
        }

        // Back button
        var backButton = CreateChromeButton("\u2190", new Thickness(8, 0, 2, 0));
        backButton.Click += (s, e) =>
        {
            if (CanGoBack)
            {
                GoBack();
            }
        };
        panel.Children.Add(backButton);

        // Forward button
        var forwardButton = CreateChromeButton("\u2192", new Thickness(0, 0, 2, 0));
        forwardButton.Click += (s, e) =>
        {
            if (CanGoForward)
            {
                GoForward();
            }
        };
        panel.Children.Add(forwardButton);

        // Refresh button
        var refreshButton = CreateChromeButton("\u21BB", new Thickness(0, 0, 8, 0));
        refreshButton.Click += (s, e) => Refresh();
        panel.Children.Add(refreshButton);

        // Wrap in a border with theme-aware background and bottom divider
        var border = new Border
        {
            Background = TryFindResource("LayerFillColorDefaultBrush") as Brush
                ?? new SolidColorBrush(Color.FromArgb(0x4C, 0x3A, 0x3A, 0x3A)),
            BorderBrush = TryFindResource("ControlBorder") as Brush
                ?? new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 2, 0, 2),
            Child = panel
        };

        return border;
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationWindow window && e.NewValue is Uri source)
        {
            window.NavigationService.Navigate(source);
        }
    }

    private static void OnShowsNavigationUIChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationWindow window)
        {
            // Recreate navigation chrome
            if (window._frame != null)
            {
                window.SetValue(Window.ContentProperty, window.CreateNavigationChrome(window._frame));
            }
        }
    }

    #endregion
}
