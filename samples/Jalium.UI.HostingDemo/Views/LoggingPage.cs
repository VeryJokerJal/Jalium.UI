using Jalium.UI.HostingDemo.ViewModels;
using Jalium.UI.Hosting;
using Microsoft.Extensions.Logging;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>DI page whose default content is defined by the hosting theme.</summary>
public sealed class LoggingPage : HostingDemoPage
{
    public LoggingPage(LoggingViewModel vm)
    {
        Title = "Logging";
        DataContext = vm;
        Application.Current?.GetLogger<LoggingPage>()?.LogInformation("LoggingPage 已创建");
    }
}
