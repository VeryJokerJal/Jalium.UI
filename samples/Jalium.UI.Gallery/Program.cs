using Jalium.UI;
using Jalium.UI.Controls;
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
        // Pick the GPU backend explicitly so captures are deterministic. Honor
        // JALIUM_RENDER_BACKEND=vulkan to validate the Vulkan backend (default D3D12).
        var backendEnv = Environment.GetEnvironmentVariable("JALIUM_RENDER_BACKEND");
        var selectedBackend = string.Equals(backendEnv, "vulkan", StringComparison.OrdinalIgnoreCase)
            ? RenderBackend.Vulkan
            : RenderBackend.D3D12;
        var renderContext = RenderContext.GetOrCreateCurrent(selectedBackend);
        renderContext.DefaultRenderingEngine = RenderingEngine.Impeller;

        var builder = AppBuilder.CreateBuilder(args);
        builder.ConfigureApplication(app => app.MainWindow = GalleryWindow.Build());

        using var host = builder.Build();
        host.UseDeveloperTools();
        return host.Run();
    }
}
