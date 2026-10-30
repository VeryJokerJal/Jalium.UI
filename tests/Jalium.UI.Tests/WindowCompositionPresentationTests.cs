using Jalium.UI.Interop;
using Jalium.UI.Threading;
using System.Runtime.InteropServices;
using System.Text;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class WindowCompositionPresentationTests
{
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void CompositionResizePresentation_KeepsItsStationaryEdgesAndReleasesItsWindow()
    {
        DispatcherWindowsCompositionTests.RunOnThread(() =>
        {
            Dispatcher.CurrentDispatcher.CoreDispatcher.EnsureWindowsCompositionQueue();
            using var window = new HiddenNativeWindow(640, 480, 0x00200000);
            using var context = new RenderContext(RenderBackend.D3D12);
            _ = ShowWindow(window.Hwnd, 4); // SW_SHOWNOACTIVATE
            for (int round = 0; round < 2; round++)
            {
                nint host;
                using (var target = context.CreateRenderTargetForComposition(window.Hwnd, 640, 480))
                {
                    host = FindResizeHost(window.Hwnd);
                    Assert.NotEqual(0, host);
                    Assert.False(IsWindowVisible(host));
                    Assert.Equal((nint)(-1), SendMessage(host, 0x0084, 0, 0)); // HTTRANSPARENT
                    Assert.NotEqual(0L, GetWindowLong(host, -20) & 0x08000000); // WS_EX_NOACTIVATE
                    foreach (int edge in new[] { 1, 3, 4, 5, 7 }) // Left, top, top-left, top-right, bottom-left.
                    {
                        Assert.True(SetWindowPos(window.Hwnd, 0, 300, 200, 640, 480, 0x0014));
                        Assert.True(SetResizeEdge(window.Hwnd, "Jalium.UI.LiveResizeEdge", edge));
                        _ = SendMessage(window.Hwnd, 0x0231, 0, 0);
                        Assert.Equal(JaliumResult.Ok, target.Resize(640, 480));
                        Draw();
                        Assert.True(IsWindowVisible(host));
                        Assert.True(GetWindowRect(host, out var before));
                        bool right = edge is 1 or 4 or 7, bottom = edge is 3 or 4 or 5;
                        Assert.Equal(right ? 940 : 300, right ? before.right : before.left);
                        Assert.Equal(bottom ? 680 : 200, bottom ? before.bottom : before.top);
                        Assert.True(SetWindowPos(window.Hwnd, 0, right ? 220 : 300, bottom ? 150 : 200,
                            right ? 720 : 640, bottom ? 530 : 480, 0x0014));
                        Assert.True(GetWindowRect(host, out var after));
                        Assert.Equal(before.right - before.left, after.right - after.left);
                        Assert.Equal(before.bottom - before.top, after.bottom - after.top);
                        Assert.Equal(right ? before.right : before.left, right ? after.right : after.left);
                        Assert.Equal(bottom ? before.bottom : before.top, bottom ? after.bottom : after.top);

                        // A native WebView draws above the surface and must remain visible.
                        Assert.True(target.TryCreateWebViewCompositionVisual(out nint visual));
                        Draw();
                        Assert.False(IsWindowVisible(host));
                        target.DestroyWebViewCompositionVisual(visual);
                        Draw();
                        Assert.True(IsWindowVisible(host));

                        _ = RemoveResizeEdge(window.Hwnd, "Jalium.UI.LiveResizeEdge");
                        _ = SendMessage(window.Hwnd, 0x0232, 0, 0);
                        Assert.False(IsWindowVisible(host));
                        Assert.Equal(JaliumResult.Ok, target.Resize(right ? 720 : 640, bottom ? 530 : 480));
                        Draw();
                    }

                    void Draw()
                    {
                        target.SetFullInvalidation();
                        target.BeginDraw();
                        target.Clear(0.1f, 0.2f, 0.3f, 1);
                        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
                        while (PeekMessage(out var message, 0, 0, 0, 1))
                            _ = DispatchMessage(ref message);
                    }
                }
                Assert.False(IsWindow(host));
                Assert.Equal(0, FindResizeHost(window.Hwnd));
                Assert.True(IsWindow(window.Hwnd));
            }
        });
    }

    private static nint FindResizeHost(nint owner)
    {
        nint host = 0;
        _ = EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            var name = new StringBuilder(128);
            _ = GetClassName(window, name, name.Capacity);
            if (GetWindow(window, 4) == owner && name.ToString() == "Jalium.UI.CompositionResizeHost")
                host = window;
            return true;
        }, 0);
        return host;
    }

    private delegate bool EnumWindow(nint window, nint parameter);
    [StructLayout(LayoutKind.Sequential)]
    private struct MessageData
    {
        public nint Window;
        public uint Message;
        public nint WParam, LParam;
        public uint Time;
        public int X, Y;
    }
    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, nint parameter);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll", EntryPoint = "SetPropW", CharSet = CharSet.Unicode)]
    private static extern bool SetResizeEdge(nint window, string name, nint edge);
    [DllImport("user32.dll", EntryPoint = "RemovePropW", CharSet = CharSet.Unicode)]
    private static extern nint RemoveResizeEdge(nint window, string name);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    private static extern bool PeekMessage(out MessageData message, nint window, uint first, uint last, uint remove);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(ref MessageData message);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void SoftwareFallback_OnCompositionOnlyWindow_ResizesAndReleasesItsPresentationHost()
    {
        DispatcherWindowsCompositionTests.RunOnThread(() =>
        {
            using var window = new HiddenNativeWindow(320, 240, 0x00200000);
            using var context = new RenderContext(RenderBackend.Software);
            for (int round = 0; round < 2; round++)
            {
                nint host;
                using (var target = context.CreateRenderTarget(window.Hwnd, 320, 240))
                {
                    host = GetWindow(window.Hwnd, 5); // GW_CHILD
                    Assert.NotEqual(0, host);
                    Assert.Equal(0L, GetWindowLong(host, -16) & 0x00C00000); // WS_CAPTION
                    Assert.True(SetWindowPos(window.Hwnd, 0, 0, 0, 460, 300, 0x0016));
                    Assert.True(GetClientRect(host, out var bounds));
                    Assert.Equal(460, bounds.right);
                    Assert.Equal(300, bounds.bottom);
                    Assert.Equal(JaliumResult.Ok, target.Resize(460, 300));
                    target.BeginDraw();
                    target.Clear(0.1f, 0.2f, 0.3f, 1);
                    Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
                }
                Assert.False(IsWindow(host));
                Assert.Equal(0, GetWindow(window.Hwnd, 5));
                Assert.True(IsWindow(window.Hwnd));
                Assert.NotEqual(0L, GetWindowLong(window.Hwnd, -20) & 0x00200000);
            }
        });
    }

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void HwndAndCompositionTargets_KeepExactPixelsAcrossResizeAndRecreation()
    {
        DispatcherWindowsCompositionTests.RunOnThread(() =>
        {
            Dispatcher.CurrentDispatcher.CoreDispatcher.EnsureWindowsCompositionQueue();
            using var window = new HiddenNativeWindow(640, 480);
            using var context = new RenderContext(RenderBackend.D3D12);
            using var cyan = context.CreateSolidBrush(0, 1, 1, 1);
            foreach (bool composition in new[] { false, true, true })
            {
                using var target = composition
                    ? context.CreateRenderTargetForComposition(window.Hwnd, 320, 240)
                    : context.CreateRenderTarget(window.Hwnd, 320, 240);
                foreach (var (width, height) in new[] { (320, 240), (480, 320), (240, 160), (2300, 700) })
                {
                    Assert.Equal(JaliumResult.Ok, target.Resize(width, height));
                    target.SetFullInvalidation();
                    target.BeginDraw();
                    target.Clear(0.1f, 0.2f, 0.3f, 1);
                    target.FillRectangle(width - 16, height - 12, 8, 4, cyan);
                    Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                    Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
                    var pixels = new byte[width * height * 4];
                    Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, (uint)width * 4, out int capturedWidth, out int capturedHeight));
                    Assert.Equal(width, capturedWidth);
                    Assert.Equal(height, capturedHeight);
                    int marker = ((height - 10) * width + width - 12) * 4;
                    Assert.InRange(pixels[marker], (byte)240, byte.MaxValue);
                    Assert.InRange(pixels[marker + 1], (byte)240, byte.MaxValue);
                    Assert.InRange(pixels[marker + 2], (byte)0, (byte)15);
                    Assert.InRange(pixels[2], (byte)20, (byte)32);
                }
            }
        });
    }
}
