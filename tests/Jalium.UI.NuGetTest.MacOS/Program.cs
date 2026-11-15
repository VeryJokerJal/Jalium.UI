using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.MacOS;

JaliumMacApplication.Initialize();
var app = new Application();
var window = new Window
{
    Title = "NuGet Package Test - macOS",
    Width = 500,
    Height = 300,
    Content = new TextBlock { Text = "Jalium.UI.MacOS", FontSize = 24 }
};
app.MainWindow = window;
window.Shown += (_, _) =>
{
    Console.WriteLine("[nuget-macos] ready");
    new Thread(() =>
    {
        Thread.Sleep(1500);
        window.Dispatcher.BeginInvoke((Action)window.Close);
    }) { IsBackground = true }.Start();
};
window.Closed += (_, _) => Console.WriteLine("[nuget-macos] completed");
return app.Run();
