using Jalium.UI.Controls.Themes;

namespace Jalium.UI.DesktopDemo.Themes;

public partial class ImagePerfTheme : ResourceDictionary
{
    private static readonly Lazy<ImagePerfTheme> Shared = new(() => new ImagePerfTheme());
    public ImagePerfTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
    public static Style WindowStyle { get { Register(); return (Style)Shared.Value["ImagePerf.Window"]!; } }
}
