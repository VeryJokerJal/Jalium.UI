using Jalium.UI.HostingDemo.ViewModels;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>DI page whose default content is defined by the hosting theme.</summary>
public sealed class MvvmPage : HostingDemoPage
{
    public MvvmPage(MvvmViewModel vm)
    {
        Title = "MVVM";
        DataContext = vm;
    }
}
