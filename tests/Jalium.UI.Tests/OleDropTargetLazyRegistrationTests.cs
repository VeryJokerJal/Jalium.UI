using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

/// <summary>
/// Exercises lazy OLE initialization against real HWNDs without showing a Jalium
/// window or creating a rendering backend. Each test owns a dedicated apartment so
/// OleInitialize/OleUninitialize balance can be observed independently.
/// </summary>
[Collection("Application")]
[SupportedOSPlatform("windows")]
public sealed class OleDropTargetLazyRegistrationTests
{
    private static readonly FieldInfo HandleField =
        typeof(Window).GetField("<Handle>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Window.Handle backing field was not found.");

    [RequiresWindowsFact]
    public void EmptyWindow_HandleReady_DoesNotAcquireOleOrRegister()
    {
        RunSta(() =>
        {
            Assert.Equal(0, OleDropTarget.CurrentThreadOleLeaseCountForTesting);

            var window = new Window();
            using var native = new HiddenNativeWindow(8, 8);
            AttachHandle(window, native.Hwnd);
            try
            {
                Assert.Equal(0, window.DropTargetRootCountForTesting);
                Assert.False(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));
                Assert.Equal(0, OleDropTarget.CurrentThreadOleLeaseCountForTesting);
            }
            finally
            {
                DetachHandle(window, native.Hwnd);
            }
        });
    }

    [RequiresWindowsFact]
    public void AllowDrop_BeforeAndAfterHandleReady_RegistersOnceAndRevokes()
    {
        RunSta(() =>
        {
            var window = new Window
            {
                AllowDrop = true,
            };

            Assert.Equal(1, window.DropTargetRootCountForTesting);
            Assert.Equal(0, OleDropTarget.CurrentThreadOleLeaseCountForTesting);

            using var native = new HiddenNativeWindow(8, 8);
            AttachHandle(window, native.Hwnd);
            try
            {
                Assert.True(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));
                Assert.Equal(1, OleDropTarget.CurrentThreadOleLeaseCountForTesting);

                // An explicit retry for the same Window must reuse the existing native
                // COM target and apartment lease.
                Assert.True(OleDropTarget.RegisterWindow(window));
                Assert.True(OleDropTarget.RegisterWindow(window));
                Assert.Equal(1, OleDropTarget.CurrentThreadOleLeaseCountForTesting);

                window.AllowDrop = false;
                Assert.Equal(0, window.DropTargetRootCountForTesting);
                Assert.False(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));
                Assert.Equal(0, OleDropTarget.CurrentThreadOleLeaseCountForTesting);

                // Setting AllowDrop after the HWND exists must register immediately.
                window.AllowDrop = true;
                Assert.True(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));
                Assert.Equal(1, OleDropTarget.CurrentThreadOleLeaseCountForTesting);

                window.CloseNativeDropTarget(native.Hwnd, nativeWindowAlive: true);
                Assert.False(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));
                Assert.Equal(0, OleDropTarget.CurrentThreadOleLeaseCountForTesting);
            }
            finally
            {
                DetachHandle(window, native.Hwnd);
            }
        });
    }

    [RequiresWindowsFact]
    public void LateAttachedInheritedAllowDropTarget_RegistersAndRemovalRevokes()
    {
        RunSta(() =>
        {
            var host = new Grid();
            var inheritedChild = new Border();
            var target = new Border
            {
                AllowDrop = true,
                Child = inheritedChild,
            };
            var window = new Window
            {
                Content = host,
            };

            using var native = new HiddenNativeWindow(8, 8);
            AttachHandle(window, native.Hwnd);
            try
            {
                Assert.False(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));

                host.Children.Add(target);
                Assert.True(inheritedChild.AllowDrop);
                Assert.Equal(1, window.DropTargetRootCountForTesting);
                Assert.True(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));
                Assert.Equal(1, OleDropTarget.CurrentThreadOleLeaseCountForTesting);

                // Moving the top-most effective root between an ancestor and a local
                // descendant is one batched net-zero transition: no revoke/re-register.
                host.AllowDrop = true;
                Assert.Equal(1, window.DropTargetRootCountForTesting);
                Assert.True(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));
                Assert.Equal(1, OleDropTarget.CurrentThreadOleLeaseCountForTesting);

                host.AllowDrop = false;
                Assert.Equal(1, window.DropTargetRootCountForTesting);
                Assert.True(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));
                Assert.Equal(1, OleDropTarget.CurrentThreadOleLeaseCountForTesting);

                host.Children.Remove(target);
                Assert.False(host.Children.Contains(target));
                Assert.Equal(0, window.DropTargetRootCountForTesting);
                Assert.False(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));
                Assert.Equal(0, OleDropTarget.CurrentThreadOleLeaseCountForTesting);
            }
            finally
            {
                DetachHandle(window, native.Hwnd);
            }
        });
    }

    [RequiresWindowsFact]
    public void FailedNativeRegistration_ReleasesOleLeaseAndCanRetry()
    {
        RunSta(() =>
        {
            var window = new Window
            {
                AllowDrop = true,
            };

            nint destroyedHwnd;
            using (var destroyed = new HiddenNativeWindow(8, 8))
            {
                destroyedHwnd = destroyed.Hwnd;
            }

            SetHandle(window, destroyedHwnd);
            window.OnNativeHandleReadyForDropTargets();

            Assert.False(OleDropTarget.IsWindowRegisteredForTesting(destroyedHwnd));
            Assert.Equal(0, OleDropTarget.CurrentThreadOleLeaseCountForTesting);

            using var native = new HiddenNativeWindow(8, 8);
            AttachHandle(window, native.Hwnd);
            try
            {
                Assert.True(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));
                Assert.Equal(1, OleDropTarget.CurrentThreadOleLeaseCountForTesting);

                window.AllowDrop = false;
                Assert.False(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));
                Assert.Equal(0, OleDropTarget.CurrentThreadOleLeaseCountForTesting);
            }
            finally
            {
                DetachHandle(window, native.Hwnd);
            }
        });
    }

    [RequiresWindowsFact]
    public void OleApartment_NestedLeasesBalanceOnOwningSta()
    {
        RunSta(() =>
        {
            Assert.Equal(0, OleDropTarget.CurrentThreadOleLeaseCountForTesting);

            using var first = OleDropTarget.TryAcquireOleApartment();
            Assert.NotNull(first);
            Assert.Equal(1, OleDropTarget.CurrentThreadOleLeaseCountForTesting);

            using (var second = OleDropTarget.TryAcquireOleApartment())
            {
                Assert.NotNull(second);
                Assert.Equal(2, OleDropTarget.CurrentThreadOleLeaseCountForTesting);
            }

            Assert.Equal(1, OleDropTarget.CurrentThreadOleLeaseCountForTesting);
            first.Dispose();
            Assert.Equal(0, OleDropTarget.CurrentThreadOleLeaseCountForTesting);
        });
    }

    [RequiresWindowsFact]
    public unsafe void OleInitializeFailure_IsNotLatchedAndCanRetryOnSameNativeThread()
    {
        nint thread = CreateThread(
            nint.Zero,
            0,
            (delegate* unmanaged[Stdcall]<nint, uint>)&RunOleInitializeRetryProbe,
            nint.Zero,
            0,
            out _);
        Assert.NotEqual(nint.Zero, thread);

        try
        {
            Assert.Equal(WAIT_OBJECT_0, WaitForSingleObject(thread, 30_000));
            Assert.True(GetExitCodeThread(thread, out uint result));
            Assert.True(result == 0, $"Native OLE retry probe failed at stage {result}.");
        }
        finally
        {
            _ = CloseHandle(thread);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint RunOleInitializeRetryProbe(nint state)
    {
        try
        {
            int hr = CoInitializeEx(nint.Zero, COINIT_MULTITHREADED);
            if (hr < 0)
            {
                return 1;
            }

            try
            {
                // OleInitialize requests an STA and must fail while this otherwise
                // clean native thread is explicitly initialized as MTA.
                OleDropTarget.OleApartmentLease? unexpected =
                    OleDropTarget.TryAcquireOleApartment();
                if (unexpected is not null)
                {
                    unexpected.Dispose();
                    return 2;
                }
                if (OleDropTarget.CurrentThreadOleLeaseCountForTesting != 0)
                {
                    return 3;
                }
            }
            finally
            {
                CoUninitialize();
            }

            // The failed attempt must leave no latched failure state. Once the MTA
            // initialization is removed, the same OS thread can acquire an STA lease.
            OleDropTarget.OleApartmentLease? retry = OleDropTarget.TryAcquireOleApartment();
            if (retry is null)
            {
                return 4;
            }
            if (OleDropTarget.CurrentThreadOleLeaseCountForTesting != 1)
            {
                retry.Dispose();
                return 5;
            }

            retry.Dispose();
            return OleDropTarget.CurrentThreadOleLeaseCountForTesting == 0 ? 0u : 6u;
        }
        catch
        {
            // Never let an exception escape a raw reverse-P/Invoke thread entry point.
            return 255;
        }
    }

    [RequiresWindowsFact]
    public void AllowDropRemoval_DuringNativeDrag_DefersRevokeUntilCancel()
    {
        RunSta(() =>
        {
            var window = new Window
            {
                AllowDrop = true,
            };

            using var native = new HiddenNativeWindow(8, 8);
            AttachHandle(window, native.Hwnd);
            try
            {
                nint comObject = OleDropTarget.GetWindowComObjectForTesting(native.Hwnd);
                Assert.NotEqual(nint.Zero, comObject);

                BeginSyntheticNativeDrag(comObject);

                window.AllowDrop = false;
                Assert.Equal(0, window.DropTargetRootCountForTesting);
                Assert.True(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));
                Assert.Equal(1, OleDropTarget.CurrentThreadOleLeaseCountForTesting);

                CancelSyntheticNativeDrag(comObject);
                Assert.False(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));
                Assert.Equal(0, OleDropTarget.CurrentThreadOleLeaseCountForTesting);
            }
            finally
            {
                DetachHandle(window, native.Hwnd);
            }
        });
    }

    [RequiresWindowsFact]
    public void RemovedTarget_DoesNotReceiveDropWithoutAnInterveningDragOver()
    {
        RunSta(() =>
        {
            var host = new Grid();
            var target = new Border
            {
                AllowDrop = true,
                Background = Jalium.UI.Media.Brushes.Transparent,
            };
            host.Children.Add(target);

            var window = new Window
            {
                Width = 100,
                Height = 100,
                Content = host,
            };
            window.Measure(new Size(100, 100));
            window.Arrange(new Rect(0, 0, 100, 100));

            var clientPoint = new Point(50, 50);
            var hit = window.HitTest(clientPoint)?.VisualHit as UIElement;
            Assert.Same(target, DragDropPlatform.FindDropTargetElement(hit));

            int dragEnterCount = 0;
            int dropCount = 0;
            target.DragEnter += (_, _) => dragEnterCount++;
            target.Drop += (_, _) => dropCount++;

            using var native = new HiddenNativeWindow(100, 100);
            AttachHandle(window, native.Hwnd);
            try
            {
                nint comObject = OleDropTarget.GetWindowComObjectForTesting(native.Hwnd);
                Assert.NotEqual(nint.Zero, comObject);

                long nativePoint = PackPoint(50, 50);
                BeginSyntheticNativeDrag(comObject, nativePoint);
                Assert.Equal(1, dragEnterCount);

                host.Children.Remove(target);
                Assert.False(host.Children.Contains(target));
                Assert.True(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));

                CompleteSyntheticNativeDrop(comObject, nativePoint);
                Assert.Equal(0, dropCount);
                Assert.False(OleDropTarget.IsWindowRegisteredForTesting(native.Hwnd));
                Assert.Equal(0, OleDropTarget.CurrentThreadOleLeaseCountForTesting);
            }
            finally
            {
                DetachHandle(window, native.Hwnd);
            }
        });
    }

    private static unsafe void BeginSyntheticNativeDrag(nint comObject)
        => BeginSyntheticNativeDrag(comObject, 0);

    private static unsafe void BeginSyntheticNativeDrag(nint comObject, long nativePoint)
    {
        nint vtable = *(nint*)comObject;
        var dragEnter = (delegate* unmanaged[Stdcall]<nint, nint, uint, long, uint*, int>)
            (*(nint*)(vtable + (3 * nint.Size)));
        uint effect = (uint)DragDropEffects.Copy;
        Assert.Equal(0, dragEnter(comObject, nint.Zero, 0, nativePoint, &effect));
    }

    private static unsafe void CancelSyntheticNativeDrag(nint comObject)
    {
        nint vtable = *(nint*)comObject;
        var dragLeave = (delegate* unmanaged[Stdcall]<nint, int>)
            (*(nint*)(vtable + (5 * nint.Size)));
        Assert.Equal(0, dragLeave(comObject));
    }

    private static unsafe void CompleteSyntheticNativeDrop(nint comObject, long nativePoint)
    {
        nint vtable = *(nint*)comObject;
        var drop = (delegate* unmanaged[Stdcall]<nint, nint, uint, long, uint*, int>)
            (*(nint*)(vtable + (6 * nint.Size)));
        uint effect = (uint)DragDropEffects.Copy;
        Assert.Equal(0, drop(comObject, nint.Zero, 0, nativePoint, &effect));
    }

    private static long PackPoint(int x, int y) =>
        unchecked((long)(uint)x | ((long)(uint)y << 32));

    private static void AttachHandle(Window window, nint hwnd)
    {
        SetHandle(window, hwnd);
        window.OnNativeHandleReadyForDropTargets();
    }

    private static void DetachHandle(Window window, nint hwnd)
    {
        window.CloseNativeDropTarget(hwnd, nativeWindowAlive: true);
        SetHandle(window, nint.Zero);
    }

    private static void SetHandle(Window window, nint hwnd) => HandleField.SetValue(window, hwnd);

    private static void RunSta(Action body) => RunApartment(body, ApartmentState.STA);

    private static void RunApartment(Action body, ApartmentState apartmentState)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(apartmentState);
        thread.Start();
        thread.Join();

        captured?.Throw();
    }

    private const uint COINIT_MULTITHREADED = 0x0;
    private const uint WAIT_OBJECT_0 = 0x0000_0000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe nint CreateThread(
        nint threadAttributes,
        nuint stackSize,
        delegate* unmanaged[Stdcall]<nint, uint> startAddress,
        nint parameter,
        uint creationFlags,
        out uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeThread(nint thread, out uint exitCode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
