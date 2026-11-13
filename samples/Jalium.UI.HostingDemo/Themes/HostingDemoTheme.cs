using Jalium.UI.Controls.Themes;

namespace Jalium.UI.HostingDemo.Themes;

public partial class HostingDemoTheme : ResourceDictionary
{
    private static readonly Lazy<HostingDemoTheme> Shared = new(() => new HostingDemoTheme());
    public HostingDemoTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
    public static Style WindowStyle { get { Register(); return (Style)Shared.Value["HostingDemo.Window"]!; } }
}
