using System.Collections;
using System.Windows.Input;
using Jalium.UI.Input;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Navigation;

/// <summary>
/// A Window that supports content navigation within a single window.
/// </summary>
[TemplatePart(Name = "PART_NavigationBar", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_ContentPresenter", Type = typeof(ContentPresenter))]
[TemplateVisualState(Name = "NavigationVisible", GroupName = "NavigationStates")]
[TemplateVisualState(Name = "NavigationHidden", GroupName = "NavigationStates")]
public class NavigationWindow : Window, IUriContext
{
    static NavigationWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NavigationWindow), new PropertyMetadata(typeof(NavigationWindow)));
    }
    private Uri? _baseUri;
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.NavigationWindowAutomationPeer(this);
    }

    private NavigationService? _navigationService;
    private object? _presentedContent;
    private bool _updatingNavigationProperties;
    private RelayCommand? _goBackCommand;
    private RelayCommand? _goForwardCommand;
    private RelayCommand? _refreshCommand;

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

    private static readonly DependencyPropertyKey CurrentSourcePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CurrentSource), typeof(Uri), typeof(NavigationWindow), new PropertyMetadata(null));
    public static readonly DependencyProperty CurrentSourceProperty = CurrentSourcePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey GoBackCommandPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(GoBackCommand), typeof(ICommand), typeof(NavigationWindow), new PropertyMetadata(null));
    public static readonly DependencyProperty GoBackCommandProperty = GoBackCommandPropertyKey.DependencyProperty;
    public ICommand GoBackCommand => (ICommand)GetValue(GoBackCommandProperty)!;

    private static readonly DependencyPropertyKey GoForwardCommandPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(GoForwardCommand), typeof(ICommand), typeof(NavigationWindow), new PropertyMetadata(null));
    public static readonly DependencyProperty GoForwardCommandProperty = GoForwardCommandPropertyKey.DependencyProperty;
    public ICommand GoForwardCommand => (ICommand)GetValue(GoForwardCommandProperty)!;

    private static readonly DependencyPropertyKey RefreshCommandPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(RefreshCommand), typeof(ICommand), typeof(NavigationWindow), new PropertyMetadata(null));
    public static readonly DependencyProperty RefreshCommandProperty = RefreshCommandPropertyKey.DependencyProperty;
    public ICommand RefreshCommand => (ICommand)GetValue(RefreshCommandProperty)!;

    public static readonly DependencyProperty NavigationBarBackgroundProperty = DependencyProperty.Register(
        nameof(NavigationBarBackground), typeof(Brush), typeof(NavigationWindow), new PropertyMetadata(null));
    public Brush? NavigationBarBackground { get => (Brush?)GetValue(NavigationBarBackgroundProperty); set => SetValue(NavigationBarBackgroundProperty, value); }

    public static readonly DependencyProperty NavigationBarBorderBrushProperty = DependencyProperty.Register(
        nameof(NavigationBarBorderBrush), typeof(Brush), typeof(NavigationWindow), new PropertyMetadata(null));
    public Brush? NavigationBarBorderBrush { get => (Brush?)GetValue(NavigationBarBorderBrushProperty); set => SetValue(NavigationBarBorderBrushProperty, value); }

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
    public Uri? CurrentSource => (Uri?)GetValue(CurrentSourceProperty);

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
        _goBackCommand = new RelayCommand(GoBack, () => CanGoBack);
        _goForwardCommand = new RelayCommand(GoForward, () => CanGoForward);
        _refreshCommand = new RelayCommand(Refresh, () => NavigationService.Content != null);
        SetValue(GoBackCommandPropertyKey, _goBackCommand);
        SetValue(GoForwardCommandPropertyKey, _goForwardCommand);
        SetValue(RefreshCommandPropertyKey, _refreshCommand);
        _navigationService ??= CreateNavigationService();
        UpdateJournalProperties();
        AddHandler(KeyDownEvent, new KeyEventHandler(OnNavigationKeyDown));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateNavigationStates();
    }

    #endregion

    #region Content Override

    /// <summary>
    /// Gets or sets the content to display.
    /// This is redirected to navigate to the content.
    /// </summary>
    public new object? Content
    {
        get => base.Content;
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
        var service = new NavigationService(this);
        service.StateChanged += OnNavigationStateChanged;
        return service;
    }

    private void OnNavigationStateChanged(object? sender, EventArgs e) => UpdateJournalProperties();

    private void UpdateJournalProperties()
    {
        var service = NavigationService;
        SetValue(CanGoBackPropertyKey, service.CanGoBack);
        SetValue(CanGoForwardPropertyKey, service.CanGoForward);
        SetValue(BackStackPropertyKey, service.BackStack);
        SetValue(ForwardStackPropertyKey, service.ForwardStack);
        SetValue(CurrentSourcePropertyKey, service.CurrentSource);
        _goBackCommand?.RaiseCanExecuteChanged();
        _goForwardCommand?.RaiseCanExecuteChanged();
        _refreshCommand?.RaiseCanExecuteChanged();
    }

    internal void SetNavigationContent(object? content, Uri? source)
    {
        var wasUpdating = _updatingNavigationProperties;
        _updatingNavigationProperties = true;
        try
        {
            SetCurrentValue(ContentProperty, content);
            if (!ReferenceEquals(NavigationService.Content, content)) return;
            SetCurrentValue(SourceProperty, source);
            PresentContent(content);
            if (!ReferenceEquals(NavigationService.Content, content)) return;
        }
        finally { _updatingNavigationProperties = wasUpdating; }

        if (content is Page page && !string.IsNullOrEmpty(page.Title))
            SetCurrentValue(TitleProperty, page.Title);
        UpdateJournalProperties();
    }

    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        if (_updatingNavigationProperties)
        {
            PresentContent(newContent);
            return;
        }

        // A Binding writes the inherited Content DP directly, bypassing the CLR
        // navigation setter. Keep that path on the same journal and cancellation
        // contract as Navigate(). Null retains the existing CLR setter behavior.
        if (newContent != null && NavigationService.Navigate(newContent)) return;
        SetNavigationContent(NavigationService.Content, NavigationService.CurrentSource);
    }

    private void PresentContent(object? content)
    {
        if (ReferenceEquals(_presentedContent, content)) return;
        var previous = _presentedContent;
        _presentedContent = content;
        base.OnContentChanged(previous, content);
    }

    private void UpdateNavigationStates()
        => VisualStateManager.GoToState(this, ShowsNavigationUI ? "NavigationVisible" : "NavigationHidden", false);

    private void OnNavigationKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || !IsEnabled) return;
        ICommand? command = e.Key switch
        {
            Key.BrowserBack => GoBackCommand,
            Key.BrowserForward => GoForwardCommand,
            Key.Left when e.KeyboardModifiers == ModifierKeys.Alt => GoBackCommand,
            Key.Right when e.KeyboardModifiers == ModifierKeys.Alt => GoForwardCommand,
            Key.F5 => RefreshCommand,
            Key.R when e.KeyboardModifiers == ModifierKeys.Control => RefreshCommand,
            _ => null,
        };
        if (command?.CanExecute(null) != true) return;
        command.Execute(null);
        e.Handled = true;
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var window = (NavigationWindow)d;
        if (!window._updatingNavigationProperties && e.NewValue is Uri source && !window.Navigate(source))
            window.SetNavigationContent(window.NavigationService.Content, window.NavigationService.CurrentSource);
    }

    private static void OnShowsNavigationUIChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((NavigationWindow)d).UpdateNavigationStates();

    #endregion
}
