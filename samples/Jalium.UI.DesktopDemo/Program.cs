using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Hosting;
using Jalium.UI.Interop;
using Jalium.UI.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jalium.UI.DesktopDemo;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        System.Diagnostics.Debug.WriteLine("[Demo] step 0: Main entered");

        // Auto = honour JALIUM_RENDER_BACKEND (vulkan/d3d12/software); the
        // platform default order still picks D3D12 on Windows when unset.
        var renderContext = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
        // JALIUM_RENDERING_ENGINE=vello switches the demo to the Vello compute
        // engine; anything else keeps the Impeller default.
        renderContext.DefaultRenderingEngine =
            string.Equals(Environment.GetEnvironmentVariable("JALIUM_RENDERING_ENGINE"), "vello",
                          StringComparison.OrdinalIgnoreCase)
                ? RenderingEngine.Vello
                : RenderingEngine.Impeller;
        System.Diagnostics.Debug.WriteLine($"[Demo] step 1: render ctx backend={renderContext.Backend} engine={renderContext.DefaultRenderingEngine}");

        var builder = AppBuilder.CreateBuilder(args);
        System.Diagnostics.Debug.WriteLine("[Demo] step 2: builder created");

        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Services.AddSingleton<ToastNotificationHost>(_ => new ToastNotificationHost
        {
            Position = ToastPosition.TopRight,
            MaxVisibleToasts = 5,
            ToastWidth = 350
        });

        builder.ConfigureApplication(app =>
        {
            System.Diagnostics.Debug.WriteLine($"[Demo] step 3a: ConfigureApplication, app type={app.GetType().FullName}");
            SystemNotificationManager.Current.Initialize("Jalium.UI.DesktopDemo", "Jalium Desktop Demo");
            System.Diagnostics.Debug.WriteLine("[Demo] step 3b: SystemNotificationManager initialized");

            var toastHost = app.Services!.GetRequiredService<ToastNotificationHost>();
            ToastService.SetHost(toastHost);
            System.Diagnostics.Debug.WriteLine("[Demo] step 3c: toast host wired");

            System.Diagnostics.Debug.WriteLine("[Demo] step 3d: building main window...");
            app.MainWindow =
                Environment.GetEnvironmentVariable("JALIUM_DEMO_WINDOW")?.ToLowerInvariant() switch
                {
                    "vellotest" => VelloTestWindow.Build(),
                    "pathperf" => PathPerfWindow.Build(),
                    "hoverprobe" => HoverProbeWindow.Build(),
                    "rotation" => RotationReproWindow.Build(),
                    "imageperf" => ImagePerfWindow.Build(),
                    "scrollstress" => ScrollStressWindow.Build(),
                    "notifications" => BuildMainWindow(toastHost),
                    _ => EffectReproWindow.Build(),
                };
            System.Diagnostics.Debug.WriteLine($"[Demo] step 3e: MainWindow set, title='{app.MainWindow?.Title}'");
        });

        System.Diagnostics.Debug.WriteLine("[Demo] step 4: about to Build host");
        using var host = builder.Build();
        System.Diagnostics.Debug.WriteLine("[Demo] step 5: host built; about to host.Run()");
        host.UseDeveloperTools();
        var exit = host.Run();
        System.Diagnostics.Debug.WriteLine($"[Demo] step 6: host.Run() returned exit={exit}");
        return exit;
    }

    private static Window BuildMainWindow(ToastNotificationHost toastHost) => new()
    {
        Title = "Jalium.UI 通知测试", Width = 600, Height = 500,
        Style = Themes.NotificationDemoTheme.WindowStyle,
        Content = new NotificationDemoSurface { ToastHost = toastHost }
    };
}
