using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Jalium.UI.Input;

namespace Jalium.UI.Interop;

/// <summary>
/// Base host for legacy COM controls. The implementation activates an existing
/// in-process COM class on Windows and uses its <c>IOleWindow</c> handle when the
/// control exposes one; other platforms fail explicitly before any COM call.
/// </summary>
public class ActiveXHost : HwndHost
{
    private readonly Guid _clsid;
    private readonly bool _isTrusted;
    private object? _activeXInstance;
    private HandleRef _parentHandle;
    private bool _isDisposed;

    internal ActiveXHost(Guid clsid, bool fTrusted)
    {
        if (clsid == Guid.Empty)
        {
            throw new ArgumentException("An ActiveX class identifier is required.", nameof(clsid));
        }

        _clsid = clsid;
        _isTrusted = fTrusted;
    }

    /// <summary>Gets whether this host has released its COM control.</summary>
    protected bool IsDisposed => _isDisposed;

    /// <inheritdoc />
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2057",
        Justification = "The COM class is selected by its CLSID and cannot be statically rooted by type name.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "The COM activation type is selected by CLSID at runtime, so its public constructor cannot be annotated statically.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "ActiveX activation is a Windows-only compatibility path and is never used by NativeAOT platform hosts.")]
    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ActiveX hosting requires Windows COM and Win32 windowing.");
        }

        _parentHandle = hwndParent;
        Type? activeXType = Type.GetTypeFromCLSID(_clsid, throwOnError: true);
        _activeXInstance = Activator.CreateInstance(activeXType!);
        if (_activeXInstance is not IOleWindow oleWindow)
        {
            return new HandleRef(this, IntPtr.Zero);
        }

        int result = oleWindow.GetWindow(out IntPtr hwnd);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        return new HandleRef(this, hwnd);
    }

    /// <inheritdoc />
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        object? instance = Interlocked.Exchange(ref _activeXInstance, null);
        _parentHandle = default;
        if (instance is not null && OperatingSystem.IsWindows() && Marshal.IsComObject(instance))
        {
            _ = Marshal.FinalReleaseComObject(instance);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        base.Dispose(disposing);

        // BuildWindowCore can activate a windowless object, in which case HwndHost
        // has no handle and therefore has nothing to pass to DestroyWindowCore.
        if (_activeXInstance is not null)
        {
            DestroyWindowCore(new HandleRef(this, IntPtr.Zero));
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size swConstraint)
    {
        double width = double.IsFinite(swConstraint.Width) ? Math.Max(0, swConstraint.Width) : 0;
        double height = double.IsFinite(swConstraint.Height) ? Math.Max(0, swConstraint.Height) : 0;
        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override void OnAccessKey(AccessKeyEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        base.OnAccessKey(args);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) =>
        base.OnPropertyChanged(e);

    /// <inheritdoc />
    protected override void OnWindowPositionChanged(Rect bounds)
    {
        base.OnWindowPositionChanged(bounds);
        if (!_isTrusted || _parentHandle.Handle == IntPtr.Zero)
        {
            return;
        }

        // The native child owns its own OLE in-place positioning contract. HwndHost
        // already applies the Win32 position before this notification is raised.
    }

    [ComImport]
    [Guid("00000114-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleWindow
    {
        [PreserveSig]
        int GetWindow(out IntPtr phwnd);

        [PreserveSig]
        int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
    }
}
