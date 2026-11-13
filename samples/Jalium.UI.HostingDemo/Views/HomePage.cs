using Jalium.UI.HostingDemo.ViewModels;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>DI page whose default content is defined by the hosting theme.</summary>
public sealed class HomePage : HostingDemoPage
{
    public HomePage(HomeViewModel vm)
    {
        Title = "首页";
        DataContext = vm;
    }
}
