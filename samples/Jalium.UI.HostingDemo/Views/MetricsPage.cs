using Jalium.UI.HostingDemo.ViewModels;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>DI page whose default content is defined by the hosting theme.</summary>
public sealed class MetricsPage : HostingDemoPage
{
    public MetricsPage(MetricsViewModel vm)
    {
        Title = "Metrics";
        DataContext = vm;
        Loaded += (_, _) => vm.Start();
        Unloaded += (_, _) => vm.Stop();
    }
}
