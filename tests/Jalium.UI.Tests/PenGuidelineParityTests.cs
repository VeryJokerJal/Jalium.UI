using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

public sealed class PenGuidelineParityTests
{
    [Fact]
    public void PenUsesDependencyPropertiesAndWpfDefaults()
    {
        var pen = new Pen();
        Assert.IsAssignableFrom<Animatable>(pen);
        Assert.Null(pen.Brush);
        Assert.Equal(1, pen.Thickness);
        Assert.Equal(PenLineCap.Flat, pen.StartLineCap);
        Assert.Equal(PenLineCap.Flat, pen.EndLineCap);
        Assert.Equal(PenLineCap.Square, pen.DashCap);
        Assert.Equal(PenLineJoin.Miter, pen.LineJoin);
        Assert.Equal(10, pen.MiterLimit);
        Assert.Same(DashStyles.Solid, pen.DashStyle);

        pen.Thickness = -1;
        Assert.Equal(-1, pen.Thickness);
        pen.DashStyle = new DashStyle([2, 3], 1);
        Pen clone = pen.CloneCurrentValue();
        Assert.NotSame(pen, clone);
        Assert.NotSame(pen.DashStyle, clone.DashStyle);
        Assert.Equal([2d, 3d], clone.DashStyle.Dashes);
    }

    [Fact]
    public void DashStyleAndGuidelineSetAreCloneableAnimatables()
    {
        Assert.True(DashStyles.Solid.IsFrozen);
        Assert.True(DashStyles.Dash.IsFrozen);

        var guidelines = new GuidelineSet([1, 2], [3, 4]);
        Assert.IsAssignableFrom<Animatable>(guidelines);
        Assert.Equal([1d, 2d], guidelines.GuidelinesX);
        Assert.Equal([3d, 4d], guidelines.GuidelinesY);

        GuidelineSet clone = guidelines.Clone();
        Assert.NotSame(guidelines.GuidelinesX, clone.GuidelinesX);
        Assert.Equal(guidelines.GuidelinesX, clone.GuidelinesX);
        Assert.Same(GuidelineSet.GuidelinesXProperty,
            typeof(GuidelineSet).GetField(nameof(GuidelineSet.GuidelinesXProperty))!.GetValue(null));
    }
}
