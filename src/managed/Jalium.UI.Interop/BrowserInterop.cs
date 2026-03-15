using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

public static class BrowserInterop
{
    private const int NotSupported = unchecked((int)0x80004001);
    private static readonly IBrowserInteropBackend s_backend = CreateBackend();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void NavigationStartingCallback(nint userData, nint uri, int isRedirected, ref int cancel);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void NavigationCompletedCallback(nint userData, int isSuccess, int httpStatusCode);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void SourceChangedCallback(nint userData, int isNewDocument);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void ContentLoadingCallback(nint userData, int isErrorPage);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void DocumentTitleChangedCallback(nint userData, nint title);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void WebMessageReceivedCallback(nint userData, nint message);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void NewWindowRequestedCallback(nint userData, nint uri, int isUserInitiated, ref int handled);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void ProcessFailedCallback(nint userData, int processFailedKind);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void ZoomFactorChangedCallback(nint userData, double zoomFactor);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void ScriptCompletedCallback(nint userData, int result, nint resultJson);

    public static int Initialize() => s_backend.Initialize();
    public static void Shutdown() => s_backend.Shutdown();
    public static int GetAvailableBrowserVersionString(string? browserExecutableFolder, out nint version) => s_backend.GetAvailableBrowserVersionString(browserExecutableFolder, out version);
    public static void FreeString(nint value) => s_backend.FreeString(value);
    public static int CreateEnvironment(string? browserExecutableFolder, string? userDataFolder, out nint environment) => s_backend.CreateEnvironment(browserExecutableFolder, userDataFolder, out environment);
    public static void DestroyEnvironment(nint environment) => s_backend.DestroyEnvironment(environment);
    public static int CreateController(nint environment, nint parentWindow, int useCompositionController, out nint controller) => s_backend.CreateController(environment, parentWindow, useCompositionController, out controller);
    public static void DestroyController(nint controller) => s_backend.DestroyController(controller);
    public static int SetCallbacks(
        nint controller,
        NavigationStartingCallback? navigationStarting,
        NavigationCompletedCallback? navigationCompleted,
        SourceChangedCallback? sourceChanged,
        ContentLoadingCallback? contentLoading,
        DocumentTitleChangedCallback? documentTitleChanged,
        WebMessageReceivedCallback? webMessageReceived,
        NewWindowRequestedCallback? newWindowRequested,
        ProcessFailedCallback? processFailed,
        ZoomFactorChangedCallback? zoomFactorChanged,
        nint userData)
        => s_backend.SetCallbacks(controller, navigationStarting, navigationCompleted, sourceChanged, contentLoading, documentTitleChanged, webMessageReceived, newWindowRequested, processFailed, zoomFactorChanged, userData);
    public static int Navigate(nint controller, string uri) => s_backend.Navigate(controller, uri);
    public static int NavigateToString(nint controller, string html) => s_backend.NavigateToString(controller, html);
    public static int Reload(nint controller) => s_backend.Reload(controller);
    public static int Stop(nint controller) => s_backend.Stop(controller);
    public static int GoBack(nint controller) => s_backend.GoBack(controller);
    public static int GoForward(nint controller) => s_backend.GoForward(controller);
    public static int GetCanGoBack(nint controller, out int canGoBack) => s_backend.GetCanGoBack(controller, out canGoBack);
    public static int GetCanGoForward(nint controller, out int canGoForward) => s_backend.GetCanGoForward(controller, out canGoForward);
    public static int ExecuteScriptAsync(nint controller, string script, ScriptCompletedCallback callback, nint userData) => s_backend.ExecuteScriptAsync(controller, script, callback, userData);
    public static int PostWebMessageAsString(nint controller, string message) => s_backend.PostWebMessageAsString(controller, message);
    public static int PostWebMessageAsJson(nint controller, string jsonMessage) => s_backend.PostWebMessageAsJson(controller, jsonMessage);
    public static int GetSource(nint controller, out nint source) => s_backend.GetSource(controller, out source);
    public static int GetDocumentTitle(nint controller, out nint title) => s_backend.GetDocumentTitle(controller, out title);
    public static int SetBounds(nint controller, int x, int y, int width, int height) => s_backend.SetBounds(controller, x, y, width, height);
    public static int GetBounds(nint controller, out int x, out int y, out int width, out int height) => s_backend.GetBounds(controller, out x, out y, out width, out height);
    public static int SetIsVisible(nint controller, int isVisible) => s_backend.SetIsVisible(controller, isVisible);
    public static int NotifyParentWindowPositionChanged(nint controller) => s_backend.NotifyParentWindowPositionChanged(controller);
    public static int Close(nint controller) => s_backend.Close(controller);
    public static int SetZoomFactor(nint controller, double zoomFactor) => s_backend.SetZoomFactor(controller, zoomFactor);
    public static int GetZoomFactor(nint controller, out double zoomFactor) => s_backend.GetZoomFactor(controller, out zoomFactor);
    public static int SetDefaultBackgroundColor(nint controller, uint argb) => s_backend.SetDefaultBackgroundColor(controller, argb);
    public static int GetDefaultBackgroundColor(nint controller, out uint argb) => s_backend.GetDefaultBackgroundColor(controller, out argb);
    public static int SetRootVisualTarget(nint controller, nint visualTarget) => s_backend.SetRootVisualTarget(controller, visualTarget);
    public static int SendMouseInput(nint controller, int eventKind, int virtualKeys, uint mouseData, int x, int y) => s_backend.SendMouseInput(controller, eventKind, virtualKeys, mouseData, x, y);
    public static int OpenDevToolsWindow(nint controller) => s_backend.OpenDevToolsWindow(controller);

    private static IBrowserInteropBackend CreateBackend()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsBrowserInteropBackend();
        }

        return new UnsupportedBrowserInteropBackend();
    }

    public static int GetNotSupportedErrorCode() => NotSupported;
}

