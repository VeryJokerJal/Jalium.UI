using Jalium.UI.Controls;
using Jalium.UI.HostingDemo.ViewModels;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>
/// 展示 <c>builder.UseJaliumMetrics()</c> 与 <c>JaliumMeter</c>:实时 FPS / 帧时间,
/// 以及 <see cref="MetricsViewModel"/> 直接读取静态值演示在任意代码里都能拿到。
/// </summary>
public sealed class MetricsPage : Page
{
    public MetricsPage(MetricsViewModel vm)
    {
        Title = "Metrics";
        DataContext = vm;

        var fpsText = new TextBlock
        {
            Text = "--",
            FontSize = 36,
            Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(166, 227, 161)),
            Margin = new Thickness(0, 4, 0, 4)
        };
        var frameText = PageLayout.Mono("Frame: -- ms");
        var refreshText = PageLayout.Mono("Refresh: -- Hz");
        var meterText = PageLayout.Mono("Meter: --");

        vm.PropertyChanged += (_, _) =>
        {
            fpsText.Text = $"{vm.Fps:F1} FPS";
            frameText.Text = $"Frame: {vm.FrameMs:F2} ms";
            refreshText.Text = $"Refresh: {vm.RefreshRate} Hz";
            meterText.Text = $"Meter: {vm.MeterName} ({(vm.MeterRunning ? "running" : "stopped")})";
        };

        NavigatedFrom += (_, _) => vm.Dispose();

        Content = PageLayout.Build(
            "Metrics · JaliumMeter",
            "builder.UseJaliumMetrics() 在 Application 启动后订阅 CompositionTarget.Rendering 做采样。JaliumMeter 本身是一个 System.Diagnostics.Metrics.Meter,可被 dotnet-counters、OpenTelemetry 等消费;指标名前缀 jalium.*。",
            fpsText,
            frameText,
            refreshText,
            meterText,
            PageLayout.Body("提示:在启动该 demo 的进程上运行 `dotnet-counters monitor --process-id <pid> Jalium.UI` 即可实时看到 jalium.frame.duration / jalium.fps 等。"));
    }
}
