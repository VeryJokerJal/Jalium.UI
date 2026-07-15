using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ShellThemeTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void PageAndFrame_ImplicitThemeStyles_ShouldPreserveExpectedBackgrounds()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var page = new Page();
            var frame = new Frame();
            var host = new StackPanel { Width = 320, Height = 160 };
            host.Children.Add(page);
            host.Children.Add(frame);

            host.Measure(new Size(320, 160));
            host.Arrange(new Rect(0, 0, 320, 160));

            Assert.True(app.Resources.TryGetValue(typeof(Page), out var pageStyleObj));
            Assert.True(app.Resources.TryGetValue(typeof(Frame), out var frameStyleObj));
            Assert.IsType<Style>(pageStyleObj);
            Assert.IsType<Style>(frameStyleObj);

            Assert.False(page.HasLocalValue(Control.BackgroundProperty));
            Assert.False(frame.HasLocalValue(Control.BackgroundProperty));
            Assert.Null(page.Background);
            Assert.NotNull(frame.Background);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Window_ImplicitThemeStyle_ShouldApplyWithoutLocalBackgroundOverride()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var window = new Window();
            app.MainWindow = window;

            window.Measure(new Size(320, 200));
            window.Arrange(new Rect(0, 0, 320, 200));

            Assert.True(app.Resources.TryGetValue(typeof(Window), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(window.HasLocalValue(Control.BackgroundProperty));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TabControls_ImplicitThemeStyles_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var tabControl = new TabControl();
            var tabItem = new TabItem { Header = "General" };
            tabControl.Items.Add(tabItem);

            var host = new StackPanel { Width = 360, Height = 160 };
            host.Children.Add(tabControl);
            host.Measure(new Size(360, 160));
            host.Arrange(new Rect(0, 0, 360, 160));

            Assert.False(tabControl.HasLocalValue(Control.BackgroundProperty));
            Assert.False(tabControl.HasLocalValue(TabControl.TabStripBackgroundProperty));
            Assert.False(tabItem.HasLocalValue(Control.ForegroundProperty));
            Assert.False(tabItem.HasLocalValue(Control.PaddingProperty));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TabControls_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var tabStripBackground = Assert.IsAssignableFrom<Brush>(app.Resources["TabStripBackground"]);
            var tabStripBorder = Assert.IsAssignableFrom<Brush>(app.Resources["TabStripBorder"]);
            var selectedBackground = Assert.IsAssignableFrom<Brush>(app.Resources["TabItemSelectedBackground"]);
            var hoverBackground = Assert.IsAssignableFrom<Brush>(app.Resources["TabItemHoverBackground"]);
            var primaryText = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);
            var secondaryText = Assert.IsAssignableFrom<Brush>(app.Resources["TextSecondary"]);
            var indicatorBrush = Assert.IsAssignableFrom<Brush>(app.Resources["TabItemIndicator"]);

            var tabControl = new TabControl();
            var tabItem = new TabItem { Header = "General" };
            tabControl.Items.Add(tabItem);

            var host = new StackPanel { Width = 360, Height = 160 };
            host.Children.Add(tabControl);
            host.Measure(new Size(360, 160));
            host.Arrange(new Rect(0, 0, 360, 160));

            Assert.Same(tabStripBackground, InvokePrivateBrushResolver(tabControl, "ResolveTabStripBackground"));
            Assert.Same(tabStripBorder, InvokePrivateBrushResolver(tabControl, "ResolveTabStripBorderBrush"));
            Assert.Same(selectedBackground, InvokePrivateBrushResolver(tabItem, "ResolveSelectedBackground"));
            Assert.Same(hoverBackground, InvokePrivateBrushResolver(tabItem, "ResolveHoverBackground"));
            Assert.Same(primaryText, InvokePrivateBrushResolver(tabItem, "ResolvePrimaryTextBrush"));
            Assert.Same(secondaryText, InvokePrivateBrushResolver(tabItem, "ResolveSecondaryTextBrush"));
            Assert.Same(indicatorBrush, InvokePrivateBrushResolver(tabItem, "ResolveIndicatorBrush"));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Brush InvokePrivateBrushResolver(Control control, string methodName)
    {
        var method = control.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(control, null));
    }
}
