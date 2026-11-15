using Jalium.UI.Controls.Themes;

namespace Jalium.UI.DesktopDemo.Themes;

public partial class EffectReproTheme : ResourceDictionary
{
    private static readonly Lazy<EffectReproTheme> Shared = new(() => new EffectReproTheme());
    public EffectReproTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
    public static Style WindowStyle { get { Register(); return (Style)Shared.Value["EffectRepro.Window"]!; } }
}
