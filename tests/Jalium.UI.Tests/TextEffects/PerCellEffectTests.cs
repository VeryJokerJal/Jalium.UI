using System.Linq;
using Jalium.UI;
using Jalium.UI.Controls.TextEffects;
using Jalium.UI.Media.Effects;

namespace Jalium.UI.Tests.TextEffects;

/// <summary>
/// Verifies the <see cref="DrawingContext.PushEffect"/> / <see cref="DrawingContext.PopEffect"/>
/// integration inside <see cref="TextEffectPresenter"/>. These tests don't need a
/// real GPU context — they use <see cref="RecordingDrawingContext"/> to observe
/// call sequence.
/// </summary>
public class PerCellEffectTests
{
    /// <summary>
    /// Test effect that unconditionally writes a <see cref="BlurEffect"/> to
    /// <c>payload.PerCellEffect</c> on every cell, regardless of phase. Real
    /// effects should scope this by phase / cell — this is just a probe.
    /// </summary>
    private sealed class AlwaysPerCellBlur : ITextEffect
    {
        public double EnterDurationMs => 100;
        public double ShiftDurationMs => 100;
        public double ExitDurationMs => 100;

        public double GetStaggerDelayMs(int indexInBatch, int batchSize) => 0;

        public void Apply(in TextEffectFrameContext context, ref TextCellRenderPayload payload)
        {
            payload.PerCellEffect = new BlurEffect(2);
        }
    }

    [Fact]
    public void DrawingContext_PushEffect_DefaultsToNoOp()
    {
        // The base class contract: any DrawingContext that doesn't opt-in
        // swallows PushEffect/PopEffect silently, so headless / test / PDF
        // export contexts never break on a caller that uses them.
        var dc = new RecordingDrawingContext();
        var stubEffect = new BlurEffect(1);

        // The recording context overrides PushEffect to record the call;
        // so instead, test the contract on a separate minimal subclass that
        // leaves the default implementation intact.
        var noopDc = new NoOpDrawingContext();
        noopDc.PushEffect(stubEffect, new Rect(0, 0, 10, 10));
        noopDc.PopEffect();

        // No exception, no state change.
        Assert.True(true);
        _ = dc;
    }

    [Fact]
    public void RenderCell_WithoutPerCellEffect_DoesNotCallPushEffect()
    {
        var p = new TextEffectPresenter
        {
            IsAnimationEnabled = false,
            Text = "ab",
        };
        p.Measure(new Size(400, 100));
        p.Arrange(new Rect(0, 0, 400, 100));

        var dc = new RecordingDrawingContext();
        p.RenderForTesting(dc);

        Assert.Equal(0, dc.PushEffectCount);
        Assert.Equal(0, dc.PopEffectCount);
        Assert.Equal(2, dc.DrawTextCount);
    }

    [Fact]
    public void RenderCell_WithPerCellEffect_PushesAndPopsOncePerCell()
    {
        var p = new TextEffectPresenter
        {
            IsAnimationEnabled = false,
            Text = "abc",
            TextEffect = new AlwaysPerCellBlur(),
        };
        p.Measure(new Size(400, 100));
        p.Arrange(new Rect(0, 0, 400, 100));

        var dc = new RecordingDrawingContext();
        p.RenderForTesting(dc);

        // 3 cells, 3 PushEffects, 3 PopEffects.
        Assert.Equal(3, dc.PushEffectCount);
        Assert.Equal(3, dc.PopEffectCount);
        Assert.All(dc.PushedEffects, e => Assert.IsType<BlurEffect>(e));
    }

    [Fact]
    public void RenderCell_PushPopEffect_BracketsTheDraw()
    {
        // Each cell's call sequence must be: PushEffect → Push(transform/opacity)*
        // → DrawText → Pop* → PopEffect. Getting the order wrong means the
        // capture scope doesn't cover the draw.
        var p = new TextEffectPresenter
        {
            IsAnimationEnabled = false,
            Text = "a",
            TextEffect = new AlwaysPerCellBlur(),
        };
        p.Measure(new Size(400, 100));
        p.Arrange(new Rect(0, 0, 400, 100));

        var dc = new RecordingDrawingContext();
        p.RenderForTesting(dc);

        var events = dc.Events;
        var pushEffectIdx = events.FindIndex(e => e.StartsWith("PushEffect"));
        var drawTextIdx = events.FindIndex(e => e.StartsWith("DrawText"));
        var popEffectIdx = events.FindIndex(e => e == "PopEffect");

        Assert.True(pushEffectIdx >= 0, "Expected a PushEffect call");
        Assert.True(drawTextIdx > pushEffectIdx, $"DrawText at {drawTextIdx} must follow PushEffect at {pushEffectIdx}");
        Assert.True(popEffectIdx > drawTextIdx, $"PopEffect at {popEffectIdx} must follow DrawText at {drawTextIdx}");
    }

    [Fact]
    public void RenderCell_EffectWithHasEffectFalse_IsSkipped()
    {
        // An effect reporting HasEffect == false should NOT trigger a capture
        // (avoids paying for an offscreen render when the effect wouldn't do
        // anything anyway).
        var p = new TextEffectPresenter
        {
            IsAnimationEnabled = false,
            Text = "ab",
            TextEffect = new IneffectiveEffect(),
        };
        p.Measure(new Size(400, 100));
        p.Arrange(new Rect(0, 0, 400, 100));

        var dc = new RecordingDrawingContext();
        p.RenderForTesting(dc);

        Assert.Equal(0, dc.PushEffectCount);
        Assert.Equal(2, dc.DrawTextCount); // draws still happen
    }

    private sealed class IneffectiveEffect : ITextEffect
    {
        public double EnterDurationMs => 100;
        public double ShiftDurationMs => 100;
        public double ExitDurationMs => 100;
        public double GetStaggerDelayMs(int i, int n) => 0;
        public void Apply(in TextEffectFrameContext context, ref TextCellRenderPayload payload)
        {
            payload.PerCellEffect = new NullEffect(); // HasEffect = false
        }
    }

    /// <summary>Effect that reports <see cref="IEffect.HasEffect"/> == false.</summary>
    private sealed class NullEffect : IEffect
    {
        public bool HasEffect => false;
        public int EffectTypeId => 0;
        public Thickness EffectPadding => Thickness.Zero;
        public event EventHandler? EffectChanged { add { _ = value; } remove { _ = value; } }
    }

    /// <summary>A DrawingContext that keeps the default PushEffect/PopEffect implementations.</summary>
    private sealed class NoOpDrawingContext : Jalium.UI.Media.DrawingContext
    {
        public override void Close() { }
        public override void DrawLine(Jalium.UI.Media.Pen pen, Point p0, Point p1) { }
        public override void DrawRectangle(Jalium.UI.Media.Brush? brush, Jalium.UI.Media.Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Jalium.UI.Media.Brush? brush, Jalium.UI.Media.Pen? pen, Rect rectangle, double rx, double ry) { }
        public override void DrawEllipse(Jalium.UI.Media.Brush? brush, Jalium.UI.Media.Pen? pen, Point center, double rx, double ry) { }
        public override void DrawText(Jalium.UI.Media.FormattedText formattedText, Point origin) { }
        public override void DrawGeometry(Jalium.UI.Media.Brush? brush, Jalium.UI.Media.Pen? pen, Jalium.UI.Media.Geometry geometry) { }
        public override void DrawImage(Jalium.UI.Media.ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, Jalium.UI.IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Jalium.UI.Media.Transform transform) { }
        public override void PushClip(Jalium.UI.Media.Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
    }
}
