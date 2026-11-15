using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Diagnostics;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.MillionScroll.Themes;
using Jalium.UI.Threading;

namespace Jalium.UI.MillionScroll;

/// <summary>Virtualized million-row lab with a replaceable UI and the original benchmark engine.</summary>
[TemplatePart(Name = "PART_List", Type = typeof(MillionScrollListBox))]
[TemplatePart(Name = "PART_ApplyRows", Type = typeof(Button))]
[TemplatePart(Name = "PART_Jump", Type = typeof(Button))]
[TemplatePart(Name = "PART_Top", Type = typeof(Button))]
[TemplatePart(Name = "PART_Half", Type = typeof(Button))]
[TemplatePart(Name = "PART_Bottom", Type = typeof(Button))]
[TemplatePart(Name = "PART_ToggleAuto", Type = typeof(Button))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Stopped", GroupName = "ScrollStates")]
[TemplateVisualState(Name = "Running", GroupName = "ScrollStates")]
[TemplateVisualState(Name = "Wide", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Compact", GroupName = "LayoutStates")]
public class MillionScrollSurface : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(MillionScrollSurface), new PropertyMetadata("Million Scroll"));
    public string Title { get => (string)GetValue(TitleProperty)!; set => SetValue(TitleProperty, value); }
    public static readonly DependencyProperty RequestedRowsProperty = DependencyProperty.Register(nameof(RequestedRows), typeof(double), typeof(MillionScrollSurface), new PropertyMetadata(1_000_000d), value => value is double number && double.IsFinite(number) && number >= 1 && number <= 10_000_000);
    public double RequestedRows { get => (double)GetValue(RequestedRowsProperty)!; set => SetValue(RequestedRowsProperty, value); }
    public static readonly DependencyProperty JumpRowProperty = DependencyProperty.Register(nameof(JumpRow), typeof(double), typeof(MillionScrollSurface), new PropertyMetadata(500_000d), value => value is double number && double.IsFinite(number) && number >= 1 && number <= 10_000_000);
    public double JumpRow { get => (double)GetValue(JumpRowProperty)!; set => SetValue(JumpRowProperty, value); }
    public static readonly DependencyProperty ScrollSpeedProperty = DependencyProperty.Register(nameof(ScrollSpeed), typeof(double), typeof(MillionScrollSurface), new PropertyMetadata(40d, OnSpeedChanged), value => value is double number && double.IsFinite(number) && number >= 1 && number <= 10_000);
    public double ScrollSpeed { get => (double)GetValue(ScrollSpeedProperty)!; set => SetValue(ScrollSpeedProperty, value); }
    public static readonly DependencyProperty IsAutoScrollingProperty = DependencyProperty.Register(nameof(IsAutoScrolling), typeof(bool), typeof(MillionScrollSurface), new PropertyMetadata(false, OnAutoChanged));
    public bool IsAutoScrolling { get => (bool)GetValue(IsAutoScrollingProperty)!; set => SetValue(IsAutoScrollingProperty, value); }
    public static readonly DependencyProperty SelectedRowIndexProperty = DependencyProperty.Register(nameof(SelectedRowIndex), typeof(int), typeof(MillionScrollSurface), new PropertyMetadata(-1));
    public int SelectedRowIndex { get => (int)GetValue(SelectedRowIndexProperty)!; set => SetValue(SelectedRowIndexProperty, value); }
    public static readonly DependencyProperty BackendProperty = DependencyProperty.Register(nameof(Backend), typeof(RenderBackend), typeof(MillionScrollSurface), new PropertyMetadata(RenderBackend.Auto, OnBackendChanged));
    public RenderBackend Backend { get => (RenderBackend)GetValue(BackendProperty)!; set => SetValue(BackendProperty, value); }
    public static readonly DependencyProperty EngineProperty = DependencyProperty.Register(nameof(Engine), typeof(RenderingEngine), typeof(MillionScrollSurface), new PropertyMetadata(RenderingEngine.Impeller, OnBackendChanged));
    public RenderingEngine Engine { get => (RenderingEngine)GetValue(EngineProperty)!; set => SetValue(EngineProperty, value); }
    private static readonly DependencyPropertyKey RowsSourcePropertyKey = DependencyProperty.RegisterReadOnly(nameof(RowsSource), typeof(IList), typeof(MillionScrollSurface), new PropertyMetadata(null));
    public static readonly DependencyProperty RowsSourceProperty = RowsSourcePropertyKey.DependencyProperty;
    public IList RowsSource => (IList)GetValue(RowsSourceProperty)!;
    private static readonly DependencyPropertyKey RowCountPropertyKey = DependencyProperty.RegisterReadOnly(nameof(RowCount), typeof(int), typeof(MillionScrollSurface), new PropertyMetadata(1_000_000));
    public static readonly DependencyProperty RowCountProperty = RowCountPropertyKey.DependencyProperty;
    public int RowCount => (int)GetValue(RowCountProperty)!;
    private static readonly DependencyPropertyKey JumpMaximumPropertyKey = DependencyProperty.RegisterReadOnly(nameof(JumpMaximum), typeof(double), typeof(MillionScrollSurface), new PropertyMetadata(1_000_000d));
    public static readonly DependencyProperty JumpMaximumProperty = JumpMaximumPropertyKey.DependencyProperty;
    public double JumpMaximum => (double)GetValue(JumpMaximumProperty)!;
    public static readonly DependencyProperty ShutdownOnBenchmarkCompletionProperty = DependencyProperty.Register(nameof(ShutdownOnBenchmarkCompletion), typeof(bool), typeof(MillionScrollSurface), new PropertyMetadata(false));
    public bool ShutdownOnBenchmarkCompletion { get => (bool)GetValue(ShutdownOnBenchmarkCompletionProperty)!; set => SetValue(ShutdownOnBenchmarkCompletionProperty, value); }
    public static readonly RoutedEvent BenchmarkCompletedEvent = EventManager.RegisterRoutedEvent(nameof(BenchmarkCompleted), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MillionScrollSurface));
    public event RoutedEventHandler BenchmarkCompleted { add => AddHandler(BenchmarkCompletedEvent, value); remove => RemoveHandler(BenchmarkCompletedEvent, value); }
    private static readonly DependencyPropertyKey BenchmarkOutputPathPropertyKey = DependencyProperty.RegisterReadOnly(nameof(BenchmarkOutputPath), typeof(string), typeof(MillionScrollSurface), new PropertyMetadata(""));
    public static readonly DependencyProperty BenchmarkOutputPathProperty = BenchmarkOutputPathPropertyKey.DependencyProperty;
    public string BenchmarkOutputPath => (string)GetValue(BenchmarkOutputPathProperty)!;
    private static readonly DependencyPropertyKey BenchmarkErrorPropertyKey = DependencyProperty.RegisterReadOnly(nameof(BenchmarkError), typeof(string), typeof(MillionScrollSurface), new PropertyMetadata(""));
    public static readonly DependencyProperty BenchmarkErrorProperty = BenchmarkErrorPropertyKey.DependencyProperty;
    public string BenchmarkError => (string)GetValue(BenchmarkErrorProperty)!;
    private static readonly DependencyPropertyKey BackendTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(BackendText), typeof(string), typeof(MillionScrollSurface), new PropertyMetadata("Auto / Impeller"));
    public static readonly DependencyProperty BackendTextProperty = BackendTextPropertyKey.DependencyProperty;
    public string BackendText => (string)GetValue(BackendTextProperty)!;
    private static readonly DependencyPropertyKey RowsTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(RowsText), typeof(string), typeof(MillionScrollSurface), new PropertyMetadata("1,000,000"));
    public static readonly DependencyProperty RowsTextProperty = RowsTextPropertyKey.DependencyProperty;
    public string RowsText => (string)GetValue(RowsTextProperty)!;
    private static readonly DependencyPropertyKey RealizedTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(RealizedText), typeof(string), typeof(MillionScrollSurface), new PropertyMetadata("0"));
    public static readonly DependencyProperty RealizedTextProperty = RealizedTextPropertyKey.DependencyProperty;
    public string RealizedText => (string)GetValue(RealizedTextProperty)!;
    private static readonly DependencyPropertyKey RangeTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(RangeText), typeof(string), typeof(MillionScrollSurface), new PropertyMetadata("-"));
    public static readonly DependencyProperty RangeTextProperty = RangeTextPropertyKey.DependencyProperty;
    public string RangeText => (string)GetValue(RangeTextProperty)!;
    private static readonly DependencyPropertyKey OffsetTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(OffsetText), typeof(string), typeof(MillionScrollSurface), new PropertyMetadata("-"));
    public static readonly DependencyProperty OffsetTextProperty = OffsetTextPropertyKey.DependencyProperty;
    public string OffsetText => (string)GetValue(OffsetTextProperty)!;
    private static readonly DependencyPropertyKey MemoryTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(MemoryText), typeof(string), typeof(MillionScrollSurface), new PropertyMetadata("-"));
    public static readonly DependencyProperty MemoryTextProperty = MemoryTextPropertyKey.DependencyProperty;
    public string MemoryText => (string)GetValue(MemoryTextProperty)!;
    private static readonly DependencyPropertyKey SelectedTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SelectedText), typeof(string), typeof(MillionScrollSurface), new PropertyMetadata("Selected row -"));
    public static readonly DependencyProperty SelectedTextProperty = SelectedTextPropertyKey.DependencyProperty;
    public string SelectedText => (string)GetValue(SelectedTextProperty)!;
    private static readonly DependencyPropertyKey SpeedTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SpeedText), typeof(string), typeof(MillionScrollSurface), new PropertyMetadata("40 px/tick"));
    public static readonly DependencyProperty SpeedTextProperty = SpeedTextPropertyKey.DependencyProperty;
    public string SpeedText => (string)GetValue(SpeedTextProperty)!;
    private static readonly DependencyPropertyKey AutoButtonTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(AutoButtonText), typeof(string), typeof(MillionScrollSurface), new PropertyMetadata("Start auto scroll"));
    public static readonly DependencyProperty AutoButtonTextProperty = AutoButtonTextPropertyKey.DependencyProperty;
    public string AutoButtonText => (string)GetValue(AutoButtonTextProperty)!;

    private MillionScrollListBox? _listBox;
    private ScrollViewer? _scrollViewer;
    private Window? _window;
    private readonly List<(Button Button, RoutedEventHandler Handler)> _buttons = [];
    private readonly DispatcherTimer _autoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _benchmarkTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private MillionScrollRunOptions _options = MillionScrollRunOptions.Parse([], 1_000_000);
    private readonly Stopwatch _benchmarkClock = new();
    private readonly List<FrameHistory.Sample> _benchmarkSamples = [];
    private readonly List<RenderDiagnostics.GpuTimingSnapshot> _benchmarkGpuSamples = [];
    private readonly FrameHistory.Sample[] _historyBuffer = new FrameHistory.Sample[FrameHistory.Capacity];
    private int _rowCount = 1_000_000;
    private bool _benchmarkStarted, _benchmarkMeasuring, _benchmarkFinished, _restorePending;
    private double _retainedOffset;
    private long _lastCollectedFrame, _benchmarkStartMemory, _benchmarkStartAllocatedBytes;
    private DateTime _lastCollectedGpuTimestamp;
    private readonly int[] _benchmarkStartGc = new int[3];
    private DateTimeOffset _measurementStartedUtc;

    public MillionScrollSurface()
    {
        MillionScrollTheme.Register();
        SetValue(RowsSourcePropertyKey, new MillionRowSource(_rowCount));
        _autoScrollTimer.Tick += (_, _) => TickAutoScroll();
        _hudTimer.Tick += (_, _) => RefreshHud();
        _benchmarkTimer.Tick += (_, _) => TickBenchmark();
        Loaded += (_, _) => StartRuntime();
        Unloaded += (_, _) => { RetainOffset(); PauseRuntime(); };
        SizeChanged += (_, _) => UpdateStates();
        DefaultStyleKey = typeof(MillionScrollSurface);
    }
    public void Configure(MillionScrollRunOptions options, RenderBackend actualBackend, RenderingEngine actualEngine)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_benchmarkStarted) throw new InvalidOperationException("An active benchmark cannot be reconfigured.");
        _options = options;
        SetCurrentValue(BackendProperty, actualBackend); SetCurrentValue(EngineProperty, actualEngine);
        SetCurrentValue(RequestedRowsProperty, (double)Math.Clamp(options.Rows, 1, 10_000_000));
        SetCurrentValue(JumpRowProperty, (double)Math.Min(Math.Clamp(options.Rows, 1, 10_000_000), 500_000));
        SetCurrentValue(ScrollSpeedProperty, options.BenchmarkEnabled ? Math.Clamp(options.ScrollSpeed, 1, 10_000) : 40d);
        ApplyRows();
    }
    public override void OnApplyTemplate()
    {
        ReleaseParts(); base.OnApplyTemplate();
        _listBox = GetTemplateChild("PART_List") as MillionScrollListBox;
        if (_listBox is not null) { _listBox.Loaded += OnListLoaded; _listBox.SelectionChanged += OnSelectionChanged; }
        Hook("PART_ApplyRows", OnApplyRows); Hook("PART_Jump", OnJump); Hook("PART_Top", OnTop); Hook("PART_Half", OnHalf); Hook("PART_Bottom", OnBottom); Hook("PART_ToggleAuto", OnToggleAuto);
        if (IsLoaded) StartRuntime();
        RefreshHud(); UpdateStates();
    }
    protected override void OnTemplateChanging()
    {
        RetainOffset();
        if (_listBox is not null) _listBox.ClearBinding(Selector.SelectedIndexProperty);
        PauseRuntime(); ReleaseParts(); _restorePending = true;
        base.OnTemplateChanging();
    }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateAutoScroll(); UpdateStates(); }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        if (_restorePending) GetScrollViewer();
        return arranged;
    }
    private static void OnSpeedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((MillionScrollSurface)d).UpdateSpeedText();
    private static void OnBackendChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { var surface = (MillionScrollSurface)d; surface.SetValue(BackendTextPropertyKey, $"{surface.Backend} / {surface.Engine}"); }
    private static void OnAutoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { var surface = (MillionScrollSurface)d; surface.UpdateAutoScroll(); surface.UpdateStates(); }
    private void UpdateAutoScroll()
    {
        SetValue(AutoButtonTextPropertyKey, IsAutoScrolling ? "Stop auto scroll" : "Start auto scroll");
        if (IsLoaded && _listBox is not null && IsAutoScrolling && IsEnabled) _autoScrollTimer.Start(); else _autoScrollTimer.Stop();
    }
    private void OnListLoaded(object? sender, RoutedEventArgs e) { StartRuntime(); RefreshHud(); }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionText();
    private void OnApplyRows(object sender, RoutedEventArgs e) { if (IsEnabled) ApplyRows(); }
    private void OnJump(object sender, RoutedEventArgs e) { if (IsEnabled) JumpToRow(); }
    private void OnTop(object sender, RoutedEventArgs e) { if (IsEnabled) JumpToIndex(0); }
    private void OnHalf(object sender, RoutedEventArgs e) { if (IsEnabled) JumpToIndex(_rowCount / 2); }
    private void OnBottom(object sender, RoutedEventArgs e) { if (IsEnabled) JumpToIndex(_rowCount - 1); }
    private void OnToggleAuto(object sender, RoutedEventArgs e) { if (IsEnabled) ToggleAutoScroll(); }
    private void Hook(string name, RoutedEventHandler handler) { if (GetTemplateChild(name) is Button button) { button.Click += handler; _buttons.Add((button, handler)); } }
    private void ReleaseParts()
    {
        if (_listBox is not null) { _listBox.Loaded -= OnListLoaded; _listBox.SelectionChanged -= OnSelectionChanged; }
        foreach (var (button, handler) in _buttons) button.Click -= handler;
        _buttons.Clear(); _listBox = null; _scrollViewer = null;
    }
    private void RetainOffset() { if (_scrollViewer is not null) _retainedOffset = _scrollViewer.VerticalOffset; }
    private void StartRuntime()
    {
        if (!IsLoaded || _listBox is null) return;
        var window = Window.GetWindow(this);
        if (!ReferenceEquals(window, _window))
        {
            if (_window is not null) _window.Closed -= OnWindowClosed;
            _window = window;
            if (_window is not null) _window.Closed += OnWindowClosed;
        }
        _hudTimer.Start(); UpdateAutoScroll();
        if (_benchmarkStarted && !_benchmarkFinished) { _benchmarkClock.Start(); _benchmarkTimer.Start(); }
        StartBenchmarkIfRequested();
    }
    private void PauseRuntime()
    {
        _hudTimer.Stop(); _autoScrollTimer.Stop(); _benchmarkTimer.Stop(); _benchmarkClock.Stop();
        if (_window is not null) _window.Closed -= OnWindowClosed;
        _window = null;
    }
    private void OnWindowClosed(object? sender, EventArgs e) => PauseRuntime();
    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, IsAutoScrolling ? "Running" : "Stopped", false);
        VisualStateManager.GoToState(this, ActualWidth < 820 ? "Compact" : "Wide", false);
    }

    public void ApplyRows()
    {
        _rowCount = Math.Clamp((int)Math.Round(RequestedRows), 1, 10_000_000);
        SetValue(RowCountPropertyKey, _rowCount);
        SetValue(JumpMaximumPropertyKey, (double)_rowCount);
        SetCurrentValue(JumpRowProperty, Math.Clamp(JumpRow, 1, _rowCount));
        SetValue(RowsSourcePropertyKey, new MillionRowSource(_rowCount));
        GetScrollViewer()?.ScrollToVerticalOffset(0);
        RefreshHud();
    }

    public void JumpToRow()
    {
        JumpToIndex((int)Math.Round(JumpRow) - 1);
    }

    public void JumpToIndex(int index)
    {
        if (_rowCount <= 0)
        {
            return;
        }

        var targetIndex = Math.Clamp(index, 0, _rowCount - 1);
        SetCurrentValue(JumpRowProperty, (double)(targetIndex + 1));
        // An explicit jump supersedes a deferred offset from the retired template.
        _restorePending = false;
        _listBox?.ScrollIndexIntoView(targetIndex);
        RefreshHud();
    }

    public void ToggleAutoScroll() => SetCurrentValue(IsAutoScrollingProperty, !IsAutoScrolling);

    private void TickAutoScroll()
    {
        var scroller = GetScrollViewer();
        if (scroller == null)
        {
            return;
        }

        var next = scroller.VerticalOffset + Math.Max(1, ScrollSpeed);
        scroller.ScrollToVerticalOffset(next >= scroller.ScrollableHeight ? 0 : next);
    }

    private void StartBenchmarkIfRequested()
    {
        if (!_options.BenchmarkEnabled || _benchmarkStarted || _window == null || GetScrollViewer() is null)
        {
            return;
        }

        _benchmarkStarted = true;
        SetCurrentValue(IsAutoScrollingProperty, true);
        // Hardware timestamp readback is opt-in. Enabling it only for an
        // explicit benchmark keeps normal sample runs free of query overhead
        // while giving Vello/Impeller comparisons direct GPU evidence.
        RenderDiagnostics.ApiStatsEnabled = true;
        _autoScrollTimer.Start();
        _benchmarkClock.Restart();
        _benchmarkTimer.Start();

        if (_options.WarmupSeconds <= 0)
        {
            BeginBenchmarkMeasurement();
        }
    }

    private void TickBenchmark()
    {
        if (!_benchmarkMeasuring)
        {
            if (_benchmarkClock.Elapsed.TotalSeconds >= _options.WarmupSeconds)
            {
                BeginBenchmarkMeasurement();
            }
            return;
        }

        CollectBenchmarkSamples();
        if (_benchmarkClock.Elapsed.TotalSeconds >= _options.BenchmarkSeconds)
        {
            FinishBenchmark();
        }
    }

    private void BeginBenchmarkMeasurement()
    {
        if (_window == null || _benchmarkMeasuring)
        {
            return;
        }

        _benchmarkSamples.Clear();
        _benchmarkGpuSamples.Clear();
        _window.FrameHistory.Clear();
        _lastCollectedFrame = 0;
        _lastCollectedGpuTimestamp = default;
        _benchmarkStartMemory = GC.GetTotalMemory(false);
        _benchmarkStartAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
        for (var generation = 0; generation < _benchmarkStartGc.Length; generation++)
        {
            _benchmarkStartGc[generation] = GC.CollectionCount(generation);
        }
        _measurementStartedUtc = DateTimeOffset.UtcNow;
        _benchmarkMeasuring = true;
        _benchmarkClock.Restart();
    }

    private void CollectBenchmarkSamples()
    {
        if (_window == null)
        {
            return;
        }

        var history = _window.FrameHistory;
        var totalFrames = history.TotalFrames;
        var copied = history.CopyTo(_historyBuffer);
        var newFrameCount = Math.Clamp(totalFrames - _lastCollectedFrame, 0, copied);
        var first = copied - (int)newFrameCount;
        for (var i = first; i < copied; i++)
        {
            _benchmarkSamples.Add(_historyBuffer[i]);
        }
        _lastCollectedFrame = totalFrames;

        var gpu = RenderDiagnostics.LatestGpuTiming;
        if (gpu is { Valid: true } && gpu.Timestamp > _lastCollectedGpuTimestamp)
        {
            _benchmarkGpuSamples.Add(gpu);
            _lastCollectedGpuTimestamp = gpu.Timestamp;
        }
    }

    private void FinishBenchmark()
    {
        if (_window == null || !_benchmarkMeasuring)
        {
            return;
        }

        CollectBenchmarkSamples();
        _benchmarkMeasuring = false;
        _benchmarkFinished = true;
        SetCurrentValue(IsAutoScrollingProperty, false);
        _benchmarkTimer.Stop();
        _autoScrollTimer.Stop();
        _hudTimer.Stop();

        var elapsedSeconds = Math.Max(0.001, _benchmarkClock.Elapsed.TotalSeconds);
        var frameCount = _window.FrameHistory.TotalFrames;
        var sampleCount = _benchmarkSamples.Count;
        var third = Math.Max(1, sampleCount / 3);
        var earlyMedian = Percentile(_benchmarkSamples, 0, Math.Min(third, sampleCount), 0.50, static sample => sample.TotalMs);
        var lateStart = Math.Max(0, sampleCount - third);
        var lateMedian = Percentile(_benchmarkSamples, lateStart, sampleCount - lateStart, 0.50, static sample => sample.TotalMs);
        var trendPercent = earlyMedian > 0 ? (lateMedian - earlyMedian) * 100.0 / earlyMedian : 0;
        var endMemory = GC.GetTotalMemory(false);
        var endAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
        var benchmarkGcCollections = new int[_benchmarkStartGc.Length];
        for (var generation = 0; generation < benchmarkGcCollections.Length; generation++)
        {
            benchmarkGcCollections[generation] = GC.CollectionCount(generation) - _benchmarkStartGc[generation];
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var retainedMemory = GC.GetTotalMemory(false);

        var result = new
        {
            schemaVersion = 3,
            startedUtc = _measurementStartedUtc,
            backend = Backend.ToString(),
            engine = Engine.ToString(),
            rows = _rowCount,
            scrollSpeedPixelsPerTick = ScrollSpeed,
            warmupSeconds = _options.WarmupSeconds,
            elapsedSeconds,
            renderedFrames = frameCount,
            capturedSamples = sampleCount,
            framesPerSecond = frameCount / elapsedSeconds,
            frameMs = Distribution(_benchmarkSamples, static sample => sample.TotalMs),
            layoutMs = Distribution(_benchmarkSamples, static sample => sample.LayoutMs),
            renderMs = Distribution(_benchmarkSamples, static sample => sample.RenderMs),
            presentMs = Distribution(_benchmarkSamples, static sample => sample.PresentMs),
            gpuTimingAvailable = _benchmarkGpuSamples.Count > 0,
            gpuTimingSamples = _benchmarkGpuSamples.Count,
            gpuMs = new
            {
                total = Distribution(_benchmarkGpuSamples, static sample => sample.TotalGpuNs / 1_000_000.0),
                path = Distribution(_benchmarkGpuSamples, static sample => sample.PathNs / 1_000_000.0),
                sdfRect = Distribution(_benchmarkGpuSamples, static sample => sample.SdfRectNs / 1_000_000.0),
                text = Distribution(_benchmarkGpuSamples, static sample => sample.TextNs / 1_000_000.0),
                bitmap = Distribution(_benchmarkGpuSamples, static sample => sample.BitmapNs / 1_000_000.0),
                other = Distribution(_benchmarkGpuSamples, static sample => sample.OtherNs / 1_000_000.0),
            },
            trend = new
            {
                earlyMedianMs = earlyMedian,
                lateMedianMs = lateMedian,
                changePercent = trendPercent,
            },
            managedMemory = new
            {
                startBytes = _benchmarkStartMemory,
                endBytes = endMemory,
                deltaBytes = endMemory - _benchmarkStartMemory,
                retainedAfterFullCollectionBytes = retainedMemory,
                retainedDeltaBytes = retainedMemory - _benchmarkStartMemory,
                allocatedBytes = endAllocatedBytes - _benchmarkStartAllocatedBytes,
            },
            gcCollections = new
            {
                gen0 = benchmarkGcCollections[0],
                gen1 = benchmarkGcCollections[1],
                gen2 = benchmarkGcCollections[2],
            },
            realizedContainers = _listBox?.Host?.Children.Count ?? 0,
            finalVerticalOffset = GetScrollViewer()?.VerticalOffset ?? 0,
        };

        try
        {
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            var outputPath = Path.GetFullPath(_options.OutputPath ?? "million-scroll-benchmark.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, json);
            Console.WriteLine(json);
            Console.WriteLine($"[MillionScroll] benchmark written to {outputPath}");
            SetValue(BenchmarkOutputPathPropertyKey, outputPath);
            SetValue(BenchmarkErrorPropertyKey, "");
            RaiseEvent(new RoutedEventArgs(BenchmarkCompletedEvent, this));
            if (ShutdownOnBenchmarkCompletion) Application.Current?.Shutdown(sampleCount > 0 ? 0 : 2);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MillionScroll] failed to write benchmark: {ex}");
            SetValue(BenchmarkErrorPropertyKey, ex.Message);
            RaiseEvent(new RoutedEventArgs(BenchmarkCompletedEvent, this));
            if (ShutdownOnBenchmarkCompletion) Application.Current?.Shutdown(2);
        }
    }

    private static object Distribution<T>(
        IReadOnlyList<T> samples,
        Func<T, double> selector) => new
    {
        p50 = Percentile(samples, 0, samples.Count, 0.50, selector),
        p95 = Percentile(samples, 0, samples.Count, 0.95, selector),
        p99 = Percentile(samples, 0, samples.Count, 0.99, selector),
        max = Percentile(samples, 0, samples.Count, 1.00, selector),
    };

    private static double Percentile<T>(
        IReadOnlyList<T> samples,
        int start,
        int count,
        double percentile,
        Func<T, double> selector)
    {
        if (count <= 0)
        {
            return 0;
        }

        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = selector(samples[start + i]);
        }
        Array.Sort(values);
        var index = (int)Math.Ceiling(Math.Clamp(percentile, 0, 1) * values.Length) - 1;
        return values[Math.Clamp(index, 0, values.Length - 1)];
    }

    private void RefreshHud()
    {
        var scroller = GetScrollViewer();
        var host = _listBox?.Host;
        var realized = host?.Children.Count ?? 0;
        var first = int.MaxValue;
        var last = -1;

        if (host != null)
        {
            foreach (var child in host.Children)
            {
                if (child is DependencyObject dependencyObject)
                {
                    var index = _listBox!.ItemContainerGenerator.IndexFromContainer(dependencyObject);
                    if (index >= 0)
                    {
                        first = Math.Min(first, index);
                        last = Math.Max(last, index);
                    }
                }
            }
        }

        SetValue(RowsTextPropertyKey, FormatInt(_rowCount));
        SetValue(RealizedTextPropertyKey, realized.ToString(CultureInfo.InvariantCulture));
        SetValue(RangeTextPropertyKey, last >= 0 ? $"{FormatInt(first + 1)} - {FormatInt(last + 1)}" : "-");

        SetValue(OffsetTextPropertyKey, scroller == null ? "-" : $"{scroller.VerticalOffset:0} / {scroller.ScrollableHeight:0}");

        SetValue(MemoryTextPropertyKey, $"{GC.GetTotalMemory(false) / 1024.0 / 1024.0:0.0} MB");
        UpdateSelectionText();
        StartBenchmarkIfRequested();
    }

    private void UpdateSelectionText()
    {
        SetValue(SelectedTextPropertyKey, _listBox?.SelectedItem is MillionRow row ? $"Selected row {FormatInt(row.RowNumber)}" : "Selected row -");
    }

    private void UpdateSpeedText() => SetValue(SpeedTextPropertyKey, $"{Math.Round(ScrollSpeed):0} px/tick");

    private ScrollViewer? GetScrollViewer()
    {
        if (_listBox is null) return null;
        _scrollViewer ??= FindDescendant<ScrollViewer>(_listBox);
        if (_scrollViewer is not null && _restorePending && _scrollViewer.ViewportHeight > 0 && _scrollViewer.ExtentHeight > 0)
        {
            _restorePending = false; _scrollViewer.ScrollToVerticalOffset(_retainedOffset);
        }
        return _scrollViewer;
    }

    private static T? FindDescendant<T>(Visual root) where T : Visual
    {
        for (var i = 0; i < root.VisualChildrenCount; i++)
        {
            var child = root.GetVisualChild(i);
            if (child is T match)
            {
                return match;
            }

            if (child != null)
            {
                var descendant = FindDescendant<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }
        }

        return null;
    }

    private static string FormatInt(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>Exposes the actual realized-items host for the lab's virtualization metrics.</summary>
public class MillionScrollListBox : ListBox
{
    public Panel? Host => ItemsHost;
    public void ScrollIndexIntoView(int index)
    {
        if ((uint)index >= (uint)Items.Count) return;
        // Use the virtualization engine's index API; object lookup would scan and
        // box rows from the lazy million-item source just to find this index.
        if (ItemsHost is VirtualizingPanel panel) panel.BringIndexIntoViewPublic(index);
        else ScrollIntoView(Items[index]);
    }
}

/// <summary>Reusable label/value presentation for the performance sidebar.</summary>
public class MillionScrollMetric : HeaderedContentControl
{
    public MillionScrollMetric() { MillionScrollTheme.Register(); DefaultStyleKey = typeof(MillionScrollMetric); }
}
