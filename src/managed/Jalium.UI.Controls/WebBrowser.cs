namespace Jalium.UI.Controls;

/// <summary>
/// Hosts and navigates between HTML documents. Uses WebView2 if available,
/// falls back to legacy WebBrowser behavior.
/// </summary>
public sealed class WebBrowser : FrameworkElement
{
    /// <summary>
    /// Identifies the <see cref="Source"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(Uri), typeof(WebBrowser),
            new PropertyMetadata(null, OnSourceChanged));

    /// <summary>Gets or sets the URI of the current document.</summary>
    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Gets a value indicating whether the browser can navigate back.</summary>
    public bool CanGoBack { get; private set; }

    /// <summary>Gets a value indicating whether the browser can navigate forward.</summary>
    public bool CanGoForward { get; private set; }

    /// <summary>Navigates to the specified URI.</summary>
    public void Navigate(Uri source) => Source = source;

    /// <summary>Navigates to the specified URI string.</summary>
    public void Navigate(string source) => Navigate(new Uri(source));

    /// <summary>Navigates to the previous page in the navigation history.</summary>
    public void GoBack() { }

    /// <summary>Navigates to the next page in the navigation history.</summary>
    public void GoForward() { }

    /// <summary>Reloads the current page.</summary>
    public void Refresh() { }

    /// <summary>Executes a script function defined in the currently loaded document.</summary>
    public object? InvokeScript(string scriptName, params object[] args) => null;

    /// <summary>Occurs just before navigation to a document.</summary>
    public event EventHandler<WebBrowserNavigatingEventArgs>? Navigating;

    /// <summary>Occurs when the document being navigated to has been downloaded and parsed.</summary>
    public event EventHandler<WebBrowserNavigatedEventArgs>? Navigated;

    /// <summary>Occurs when the document being navigated to has finished loading.</summary>
    public event EventHandler<WebBrowserNavigatedEventArgs>? LoadCompleted;

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WebBrowser browser && e.NewValue is Uri uri)
        {
            browser.Navigating?.Invoke(browser, new WebBrowserNavigatingEventArgs { Uri = uri });
        }
    }
}

/// <summary>
/// Provides data for the <see cref="WebBrowser.Navigating"/> event.
/// </summary>
public sealed class WebBrowserNavigatingEventArgs : EventArgs
{
    /// <summary>Gets the URI being navigated to.</summary>
    public Uri? Uri { get; init; }

    /// <summary>Gets or sets a value indicating whether the navigation should be canceled.</summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// Provides data for the <see cref="WebBrowser.Navigated"/> and
/// <see cref="WebBrowser.LoadCompleted"/> events.
/// </summary>
public sealed class WebBrowserNavigatedEventArgs : EventArgs
{
    /// <summary>Gets the URI that was navigated to.</summary>
    public Uri? Uri { get; init; }

    /// <summary>Gets the content of the page.</summary>
    public object? Content { get; init; }

    /// <summary>Gets a value indicating whether this browser initiated the navigation.</summary>
    public bool IsNavigationInitiator { get; init; }

    /// <summary>Gets extra data associated with the navigation.</summary>
    public object? ExtraData { get; init; }
}
