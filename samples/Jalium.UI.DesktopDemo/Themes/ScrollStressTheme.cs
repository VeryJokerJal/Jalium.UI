using Jalium.UI.Controls.Themes;

namespace Jalium.UI.DesktopDemo.Themes;

public partial class ScrollStressTheme : ResourceDictionary
{
    private static readonly Lazy<ScrollStressTheme> Shared = new(() => new ScrollStressTheme());
    public ScrollStressTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
    public static Style WindowStyle { get { Register(); return (Style)Shared.Value["ScrollStress.Window"]!; } }
}
