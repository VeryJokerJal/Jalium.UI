using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jalium.UI.HostingDemo.Services;

/// <summary>
/// 典型的 <see cref="IHostedService"/>:由 <c>JaliumApp.Run()</c> 启动时自动 <c>StartAsync</c>,
/// 退出时自动 <c>StopAsync</c>。演示后台任务(每秒 tick 一次,把当前时间发到 <see cref="TickEvent"/>)。
/// 通过 <c>builder.Services.AddHostedService&lt;ClockHostedService&gt;()</c> 注册。
/// </summary>
public sealed class ClockHostedService : BackgroundService
{
    private readonly ILogger<ClockHostedService> _logger;

    /// <summary>每秒触发一次,参数是当前时间。UI 订阅该事件刷新显示。</summary>
    public event EventHandler<DateTime>? Tick;

    public ClockHostedService(ILogger<ClockHostedService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ClockHostedService 已启动");
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Tick?.Invoke(this, DateTime.Now);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关机路径
        }
        _logger.LogInformation("ClockHostedService 已停止");
    }
}
