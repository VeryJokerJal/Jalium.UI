using System.Dynamic;
using System.Reflection;
using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusWisp;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using InteropD3DImage = Jalium.UI.Interop.D3DImage;
using InteropBitmap = Jalium.UI.Interop.InteropBitmap;

namespace Jalium.UI.Tests;

public sealed class InteropRemainingParityTests
{
    [Fact]
    public void ComponentDispatcher_UsesThreadLocalModalAndMessageChains()
    {
        var calls = new List<string>();
        EventHandler enter = (_, _) => calls.Add("enter");
        EventHandler leave = (_, _) => calls.Add("leave");
        ThreadMessageEventHandler filter = (ref MSG msg, ref bool handled) =>
        {
            calls.Add("filter");
            msg.message++;
        };
        ThreadMessageEventHandler preprocess = (ref MSG msg, ref bool handled) =>
        {
            calls.Add("preprocess");
            handled = msg.message == 11;
        };

        ComponentDispatcher.EnterThreadModal += enter;
        ComponentDispatcher.LeaveThreadModal += leave;
        ComponentDispatcher.ThreadFilterMessage += filter;
        ComponentDispatcher.ThreadPreprocessMessage += preprocess;
        try
        {
            ComponentDispatcher.PushModal();
            ComponentDispatcher.PushModal();
            Assert.True(ComponentDispatcher.IsThreadModal);

            var message = new MSG { message = 10 };
            Assert.True(ComponentDispatcher.RaiseThreadMessage(ref message));
            Assert.Equal(11, message.message);

            ComponentDispatcher.PopModal();
            Assert.True(ComponentDispatcher.IsThreadModal);
            ComponentDispatcher.PopModal();
            Assert.False(ComponentDispatcher.IsThreadModal);
            Assert.Equal(["enter", "filter", "preprocess", "leave"], calls);
        }
        finally
        {
            ComponentDispatcher.EnterThreadModal -= enter;
            ComponentDispatcher.LeaveThreadModal -= leave;
            ComponentDispatcher.ThreadFilterMessage -= filter;
            ComponentDispatcher.ThreadPreprocessMessage -= preprocess;
            while (ComponentDispatcher.IsThreadModal)
            {
                ComponentDispatcher.PopModal();
            }
        }
    }

    [Fact]
    public void ComponentDispatcher_SubscriptionsAreIsolatedPerThread()
    {
        int callbacks = 0;
        ThreadMessageEventHandler handler = (ref MSG msg, ref bool handled) => callbacks++;
        ComponentDispatcher.ThreadFilterMessage += handler;
        try
        {
            int workerCallbacks = -1;
            var worker = new Thread(() =>
            {
                var message = new MSG();
                _ = ComponentDispatcher.RaiseThreadMessage(ref message);
                workerCallbacks = callbacks;
            });
            worker.Start();
            worker.Join();

            Assert.Equal(0, workerCallbacks);
            var currentMessage = new MSG();
            _ = ComponentDispatcher.RaiseThreadMessage(ref currentMessage);
            Assert.Equal(1, callbacks);
        }
        finally
        {
            ComponentDispatcher.ThreadFilterMessage -= handler;
        }
    }

    [Fact]
    public void CursorInteropHelper_KeepsCallerOwnedSafeHandleAlive()
    {
        using var handle = new TestSafeHandle(new IntPtr(123));
        using Cursor cursor = CursorInteropHelper.Create(handle);

        Assert.Equal(CursorType.None, cursor.CursorType);
        Assert.False(handle.IsClosed);
        Assert.Throws<ArgumentNullException>(() => CursorInteropHelper.Create(null!));
    }

    [Fact]
    public void BrowserAndActiveXContracts_HaveWpfShapeAndSafeDefaults()
    {
        Assert.False(BrowserInteropHelper.IsBrowserHosted);
        Assert.Null(BrowserInteropHelper.ClientSite);
        Assert.Null(BrowserInteropHelper.HostScript);
        Assert.Null(BrowserInteropHelper.Source);

        Assert.Equal(typeof(DynamicObject), typeof(DynamicScriptObject).BaseType);
        Assert.True(typeof(DynamicScriptObject).IsSealed);
        Assert.Equal(typeof(HwndHost), typeof(ActiveXHost).BaseType);
        Assert.Contains(
            typeof(IErrorPage).GetProperties(),
            property => property.Name == nameof(IErrorPage.GetWinFxCallback));
        Assert.Contains(
            typeof(IProgressPage).GetMethods(),
            method => method.Name == nameof(IProgressPage.UpdateProgress) &&
                      method.GetParameters().Select(parameter => parameter.Name)
                          .SequenceEqual(["bytesDownloaded", "bytesTotal"]));
    }

    [Fact]
    public void DynamicScriptObject_BridgesManagedHostObjects()
    {
        dynamic script = new DynamicScriptObject(new Dictionary<string, object?>
        {
            ["answer"] = 42,
        });

        Assert.Equal(42, (int)script.answer);
        script.answer = 43;
        Assert.Equal(43, (int)script["answer"]);
    }

    [Fact]
    public void D3DImage_UsesCanonicalNamespaceDependencyPropertyAndDurationContract()
    {
        var image = new InteropD3DImage(192, 192);
        Assert.Equal(typeof(ImageSource), typeof(InteropD3DImage).BaseType);
        Assert.True(image.IsFrontBufferAvailable);
        Assert.Same(
            InteropD3DImage.IsFrontBufferAvailableProperty,
            typeof(InteropD3DImage).GetField(nameof(InteropD3DImage.IsFrontBufferAvailableProperty))!.GetValue(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.TryLock(Duration.Automatic));

        Assert.True(image.TryLock(Duration.Forever));
        image.SetPixelSize(200, 100);
        image.SetBackBuffer(Jalium.UI.Interop.D3DResourceType.IDirect3DSurface9, new IntPtr(123));
        image.AddDirtyRect(new Int32Rect(0, 0, 20, 10));
        image.Unlock();

        Assert.Equal(100, image.Width);
        Assert.Equal(50, image.Height);
        Assert.Equal(200, image.PixelWidth);
        Assert.Equal(100, image.PixelHeight);
        Assert.Equal(new IntPtr(123), image.NativeHandle);
        Assert.IsType<InteropD3DImage>(image.Clone());
    }

    [Fact]
    public void InteropBitmapAndImaging_ExposeCanonicalWpfSignatures()
    {
        Assert.Equal(typeof(Jalium.UI.Media.Imaging.BitmapSource), typeof(InteropBitmap).BaseType);
        Assert.True(typeof(InteropBitmap).IsSealed);

        MethodInfo invalidate = typeof(InteropBitmap).GetMethod(
            nameof(InteropBitmap.Invalidate),
            [typeof(Int32Rect?)])!;
        Assert.Equal("dirtyRect", Assert.Single(invalidate.GetParameters()).Name);

        MethodInfo memoryFactory = typeof(Imaging).GetMethod(
            nameof(Imaging.CreateBitmapSourceFromMemorySection))!;
        Assert.Equal(
            ["section", "pixelWidth", "pixelHeight", "format", "stride", "offset"],
            memoryFactory.GetParameters().Select(parameter => parameter.Name));
    }

    [Fact]
    public void WindowInteropHelper_UsesCanonicalNamespaceAndDelegatesToWindow()
    {
        var window = new Window();
        var helper = new Jalium.UI.Interop.WindowInteropHelper(window);

        Assert.Null(typeof(Jalium.UI.Interop.WindowInteropHelper).Assembly.GetType(
            "Jalium.UI.Controls.WindowInteropHelper", throwOnError: false));
        Assert.Equal(window.Handle, helper.Handle);
        Assert.Equal(window.Handle, helper.EnsureHandle());
        helper.Owner = new IntPtr(123);
        Assert.Equal(new IntPtr(123), helper.Owner);
    }

    [Fact]
    public void WispTabletCollection_IsCanonicalTabletCollectionAdapter()
    {
        Assert.Equal(typeof(TabletDeviceCollection), typeof(WispTabletDeviceCollection).BaseType);
        ConstructorInfo constructor = Assert.Single(
            typeof(WispTabletDeviceCollection).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.True(constructor.IsAssembly);
    }

    private sealed class TestSafeHandle : SafeHandle
    {
        internal TestSafeHandle(IntPtr handle)
            : base(IntPtr.Zero, ownsHandle: false)
        {
            SetHandle(handle);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle() => true;
    }
}
