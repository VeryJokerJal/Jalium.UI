using System.Diagnostics;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Diagnostics;

namespace Jalium.UI.Tests;

public sealed class DevToolsPerformanceModelTests
{
    [Fact]
    public void FrameSummaryCountsTheLatestSecondAndDistinguishesIdleFromNoFrames()
    {
        long now = Stopwatch.Frequency * 10;
        var samples = new[] { Frame(100, now - Stopwatch.Frequency * 2), Frame(2, now - Stopwatch.Frequency / 2), Frame(4, now) };
        var live = DevToolsFrameSummary.Create(samples, now);
        Assert.True(live.HasFrames); Assert.False(live.IsIdle); Assert.Equal(2, live.FramesPerSecond); Assert.Equal(3, live.AverageMilliseconds);
        Assert.Equal(4, live.WorstMilliseconds); Assert.Equal("2", live.RateText); Assert.Equal(DevToolsPerformanceTone.Accent, live.Tone);
        var idle = DevToolsFrameSummary.Create(samples, now + Stopwatch.Frequency * 3);
        Assert.True(idle.IsIdle); Assert.Equal(0, idle.FramesPerSecond); Assert.Equal(3, idle.IdleSeconds); Assert.Equal(4, idle.LastMilliseconds); Assert.Equal("—", idle.RateText);
        var empty = DevToolsFrameSummary.Create(null, now); Assert.False(empty.HasFrames); Assert.False(empty.IsIdle); Assert.Equal("Waiting for frames", empty.DetailText);
    }

    [Fact]
    public void FrameSummaryHealthUsesTheBudgetAndItsAverageDoesNotOverflow()
    {
        long now = Stopwatch.Frequency * 10;
        var live = Enumerable.Range(0, 60).Select(_ => Frame(2, now)).ToArray();
        Assert.Equal(DevToolsPerformanceTone.Success, DevToolsFrameSummary.Create(live, now).Tone);
        Assert.Equal(DevToolsPerformanceTone.Warning, DevToolsFrameSummary.Create(live, now, 1).Tone);
        var huge = DevToolsFrameSummary.Create(new[] { Frame(double.MaxValue, now), Frame(double.MaxValue, now) }, now);
        Assert.Equal(double.MaxValue, huge.AverageMilliseconds); Assert.True(double.IsFinite(huge.AverageMilliseconds));
        Assert.Throws<ArgumentOutOfRangeException>(() => DevToolsFrameSummary.Create(live, now, double.PositiveInfinity));
    }

    [Fact]
    public void DataModelsNormalizeFractionsAndPublishTheirAutomationNames()
    {
        var metric = new DevToolsPerformanceMetric { Label = "Glyph atlas", ValueText = "7 / 2048", Fraction = double.NaN };
        Assert.Equal("Glyph atlas: 7 / 2048", metric.AutomationName); Assert.Equal(0, metric.Fraction);
        metric.Fraction = 2; Assert.Equal(1, metric.Fraction); metric.ValueText = ""; Assert.Equal("Glyph atlas", metric.AutomationName);
        var api = new DevToolsDrawApiItem { Name = "DrawText", CallsText = "10", TotalText = "1.23", AverageText = "123.0", Fraction = .75 };
        Assert.Equal(.25, api.RemainingFraction); Assert.Contains("DrawText; 10 calls; 1.23 ms", api.AutomationName);
        api.Fraction = -1; Assert.Equal(1, api.RemainingFraction);
        Assert.Throws<InvalidOperationException>(() => api.SetValue(DevToolsDrawApiItem.RemainingFractionProperty, .5));
    }
    private static FrameHistory.Sample Frame(double milliseconds, long ticks) => new(0, milliseconds, 0, milliseconds, 0, ticks);
}
