using System.Runtime.InteropServices;
using System.Text;
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
    private object? _objectForScripting;

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

    /// <summary>Gets the native browser document object, or null before initialization.</summary>
    public object? Document => _webView.CoreWebView2;

    /// <summary>Gets or sets the COM-visible host object exposed to browser script.</summary>
    public object? ObjectForScripting
    {
        get => _objectForScripting;
        set
        {
            if (value != null && OperatingSystem.IsWindows() &&
                !Marshal.IsTypeVisibleFromCom(value.GetType()))
            {
                throw new ArgumentException("The scripting object type must be visible to COM.", nameof(value));
            }

            _objectForScripting = value;
            ApplyObjectForScripting();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebBrowser"/> class.
    /// </summary>
    public WebBrowser()
    {
        _webView = new WebView();
        _webView.NavigationStarting += OnInnerNavigationStarting;
        _webView.NavigationCompleted += OnInnerNavigationCompleted;
        _webView.SourceChanged += OnInnerSourceChanged;
        _webView.CoreWebView2InitializationCompleted += OnInnerInitializationCompleted;

        AddVisualChild(_webView);
        UpdateNavigationState();
    }

    /// <summary>Navigates to the specified URI.</summary>
    public void Navigate(Uri source) => Navigate(source, null!, null!, null!);

    /// <summary>Navigates to the specified URI string.</summary>
    public void Navigate(string source) => Navigate(new Uri(source));

    /// <summary>Navigates with optional target, POST data, and additional headers.</summary>
    public void Navigate(Uri source, string targetFrameName, byte[] postData, string additionalHeaders)
    {
        if (source != null && !source.IsAbsoluteUri)
        {
            throw new ArgumentException("Only absolute URIs are supported.", nameof(source));
        }

        if (source == null)
        {
            Source = null;
            _webView.NavigateToBlank();
            return;
        }

        if ((postData != null || !string.IsNullOrEmpty(additionalHeaders)) &&
            _webView.TryNavigateWithRequest(source, postData, additionalHeaders))
        {
            _syncingSourceFromInner = true;
            try
            {
                SetValue(SourceProperty, source);
            }
            finally
            {
                _syncingSourceFromInner = false;
            }
            return;
        }

        Source = source;
    }

    /// <summary>Navigates with optional target, POST data, and additional headers.</summary>
    public void Navigate(string source, string targetFrameName, byte[] postData, string additionalHeaders) =>
        Navigate(new Uri(source), targetFrameName, postData, additionalHeaders);

    /// <summary>Navigates to an HTML document read from a stream.</summary>
    public void NavigateToStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);
        Source = null;
        _webView.NavigateToString(reader.ReadToEnd());
    }

    /// <summary>Navigates to an HTML document supplied as text.</summary>
    public void NavigateToString(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentNullException(nameof(text));
        }

        Source = null;
        _webView.NavigateToString(text);
    }

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

    /// <summary>Reloads the current page, requesting a complete refresh when possible.</summary>
    public void Refresh(bool noCache)
    {
        if (!noCache || Source == null ||
            !_webView.TryNavigateWithRequest(
                Source,
                postData: null,
                additionalHeaders: "Cache-Control: no-cache\r\nPragma: no-cache"))
        {
            _webView.Refresh();
        }
        UpdateNavigationState();
    }

    /// <summary>Executes a script function defined in the currently loaded document.</summary>
    public object? InvokeScript(string scriptName) => InvokeScript(scriptName, null!);

    /// <summary>Executes a script function defined in the currently loaded document.</summary>
    public object? InvokeScript(string scriptName, params object[] args)
    {
        if (string.IsNullOrEmpty(scriptName))
            throw new ArgumentNullException(nameof(scriptName));

        var script = BuildScriptInvocation(scriptName, args ?? []);
        var rawResult = _webView.ExecuteScriptAsync(script).GetAwaiter().GetResult();
        return ParseScriptResult(rawResult);
    }

    /// <summary>Occurs just before navigation to a document.</summary>
    public event EventHandler<WebBrowserNavigatingEventArgs>? Navigating;

    /// <summary>Occurs when the document being navigated to has been downloaded and parsed.</summary>
    public event EventHandler<WebBrowserNavigatedEventArgs>? Navigated;

    /// <summary>Occurs when the document being navigated to has finished loading.</summary>
    public event EventHandler<WebBrowserNavigatedEventArgs>? LoadCompleted;

    protected override int VisualChildrenCount => 1;

    protected override Visual? GetVisualChild(int index)
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
        ApplyObjectForScripting();
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

    private void OnInnerInitializationCompleted(object? sender, EventArgs e) =>
        ApplyObjectForScripting();

    private void ApplyObjectForScripting()
    {
        var core = _webView.CoreWebView2;
        if (core == null)
        {
            return;
        }

        try
        {
            core.RemoveHostObjectFromScript("external");
        }
        catch (COMException)
        {
            // No previous host object was registered.
        }

        if (_objectForScripting != null)
        {
            core.AddHostObjectToScript("external", _objectForScripting);
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
