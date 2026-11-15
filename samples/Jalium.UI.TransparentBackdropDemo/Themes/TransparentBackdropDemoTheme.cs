using Jalium.UI.Controls.Themes;

namespace Jalium.UI.TransparentBackdropDemo.Themes;

public partial class TransparentBackdropDemoTheme : ResourceDictionary
{
    private static readonly Lazy<TransparentBackdropDemoTheme> Shared = new(() => new TransparentBackdropDemoTheme());
    public TransparentBackdropDemoTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
}
