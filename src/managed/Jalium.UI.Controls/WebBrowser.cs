using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jalium.UI.Controls;

/// <summary>
/// Hosts and navigates between HTML documents.
/// This is a compatibility surface that forwards to <see cref="WebView"/>.
/// </summary>
public class WebBrowser : FrameworkElement
{
    private readonly WebView _webView;
    private bool _syncingSourceFromInner;

    /// <summary>
    /// Identifies the <see cref="Source"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(Uri), typeof(WebBrowser),
            new PropertyMetadata(null, OnSourceChanged));

    /// <summary>Gets or sets the URI of the current document.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Gets a value indicating whether the browser can navigate back.</summary>
    public bool CanGoBack { get; private set; }

    /// <summary>Gets a value indicating whether the browser can navigate forward.</summary>
    public bool CanGoForward { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebBrowser"/> class.
    /// </summary>
    public WebBrowser()
    {
        _webView = new WebView();
        _webView.NavigationStarting += OnInnerNavigationStarting;
        _webView.NavigationCompleted += OnInnerNavigationCompleted;
        _webView.SourceChanged += OnInnerSourceChanged;

        AddVisualChild(_webView);
        UpdateNavigationState();
    }

    /// <summary>Navigates to the specified URI.</summary>
    public void Navigate(Uri source) => Source = source;

    /// <summary>Navigates to the specified URI string.</summary>
    public void Navigate(string source) => Navigate(new Uri(source));

    /// <summary>Navigates to the previous page in the navigation history.</summary>
    public void GoBack()
    {
        _webView.GoBack();
        UpdateNavigationState();
    }

    /// <summary>Navigates to the next page in the navigation history.</summary>
    public void GoForward()
    {
        _webView.GoForward();
        UpdateNavigationState();
    }

    /// <summary>Reloads the current page.</summary>
    public void Refresh()
    {
        _webView.Refresh();
        UpdateNavigationState();
    }

    /// <summary>Executes a script function defined in the currently loaded document.</summary>
    public object? InvokeScript(string scriptName, params object[] args)
    {
        if (string.IsNullOrWhiteSpace(scriptName))
            throw new ArgumentException("Script name cannot be null or empty.", nameof(scriptName));

        var script = BuildScriptInvocation(scriptName, args);
        var rawResult = _webView.ExecuteScriptAsync(script).GetAwaiter().GetResult();
        return ParseScriptResult(rawResult);
    }

    /// <summary>Occurs just before navigation to a document.</summary>
    public event EventHandler<WebBrowserNavigatingEventArgs>? Navigating;

    /// <summary>Occurs when the document being navigated to has been downloaded and parsed.</summary>
    public event EventHandler<WebBrowserNavigatedEventArgs>? Navigated;

    /// <summary>Occurs when the document being navigated to has finished loading.</summary>
    public event EventHandler<WebBrowserNavigatedEventArgs>? LoadCompleted;

    public override int VisualChildrenCount => 1;

    public override Visual? GetVisualChild(int index)
    {
        if (index != 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        return _webView;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _webView.Measure(availableSize);
        return _webView.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _webView.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WebBrowser browser || browser._syncingSourceFromInner)
            return;

        browser._webView.Source = e.NewValue as Uri;
    }

    private void OnInnerNavigationStarting(object? sender, WebViewNavigationStartingEventArgs e)
    {
        var navigatingArgs = new WebBrowserNavigatingEventArgs { Uri = e.Uri };
        Navigating?.Invoke(this, navigatingArgs);
        e.Cancel = navigatingArgs.Cancel;
    }

    private void OnInnerNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        UpdateNavigationState();

        var args = new WebBrowserNavigatedEventArgs
        {
            Uri = _webView.Source,
            Content = null,
            IsNavigationInitiator = true,
            ExtraData = null
        };

        Navigated?.Invoke(this, args);
        LoadCompleted?.Invoke(this, args);
    }

    private void OnInnerSourceChanged(object? sender, WebViewSourceChangedEventArgs e)
    {
        _syncingSourceFromInner = true;
        try
        {
            SetValue(SourceProperty, e.Source);
        }
        finally
        {
            _syncingSourceFromInner = false;
        }
    }

    private void UpdateNavigationState()
    {
        CanGoBack = _webView.CanGoBack;
        CanGoForward = _webView.CanGoForward;
    }

    private static string BuildScriptInvocation(string scriptName, object[] args)
    {
        var encodedName = JsonSerializer.Serialize(scriptName, WebBrowserJsonContext.Default.String);
        var encodedArgs = args.Length == 0
            ? string.Empty
            : string.Join(", ", args.Select(static arg =>
                JsonSerializer.Serialize(arg, WebBrowserJsonContext.Default.Object)));

        return $"(function(){{const name={encodedName};const fn=name.split('.').reduce((current, part)=>current?.[part], globalThis);if(typeof fn!=='function'){{throw new Error('Script function not found: '+name);}}return fn({encodedArgs});}})();";
    }

    private static object? ParseScriptResult(string rawResult)
    {
        if (string.IsNullOrWhiteSpace(rawResult))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(rawResult);
            var root = doc.RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => root.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when root.TryGetInt64(out var intValue) => intValue,
                JsonValueKind.Number when root.TryGetDouble(out var doubleValue) => doubleValue,
                _ => root.Clone()
            };
        }
        catch (JsonException)
        {
            // WebView2 usually returns JSON, but keep a robust fallback for non-JSON payloads.
            return rawResult;
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

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(object))]
internal partial class WebBrowserJsonContext : JsonSerializerContext;
