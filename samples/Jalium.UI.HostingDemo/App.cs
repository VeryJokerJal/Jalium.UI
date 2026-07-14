using Jalium.UI.Controls;
using Jalium.UI.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jalium.UI.HostingDemo;

/// <summary>
/// 自定义 <see cref="Application"/> 子类:通过 <c>builder.UseApplication&lt;App&gt;()</c>
/// 注册。演示 WPF 风格的 OnStartup/OnExit/OnSessionEnding 重写,同时利用
/// <see cref="Application.Services"/> 在 Application 级别访问 DI。
/// </summary>
public sealed class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var logger = this.GetLogger<App>();
        logger?.LogInformation("[App.OnStartup] args=[{Args}], env={Env}",
            string.Join(" ", e.Args),
            HostEnvironment?.EnvironmentName ?? "(none)");

        // 通过 IServiceProvider 解析出 MainWindow;MainWindow 构造时会注入 ILogger<MainWindow>。
        MainWindow = Services!.GetRequiredService<Views.MainWindow>();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        this.GetLogger<App>()?.LogInformation("[App.OnExit] exitCode={ExitCode}", e.ApplicationExitCode);
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        this.GetLogger<App>()?.LogWarning("[App.OnSessionEnding] reason={Reason}", e.ReasonSessionEnding);
        base.OnSessionEnding(e);
    }
}
