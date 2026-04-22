using Jalium.UI;
using Jalium.UI.Controls.TextEffects;
using Jalium.UI.Controls.TextEffects.Effects;

namespace Jalium.UI.Tests.TextEffects;

/// <summary>
/// Behavioural contract tests for the three secondary built-in effects
/// (<see cref="FadeInEffect"/>, <see cref="TypewriterEffect"/>,
/// <see cref="WaveBounceEffect"/>). Each effect is verified against its
/// defining visual property — whatever that effect is supposed to uniquely
/// deliver — so a regression in the per-phase math gets caught immediately.
/// </summary>
public class BuiltInEffectsTests
{
    private static TextEffectCell MakeCell(double width = 10, double lineHeight = 20)
    {
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
            cell, phase, progress,
            timeInPhaseMs: progress * 500,
            totalTimeMs: progress * 500,
            presenterSize: new Size(200, 40));
    }

    #region FadeInEffect

    [Fact]
    public void FadeIn_Enter_FullyOpaqueAtEnd()
    {
        var effect = new FadeInEffect();
        var cell = MakeCell();

        var startPayload = TextCellRenderPayload.Identity;
        effect.Apply(MakeCtx(cell, TextEffectCellPhase.Entering, 0.0), ref startPayload);
        Assert.Equal(0.0, startPayload.Opacity, precision: 3);

        var endPayload = TextCellRenderPayload.Identity;
        effect.Apply(MakeCtx(cell, TextEffectCellPhase.Entering, 1.0), ref endPayload);
        Assert.Equal(1.0, endPayload.Opacity, precision: 3);
    }

    [Fact]
    public void FadeIn_HasNoMotionOrBlur()
    {
        // Defining property: pure opacity crossfade, nothing else moves.
        var effect = new FadeInEffect();
        var cell = MakeCell();

        for (double t = 0.0; t <= 1.0; t += 0.25)
        {
            var payload = TextCellRenderPayload.Identity;
            effect.Apply(MakeCtx(cell, TextEffectCellPhase.Entering, t), ref payload);

            Assert.Equal(0.0, payload.TranslateX, precision: 3);
            Assert.Equal(0.0, payload.TranslateY, precision: 3);
            Assert.Equal(0.0, payload.BlurRadius, precision: 3);
            Assert.Equal(1.0, payload.ScaleX, precision: 3);
            Assert.Equal(1.0, payload.ScaleY, precision: 3);
        }
    }

    [Fact]
    public void FadeIn_Exit_FullyTransparentAtEnd()
    {
        var effect = new FadeInEffect();
        var cell = MakeCell();

        var payload = TextCellRenderPayload.Identity;
        effect.Apply(MakeCtx(cell, TextEffectCellPhase.Exiting, 1.0), ref payload);

        Assert.Equal(0.0, payload.Opacity, precision: 3);
    }

    [Fact]
    public void FadeIn_OpacityCurveAffectsProgress()
    {
        // OpacityCurve = 2 means the ramp is concave — at t = 0.5 opacity
        // should be 0.25, not the 0.5 a linear ramp would give.
        var effect = new FadeInEffect { OpacityCurve = 2.0 };
        var cell = MakeCell();

        var payload = TextCellRenderPayload.Identity;
        effect.Apply(MakeCtx(cell, TextEffectCellPhase.Entering, 0.5), ref payload);

        Assert.Equal(0.25, payload.Opacity, precision: 3);
    }

    #endregion

    #region TypewriterEffect

    [Fact]
    public void Typewriter_Opacity_IsBinaryAroundCutoff()
    {
        // Defining property: no smooth fade — glyphs flip on at the cutoff.
        var effect = new TypewriterEffect { Cutoff = 0.5 };
        var cell = MakeCell();

        var before = TextCellRenderPayload.Identity;
        effect.Apply(MakeCtx(cell, TextEffectCellPhase.Entering, 0.49), ref before);

        var after = TextCellRenderPayload.Identity;
        effect.Apply(MakeCtx(cell, TextEffectCellPhase.Entering, 0.51), ref after);

        Assert.Equal(0.0, before.Opacity, precision: 3);
        Assert.Equal(1.0, after.Opacity, precision: 3);
    }

    [Fact]
    public void Typewriter_Stagger_GrowsLinearlyWithoutCeiling()
    {
        // Defining property: no upper cap, unlike TextEffectBase's default.
        // A long paragraph should take proportionally longer.
        var effect = new TypewriterEffect { CharacterDelayMs = 40 };

        Assert.Equal(0, effect.GetStaggerDelayMs(0, 1000));
        Assert.Equal(40, effect.GetStaggerDelayMs(1, 1000));
        Assert.Equal(4000, effect.GetStaggerDelayMs(100, 1000));
        Assert.Equal(40_000, effect.GetStaggerDelayMs(1000, 1000));
    }

    [Fact]
    public void Typewriter_CharacterDelay_ControlsStagger()
    {
        var slow = new TypewriterEffect { CharacterDelayMs = 100 };
        var fast = new TypewriterEffect { CharacterDelayMs = 20 };

        Assert.True(slow.GetStaggerDelayMs(5, 10) > fast.GetStaggerDelayMs(5, 10));
    }

    [Fact]
    public void Typewriter_Visible_IsIdentity()
    {
        var effect = new TypewriterEffect();
        var cell = MakeCell();

        var payload = TextCellRenderPayload.Identity;
        effect.Apply(MakeCtx(cell, TextEffectCellPhase.Visible, 0.5), ref payload);

        Assert.Equal(1.0, payload.Opacity, precision: 3);
        Assert.Equal(0.0, payload.TranslateX, precision: 3);
        Assert.Equal(0.0, payload.TranslateY, precision: 3);
    }

    #endregion

    #region WaveBounceEffect

    [Fact]
    public void WaveBounce_Enter_StartsAboveAndPopsScale()
    {
        // Defining property: drops from above with a larger-than-1 scale.
        var effect = new WaveBounceEffect { DropDistance = -0.6, PopScale = 1.3 };
        var cell = MakeCell(lineHeight: 20);

        var payload = TextCellRenderPayload.Identity;
        effect.Apply(MakeCtx(cell, TextEffectCellPhase.Entering, 0.0), ref payload);

        Assert.True(payload.TranslateY < 0, "Cell should start above target (dropping in)");
        Assert.True(payload.ScaleX > 1.0, $"Scale should pop > 1 at start, got {payload.ScaleX}");
        Assert.True(payload.ScaleY > 1.0);
    }

    [Fact]
    public void WaveBounce_Enter_SettlesToIdentityAtEnd()
    {
        var effect = new WaveBounceEffect();
        var cell = MakeCell();

        var payload = TextCellRenderPayload.Identity;
        effect.Apply(MakeCtx(cell, TextEffectCellPhase.Entering, 1.0), ref payload);

        Assert.Equal(0.0, payload.TranslateY, precision: 1);
        Assert.Equal(1.0, payload.ScaleX, precision: 2);
        Assert.Equal(1.0, payload.ScaleY, precision: 2);
        Assert.Equal(1.0, payload.Opacity, precision: 2);
    }

    [Fact]
    public void WaveBounce_Wobble_OscillatesThroughEnter()
    {
        // Defining property: the sinusoidal wobble means vertical position is
        // NOT monotonic during the enter phase — it crosses the baseline
        // multiple times even though the base descent is monotonic.
        var effect = new WaveBounceEffect
        {
            DropDistance = 0, // isolate wobble from descent
            WobbleAmplitude = 0.3,
            WobbleCycles = 2.0,
        };
        var cell = MakeCell(lineHeight: 20);

        var samples = new double[20];
        for (int i = 0; i < samples.Length; i++)
        {
            var t = (i + 0.5) / samples.Length;
            var payload = TextCellRenderPayload.Identity;
            effect.Apply(MakeCtx(cell, TextEffectCellPhase.Entering, t), ref payload);
            samples[i] = payload.TranslateY;
        }

        // Two oscillations of the sine (WobbleCycles = 2) means we should
        // see both positive and negative TranslateY values somewhere in the
        // progression, confirming the wobble is actually oscillating.
        var hasPositive = false;
        var hasNegative = false;
        foreach (var y in samples)
        {
            if (y > 0.5) hasPositive = true;
            if (y < -0.5) hasNegative = true;
        }
        Assert.True(hasPositive, "Wobble should swing below baseline");
        Assert.True(hasNegative, "Wobble should swing above baseline");
    }

    [Fact]
    public void WaveBounce_Exit_FallsAndFades()
    {
        var effect = new WaveBounceEffect { DropDistance = -0.6 };
        var cell = MakeCell(lineHeight: 20);

        var payload = TextCellRenderPayload.Identity;
        effect.Apply(MakeCtx(cell, TextEffectCellPhase.Exiting, 1.0), ref payload);

        // Exit direction matches DropDistance sign — stays visually consistent
        // with entrance (cell came from above, falls back up on exit isn't
        // right here, the effect is "came down → falls the same way").
        Assert.Equal(0.0, payload.Opacity, precision: 3);
        // With negative DropDistance, exit TranslateY is also negative
        // (0.8 * -0.6 * 20 = -9.6).
        Assert.True(payload.TranslateY < 0);
    }

    #endregion

    #region Cross-effect contract: Shift never overshoots

    [Theory]
    [InlineData("fade")]
    [InlineData("typewriter")]
    [InlineData("wavebounce")]
    [InlineData("risesettle")]
    public void AllEffects_Shift_DoesNotOvershootTarget(string kind)
    {
        // All four effects MUST implement shift as a monotonic collapse to
        // the target — otherwise space-adjustment after Insert/Remove would
        // wobble, which the requirements explicitly rule out.
        ITextEffect effect = kind switch
        {
            "fade" => new FadeInEffect(),
            "typewriter" => new TypewriterEffect(),
            "wavebounce" => new WaveBounceEffect(),
            "risesettle" => new RiseSettleEffect(),
            _ => throw new System.InvalidOperationException(),
        };

        var cell = MakeCell();
        cell.Bounds = new Rect(100, 0, 10, 20);
        cell.ShiftOriginX = 50;

        for (double p = 0.0; p <= 1.0; p += 0.1)
        {
            var payload = TextCellRenderPayload.Identity;
            effect.Apply(MakeCtx(cell, TextEffectCellPhase.Shifting, p), ref payload);

            var renderedX = cell.Bounds.X + payload.TranslateX;
            Assert.InRange(renderedX, 50.0 - 0.01, 100.0 + 0.01);
        }
    }

    #endregion
}
