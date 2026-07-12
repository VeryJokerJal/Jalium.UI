using Jalium.UI.Controls;
using System.Runtime.InteropServices;
using System.Text;

namespace Jalium.UI.Tests;

public class WebBrowserAdapterTests
{
    [Fact]
    public void RemainingWpfSurface_HasExactOverloads()
    {
        Type type = typeof(WebBrowser);
        Assert.Equal(typeof(object), type.GetProperty(nameof(WebBrowser.Document))!.PropertyType);
        Assert.Equal(typeof(object), type.GetProperty(nameof(WebBrowser.ObjectForScripting))!.PropertyType);
        Assert.NotNull(type.GetMethod(nameof(WebBrowser.InvokeScript), [typeof(string)]));
        Assert.NotNull(type.GetMethod(nameof(WebBrowser.Navigate),
            [typeof(Uri), typeof(string), typeof(byte[]), typeof(string)]));
        Assert.NotNull(type.GetMethod(nameof(WebBrowser.Navigate),
            [typeof(string), typeof(string), typeof(byte[]), typeof(string)]));
        Assert.NotNull(type.GetMethod(nameof(WebBrowser.NavigateToStream), [typeof(Stream)]));
        Assert.NotNull(type.GetMethod(nameof(WebBrowser.NavigateToString), [typeof(string)]));
        Assert.NotNull(type.GetMethod(nameof(WebBrowser.Refresh), [typeof(bool)]));
    }

    [Fact]
    public void Source_ShouldForwardToInnerWebView()
    {
        var browser = new WebBrowser();
        var source = new Uri("https://example.com/");

        browser.Source = source;

        var inner = Assert.IsType<WebView>(browser.GetVisualChild(0));
        Assert.Equal(source, inner.Source);
        Assert.Equal(1, browser.VisualChildrenCount);
    }

    [Fact]
    public void InvokeScript_WithEmptyName_ShouldThrow()
    {
        var browser = new WebBrowser();

        Assert.Throws<ArgumentNullException>(() => browser.InvokeScript(string.Empty));
        Assert.Throws<ArgumentNullException>(() => browser.InvokeScript(null!));
    }

    [Fact]
    public void DocumentAndObjectForScripting_FollowWpfCompatibilityShape()
    {
        var browser = new WebBrowser();
        var bridge = new ScriptBridge();

        Assert.Null(browser.Document);
        browser.ObjectForScripting = bridge;
        Assert.Same(bridge, browser.ObjectForScripting);
        browser.ObjectForScripting = null;
        Assert.Null(browser.ObjectForScripting);
        Assert.Throws<ArgumentException>(() => browser.ObjectForScripting = new HiddenScriptBridge());
    }

    [Fact]
    public void NavigateOverloads_ValidateAndForwardDestination()
    {
        var browser = new WebBrowser();
        var source = new Uri("https://example.com/form");
        var inner = Assert.IsType<WebView>(browser.GetVisualChild(0));

        browser.Navigate(source, "frame", [1, 2, 3], "X-Test: value");
        Assert.Equal(source, browser.Source);
        Assert.Equal(source, inner.Source);
        Assert.Equal(source, inner.PendingRequestSource);
        Assert.Equal(new byte[] { 1, 2, 3 }, inner.PendingRequestPostData);
        Assert.Equal("X-Test: value", inner.PendingRequestAdditionalHeaders);

        browser.Navigate("https://example.com/next", "", null!, null!);
        Assert.Equal(new Uri("https://example.com/next"), browser.Source);

        Assert.Throws<ArgumentException>(() =>
            browser.Navigate(new Uri("relative", UriKind.Relative), "", null!, null!));

        browser.Navigate((Uri)null!);
        Assert.Null(browser.Source);
    }

    [Fact]
    public void NavigateToStringAndStream_QueueHtmlForWebViewBackend()
    {
        var browser = new WebBrowser();
        var inner = Assert.IsType<WebView>(browser.GetVisualChild(0));

        browser.NavigateToString("<h1>hello</h1>");
        Assert.Equal("<h1>hello</h1>", inner.PendingHtmlContent);
        Assert.Null(browser.Source);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<p>stream</p>"));
        browser.NavigateToStream(stream);
        Assert.Equal("<p>stream</p>", inner.PendingHtmlContent);
        Assert.True(stream.CanRead);

        using var emptyStream = new MemoryStream();
        browser.NavigateToStream(emptyStream);
        Assert.Equal(string.Empty, inner.PendingHtmlContent);

        Assert.Throws<ArgumentNullException>(() => browser.NavigateToStream(null!));
        Assert.Throws<ArgumentNullException>(() => browser.NavigateToString(null!));
        Assert.Throws<ArgumentNullException>(() => browser.NavigateToString(string.Empty));
    }

    [Fact]
    public void RefreshBooleanOverload_IsSafeBeforeBackendInitialization()
    {
        var browser = new WebBrowser();
        var source = new Uri("https://example.com/");
        var inner = Assert.IsType<WebView>(browser.GetVisualChild(0));

        browser.Source = source;
        browser.Refresh(noCache: false);
        browser.Refresh(noCache: true);

        Assert.Equal(source, inner.PendingRequestSource);
        Assert.Contains("Cache-Control: no-cache",
            Assert.IsType<string>(inner.PendingRequestAdditionalHeaders));
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.AutoDual)]
    public sealed class ScriptBridge
    {
    }

    [ComVisible(false)]
    public sealed class HiddenScriptBridge
    {
    }
}
