using Jalium.UI.Controls;

namespace Jalium.UI.Navigation;

/// <summary>Coordinates content navigation, journaling, and navigation events.</summary>
public sealed class NavigationService
{
    private readonly Stack<JournalEntry> _backStack = new();
    private readonly Stack<JournalEntry> _forwardStack = new();
    private readonly Frame? _frame;
    private object? _content;
    private object? _extraData;
    private Uri? _currentSource;
    private bool _isNavigating;

    public NavigationService()
    {
    }

    /// <summary>Returns the navigation service that owns the supplied element.</summary>
    public static NavigationService GetNavigationService(DependencyObject dependencyObject)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);

        DependencyObject? current = dependencyObject;
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        while (current is not null && visited.Add(current))
        {
            if (current is Frame frame)
            {
                return frame.NavigationService;
            }

            if (current is Page { Frame: not null } page)
            {
                return page.Frame.NavigationService;
            }

            current = LogicalTreeHelper.GetParent(current);
        }

        // WPF's pre-nullable signature returns null when the object is outside a navigator.
        return null!;
    }

    internal NavigationService(Frame frame)
    {
        _frame = frame;
    }

    public static Func<Uri, object?>? DefaultContentLoader { get; set; }

    public Func<Uri, object?>? ContentLoader { get; set; }

    public object? Content => _content;

    public Uri? Source
    {
        get => _currentSource;
        set
        {
            if (value != null && value != _currentSource)
            {
                Navigate(value);
            }
        }
    }

    public Uri? CurrentSource => _currentSource;

    public bool CanGoBack => _backStack.Count != 0;

    public bool CanGoForward => _forwardStack.Count != 0;

    public IEnumerable<JournalEntry> BackStack => _backStack;

    public IEnumerable<JournalEntry> ForwardStack => _forwardStack;

    public bool Navigate(Uri source) => Navigate(source, null);

    public bool Navigate(Uri source, object? extraData)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!TryBeginNavigation(source, content: null, extraData, NavigationMode.New, out var navigatingArgs))
        {
            return false;
        }

        try
        {
            var content = LoadContent(source);
            if (content == null)
            {
                throw new InvalidOperationException($"No content loader could resolve '{source}'.");
            }

            CommitNewNavigation(source, content, extraData, isObjectNavigation: false);
            RaiseProgressAndFragment(source);
            return true;
        }
        catch (Exception exception)
        {
            RaiseNavigationFailed(source, extraData, exception);
            return false;
        }
        finally
        {
            CompleteNavigation();
        }
    }

    public bool Navigate(object content) => Navigate(content, null);

    public bool Navigate(object content, object? extraData)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!TryBeginNavigation(null, content, extraData, NavigationMode.New, out var navigatingArgs))
        {
            return false;
        }

        try
        {
            CommitNewNavigation(null, content, extraData, isObjectNavigation: true);
            return true;
        }
        catch (Exception exception)
        {
            RaiseNavigationFailed(null, extraData, exception);
            return false;
        }
        finally
        {
            CompleteNavigation();
        }
    }

    public void GoBack()
    {
        if (!CanGoBack)
        {
            throw new InvalidOperationException("There is no entry in back navigation history.");
        }

        NavigateJournal(_backStack, _forwardStack, NavigationMode.Back);
    }

    public void GoForward()
    {
        if (!CanGoForward)
        {
            throw new InvalidOperationException("There is no entry in forward navigation history.");
        }

        NavigateJournal(_forwardStack, _backStack, NavigationMode.Forward);
    }

    public void Refresh()
    {
        if (_content == null && _currentSource == null)
        {
            return;
        }

        if (!TryBeginNavigation(_currentSource, _content, _extraData, NavigationMode.Refresh, out _))
        {
            return;
        }

        try
        {
            var content = _currentSource == null ? _content : LoadContent(_currentSource);
            if (content == null)
            {
                throw new InvalidOperationException("The current content could not be refreshed.");
            }

            ApplyContent(_currentSource, content, _extraData, NavigationMode.Refresh);
            RaiseProgressAndFragment(_currentSource);
        }
        catch (Exception exception)
        {
            RaiseNavigationFailed(_currentSource, _extraData, exception);
        }
        finally
        {
            CompleteNavigation();
        }
    }

    public void StopLoading()
    {
        if (!_isNavigating)
        {
            return;
        }

        _isNavigating = false;
        var args = CreateNavigationEventArgs(_currentSource, _content, _extraData);
        NavigationStopped?.Invoke(this, args);
        Application.Current?.RaiseNavigationStopped(args);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddBackEntry(CustomContentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var entry = CreateJournalEntry(_currentSource, _content, _extraData, _currentSource == null);
        entry.CustomContentState = state;
        entry.Name = state.JournalEntryName ?? string.Empty;
        _backStack.Push(entry);
        _forwardStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public JournalEntry? RemoveBackEntry()
    {
        if (_backStack.Count == 0)
        {
            return null;
        }

        var result = _backStack.Pop();
        StateChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public event NavigatingCancelEventHandler? Navigating;
    public event NavigatedEventHandler? Navigated;
    public event NavigationFailedEventHandler? NavigationFailed;
    public event NavigationProgressEventHandler? NavigationProgress;
    public event LoadCompletedEventHandler? LoadCompleted;
    public event NavigationStoppedEventHandler? NavigationStopped;
    public event FragmentNavigationEventHandler? FragmentNavigation;

    internal event EventHandler? StateChanged;

    private bool TryBeginNavigation(
        Uri? uri,
        object? content,
        object? extraData,
        NavigationMode mode,
        out NavigatingCancelEventArgs args)
    {
        args = new NavigatingCancelEventArgs(
            uri,
            content,
            targetContentState: null,
            extraData,
            mode,
            webRequest: null,
            Navigator,
            isNavigationInitiator: true);
        Navigating?.Invoke(this, args);
        Application.Current?.RaiseNavigating(args);
        if (args.Cancel)
        {
            return false;
        }

        _isNavigating = true;
        return true;
    }

    private void CommitNewNavigation(Uri? source, object content, object? extraData, bool isObjectNavigation)
    {
        if (_content != null)
        {
            _backStack.Push(CreateJournalEntry(_currentSource, _content, _extraData, _currentSource == null));
        }

        _forwardStack.Clear();
        ApplyContent(source, content, extraData, NavigationMode.New);
    }

    private void NavigateJournal(
        Stack<JournalEntry> sourceStack,
        Stack<JournalEntry> destinationStack,
        NavigationMode mode)
    {
        var entry = sourceStack.Pop();
        if (!TryBeginNavigation(entry.Source, entry.Content, entry.ExtraData, mode, out _))
        {
            sourceStack.Push(entry);
            return;
        }

        var oldSource = _currentSource;
        var oldContent = _content;
        var oldExtraData = _extraData;
        JournalEntry? destinationEntry = null;
        try
        {
            if (_content != null)
            {
                destinationEntry = CreateJournalEntry(_currentSource, _content, _extraData, _currentSource == null);
                destinationStack.Push(destinationEntry);
            }

            var content = entry.Content ?? LoadContent(entry.Source);
            if (content == null)
            {
                throw new InvalidOperationException("The journal entry content could not be restored.");
            }

            ApplyContent(entry.Source, content, entry.ExtraData, mode);
            entry.CustomContentState?.Replay(this, mode);
            RaiseProgressAndFragment(entry.Source);
        }
        catch (Exception exception)
        {
            if (destinationEntry != null && destinationStack.Count > 0 && ReferenceEquals(destinationStack.Peek(), destinationEntry))
            {
                destinationStack.Pop();
            }

            sourceStack.Push(entry);
            _currentSource = oldSource;
            _content = oldContent;
            _extraData = oldExtraData;
            _frame?.SetNavigationContent(oldContent, oldSource, oldExtraData);
            RaiseNavigationFailed(entry.Source, entry.ExtraData, exception);
        }
        finally
        {
            CompleteNavigation();
        }
    }

    private void ApplyContent(Uri? source, object content, object? extraData, NavigationMode mode)
    {
        var previous = _content;
        if (previous is Page oldPage)
        {
            oldPage.OnNavigatedFrom(new Controls.NavigationEventArgs(
                previous,
                extraData,
                (Controls.NavigationMode)(int)mode,
                content.GetType()));
        }

        _currentSource = source;
        _content = content;
        _extraData = extraData;

        if (content is Page newPage)
        {
            if (_frame != null)
            {
                newPage.Frame = _frame;
            }

            newPage.NavigationParameter = extraData;
            newPage.OnNavigatedTo(new Controls.NavigationEventArgs(
                content,
                extraData,
                (Controls.NavigationMode)(int)mode,
                content.GetType()));
        }

        _frame?.SetNavigationContent(content, source, extraData);
        var args = CreateNavigationEventArgs(source, content, extraData);
        Navigated?.Invoke(this, args);
        Application.Current?.RaiseNavigated(args);
        LoadCompleted?.Invoke(this, args);
        Application.Current?.RaiseLoadCompleted(args);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private JournalEntry CreateJournalEntry(Uri? source, object? content, object? extraData, bool isObjectNavigation)
    {
        var entry = new JournalEntry(source, content, extraData, isObjectNavigation)
        {
            CustomContentState = (content as IProvideCustomContentState)?.GetContentState(),
        };
        entry.Name = entry.CustomContentState?.JournalEntryName ?? content?.ToString() ?? source?.ToString() ?? string.Empty;
        return entry;
    }

    private object? LoadContent(Uri? source)
    {
        if (source == null)
        {
            return null;
        }

        if (ContentLoader != null)
        {
            return ContentLoader(source);
        }

        if (DefaultContentLoader != null)
        {
            return DefaultContentLoader(source);
        }

        var application = Application.Current;
        return application != null && Application.StartupObjectLoader != null
            ? Application.StartupObjectLoader(application, source)
            : null;
    }

    private void RaiseProgressAndFragment(Uri? source)
    {
        if (source == null)
        {
            return;
        }

        var progressArgs = new NavigationProgressEventArgs(source, 1, 1, Navigator);
        NavigationProgress?.Invoke(this, progressArgs);
        Application.Current?.RaiseNavigationProgress(progressArgs);
        // Uri.Fragment throws InvalidOperationException for relative URIs even though
        // relative navigation targets commonly contain a fragment ("Page.xaml#part").
        // Frame accepts those URIs, so extract the fragment from OriginalString on the
        // relative path and use Uri.Fragment only for absolute URIs.
        var fragment = GetFragment(source);
        if (!string.IsNullOrEmpty(fragment))
        {
            var fragmentArgs = new FragmentNavigationEventArgs(fragment.TrimStart('#'), Navigator);
            FragmentNavigation?.Invoke(this, fragmentArgs);
            Application.Current?.RaiseFragmentNavigation(fragmentArgs);
        }
    }

    private static string GetFragment(Uri source)
    {
        if (source.IsAbsoluteUri)
        {
            return source.Fragment;
        }

        var text = source.OriginalString;
        var fragmentIndex = text.IndexOf('#');
        return fragmentIndex >= 0 && fragmentIndex + 1 < text.Length
            ? text[fragmentIndex..]
            : string.Empty;
    }

    private void RaiseNavigationFailed(Uri? source, object? extraData, Exception exception)
    {
        var args = new NavigationFailedEventArgs(source, extraData, Navigator, null, null, exception);
        NavigationFailed?.Invoke(this, args);
        Application.Current?.RaiseNavigationFailed(args);
        if (!args.Handled)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private NavigationEventArgs CreateNavigationEventArgs(Uri? source, object? content, object? extraData)
        => new(source, content, extraData, null, Navigator, isNavigationInitiator: true);

    private object Navigator => (object?)_frame ?? this;

    private void CompleteNavigation()
    {
        _isNavigating = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
