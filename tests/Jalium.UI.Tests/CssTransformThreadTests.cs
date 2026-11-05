using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssTransformThreadTests
{
    [Fact]
    public void CachedInlineTransformChain_CanBeRenderedOnAnotherDispatcher()
    {
        var declaration = $"transform: rotate(-4deg) scale(1.05); --thread-test: {Guid.NewGuid():N}";
        Transform? first = null;
        Matrix expected = default;
        Exception? failure = null;
        var source = new Thread(() =>
        {
            try
            {
                var border = new Border();
                Css.SetStyle(border, declaration);
                first = border.RenderTransform;
                expected = first!.Value;
            }
            catch (Exception error) { failure = error; }
        });
        source.Start();
        Assert.True(source.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
        Assert.IsType<TransformGroup>(first);

        Transform? second = null;
        Matrix actual = default;
        var destination = new Thread(() =>
        {
            try
            {
                var border = new Border();
                Css.SetStyle(border, declaration);
                second = border.RenderTransform;
                actual = second!.Value;
            }
            catch (Exception error) { failure = error; }
        });
        destination.Start();
        Assert.True(destination.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
        Assert.Same(first, second);
        Assert.True(first!.IsFrozen);
        Assert.Equal(expected, actual);
    }
}
