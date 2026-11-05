using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Hosting;

namespace Jalium.UI.TransparentBackdropDemo;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var builder = AppBuilder.CreateBuilder(args);
        builder.ConfigureApplication(app => app.MainWindow = new MainWindow());
        using var host = builder.Build();
        return host.Run();
    }
}

