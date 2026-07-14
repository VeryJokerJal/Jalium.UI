namespace Jalium.UI.Tests;

public sealed class AnimationTimelineCanonicalOwnershipTests
{
    [Fact]
    public void WpfAnimationTimelineNameHasOnePublicTypeIdentity()
    {
        Type[] exported = typeof(FrameworkElement).Assembly.GetExportedTypes();

        Assert.Contains(
            exported,
            type => type.FullName == "Jalium.UI.Media.Animation.AnimationTimeline");
        Assert.DoesNotContain(
            exported,
            type => type.FullName == "Jalium.UI.Media.Animation.AnimationTimeline`1");
        Assert.Contains(
            exported,
            type => type.FullName == "Jalium.UI.Media.Animation.TypedAnimationTimeline`1");
    }
}
