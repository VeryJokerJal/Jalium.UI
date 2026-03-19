using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using Jalium.UI.Interop;

namespace Microsoft.Web.WebView2.Core;

public sealed class WebView2RuntimeNotFoundException : InvalidOperationException
{
    public WebView2RuntimeNotFoundException(string message)
        : base(message)
    {
    }
}

public enum CoreWebView2MouseEventKind
{
    HorizontalWheel = 526,
    LeftButtonDoubleClick = 515,
    LeftButtonDown = 513,
    LeftButtonUp = 514,
    Leave = 675,
    MiddleButtonDoubleClick = 521,
    MiddleButtonDown = 519,
    MiddleButtonUp = 520,
    Move = 512,
    RightButtonDoubleClick = 518,
    RightButtonDown = 516,
    RightButtonUp = 517,
    Wheel = 522,
    XButtonDoubleClick = 525,
    XButtonDown = 523,
    XButtonUp = 524,
    NonClientRightButtonDown = 164,
    NonClientRightButtonUp = 165
}

[Flags]
public enum CoreWebView2MouseEventVirtualKeys
{
    None = 0,
    LeftButton = 0x1,
    RightButton = 0x2,
    Shift = 0x4,
    Control = 0x8,
    MiddleButton = 0x10,
    XButton1 = 0x20,
    XButton2 = 0x40
}

public enum CoreWebView2ProcessFailedKind
{
    BrowserProcessExited = 0,
    RenderProcessExited = 1,
    RenderProcessUnresponsive = 2,
    FrameRenderProcessExited = 3,
    UtilityProcessExited = 4,
    SandboxHelperProcessExited = 5,
    GpuProcessExited = 6,
    PpapiPluginProcessExited = 7,
    PpapiBrokerProcessExited = 8,
    UnknownProcessExited = 9
}

public sealed class CoreWebView2NavigationStartingEventArgs : EventArgs
{
    public CoreWebView2NavigationStartingEventArgs(string uri, bool isRedirected)
    {
        Uri = uri;
        IsRedirected = isRedirected;
    }

    public string Uri { get; }

    public bool IsRedirected { get; }

    public bool Cancel { get; set; }
}

public sealed class CoreWebView2NavigationCompletedEventArgs : EventArgs
{
    public CoreWebView2NavigationCompletedEventArgs(bool isSuccess, int httpStatusCode)
    {
        IsSuccess = isSuccess;
        HttpStatusCode = httpStatusCode;
    }

    public bool IsSuccess { get; }

    public int HttpStatusCode { get; }
}

public sealed class CoreWebView2SourceChangedEventArgs : EventArgs
{
    public CoreWebView2SourceChangedEventArgs(bool isNewDocument)
    {
        IsNewDocument = isNewDocument;
    }

    public bool IsNewDocument { get; }
}

public sealed class CoreWebView2ContentLoadingEventArgs : EventArgs
{
    public CoreWebView2ContentLoadingEventArgs(bool isErrorPage)
    {
        IsErrorPage = isErrorPage;
    }

    public bool IsErrorPage { get; }
}

public sealed class CoreWebView2WebMessageReceivedEventArgs : EventArgs
{
    private readonly string _message;

    public CoreWebView2WebMessageReceivedEventArgs(string message)
    {
        _message = message;
    }

    public string TryGetWebMessageAsString() => _message;
}

public sealed class CoreWebView2NewWindowRequestedEventArgs : EventArgs
{
    public CoreWebView2NewWindowRequestedEventArgs(string uri, bool isUserInitiated)
    {
        Uri = uri;
        IsUserInitiated = isUserInitiated;
    }

    public string Uri { get; }

    public bool IsUserInitiated { get; }

    public bool Handled { get; set; }
}

public sealed class CoreWebView2ProcessFailedEventArgs : EventArgs
{
    public CoreWebView2ProcessFailedEventArgs(CoreWebView2ProcessFailedKind processFailedKind)
    {
        ProcessFailedKind = processFailedKind;
    }

    public CoreWebView2ProcessFailedKind ProcessFailedKind { get; }
}

internal static class WebView2NativeHelpers
{
    private static int _nativeInitState;

    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _nativeInitState, 1) == 1)
        {
            return;
        }

        var hr = BrowserInterop.Initialize();
        ThrowIfFailed(hr, "Failed to initialize native WebView2 host.");
    }

    public static void ThrowIfFailed(int hr, string message)
    {
        if (hr >= 0)
        {
            return;
        }

        if (IsRuntimeMissingError(hr))
        {
            throw new WebView2RuntimeNotFoundException($"{message} HRESULT=0x{hr:X8}");
        }

        throw new COMException(message, hr);
    }

    public static string PtrToStringAndFree(nint value)
    {
        if (value == nint.Zero)
        {
            return string.Empty;
        }

        try
        {
            return Marshal.PtrToStringUni(value) ?? string.Empty;
        }
        finally
        {
            BrowserInterop.FreeString(value);
        }
    }

    private static bool IsRuntimeMissingError(int hr)
    {
        uint code = unchecked((uint)hr);
        return code == 0x80070002u
               || code == 0x8007007Eu
               || code == 0x80040154u
               || code == 0x80070490u;
    }

    public static bool IsPlatformNotSupportedError(int hr)
    {
        uint code = unchecked((uint)hr);
        return code == 0x80004001u;
    }
}

public sealed class CoreWebView2Environment : IDisposable
{
    private nint _nativeHandle;
    private bool _disposed;

    private CoreWebView2Environment(nint nativeHandle)
    {
        _nativeHandle = nativeHandle;
    }

    public static string GetAvailableBrowserVersionString(string? browserExecutableFolder)
    {
        WebView2NativeHelpers.EnsureInitialized();

        var hr = BrowserInterop.GetAvailableBrowserVersionString(browserExecutableFolder, out var versionPtr);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to get WebView2 runtime version.");
        return WebView2NativeHelpers.PtrToStringAndFree(versionPtr);
    }

    public static Task<CoreWebView2Environment> CreateAsync(
        string? browserExecutableFolder = null,
        string? userDataFolder = null,
        object? options = null)
    {
        _ = options;
        WebView2NativeHelpers.EnsureInitialized();

        var hr = BrowserInterop.CreateEnvironment(browserExecutableFolder, userDataFolder, out var environmentHandle);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to create WebView2 environment.");
        return Task.FromResult(new CoreWebView2Environment(environmentHandle));
    }

    public Task<CoreWebView2Controller> CreateCoreWebView2ControllerAsync(nint parentWindow)
    {
        ThrowIfDisposed();

        var hr = BrowserInterop.CreateController(_nativeHandle, parentWindow, 0, out var controllerHandle);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to create WebView2 controller.");
        return Task.FromResult(new CoreWebView2Controller(controllerHandle, isCompositionController: false));
    }

    public Task<CoreWebView2CompositionController> CreateCoreWebView2CompositionControllerAsync(nint parentWindow)
    {
        ThrowIfDisposed();

        var hr = BrowserInterop.CreateController(_nativeHandle, parentWindow, 1, out var controllerHandle);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to create WebView2 composition controller.");
        return Task.FromResult(new CoreWebView2CompositionController(controllerHandle));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_nativeHandle != nint.Zero)
        {
            BrowserInterop.DestroyEnvironment(_nativeHandle);
            _nativeHandle = nint.Zero;
        }

        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

public class CoreWebView2Controller : IDisposable
{
    private readonly CoreWebView2 _coreWebView2;
    private readonly bool _isCompositionController;
    private GCHandle _userDataHandle;
    private bool _closed;

    private readonly BrowserInterop.NavigationStartingCallback _navigationStartingCallback;
    private readonly BrowserInterop.NavigationCompletedCallback _navigationCompletedCallback;
    private readonly BrowserInterop.SourceChangedCallback _sourceChangedCallback;
    private readonly BrowserInterop.ContentLoadingCallback _contentLoadingCallback;
    private readonly BrowserInterop.DocumentTitleChangedCallback _documentTitleChangedCallback;
    private readonly BrowserInterop.WebMessageReceivedCallback _webMessageReceivedCallback;
    private readonly BrowserInterop.NewWindowRequestedCallback _newWindowRequestedCallback;
    private readonly BrowserInterop.ProcessFailedCallback _processFailedCallback;
    private readonly BrowserInterop.ZoomFactorChangedCallback _zoomFactorChangedCallback;

    private Color _defaultBackgroundColor = Color.White;

    protected internal CoreWebView2Controller(nint nativeHandle, bool isCompositionController)
    {
        if (nativeHandle == nint.Zero)
        {
            throw new ArgumentException("Invalid native WebView2 controller handle.", nameof(nativeHandle));
        }

        NativeHandle = nativeHandle;
        _isCompositionController = isCompositionController;
        _coreWebView2 = new CoreWebView2(this);

        _navigationStartingCallback = OnNavigationStarting;
        _navigationCompletedCallback = OnNavigationCompleted;
        _sourceChangedCallback = OnSourceChanged;
        _contentLoadingCallback = OnContentLoading;
        _documentTitleChangedCallback = OnDocumentTitleChanged;
        _webMessageReceivedCallback = OnWebMessageReceived;
        _newWindowRequestedCallback = OnNewWindowRequested;
        _processFailedCallback = OnProcessFailed;
        _zoomFactorChangedCallback = OnZoomFactorChanged;

        _userDataHandle = GCHandle.Alloc(this, GCHandleType.Normal);

        var hr = BrowserInterop.SetCallbacks(
            NativeHandle,
            _navigationStartingCallback,
            _navigationCompletedCallback,
            _sourceChangedCallback,
            _contentLoadingCallback,
            _documentTitleChangedCallback,
            _webMessageReceivedCallback,
            _newWindowRequestedCallback,
            _processFailedCallback,
            _zoomFactorChangedCallback,
            GCHandle.ToIntPtr(_userDataHandle));
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to register WebView2 callbacks.");
    }

    internal nint NativeHandle { get; private set; }

    public virtual CoreWebView2 CoreWebView2 => _coreWebView2;

    public virtual Rectangle Bounds
    {
        get
        {
            ThrowIfClosed();
            var hr = BrowserInterop.GetBounds(NativeHandle, out var x, out var y, out var width, out var height);
            WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to get WebView2 bounds.");
            return new Rectangle(x, y, width, height);
        }
        set
        {
            ThrowIfClosed();
            var hr = BrowserInterop.SetBounds(NativeHandle, value.X, value.Y, value.Width, value.Height);
            WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to set WebView2 bounds.");
        }
    }

    public virtual bool IsVisible
    {
        set
        {
            ThrowIfClosed();
            var hr = BrowserInterop.SetIsVisible(NativeHandle, value ? 1 : 0);
            WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to set WebView2 visibility.");
        }
    }

    public virtual double ZoomFactor
    {
        get
        {
            ThrowIfClosed();
            var hr = BrowserInterop.GetZoomFactor(NativeHandle, out var zoomFactor);
            WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to get WebView2 zoom factor.");
            return zoomFactor;
        }
        set
        {
            ThrowIfClosed();
            var hr = BrowserInterop.SetZoomFactor(NativeHandle, value);
            WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to set WebView2 zoom factor.");
        }
    }

    public virtual Color DefaultBackgroundColor
    {
        get
        {
            ThrowIfClosed();

            var hr = BrowserInterop.GetDefaultBackgroundColor(NativeHandle, out var argb);
            if (hr < 0)
            {
                return _defaultBackgroundColor;
            }

            return Color.FromArgb(unchecked((int)argb));
        }
        set
        {
            ThrowIfClosed();
            _defaultBackgroundColor = value;
            var hr = BrowserInterop.SetDefaultBackgroundColor(NativeHandle, unchecked((uint)value.ToArgb()));
            if (hr < 0)
            {
                // Older runtimes may not support this API. Keep managed fallback value.
                return;
            }
        }
    }

    public event EventHandler? ZoomFactorChanged;

    public virtual void NotifyParentWindowPositionChanged()
    {
        ThrowIfClosed();
        var hr = BrowserInterop.NotifyParentWindowPositionChanged(NativeHandle);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to notify WebView2 parent window position change.");
    }

    public virtual void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        if (NativeHandle != nint.Zero)
        {
            BrowserInterop.Close(NativeHandle);
            BrowserInterop.DestroyController(NativeHandle);
            NativeHandle = nint.Zero;
        }

        if (_userDataHandle.IsAllocated)
        {
            _userDataHandle.Free();
        }
    }

    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    protected void ThrowIfClosed()
    {
        if (_closed || NativeHandle == nint.Zero)
        {
            throw new ObjectDisposedException(nameof(CoreWebView2Controller));
        }
    }

    private void OnNavigationStarting(nint userData, nint uri, int isRedirected, ref int cancel)
    {
        _ = userData;
        var args = new CoreWebView2NavigationStartingEventArgs(Marshal.PtrToStringUni(uri) ?? string.Empty, isRedirected != 0);
        _coreWebView2.RaiseNavigationStarting(args);
        cancel = args.Cancel ? 1 : 0;
    }

    private void OnNavigationCompleted(nint userData, int isSuccess, int httpStatusCode)
    {
        _ = userData;
        _coreWebView2.RaiseNavigationCompleted(new CoreWebView2NavigationCompletedEventArgs(isSuccess != 0, httpStatusCode));
    }

    private void OnSourceChanged(nint userData, int isNewDocument)
    {
        _ = userData;
        _coreWebView2.RaiseSourceChanged(new CoreWebView2SourceChangedEventArgs(isNewDocument != 0));
    }

    private void OnContentLoading(nint userData, int isErrorPage)
    {
        _ = userData;
        _coreWebView2.RaiseContentLoading(new CoreWebView2ContentLoadingEventArgs(isErrorPage != 0));
    }

    private void OnDocumentTitleChanged(nint userData, nint title)
    {
        _ = userData;
        _coreWebView2.RaiseDocumentTitleChanged();
    }

    private void OnWebMessageReceived(nint userData, nint message)
    {
        _ = userData;
        _coreWebView2.RaiseWebMessageReceived(new CoreWebView2WebMessageReceivedEventArgs(Marshal.PtrToStringUni(message) ?? string.Empty));
    }

    private void OnNewWindowRequested(nint userData, nint uri, int isUserInitiated, ref int handled)
    {
        _ = userData;
        var args = new CoreWebView2NewWindowRequestedEventArgs(Marshal.PtrToStringUni(uri) ?? string.Empty, isUserInitiated != 0);
        _coreWebView2.RaiseNewWindowRequested(args);
        handled = args.Handled ? 1 : 0;
    }

    private void OnProcessFailed(nint userData, int processFailedKind)
    {
        _ = userData;
        _coreWebView2.RaiseProcessFailed(new CoreWebView2ProcessFailedEventArgs((CoreWebView2ProcessFailedKind)processFailedKind));
    }

    private void OnZoomFactorChanged(nint userData, double zoomFactor)
    {
        _ = userData;
        _ = zoomFactor;
        ZoomFactorChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class CoreWebView2CompositionController : CoreWebView2Controller
{
    private object? _rootVisualTarget;
    private BrowserInterop.CursorChangedCallback? _cursorChangedCallback;
    private GCHandle _cursorCallbackHandle;

    internal CoreWebView2CompositionController(nint nativeHandle)
        : base(nativeHandle, isCompositionController: true)
    {
    }

    public event EventHandler<nint>? CursorChanged;

    public void EnableCursorChangeNotifications()
    {
        ThrowIfClosed();

        if (_cursorChangedCallback != null)
            return;

        _cursorChangedCallback = OnCursorChanged;
        _cursorCallbackHandle = GCHandle.Alloc(this, GCHandleType.Normal);

        var hr = BrowserInterop.SetCursorChangedCallback(
            NativeHandle,
            _cursorChangedCallback,
            GCHandle.ToIntPtr(_cursorCallbackHandle));
        if (hr < 0)
        {
            _cursorChangedCallback = null;
            if (_cursorCallbackHandle.IsAllocated)
                _cursorCallbackHandle.Free();
        }
    }

    private void OnCursorChanged(nint userData, nint cursorHandle)
    {
        _ = userData;
        CursorChanged?.Invoke(this, cursorHandle);
    }

    public object? RootVisualTarget
    {
        get => _rootVisualTarget;
        set
        {
            ThrowIfClosed();

            nint target = nint.Zero;
            bool releaseTarget = false;
            try
            {
                if (value != null)
                {
                    if (value is nint rawTarget)
                    {
                        target = rawTarget;
                    }
                    else if (value is IntPtr rawTargetPtr)
                    {
                        target = rawTargetPtr;
                    }
                    else
                    {
                        target = Marshal.GetIUnknownForObject(value);
                        releaseTarget = true;
                    }
                }

                var hr = BrowserInterop.SetRootVisualTarget(NativeHandle, target);
                WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to set WebView2 root visual target.");
                _rootVisualTarget = value;
            }
            finally
            {
                if (releaseTarget && target != nint.Zero)
                {
                    Marshal.Release(target);
                }
            }
        }
    }

    public void SendMouseInput(
        CoreWebView2MouseEventKind eventKind,
        CoreWebView2MouseEventVirtualKeys virtualKeys,
        uint mouseData,
        Point point)
    {
        ThrowIfClosed();

        var hr = BrowserInterop.SendMouseInput(
            NativeHandle,
            (int)eventKind,
            (int)virtualKeys,
            mouseData,
            point.X,
            point.Y);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to forward mouse input to WebView2 composition controller.");
    }
}

public sealed class CoreWebView2
{
    private readonly CoreWebView2Controller _controller;

    private static readonly BrowserInterop.ScriptCompletedCallback ScriptCompleted = OnScriptCompleted;
    private static readonly ConcurrentDictionary<nint, TaskCompletionSource<string>> PendingScriptCalls = new();

    internal CoreWebView2(CoreWebView2Controller controller)
    {
        _controller = controller;
    }

    public event EventHandler<CoreWebView2NavigationStartingEventArgs>? NavigationStarting;
    public event EventHandler<CoreWebView2NavigationCompletedEventArgs>? NavigationCompleted;
    public event EventHandler<object>? DocumentTitleChanged;
    public event EventHandler<CoreWebView2WebMessageReceivedEventArgs>? WebMessageReceived;
    public event EventHandler<CoreWebView2NewWindowRequestedEventArgs>? NewWindowRequested;
    public event EventHandler<CoreWebView2SourceChangedEventArgs>? SourceChanged;
    public event EventHandler<CoreWebView2ContentLoadingEventArgs>? ContentLoading;
    public event EventHandler<CoreWebView2ProcessFailedEventArgs>? ProcessFailed;

    public bool CanGoBack
    {
        get
        {
            var hr = BrowserInterop.GetCanGoBack(_controller.NativeHandle, out var canGoBack);
            WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to get CanGoBack state.");
            return canGoBack != 0;
        }
    }

    public bool CanGoForward
    {
        get
        {
            var hr = BrowserInterop.GetCanGoForward(_controller.NativeHandle, out var canGoForward);
            WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to get CanGoForward state.");
            return canGoForward != 0;
        }
    }

    public string Source
    {
        get
        {
            var hr = BrowserInterop.GetSource(_controller.NativeHandle, out var sourcePtr);
            WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to get WebView2 source.");
            return WebView2NativeHelpers.PtrToStringAndFree(sourcePtr);
        }
    }

    public string DocumentTitle
    {
        get
        {
            var hr = BrowserInterop.GetDocumentTitle(_controller.NativeHandle, out var titlePtr);
            WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to get WebView2 document title.");
            return WebView2NativeHelpers.PtrToStringAndFree(titlePtr);
        }
    }

    public void Navigate(string uri)
    {
        var hr = BrowserInterop.Navigate(_controller.NativeHandle, uri);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to navigate WebView2.");
    }

    public void NavigateToString(string htmlContent)
    {
        var hr = BrowserInterop.NavigateToString(_controller.NativeHandle, htmlContent);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to navigate WebView2 to string content.");
    }

    public void GoBack()
    {
        var hr = BrowserInterop.GoBack(_controller.NativeHandle);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to navigate back.");
    }

    public void GoForward()
    {
        var hr = BrowserInterop.GoForward(_controller.NativeHandle);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to navigate forward.");
    }

    public void Reload()
    {
        var hr = BrowserInterop.Reload(_controller.NativeHandle);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to reload WebView2.");
    }

    public void Stop()
    {
        var hr = BrowserInterop.Stop(_controller.NativeHandle);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to stop WebView2 navigation.");
    }

    public Task<string> ExecuteScriptAsync(string script)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs, GCHandleType.Normal);
        var userData = GCHandle.ToIntPtr(handle);
        PendingScriptCalls[userData] = tcs;

        var hr = BrowserInterop.ExecuteScriptAsync(_controller.NativeHandle, script, ScriptCompleted, userData);
        if (hr < 0)
        {
            PendingScriptCalls.TryRemove(userData, out _);
            handle.Free();
            WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to execute WebView2 script.");
        }

        return tcs.Task;
    }

    public void PostWebMessageAsString(string message)
    {
        var hr = BrowserInterop.PostWebMessageAsString(_controller.NativeHandle, message);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to post WebView2 string message.");
    }

    public void PostWebMessageAsJson(string json)
    {
        var hr = BrowserInterop.PostWebMessageAsJson(_controller.NativeHandle, json);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to post WebView2 JSON message.");
    }

    public void OpenDevToolsWindow()
    {
        var hr = BrowserInterop.OpenDevToolsWindow(_controller.NativeHandle);
        WebView2NativeHelpers.ThrowIfFailed(hr, "Failed to open WebView2 devtools.");
    }

    internal void RaiseNavigationStarting(CoreWebView2NavigationStartingEventArgs args)
    {
        NavigationStarting?.Invoke(this, args);
    }

    internal void RaiseNavigationCompleted(CoreWebView2NavigationCompletedEventArgs args)
    {
        NavigationCompleted?.Invoke(this, args);
    }

    internal void RaiseDocumentTitleChanged()
    {
        DocumentTitleChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseWebMessageReceived(CoreWebView2WebMessageReceivedEventArgs args)
    {
        WebMessageReceived?.Invoke(this, args);
    }

    internal void RaiseNewWindowRequested(CoreWebView2NewWindowRequestedEventArgs args)
    {
        NewWindowRequested?.Invoke(this, args);
    }

    internal void RaiseSourceChanged(CoreWebView2SourceChangedEventArgs args)
    {
        SourceChanged?.Invoke(this, args);
    }

    internal void RaiseContentLoading(CoreWebView2ContentLoadingEventArgs args)
    {
        ContentLoading?.Invoke(this, args);
    }

    internal void RaiseProcessFailed(CoreWebView2ProcessFailedEventArgs args)
    {
        ProcessFailed?.Invoke(this, args);
    }

    private static void OnScriptCompleted(nint userData, int result, nint resultJson)
    {
        if (!PendingScriptCalls.TryRemove(userData, out var tcs))
        {
            return;
        }

        var handle = GCHandle.FromIntPtr(userData);
        if (handle.IsAllocated)
        {
            handle.Free();
        }

        if (result < 0)
        {
            tcs.TrySetException(new COMException("WebView2 script execution failed.", result));
            return;
        }

        tcs.TrySetResult(Marshal.PtrToStringUni(resultJson) ?? string.Empty);
    }
}
