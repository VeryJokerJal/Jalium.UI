using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using System.Windows.Input;
using Jalium.UI.Input;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class SplitButtonTests
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
    public void SplitButton_XamlFlyoutPropertyElement_ShouldPopulateMenuItems()
    {
        const string xaml = """
            <SplitButton xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         Content="Run">
                <SplitButton.Flyout>
                    <MenuFlyout>
                        <MenuFlyoutItem Text="Jalium.UI.Controls" />
                        <MenuFlyoutItem Text="Jalium.UI.Core" />
                    </MenuFlyout>
                </SplitButton.Flyout>
            </SplitButton>
            """;

        var splitButton = Assert.IsType<SplitButton>(XamlReader.Parse(xaml));
        var flyout = Assert.IsType<MenuFlyout>(splitButton.Flyout);

        Assert.Equal(2, flyout.Items.Count);
        Assert.Equal("Jalium.UI.Controls", Assert.IsType<MenuFlyoutItem>(flyout.Items[0]).Text);
        Assert.Equal("Jalium.UI.Core", Assert.IsType<MenuFlyoutItem>(flyout.Items[1]).Text);
    }

    [Fact]
    public void SplitButton_PrimaryButtonClick_ShouldRaiseClickAndExecuteCommand()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        _ = new Application();

        try
        {
            object? executedParameter = null;
            var command = new DelegateCommand(parameter => executedParameter = parameter);

            var splitButton = new SplitButton
            {
                Content = "Run",
                Command = command,
                CommandParameter = "payload",
                Flyout = new MenuFlyout()
            };

            var host = new StackPanel { Width = 300, Height = 80 };
            host.Children.Add(splitButton);
            host.Measure(new Size(300, 80));
            host.Arrange(new Rect(0, 0, 300, 80));

            var primaryButton = Assert.IsType<Button>(splitButton.FindName("PrimaryButton"));

            SplitButton? clickSender = null;
            SplitButtonClickEventArgs? clickArgs = null;
            splitButton.Click += (sender, args) =>
            {
                clickSender = sender;
                clickArgs = args;
            };

            primaryButton.PerformClick();

            Assert.Same(splitButton, clickSender);
            Assert.NotNull(clickArgs);
            Assert.Equal("payload", executedParameter);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void SplitButton_SecondaryButtonClick_ShouldOpenFlyout()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        _ = new Application();

        try
        {
            var flyout = new MenuFlyout();
            flyout.Items.Add(new MenuFlyoutItem { Text = "Run item" });

            var splitButton = new SplitButton
            {
                Content = "Run",
                Flyout = flyout
            };

            var host = new StackPanel { Width = 300, Height = 80 };
            host.Children.Add(splitButton);
            host.Measure(new Size(300, 80));
            host.Arrange(new Rect(0, 0, 300, 80));

            var secondaryButton = Assert.IsType<Button>(splitButton.FindName("SecondaryButton"));

            Assert.False(flyout.IsOpen);

            secondaryButton.PerformClick();

            Assert.True(flyout.IsOpen);

            secondaryButton.PerformClick();

            Assert.True(flyout.IsOpen);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void SplitButton_SpaceKey_ShouldRaiseClickAndExecuteCommand()
    {
        object? executedParameter = null;
        var command = new DelegateCommand(parameter => executedParameter = parameter);
        var splitButton = new SplitButton
        {
            Command = command,
            CommandParameter = "from-keyboard"
        };

        var clickCount = 0;
        splitButton.Click += (_, _) => clickCount++;

        splitButton.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Space, ModifierKeys.None, true, false, 0));
        splitButton.RaiseEvent(new KeyEventArgs(UIElement.KeyUpEvent, Key.Space, ModifierKeys.None, false, false, 1));

        Assert.Equal(1, clickCount);
        Assert.Equal("from-keyboard", executedParameter);
    }

    [Fact]
    public void SplitButton_ApiSurface_ShouldMatchWinUI()
    {
        var splitButtonType = typeof(SplitButton);

        Assert.NotNull(splitButtonType.GetProperty(nameof(SplitButton.Flyout), BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(splitButtonType.GetProperty(nameof(SplitButton.Command), BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(splitButtonType.GetProperty(nameof(SplitButton.CommandParameter), BindingFlags.Public | BindingFlags.Instance));

        Assert.NotNull(splitButtonType.GetField(nameof(SplitButton.FlyoutProperty), BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(splitButtonType.GetField(nameof(SplitButton.CommandProperty), BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(splitButtonType.GetField(nameof(SplitButton.CommandParameterProperty), BindingFlags.Public | BindingFlags.Static));

        Assert.Null(splitButtonType.GetProperty("IsFlyoutOpen", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(splitButtonType.GetField("IsFlyoutOpenProperty", BindingFlags.Public | BindingFlags.Static));
    }

    private sealed class DelegateCommand : ICommand
    {
        private readonly Action<object?> _execute;

        public DelegateCommand(Action<object?> execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            _execute(parameter);
        }
    }
}
