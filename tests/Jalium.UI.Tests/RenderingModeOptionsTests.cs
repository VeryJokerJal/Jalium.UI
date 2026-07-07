using Jalium.UI.Hosting;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers <see cref="RenderingModeOptions"/> — the process-wide rendering-mode
/// selection behind <c>app.UseRenderingMode(...)</c>. These use fresh instances
/// (not the <see cref="RenderingModeOptions.Current"/> singleton) so they neither
/// pollute nor depend on global state, and assert the mode → effective-flag
/// invariants rather than a specific adapter's resolution (the Adaptive path reads
/// the live GPU adapter, which is environment-dependent).
/// </summary>
public class RenderingModeOptionsTests
{
    [Fact]
    public void Default_Mode_Is_Adaptive()
    {
        var options = new RenderingModeOptions();
        Assert.Equal(RenderingMode.Adaptive, options.Mode);
    }

    [Fact]
    public void FullFrame_Runs_FullFrame_With_RenderThread_Allowed_And_No_DamageScoping()
    {
        var options = new RenderingModeOptions { Mode = RenderingMode.FullFrame };

        Assert.Equal(RenderingMode.FullFrame, options.EffectiveMode);
        Assert.True(options.AllowRenderThread);
        Assert.False(options.DamageScopedEnabled);

        // Even with the native capability present, FullFrame never damage-scopes.
        options.DamageScopedAvailable = true;
        Assert.False(options.DamageScopedEnabled);
    }

    [Fact]
    public void Performance_Forbids_RenderThread_And_Gates_DamageScoping_On_Capability()
    {
        var options = new RenderingModeOptions { Mode = RenderingMode.Performance };

        Assert.Equal(RenderingMode.Performance, options.EffectiveMode);
        // R-A: Performance requires the inline path — the render thread is disallowed.
        Assert.False(options.AllowRenderThread);

        // Capability off (native damage-scoped present not yet available) → inert.
        Assert.False(options.DamageScopedAvailable);
        Assert.False(options.DamageScopedEnabled);

        // Capability on → Performance actually activates damage-scoped rendering.
        options.DamageScopedAvailable = true;
        Assert.True(options.DamageScopedEnabled);
    }

    [Fact]
    public void Adaptive_Resolves_To_A_Concrete_Mode_With_Consistent_Flags()
    {
        var options = new RenderingModeOptions { Mode = RenderingMode.Adaptive, DamageScopedAvailable = true };

        var effective = options.EffectiveMode;

        // Adaptive never stays Adaptive — it resolves to a concrete strategy.
        Assert.True(effective is RenderingMode.FullFrame or RenderingMode.Performance);
        // Flags must stay consistent with whatever it resolved to.
        Assert.Equal(effective != RenderingMode.Performance, options.AllowRenderThread);
        Assert.Equal(effective == RenderingMode.Performance, options.DamageScopedEnabled);
    }

    [Fact]
    public void Adaptive_Without_Capability_Never_DamageScopes()
    {
        // Weak adapters are only routed to Performance once the native capability
        // exists; until then Adaptive must not enable damage-scoping (no regression).
        var options = new RenderingModeOptions { Mode = RenderingMode.Adaptive, DamageScopedAvailable = false };

        Assert.False(options.DamageScopedEnabled);
        Assert.True(options.AllowRenderThread);
        Assert.Equal(RenderingMode.FullFrame, options.EffectiveMode);
    }

    [Fact]
    public void Changing_Mode_Reresolves_Effective_Mode()
    {
        var options = new RenderingModeOptions { Mode = RenderingMode.Performance };
        Assert.Equal(RenderingMode.Performance, options.EffectiveMode);

        options.Mode = RenderingMode.FullFrame;
        Assert.Equal(RenderingMode.FullFrame, options.EffectiveMode);
        Assert.True(options.AllowRenderThread);
    }
}
