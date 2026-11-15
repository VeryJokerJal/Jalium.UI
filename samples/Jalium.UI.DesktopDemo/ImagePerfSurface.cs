using Jalium.UI.Controls;
using Jalium.UI.DesktopDemo.Themes;
using Jalium.UI.Diagnostics;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Threading;

namespace Jalium.UI.DesktopDemo;

/// <summary>Image-cache workload with shared bitmap data and replaceable category/card templates.</summary>
[TemplatePart(Name = "PART_CategoryA", Type = typeof(Button))]
[TemplatePart(Name = "PART_CategoryB", Type = typeof(Button))]
[TemplatePart(Name = "PART_CategoryC", Type = typeof(Button))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Ready", GroupName = "DataStates")]
[TemplateVisualState(Name = "Empty", GroupName = "DataStates")]
[TemplateVisualState(Name = "CategoryA", GroupName = "CategoryStates")]
[TemplateVisualState(Name = "CategoryB", GroupName = "CategoryStates")]
[TemplateVisualState(Name = "CategoryC", GroupName = "CategoryStates")]
public class ImagePerfSurface : ItemsControl
{
    public static readonly DependencyProperty SourceImagesProperty = DependencyProperty.Register(nameof(SourceImages), typeof(IReadOnlyList<BitmapImage>), typeof(ImagePerfSurface), new PropertyMetadata(null, OnSourcesChanged));
    public static readonly DependencyProperty CardsPerCategoryProperty = DependencyProperty.Register(nameof(CardsPerCategory), typeof(int), typeof(ImagePerfSurface), new PropertyMetadata(31, OnSourcesChanged), value => value is int count && count > 0 && count <= 10_000);
    public static readonly DependencyProperty SelectedCategoryProperty = DependencyProperty.Register(nameof(SelectedCategory), typeof(int), typeof(ImagePerfSurface), new PropertyMetadata(0, OnCategoryChanged), value => value is int index && index >= 0 && index <= 2);
    public static readonly DependencyProperty IsMonitoringProperty = DependencyProperty.Register(nameof(IsMonitoring), typeof(bool), typeof(ImagePerfSurface), new PropertyMetadata(true, OnMonitoringChanged));
    private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(StatusText), typeof(string), typeof(ImagePerfSurface), new PropertyMetadata("5 种 800×800 BitmapImage × 31 卡片,每张显示 168×190。打开 F3 看 GpuRes / Cache 指标。"));
    public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;
    public IReadOnlyList<BitmapImage>? SourceImages { get => (IReadOnlyList<BitmapImage>?)GetValue(SourceImagesProperty); set => SetValue(SourceImagesProperty, value); }
    public int CardsPerCategory { get => (int)GetValue(CardsPerCategoryProperty)!; set => SetValue(CardsPerCategoryProperty, value); }
    public int SelectedCategory { get => (int)GetValue(SelectedCategoryProperty)!; set => SetValue(SelectedCategoryProperty, value); }
    public bool IsMonitoring { get => (bool)GetValue(IsMonitoringProperty)!; set => SetValue(IsMonitoringProperty, value); }
    public string StatusText => (string)GetValue(StatusTextProperty)!;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private IReadOnlyList<BitmapImage>[] _categories = [Array.Empty<BitmapImage>(), Array.Empty<BitmapImage>(), Array.Empty<BitmapImage>()];
    private Button? _a, _b, _c;

    public ImagePerfSurface()
    {
        ImagePerfTheme.Register(); SetCurrentValue(SourceImagesProperty, BuildSyntheticImages(5));
        _timer.Tick += (_, _) => RefreshStatistics();
        Loaded += (_, _) => { RenderDiagnostics.ApiStatsEnabled = true; UpdateTimer(); };
        Unloaded += (_, _) => _timer.Stop(); DefaultStyleKey = typeof(ImagePerfSurface);
    }
    public override void OnApplyTemplate()
    {
        ReleaseParts(); base.OnApplyTemplate();
        _a = GetTemplateChild("PART_CategoryA") as Button; if (_a is not null) _a.Click += OnA;
        _b = GetTemplateChild("PART_CategoryB") as Button; if (_b is not null) _b.Click += OnB;
        _c = GetTemplateChild("PART_CategoryC") as Button; if (_c is not null) _c.Click += OnC;
        UpdateStates(); UpdateTimer();
    }
    protected override void OnTemplateChanging() { _timer.Stop(); ReleaseParts(); base.OnTemplateChanging(); }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateStates(); }
    public void SelectCategory(int index) => SetCurrentValue(SelectedCategoryProperty, index);
    public void RefreshStatistics()
    {
        if (RenderDiagnostics.LatestBitmapUploadStats is not { } stats) return;
        long total = stats.UploadCount + stats.FastPathHits; double fast = total == 0 ? 0 : stats.FastPathHits * 100d / total;
        SetValue(StatusTextPropertyKey, $"Uploads {stats.UploadCount}  ({stats.UploadBytes / 1024d / 1024d:F1} MB)   FastPath {fast:F0}%   Memcmp {stats.MemcmpShortCircuits}   GpuRes Δ{stats.GpuResidentBytes / 1024d / 1024d:+0.00;-0.00;0} MB   CacheEvict {stats.CacheEvictions}");
    }
    private static void OnSourcesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ImagePerfSurface)d).RebuildCategories();
    private static void OnCategoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ImagePerfSurface)d).Swap();
    private static void OnMonitoringChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ImagePerfSurface)d).UpdateTimer();
    private void RebuildCategories()
    {
        var sources = SourceImages;
        _categories = Enumerable.Range(0, 3).Select(seed => sources is { Count: > 0 }
            ? (IReadOnlyList<BitmapImage>)Enumerable.Range(0, CardsPerCategory).Select(index => sources[(index + seed) % sources.Count]).ToArray() : Array.Empty<BitmapImage>()).ToArray();
        Swap();
    }
    private void Swap() { SetCurrentValue(ItemsSourceProperty, _categories[SelectedCategory]); UpdateStates(); }
    private void OnA(object sender, RoutedEventArgs e) { if (IsEnabled) SelectCategory(0); }
    private void OnB(object sender, RoutedEventArgs e) { if (IsEnabled) SelectCategory(1); }
    private void OnC(object sender, RoutedEventArgs e) { if (IsEnabled) SelectCategory(2); }
    private void ReleaseParts()
    {
        if (_a is not null) _a.Click -= OnA; if (_b is not null) _b.Click -= OnB; if (_c is not null) _c.Click -= OnC;
        _a = _b = _c = null;
    }
    private void UpdateTimer() { if (IsLoaded && IsMonitoring) _timer.Start(); else _timer.Stop(); }
    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, SourceImages is { Count: > 0 } ? "Ready" : "Empty", false);
        VisualStateManager.GoToState(this, SelectedCategory switch { 0 => "CategoryA", 1 => "CategoryB", _ => "CategoryC" }, false);
    }
    private static List<BitmapImage> BuildSyntheticImages(int count)
    {
        var result = new List<BitmapImage>(count);
        for (int i = 0; i < count; ++i)
        {
            const int W = 800, H = 800;
            var pixels = new byte[W * H * 4];
            byte baseR = (byte)(80 + i * 30);
            byte baseG = (byte)(120 + i * 25);
            byte baseB = (byte)(160 + i * 20);
            for (int y = 0; y < H; ++y)
            {
                for (int x = 0; x < W; ++x)
                {
                    int o = (y * W + x) * 4;
                    pixels[o]     = (byte)((baseB + x / 4) & 0xff);
                    pixels[o + 1] = (byte)((baseG + y / 4) & 0xff);
                    pixels[o + 2] = (byte)((baseR + (x + y) / 8) & 0xff);
                    pixels[o + 3] = 255;
                }
            }
            result.Add(BitmapImage.FromPixels(pixels, W, H));
        }
        return result;
    }

}
