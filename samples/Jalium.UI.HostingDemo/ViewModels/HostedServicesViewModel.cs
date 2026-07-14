using Jalium.UI.HostingDemo.Services;

namespace Jalium.UI.HostingDemo.ViewModels;

/// <summary>
/// 展示 <c>IHostedService</c> / <c>BackgroundService</c>:构造函数注入框架启动的
/// <see cref="ClockHostedService"/>,订阅其 Tick 事件,VM 持续刷新当前时间 —— 证明
/// <see cref="JaliumApp.Run"/> 进入 UI 消息循环之前就把 hosted service 启动了。
/// </summary>
public sealed class HostedServicesViewModel : ViewModelBase, IDisposable
{
    private readonly ClockHostedService _clock;
    private string _currentTime = "等待第一次 tick...";

    public HostedServicesViewModel(ClockHostedService clock)
    {
        _clock = clock;
        _clock.Tick += OnTick;
    }

    public string CurrentTime
    {
        get => _currentTime;
        private set => SetProperty(ref _currentTime, value);
    }

    private void OnTick(object? sender, DateTime e)
    {
        // Tick 来自 BackgroundService 线程;Dispatcher 把更新路由到 UI 线程。
        Application.Current?.Dispatcher.Invoke(() =>
        {
            CurrentTime = e.ToString("yyyy-MM-dd HH:mm:ss");
        });
    }

    public void Dispose()
    {
        _clock.Tick -= OnTick;
    }
}
