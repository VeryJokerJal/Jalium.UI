using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class StoryboardDesignerSeekTests
{
    [Fact]
    public void XamlImplicitTimelineAndKeyframeChildrenParticipateInPausedSeeking()
    {
        var storyboard = Assert.IsType<Storyboard>(XamlReader.Parse("""
            <Storyboard xmlns="http://schemas.jalium.ui/2024">
                <DoubleAnimationUsingKeyFrames Storyboard.TargetProperty="Opacity" Duration="0:0:1.2">
                    <LinearDoubleKeyFrame KeyTime="0:0:0" Value="0.15" />
                    <LinearDoubleKeyFrame KeyTime="0:0:0.6" Value="0.8" />
                    <LinearDoubleKeyFrame KeyTime="0:0:1.2" Value="0.15" />
                </DoubleAnimationUsingKeyFrames>
            </Storyboard>
            """));
        var animation = Assert.IsType<DoubleAnimationUsingKeyFrames>(Assert.Single(storyboard.Children));
        Assert.Equal(3, animation.KeyFrames.Count);
        var target = new Border { Opacity = .15 }; Storyboard.SetTarget(animation, target);
        try
        {
            storyboard.Begin(target, true); storyboard.Pause();
            AnimationEngineTests.RunInsideFrame(System.Diagnostics.Stopwatch.GetTimestamp(), _ => storyboard.Seek(TimeSpan.FromSeconds(.6)));
            Assert.Equal(.8, target.Opacity, 6);
            storyboard.Remove(); Assert.Equal(.15, target.Opacity, 6);
        }
        finally { storyboard.Remove(); }
    }

    [Fact]
    public void PausedElementSeekImmediatelyUpdatesThePreviewAndRemoveRestoresItsBase()
    {
        var target = new Border { Opacity = .2 };
        var animation = new DoubleAnimation { From = .2, To = 1, Duration = TimeSpan.FromSeconds(2) };
        Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
        var storyboard = new Storyboard(); storyboard.Children.Add(animation);
        try
        {
            storyboard.Begin(target, true); storyboard.Pause();
            AnimationEngineTests.RunInsideFrame(System.Diagnostics.Stopwatch.GetTimestamp(), _ => storyboard.Seek(TimeSpan.FromSeconds(1)));
            Assert.True(storyboard.GetIsPaused()); Assert.Equal(.6, target.Opacity, 6);
            AnimationEngineTests.RunInsideFrame(System.Diagnostics.Stopwatch.GetTimestamp(), _ => storyboard.Seek(TimeSpan.FromSeconds(.5)));
            Assert.Equal(.4, target.Opacity, 6);
            storyboard.Remove(); Assert.Equal(.2, target.Opacity, 6);
        }
        finally { storyboard.Remove(); }
    }

    [Fact]
    public void PausedNonElementSeekUpdatesTransformWithoutAFrameAndPreservesStopRestoration()
    {
        var owner = new Border(); var target = new TranslateTransform { X = 2 };
        var animation = new DoubleAnimation { From = 2, To = 10, Duration = TimeSpan.FromSeconds(2) };
        Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, new PropertyPath(TranslateTransform.XProperty));
        var storyboard = new Storyboard(); storyboard.Children.Add(animation);
        try
        {
            storyboard.Begin(owner, true); storyboard.Pause();
            AnimationEngineTests.RunInsideFrame(System.Diagnostics.Stopwatch.GetTimestamp(), _ => storyboard.SeekAlignedToLastTick(TimeSpan.FromSeconds(1)));
            Assert.True(storyboard.GetIsPaused()); Assert.Equal(6, target.X, 6);
            storyboard.Remove(); Assert.Equal(2, target.X, 6);
        }
        finally { storyboard.Remove(); }
    }
}
