using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Diagnostics;
using Jalium.UI.Hosting;
using Jalium.UI.Interop;

namespace Jalium.UI.Gallery;

/// <summary>
/// Entry point for the Jalium.UI control gallery — a single scrollable page that
/// showcases every control in the framework. Used for README / marketing captures.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        StartupDiagnostics.Mark("MainEntered", blocksUiThread: true);

        // The gallery does not consume appsettings, environment configuration,
        // command-line configuration, or default logging providers during startup.
        // Keep the Generic Host for lifecycle/DI, but skip those unused defaults.
        var builder = AppBuilder.CreateBuilder(new AppBuilderSettings
        {
            Args = args,
            DisableDefaults = true,
        });
        builder.ConfigureApplication(app =>
        {
            // Application construction starts the shared GPU prewarm before this
            // callback. Preserve the gallery's D3D12/Vulkan + Impeller contract here,
            // after that work has overlapped theme initialization, rather than blocking
            // at the first line of Main.
            using (StartupDiagnostics.Begin(
                "Gallery.ConfigureRenderContext",
                blocksUiThread: true))
            {
                var backendEnv = Environment.GetEnvironmentVariable("JALIUM_RENDER_BACKEND");
                var selectedBackend = string.Equals(
                    backendEnv,
                    "vulkan",
                    StringComparison.OrdinalIgnoreCase)
                    ? RenderBackend.Vulkan
                    : RenderBackend.D3D12;
                var renderContext = RenderContext.GetOrCreateCurrent(selectedBackend);
                renderContext.DefaultRenderingEngine = RenderingEngine.Impeller;
            }

            using var mainWindow = StartupDiagnostics.Begin(
                "Gallery.BuildMainWindow",
                blocksUiThread: true);
            app.MainWindow = GalleryWindow.Build();
        });

        using var host = builder.Build();
        host.UseDeveloperTools();
        StartupDiagnostics.Mark("Gallery.RunEntering", blocksUiThread: true);
        return host.Run();
    }
}
