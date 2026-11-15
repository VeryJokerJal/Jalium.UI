using Jalium.UI;

namespace Jalium.UI.LinuxDemo;

internal static class Program
{
    private static int Main(string[] args)
    {
        var builder = AppBuilder.CreateBuilder(new AppBuilderSettings
        {
            Args = args,
            DisableDefaults = true,
        });

        builder.ConfigureApplication(app =>
        {
            app.MainWindow = new LinuxDemoWindow();
        });

        using var host = builder.Build();
        return host.Run();
    }

}
