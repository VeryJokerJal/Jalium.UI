using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

public sealed class WindowEmptyRenderingPolicyTests
{
    [Fact]
    public void DefaultWindowsAutoWindow_IsEligibleForEmptySoftwareRendering()
    {
        Assert.True(Window.ShouldUseAutomaticEmptySoftware(EligibleInput));
    }

    [Theory]
    [InlineData(true, RenderBackend.D3D12, true, true)]
    [InlineData(true, RenderBackend.D3D12, false, false)]
    [InlineData(true, RenderBackend.Software, true, false)]
    [InlineData(true, RenderBackend.Vulkan, true, false)]
    [InlineData(false, RenderBackend.D3D12, true, false)]
    public void ReversibleAutoPresentation_ProtectsOnlyTheWindowsD3D12GdiBoundary(
        bool windows, RenderBackend backend, bool automatic, bool expected)
    {
        Assert.Equal(expected, Window.ShouldPreserveGdiPresentation(windows, backend, automatic));
    }

    [Fact]
    public void AnyApplicationDrawingOrSurfaceRequirement_RejectsEmptySoftwareRendering()
    {
        var rejected = new (string Name, Window.EmptyRenderingPolicyInput Input)[]
        {
            ("non-Windows", EligibleInput with { IsWindows = false }),
            ("explicit backend", EligibleInput with { RequestedBackend = RenderBackend.D3D12 }),
            ("forced context", EligibleInput with { ForceNewContext = true }),
            ("content", EligibleInput with { HasContent = true }),
            ("transparency", EligibleInput with { AllowsTransparency = true }),
            ("system backdrop", EligibleInput with { HasSystemBackdrop = true }),
            ("effect", EligibleInput with { HasEffect = true }),
            ("backdrop effect", EligibleInput with { HasBackdropEffect = true }),
            ("custom drawing", EligibleInput with { HasCustomDrawing = true }),
            ("title-bar commands", EligibleInput with { HasTitleBarCommands = true }),
            ("overlay", EligibleInput with { HasOverlayContent = true }),
            ("template", EligibleInput with { HasCustomTemplate = true }),
            ("unexpected visual", EligibleInput with { HasUnexpectedVisualChildren = true }),
            ("embedded surface", EligibleInput with { RequiresEmbeddedSurface = true }),
            ("rendering engine", EligibleInput with { HasExplicitRenderingEngine = true }),
        };

        foreach (var (name, input) in rejected)
        {
            Assert.False(
                Window.ShouldUseAutomaticEmptySoftware(input),
                $"The {name} case must use the ordinary rendering path.");
        }
    }

    private static Window.EmptyRenderingPolicyInput EligibleInput => new(
        IsWindows: true,
        RequestedBackend: RenderBackend.Auto,
        ForceNewContext: false,
        HasContent: false,
        AllowsTransparency: false,
        HasSystemBackdrop: false,
        HasEffect: false,
        HasBackdropEffect: false,
        HasCustomDrawing: false,
        HasTitleBarCommands: false,
        HasOverlayContent: false,
        HasCustomTemplate: false,
        HasUnexpectedVisualChildren: false,
        RequiresEmbeddedSurface: false,
        HasExplicitRenderingEngine: false);
}

[Collection("Application")]
public sealed class WindowEmptyRenderingStateTests : IDisposable
{
    public WindowEmptyRenderingStateTests()
    {
        // An auxiliary empty-window pool can survive a previous test's Current
        // replacement. Reset both ownership paths before asserting last-lease
        // disposal, just as the context-level lease tests do.
        RenderContextEmptyWindowTests.DrainAllContexts();
    }

    public void Dispose() => RenderContextEmptyWindowTests.DrainAllContexts();

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void DefaultEmptyWindow_ShowUsesSoftware_AndCloseReleasesWindowLease()
    {
        RenderContext.Current?.Dispose();
        var window = new Window
        {
            Width = 320,
            Height = 200,
        };

        RenderContext? software = null;
        try
        {
            window.Show();

            Assert.Equal(
                Window.EmptyRenderingState.Software,
                window.CurrentEmptyRenderingState);
            Assert.True(window.UsesAutomaticEmptySoftwareContext);
            Assert.NotNull(window.RenderTarget);
            Assert.Equal(RenderBackend.Software, window.RenderTarget!.Backend);

            software = window.RenderTarget.OwnerContext;
            Assert.Same(software, RenderContext.Current);
            Assert.True(RenderContext.IsEmptyWindowProvisionalCurrent(software!));
        }
        finally
        {
            window.Close();
        }

        Assert.NotNull(software);
        Assert.False(window.UsesAutomaticEmptySoftwareContext);
        Assert.Equal(
            Window.EmptyRenderingState.Full,
            window.CurrentEmptyRenderingState);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ContentRoundTrip_PromotesToGpu_ReturnsToSoftware_AndPromotesAgain()
    {
        RenderContext.Current?.Dispose();
        var window = new Window
        {
            Width = 320,
            Height = 200,
        };

        RenderContext? provisionalSoftware = null;
        RenderContext? firstGpu = null;
        RenderContext? restoredSoftware = null;
        RenderContext? secondGpu = null;
        try
        {
            window.Show();
            provisionalSoftware = window.RenderTarget!.OwnerContext;
            Assert.Equal(RenderBackend.Software, provisionalSoftware!.Backend);

            window.Content = new object();

            Assert.Equal(
                Window.EmptyRenderingState.Full,
                window.CurrentEmptyRenderingState);
            Assert.True(
                window.CurrentEmptyRenderingDemand.HasFlag(
                    Window.EmptyRenderingDemand.Content));
            Assert.NotNull(window.RenderTarget);
            Assert.Equal(RenderBackend.D3D12, window.RenderTarget!.Backend);
            // A flip swap chain bound directly to this HWND permanently disables
            // GDI there. A detachable composition tree keeps the return-to-empty
            // contract valid after the first real GPU present.
            Assert.True(window.RenderTarget.IsCompositionTarget);
            var originalHandle = window.Handle;

            firstGpu = window.RenderTarget.OwnerContext;
            Assert.Same(firstGpu, RenderContext.Current);
            Assert.NotSame(provisionalSoftware, firstGpu);
            Assert.False(provisionalSoftware!.IsValid);

            window.Content = null;

            Assert.Equal(
                Window.EmptyRenderingState.Software,
                window.CurrentEmptyRenderingState);
            Assert.True(window.UsesAutomaticEmptySoftwareContext);
            Assert.False(window.UsesAutomaticGpuContext);
            Assert.NotNull(window.RenderTarget);
            Assert.Equal(RenderBackend.Software, window.RenderTarget!.Backend);
            Assert.False(window.RenderTarget.IsCompositionTarget);
            Assert.Equal(originalHandle, window.Handle);

            restoredSoftware = window.RenderTarget.OwnerContext;
            Assert.Same(restoredSoftware, RenderContext.Current);
            Assert.NotSame(firstGpu, restoredSoftware);
            Assert.False(firstGpu!.IsValid);

            window.Content = new object();

            Assert.Equal(
                Window.EmptyRenderingState.Full,
                window.CurrentEmptyRenderingState);
            Assert.False(window.UsesAutomaticEmptySoftwareContext);
            Assert.True(window.UsesAutomaticGpuContext);
            Assert.Equal(RenderBackend.D3D12, window.RenderTarget!.Backend);
            Assert.True(window.RenderTarget.IsCompositionTarget);
            Assert.Equal(originalHandle, window.Handle);

            secondGpu = window.RenderTarget.OwnerContext;
            Assert.Same(secondGpu, RenderContext.Current);
            Assert.NotSame(firstGpu, secondGpu);
            Assert.True(secondGpu!.Generation > firstGpu.Generation);
            Assert.False(restoredSoftware!.IsValid);
        }
        finally
        {
            window.Close();
            RenderContext.Current?.Dispose();
        }

        Assert.NotNull(secondGpu);
        Assert.False(secondGpu!.IsValid);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ExplicitGpuSelection_ContentRemovalKeepsD3D12Current()
    {
        RenderContext.Current?.Dispose();
        var explicitGpu = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var window = new Window
        {
            Width = 320,
            Height = 200,
            Content = new object(),
        };

        try
        {
            window.Show();
            Assert.Equal(RenderBackend.D3D12, window.CurrentRenderBackend);
            Assert.Same(explicitGpu, window.RenderTarget!.OwnerContext);
            Assert.False(window.UsesAutomaticGpuContext);

            window.Content = null;

            Assert.Equal(RenderBackend.D3D12, window.CurrentRenderBackend);
            Assert.Same(explicitGpu, window.RenderTarget!.OwnerContext);
            Assert.Same(explicitGpu, RenderContext.Current);
            Assert.True(explicitGpu.IsValid);
            Assert.Equal(
                Window.EmptyRenderingState.Full,
                window.CurrentEmptyRenderingState);
        }
        finally
        {
            window.Close();
            explicitGpu.Dispose();
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void EmptyWindowBesideAutomaticGpuCurrent_UsesAuxiliarySoftwareWithoutReplacingCurrent()
    {
        RenderContext.Current?.Dispose();
        var gpu = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
        Assert.Equal(RenderBackend.D3D12, gpu.Backend);

        var window = new Window
        {
            Width = 320,
            Height = 200,
        };

        RenderContext? auxiliarySoftware = null;
        try
        {
            window.Show();

            Assert.NotNull(window.RenderTarget);
            Assert.Equal(RenderBackend.Software, window.RenderTarget!.Backend);
            auxiliarySoftware = window.RenderTarget.OwnerContext;
            Assert.NotSame(gpu, auxiliarySoftware);
            Assert.Same(gpu, RenderContext.Current);
        }
        finally
        {
            window.Close();
        }

        Assert.NotNull(auxiliarySoftware);
        Assert.False(auxiliarySoftware!.IsValid);
        Assert.True(gpu.IsValid);
        Assert.Same(gpu, RenderContext.Current);

        gpu.Dispose();
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ExplicitBackendChosenAfterLease_BeforeFirstTarget_CancelsSoftwarePath()
    {
        RenderContext.Current?.Dispose();
        var window = new Window();

        Assert.True(window.TryPrepareAutomaticEmptyRendering());
        var provisionalSoftware = Assert.IsType<RenderContext>(RenderContext.Current);
        Assert.Equal(RenderBackend.Software, provisionalSoftware.Backend);

        var explicitGpu = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        Assert.Equal(RenderBackend.D3D12, explicitGpu.Backend);

        Assert.False(window.TryGetAutomaticEmptyRenderingContext(
            RenderBackend.Auto,
            forceNewContext: false,
            out _));
        Assert.Equal(
            Window.EmptyRenderingState.FullRequested,
            window.CurrentEmptyRenderingState);
        Assert.False(provisionalSoftware.IsValid);
        Assert.Same(explicitGpu, RenderContext.Current);

        explicitGpu.Dispose();
    }

    [RequiresWindowsFact]
    public void PerWindowExplicitBackendAfterLease_CancelsSoftwarePathAndReleasesLease()
    {
        RenderContext.Current?.Dispose();
        var window = new Window();

        Assert.True(window.TryPrepareAutomaticEmptyRendering());
        var provisionalSoftware = Assert.IsType<RenderContext>(RenderContext.Current);

        Assert.False(window.TryGetAutomaticEmptyRenderingContext(
            RenderBackend.D3D12,
            forceNewContext: false,
            out _));

        Assert.Equal(
            Window.EmptyRenderingState.FullRequested,
            window.CurrentEmptyRenderingState);
        Assert.False(window.UsesAutomaticEmptySoftwareContext);
        Assert.False(provisionalSoftware.IsValid);
        Assert.Null(RenderContext.Current);
    }

    [RequiresWindowsFact]
    public void WindowSubclassCustomDrawingAndTemplateLifecycle_RejectSoftwarePath()
    {
        RenderContext.Current?.Dispose();

        Assert.False(new CustomDrawingWindow().TryPrepareAutomaticEmptyRendering());
        Assert.False(new CustomTemplateWindow().TryPrepareAutomaticEmptyRendering());
        Assert.Null(RenderContext.Current);
    }

    [RequiresWindowsFact]
    public void PerWindowCancellation_KeepsAnotherWindowsLeaseUntilItsOwnerReleasesIt()
    {
        var first = new Window();
        var second = new Window();
        try
        {
            Assert.True(first.TryPrepareAutomaticEmptyRendering());
            var shared = Assert.IsType<RenderContext>(RenderContext.Current);
            Assert.True(second.TryPrepareAutomaticEmptyRendering());
            Assert.Same(shared, RenderContext.Current);

            Assert.False(first.TryGetAutomaticEmptyRenderingContext(
                RenderBackend.D3D12, forceNewContext: false, out _));
            Assert.False(first.UsesAutomaticEmptySoftwareContext);
            Assert.True(second.UsesAutomaticEmptySoftwareContext);
            Assert.True(shared.IsValid);

            second.Close();
            Assert.False(shared.IsValid);
            Assert.Null(RenderContext.Current);
        }
        finally
        {
            first.Close();
            second.Close();
        }
    }

    [Fact]
    public void ContentAddedThenRemovedBeforeHandle_ReturnsToSoftwareEligibilityWithoutEagerContext()
    {
        RenderContext.Current?.Dispose();
        var window = new Window();

        Assert.Equal(
            Window.EmptyRenderingState.Undecided,
            window.CurrentEmptyRenderingState);

        window.Content = new object();
        window.Content = null;

        Assert.Equal(
            Window.EmptyRenderingState.SoftwareRequested,
            window.CurrentEmptyRenderingState);
        Assert.True(
            window.CurrentEmptyRenderingDemand.HasFlag(
                Window.EmptyRenderingDemand.Content));
        Assert.Null(RenderContext.Current);

        Assert.True(window.TryPrepareAutomaticEmptyRendering());
        Assert.Equal(
            Window.EmptyRenderingState.Software,
            window.CurrentEmptyRenderingState);
        Assert.Equal(RenderBackend.Software, RenderContext.Current!.Backend);

        window.Close();
    }

    [Fact]
    public void TransparencyBeforeHandle_LatchesFullRendering()
    {
        var window = new Window
        {
            AllowsTransparency = true,
        };

        Assert.Equal(
            Window.EmptyRenderingState.FullRequested,
            window.CurrentEmptyRenderingState);
        Assert.True(
            window.CurrentEmptyRenderingDemand.HasFlag(
                Window.EmptyRenderingDemand.Transparency));
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void CloseReenteredFromContentChange_DoesNotReacquireRenderingContexts()
    {
        RenderContext.Current?.Dispose();
        var window = new CloseBeforeBaseContentWindow
        {
            Width = 320,
            Height = 200,
            Content = new object(),
        };

        window.Show();
        var gpu = Assert.IsType<RenderContext>(window.RenderTarget?.OwnerContext);
        Assert.Equal(RenderBackend.D3D12, gpu.Backend);
        Assert.True(window.UsesAutomaticGpuContext);

        window.Content = null;

        Assert.Equal(nint.Zero, window.Handle);
        Assert.Null(window.RenderTarget);
        Assert.False(window.UsesAutomaticGpuContext);
        Assert.False(window.UsesAutomaticEmptySoftwareContext);
        Assert.False(gpu.IsValid);
        Assert.Null(RenderContext.Current);
    }

    private sealed class CustomDrawingWindow : Window
    {
        protected override void OnRender(RenderTarget renderTarget)
        {
        }
    }

    private sealed class CustomTemplateWindow : Window
    {
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
        }
    }

    private sealed class CloseBeforeBaseContentWindow : Window
    {
        private bool _closingFromContentChange;

        protected override void OnContentChanged(object? oldContent, object? newContent)
        {
            if (newContent == null &&
                Handle != nint.Zero &&
                !_closingFromContentChange)
            {
                _closingFromContentChange = true;
                Close();
            }

            base.OnContentChanged(oldContent, newContent);
        }
    }
}
