using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation;
using Jalium.UI.Interop;
using Jalium.UI.Media;

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("This smoke test must run on Linux.");
    return 2;
}

_ = RenderContext.GetOrCreateCurrent(RenderBackend.Software);

var application = new Application();
var panel = new StackPanel
{
    Orientation = Orientation.Vertical,
    Margin = new Thickness(24),
};
var status = new TextBlock { Text = "AT-SPI smoke ready" };
AutomationProperties.SetAutomationId(status, "smoke-status");

var action = new Button { Content = "Run accessible action" };
AutomationProperties.SetName(action, "Smoke Action");
AutomationProperties.SetAutomationId(action, "smoke-action");

var editor = new TextBox { Text = "Editable smoke text" };
AutomationProperties.SetName(editor, "Smoke Editor");
AutomationProperties.SetAutomationId(editor, "smoke-editor");

var closeAction = new Button { Content = "Close smoke window" };
AutomationProperties.SetName(closeAction, "Close Smoke Window");
AutomationProperties.SetAutomationId(closeAction, "smoke-close");

panel.Children.Add(status);
panel.Children.Add(action);
panel.Children.Add(editor);
panel.Children.Add(closeAction);

action.Click += (_, _) =>
{
    string? marker = Environment.GetEnvironmentVariable("JALIUM_ATSPI_ACTION_MARKER");
    if (!string.IsNullOrWhiteSpace(marker))
        File.WriteAllText(marker, "invoked");

    var peer = action.GetAutomationPeer();
    AutomationProperties.SetName(action, "Smoke Action Invoked");
    peer?.RaisePropertyChangedEvent(
        AutomationProperty.NameProperty,
        "Smoke Action",
        "Smoke Action Invoked");

    panel.Children.Add(new TextBlock { Text = "Dynamic accessible child" });
};

var window = new Window
{
    Title = "Jalium AT-SPI Smoke",
    Width = 520,
    Height = 320,
    Content = panel,
};
AutomationProperties.SetAutomationId(window, "atspi-smoke-window");
closeAction.Click += (_, _) => window.Dispatcher.BeginInvoke(window.Close);

window.Shown += (_, _) => window.Dispatcher.BeginInvoke(() =>
{
    Console.WriteLine(
        $"AT-SPI status={LinuxAccessibility.AtSpiStatus}; " +
        $"active={LinuxAccessibility.IsAtSpiActive}; " +
        $"error={LinuxAccessibility.AtSpiLastError ?? "none"}; pid={Environment.ProcessId}");
});

int lifetimeSeconds = int.TryParse(
    Environment.GetEnvironmentVariable("JALIUM_ATSPI_SMOKE_SECONDS"),
    out int parsed)
    ? Math.Clamp(parsed, 5, 120)
    : 30;
var closer = new Thread(() =>
{
    Thread.Sleep(TimeSpan.FromSeconds(lifetimeSeconds));
    window.Dispatcher.BeginInvoke(window.Close);
})
{
    IsBackground = true,
    Name = "AT-SPI smoke timeout",
};
closer.Start();

application.MainWindow = window;
application.Run();
return 0;
