using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CommandBarTests
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
    public void CommandBar_SecondaryCommandsChanges_ShouldRefreshMoreButtonVisibility()
    {
        ResetApplicationState();
        _ = new Application();

        try
        {
            var commandBar = new CommandBar();
            var host = MeasureInHost(commandBar);
            var moreButton = Assert.IsType<Button>(commandBar.FindName("PART_MoreButton"));

            Assert.Equal(Visibility.Collapsed, moreButton.Visibility);

            commandBar.SecondaryCommands.Add(new AppBarButton { Label = "Settings" });
            host.Measure(new Size(400, 80));
            host.Arrange(new Rect(0, 0, 400, 80));

            Assert.Equal(Visibility.Visible, moreButton.Visibility);

            commandBar.SecondaryCommands.Clear();
            host.Measure(new Size(400, 80));
            host.Arrange(new Rect(0, 0, 400, 80));

            Assert.Equal(Visibility.Collapsed, moreButton.Visibility);
            Assert.False(commandBar.IsOpen);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void CommandBar_OverflowPopupClosedExternally_ShouldResetIsOpen()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var commandBar = new CommandBar();
            commandBar.SecondaryCommands.Add(new AppBarButton { Label = "Settings" });
            _ = MeasureInWindow(app, commandBar);

            commandBar.IsOpen = true;

            var popup = Assert.IsType<Popup>(commandBar.FindName("PART_OverflowPopup"));
            Assert.True(popup.IsOpen);

            popup.IsOpen = false;

            Assert.False(commandBar.IsOpen);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void CommandBar_OverflowChrome_ShouldTrackControlThemeProperties()
    {
        ResetApplicationState();
        _ = new Application();

        try
        {
            var background = new SolidColorBrush(Color.FromRgb(10, 20, 30));
            var borderBrush = new SolidColorBrush(Color.FromRgb(40, 50, 60));

            var commandBar = new CommandBar
            {
                Background = background,
                BorderBrush = borderBrush
            };
            commandBar.SecondaryCommands.Add(new AppBarButton { Label = "Settings" });

            _ = MeasureInHost(commandBar);

            var overflowBorder = Assert.IsAssignableFrom<Control>(commandBar.FindName("PART_OverflowBorder"));
            Assert.Same(background, overflowBorder.Background);
            Assert.Same(borderBrush, overflowBorder.BorderBrush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void CommandBar_TemplateBindings_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var commandBarBackground = Assert.IsAssignableFrom<Brush>(app.Resources["CommandBarBackground"]);
            var overflowBackground = Assert.IsAssignableFrom<Brush>(app.Resources["CommandBarOverflowBackground"]);
            var commandBarBorder = Assert.IsAssignableFrom<Brush>(app.Resources["CommandBarBorderBrush"]);

            var commandBar = new CommandBar();

            _ = MeasureInHost(commandBar);
            var border = Assert.IsType<ContentControl>(commandBar.FindName("PART_OverflowBorder"));
            Assert.Same(commandBarBackground, commandBar.Background);
            Assert.Same(commandBar.Background, border.Background);
            Assert.Same(commandBarBorder, border.BorderBrush);
            commandBar.OverflowBackground = overflowBackground;
            Assert.Same(overflowBackground, border.Background);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static StackPanel MeasureInHost(CommandBar commandBar)
    {
        var host = new StackPanel { Width = 400, Height = 80 };
        host.Children.Add(commandBar);
        host.Measure(new Size(400, 80));
        host.Arrange(new Rect(0, 0, 400, 80));
        return host;
    }

    private static Window MeasureInWindow(Application app, CommandBar commandBar)
    {
        var host = new StackPanel { Width = 400, Height = 80 };
        host.Children.Add(commandBar);

        var window = new DisplayedTestWindow
        {
            Width = 400,
            Height = 80,
            Content = host
        };
        app.MainWindow = window;

        window.Measure(new Size(400, 80));
        window.Arrange(new Rect(0, 0, 400, 80));
        return window;
    }

}
