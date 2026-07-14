using Jalium.UI.Controls;
using Jalium.UI.HostingDemo.ViewModels;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>
/// 展示 <c>IHostedService</c> / <c>BackgroundService</c>:<see cref="Services.ClockHostedService"/>
/// 由 <c>builder.Services.AddHostedService&lt;ClockHostedService&gt;()</c> 注册,
/// <see cref="JaliumApp.Run"/> 进入 UI 消息循环前 <c>IHost.StartAsync</c> 已经把它启动。
/// </summary>
public sealed class HostedServicesPage : Page
{
    public HostedServicesPage(HostedServicesViewModel vm)
    {
        Title = "HostedService";
        DataContext = vm;

        var timeText = new TextBlock
        {
            Text = vm.CurrentTime,
            FontSize = 36,
            FontFamily = new Media.FontFamily("Consolas"),
            Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(249, 226, 175)),
            Margin = new Thickness(0, 8, 0, 8)
        };

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HostedServicesViewModel.CurrentTime))
            {
                timeText.Text = vm.CurrentTime;
            }
        };

        NavigatedFrom += (_, _) => vm.Dispose();

        Content = PageLayout.Build(
            "IHostedService · BackgroundService",
            "ClockHostedService 每秒 Tick 一次。时间源于后台线程,VM 通过 Application.Current.Dispatcher.Invoke 回到 UI 线程更新。这条链路证明:JaliumApp.Run() 先 StartAsync 所有 hosted service,再进入 Application.Run() 的消息循环,最后 Exit 时 StopAsync 干净关闭。",
            timeText);
    }
}
