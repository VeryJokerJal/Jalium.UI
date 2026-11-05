using Jalium.UI.Controls.Themes;

namespace Jalium.UI.MillionScroll.Themes;

public partial class MillionScrollTheme : ResourceDictionary
{
    private static readonly Lazy<MillionScrollTheme> Shared = new(() => new MillionScrollTheme());
    public MillionScrollTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
    public static Style WindowStyle { get { Register(); return (Style)Shared.Value["MillionScroll.Window"]!; } }
}
