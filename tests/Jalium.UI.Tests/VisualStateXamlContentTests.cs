using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class VisualStateXamlContentTests
{
    [Fact]
    public void ImplicitStateStoryboardAnimatesItsActualTemplateTarget()
    {
        var button = Assert.IsType<Button>(XamlReader.Parse("""
            <Button xmlns="http://schemas.jalium.ui/2024" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Width="360" Height="220">
                <Button.Template><ControlTemplate TargetType="Button">
                    <Border x:Name="Frame">
                        <VisualStateManager.VisualStateGroups>
                            <VisualStateGroup x:Name="CommonStates">
                                <VisualState x:Name="Normal" />
                                <VisualState x:Name="Highlighted">
                                    <Storyboard><DoubleAnimation Storyboard.TargetName="Highlight" Storyboard.TargetProperty="Opacity" To="1" Duration="0:0:0.18" /></Storyboard>
                                </VisualState>
                            </VisualStateGroup>
                        </VisualStateManager.VisualStateGroups>
                        <Border x:Name="Highlight" Opacity="0.15" />
                    </Border>
                </ControlTemplate></Button.Template>
            </Button>
            """));
        var window = new Window { Content = button, Width = 360, Height = 220, IsShowTitleBar = false };
        button.ApplyTemplate(); window.Measure(new Size(360, 220)); window.Arrange(new Rect(0, 0, 360, 220));
        var root = Assert.IsType<Border>(button.Template!.FindName("Frame", button));
        var target = Assert.IsType<Border>(button.Template.FindName("Highlight", button));
        var group = Assert.IsType<VisualStateGroup>(Assert.Single(VisualStateManager.GetVisualStateGroups(root)!.Cast<object>()));
        var storyboard = Assert.IsType<Storyboard>(group.States.OfType<VisualState>().Single(s => s.Name == "Highlighted").Storyboard);
        var animation = Assert.IsType<DoubleAnimation>(Assert.Single(storyboard.Children));
        Assert.Equal(1d, animation.To);
        try
        {
            Assert.True(VisualStateManager.GoToState(button, "Highlighted", false));
            Assert.Same(target, button.Template.FindName("Highlight", button));
            storyboard.Pause();
            AnimationEngineTests.RunInsideFrame(System.Diagnostics.Stopwatch.GetTimestamp(), _ => storyboard.Seek(TimeSpan.FromSeconds(.18)));
            Assert.True(Math.Abs(1 - target.Opacity) < .000001,
                $"state={group.CurrentState?.Name}, clock={storyboard.GetCurrentState()}, time={storyboard.GetCurrentTime()}, progress={storyboard.GetCurrentProgress()}, opacity={target.Opacity}, animated={target.HasAnimatedProperties}");
            Assert.True(VisualStateManager.GoToState(button, "Normal", false));
            Assert.Equal(.15, target.Opacity, 6);
        }
        finally { storyboard.Remove(); window.Content = null; }
    }

    [Fact]
    public void ImplicitTransitionStoryboardIsKeptByTheRuntimeReader()
    {
        var transition = Assert.IsType<VisualTransition>(XamlReader.Parse("""
            <VisualTransition xmlns="http://schemas.jalium.ui/2024" From="Normal" To="Highlighted">
                <Storyboard><DoubleAnimation Storyboard.TargetProperty="Opacity" To="1" Duration="0:0:0.12" /></Storyboard>
            </VisualTransition>
            """));
        Assert.Equal("Normal", transition.From); Assert.Equal("Highlighted", transition.To);
        Assert.IsType<DoubleAnimation>(Assert.Single(Assert.IsType<Storyboard>(transition.Storyboard).Children));
    }
}
