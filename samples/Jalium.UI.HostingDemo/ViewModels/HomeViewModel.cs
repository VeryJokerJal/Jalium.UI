using Jalium.UI.HostingDemo.Services;
using Microsoft.Extensions.Hosting;

namespace Jalium.UI.HostingDemo.ViewModels;

/// <summary>
/// 首页 VM。构造时由 DI 注入 <see cref="IGreeter"/> 和 <see cref="IHostEnvironment"/>,
/// 展示 <c>builder.Services</c> + <c>builder.Environment</c> 两条链路的终点。
/// </summary>
public sealed class HomeViewModel : ViewModelBase
{
    public HomeViewModel(IGreeter greeter, IHostEnvironment environment)
    {
        Greeting = greeter.Hello("开发者");
        EnvironmentName = environment.EnvironmentName;
        ApplicationName = environment.ApplicationName;
        ContentRoot = environment.ContentRootPath;
    }

    public string Greeting { get; }
    public string EnvironmentName { get; }
    public string ApplicationName { get; }
    public string ContentRoot { get; }
}
