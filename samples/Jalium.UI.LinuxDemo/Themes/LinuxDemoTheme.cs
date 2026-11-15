using Jalium.UI.Controls.Themes;

namespace Jalium.UI.LinuxDemo.Themes;

public partial class LinuxDemoTheme : ResourceDictionary
{
    private static readonly Lazy<LinuxDemoTheme> Shared = new(() => new LinuxDemoTheme());
    public LinuxDemoTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
    public static Style WindowStyle { get { Register(); return (Style)Shared.Value["LinuxDemo.Window"]!; } }
}
