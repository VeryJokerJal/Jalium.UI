using System.Diagnostics;
using Jalium.UI;
using Jalium.UI.Controls.TextEffects;
using Jalium.UI.Controls.TextEffects.Effects;

namespace Jalium.UI.Tests.TextEffects;

/// <summary>
/// Smoke-level performance regression guards for the <see cref="TextEffectPresenter"/>
/// state machine and effect-apply hot path. These are not replacement for a real
/// profiler — they only catch O(N²) accidents and similar algorithmic regressions.
/// Thresholds are set with a ~10× headroom over current measured times so CI
/// variance doesn't make them flaky.
/// </summary>
/// <remarks>
/// <para><b>Why no DrawGlyphRun batching?</b></para>
/// <para>
/// Current render path (<c>RenderTargetDrawingContext.DrawText</c>) issues one
/// P/Invoke per cell. That looks like low-hanging fruit until you realise:
/// </para>
/// <list type="bullet">
///   <item>During any active animation each cell carries a unique transform
///   (TranslateX/Y, Scale, Opacity, BlurRadius) — cells cannot legally share
///   a single DrawText call.</item>
///   <item>In steady state the presenter un-subscribes from
///   <c>CompositionTarget.Rendering</c>, so there's no per-frame cost to
///   amortise in the first place.</item>
///   <item>Native D3D12 backend already batches individual glyphs inside one
///   DrawText into one instanced draw — the per-call overhead is a single
///   P/Invoke + text shaping, not a per-glyph draw.</item>
/// </list>
/// <para>
/// These tests lock the current per-frame cost in place; if it ever climbs
/// into "batching would actually help" territory we'll know to revisit.
/// </para>
/// </remarks>
public class TextEffectPresenterPerfTests
{
    private const int WarmupFrames = 50;
    private const int MeasureFrames = 200;

    private static TextEffectPresenter MakePresenter(string text, ITextEffect? effect = null)
    {
        return new TextEffectPresenter
        {
            FontSize = 16,
            IsAnimationEnabled = true,
            TextEffect = effect ?? new RiseSettleEffect(),
            Text = text,
        };
    }

    private static double MeasureAveragePerFrameMs(TextEffectPresenter p, int frameCount, double deltaMs)
    {
        // Warmup the JIT + font caches.
        for (int i = 0; i < WarmupFrames; i++)
        {
            p.AdvanceFrameForTesting(deltaMs);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < frameCount; i++)
        {
            p.AdvanceFrameForTesting(deltaMs);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds / frameCount;
    }

    #region State-machine throughput

    [Fact]
    public void AdvanceFrame_IdleCells_IsEffectivelyFree()
    {
        // Idle == all cells in Visible phase. UpdateCellPhase short-circuits,
        // so each frame should be a walk-the-list of ~no-ops.
        var p = MakePresenter(new string('a', 500));
        p.AdvanceFrameForTesting(10_000); // drive every cell out of Entering

        var avgMs = MeasureAveragePerFrameMs(p, MeasureFrames, 16.0);

        Assert.True(avgMs < 1.0,
            $"Idle frame should cost < 1 ms, got {avgMs:F3} ms at 500 cells");
    }

    [Fact]
    public void AdvanceFrame_AllEntering_ScalesLinearly()
    {
        // Every cell in Entering — max work per frame: compute phase progress
        // for each. No O(N²) hidden scans should leak in.
        var p = MakePresenter(new string('a', 500));

        var avgMs = MeasureAveragePerFrameMs(p, MeasureFrames, 1.0);

        Assert.True(avgMs < 5.0,
            $"Full-Entering frame at 500 cells should cost < 5 ms, got {avgMs:F3} ms");
    }

    #endregion

    #region Effect.Apply hot path

    [Theory]
    [InlineData("risesettle")]
    [InlineData("fadein")]
    [InlineData("typewriter")]
    [InlineData("wavebounce")]
    public void ApplyEffect_500Cells_UnderThreshold(string kind)
    {
        // Direct measurement of the Effect.Apply pipeline — the hot path every
        // frame for animated presenters. Each effect should execute at
        // roughly the same cost per cell; an outlier flags a regression.
        ITextEffect effect = kind switch
        {
            "risesettle" => new RiseSettleEffect(),
            "fadein" => new FadeInEffect(),
            "typewriter" => new TypewriterEffect(),
            "wavebounce" => new WaveBounceEffect(),
            _ => throw new System.InvalidOperationException(),
        };

        var cell = new TextEffectCell(id: 1, text: "a", batchId: 0, indexInBatch: 0, batchSize: 1);
        cell.Bounds = new Rect(0, 0, 10, 20);
        cell.LineHeight = 20;
        var ctx = new TextEffectFrameContext(
            cell, TextEffectCellPhase.Entering,
            phaseProgressLinear: 0.5,
            timeInPhaseMs: 250, totalTimeMs: 250,
            presenterSize: new Size(200, 40));

        const int iterations = 500 * MeasureFrames; // 500 cells × 200 frames

        // Warmup.
        for (int i = 0; i < 1000; i++)
        {
            var warm = TextCellRenderPayload.Identity;
            effect.Apply(in ctx, ref warm);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var payload = TextCellRenderPayload.Identity;
            effect.Apply(in ctx, ref payload);
        }
        sw.Stop();

        var perFrameMs = sw.Elapsed.TotalMilliseconds / MeasureFrames;
        Assert.True(perFrameMs < 3.0,
            $"{kind}: Apply×500/frame should cost < 3 ms, got {perFrameMs:F3} ms");
    }

    #endregion

    #region Insert / Remove complexity

    [Fact]
    public void InsertText_AtMiddleOfLargeBuffer_CompletesQuickly()
    {
        // The ShiftOrigin capture loop runs over the suffix — O(N) per insert.
        // A regression that made it O(N²) per character would surface here.
        var p = MakePresenter(new string('x', 1000));
        p.AdvanceFrameForTesting(10_000);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 50; i++)
        {
            p.InsertText(500, "y");
        }
        sw.Stop();

        Assert.True(sw.Elapsed.TotalMilliseconds < 200,
            $"50 middle-inserts into a 1000-cell buffer took {sw.Elapsed.TotalMilliseconds:F0} ms");
    }

    #endregion
}
