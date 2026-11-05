using Jalium.UI.Hosting;
using Jalium.UI.Media;

namespace Jalium.UI.HostingDemo.ViewModels;

/// <summary>
/// 展示 <see cref="JaliumMeter"/> —— 订阅 <see cref="CompositionTarget.Rendering"/>
/// 每帧读取最新的 FPS / 帧时间,同时显示 Meter 对象本身的基础信息。
/// </summary>
public sealed class MetricsViewModel : ViewModelBase, IDisposable

{
    private double _fps;
    private double _frameMs;
    private int _refreshRate;

    private bool _active;

    public MetricsViewModel()
    {
        Start();
    }

    public double Fps { get => _fps; private set => SetProperty(ref _fps, value); }
    public double FrameMs { get => _frameMs; private set => SetProperty(ref _frameMs, value); }
    public int RefreshRate { get => _refreshRate; private set => SetProperty(ref _refreshRate, value); }

    public string MeterName => JaliumMeter.MeterName;
    public bool MeterRunning => JaliumMeter.IsRunning;
    public string MeterStatus => $"Meter: {MeterName} ({(MeterRunning ? "running" : "stopped")})";

    private void OnRendering(object? sender, EventArgs e)
    {
        Fps = JaliumMeter.CurrentFps;
        FrameMs = JaliumMeter.LastFrameDurationMs;
        RefreshRate = (int)Math.Round(Fps);
        Notify(nameof(MeterRunning));
        Notify(nameof(MeterStatus));
    }

    public void Start()
    {
        if (_active) return;
        _active = true; CompositionTarget.Rendering += OnRendering;
    }
    public void Stop()
    {
        if (!_active) return;
        _active = false; CompositionTarget.Rendering -= OnRendering;
    }
    public void Dispose() => Stop();
}
