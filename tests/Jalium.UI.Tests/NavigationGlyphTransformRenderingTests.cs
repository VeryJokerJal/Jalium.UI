using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Rendering;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class NavigationGlyphTransformRenderingTests
{
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Impeller_ScaledNavigationGlyphsStayInsideTheirSlots() => Verify(RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Vello_ScaledNavigationGlyphsStayInsideTheirSlots() => Verify(RenderingEngine.Vello);

    private static void Verify(RenderingEngine engine)
    {
        foreach (float dpi in new[] { 96f, 120f, 144f, 192f }) Verify(engine, dpi);
    }

    private static void Verify(RenderingEngine engine, float dpi)
    {
        const int width = 512, height = 320;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(RenderBackend.D3D12, GpuPreference.Auto, engine);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        target.SetDpi(dpi, dpi);
        target.SetPathMsaaSampleCount(4);
        var root = new Canvas();
        var icons = new List<Grid>();
        var scale = new ScaleTransform(1.2, 1.2, 11, 11);
        string[] paths =
        [
            "M11 4.5 L17.5 8 L11 11.5 L4.5 8 Z M4.5 11.25 L11 14.75 L17.5 11.25 M4.5 14.5 L11 18 L17.5 14.5",
            "M4.5 10 A5.5 5.5 0 1 1 15.5 10 A5.5 5.5 0 1 1 4.5 10 Z M13.9 13.9 L17.5 17.5 M7.75 10 L9.25 11.5 L12.25 8.5",
            "M4.5 11 A6.5 6.5 0 1 1 17.5 11 A6.5 6.5 0 1 1 4.5 11 Z M4.5 11 H17.5 M11 4.5 C9.1 6.5 8.3 8.7 8.3 11 C8.3 13.3 9.1 15.5 11 17.5 C12.9 15.5 13.7 13.3 13.7 11 C13.7 8.7 12.9 6.5 11 4.5 Z"
        ];
        var template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.jalium.ui/2024" TargetType="NavigationViewItem">
                <Border Height="26" Background="{TemplateBinding Background}">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="14"/>
                            <ColumnDefinition Width="32"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <ContentPresenter Grid.Column="1" ContentSource="Icon" HorizontalAlignment="Left" VerticalAlignment="Center"/>
                        <ContentPresenter Grid.Column="2" VerticalAlignment="Center"/>
                    </Grid>
                </Border>
            </ControlTemplate>
            """);
        for (int i = 0; i < paths.Length; i++)
        {
            var geometry = Geometry.Parse(paths[i]);
            geometry.Freeze();
            var icon = new Grid { Width = 22, Height = 22, RenderTransform = scale };
            icon.Children.Add(new Shapes.Path
            {
                Data = geometry, Stretch = Stretch.None, Stroke = Brushes.Lime, StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            });
            var item = new NavigationViewItem
            {
                Width = 180, Height = 26, Content = "Category " + i, Icon = icon, Template = template,
                Foreground = Brushes.White, Background = Brushes.Black,
            };
            Canvas.SetLeft(item, 22.3);
            Canvas.SetTop(item, 54.7 + i * 28);
            root.Children.Add(item);
            icons.Add(icon);
        }
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        nint layer = 0;
        var captureBounds = new Rect(22.3, 54.7, 180, 82);
        try
        {
            for (int frame = 0; frame < 7; frame++)
            {
                target.SetFullInvalidation();
                long deadline = Environment.TickCount64 + 3000;
                while (!target.TryBeginDraw())
                { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
                target.Clear(0, 0, 0);
                if (frame == 0) root.Render(drawing);
                else if (frame == 1)
                {
                    var recorder = new DrawingRecorder(); recorder.BindWholeFrame();
                    root.Render(recorder);
                    var commands = recorder.Commit();
                    Assert.True(commands.IsFullyRecordable);
                    DrawingReplayer.Replay(commands, drawing);
                    DrawingLifetime.Release(commands);
                }
                else if (frame < 5)
                {
                    if (frame == 2)
                    {
                        layer = drawing.BeginLayerCapture(0, captureBounds);
                        Assert.NotEqual(0, layer);
                        root.Render(drawing);
                        drawing.EndLayerCapture(layer);
                    }
                    drawing.CompositeLayer(layer, captureBounds, 1, null, 0, 0);
                }
                else
                {
                    drawing.BeginTransitionCapture(0, captureBounds);
                    if (frame == 6)
                        drawing.BeginEffectCapture(24, 56, 176, 80);
                    root.Render(drawing);
                    if (frame == 6)
                        drawing.EndEffectCapture();
                    drawing.EndTransitionCapture(0);
                    drawing.DrawCapturedTransition(0, captureBounds, 1);
                }
                Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
                var pixels = new byte[width * height * 4];
                Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
                var expected = icons.Select(icon => icon.TransformToVisual(root)!
                    .TransformBounds(new Rect(0, 0, 22, 22)))
                    .Select(rect => new Rect(rect.X * dpi / 96, rect.Y * dpi / 96, rect.Width * dpi / 96, rect.Height * dpi / 96)).ToArray();
                var counts = new int[expected.Length];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int p = (y * width + x) * 4;
                    if (pixels[p + 1] < 60 || pixels[p + 1] < pixels[p] + 30 || pixels[p + 1] < pixels[p + 2] + 30) continue;
                    int slot = Array.FindIndex(expected, rect => rect.Contains(new Point(x + .5, y + .5)));
                    Assert.True(slot >= 0, $"{engine}, dpi={dpi}, frame={frame}: green glyph pixel outside icon slots at ({x},{y}); expected={string.Join("; ", expected)}");
                    counts[slot]++;
                }
                Assert.All(counts, count => Assert.True(count > 35, $"{engine}, frame={frame}: missing glyph ({string.Join(", ", counts)})"));
                if (frame >= 3) Assert.True(drawing.CompositedLayerCountForTests > 0);
            }
        }
        finally
        {
            if (layer != 0) target.DestroyRetainedLayer(layer);
            Visual.ReleaseRetainedLayersRecursive(root); drawing.DrainPendingRetainedLayers();
        }
    }
}
