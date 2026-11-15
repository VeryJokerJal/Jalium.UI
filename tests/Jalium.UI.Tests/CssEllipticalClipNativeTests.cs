using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssEllipticalClipNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_ClipsPrimitivesAndRestoresNestedState() => Verify(RenderBackend.Software, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_ClipsPrimitivesAndRestoresNestedState() => Verify(RenderBackend.D3D12, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_VelloPreservesClipState() => Verify(RenderBackend.D3D12, RenderingEngine.Vello);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_ClipsPrimitivesAndRestoresNestedState() => Verify(RenderBackend.Vulkan, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_VelloPreservesClipState() => Verify(RenderBackend.Vulkan, RenderingEngine.Vello);

    [Fact]
    public void DescriptorLayout_MatchesTheVersionedNativeAbi()
    {
        Assert.Equal(56, Marshal.SizeOf<NativeEllipticalClip>());
        Assert.Equal(40, Marshal.OffsetOf<NativeEllipticalClip>(nameof(NativeEllipticalClip.TopLeftY)).ToInt32());
    }

    private static void Verify(RenderBackend backend, RenderingEngine engine)
    {
        const int width = 128, height = 96;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend, GpuPreference.Auto, engine);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var brush = context.CreateSolidBrush(0, 1, 0, 1);
        using var bitmap = context.CreateBitmapFromPixels([0, 255, 0, 255], 1, 1);
        using var font = context.CreateTextFormat("Arial", 32);
        Assert.Equal(backend, context.Backend);
        if (backend != RenderBackend.Software) Assert.Equal(engine, target.RenderingEngine);
        var outer = Descriptor(new Rect(8, 12, 100, 60));
        var inner = Descriptor(new Rect(24, 4, 80, 72));
        var path = Geometry.Parse("M0,0 L128,0 L128,96 L0,96 Z M52,36 L76,36 L76,60 L52,60 Z");

        void Push(int scene)
        {
            if (scene == 4)
            {
                target.PushTransform([0, .5f, -1, 0, 108, 0]);
                Assert.True(target.TryPushEllipticalRectClip(outer));
                target.PopTransform();
                return;
            }
            if (scene == 6)
            {
                var empty = Descriptor(new Rect(8, 12, 0, 60));
                Assert.True(target.TryPushEllipticalRectClip(empty));
                return;
            }
            if (scene == 7)
            {
                var open = Descriptor(new Rect(20, 20, 60, 40));
                open.Edges = 3; open.TopLeftX = 25; open.TopLeftY = 12;
                Assert.True(target.TryPushEllipticalRectClip(open));
                return;
            }
            if (scene == 8)
            {
                var thick = new NativeEllipticalClip(new Rect(30, 30, 40, 40),
                    new(default, new Size(70, 70), default, default), ClipEdges.All);
                Assert.True(target.TryPushEllipticalRectClip(thick));
                return;
            }
            Assert.True(target.TryPushEllipticalRectClip(outer), "This test requires the new native clip interface.");
            if (scene == 5) for (var i = 1; i < 32; i++) Assert.True(target.TryPushEllipticalRectClip(outer));
            if (scene == 2)
            {
                Assert.True(target.TryPushEllipticalRectClip(inner));
                target.PushClip(0, 0, width, height); // a rectangular child must retain both curved ancestors
            }
            if (scene == 3)
            {
                Assert.True(target.TryPushEllipticalRectClip(inner));
                target.PopClip();
            }
        }

        byte[] Paint(int primitive, int scene)
        {
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
            target.Clear(0, 0, 0, 1);
            if (scene > 0) Push(scene);
            switch (primitive)
            {
                case 0: target.FillRectangle(0, 0, width, height, brush); break;
                case 1: target.DrawBitmap(bitmap, 0, 0, width, height); break;
                case 2: target.DrawText("MMMMMMMM\nMMMMMMMM\nMMMMMMMM", font, 0, 0, width, height, brush); break;
                default:
                    using (var drawing = new RenderTargetDrawingContext(target, context)) drawing.DrawGeometry(Brushes.Lime, null, path);
                    break;
            }
            if (scene > 0)
            {
                if (scene == 2) { target.PopClip(); target.PopClip(); }
                if (scene == 5) for (var i = 1; i < 32; i++) target.PopClip();
                target.PopClip();
            }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out var w, out var h));
            Assert.Equal(width, w); Assert.Equal(height, h);
            return pixels;
        }

        static double Distance(double x, double y, double cx, double cy, double rx, double ry)
            => (Math.Sqrt(Math.Pow((x - cx) / rx, 2) + Math.Pow((y - cy) / ry, 2)) - 1) * Math.Min(rx, ry);

        for (var primitive = 0; primitive < 4; primitive++)
        {
            var reference = Paint(primitive, 0);
            byte[]? singleClip = null;
            for (var scene = 1; scene <= (primitive == 0 ? 8 : 6); scene++)
            {
                var actual = Paint(primitive, scene);
                if (scene == 1) singleClip = actual;
                if (scene == 5) Assert.Equal(singleClip, actual); // identical nested masks must not darken antialiasing
                var painted = 0; var rejected = 0;
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                {
                    var distance = Distance(x + .5, y + .5, 58, 42, 50, 30);
                    if (scene == 2) distance = Math.Max(distance, Distance(x + .5, y + .5, 64, 40, 40, 36));
                    if (scene == 4) distance = Distance(x + .5, y + .5, 66, 29, 30, 25);
                    if (scene == 6) distance = 100;
                    if (scene == 7)
                    {
                        distance = Math.Max(20 - (x + .5), 20 - (y + .5));
                        if (x + .5 < 45 && y + .5 < 32) distance = Math.Max(distance, Distance(x + .5, y + .5, 45, 32, 25, 12));
                    }
                    if (scene == 8) distance = Math.Max(Distance(x + .5, y + .5, 0, 100, 70, 70),
                        Math.Max(Math.Max(30 - (x + .5), x + .5 - 70), Math.Max(30 - (y + .5), y + .5 - 70)));
                    // Different backends use different AA kernels. Check every
                    // pixel outside the 2px contour band with exact channel values.
                    if (Math.Abs(distance) <= 2) continue;
                    var at = (y * width + x) * 4;
                    for (var channel = 0; channel < 3; channel++)
                        Assert.True(actual[at + channel] == (distance < 0 ? reference[at + channel] : 0),
                            $"{backend}/{engine}, primitive {primitive}, scene {scene}, ({x},{y}), channel {channel}");
                    if (distance < 0 && actual[at + 1] > 0) painted++;
                    if (distance > 0 && reference[at + 1] > 0) rejected++;
                }
                if (scene == 6) Assert.Equal(0, painted);
                else Assert.True(painted > 100 && rejected > 100, $"The primitive must exercise both sides of the contour: {painted}/{rejected}.");
            }
            Assert.Equal(reference, Paint(primitive, 0));
        }

        // Exercise the actual CSS overflow -> Border -> DrawingContext -> C ABI path.
        var child = new Border { Background = Brushes.Lime };
        var border = new Border { Child = child };
        Css.SetStyle(border, "border-radius:50%; overflow:hidden");
        CssBorderRadiusTests.Layout(border, 120, 60);
        target.SetFullInvalidation(); Assert.True(target.TryBeginDraw()); target.Clear(0, 0, 0, 1);
        using (var drawing = new RenderTargetDrawingContext(target, context)) border.Render(drawing);
        Assert.Equal(JaliumResult.Ok, target.RequestReadback()); Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        var cssPixels = new byte[width * height * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(cssPixels, width * 4u, out _, out _));
        Assert.Equal((byte)0, cssPixels[(1 * width + 1) * 4 + 1]);
        Assert.Equal((byte)255, cssPixels[(30 * width + 60) * 4 + 1]);

        var owner = new Control();
        Border? chrome = null;
        var template = new ControlTemplate(typeof(Control));
        template.SetVisualTree(() =>
        {
            chrome = new Border { Background = Brushes.Red };
            Jalium.UI.Data.TemplateBindingExtensions.SetTemplateBinding(chrome, Border.CornerRadiusProperty, Control.CornerRadiusProperty);
            return chrome;
        });
        owner.Template = template;
        Css.SetStyle(owner, "border-radius:50%; overflow:hidden");
        CssBorderRadiusTests.Layout(owner, 120, 60);
        for (var native = 0; native < 2; native++)
        {
            if (native == 1) owner.CornerRadius = new CornerRadius(0);
            target.SetFullInvalidation(); Assert.True(target.TryBeginDraw()); target.Clear(0, 0, 0, 1);
            using (var drawing = new RenderTargetDrawingContext(target, context)) owner.Render(drawing);
            Assert.Equal(JaliumResult.Ok, target.RequestReadback()); Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(cssPixels, width * 4u, out _, out _));
            Assert.Equal((byte)(native == 0 ? 0 : 255), cssPixels[(1 * width + 1) * 4 + 2]);
            Assert.Equal((byte)255, cssPixels[(30 * width + 60) * 4 + 2]);
        }
        Assert.NotNull(Jalium.UI.Data.BindingOperations.GetBindingExpressionBase(chrome!, Border.CornerRadiusProperty));
    }

    private static NativeEllipticalClip Descriptor(Rect rect)
    {
        var radius = new Size(rect.Width / 2, rect.Height / 2);
        return new(rect, new(radius, radius, radius, radius), ClipEdges.All);
    }
}
