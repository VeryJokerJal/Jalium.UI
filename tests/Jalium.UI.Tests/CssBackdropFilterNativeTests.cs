using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssBackdropFilterNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_BlursBackdropBehindBorder() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_BlursBackdropBehindBorder() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_BlursBackdropBehindBorder() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 200, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        Assert.Equal(backend, context.Backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        using var black = context.CreateSolidBrush(0, 0, 0, 1);
        using var white = context.CreateSolidBrush(1, 1, 1, 1);
        using var colored = context.CreateSolidBrush(0.8f, 0.2f, 0.1f, 1);
        using var semiRed = context.CreateSolidBrush(1, 0, 0, 0.5f);
        var border = new Border();
        var panel = new Grid();

        byte[] Render(string style, bool recorded, bool horizontalEdge = false,
            bool colorScene = false, bool usePanel = false,
            bool transparentScene = false, bool semiTransparentScene = false)
        {
            FrameworkElement surface = usePanel ? panel : border;
            Css.SetStyle(surface, style);
            CssBorderRadiusTests.Layout(surface, 120, 60);
            for (var frame = 0; frame < 2; frame++)
            {
                target.SetFullInvalidation();
                var deadline = Environment.TickCount64 + 2000;
                while (!target.TryBeginDraw())
                {
                    Assert.True(Environment.TickCount64 < deadline);
                    Thread.Sleep(1);
                }
                target.Clear(0, 0, 0, transparentScene || semiTransparentScene ? 0 : 1);
                if (semiTransparentScene)
                    target.FillRectangle(0, 0, width, height, semiRed);
                else if (transparentScene)
                {
                    // An untinted CSS backdrop must not fabricate opacity.
                }
                else if (colorScene)
                    target.FillRectangle(0, 0, width, height, colored);
                else
                {
                    target.FillRectangle(0, 0, width, height, black);
                    if (horizontalEdge)
                        target.FillRectangle(0, 30, width, height - 30, white);
                    else
                        target.FillRectangle(width / 2f, 0, width / 2f, height, white);
                }

                if (recorded)
                {
                    var recorder = new DrawingRecorder();
                    recorder.BindWholeFrame();
                    ((IOffsetDrawingContext)recorder).Offset = new Point(40, 20);
                    surface.Render(recorder);
                    var commands = recorder.Commit();
                    Assert.True(commands.FullyRecordable);
                    DrawingReplayer.Replay(commands, drawing);
                }
                else
                {
                    drawing.Offset = new Point(40, 20);
                    try { surface.Render(drawing); }
                    finally { drawing.Offset = default; }
                }

                if (frame == 1) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            }

            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static byte Light(byte[] pixels, int x, int y)
            => pixels[(y * width + x) * 4];

        static byte Alpha(byte[] pixels)
            => pixels[(50 * width + 80) * 4 + 3];

        static (int R, int G, int B) Sample(byte[] pixels)
        {
            var index = (50 * width + 80) * 4;
            return (pixels[index + 2], pixels[index + 1], pixels[index]);
        }

        static void AssertColorNear((int R, int G, int B) actual,
            (double R, double G, double B) expected, string label)
        {
            Assert.True(Math.Abs(actual.R - Math.Clamp(expected.R, 0, 255)) <= 6 &&
                        Math.Abs(actual.G - Math.Clamp(expected.G, 0, 255)) <= 6 &&
                        Math.Abs(actual.B - Math.Clamp(expected.B, 0, 255)) <= 6,
                $"{label}: got {actual}, expected {expected}");
        }

        foreach (var recorded in new[] { false, true })
        {
            var plain = Render("backdrop-filter: none", recorded);
            var blurred = Render("backdrop-filter: blur(8px)", recorded);

            Assert.True(Light(plain, 96, 50) < 8 && Light(plain, 104, 50) > 247,
                $"{backend} recorded={recorded}: reference edge is not sharp");
            Assert.True(Light(blurred, 96, 50) > Light(plain, 96, 50) + 20,
                $"{backend} recorded={recorded}: CSS blur did not lighten the dark side");
            Assert.True(Light(blurred, 104, 50) + 20 < Light(plain, 104, 50),
                $"{backend} recorded={recorded}: CSS blur did not darken the light side");
            Assert.Equal(Light(plain, 20, 50), Light(blurred, 20, 50));
            Assert.Equal(Light(plain, 180, 50), Light(blurred, 180, 50));

            const string ellipse = "border-radius: 50px / 20px;";
            var roundedPlain = Render(ellipse + "backdrop-filter: none", recorded,
                horizontalEdge: true);
            var roundedBlurred = Render(ellipse + "backdrop-filter: blur(8px)", recorded,
                horizontalEdge: true);
            Assert.True(Light(roundedBlurred, 100, 25) > Light(roundedPlain, 100, 25) + 20,
                $"{backend} recorded={recorded}: backdrop did not blur inside the ellipse");
            Assert.Equal(Light(roundedPlain, 42, 25), Light(roundedBlurred, 42, 25));

            var panelPlain = Render(ellipse + "backdrop-filter: none", recorded,
                horizontalEdge: true, usePanel: true);
            var panelBlurred = Render(ellipse + "backdrop-filter: blur(8px)", recorded,
                horizontalEdge: true, usePanel: true);
            Assert.True(Light(panelBlurred, 100, 25) > Light(panelPlain, 100, 25) + 20,
                $"{backend} recorded={recorded}: panel backdrop did not blur inside the ellipse");
            Assert.Equal(Light(panelPlain, 42, 25), Light(panelBlurred, 42, 25));

            var source = Sample(Render("backdrop-filter: none", recorded, colorScene: true));
            var label = $"{backend} recorded={recorded}";
            AssertColorNear(Sample(Render("backdrop-filter: invert(1)", recorded,
                    colorScene: true, usePanel: true)),
                (255 - source.R, 255 - source.G, 255 - source.B), label + " panel invert");
            AssertColorNear(Sample(Render("backdrop-filter: brightness(50%)", recorded, colorScene: true)),
                (source.R * 0.5, source.G * 0.5, source.B * 0.5), label + " brightness");
            AssertColorNear(Sample(Render("backdrop-filter: contrast(0)", recorded, colorScene: true)),
                (127.5, 127.5, 127.5), label + " contrast");
            AssertColorNear(Sample(Render("backdrop-filter: invert()", recorded, colorScene: true)),
                (255 - source.R, 255 - source.G, 255 - source.B), label + " invert");

            var grey = 0.2126 * source.R + 0.7152 * source.G + 0.0722 * source.B;
            AssertColorNear(Sample(Render("backdrop-filter: grayscale(1)", recorded, colorScene: true)),
                (grey, grey, grey), label + " grayscale");
            var desaturated = 0.213 * source.R + 0.715 * source.G + 0.072 * source.B;
            AssertColorNear(Sample(Render("backdrop-filter: saturate(0)", recorded, colorScene: true)),
                (desaturated, desaturated, desaturated), label + " saturate");
            AssertColorNear(Sample(Render("backdrop-filter: sepia(1)", recorded, colorScene: true)),
                (0.393 * source.R + 0.769 * source.G + 0.189 * source.B,
                 0.349 * source.R + 0.686 * source.G + 0.168 * source.B,
                 0.272 * source.R + 0.534 * source.G + 0.131 * source.B), label + " sepia");
            AssertColorNear(Sample(Render("backdrop-filter: hue-rotate(180deg)", recorded, colorScene: true)),
                ((-0.574 * source.R + 1.430 * source.G + 0.144 * source.B),
                 (0.426 * source.R + 0.430 * source.G + 0.144 * source.B),
                 (0.426 * source.R + 1.430 * source.G - 0.856 * source.B)), label + " hue-rotate");

            var transparent = Render("backdrop-filter: opacity(50%)", recorded,
                transparentScene: true);
            Assert.InRange(Alpha(transparent), 0, 2);
            var semiPlain = Alpha(Render("backdrop-filter: none", recorded,
                semiTransparentScene: true));
            var semiFiltered = Alpha(Render("backdrop-filter: opacity(50%)", recorded,
                semiTransparentScene: true));
            Assert.InRange(semiPlain, 100, 155);
            var expectedAlpha = semiPlain + semiPlain * 0.5 * (1 - semiPlain / 255.0);
            Assert.InRange((double)semiFiltered, expectedAlpha - 10, expectedAlpha + 10);
        }
    }
}
