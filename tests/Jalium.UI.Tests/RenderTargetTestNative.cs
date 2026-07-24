using Jalium.UI;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

internal sealed class RenderTargetTestNative : IRenderTargetNative
{
    public nint CreatedHandle { get; set; } = new(0xCAFE);
    public NativeSurfaceDescriptor? LastSurface { get; private set; }
    public int ContextLastError { get; set; } = (int)JaliumResult.Unknown;
    public int ResizeResult { get; set; } = (int)JaliumResult.Ok;
    public int BeginDrawResult { get; set; } = (int)JaliumResult.Ok;
    public int EndDrawResult { get; set; } = (int)JaliumResult.Ok;
    public bool SupportsPartialPresentationValue { get; set; } = true;
    public bool ThrowOnSupportsPartialPresentation { get; set; }
    public int BeginDrawCalls { get; private set; }
    public int EndDrawCalls { get; private set; }
    public int ResizeCalls { get; private set; }
    public List<(int Width, int Height)> ResizeSizes { get; } = new();
    public int DestroyCalls { get; private set; }
    public Action<int, int>? OnResize { get; set; }
    public Action? OnDestroy { get; set; }

    public nint CreateForSurface(nint context, NativeSurfaceDescriptor surface, int width, int height)
    {
        LastSurface = surface;
        return CreatedHandle;
    }

    public nint CreateForCompositionSurface(nint context, NativeSurfaceDescriptor surface, int width, int height)
    {
        LastSurface = surface;
        return CreatedHandle;
    }

    public int GetContextLastError(nint context) => ContextLastError;

    public int Resize(nint renderTarget, int width, int height)
    {
        ResizeCalls++;
        ResizeSizes.Add((width, height));
        OnResize?.Invoke(width, height);
        return ResizeResult;
    }

    public int BeginDraw(nint renderTarget)
    {
        BeginDrawCalls++;
        return BeginDrawResult;
    }

    public int EndDraw(nint renderTarget)
    {
        EndDrawCalls++;
        return EndDrawResult;
    }

    /// <summary>
    /// Reported engine for the fake target. Returning a value here keeps
    /// <see cref="RenderTarget.RenderingEngine"/> off a real native P/Invoke,
    /// which would dereference <see cref="CreatedHandle"/> (a non-owned handle)
    /// and crash the test host with an AccessViolation.
    /// </summary>
    public RenderingEngine EngineValue { get; set; } = RenderingEngine.Auto;

    public RenderingEngine GetEngine(nint renderTarget) => EngineValue;

    public void SetVSyncEnabled(nint renderTarget, bool enabled)
    {
    }

    /// <summary>Last external-pacing state requested (see <see cref="IRenderTargetNative.SetExternalPresentPacing"/>).</summary>
    public bool LastExternalPresentPacing { get; private set; }

    public void SetExternalPresentPacing(nint renderTarget, bool enabled)
    {
        LastExternalPresentPacing = enabled;
    }

    public void SetFullInvalidation(nint renderTarget)
    {
    }

    /// <summary>Records the last path MSAA sample count requested (see <see cref="IRenderTargetNative.SetPathMsaaSampleCount"/>).</summary>
    public uint LastPathMsaaSampleCount { get; private set; }

    public void SetPathMsaaSampleCount(nint renderTarget, uint sampleCount)
    {
        LastPathMsaaSampleCount = sampleCount;
    }

    public bool SupportsPartialPresentation(nint renderTarget)
    {
        if (ThrowOnSupportsPartialPresentation)
        {
            throw new InvalidOperationException("Injected capability-query failure.");
        }

        return SupportsPartialPresentationValue;
    }

    /// <summary>Number of retained-layer destroy requests routed through the seam.</summary>
    public int DestroyRetainedLayerCalls { get; private set; }

    public void DestroyRetainedLayer(nint renderTarget, nint layer) => DestroyRetainedLayerCalls++;

    public void Destroy(nint renderTarget)
    {
        DestroyCalls++;
        OnDestroy?.Invoke();
    }
}
