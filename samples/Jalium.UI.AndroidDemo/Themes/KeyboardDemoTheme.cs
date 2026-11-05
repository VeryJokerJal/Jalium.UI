using Jalium.UI.Controls.Themes;

namespace Jalium.UI.AndroidDemo.Themes;

public partial class KeyboardDemoTheme : ResourceDictionary
{
    private static readonly Lazy<KeyboardDemoTheme> Shared = new(() => new KeyboardDemoTheme());
    public KeyboardDemoTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
    public static Style WindowStyle { get { Register(); return (Style)Shared.Value["KeyboardDemo.Window"]!; } }
}
