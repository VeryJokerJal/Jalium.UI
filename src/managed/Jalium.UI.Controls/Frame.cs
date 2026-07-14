using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Jalium.UI.Navigation;

namespace Jalium.UI.Controls;

/// <summary>A content control with URI, object, and typed-page navigation.</summary>
public class Frame : ContentControl
{
    private static readonly DependencyPropertyKey BackStackPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(BackStack), typeof(IEnumerable), typeof(Frame), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey ForwardStackPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(ForwardStack), typeof(IEnumerable), typeof(Frame), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey CanGoBackPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanGoBack), typeof(bool), typeof(Frame), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey CanGoForwardPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanGoForward), typeof(bool), typeof(Frame), new PropertyMetadata(false));

    public static readonly DependencyProperty BackStackProperty = BackStackPropertyKey.DependencyProperty;
    public static readonly DependencyProperty ForwardStackProperty = ForwardStackPropertyKey.DependencyProperty;
    public static readonly DependencyProperty CanGoBackProperty = CanGoBackPropertyKey.DependencyProperty;
    public static readonly DependencyProperty CanGoForwardProperty = CanGoForwardPropertyKey.DependencyProperty;

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(
            nameof(Source),
            typeof(Uri),
            typeof(Frame),
            new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty SandboxExternalContentProperty =
        DependencyProperty.Register(
            nameof(SandboxExternalContent),
            typeof(bool),
            typeof(Frame),
            new PropertyMetadata(false));

    public static readonly DependencyProperty JournalOwnershipProperty =
        DependencyProperty.Register(
            nameof(JournalOwnership),
            typeof(JournalOwnership),
            typeof(Frame),
            new PropertyMetadata(JournalOwnership.Automatic));

    public static readonly DependencyProperty NavigationUIVisibilityProperty =
        DependencyProperty.Register(
            nameof(NavigationUIVisibility),
            typeof(NavigationUIVisibility),
            typeof(Frame),
            new PropertyMetadata(NavigationUIVisibility.Automatic));

    public static readonly DependencyProperty SourcePageTypeProperty =
        DependencyProperty.Register(
            nameof(SourcePageType),
            typeof(Type),
            typeof(Frame),
            new PropertyMetadata(null, OnSourcePageTypeChanged));

    private readonly Dictionary<Type, Page> _pageCache = new();
    private readonly NavigationService _navigationService;
    private bool _updatingNavigationProperties;
    private Uri? _baseUri;

    public Frame()
    {
        _navigationService = new NavigationService(this);
        _navigationService.StateChanged += OnNavigationStateChanged;
        UpdateNavigationState();
    }

    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.FrameAutomationPeer(this);

    [Bindable(true)]
    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Uri? CurrentSource => _navigationService.CurrentSource;

    public NavigationService NavigationService => _navigationService;

    public IEnumerable BackStack => _navigationService.BackStack;

    public IEnumerable ForwardStack => _navigationService.ForwardStack;

    public bool CanGoBack => _navigationService.CanGoBack;

    public bool CanGoForward => _navigationService.CanGoForward;

    public JournalOwnership JournalOwnership
    {
        get => (JournalOwnership)(GetValue(JournalOwnershipProperty) ?? JournalOwnership.Automatic);
        set => SetValue(JournalOwnershipProperty, value);
    }

    public NavigationUIVisibility NavigationUIVisibility
    {
        get => (NavigationUIVisibility)(GetValue(NavigationUIVisibilityProperty) ?? NavigationUIVisibility.Automatic);
        set => SetValue(NavigationUIVisibilityProperty, value);
    }

    public bool SandboxExternalContent
    {
        get => (bool)(GetValue(SandboxExternalContentProperty) ?? false);
        set => SetValue(SandboxExternalContentProperty, value);
    }

    protected virtual Uri? BaseUri
    {
        get => _baseUri;
        set => _baseUri = value;
    }

    public Type? SourcePageType
    {
        get => (Type?)GetValue(SourcePageTypeProperty);
        set => SetValue(SourcePageTypeProperty, value);
    }

    public int BackStackDepth => _navigationService.BackStack.Count();

    public Page? CurrentPage => _navigationService.Content as Page;

    public int CacheSize { get; set; } = 10;

    public Func<Uri, object?>? ContentLoader
    {
        get => _navigationService.ContentLoader;
        set => _navigationService.ContentLoader = value;
    }

    public bool Navigate(Uri source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _navigationService.Navigate(
            ResolveSource(source),
            extraData: null,
            sandboxExternalContent: SandboxExternalContent);
    }

    public bool Navigate(Uri source, object? extraData)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _navigationService.Navigate(
            ResolveSource(source),
            extraData,
            sandboxExternalContent: SandboxExternalContent);
    }

    public bool Navigate(object content)
        => _navigationService.Navigate(content);

    public bool Navigate(object content, object? extraData)
        => _navigationService.Navigate(content, extraData);

    [RequiresUnreferencedCode("Constructs the page type using DI or Activator.CreateInstance.")]
    public bool Navigate(Type sourcePageType)
        => Navigate(sourcePageType, null);

    [RequiresUnreferencedCode("Constructs the page type using DI or Activator.CreateInstance.")]
    public bool Navigate(Type sourcePageType, object? parameter)
    {
        ArgumentNullException.ThrowIfNull(sourcePageType);
        if (!typeof(Page).IsAssignableFrom(sourcePageType))
        {
            return false;
        }

        var page = GetOrCreatePage(sourcePageType);
        return page != null && _navigationService.Navigate(page, parameter);
    }

    public void GoBack() => _navigationService.GoBack();

    public void GoForward() => _navigationService.GoForward();

    public void Refresh() => _navigationService.Refresh();

    public void StopLoading() => _navigationService.StopLoading();

    public void AddBackEntry(CustomContentState state) => _navigationService.AddBackEntry(state);

    public JournalEntry? RemoveBackEntry() => _navigationService.RemoveBackEntry();

    public event NavigatingCancelEventHandler Navigating
    {
        add => _navigationService.Navigating += value;
        remove => _navigationService.Navigating -= value;
    }

    public event NavigatedEventHandler Navigated
    {
        add => _navigationService.Navigated += value;
        remove => _navigationService.Navigated -= value;
    }

    public event NavigationFailedEventHandler NavigationFailed
    {
        add => _navigationService.NavigationFailed += value;
        remove => _navigationService.NavigationFailed -= value;
    }

    public event NavigationProgressEventHandler NavigationProgress
    {
        add => _navigationService.NavigationProgress += value;
        remove => _navigationService.NavigationProgress -= value;
    }

    public event LoadCompletedEventHandler LoadCompleted
    {
        add => _navigationService.LoadCompleted += value;
        remove => _navigationService.LoadCompleted -= value;
    }

    public event NavigationStoppedEventHandler NavigationStopped
    {
        add => _navigationService.NavigationStopped += value;
        remove => _navigationService.NavigationStopped -= value;
    }

    public event FragmentNavigationEventHandler FragmentNavigation
    {
        add => _navigationService.FragmentNavigation += value;
        remove => _navigationService.FragmentNavigation -= value;
    }

    public event EventHandler? ContentRendered;

    protected virtual void OnContentRendered(EventArgs args)
        => ContentRendered?.Invoke(this, args);

    internal void SetNavigationContent(object? content, Uri? source, object? extraData)
    {
        _updatingNavigationProperties = true;
        try
        {
            base.Content = content;
            SetValue(SourceProperty, source);
            SetValue(SourcePageTypeProperty, content is Page ? content.GetType() : null);
        }
        finally
        {
            _updatingNavigationProperties = false;
        }

        OnContentRendered(EventArgs.Empty);
        UpdateNavigationState();
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var frame = (Frame)d;
        if (!frame._updatingNavigationProperties && e.NewValue is Uri source)
        {
            frame.Navigate(source);
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "SourcePageType is an explicit runtime navigation contract; applications must preserve constructors for types assigned to it.")]
    private static void OnSourcePageTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var frame = (Frame)d;
        if (!frame._updatingNavigationProperties && e.NewValue is Type pageType)
        {
            frame.Navigate(pageType);
        }
    }

    private void OnNavigationStateChanged(object? sender, EventArgs e) => UpdateNavigationState();

    private void UpdateNavigationState()
    {
        SetValue(BackStackPropertyKey, _navigationService.BackStack);
        SetValue(ForwardStackPropertyKey, _navigationService.ForwardStack);
        SetValue(CanGoBackPropertyKey, _navigationService.CanGoBack);
        SetValue(CanGoForwardPropertyKey, _navigationService.CanGoForward);
    }

    private Uri ResolveSource(Uri source)
    {
        if (source.IsAbsoluteUri || BaseUri == null)
        {
            return source;
        }

        return new Uri(BaseUri, source);
    }

    [RequiresUnreferencedCode("Constructs the page type using DI or Activator.CreateInstance.")]
    private Page? GetOrCreatePage(Type pageType)
    {
        if (_pageCache.TryGetValue(pageType, out var cachedPage))
        {
            return cachedPage;
        }

        try
        {
            var factory = Application.Current?.Services is { } services
                ? services.GetService(typeof(Hosting.IViewFactory)) as Hosting.IViewFactory
                : null;
            var page = factory?.CreateView(pageType) as Page ?? Activator.CreateInstance(pageType) as Page;
            if (page != null && page.NavigationCacheMode != NavigationCacheMode.Disabled)
            {
                TrimCache();
                _pageCache[pageType] = page;
            }

            return page;
        }
        catch
        {
            return null;
        }
    }

    private void TrimCache()
    {
        while (_pageCache.Count >= Math.Max(1, CacheSize))
        {
            var candidate = _pageCache.FirstOrDefault(static pair => pair.Value.NavigationCacheMode != NavigationCacheMode.Required);
            if (candidate.Value == null)
            {
                return;
            }

            _pageCache.Remove(candidate.Key);
        }
    }
}
