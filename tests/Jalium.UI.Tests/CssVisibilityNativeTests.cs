using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssVisibilityNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsVisibleDescendants() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsVisibleDescendants() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsVisibleDescendants() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 100, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var child = new Border { Width = 20, Height = 20, Background = Brushes.Green,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var parent = new Border { Width = 40, Height = 40, Background = Brushes.Red, Child = child };
        Css.SetStyle(child, "visibility:visible");

        byte[] Paint(string style, bool recorded)
        {
            Css.SetStyle(parent, style);
            CssBorderRadiusTests.Layout(parent, 40, 40);
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
            target.Clear(0, 0, 1, 1);
            if (recorded)
            {
                var recorder = new DrawingRecorder();
                recorder.BindWholeFrame();
                ((IOffsetDrawingContext)recorder).Offset = new Point(20, 20);
                parent.Render(recorder);
                var commands = recorder.Commit();
                Assert.True(commands.FullyRecordable);
                DrawingReplayer.Replay(commands, drawing);
            }
            else
            {
                drawing.Offset = new Point(20, 20);
                try { parent.Render(drawing); }
                finally { drawing.Offset = default; }
            }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static byte Channel(byte[] pixels, int x, int y, int channel)
            => pixels[(y * width + x) * 4 + channel];

        foreach (var recorded in new[] { false, true })
        {
            var hidden = Paint("visibility:hidden", recorded);
            Assert.True(Channel(hidden, 25, 25, 1) > 110,
                $"{backend} recorded={recorded}: visible descendant was hidden");
            Assert.True(Channel(hidden, 55, 55, 0) > 245,
                $"{backend} recorded={recorded}: hidden parent painted");

            var shadow = Paint("visibility:hidden;box-shadow:12px 0 0 red", recorded);
            Assert.True(Channel(shadow, 65, 40, 0) > 245,
                $"{backend} recorded={recorded}: hidden parent shadow painted");

            var filtered = Paint("visibility:hidden;filter:blur(2px)", recorded);
            Assert.True(Channel(filtered, 30, 30, 1) > 90,
                $"{backend} recorded={recorded}: visible descendant lost its filter output");
            Assert.True(Channel(filtered, 55, 55, 0) > 245,
                $"{backend} recorded={recorded}: hidden parent appeared under filter");

            var none = Paint("display:none;visibility:hidden", recorded);
            Assert.True(Channel(none, 25, 25, 0) > 245,
                $"{backend} recorded={recorded}: display:none allowed a descendant");

            var visible = Paint("visibility:visible", recorded);
            Assert.True(Channel(visible, 55, 55, 2) > 245,
                $"{backend} recorded={recorded}: visible parent did not paint");
        }
    }
}
