using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class ProgressBarRangeWpfParityTests
{
    [Fact]
    public void ProgressBarUsesRangeBaseContractsAndWpfMaximumDefault()
    {
        Assert.Equal(typeof(RangeBase), typeof(ProgressBar).BaseType);
        Assert.Same(RangeBase.MinimumProperty, ProgressBar.MinimumProperty);
        Assert.Same(RangeBase.MaximumProperty, ProgressBar.MaximumProperty);
        Assert.Same(RangeBase.ValueProperty, ProgressBar.ValueProperty);
        Assert.Same(RangeBase.ValueChangedEvent, ProgressBar.ValueChangedEvent);

        var progressBar = new ProgressBar();
        Assert.Equal(0, progressBar.Minimum);
        Assert.Equal(100, progressBar.Maximum);
        Assert.Equal(0, progressBar.Value);
    }

    [Fact]
    public void RangeChangesInvokeProgressBarOverridesAndCoerceValue()
    {
        var progressBar = new ProbeProgressBar { Value = 80 };
        var valueChangedCount = 0;
        progressBar.ValueChanged += (_, _) => valueChangedCount++;

        progressBar.Maximum = 50;
        progressBar.Minimum = 10;

        Assert.Equal(50, progressBar.Value);
        Assert.Equal(1, progressBar.MaximumChangedCount);
        Assert.Equal(1, progressBar.MinimumChangedCount);
        Assert.Equal(1, valueChangedCount);

        progressBar.Maximum = 100;

        Assert.Equal(80, progressBar.Value);
        Assert.Equal(2, progressBar.MaximumChangedCount);
        Assert.Equal(2, valueChangedCount);
    }

    private sealed class ProbeProgressBar : ProgressBar
    {
        public int MinimumChangedCount { get; private set; }
        public int MaximumChangedCount { get; private set; }

        protected override void OnMinimumChanged(double oldMinimum, double newMinimum)
        {
            MinimumChangedCount++;
            base.OnMinimumChanged(oldMinimum, newMinimum);
        }

        protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
        {
            MaximumChangedCount++;
            base.OnMaximumChanged(oldMaximum, newMaximum);
        }
    }
}
