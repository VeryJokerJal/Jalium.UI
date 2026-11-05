using Jalium.UI.Controls;
using Jalium.UI.LinuxDemo.Themes;
using Jalium.UI.Threading;

namespace Jalium.UI.LinuxDemo;

/// <summary>Native Linux host; the UI is supplied by the themeable surface.</summary>
public class LinuxDemoWindow : Window
{
    private DispatcherTimer? _autoClose;
    public LinuxDemoWindow()
    {
        Title = "Jalium.UI on Linux";
        Width = 720; Height = 560; MinWidth = 420; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Style = LinuxDemoTheme.WindowStyle;
        Content = new LinuxDemoSurface();
        Loaded += OnLoaded;
        Closed += (_, _) => { _autoClose?.Stop(); _autoClose = null; };
    }
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("[demo] ready");
        if (_autoClose is null && Environment.GetEnvironmentVariable("JALIUM_DEMO_AUTOCLOSE_MS") is { } text && int.TryParse(text, out var delay) && delay > 0)
        {
            _autoClose = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
            _autoClose.Tick += (_, _) => { _autoClose!.Stop(); Console.WriteLine("[demo] auto-close"); Close(); };
            _autoClose.Start();
        }
    }
}
