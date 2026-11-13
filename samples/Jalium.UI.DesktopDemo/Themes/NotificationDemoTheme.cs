using Jalium.UI.Controls.Themes;

namespace Jalium.UI.DesktopDemo.Themes;

public partial class NotificationDemoTheme : ResourceDictionary
{
    private static readonly Lazy<NotificationDemoTheme> Shared = new(() => new NotificationDemoTheme());
    public NotificationDemoTheme() => InitializeComponent();
    public static void Register() => ThemeManager.RegisterControlThemes(Shared.Value);
    public static Style WindowStyle { get { Register(); return (Style)Shared.Value["NotificationDemo.Window"]!; } }
}
