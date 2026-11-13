using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.AotWindowDemo.Themes;

namespace Jalium.UI.AotWindowDemo;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var builder = AppBuilder.CreateBuilder(new AppBuilderSettings
        {
            Args = args,
            // Skip the Microsoft.Extensions.Hosting defaults (appsettings.json,
            // env-var configuration, default logging providers). The demo doesn't
            // need any of them and skipping keeps the AOT image lean.
            DisableDefaults = true,
        });

        builder.ConfigureApplication(app =>
        {
            app.MainWindow = BuildWindow();
        });

        using var host = builder.Build();
        return host.Run();
    }

    private static Window BuildWindow() => new()
    {
        Title = "Jalium.UI — NativeAOT single-exe demo",
        Width = 600,
        Height = 360,
        Style = AotDemoTheme.WindowStyle,
        Content = new AotCounterSurface(),
    };
}
