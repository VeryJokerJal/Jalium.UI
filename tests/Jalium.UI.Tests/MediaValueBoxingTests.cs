using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class MediaValueBoxingTests
{
    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void LineCaps_PreserveEnumDomainAcrossStorageCloneAndFreeze(int raw)
    {
        var value = (PenLineCap)raw;
        var pen = new Pen { StartLineCap = value, EndLineCap = value, DashCap = value };
        Assert.Equal(value, pen.StartLineCap);
        Assert.Equal(value, pen.EndLineCap);
        Assert.Equal(value, pen.DashCap);
        Assert.Equal(value, Assert.IsType<PenLineCap>(pen.ReadLocalValue(Pen.StartLineCapProperty)));
        Assert.Equal(value, pen.Clone().StartLineCap);
        pen.Freeze();
        Assert.Equal(value, pen.CloneCurrentValue().EndLineCap);
        Assert.Throws<InvalidOperationException>(() => pen.StartLineCap = PenLineCap.Flat);
    }

    [Fact]
    public void DrawingFlags_ExplicitObjectSetterPreservesSuppliedBox()
    {
        var pen = new Pen();
        object cap = PenLineCap.Round;
        pen.SetValue(Pen.EndLineCapProperty, cap);
        Assert.Same(cap, pen.ReadLocalValue(Pen.EndLineCapProperty));

        var figure = new PathFigure();
        object filled = false;
        figure.SetValue(PathFigure.IsFilledProperty, filled);
        Assert.Same(filled, figure.ReadLocalValue(PathFigure.IsFilledProperty));
    }

    [Fact]
    public void ColorStorage_IsIndependentAcrossBrushes()
    {
        var color = Color.FromArgb(200, 30, 90, 170);
        var first = new SolidColorBrush(color);
        var second = new SolidColorBrush(color);
        object value = first.ReadLocalValue(SolidColorBrush.ColorProperty)!;
        typeof(Color).GetProperty(nameof(Color.R))!.SetValue(value, (byte)240);
        Assert.Equal(240, first.Color.R);
        Assert.Equal(30, second.Color.R);
    }

    [Fact]
    public void ColorStorage_MutableBrushDoesNotChangeSeparateFrozenBrush()
    {
        var color = Color.FromArgb(200, 30, 90, 170);
        var mutable = new SolidColorBrush(color);
        var frozen = new SolidColorBrush(color);
        frozen.Freeze();
        object value = mutable.ReadLocalValue(SolidColorBrush.ColorProperty)!;
        typeof(Color).GetProperty(nameof(Color.R))!.SetValue(value, (byte)240);
        Assert.Equal(30, frozen.Color.R);
        Assert.True(frozen.IsFrozen);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(long.MinValue)]
    [InlineData(0x3FF0_0000_0000_0000L)]
    [InlineData(0x3FF4_0000_0000_0000L)]
    [InlineData(0x4009_21FB_5444_2D18L)]
    [InlineData(1L)]
    [InlineData(0x0010_0000_0000_0000L)]
    [InlineData(0x7FEF_FFFF_FFFF_FFFFL)]
    [InlineData(0x7FF0_0000_0000_0000L)]
    [InlineData(unchecked((long)0xFFF0_0000_0000_0000UL))]
    [InlineData(0x7FF8_0000_0000_0001L)]
    [InlineData(0x7FF8_0000_0000_0002L)]
    [InlineData(unchecked((long)0xFFF8_0000_0000_0001UL))]
    public void Thickness_PreservesExactBitsAcrossStorageCloneAndFreeze(long bits)
    {
        double value = BitConverter.Int64BitsToDouble(bits);
        var pen = new Pen(null, value);
        AssertBits(bits, pen.Thickness);
        AssertBits(bits, Assert.IsType<double>(pen.ReadLocalValue(Pen.ThicknessProperty)));
        pen.Thickness = 2.0;
        pen.Thickness = value;
        AssertBits(bits, pen.Thickness);
        AssertBits(bits, pen.Clone().Thickness);
        pen.Freeze();
        AssertBits(bits, pen.Thickness);
        AssertBits(bits, pen.CloneCurrentValue().Thickness);
        Assert.Throws<InvalidOperationException>(() => pen.Thickness = 3.0);
    }

    [Fact]
    public void Thickness_ExplicitObjectSetterPreservesSuppliedBox()
    {
        var pen = new Pen(null, 1.0);
        object value = 1.25;
        pen.SetValue(Pen.ThicknessProperty, value);
        Assert.Same(value, pen.ReadLocalValue(Pen.ThicknessProperty));
    }

    [Fact]
    public void Thickness_ConcurrentIndependentOwnersPreserveValues()
    {
        var failures = new Exception?[4];
        var threads = Enumerable.Range(0, failures.Length).Select(worker => new Thread(() =>
        {
            try
            {
                var pen = new Pen(null, 1.0);
                for (int index = 0; index < 4096; index++)
                {
                    long bits = 0x3FF0_0000_0000_0000L + ((long)index << 20) + worker;
                    pen.Thickness = BitConverter.Int64BitsToDouble(bits);
                    AssertBits(bits, pen.Thickness);
                }
            }
            catch (Exception exception) { failures[worker] = exception; }
        }) { IsBackground = true }).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) Assert.True(thread.Join(10000), "Numeric storage worker did not finish.");
        Assert.All(failures, failure => Assert.Null(failure));
    }

    private static void AssertBits(long expected, double actual)
        => Assert.Equal(expected, BitConverter.DoubleToInt64Bits(actual));
}
