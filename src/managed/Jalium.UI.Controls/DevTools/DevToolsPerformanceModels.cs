using System.Diagnostics;
using System.Globalization;
using Jalium.UI.Diagnostics;

namespace Jalium.UI.Controls.DevTools;

/// <summary>Semantic colors selected by the consumer's performance templates.</summary>
public enum DevToolsPerformanceTone { Primary, Muted, Accent, Info, Success, Warning, Error, Keyword, Type, Enum }
public enum DevToolsPerformanceMetricKind { Value, Heading, Meter, Bar, Message }

/// <summary>One proportional diagnostic segment, independent of a particular theme palette.</summary>
public sealed record DevToolsPerformanceSegment(double Value, DevToolsPerformanceTone Tone)
{
    static DevToolsPerformanceSegment()
    {
        PropertyAccessorRegistry.Register(typeof(DevToolsPerformanceSegment), nameof(Value), item => ((DevToolsPerformanceSegment)item).Value);
        PropertyAccessorRegistry.Register(typeof(DevToolsPerformanceSegment), nameof(Tone), item => ((DevToolsPerformanceSegment)item).Tone);
    }
}

/// <summary>A stable bindable renderer metric; its presentation belongs to the item template.</summary>
public class DevToolsPerformanceMetric : DependencyObject
{
    internal string Key { get; set; } = string.Empty;
    private static DependencyProperty Property(string name, Type type, object? value) => DependencyProperty.Register(name, type, typeof(DevToolsPerformanceMetric), new PropertyMetadata(value));
    public static readonly DependencyProperty KindProperty = Property(nameof(Kind), typeof(DevToolsPerformanceMetricKind), DevToolsPerformanceMetricKind.Value);
    public static readonly DependencyProperty LabelProperty = Property(nameof(Label), typeof(string), string.Empty);
    public static readonly DependencyProperty ValueTextProperty = Property(nameof(ValueText), typeof(string), string.Empty);
    public static readonly DependencyProperty ToneProperty = Property(nameof(Tone), typeof(DevToolsPerformanceTone), DevToolsPerformanceTone.Primary);
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(DevToolsPerformanceMetric), new PropertyMetadata(0.0, null, (_, value) => Normalize(value is double fraction ? fraction : 0)));
    public static readonly DependencyProperty SegmentsProperty = Property(nameof(Segments), typeof(IReadOnlyList<DevToolsPerformanceSegment>), null);
    private static readonly DependencyPropertyKey AutomationNamePropertyKey = DependencyProperty.RegisterReadOnly(nameof(AutomationName), typeof(string), typeof(DevToolsPerformanceMetric), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty AutomationNameProperty = AutomationNamePropertyKey.DependencyProperty;
    public DevToolsPerformanceMetricKind Kind { get => (DevToolsPerformanceMetricKind)GetValue(KindProperty)!; set => SetValue(KindProperty, value); }
    public string Label { get => (string?)GetValue(LabelProperty) ?? string.Empty; set => SetValue(LabelProperty, value); }
    public string ValueText { get => (string?)GetValue(ValueTextProperty) ?? string.Empty; set => SetValue(ValueTextProperty, value); }
    public DevToolsPerformanceTone Tone { get => (DevToolsPerformanceTone)GetValue(ToneProperty)!; set => SetValue(ToneProperty, value); }
    public double Fraction { get => (double)GetValue(FractionProperty)!; set => SetValue(FractionProperty, value); }
    public IReadOnlyList<DevToolsPerformanceSegment>? Segments { get => (IReadOnlyList<DevToolsPerformanceSegment>?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public string AutomationName => (string)GetValue(AutomationNameProperty)!;
    internal static double Normalize(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == LabelProperty || e.Property == ValueTextProperty) SetValue(AutomationNamePropertyKey, string.IsNullOrEmpty(ValueText) ? Label : Label + ": " + ValueText);
    }
}

/// <summary>A stable draw-call row whose numeric text and relative cost can be styled independently.</summary>
public class DevToolsDrawApiItem : DependencyObject
{
    private static DependencyProperty Property(string name, Type type, object? value) => DependencyProperty.Register(name, type, typeof(DevToolsDrawApiItem), new PropertyMetadata(value));
    public static readonly DependencyProperty NameProperty = Property(nameof(Name), typeof(string), string.Empty);
    public static readonly DependencyProperty CallsTextProperty = Property(nameof(CallsText), typeof(string), "0");
    public static readonly DependencyProperty TotalTextProperty = Property(nameof(TotalText), typeof(string), "0.00");
    public static readonly DependencyProperty AverageTextProperty = Property(nameof(AverageText), typeof(string), "0.0");
    public static readonly DependencyProperty ToneProperty = Property(nameof(Tone), typeof(DevToolsPerformanceTone), DevToolsPerformanceTone.Primary);
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(DevToolsDrawApiItem), new PropertyMetadata(0.0, null, (_, value) => DevToolsPerformanceMetric.Normalize(value is double fraction ? fraction : 0)));
    private static readonly DependencyPropertyKey RemainingFractionPropertyKey = DependencyProperty.RegisterReadOnly(nameof(RemainingFraction), typeof(double), typeof(DevToolsDrawApiItem), new PropertyMetadata(1.0));
    public static readonly DependencyProperty RemainingFractionProperty = RemainingFractionPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey AutomationNamePropertyKey = DependencyProperty.RegisterReadOnly(nameof(AutomationName), typeof(string), typeof(DevToolsDrawApiItem), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty AutomationNameProperty = AutomationNamePropertyKey.DependencyProperty;
    public string Name { get => (string?)GetValue(NameProperty) ?? string.Empty; set => SetValue(NameProperty, value); }
    public string CallsText { get => (string?)GetValue(CallsTextProperty) ?? string.Empty; set => SetValue(CallsTextProperty, value); }
    public string TotalText { get => (string?)GetValue(TotalTextProperty) ?? string.Empty; set => SetValue(TotalTextProperty, value); }
    public string AverageText { get => (string?)GetValue(AverageTextProperty) ?? string.Empty; set => SetValue(AverageTextProperty, value); }
    public DevToolsPerformanceTone Tone { get => (DevToolsPerformanceTone)GetValue(ToneProperty)!; set => SetValue(ToneProperty, value); }
    public double Fraction { get => (double)GetValue(FractionProperty)!; set => SetValue(FractionProperty, value); }
    public double RemainingFraction => (double)GetValue(RemainingFractionProperty)!;
    public string AutomationName => (string)GetValue(AutomationNameProperty)!;
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == FractionProperty) SetValue(RemainingFractionPropertyKey, 1 - Fraction);
        if (e.Property == NameProperty || e.Property == CallsTextProperty || e.Property == TotalTextProperty || e.Property == AverageTextProperty)
            SetValue(AutomationNamePropertyKey, $"{Name}; {CallsText} calls; {TotalText} ms total; {AverageText} microseconds average");
    }
}

/// <summary>A wall-clock frame-rate readout that distinguishes idle history from live frames.</summary>
public sealed record DevToolsFrameSummary(bool HasFrames, bool IsIdle, double FramesPerSecond, double AverageMilliseconds,
    double WorstMilliseconds, double IdleSeconds, double LastMilliseconds, DevToolsPerformanceTone Tone)
{
    public string RateText => HasFrames && !IsIdle ? FramesPerSecond.ToString("F0", CultureInfo.InvariantCulture) : "—";
    public string DetailText => !HasFrames ? "Waiting for frames" : IsIdle
        ? $"Idle {IdleSeconds:F1} s · last {LastMilliseconds:F1} ms"
        : $"avg {AverageMilliseconds:F1} ms · worst {WorstMilliseconds:F1} ms";
    static DevToolsFrameSummary()
    {
        PropertyAccessorRegistry.Register(typeof(DevToolsFrameSummary), nameof(HasFrames), item => ((DevToolsFrameSummary)item).HasFrames);
        PropertyAccessorRegistry.Register(typeof(DevToolsFrameSummary), nameof(IsIdle), item => ((DevToolsFrameSummary)item).IsIdle);
        PropertyAccessorRegistry.Register(typeof(DevToolsFrameSummary), nameof(FramesPerSecond), item => ((DevToolsFrameSummary)item).FramesPerSecond);
        PropertyAccessorRegistry.Register(typeof(DevToolsFrameSummary), nameof(AverageMilliseconds), item => ((DevToolsFrameSummary)item).AverageMilliseconds);
        PropertyAccessorRegistry.Register(typeof(DevToolsFrameSummary), nameof(WorstMilliseconds), item => ((DevToolsFrameSummary)item).WorstMilliseconds);
        PropertyAccessorRegistry.Register(typeof(DevToolsFrameSummary), nameof(IdleSeconds), item => ((DevToolsFrameSummary)item).IdleSeconds);
        PropertyAccessorRegistry.Register(typeof(DevToolsFrameSummary), nameof(LastMilliseconds), item => ((DevToolsFrameSummary)item).LastMilliseconds);
        PropertyAccessorRegistry.Register(typeof(DevToolsFrameSummary), nameof(Tone), item => ((DevToolsFrameSummary)item).Tone);
        PropertyAccessorRegistry.Register(typeof(DevToolsFrameSummary), nameof(RateText), item => ((DevToolsFrameSummary)item).RateText);
        PropertyAccessorRegistry.Register(typeof(DevToolsFrameSummary), nameof(DetailText), item => ((DevToolsFrameSummary)item).DetailText);
    }
    public static DevToolsFrameSummary Create(IReadOnlyList<FrameHistory.Sample>? samples, long nowTicks, double frameBudgetMs = 16)
    {
        if (!double.IsFinite(frameBudgetMs) || frameBudgetMs <= 0) throw new ArgumentOutOfRangeException(nameof(frameBudgetMs));
        int count = samples?.Count ?? 0;
        if (count == 0) return new(false, false, 0, 0, 0, 0, 0, DevToolsPerformanceTone.Muted);
        int live = 0; double average = 0, worst = 0; long earliest = nowTicks - Stopwatch.Frequency;
        for (int i = count - 1; i >= Math.Max(0, count - FrameHistory.Capacity); i--)
        {
            var sample = samples![i]; if (sample.TimestampTicks < earliest) break;
            if (sample.TimestampTicks > nowTicks) continue;
            double duration = double.IsFinite(sample.TotalMs) ? Math.Max(0, sample.TotalMs) : 0;
            live++; average += (duration - average) / live; worst = Math.Max(worst, duration);
        }
        var last = samples![count - 1]; double age = Math.Max(0, (nowTicks - (double)last.TimestampTicks) / Stopwatch.Frequency);
        double lastMs = double.IsFinite(last.TotalMs) ? Math.Max(0, last.TotalMs) : 0;
        return new(true, live == 0, live, average, worst, live == 0 ? age : 0, lastMs,
            live == 0 ? DevToolsPerformanceTone.Muted : worst > frameBudgetMs ? DevToolsPerformanceTone.Warning : live >= 55 ? DevToolsPerformanceTone.Success : DevToolsPerformanceTone.Accent);
    }
}
