using Jalium.UI;
using Jalium.UI.Controls.TextEffects;
using Jalium.UI.Controls.TextEffects.Effects;
using Jalium.UI.Media.Effects;

namespace Jalium.UI.Tests.TextEffects;

/// <summary>
/// Contract tests for <see cref="ShaderTextEffect"/> — the per-cell Apply is
/// a no-op by design, UpdateForFrame fires from the render path, and a null
/// <see cref="ShaderTextEffect.CurrentEffect"/> falls back to normal rendering.
/// </summary>
public class ShaderTextEffectTests
{
    private sealed class CountingShaderEffect : ShaderTextEffect
    {
        public int UpdateCallCount;
        public double LastElapsedMs;
        public Size LastSize;
        public IEffect? EffectToReturn { get; set; }

        protected internal override void UpdateForFrame(Size presenterSize, double totalElapsedMs)
        {
            UpdateCallCount++;
            LastElapsedMs = totalElapsedMs;
            LastSize = presenterSize;
        }

        public override IEffect? CurrentEffect => EffectToReturn;
    }

    [Fact]
    public void Apply_DefaultImplementation_DoesNotMutatePayload()
    {
        // The shader pass is the visual; per-cell Apply should stay empty so
        // transforms / opacity don't compound with the GPU pass in unexpected
        // ways. Subclasses can override if they need per-cell positioning.
        var effect = new CountingShaderEffect();
        var cell = new TextEffectCell(1, "a", 0, 0, 1);
        cell.Bounds = new Rect(0, 0, 10, 20);
        var ctx = new TextEffectFrameContext(
            cell, TextEffectCellPhase.Entering, 0.5, 250, 250, new Size(200, 40));

        var payload = TextCellRenderPayload.Identity;
        effect.Apply(in ctx, ref payload);

        Assert.Equal(0.0, payload.TranslateX, precision: 3);
        Assert.Equal(0.0, payload.TranslateY, precision: 3);
        Assert.Equal(1.0, payload.Opacity, precision: 3);
        Assert.Equal(1.0, payload.ScaleX, precision: 3);
        Assert.Equal(0.0, payload.BlurRadius, precision: 3);
    }

    [Fact]
    public void PulsingBlur_RadiusOscillatesOverTime()
    {
        // PulsingBlurTextEffect's defining behaviour: the radius is a sine
        // wave of wall-clock time around (Baseline + Amplitude/2).
        var effect = new PulsingBlurTextEffect
        {
            BaselinePx = 1.0,
            AmplitudePx = 10.0,
            PeriodMs = 2000.0,
        };

        // Phase 0 (sine = 0 at start) → radius = baseline + amplitude * 0.5 = 6
        effect.UpdateForFrame(new Size(100, 50), 0.0);
        var atZero = ((BlurEffect)effect.CurrentEffect!).Radius;

        // Quarter period (sine = 1) → radius = baseline + amplitude = 11
        effect.UpdateForFrame(new Size(100, 50), 500.0);
        var atPeak = ((BlurEffect)effect.CurrentEffect!).Radius;

        // Three-quarter period (sine = -1) → radius = baseline = 1
        effect.UpdateForFrame(new Size(100, 50), 1500.0);
        var atTrough = ((BlurEffect)effect.CurrentEffect!).Radius;

        Assert.Equal(6.0, atZero, precision: 1);
        Assert.Equal(11.0, atPeak, precision: 1);
        Assert.Equal(1.0, atTrough, precision: 1);
    }

    [Fact]
    public void CurrentEffect_WhenNull_FallsBackToNormalRendering()
    {
        // If CurrentEffect returns null (shader in "off" phase), the presenter
        // must fall through to its standard two-pass blur composition rather
        // than skipping rendering entirely.
        var shader = new CountingShaderEffect { EffectToReturn = null };
        var p = new TextEffectPresenter
        {
            IsAnimationEnabled = false,
            TextEffect = shader,
            Text = "ab",
        };
        p.Measure(new Size(400, 100));
        p.Arrange(new Rect(0, 0, 400, 100));

        var dc = new RecordingDrawingContext();
        p.RenderForTesting(dc);

        // No shader scope opened because CurrentEffect == null — cells draw
        // directly to the main RT via the normal path.
        Assert.Equal(0, dc.PushEffectCount);
        Assert.Equal(2, dc.DrawTextCount);
    }

    [Fact]
    public void CurrentEffect_WhenPresent_OpensOneShaderScopeAroundAllCells()
    {
        // Confirm the shader integration: one PushEffect wrapping every cell's
        // DrawText, then PopEffect. Order: PushEffect → N × DrawText → PopEffect.
        var blur = new BlurEffect(4);
        var shader = new CountingShaderEffect { EffectToReturn = blur };
        var p = new TextEffectPresenter
        {
            IsAnimationEnabled = false,
            TextEffect = shader,
            Text = "abc",
        };
        p.Measure(new Size(400, 100));
        p.Arrange(new Rect(0, 0, 400, 100));

        var dc = new RecordingDrawingContext();
        p.RenderForTesting(dc);

        Assert.Equal(1, dc.PushEffectCount);
        Assert.Same(blur, dc.PushedEffects[0]);
        Assert.Equal(1, dc.PopEffectCount);
        Assert.Equal(3, dc.DrawTextCount);

        // UpdateForFrame was called exactly once for this render pass.
        Assert.Equal(1, shader.UpdateCallCount);
    }

    [Fact]
    public void UpdateForFrame_ReceivesPresenterSizeAndElapsedMs()
    {
        var shader = new CountingShaderEffect { EffectToReturn = new BlurEffect(4) };
        var p = new TextEffectPresenter
        {
            IsAnimationEnabled = false,
            TextEffect = shader,
            Text = "x",
        };
        p.Measure(new Size(200, 80));
        p.Arrange(new Rect(0, 0, 200, 80));

        var dc = new RecordingDrawingContext();
        p.RenderForTesting(dc);

        // Size matches presenter render size. Elapsed is whatever the clock
        // accumulated — here zero since we didn't AdvanceFrameForTesting,
        // but the argument was plumbed through correctly.
        Assert.Equal(200, shader.LastSize.Width, precision: 1);
        Assert.Equal(80, shader.LastSize.Height, precision: 1);
        Assert.Equal(0.0, shader.LastElapsedMs, precision: 3);
    }

    [Fact]
    public void UpdateForFrame_PicksUpClockAfterAdvance()
    {
        var shader = new CountingShaderEffect { EffectToReturn = new BlurEffect(4) };
        var p = new TextEffectPresenter
        {
            IsAnimationEnabled = false,
            TextEffect = shader,
            Text = "x",
        };
        p.Measure(new Size(200, 80));
        p.Arrange(new Rect(0, 0, 200, 80));

        p.AdvanceFrameForTesting(123);
        var dc = new RecordingDrawingContext();
        p.RenderForTesting(dc);

        Assert.Equal(123.0, shader.LastElapsedMs, precision: 3);
    }
}
