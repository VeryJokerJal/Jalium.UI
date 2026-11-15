using System.Globalization;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Tests.RenderThread;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class TextBoxRenderCacheTests
{
    private static readonly Action<TextBox, DrawingContext, Rect, double> Draw =
        typeof(TextBox).GetMethod("DrawText", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<TextBox, DrawingContext, Rect, double>>();
    private static readonly FieldInfo VerticalOffset = typeof(TextBoxBase).GetField(
        "_verticalOffset", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void Repaint_ReusesVisibleLineValuesAndKeepsUnwrappedWidth()
    {
        var box = Create("alpha\r\nbeta\ngamma");
        var sink = new CaptureSink();
        Draw(box, sink, new Rect(0, 0, 100, 50), 14);
        var first = sink.Texts.ToArray();
        sink.Texts.Clear();
        Draw(box, sink, new Rect(20, 30, 100, 50), 14);
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, sink.Texts.Select(item => item.Text.Text));
        for (int index = 0; index < first.Length; index++)
        {
            Assert.Same(first[index].Text, sink.Texts[index].Text);
            Assert.Equal(double.MaxValue, sink.Texts[index].Text.MaxTextWidth);
            Assert.Equal(first[index].Origin.X + 20, sink.Texts[index].Origin.X);
            Assert.Equal(first[index].Origin.Y + 30, sink.Texts[index].Origin.Y);
        }
    }

    [Theory]
    [InlineData("text")]
    [InlineData("family")]
    [InlineData("size")]
    [InlineData("weight")]
    [InlineData("style")]
    [InlineData("brush")]
    [InlineData("trimming")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("culture")]
    public void ChangedRenderInputs_CreateNewValuesWithoutRewritingOldOnes(string change)
    {
        var box = Create("alpha beta gamma");
        box.TextWrapping = TextWrapping.Wrap;
        var sink = new CaptureSink();
        var rect = new Rect(0, 0, 150, 60);
        double height = 14;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            Draw(box, sink, rect, height);
            var old = Assert.Single(sink.Texts).Text;
            switch (change)
            {
                case "text": box.Text = "changed"; break;
                case "family": box.FontFamily = new FontFamily("Arial"); break;
                case "size": box.FontSize = 18; break;
                case "weight": box.FontWeight = FontWeights.Bold; break;
                case "style": box.FontStyle = FontStyles.Italic; break;
                case "brush": box.Foreground = Brushes.Red; break;
                case "trimming": box.TextTrimming = box.TextTrimming == TextTrimming.None
                    ? TextTrimming.CharacterEllipsis : TextTrimming.None; break;
                case "width": rect = new Rect(0, 0, 120, 60); break;
                case "height": height = 16; break;
                case "culture": CultureInfo.CurrentCulture = new CultureInfo("fr-FR"); break;
            }
            sink.Texts.Clear();
            Draw(box, sink, rect, height);
            var current = Assert.Single(sink.Texts).Text;
            Assert.NotSame(old, current);
            Assert.Equal("alpha beta gamma", old.Text);
            Assert.Equal(12, old.FontSize);
            Assert.Same(Brushes.Black, old.Foreground);
            Assert.Equal(150, old.MaxTextWidth);
            Assert.Equal(14, old.MaxTextHeight);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    [Fact]
    public void ScrollingAndLargeVisibleRanges_KeepStorageBoundedAndTextCurrent()
    {
        var box = Create(string.Join('\n', Enumerable.Range(0, 2_000).Select(i => "Line " + i)));
        var sink = new CaptureSink();
        Draw(box, sink, new Rect(0, 0, 150, 6_000), 10);
        Assert.True(CachedLineCount(box) <= 256);
        VerticalOffset.SetValue(box, 10_000d);
        sink.Texts.Clear();
        Draw(box, sink, new Rect(0, 0, 150, 40), 10);
        Assert.Equal(Enumerable.Range(999, 6).Select(index => "Line " + index),
            sink.Texts.Select(item => item.Text.Text));
        Assert.True(CachedLineCount(box) <= 256);
    }

    [Fact]
    public void DrawingContextMutation_DoesNotChangeTheNextPlainTextPaint()
    {
        var box = Create("alpha");
        var sink = new CaptureSink();
        Draw(box, sink, new Rect(0, 0, 150, 40), 14);
        var first = Assert.Single(sink.Texts).Text;
        first.SetFontWeight(FontWeights.Bold, 0, 1);
        first.MaxLineCount = 1;
        first.Foreground = Brushes.Red;
        sink.Texts.Clear();
        Draw(box, sink, new Rect(0, 0, 150, 40), 14);
        var current = Assert.Single(sink.Texts).Text;
        Assert.NotSame(first, current);
        Assert.Same(Brushes.Black, current.Foreground);
        Assert.Equal(400, current.FontWeight);
        Assert.Equal(int.MaxValue, current.MaxLineCount);
        Assert.True(current.HasSimpleRenderFormatting);
    }

    [Fact]
    public void RecordedText_StaysIndependentAfterTextFontAndMutableBrushChanges()
    {
        var brush = new SolidColorBrush(Colors.Blue);
        var box = Create("old");
        box.Foreground = brush;
        var firstRecorder = new DrawingRecorder();
        firstRecorder.BindWholeFrame();
        Draw(box, firstRecorder, new Rect(0, 0, 150, 40), 14);
        var first = firstRecorder.Commit();
        var old = Assert.Single(first.Commands, command => command.Kind == DrawCommandKind.DrawText);
        box.Text = "new";
        box.FontSize = 20;
        brush.Color = Colors.Red;
        var secondRecorder = new DrawingRecorder();
        secondRecorder.BindWholeFrame();
        Draw(box, secondRecorder, new Rect(0, 0, 150, 40), 24);
        var second = secondRecorder.Commit();
        var current = Assert.Single(second.Commands, command => command.Kind == DrawCommandKind.DrawText);
        Assert.Equal("old", Assert.IsType<FormattedText>(old.A).Text);
        Assert.Equal(12, ((FormattedText)old.A!).FontSize);
        Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(((FormattedText)old.A!).Foreground).Color);
        Assert.Equal("new", Assert.IsType<FormattedText>(current.A).Text);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(((FormattedText)current.A!).Foreground).Color);
    }

    private static TextBox Create(string text) => new()
    {
        Text = text, FontSize = 12, FontFamily = new FontFamily("Consolas"),
        Foreground = Brushes.Black, TextWrapping = TextWrapping.NoWrap,
    };

    private static int CachedLineCount(TextBox box)
    {
        var cache = typeof(TextBox).GetField("_renderLineCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(box)!;
        return ((System.Collections.ICollection)cache.GetType().GetField("_lines", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!).Count;
    }

    private sealed class CaptureSink : RecordingRenderSink
    {
        public readonly List<(FormattedText Text, Point Origin)> Texts = new();
        public override void DrawText(FormattedText formattedText, Point origin) => Texts.Add((formattedText, origin));
    }
}
