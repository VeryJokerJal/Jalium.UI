using Jalium.UI.HostingDemo.ViewModels;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>DI page whose default content is defined by the hosting theme.</summary>
public sealed class OptionsPage : HostingDemoPage
{
    public OptionsPage(OptionsViewModel vm)
    {
        Title = "Options";
        DataContext = vm;
    }
}
