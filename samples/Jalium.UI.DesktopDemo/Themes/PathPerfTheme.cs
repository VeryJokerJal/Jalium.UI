using Jalium.UI.Controls.Themes;

namespace Jalium.UI.DesktopDemo.Themes;

public partial class PathPerfTheme : ResourceDictionary
{
    private static readonly Lazy<PathPerfTheme> Shared = new(() => new PathPerfTheme());
    public PathPerfTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
    public static Style WindowStyle { get { Register(); return (Style)Shared.Value["PathPerf.Window"]!; } }
}
