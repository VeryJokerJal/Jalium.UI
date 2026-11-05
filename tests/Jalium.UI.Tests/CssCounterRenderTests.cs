using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssCounterRenderTests
{
    [Theory]
    [InlineData(375)]
    [InlineData(720)]
    public void CounterAndQuoteTextWrapWithTheSameSourceOrderAtBothWidths(int width)
    {
        var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
        var rows = new List<StackPanel>();
        foreach (var name in new[] { "面板 A", "面板 B", "面板 C" })
        {
            var row = new StackPanel { Tag = name }; Css.SetClass(row, "card");
            row.Children.Add(new TextBlock { Text = "生成内容跟随文档顺序。", TextWrapping = TextWrapping.Wrap });
            root.Children.Add(row); rows.Add(row);
        }
        var sheet = CssStyleSheet.Parse("""
            .root {
                display: flex; flex-wrap: wrap; gap: 16px; padding: 20px;
                align-content: start; align-items: start;
                counter-reset: reversed(step) total; quotes: '“' '”' '‘' '’';
                font-family: 'PingFang SC', sans-serif; font-size: 16px; color: #172f26;
            }
            .root::before { content: 'CSS 计数器'; flex: 0 0 100%; height: 36px; font-size: 24px; color: #146342; }
            .card { display: block; width: 280px; padding: 12px; background: #e9f4ef; border: 1px solid #abcdbd; counter-increment: step -1 total; }
            .card::before { content: '#' counter(step, decimal-leading-zero) ' ' open-quote attr(Tag) close-quote; height: 30px; font-size: 18px; }
            .root::after { content: '合计：' counter(total) ' 项'; flex: 0 0 100%; height: 28px; }
            """);
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(root).Add(sheet);
        Layout(root, width);
        Assert.Equal(new[] { "#03 “面板 A”", "#02 “面板 B”", "#01 “面板 C”" }, rows.Select(row => CssGeneratedContent.Get(row)!.Before!.Text));
        Assert.Equal("合计：3 项", CssGeneratedContent.Get(root)!.After!.Text);
        if (width == 375) Assert.True(rows[1].VisualBounds.Y > rows[0].VisualBounds.Y);
        else Assert.Equal(rows[0].VisualBounds.Y, rows[1].VisualBounds.Y, 6);
        foreach (var row in rows) Assert.InRange(row.VisualBounds.Right, 0, width);
        Save(root, width, $"counters-{width}.png");
        rows[1].Visibility = Visibility.Collapsed; Layout(root, width);
        Assert.Equal("#02 “面板 A”", CssGeneratedContent.Get(rows[0])!.Before!.Text);
        Assert.Equal("#01 “面板 C”", CssGeneratedContent.Get(rows[2])!.Before!.Text);
        Assert.Equal("合计：2 项", CssGeneratedContent.Get(root)!.After!.Text);
        Save(root, width, $"counters-{width}-hidden.png");
    }

    private static void Layout(FrameworkElement root, int width)
    {
        for (var i = 0; i < 5; i++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new Size(width, 420)); root.Arrange(new Rect(0, 0, width, 420));
        }
    }
    private static void Save(FrameworkElement root, int width, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_COUNTER_EVIDENCE_DIR") is not { Length: > 0 } path) return;
        Directory.CreateDirectory(path);
        var bitmap = new RenderTargetBitmap(width, 420, 96, 96, PixelFormat.Bgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(path, name)); encoder.Save(output);
    }
}
