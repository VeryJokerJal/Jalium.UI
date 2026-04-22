using Jalium.UI;
using Jalium.UI.Controls.TextEffects;
using Jalium.UI.Controls.TextEffects.Effects;
using Jalium.UI.Media.Effects;

namespace Jalium.UI.Tests.TextEffects;

public class RiseSettleEffectTests
{
    private static TextEffectCell MakeCell(double width = 10, double lineHeight = 20)
    {
        // Internal ctor is accessible via InternalsVisibleTo.
        var cell = new TextEffectCell(id: 1, text: "a", batchId: 0, indexInBatch: 0, batchSize: 1);
        cell.Bounds = new Rect(0, 0, width, lineHeight);
        cell.LineHeight = lineHeight;
        return cell;
    }

    private static TextEffectFrameContext MakeCtx(
        TextEffectCell cell,
        TextEffectCellPhase phase,
        double progress)
    {
        return new TextEffectFrameContext(
            cell,
            phase,
            progress,
            timeInPhaseMs: progress * 500,
            totalTimeMs: progress * 500,
            presenterSize: new Size(200, 40));
    }

    #region Entering phase

    [Fact]
    public void Enter_AtStart_CellIsBelowTargetAndBlurred()
    {
        var effect = new RiseSettleEffect();
        var cell = MakeCell(lineHeight: 20);
        var ctx = MakeCtx(cell, TextEffectCellPhase.Entering, progress: 0.0);
        var payload = TextCellRenderPayload.Identity;

        effect.Apply(in ctx, ref payload);

        // At progress 0: full rise distance below, blur at RiseBlurPx via
        // payload.BlurRadius (CPU sampled path — see RiseSettleEffect comment
        // for why per-cell GPU blur was abandoned), zero opacity.
        Assert.True(payload.TranslateY > 0, "Should start below target");
        Assert.Equal(effect.RiseDistance * 20.0, payload.TranslateY, precision: 5);
        Assert.Equal(effect.RiseBlurPx, payload.BlurRadius, precision: 3);
        Assert.Null(payload.PerCellEffect);
        Assert.Equal(0.0, payload.Opacity, precision: 3);
    }

    [Fact]
    public void Enter_AtEnd_SettlesToIdentity()
    {
        var effect = new RiseSettleEffect();
        var cell = MakeCell(lineHeight: 20);
        var ctx = MakeCtx(cell, TextEffectCellPhase.Entering, progress: 1.0);
        var payload = TextCellRenderPayload.Identity;

        effect.Apply(in ctx, ref payload);

        Assert.Equal(0.0, payload.TranslateY, precision: 3);
        Assert.Null(payload.PerCellEffect);
        Assert.Equal(1.0, payload.Opacity, precision: 3);
    }

    [Fact]
    public void Enter_MidAnimation_OvershootsPastTarget()
    {
        var effect = new RiseSettleEffect { OvershootTension = 1.7 };
        var cell = MakeCell(lineHeight: 20);

        // BackEaseOut crosses 1.0 (the target) around t = 0.6–0.7; at t = 0.8
        // it has settled back but not yet to 1.0. Around t = 0.5 it's on the
        // way up and near the peak of the overshoot — translateY should be
        // slightly negative (above target).
        var ctx = MakeCtx(cell, TextEffectCellPhase.Entering, progress: 0.75);
        var payload = TextCellRenderPayload.Identity;
        effect.Apply(in ctx, ref payload);

        // Overshoot means the cell briefly sits above its target, so TranslateY
        // goes negative at some mid-to-late progress.
        Assert.True(payload.TranslateY < 0, $"Expected overshoot above target at p=0.75 but got TranslateY={payload.TranslateY}");
    }

    [Fact]
    public void Enter_OpacityRampCompletesEarly()
    {
        var effect = new RiseSettleEffect { OpacityRampFraction = 0.4 };
        var cell = MakeCell();
        // At progress = ramp fraction, opacity should have hit 1 while motion
        // and blur still have work to do. At p=0.4 the blur radius is
        // RiseBlurPx * (1 - 0.16) = 12 * 0.84 ≈ 10.08 px.
        var ctx = MakeCtx(cell, TextEffectCellPhase.Entering, progress: 0.4);
        var payload = TextCellRenderPayload.Identity;
        effect.Apply(in ctx, ref payload);

        Assert.Equal(1.0, payload.Opacity, precision: 3);
        Assert.True(payload.BlurRadius > 1.0,
            $"Blur should still be substantial during settle, got {payload.BlurRadius}");
    }

    #endregion

    #region Exiting phase

    [Fact]
    public void Exit_AtStart_IsIdentityMinusOpacityStart()
    {
        var effect = new RiseSettleEffect();
        var cell = MakeCell(lineHeight: 20);
        var ctx = MakeCtx(cell, TextEffectCellPhase.Exiting, progress: 0.0);
        var payload = TextCellRenderPayload.Identity;

        effect.Apply(in ctx, ref payload);

        Assert.Equal(0.0, payload.TranslateY, precision: 3);
        Assert.Equal(0.0, payload.BlurRadius, precision: 3);
        Assert.Equal(1.0, payload.Opacity, precision: 3);
    }

    [Fact]
    public void Exit_AtEnd_IsInvisibleAndBlurred()
    {
        var effect = new RiseSettleEffect { DissipateBlurPx = 8.0 };
        var cell = MakeCell(lineHeight: 20);
        var ctx = MakeCtx(cell, TextEffectCellPhase.Exiting, progress: 1.0);
        var payload = TextCellRenderPayload.Identity;

        effect.Apply(in ctx, ref payload);

        Assert.Equal(0.0, payload.Opacity, precision: 3);
        Assert.Equal(8.0, payload.BlurRadius, precision: 3);
        // Drifts upward.
        Assert.True(payload.TranslateY < 0);
    }

    #endregion

    #region Shifting phase

    [Fact]
    public void Shift_AtStart_SitsAtOrigin()
    {
        var effect = new RiseSettleEffect();
        var cell = MakeCell();
        cell.Bounds = new Rect(100, 0, 10, 20);
        cell.ShiftOriginX = 50;
        cell.ShiftOriginY = 0;

        var ctx = MakeCtx(cell, TextEffectCellPhase.Shifting, progress: 0.0);
        var payload = TextCellRenderPayload.Identity;
        effect.Apply(in ctx, ref payload);

        // Target is X=100, origin was X=50, so at progress 0 the cell should
        // render as if it were still at X=50 — TranslateX = origin - target = -50.
        Assert.Equal(-50.0, payload.TranslateX, precision: 3);
    }

    [Fact]
    public void Shift_AtEnd_TargetReached()
    {
        var effect = new RiseSettleEffect();
        var cell = MakeCell();
        cell.Bounds = new Rect(100, 0, 10, 20);
        cell.ShiftOriginX = 50;

        var ctx = MakeCtx(cell, TextEffectCellPhase.Shifting, progress: 1.0);
        var payload = TextCellRenderPayload.Identity;
        effect.Apply(in ctx, ref payload);

        Assert.Equal(0.0, payload.TranslateX, precision: 3);
    }

    [Fact]
    public void Shift_DoesNotOvershoot()
    {
        // Unlike Enter, Shift must NOT overshoot — otherwise collapsing space
        // after a RemoveText would visibly wobble, which the requirements call out.
        var effect = new RiseSettleEffect();
        var cell = MakeCell();
        cell.Bounds = new Rect(100, 0, 10, 20);
        cell.ShiftOriginX = 50;

        for (double p = 0.0; p <= 1.0; p += 0.1)
        {
            var ctx = MakeCtx(cell, TextEffectCellPhase.Shifting, progress: p);
            var payload = TextCellRenderPayload.Identity;
            effect.Apply(in ctx, ref payload);

            // The cell's rendered X = Bounds.X + TranslateX must remain in
            // [origin, target] — monotonic collapse toward target.
            var renderedX = cell.Bounds.X + payload.TranslateX;
            Assert.InRange(renderedX, 50.0 - 0.01, 100.0 + 0.01);
        }
    }

    #endregion

    #region Visible phase

    [Fact]
    public void Visible_IsNoOp()
    {
        var effect = new RiseSettleEffect();
        var cell = MakeCell();
        var ctx = MakeCtx(cell, TextEffectCellPhase.Visible, progress: 0.5);
        var payload = TextCellRenderPayload.Identity;

        effect.Apply(in ctx, ref payload);

        Assert.Equal(0.0, payload.TranslateX, precision: 3);
        Assert.Equal(0.0, payload.TranslateY, precision: 3);
        Assert.Equal(0.0, payload.BlurRadius, precision: 3);
        Assert.Null(payload.PerCellEffect);
        Assert.Equal(1.0, payload.Opacity, precision: 3);
    }

    #endregion

    #region Stagger

    [Fact]
    public void Stagger_EveryCellEntersTogether()
    {
        // RiseSettle is a block-rise effect — the user wants the whole appended
        // string to move as one, not wave across character-by-character.
        // Staggered per-char timing belongs on TypewriterEffect, not here.
        var effect = new RiseSettleEffect();
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(0, effect.GetStaggerDelayMs(i, 20));
        }
    }

    #endregion
}
