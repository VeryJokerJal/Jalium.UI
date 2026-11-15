using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests.RenderThread;

[Collection("Application")]
public sealed class DrawingObjectPoolValueTests : IDisposable
{
    public DrawingObjectPoolValueTests() => DrawingObjectPool.Clear();
    public void Dispose() => DrawingObjectPool.Clear();

    [Fact]
    public void SolidBrushPreservesOpacityAndSnapshotsItsValue()
    {
        var source = new SolidColorBrush(Colors.Red) { Opacity = 0.25 };
        var pooled = Assert.IsType<SolidColorBrush>(DrawingObjectPool.CanonicalizeBrush(source));
        Assert.NotSame(source, pooled);
        Assert.Equal(0.25, pooled.Opacity);
        Assert.True(pooled.IsFrozen);
        source.Color = Colors.Blue;
        source.Opacity = 0.75;
        Assert.Equal(Colors.Red, pooled.Color);
        Assert.Equal(0.25, pooled.Opacity);
    }

    [Fact]
    public void OpacityParticipatesInSolidBrushIdentity()
    {
        var first = DrawingObjectPool.CanonicalizeBrush(new SolidColorBrush(Colors.Red) { Opacity = 0.25 });
        var second = DrawingObjectPool.CanonicalizeBrush(new SolidColorBrush(Colors.Red) { Opacity = 0.5 });
        Assert.NotSame(first, second);
        Assert.Same(first, DrawingObjectPool.CanonicalizeBrush(new SolidColorBrush(Colors.Red) { Opacity = 0.25 }));
    }

    [Theory]
    [InlineData(PenLineCap.Flat, PenLineJoin.Miter, 10)]
    [InlineData(PenLineCap.Round, PenLineJoin.Round, 5)]
    [InlineData(PenLineCap.Square, PenLineJoin.Bevel, 3)]
    public void UndashedPensShareCompleteFrozenValue(PenLineCap cap, PenLineJoin join, double miter)
    {
        Pen Create() => new(new SolidColorBrush(Colors.Red) { Opacity = 0.5 }, 3)
        {
            StartLineCap = cap, EndLineCap = cap, LineJoin = join, MiterLimit = miter,
        };
        var source = Create();
        var pooled = DrawingObjectPool.CanonicalizePen(source)!;
        Assert.NotSame(source, pooled);
        Assert.Same(pooled, DrawingObjectPool.CanonicalizePen(Create()));
        Assert.True(pooled.IsFrozen);
        Assert.Equal(cap, pooled.StartLineCap);
        Assert.Equal(cap, pooled.EndLineCap);
        Assert.Equal(join, pooled.LineJoin);
        Assert.Equal(miter, pooled.MiterLimit);
        Assert.Equal(0.5, pooled.Brush!.Opacity);
        source.Thickness = 19;
        ((SolidColorBrush)source.Brush!).Color = Colors.Blue;
        Assert.Equal(3, pooled.Thickness);
        Assert.Equal(Colors.Red, ((SolidColorBrush)pooled.Brush).Color);
    }

    [Fact]
    public void DifferentStrokeSettingsDoNotAlias()
    {
        var first = DrawingObjectPool.CanonicalizePen(new Pen(new SolidColorBrush(Colors.Red), 3));
        var thick = DrawingObjectPool.CanonicalizePen(new Pen(new SolidColorBrush(Colors.Red), 4));
        var capped = DrawingObjectPool.CanonicalizePen(new Pen(new SolidColorBrush(Colors.Red), 3) { EndLineCap = PenLineCap.Round });
        Assert.NotSame(first, thick);
        Assert.NotSame(first, capped);
    }

    [Fact]
    public void AnimatedColorsDoNotFlushFrequentlyUsedValues()
    {
        var hotBrush = new SolidColorBrush(Colors.Red);
        var hot = DrawingObjectPool.CanonicalizeBrush(hotBrush);
        for (int i = 0; i < 2048; i++)
        {
            DrawingObjectPool.CanonicalizeBrush(new SolidColorBrush(Color.FromArgb(255, (byte)(i >> 8), (byte)i, 32)));
            Assert.Same(hot, DrawingObjectPool.CanonicalizeBrush(hotBrush));
        }
    }

    [Fact]
    public void CustomDashAndGradientPensKeepTheirSpecializedSnapshotPath()
    {
        var dashed = new Pen(new SolidColorBrush(Colors.Red), 3) { DashStyle = new DashStyle([1d, 2d]) };
        var gradient = new Pen(new LinearGradientBrush(Colors.Red, Colors.Blue, 0), 3);
        Assert.Same(dashed, DrawingObjectPool.CanonicalizePen(dashed));
        Assert.Same(gradient, DrawingObjectPool.CanonicalizePen(gradient));
    }
}
