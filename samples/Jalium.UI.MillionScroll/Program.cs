using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Diagnostics;
using Jalium.UI.Hosting;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using SelectionMode = Jalium.UI.Controls.SelectionMode;

namespace Jalium.UI.MillionScroll;

internal static class Program
{
    private const int DefaultRowCount = 1_000_000;

    [STAThread]
    private static int Main(string[] args)
    {
        var options = MillionScrollRunOptions.Parse(args, DefaultRowCount);
        var renderContext = RenderContext.GetOrCreateCurrent(options.Backend);
        renderContext.DefaultRenderingEngine = options.Engine;

        var builder = AppBuilder.CreateBuilder(args);
        builder.ConfigureApplication(app => app.MainWindow = MillionScrollWindow.Build(
            options, renderContext.Backend, renderContext.DefaultRenderingEngine));

        using var host = builder.Build();
        host.UseDeveloperTools();
        return host.Run();
    }

}

