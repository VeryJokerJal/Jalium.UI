using Jalium.UI.Controls.Themes;

namespace Jalium.UI.DesktopDemo.Themes;

public partial class HoverProbeTheme : ResourceDictionary
{
    private static readonly Lazy<HoverProbeTheme> Shared = new(() => new HoverProbeTheme());
    public HoverProbeTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
    public static Style WindowStyle { get { Register(); return (Style)Shared.Value["HoverProbe.Window"]!; } }
}
