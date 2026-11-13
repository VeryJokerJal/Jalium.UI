using Jalium.UI.Controls.Themes;

namespace Jalium.UI.AotWindowDemo.Themes;

/// <summary>Uses generated resource builders so the AOT demo does not parse XAML at runtime.</summary>
public partial class AotDemoTheme : ResourceDictionary
{
    private static readonly Lazy<AotDemoTheme> Shared = new(() => new AotDemoTheme());
    public AotDemoTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
    public static Style WindowStyle { get { Register(); return (Style)Shared.Value["AotDemo.Window"]!; } }
}
