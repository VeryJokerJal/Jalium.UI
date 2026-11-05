using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Hosting;

namespace Jalium.UI.BorderlessDemo;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // WPF 风格：子类化 Application、重写 OnStartup/OnExit/OnSessionEnding。
        // JaliumApp.UseApplication<T>() 负责在 Run() 前通过无参构造创建实例。
        var builder = AppBuilder.CreateBuilder(args);
        //builder.UseJaliumMetrics();

        using var app = builder.Build();
        app.UseApplication<BorderlessDemoApp>();
        return app.Run();
    }
}

/// <summary>
/// 演示 Application 生命周期重写：OnStartup / OnExit / OnSessionEnding。
/// </summary>
public class BorderlessDemoApp : Application
{
    private DateTime _startedAt;

    protected override void OnStartup(StartupEventArgs e)
    {
        _startedAt = DateTime.Now;
        System.Diagnostics.Debug.WriteLine($"[OnStartup] args=[{string.Join(", ", e.Args)}]");
        System.Diagnostics.Debug.WriteLine($"[OnStartup] started at {_startedAt:HH:mm:ss.fff}");

        MainWindow = new BorderlessMainWindow();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var elapsed = DateTime.Now - _startedAt;
        System.Diagnostics.Debug.WriteLine($"[OnExit] exitCode={e.ApplicationExitCode}, elapsed={elapsed.TotalSeconds:F2}s");

        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[OnSessionEnding] reason={e.ReasonSessionEnding}");
        base.OnSessionEnding(e);
    }
}

