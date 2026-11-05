using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssGeneratedContentRenderTests
{
    [Theory]
    [InlineData(375, 60, 16)]
    [InlineData(720, 16, 320)]
    public void GeneratedBoxesWrapAndPaintAtTheirCssAllocatedPositions(int width, double afterY, double afterX)
    {
        var root = new StackPanel { Background = Brushes.White };
        Css.SetClass(root, "host");
        var real = new Border { Width = 220, Height = 32, Background = Brushes.Blue };
        root.Children.Add(real);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
            .host { display: flex; flex-wrap: wrap; gap: 12px; padding: 16px; align-items: start; align-content: start; }
            .host::before { content: ""; width: 60px; height: 32px; background-color: red; }
            .host::after { content: ""; width: 60px; height: 32px; background-color: green; }
            """));
        Layout(root, width, 160);
        var slots = CssGeneratedContent.Get(root)!;
        Assert.Equal(16, slots.Before!.VisualBounds.X, 6);
        Assert.Equal(88, real.VisualBounds.X, 6);
        Assert.Equal(afterX, slots.After!.VisualBounds.X, 6);
        Assert.Equal(afterY, slots.After.VisualBounds.Y, 6);
        var bitmap = Render(root, width, 160);
        Assert.Equal(Colors.Red, Pixel(bitmap, 20, 20));
        Assert.Equal(Colors.Blue, Pixel(bitmap, 92, 20));
        Assert.Equal(Colors.Green, Pixel(bitmap, (int)afterX + 4, (int)afterY + 4));
        Save(bitmap, $"generated-{width}.png");
    }

    [Fact]
    public void FixedPseudoElementsUseSourceOrderAndRetainTheirEventTarget()
    {
        var root = new StackPanel { Background = Brushes.White };
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
            StackPanel::before { content: ""; position: fixed; left: 10px; top: 10px; width: 40px; height: 30px; background-color: red; }
            StackPanel::after { content: ""; position: fixed; left: 10px; top: 10px; width: 40px; height: 30px; background-color: green; }
            """));
        Layout(root, 200, 120);
        Assert.Equal(Colors.Green, Pixel(Render(root, 200, 120), 20, 20));
        Assert.Same(root, root.HitTest(new Point(20, 20))?.VisualHit);
        Assert.Empty(root.Children);
    }

    [Fact]
    public void RemovingGeneratedContentClearsItsPaintWithoutLeavingASpacingSlot()
    {
        var root = new StackPanel { Background = Brushes.White, Spacing = 8 };
        var real = new Border { Height = 20, Background = Brushes.Blue }; root.Children.Add(real);
        var sheet = CssStyleSheet.Parse("StackPanel::before { content: ''; height: 12px; background-color: red; }");
        Css.GetStyleSheets(root).Add(sheet); Layout(root, 200, 120);
        Assert.Equal(Colors.Red, Pixel(Render(root, 200, 120), 10, 5));
        Css.GetStyleSheets(root).Remove(sheet); Layout(root, 200, 120);
        var result = Render(root, 200, 120);
        Assert.Equal(0, real.VisualBounds.Y, 6);
        Assert.Equal(Colors.Blue, Pixel(result, 10, 5));
        Assert.Equal(Colors.White, Pixel(result, 10, 30));
    }

    private static void Layout(FrameworkElement root, int width, int height)
    {
        for (var i = 0; i < 4; i++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height));
        }
    }
    private static RenderTargetBitmap Render(FrameworkElement root, int width, int height)
    {
        var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormat.Bgra32); result.Render(root); return result;
    }
    private static Color Pixel(RenderTargetBitmap bitmap, int x, int y)
    {
        var bytes = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0);
        return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }
    private static void Save(RenderTargetBitmap bitmap, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_GENERATED_EVIDENCE_DIR") is not { Length: > 0 } path) return;
        Directory.CreateDirectory(path);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(path, name)); encoder.Save(output);
    }
}
