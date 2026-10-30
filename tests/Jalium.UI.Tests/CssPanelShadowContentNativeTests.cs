using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssPanelShadowContentNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_ChildrenPaintAboveInset() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_ChildrenPaintAboveInset() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_ChildrenPaintAboveInset() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 160, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        foreach (var panel in new Panel[]
        {
            new Canvas(), new DockPanel(), new FlexPanel(), new Grid(),
            new StackPanel(), new WrapPanel(), new Jalium.UI.Controls.Primitives.UniformGrid(),
        })
        {
            panel.Background = Brushes.Blue;
            var child = new Border { Width = 80, Height = 40, Background = Brushes.Cyan };
            panel.Children.Add(child);
            Css.SetStyle(panel, "box-shadow:inset 0 0 8px red, 12px 0 0 red");

            byte[] Render()
            {
                CssBorderRadiusTests.Layout(panel, 80, 40);
                target.SetFullInvalidation();
                var deadline = Environment.TickCount64 + 2000;
                while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
                target.Clear(0, 0, 0, 1);
                drawing.Offset = new Point(40, 30);
                try { panel.Render(drawing); }
                finally { drawing.Offset = default; }
                Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
                var pixels = new byte[width * height * 4];
                Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
                return pixels;
            }

            var covered = Render();
            Assert.True(Channel(covered, 80, 32, 2) < 10 &&
                        Channel(covered, 80, 32, 1) > 200,
                $"{backend}/{panel.GetType().Name}: inset shadow covered child " +
                $"(red={Channel(covered, 80, 32, 2)})");
            Assert.True(Channel(covered, 125, 50, 2) > 150,
                $"{backend}/{panel.GetType().Name}: outer shadow disappeared");

            panel.Children.Clear();
            var uncovered = Render();
            Assert.True(Channel(uncovered, 80, 32, 2) > 25,
                $"{backend}/{panel.GetType().Name}: inset shadow required child content");
        }

        static byte Channel(byte[] pixels, int x, int y, int channel)
            => pixels[(y * width + x) * 4 + channel];
    }
}
