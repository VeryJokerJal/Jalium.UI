using Jalium.UI.Controls;
using Jalium.UI.Media.Effects;

namespace Jalium.UI.Tests;

public class EffectCaptureBoundsTests
{
    [Fact]
    public void Render_WithElementEffect_SnapsCaptureBoundsToPixelGrid()
    {
        var border = new Border
        {
            Width = 100,
            Height = 50,
            Effect = new BlurEffect(2.4)
        };

        border.Measure(new Size(100, 50));
        border.Arrange(new Rect(0, 0, 100, 50));

        var context = new RecordingEffectContext
        {
            Offset = new Point(10.25, 20.75)
        };

        border.Render(context);

        Assert.Single(context.BeginCalls);
        Assert.Single(context.ApplyCalls);

        var begin = context.BeginCalls[0];
        var apply = context.ApplyCalls[0];

        Assert.Equal(7f, begin.X);
        Assert.Equal(18f, begin.Y);
        Assert.Equal(106f, begin.Width);
        Assert.Equal(56f, begin.Height);

        Assert.Equal(begin, apply);
    }

    private sealed class RecordingEffectContext : IOffsetDrawingContext, IEffectDrawingContext
    {
        public Point Offset { get; set; }

        public List<CaptureBounds> BeginCalls { get; } = [];
        public List<CaptureBounds> ApplyCalls { get; } = [];

        public void BeginEffectCapture(float x, float y, float w, float h)
        {
            BeginCalls.Add(new CaptureBounds(x, y, w, h));
        }

        public void EndEffectCapture()
        {
        }

        public void ApplyElementEffect(IEffect effect, float x, float y, float w, float h,
            float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
        {
            ApplyCalls.Add(new CaptureBounds(x, y, w, h));
        }

        public void ApplyElementEffect(IEffect effect, float x, float y, float w, float h, float captureOriginX = 0, float captureOriginY = 0, float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
        {
            // 这里可以添加效果应用的逻辑
            // 例如，使用 effect 对象的属性来修改元素的外观
            // 具体实现取决于 IEffect 接口的定义和效果类型
            // 这只是一个示例，实际逻辑可能会有所不同
            // 例如：
            // effect.Apply(x, y, w, h, cornerTL, cornerTR, cornerBR, cornerBL);
        }
    }

    private readonly record struct CaptureBounds(float X, float Y, float Width, float Height);
}
