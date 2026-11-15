using Jalium.UI.Controls.Themes;

namespace Jalium.UI.DesktopDemo.Themes;

public partial class RotationReproTheme : ResourceDictionary
{
    private static readonly Lazy<RotationReproTheme> Shared = new(() => new RotationReproTheme());
    public RotationReproTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
    public static Style WindowStyle { get { Register(); return (Style)Shared.Value["RotationRepro.Window"]!; } }
}
