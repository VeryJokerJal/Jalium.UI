using Jalium.UI.Controls;
using Jalium.UI.HostingDemo.ViewModels;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>
/// 展示 <c>builder.Services</c> + <c>builder.Environment</c>:
/// 构造函数直接注入 <see cref="HomeViewModel"/>,<see cref="HomeViewModel"/> 又注入
/// <c>IGreeter</c>/<c>IHostEnvironment</c>,构造链一路走通即证明 DI 工作。
/// </summary>
public sealed class HomePage : Page
{
    public HomePage(HomeViewModel vm)
    {
        Title = "首页";
        DataContext = vm;

        Content = PageLayout.Build(
            "DI + Environment",
            "HomePage 通过 AddView<HomePage, HomeViewModel>() 注册;Frame.Navigate(typeof(HomePage)) 会走 IViewFactory,自动从 DI 构造 HomePage(注入 HomeViewModel)与 HomeViewModel(注入 IGreeter、IHostEnvironment)。",
            PageLayout.Body($"问候语: {vm.Greeting}"),
            PageLayout.Mono($"Environment.EnvironmentName = {vm.EnvironmentName}"),
            PageLayout.Mono($"Environment.ApplicationName = {vm.ApplicationName}"),
            PageLayout.Mono($"Environment.ContentRootPath = {vm.ContentRoot}"));
    }
}
