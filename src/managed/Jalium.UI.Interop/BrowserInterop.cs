using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

internal static class BrowserInterop
{
    private const int NotSupported = unchecked((int)0x80004001);
    private static readonly IBrowserInteropBackend s_backend = CreateBackend();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void NavigationStartingCallback(nint userData, nint uri, int isRedirected, ref int cancel);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void NavigationCompletedCallback(nint userData, int isSuccess, int httpStatusCode);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void SourceChangedCallback(nint userData, int isNewDocument);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void ContentLoadingCallback(nint userData, int isErrorPage);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void DocumentTitleChangedCallback(nint userData, nint title);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void WebMessageReceivedCallback(nint userData, nint message);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void NewWindowRequestedCallback(nint userData, nint uri, int isUserInitiated, ref int handled);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void ProcessFailedCallback(nint userData, int processFailedKind);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void ZoomFactorChangedCallback(nint userData, double zoomFactor);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void ScriptCompletedCallback(nint userData, int result, nint resultJson);

    internal static int Initialize() => s_backend.Initialize();
    internal static void Shutdown() => s_backend.Shutdown();
    internal static int GetAvailableBrowserVersionString(string? browserExecutableFolder, out nint version) => s_backend.GetAvailableBrowserVersionString(browserExecutableFolder, out version);
    internal static void FreeString(nint value) => s_backend.FreeString(value);
    internal static int CreateEnvironment(string? browserExecutableFolder, string? userDataFolder, out nint environment) => s_backend.CreateEnvironment(browserExecutableFolder, userDataFolder, out environment);
    internal static void DestroyEnvironment(nint environment) => s_backend.DestroyEnvironment(environment);
    internal static int CreateController(nint environment, nint parentWindow, int useCompositionController, out nint controller) => s_backend.CreateController(environment, parentWindow, useCompositionController, out controller);
    internal static void DestroyController(nint controller) => s_backend.DestroyController(controller);
    internal static int SetCallbacks(
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
    internal static int Navigate(nint controller, string uri) => s_backend.Navigate(controller, uri);
    internal static int NavigateToString(nint controller, string html) => s_backend.NavigateToString(controller, html);
    internal static int Reload(nint controller) => s_backend.Reload(controller);
    internal static int Stop(nint controller) => s_backend.Stop(controller);
    internal static int GoBack(nint controller) => s_backend.GoBack(controller);
    internal static int GoForward(nint controller) => s_backend.GoForward(controller);
    internal static int GetCanGoBack(nint controller, out int canGoBack) => s_backend.GetCanGoBack(controller, out canGoBack);
    internal static int GetCanGoForward(nint controller, out int canGoForward) => s_backend.GetCanGoForward(controller, out canGoForward);
    internal static int ExecuteScriptAsync(nint controller, string script, ScriptCompletedCallback callback, nint userData) => s_backend.ExecuteScriptAsync(controller, script, callback, userData);
    internal static int PostWebMessageAsString(nint controller, string message) => s_backend.PostWebMessageAsString(controller, message);
    internal static int PostWebMessageAsJson(nint controller, string jsonMessage) => s_backend.PostWebMessageAsJson(controller, jsonMessage);
    internal static int GetSource(nint controller, out nint source) => s_backend.GetSource(controller, out source);
    internal static int GetDocumentTitle(nint controller, out nint title) => s_backend.GetDocumentTitle(controller, out title);
    internal static int SetBounds(nint controller, int x, int y, int width, int height) => s_backend.SetBounds(controller, x, y, width, height);
    internal static int GetBounds(nint controller, out int x, out int y, out int width, out int height) => s_backend.GetBounds(controller, out x, out y, out width, out height);
    internal static int SetIsVisible(nint controller, int isVisible) => s_backend.SetIsVisible(controller, isVisible);
    internal static int NotifyParentWindowPositionChanged(nint controller) => s_backend.NotifyParentWindowPositionChanged(controller);
    internal static int Close(nint controller) => s_backend.Close(controller);
    internal static int SetZoomFactor(nint controller, double zoomFactor) => s_backend.SetZoomFactor(controller, zoomFactor);
    internal static int GetZoomFactor(nint controller, out double zoomFactor) => s_backend.GetZoomFactor(controller, out zoomFactor);
    internal static int SetDefaultBackgroundColor(nint controller, uint argb) => s_backend.SetDefaultBackgroundColor(controller, argb);
    internal static int GetDefaultBackgroundColor(nint controller, out uint argb) => s_backend.GetDefaultBackgroundColor(controller, out argb);
    internal static int SetRootVisualTarget(nint controller, nint visualTarget) => s_backend.SetRootVisualTarget(controller, visualTarget);
    internal static int SendMouseInput(nint controller, int eventKind, int virtualKeys, uint mouseData, int x, int y) => s_backend.SendMouseInput(controller, eventKind, virtualKeys, mouseData, x, y);
    internal static int OpenDevToolsWindow(nint controller) => s_backend.OpenDevToolsWindow(controller);

    private static IBrowserInteropBackend CreateBackend()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsBrowserInteropBackend();
        }

        return new UnsupportedBrowserInteropBackend();
    }

    internal static int GetNotSupportedErrorCode() => NotSupported;
}
