using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSRecordedTextTests : MacOSGeometryTestBase
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Recording_GlyphFilterCannotAliasNormalTextOrMutableRanges(bool filteredFirst, bool wholeFrame)
    {
        DrawingObjectPool.Clear();
        var filtered = Text(null);
        filtered.GlyphVisibility = 2;
        filtered.GlyphRanges = new uint[] { 0, 2 };
        var recorder = new DrawingRecorder();
        if (wholeFrame) recorder.BindWholeFrame(); else recorder.Bind(new object());
        recorder.DrawText(filteredFirst ? filtered : Text(null), new Point(10, 10));
        recorder.DrawText(filteredFirst ? Text(null) : filtered, new Point(10, 40));
        var drawing = recorder.Commit();
        filtered.GlyphRanges[1] = 1;
        filtered.GlyphVisibility = 0;
        var captured = (FormattedText)drawing.Commands[filteredFirst ? 0 : 1].A!;
        var normal = (FormattedText)drawing.Commands[filteredFirst ? 1 : 0].A!;
        Assert.Equal(2, captured.GlyphVisibility);
        Assert.Equal(new uint[] { 0, 2 }, captured.GlyphRanges);
        Assert.Equal(0, normal.GlyphVisibility);
        Assert.Null(normal.GlyphRanges);
        Assert.Equal(0, DrawingObjectPool.CanonicalizeFormattedText(Text(null)).GlyphVisibility);
        DrawingObjectPool.Clear();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recording_EqualTextKeepsEachPlatformLayout(bool wholeFrame)
    {
        DrawingObjectPool.Clear();
        var firstLayout = new object();
        var secondLayout = new object();
        var recorder = new DrawingRecorder();
        if (wholeFrame) recorder.BindWholeFrame();
        else recorder.Bind(new object());
        recorder.DrawText(Text(firstLayout), new Point(10, 10));
        recorder.DrawText(Text(secondLayout), new Point(10, 40));
        var drawing = recorder.Commit();

        Assert.Same(firstLayout, ((FormattedText)drawing.Commands[0].A!).PlatformTextLine);
        Assert.Same(secondLayout, ((FormattedText)drawing.Commands[1].A!).PlatformTextLine);
        DrawingObjectPool.Clear();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recording_PlainAndShapedTextDoNotExchangeLayouts(bool shapedFirst)
    {
        DrawingObjectPool.Clear();
        var layout = new object();
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        recorder.DrawText(Text(shapedFirst ? layout : null), new Point(10, 10));
        recorder.DrawText(Text(shapedFirst ? null : layout), new Point(10, 40));
        var drawing = recorder.Commit();

        Assert.Same(shapedFirst ? layout : null, ((FormattedText)drawing.Commands[0].A!).PlatformTextLine);
        Assert.Same(shapedFirst ? null : layout, ((FormattedText)drawing.Commands[1].A!).PlatformTextLine);
        DrawingObjectPool.Clear();
    }

    [Fact]
    public void Recording_PlainTextStillSharesAnImmutableValueSnapshot()
    {
        DrawingObjectPool.Clear();
        var source = Text(null);
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        recorder.DrawText(source, new Point(10, 10));
        recorder.DrawText(Text(null), new Point(10, 40));
        var drawing = recorder.Commit();
        var snapshot = (FormattedText)drawing.Commands[0].A!;
        Assert.Same(snapshot, drawing.Commands[1].A);
        Assert.NotSame(source, snapshot);
        source.MaxTextWidth = 25;
        source.Foreground = Brushes.Red;
        Assert.Equal(300, snapshot.MaxTextWidth);
        Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(snapshot.Foreground).Color);
        Assert.Null(snapshot.PlatformTextLine);
        DrawingObjectPool.Clear();
    }

    private static FormattedText Text(object? layout) => new("Window abc אבג 中文", "Helvetica", 20)
    {
        PlatformTextLine = layout,
        Foreground = new SolidColorBrush(Colors.Black),
        MaxTextWidth = 300,
        MaxTextHeight = 30,
    };
}
