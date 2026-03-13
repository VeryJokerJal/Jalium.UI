using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

internal interface IBrowserInteropBackend
{
    int Initialize();
    void Shutdown();
    int GetAvailableBrowserVersionString(string? browserExecutableFolder, out nint version);
    void FreeString(nint value);
    int CreateEnvironment(string? browserExecutableFolder, string? userDataFolder, out nint environment);
    void DestroyEnvironment(nint environment);
    int CreateController(nint environment, nint parentWindow, int useCompositionController, out nint controller);
    void DestroyController(nint controller);
    int SetCallbacks(nint controller, BrowserInterop.NavigationStartingCallback? navigationStarting, BrowserInterop.NavigationCompletedCallback? navigationCompleted, BrowserInterop.SourceChangedCallback? sourceChanged, BrowserInterop.ContentLoadingCallback? contentLoading, BrowserInterop.DocumentTitleChangedCallback? documentTitleChanged, BrowserInterop.WebMessageReceivedCallback? webMessageReceived, BrowserInterop.NewWindowRequestedCallback? newWindowRequested, BrowserInterop.ProcessFailedCallback? processFailed, BrowserInterop.ZoomFactorChangedCallback? zoomFactorChanged, nint userData);
    int Navigate(nint controller, string uri);
    int NavigateToString(nint controller, string html);
    int Reload(nint controller);
    int Stop(nint controller);
    int GoBack(nint controller);
    int GoForward(nint controller);
    int GetCanGoBack(nint controller, out int canGoBack);
    int GetCanGoForward(nint controller, out int canGoForward);
    int ExecuteScriptAsync(nint controller, string script, BrowserInterop.ScriptCompletedCallback callback, nint userData);
    int PostWebMessageAsString(nint controller, string message);
    int PostWebMessageAsJson(nint controller, string jsonMessage);
    int GetSource(nint controller, out nint source);
    int GetDocumentTitle(nint controller, out nint title);
    int SetBounds(nint controller, int x, int y, int width, int height);
    int GetBounds(nint controller, out int x, out int y, out int width, out int height);
    int SetIsVisible(nint controller, int isVisible);
    int NotifyParentWindowPositionChanged(nint controller);
    int Close(nint controller);
    int SetZoomFactor(nint controller, double zoomFactor);
    int GetZoomFactor(nint controller, out double zoomFactor);
    int SetDefaultBackgroundColor(nint controller, uint argb);
    int GetDefaultBackgroundColor(nint controller, out uint argb);
    int SetRootVisualTarget(nint controller, nint visualTarget);
    int SendMouseInput(nint controller, int eventKind, int virtualKeys, uint mouseData, int x, int y);
    int OpenDevToolsWindow(nint controller);
}

internal sealed class UnsupportedBrowserInteropBackend : IBrowserInteropBackend
{
    private static int NotSupported => BrowserInterop.GetNotSupportedErrorCode();

    public int Initialize() => NotSupported;
    public void Shutdown() { }
    public int GetAvailableBrowserVersionString(string? browserExecutableFolder, out nint version) { version = nint.Zero; return NotSupported; }
    public void FreeString(nint value) { }
    public int CreateEnvironment(string? browserExecutableFolder, string? userDataFolder, out nint environment) { environment = nint.Zero; return NotSupported; }
    public void DestroyEnvironment(nint environment) { }
    public int CreateController(nint environment, nint parentWindow, int useCompositionController, out nint controller) { controller = nint.Zero; return NotSupported; }
    public void DestroyController(nint controller) { }
    public int SetCallbacks(nint controller, BrowserInterop.NavigationStartingCallback? navigationStarting, BrowserInterop.NavigationCompletedCallback? navigationCompleted, BrowserInterop.SourceChangedCallback? sourceChanged, BrowserInterop.ContentLoadingCallback? contentLoading, BrowserInterop.DocumentTitleChangedCallback? documentTitleChanged, BrowserInterop.WebMessageReceivedCallback? webMessageReceived, BrowserInterop.NewWindowRequestedCallback? newWindowRequested, BrowserInterop.ProcessFailedCallback? processFailed, BrowserInterop.ZoomFactorChangedCallback? zoomFactorChanged, nint userData) => NotSupported;
    public int Navigate(nint controller, string uri) => NotSupported;
    public int NavigateToString(nint controller, string html) => NotSupported;
    public int Reload(nint controller) => NotSupported;
    public int Stop(nint controller) => NotSupported;
    public int GoBack(nint controller) => NotSupported;
    public int GoForward(nint controller) => NotSupported;
    public int GetCanGoBack(nint controller, out int canGoBack) { canGoBack = 0; return NotSupported; }
    public int GetCanGoForward(nint controller, out int canGoForward) { canGoForward = 0; return NotSupported; }
    public int ExecuteScriptAsync(nint controller, string script, BrowserInterop.ScriptCompletedCallback callback, nint userData) => NotSupported;
    public int PostWebMessageAsString(nint controller, string message) => NotSupported;
    public int PostWebMessageAsJson(nint controller, string jsonMessage) => NotSupported;
    public int GetSource(nint controller, out nint source) { source = nint.Zero; return NotSupported; }
    public int GetDocumentTitle(nint controller, out nint title) { title = nint.Zero; return NotSupported; }
    public int SetBounds(nint controller, int x, int y, int width, int height) => NotSupported;
    public int GetBounds(nint controller, out int x, out int y, out int width, out int height) { x = 0; y = 0; width = 0; height = 0; return NotSupported; }
    public int SetIsVisible(nint controller, int isVisible) => NotSupported;
    public int NotifyParentWindowPositionChanged(nint controller) => NotSupported;
    public int Close(nint controller) => NotSupported;
    public int SetZoomFactor(nint controller, double zoomFactor) => NotSupported;
    public int GetZoomFactor(nint controller, out double zoomFactor) { zoomFactor = 0; return NotSupported; }
    public int SetDefaultBackgroundColor(nint controller, uint argb) => NotSupported;
    public int GetDefaultBackgroundColor(nint controller, out uint argb) { argb = 0; return NotSupported; }
    public int SetRootVisualTarget(nint controller, nint visualTarget) => NotSupported;
    public int SendMouseInput(nint controller, int eventKind, int virtualKeys, uint mouseData, int x, int y) => NotSupported;
    public int OpenDevToolsWindow(nint controller) => NotSupported;
}

internal sealed class WindowsBrowserInteropBackend : IBrowserInteropBackend
{
    public int Initialize() => WindowsBrowserNativeMethods.Initialize();
    public void Shutdown() => WindowsBrowserNativeMethods.Shutdown();
    public int GetAvailableBrowserVersionString(string? browserExecutableFolder, out nint version) => WindowsBrowserNativeMethods.GetAvailableBrowserVersionString(browserExecutableFolder, out version);
    public void FreeString(nint value) => WindowsBrowserNativeMethods.FreeString(value);
    public int CreateEnvironment(string? browserExecutableFolder, string? userDataFolder, out nint environment) => WindowsBrowserNativeMethods.CreateEnvironment(browserExecutableFolder, userDataFolder, out environment);
    public void DestroyEnvironment(nint environment) => WindowsBrowserNativeMethods.DestroyEnvironment(environment);
    public int CreateController(nint environment, nint parentWindow, int useCompositionController, out nint controller) => WindowsBrowserNativeMethods.CreateController(environment, parentWindow, useCompositionController, out controller);
    public void DestroyController(nint controller) => WindowsBrowserNativeMethods.DestroyController(controller);
    public int SetCallbacks(nint controller, BrowserInterop.NavigationStartingCallback? navigationStarting, BrowserInterop.NavigationCompletedCallback? navigationCompleted, BrowserInterop.SourceChangedCallback? sourceChanged, BrowserInterop.ContentLoadingCallback? contentLoading, BrowserInterop.DocumentTitleChangedCallback? documentTitleChanged, BrowserInterop.WebMessageReceivedCallback? webMessageReceived, BrowserInterop.NewWindowRequestedCallback? newWindowRequested, BrowserInterop.ProcessFailedCallback? processFailed, BrowserInterop.ZoomFactorChangedCallback? zoomFactorChanged, nint userData) => WindowsBrowserNativeMethods.SetCallbacks(controller, navigationStarting, navigationCompleted, sourceChanged, contentLoading, documentTitleChanged, webMessageReceived, newWindowRequested, processFailed, zoomFactorChanged, userData);
    public int Navigate(nint controller, string uri) => WindowsBrowserNativeMethods.Navigate(controller, uri);
    public int NavigateToString(nint controller, string html) => WindowsBrowserNativeMethods.NavigateToString(controller, html);
    public int Reload(nint controller) => WindowsBrowserNativeMethods.Reload(controller);
    public int Stop(nint controller) => WindowsBrowserNativeMethods.Stop(controller);
    public int GoBack(nint controller) => WindowsBrowserNativeMethods.GoBack(controller);
    public int GoForward(nint controller) => WindowsBrowserNativeMethods.GoForward(controller);
    public int GetCanGoBack(nint controller, out int canGoBack) => WindowsBrowserNativeMethods.GetCanGoBack(controller, out canGoBack);
    public int GetCanGoForward(nint controller, out int canGoForward) => WindowsBrowserNativeMethods.GetCanGoForward(controller, out canGoForward);
    public int ExecuteScriptAsync(nint controller, string script, BrowserInterop.ScriptCompletedCallback callback, nint userData) => WindowsBrowserNativeMethods.ExecuteScriptAsync(controller, script, callback, userData);
    public int PostWebMessageAsString(nint controller, string message) => WindowsBrowserNativeMethods.PostWebMessageAsString(controller, message);
    public int PostWebMessageAsJson(nint controller, string jsonMessage) => WindowsBrowserNativeMethods.PostWebMessageAsJson(controller, jsonMessage);
    public int GetSource(nint controller, out nint source) => WindowsBrowserNativeMethods.GetSource(controller, out source);
    public int GetDocumentTitle(nint controller, out nint title) => WindowsBrowserNativeMethods.GetDocumentTitle(controller, out title);
    public int SetBounds(nint controller, int x, int y, int width, int height) => WindowsBrowserNativeMethods.SetBounds(controller, x, y, width, height);
    public int GetBounds(nint controller, out int x, out int y, out int width, out int height) => WindowsBrowserNativeMethods.GetBounds(controller, out x, out y, out width, out height);
    public int SetIsVisible(nint controller, int isVisible) => WindowsBrowserNativeMethods.SetIsVisible(controller, isVisible);
    public int NotifyParentWindowPositionChanged(nint controller) => WindowsBrowserNativeMethods.NotifyParentWindowPositionChanged(controller);
    public int Close(nint controller) => WindowsBrowserNativeMethods.Close(controller);
    public int SetZoomFactor(nint controller, double zoomFactor) => WindowsBrowserNativeMethods.SetZoomFactor(controller, zoomFactor);
    public int GetZoomFactor(nint controller, out double zoomFactor) => WindowsBrowserNativeMethods.GetZoomFactor(controller, out zoomFactor);
    public int SetDefaultBackgroundColor(nint controller, uint argb) => WindowsBrowserNativeMethods.SetDefaultBackgroundColor(controller, argb);
    public int GetDefaultBackgroundColor(nint controller, out uint argb) => WindowsBrowserNativeMethods.GetDefaultBackgroundColor(controller, out argb);
    public int SetRootVisualTarget(nint controller, nint visualTarget) => WindowsBrowserNativeMethods.SetRootVisualTarget(controller, visualTarget);
    public int SendMouseInput(nint controller, int eventKind, int virtualKeys, uint mouseData, int x, int y) => WindowsBrowserNativeMethods.SendMouseInput(controller, eventKind, virtualKeys, mouseData, x, y);
    public int OpenDevToolsWindow(nint controller) => WindowsBrowserNativeMethods.OpenDevToolsWindow(controller);
}

internal static class WindowsBrowserNativeMethods
{
    private const string BrowserLib = "jalium.native.browser";

    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_initialize", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Initialize();
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_shutdown", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Shutdown();
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_get_available_browser_version_string", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int GetAvailableBrowserVersionString(string? browserExecutableFolder, out nint version);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_free_string", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FreeString(nint value);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_create_environment", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int CreateEnvironment(string? browserExecutableFolder, string? userDataFolder, out nint environment);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_destroy_environment", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DestroyEnvironment(nint environment);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_create_controller", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CreateController(nint environment, nint parentWindow, int useCompositionController, out nint controller);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_destroy_controller", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DestroyController(nint controller);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_set_callbacks", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SetCallbacks(nint controller, BrowserInterop.NavigationStartingCallback? navigationStarting, BrowserInterop.NavigationCompletedCallback? navigationCompleted, BrowserInterop.SourceChangedCallback? sourceChanged, BrowserInterop.ContentLoadingCallback? contentLoading, BrowserInterop.DocumentTitleChangedCallback? documentTitleChanged, BrowserInterop.WebMessageReceivedCallback? webMessageReceived, BrowserInterop.NewWindowRequestedCallback? newWindowRequested, BrowserInterop.ProcessFailedCallback? processFailed, BrowserInterop.ZoomFactorChangedCallback? zoomFactorChanged, nint userData);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_navigate", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int Navigate(nint controller, string uri);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_navigate_to_string", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int NavigateToString(nint controller, string html);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_reload", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Reload(nint controller);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_stop", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Stop(nint controller);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_go_back", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GoBack(nint controller);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_go_forward", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GoForward(nint controller);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_get_can_go_back", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetCanGoBack(nint controller, out int canGoBack);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_get_can_go_forward", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetCanGoForward(nint controller, out int canGoForward);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_execute_script_async", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int ExecuteScriptAsync(nint controller, string script, BrowserInterop.ScriptCompletedCallback callback, nint userData);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_post_web_message_as_string", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int PostWebMessageAsString(nint controller, string message);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_post_web_message_as_json", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int PostWebMessageAsJson(nint controller, string jsonMessage);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_get_source", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetSource(nint controller, out nint source);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_get_document_title", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetDocumentTitle(nint controller, out nint title);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_set_bounds", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SetBounds(nint controller, int x, int y, int width, int height);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_get_bounds", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetBounds(nint controller, out int x, out int y, out int width, out int height);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_set_is_visible", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SetIsVisible(nint controller, int isVisible);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_notify_parent_window_position_changed", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NotifyParentWindowPositionChanged(nint controller);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_close", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Close(nint controller);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_set_zoom_factor", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SetZoomFactor(nint controller, double zoomFactor);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_get_zoom_factor", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetZoomFactor(nint controller, out double zoomFactor);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_set_default_background_color", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SetDefaultBackgroundColor(nint controller, uint argb);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_get_default_background_color", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetDefaultBackgroundColor(nint controller, out uint argb);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_set_root_visual_target", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SetRootVisualTarget(nint controller, nint visualTarget);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_send_mouse_input", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SendMouseInput(nint controller, int eventKind, int virtualKeys, uint mouseData, int x, int y);
    [DllImport(BrowserLib, EntryPoint = "jalium_webview2_open_devtools_window", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int OpenDevToolsWindow(nint controller);
}
