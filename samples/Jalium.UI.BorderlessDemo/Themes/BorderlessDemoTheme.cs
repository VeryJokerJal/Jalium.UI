using Jalium.UI.Controls.Themes;

namespace Jalium.UI.BorderlessDemo.Themes;

/// <summary>Generated theme builders keep the sample's default UI in markup.</summary>
public partial class BorderlessDemoTheme : ResourceDictionary
{
    private static readonly Lazy<BorderlessDemoTheme> Shared = new(() => new BorderlessDemoTheme());
    public BorderlessDemoTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
    public static Style WindowStyle { get { Register(); return (Style)Shared.Value["BorderlessDemo.Window"]!; } }
}
