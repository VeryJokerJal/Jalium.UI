using System.Collections;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Navigation;

/// <summary>
/// A Window that supports content navigation within a single window.
/// </summary>
public class NavigationWindow : Window, IUriContext
{
    private Uri? _baseUri;
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.NavigationWindowAutomationPeer(this);
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

    private static readonly DependencyPropertyKey CanGoBackPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CanGoBack), typeof(bool), typeof(NavigationWindow), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey CanGoForwardPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CanGoForward), typeof(bool), typeof(NavigationWindow), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey BackStackPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(BackStack), typeof(IEnumerable), typeof(NavigationWindow), new PropertyMetadata(null));

    private static readonly DependencyPropertyKey ForwardStackPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ForwardStack), typeof(IEnumerable), typeof(NavigationWindow), new PropertyMetadata(null));

    public static readonly DependencyProperty CanGoBackProperty = CanGoBackPropertyKey.DependencyProperty;
    public static readonly DependencyProperty CanGoForwardProperty = CanGoForwardPropertyKey.DependencyProperty;
    public static readonly DependencyProperty BackStackProperty = BackStackPropertyKey.DependencyProperty;
    public static readonly DependencyProperty ForwardStackProperty = ForwardStackPropertyKey.DependencyProperty;

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
    public bool CanGoBack => (bool)(GetValue(CanGoBackProperty) ?? false);

    /// <summary>
    /// Gets a value that indicates whether there is at least one entry in forward navigation history.
    /// </summary>
    public bool CanGoForward => (bool)(GetValue(CanGoForwardProperty) ?? false);

    /// <summary>
    /// Gets an IEnumerable that can be used to enumerate the entries in back navigation history.
    /// </summary>
    public IEnumerable BackStack =>
        (IEnumerable?)GetValue(BackStackProperty) ?? Array.Empty<JournalEntry>();

    /// <summary>
    /// Gets an IEnumerable that can be used to enumerate the entries in forward navigation history.
    /// </summary>
    public IEnumerable ForwardStack =>
        (IEnumerable?)GetValue(ForwardStackProperty) ?? Array.Empty<JournalEntry>();

    /// <summary>
    /// Gets the URI of the content that was last navigated to.
    /// </summary>
    public Uri? CurrentSource => NavigationService.CurrentSource;

    Uri? IUriContext.BaseUri
    {
        get => _baseUri;
        set => _baseUri = value;
    }

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

        _navigationService = CreateNavigationService();
        UpdateJournalProperties();

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

    /// <inheritdoc />
    protected override void AddChild(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Navigate(value);
    }

    /// <inheritdoc />
    protected override void AddText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("NavigationWindow cannot contain non-whitespace text.", nameof(text));
        }
    }

    /// <inheritdoc />
    public override bool ShouldSerializeContent() => Source is null && Content is not null;

    #endregion

    #region Navigation Methods

    /// <summary>
    /// Navigates to the content specified by the URI.
    /// </summary>
    /// <param name="source">The URI of the content to navigate to.</param>
    /// <returns>True if navigation is not canceled; otherwise, false.</returns>
    public bool Navigate(Uri source)
    {
        bool navigated = NavigationService.Navigate(
            source,
            extraData: null,
            sandboxExternalContent: SandboxExternalContent);
        UpdateJournalProperties();
        return navigated;
    }

    /// <summary>
    /// Navigates to the content specified by the URI with extra data.
    /// </summary>
    /// <param name="source">The URI of the content to navigate to.</param>
    /// <param name="extraData">Additional data for use by the target.</param>
    /// <returns>True if navigation is not canceled; otherwise, false.</returns>
    public bool Navigate(Uri source, object? extraData)
    {
        bool navigated = NavigationService.Navigate(
            source,
            extraData,
            sandboxExternalContent: SandboxExternalContent);
        UpdateJournalProperties();
        return navigated;
    }

    /// <summary>
    /// Navigates to the specified content object.
    /// </summary>
    /// <param name="content">The object to navigate to.</param>
    /// <returns>True if navigation is not canceled; otherwise, false.</returns>
    public bool Navigate(object content)
    {
        bool navigated = NavigationService.Navigate(content);
        UpdateJournalProperties();
        return navigated;
    }

    /// <summary>
    /// Navigates to the specified content object with extra data.
    /// </summary>
    /// <param name="content">The object to navigate to.</param>
    /// <param name="extraData">Additional data for use by the target.</param>
    /// <returns>True if navigation is not canceled; otherwise, false.</returns>
    public bool Navigate(object content, object? extraData)
    {
        bool navigated = NavigationService.Navigate(content, extraData);
        UpdateJournalProperties();
        return navigated;
    }

    /// <summary>
    /// Navigates to the most recent entry in back navigation history.
    /// </summary>
    public void GoBack()
    {
        NavigationService.GoBack();
        UpdateJournalProperties();
    }

    /// <summary>
    /// Navigates to the most recent entry in forward navigation history.
    /// </summary>
    public void GoForward()
    {
        NavigationService.GoForward();
        UpdateJournalProperties();
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
        UpdateJournalProperties();
    }

    /// <summary>
    /// Removes the most recent journal entry from back history.
    /// </summary>
    /// <returns>The most recent JournalEntry in back navigation history.</returns>
    public JournalEntry? RemoveBackEntry()
    {
        JournalEntry? entry = NavigationService.RemoveBackEntry();
        UpdateJournalProperties();
        return entry;
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when a new navigation is requested.
    /// </summary>
    public event NavigatingCancelEventHandler? Navigating
    {
        add => NavigationService.Navigating += value;
        remove => NavigationService.Navigating -= value;
    }

    /// <summary>
    /// Occurs when the content that is being navigated to has been found.
    /// </summary>
    public event NavigatedEventHandler? Navigated
    {
        add => NavigationService.Navigated += value;
        remove => NavigationService.Navigated -= value;
    }

    /// <summary>
    /// Occurs when an error is raised while navigating to the requested content.
    /// </summary>
    public event NavigationFailedEventHandler? NavigationFailed
    {
        add => NavigationService.NavigationFailed += value;
        remove => NavigationService.NavigationFailed -= value;
    }

    /// <summary>
    /// Occurs when the content that was navigated to has been loaded.
    /// </summary>
    public event LoadCompletedEventHandler? LoadCompleted
    {
        add => NavigationService.LoadCompleted += value;
        remove => NavigationService.LoadCompleted -= value;
    }

    /// <summary>
    /// Occurs when the StopLoading method is called.
    /// </summary>
    public event NavigationStoppedEventHandler? NavigationStopped
    {
        add => NavigationService.NavigationStopped += value;
        remove => NavigationService.NavigationStopped -= value;
    }

    /// <summary>
    /// Occurs periodically during download of content.
    /// </summary>
    public event NavigationProgressEventHandler? NavigationProgress
    {
        add => NavigationService.NavigationProgress += value;
        remove => NavigationService.NavigationProgress -= value;
    }

    /// <summary>
    /// Occurs when navigation to a content fragment begins.
    /// </summary>
    public event FragmentNavigationEventHandler? FragmentNavigation
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
        service.StateChanged += (_, _) => UpdateJournalProperties();

        return service;
    }

    private void UpdateJournalProperties()
    {
        NavigationService service = NavigationService;
        SetValue(CanGoBackPropertyKey, service.CanGoBack);
        SetValue(CanGoForwardPropertyKey, service.CanGoForward);
        SetValue(BackStackPropertyKey, service.BackStack);
        SetValue(ForwardStackPropertyKey, service.ForwardStack);
    }

    private void OnNavigationServiceNavigated(object? sender, NavigationEventArgs e)
    {
        UpdateJournalProperties();
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
            window.Navigate(source);
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
