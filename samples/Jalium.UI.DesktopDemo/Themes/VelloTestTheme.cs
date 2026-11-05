using Jalium.UI.Controls.Themes;

namespace Jalium.UI.DesktopDemo.Themes;

public partial class VelloTestTheme : ResourceDictionary
{
    private static readonly Lazy<VelloTestTheme> Shared = new(() => new VelloTestTheme());
    public VelloTestTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
    public static Style WindowStyle { get { Register(); return (Style)Shared.Value["VelloTest.Window"]!; } }
}
