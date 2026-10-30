using Jalium.UI;
using Jalium.UI.Controls;

namespace Jalium.UI.MinimalWindowProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var application = new Application();
        // Keep the same complete-theme contract as MemoryProbe without its
        // in-process logging, module enumeration, or action scheduler.
        if (application.Resources.MergedDictionaries.Count < 3 ||
            !application.Resources.Contains(typeof(Button)))
        {
            throw new InvalidOperationException("The complete desktop theme was not loaded.");
        }
        var window = new Window
        {
            Title = "Jalium.UI Minimal Window Probe",
            Width = 800,
            Height = 600,
            Content = null,
        };

        return application.Run(window, args);
    }
}
