using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class SplitButtonContentRoutingTests : IDisposable
{
    public SplitButtonContentRoutingTests()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        _ = new Application();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingTemplate_ContentClicksReachTheCurrentPrimaryButton(bool contentBeforeTemplate)
    {
        var split = new SplitButton { Flyout = new MenuFlyout() };
        var host = new Grid { Width = 240, Height = 48 };
        host.Children.Add(split);
        Layout(host);
        var clicks = 0;
        split.Click += (_, _) => clicks++;

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var oldPrimary = Assert.IsType<Button>(split.FindName("PrimaryButton"));
            var label = new TextBlock { Text = $"Run {iteration}" };
            var content = new StackPanel();
            content.Children.Add(label);
            if (contentBeforeTemplate) split.Content = content;

            split.Template = CreateTemplate();
            if (!contentBeforeTemplate) split.Content = content;
            Layout(host);

            var primary = Assert.IsType<Button>(split.FindName("PrimaryButton"));
            Assert.NotSame(oldPrimary, primary);
            Assert.True(IsDescendantOf(label, primary), "Content is still parented to a retired template.");
            Assert.True(IsDescendantOf(label, host));
            ClickContent(label);
            Assert.Equal(iteration + 1, clicks);
        }
    }

    [Fact]
    public void ClearingTemplate_ThenSettingContent_DoesNotLetRetiredPartsClaimIt()
    {
        var split = new SplitButton();
        var host = new Grid { Width = 240, Height = 48 };
        host.Children.Add(split);
        Layout(host);

        split.Template = null;
        var label = new TextBlock { Text = "Restart" };
        split.Content = label;
        split.Template = CreateTemplate();
        Layout(host);

        var clicks = 0;
        split.Click += (_, _) => clicks++;
        Assert.True(IsDescendantOf(label, split));
        ClickContent(label);
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void ReplacingOneSharedTemplate_DoesNotDisconnectAnotherSplitButton()
    {
        var template = CreateTemplate();
        var firstLabel = new TextBlock { Text = "First" };
        var secondLabel = new TextBlock { Text = "Second" };
        var first = new SplitButton { Template = template, Content = firstLabel };
        var second = new SplitButton { Template = template, Content = secondLabel };
        var host = new StackPanel { Width = 240, Height = 96 };
        host.Children.Add(first);
        host.Children.Add(second);
        Layout(host);

        first.Template = CreateTemplate();
        var replacement = new TextBlock { Text = "Second updated" };
        second.Content = replacement;
        Layout(host);

        var clicks = 0;
        second.Click += (_, _) => clicks++;
        Assert.True(IsDescendantOf(replacement, second));
        ClickContent(replacement);
        Assert.Equal(1, clicks);
    }

    private static ControlTemplate CreateTemplate() => Assert.IsType<ControlTemplate>(XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.jalium.ui/2024" TargetType="SplitButton">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="28" />
                </Grid.ColumnDefinitions>
                <Button Name="PrimaryButton" Content="{TemplateBinding Content}" />
                <Button Name="SecondaryButton" Grid.Column="1" Content="v" />
            </Grid>
        </ControlTemplate>
        """));

    private static void Layout(FrameworkElement host)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            host.Measure(new Size(host.Width, host.Height));
            host.Arrange(new Rect(0, 0, host.Width, host.Height));
        }
    }

    private static bool IsDescendantOf(Visual visual, Visual ancestor)
    {
        for (Visual? current = visual; current != null; current = current.VisualParent)
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    private static void ClickContent(UIElement content)
    {
        content.RaiseEvent(Mouse(UIElement.MouseDownEvent, new Point(5, 5), MouseButtonState.Pressed));
        var captured = Assert.IsType<Button>(UIElement.MouseCapturedElement);
        captured.RaiseEvent(Mouse(UIElement.MouseUpEvent, new Point(5, 5), MouseButtonState.Released));
    }

    private static MouseButtonEventArgs Mouse(RoutedEvent routedEvent, Point point, MouseButtonState state) =>
        new(routedEvent, point, MouseButton.Left, state, 1, state,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, ModifierKeys.None, 0);

    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
    }

    public void Dispose()
    {
        UIElement.MouseCapturedElement?.ReleaseMouseCapture();
        ResetApplicationState();
    }
}
