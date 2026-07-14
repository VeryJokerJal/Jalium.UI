using Jalium.UI.Controls;
using Jalium.UI.Hosting;
using Jalium.UI.HostingDemo.ViewModels;
using Microsoft.Extensions.Logging;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>
/// 展示 <c>builder.Logging</c>:VM 注入 <see cref="Microsoft.Extensions.Logging.ILogger{TCategoryName}"/>,
/// 点击按钮按不同等级写日志。同时读 <c>Application.Current.GetLogger&lt;LoggingPage&gt;()</c>
/// 证明 <see cref="Hosting.JaliumHostingExtensions.GetLogger{T}(Application)"/> 扩展可用。
/// </summary>
public sealed class LoggingPage : Page
{
    public LoggingPage(LoggingViewModel vm)
    {
        Title = "Logging";
        DataContext = vm;

        var lastText = PageLayout.Body(vm.LastEntry);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoggingViewModel.LastEntry))
            {
                lastText.Text = vm.LastEntry;
            }
        };

        // 从 Application.Current 拿 logger —— 也可以 via VM 注入,这里仅展示扩展方法
        var pageLogger = Application.Current?.GetLogger<LoggingPage>();
        pageLogger?.LogInformation("LoggingPage 已创建");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(PageLayout.DemoButton("LogTrace", vm.LogTrace));
        buttons.Children.Add(PageLayout.DemoButton("LogDebug", vm.LogDebug));
        buttons.Children.Add(PageLayout.DemoButton("LogInformation", vm.LogInformation));
        buttons.Children.Add(PageLayout.DemoButton("LogWarning", vm.LogWarning));
        buttons.Children.Add(PageLayout.DemoButton("LogError (exception)", vm.LogError));

        Content = PageLayout.Build(
            "Logging · ILogger<T>",
            "点击按钮后,看启动该 demo 的控制台。appsettings.json 里把 Jalium.UI.HostingDemo 的最小级别设为 Debug,所以 Trace 会被过滤,Debug 及以上会输出。",
            buttons,
            lastText);
    }
}
