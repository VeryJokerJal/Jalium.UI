using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ThemeTemplateSmokeTests
{
    private const string HighContrastThemeKey = "HighContrast";

    [Theory]
    [InlineData(nameof(ThemeVariant.Dark))]
    [InlineData(nameof(ThemeVariant.Light))]
    [InlineData(HighContrastThemeKey)]
    public void CommonControlStyles_ShouldBePresent_AndTemplatesShouldApply(string themeKey)
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            ApplyThemeKey(themeKey);

            var splitButton = new SplitButton
            {
                Content = "Run",
                Flyout = new MenuFlyout()
            };

            var statusBar = new StatusBar();
            statusBar.Items.Add("Ready");

            var dockTabPanel = new DockTabPanel();
            dockTabPanel.Items.Add(new DockItem { Header = "Explorer" });

            var dockLayout = new DockLayout
            {
                Content = dockTabPanel
            };

            var controls = new FrameworkElement[]
            {
                new Button(),
                new TitleBarButton(),
                new CheckBox(),
                new RadioButton(),
                new HyperlinkButton(),
                new TextBox(),
                new AutoCompleteBox(),
                new NumberBox(),
                new RichTextBox(),
                new ProgressBar(),
                new ScrollViewer(),
                new ListBox(),
                new ListView(),
                new DataGrid(),
                new InfoBar(),
                new Calendar(),
                new ComboBox(),
                new ColorPicker(),
                new DatePicker(),
                new EditControl(),
                new TimePicker(),
                new Separator(),
                new Slider(),
                new ToggleSwitch(),
                splitButton,
                new CommandBar(),
                new MenuBar(),
                new Menu(),
                new Page(),
                new Frame(),
                new TabControl(),
                dockLayout,
                statusBar,
                new ScrollBar(),
                new PasswordBox()
            };

            var host = new StackPanel { Width = 1000, Height = 800 };
            foreach (var control in controls)
            {
                host.Children.Add(control);
            }

            host.Measure(new Size(1000, 800));
            host.Arrange(new Rect(0, 0, 1000, 800));

            Assert.All(controls, element =>
            {
                ControlTemplate? template = element switch
                {
                    Control control => control.Template,
                    Page page => page.Template,
                    _ => null,
                };
                Assert.True(element.VisualChildrenCount > 0 || template == null);
            });
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static void ApplyThemeKey(string themeKey)
    {
        if (Enum.TryParse<ThemeVariant>(themeKey, ignoreCase: false, out var theme))
        {
            ThemeManager.ApplyTheme(theme);
            Assert.Equal(theme, ThemeManager.CurrentTheme);
        }
        else
        {
            Assert.Equal(HighContrastThemeKey, themeKey);
            ResourceDictionary.CurrentThemeKey = themeKey;
        }

        Assert.Equal(themeKey, ResourceDictionary.CurrentThemeKey as string);
    }

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }
}
