using Jalium.UI.Diagnostics;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI;

public partial class Window
{
    [Flags]
    internal enum EmptyRenderingDemand
    {
        None = 0,
        Content = 1 << 0,
        Transparency = 1 << 1,
        Effect = 1 << 2,
        EmbeddedSurface = 1 << 3,
        CustomDrawing = 1 << 4,
        RenderingEngine = 1 << 5,
        DeveloperOverlay = 1 << 6,
        SystemBackdrop = 1 << 7,
        ForcedContextReplacement = 1 << 8,
    }

    internal enum EmptyRenderingState
    {
        Undecided,
        Software,
        SoftwareRequested,
        FullRequested,
        Full,
    }

    internal readonly record struct EmptyRenderingPolicyInput(
        bool IsWindows,
        RenderBackend RequestedBackend,
        bool ForceNewContext,
        bool HasContent,
        bool AllowsTransparency,
        bool HasSystemBackdrop,
        bool HasEffect,
        bool HasBackdropEffect,
        bool HasCustomDrawing,
        bool HasTitleBarCommands,
        bool HasOverlayContent,
        bool HasCustomTemplate,
        bool HasUnexpectedVisualChildren,
        bool RequiresEmbeddedSurface,
        bool HasExplicitRenderingEngine);

    private EmptyRenderingState _emptyRenderingState;
    private EmptyRenderingDemand _emptyRenderingDemand;
    private RenderContext.EmptyWindowSoftwareContextLease? _emptyWindowSoftwareContextLease;
    private RenderContext.AutomaticWindowContextLease? _automaticWindowContextLease;
    private int _emptyRenderingPromotionScheduled;
    private int _emptyRenderingPromotionInProgress;
    // An HWND that has presented a DXGI flip swap chain can never display GDI
    // updates again, even after the swap chain is destroyed. Keep automatic
    // D3D12 presentation in a detachable composition tree so the same HWND can
    // return to the empty Software renderer. Preserve that surface choice on
    // device recovery; the parent HWND's redirection style remains unchanged.
    private bool _preserveGdiPresentation;

    internal static bool ShouldPreserveGdiPresentation(
        bool isWindows, RenderBackend backend, bool automaticBackendSelection) =>
        isWindows && backend == RenderBackend.D3D12 && automaticBackendSelection;

    /// <summary>
    /// Pure policy seam for the Windows Auto empty-window optimization. Custom
    /// title-bar visuals and the Window background are intentionally absent from
    /// this input: both are rendered by the existing Software backend. Every
    /// condition that changes the surface contract or introduces application
    /// drawing rejects the lightweight path.
    /// </summary>
    internal static bool ShouldUseAutomaticEmptySoftware(
        in EmptyRenderingPolicyInput input)
        => input.IsWindows &&
           input.RequestedBackend == RenderBackend.Auto &&
           !input.ForceNewContext &&
           !input.HasContent &&
           !input.AllowsTransparency &&
           !input.HasSystemBackdrop &&
           !input.HasEffect &&
           !input.HasBackdropEffect &&
           !input.HasCustomDrawing &&
           !input.HasTitleBarCommands &&
           !input.HasOverlayContent &&
           !input.HasCustomTemplate &&
           !input.HasUnexpectedVisualChildren &&
           !input.RequiresEmbeddedSurface &&
           !input.HasExplicitRenderingEngine;

    /// <summary>
    /// Acquires the shared Software lease before HWND creation. A successful
    /// result lets EnsureHandle skip its Auto backend probe, which otherwise
    /// creates the process GPU context before the empty window has rendered.
    /// </summary>
    internal bool TryPrepareAutomaticEmptyRendering()
    {
        if (_emptyRenderingState == EmptyRenderingState.Software)
        {
            return _emptyWindowSoftwareContextLease?.Context.IsValid == true;
        }

        var policy = CaptureEmptyRenderingPolicy(
            _renderBackendOverride,
            forceNewContext: false);
        if (!ShouldUseAutomaticEmptySoftware(policy))
        {
            _emptyRenderingState = EmptyRenderingState.Full;
            return false;
        }

        var existingLease = _emptyWindowSoftwareContextLease;
        if (existingLease?.Context.IsValid == true)
        {
            _emptyRenderingState = EmptyRenderingState.Software;
            return true;
        }
        if (existingLease != null)
        {
            ReleaseEmptyRenderingLease();
        }

        var lease = RenderContext.TryAcquireEmptyWindowSoftwareContext();
        if (lease == null)
        {
            _emptyRenderingState = EmptyRenderingState.Full;
            return false;
        }

        _emptyWindowSoftwareContextLease = lease;
        _emptyRenderingState = EmptyRenderingState.Software;
        return true;
    }

    /// <summary>
    /// Supplies the prepared Software context to EnsureRenderTarget. The caller
    /// falls back to the ordinary GetOrCreateCurrent path when this returns false.
    /// </summary>
    internal bool TryGetAutomaticEmptyRenderingContext(
        RenderBackend requestedBackend,
        bool forceNewContext,
        out RenderContext context)
    {
        if (_emptyRenderingState == EmptyRenderingState.Software &&
            (requestedBackend != RenderBackend.Auto || forceNewContext))
        {
            RequireFullRendering(EmptyRenderingDemand.ForcedContextReplacement);
        }

        if (_emptyRenderingState == EmptyRenderingState.Undecided &&
            requestedBackend == RenderBackend.Auto &&
            !forceNewContext)
        {
            _ = TryPrepareAutomaticEmptyRendering();
        }

        if (_emptyRenderingState == EmptyRenderingState.SoftwareRequested &&
            RenderTarget == null &&
            requestedBackend == RenderBackend.Auto &&
            !forceNewContext)
        {
            _ = TryPrepareAutomaticEmptyRendering();
        }

        var lease = _emptyWindowSoftwareContextLease;
        if (_emptyRenderingState == EmptyRenderingState.Software &&
            requestedBackend == RenderBackend.Auto &&
            !forceNewContext &&
            lease?.Context.IsValid == true)
        {
            if (!RenderContext.IsAutomaticEmptyWindowSoftwareAllowed())
            {
                RequireFullRendering(EmptyRenderingDemand.ForcedContextReplacement);
                context = null!;
                return false;
            }

            context = lease.Context;
            return true;
        }

        context = null!;
        return false;
    }

    /// <summary>
    /// Latches a reason that requires the ordinary rendering path at the current
    /// point in time. Actual properties are re-evaluated before a later automatic
    /// GPU-to-Software transition; irreversible surface contracts (embedded
    /// content and an explicit rendering engine) remain represented in the demand
    /// mask and therefore continue to block that transition.
    /// </summary>
    internal void RequireFullRendering(EmptyRenderingDemand demand)
    {
        if (demand == EmptyRenderingDemand.None)
        {
            return;
        }

        _emptyRenderingDemand |= demand;
        StopEmptyStorageCompaction();
        if (_emptyRenderingState == EmptyRenderingState.Full &&
            _emptyWindowSoftwareContextLease == null)
        {
            return;
        }

        _emptyRenderingState = EmptyRenderingState.FullRequested;

        // A lease prepared before HWND/target creation has no native target
        // depending on it and can be returned immediately. Once a target exists,
        // its drawing context may still create backend-bound resources; the lease
        // stays until the promotion/teardown hook confirms those resources are gone.
        var lease = _emptyWindowSoftwareContextLease;
        if (RenderTarget == null ||
            (lease != null &&
             !ReferenceEquals(RenderTarget.OwnerContext, lease.Context)))
        {
            ReleaseEmptyRenderingLease();
        }
    }

    /// <summary>
    /// Requests the transition from the provisional Software target to
    /// the process' ordinary Auto backend. Property and visual-tree callbacks can
    /// run while a frame is being captured, so the actual target replacement is
    /// deferred to render priority whenever the current draw owns the resources.
    /// </summary>
    internal void RequestFullRendering(EmptyRenderingDemand demand)
    {
        RequireFullRendering(demand);

        if (_emptyRenderingState != EmptyRenderingState.FullRequested ||
            _isClosing ||
            _managedTeardownStarted)
        {
            return;
        }

        if (HasRenderFlag(RenderFlag_Rendering) ||
            Volatile.Read(ref _emptyRenderingPromotionInProgress) != 0)
        {
            ScheduleEmptyRenderingPromotion();
            return;
        }

        // Content may return while a GPU-to-Software transition is merely queued.
        // If the live target never left the ordinary context and the auxiliary
        // Software lease was released above, there is nothing to rebuild.
        if (_emptyWindowSoftwareContextLease == null &&
            RenderTarget is { IsValid: true })
        {
            _emptyRenderingState = EmptyRenderingState.Full;
            return;
        }

        if (_emptyWindowSoftwareContextLease == null ||
            Handle == nint.Zero)
        {
            return;
        }

        PromoteAutomaticEmptyRenderingIfNeeded();
    }

    /// <summary>
    /// Promotes an already-rendering empty Software window. The process Current is
    /// resolved first while the old target remains usable. Only after that succeeds
    /// do we drain target-owned resources, dispose the old target, and create the
    /// replacement. The Software lease remains pinned across the gap so a failed
    /// GPU target creation cannot destroy the backend underneath cleanup/recovery.
    /// </summary>
    internal void PromoteAutomaticEmptyRenderingIfNeeded()
    {
        if (_emptyRenderingState != EmptyRenderingState.FullRequested ||
            _emptyWindowSoftwareContextLease == null ||
            Handle == nint.Zero ||
            _isClosing ||
            _managedTeardownStarted)
        {
            return;
        }

        if (HasRenderFlag(RenderFlag_Rendering))
        {
            ScheduleEmptyRenderingPromotion();
            return;
        }

        if (Interlocked.CompareExchange(ref _emptyRenderingPromotionInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (!StopRenderThread())
            {
                ScheduleEmptyRenderingPromotion();
                return;
            }

            lock (_rtChannelLock)
            {
                _rtPendingFrame = null;
            }

            var lease = _emptyWindowSoftwareContextLease;
            if (lease == null)
            {
                _emptyRenderingState = EmptyRenderingState.Full;
                return;
            }

            RenderContext fullContext;
            using (StartupDiagnostics.Begin(
                       "Window.EmptyRenderingPromoteContext",
                       blocksUiThread: true))
            {
                fullContext = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
            }

            // Auto can legitimately remain Software when no GPU backend is
            // available. In that case the existing target is already a valid
            // full renderer; convert the provisional metadata in place and keep
            // every pixel/resource intact.
            if (RenderTarget is { IsValid: true } currentTarget &&
                ReferenceEquals(fullContext, lease.Context) &&
                ReferenceEquals(currentTarget.OwnerContext, fullContext))
            {
                ConfigureRenderingModeForTarget(
                    Jalium.UI.Hosting.RenderingModeOptions.Current,
                    currentTarget.SupportsPartialPresentation);
                CompleteFullRenderingTransition();
                StartFramePacerIfSupported();
                StartRenderThreadIfSupported();
                StartExternalPresentPacingIfSupported();
                InvalidateAfterEmptyRenderingPromotion();
                return;
            }

            if (RenderTarget != null)
            {
                // Retained layers and drawing-context caches contain native
                // handles owned by the old Software context. Drain them before
                // disposing its target; the target OwnerContext pin keeps that
                // backend alive until the last handle has been destroyed.
                Visual.ReleaseRetainedLayersRecursive(this);
                ReleaseDrawingContextBeforeRenderTargetDisposal(clearAllCaches: true);
                StopFramePacer();
                StopExternalPresentPacing();

                var oldRenderTarget = RenderTarget;
                RenderTarget = null;
                oldRenderTarget.Dispose();
            }

            EnsureRenderTarget();
            if (RenderTarget is not { IsValid: true })
            {
                throw new InvalidOperationException(
                    "The full rendering target was not created during empty-window promotion.");
            }

            CompleteFullRenderingTransition();
            InvalidateAfterEmptyRenderingPromotion();
        }
        catch (Exception ex)
        {
            LogRenderFailure(ex, "EmptyRendering.Promote");

            // Keep the lease/context alive if target replacement failed after the
            // old target was drained. The regular recovery/invalidation pipeline
            // gets another chance without using a disposed Software backend.
            RequestFullInvalidation();
            InvalidateWindow();
        }
        finally
        {
            Volatile.Write(ref _emptyRenderingPromotionInProgress, 0);
            if (_emptyRenderingState == EmptyRenderingState.SoftwareRequested &&
                !_isClosing &&
                !_managedTeardownStarted)
            {
                ScheduleEmptyRenderingPromotion();
            }
        }
    }

    /// <summary>
    /// Re-evaluates the live window after application content/drawing demand was
    /// removed. An ordinary Windows Auto window can migrate back to the shared
    /// Software backend; explicit rendering selections and non-empty surface
    /// contracts remain on their established backend.
    /// </summary>
    internal void RequestAutomaticEmptyRenderingIfEligible()
    {
        if (_isClosing || _managedTeardownStarted ||
            !CanUseAutomaticEmptySoftwareNow())
        {
            return;
        }

        if (_emptyRenderingState == EmptyRenderingState.Software &&
            _emptyWindowSoftwareContextLease?.Context.IsValid == true)
        {
            return;
        }

        _emptyRenderingState = EmptyRenderingState.SoftwareRequested;
        StopEmptyStorageCompaction();

        // Before HWND creation this is only a policy decision. Show/EnsureHandle
        // acquires the Software lease at the same point as a natively-empty window,
        // so setting Content and clearing it again has no eager backend side effect.
        if (Handle == nint.Zero)
        {
            return;
        }

        if (HasRenderFlag(RenderFlag_Rendering) ||
            _renderRecoveryInProgress ||
            Volatile.Read(ref _emptyRenderingPromotionInProgress) != 0)
        {
            ScheduleEmptyRenderingPromotion();
            return;
        }

        RestoreAutomaticEmptyRenderingIfNeeded();
    }

    /// <summary>
    /// Replaces an automatically-selected full target with a Software target after
    /// all application rendering demand has disappeared. The old target, drawing
    /// caches and retained layers are drained before its automatic context lease is
    /// released. Independent external resources keep their own context pins.
    /// </summary>
    internal void RestoreAutomaticEmptyRenderingIfNeeded()
    {
        if (_emptyRenderingState != EmptyRenderingState.SoftwareRequested ||
            Handle == nint.Zero ||
            _isClosing ||
            _managedTeardownStarted)
        {
            return;
        }

        if (!CanUseAutomaticEmptySoftwareNow())
        {
            _emptyRenderingState = EmptyRenderingState.Full;
            return;
        }

        if (HasRenderFlag(RenderFlag_Rendering) || _renderRecoveryInProgress)
        {
            ScheduleEmptyRenderingPromotion();
            return;
        }

        if (Interlocked.CompareExchange(ref _emptyRenderingPromotionInProgress, 1, 0) != 0)
        {
            return;
        }

        bool renderThreadStopped = false;
        try
        {
            if (!StopRenderThread())
            {
                ScheduleEmptyRenderingPromotion();
                return;
            }
            renderThreadStopped = true;

            lock (_rtChannelLock)
            {
                _rtPendingFrame = null;
            }

            if (_emptyRenderingState != EmptyRenderingState.SoftwareRequested ||
                _isClosing ||
                _managedTeardownStarted ||
                !CanUseAutomaticEmptySoftwareNow())
            {
                return;
            }

            var softwareLease = _emptyWindowSoftwareContextLease;
            if (softwareLease?.Context.IsValid != true)
            {
                ReleaseEmptyRenderingLease();
                softwareLease = RenderContext.TryAcquireEmptyWindowSoftwareContext();
                if (softwareLease == null)
                {
                    _emptyRenderingState = EmptyRenderingState.Full;
                    return;
                }
                _emptyWindowSoftwareContextLease = softwareLease;
            }

            // A machine whose normal Auto backend is already Software needs no
            // target churn. Adopt the lease and only update the reversible state.
            if (RenderTarget is { IsValid: true, Backend: RenderBackend.Software } softwareTarget &&
                ReferenceEquals(softwareTarget.OwnerContext, softwareLease.Context))
            {
                ReleaseAutomaticRenderingContextLease();
                _emptyRenderingState = EmptyRenderingState.Software;
                InvalidateAfterEmptyRenderingPromotion();
                return;
            }

            // Content can return through a re-entrant callback while the render
            // worker is joining or the auxiliary Software context is constructed.
            // In that case keep the intact GPU target and release the unused lease.
            if (_emptyRenderingState != EmptyRenderingState.SoftwareRequested ||
                _isClosing ||
                _managedTeardownStarted ||
                !CanUseAutomaticEmptySoftwareNow())
            {
                ReleaseEmptyRenderingLease();
                return;
            }

            if (RenderTarget != null)
            {
                Visual.ReleaseRetainedLayersRecursive(this);
                ReleaseDrawingContextBeforeRenderTargetDisposal(clearAllCaches: true);
                StopFramePacer();
                StopExternalPresentPacing();

                var oldRenderTarget = RenderTarget;
                RenderTarget = null;
                oldRenderTarget.Dispose();
            }

            // The window's target/caches no longer reference the GPU backend. The
            // final automatic-window lease can now detach Current; external target
            // or wrapper pins defer its physical native destruction independently.
            ReleaseAutomaticRenderingContextLease();

            if (_isClosing || _managedTeardownStarted)
            {
                return;
            }

            _emptyRenderingState = EmptyRenderingState.Software;
            EnsureRenderTarget();
            if (RenderTarget is not { IsValid: true, Backend: RenderBackend.Software } replacement ||
                !ReferenceEquals(replacement.OwnerContext, softwareLease.Context))
            {
                throw new InvalidOperationException(
                    "The Software rendering target was not created during automatic empty-window restoration.");
            }

            InvalidateAfterEmptyRenderingPromotion();
        }
        catch (Exception ex)
        {
            LogRenderFailure(ex, "EmptyRendering.RestoreSoftware");

            // If the old target was already removed, retain the Software lease so
            // the regular render-target retry can complete without resurrecting a
            // context whose last consumer has already gone away.
            bool needsSoftwareRetry = RenderTarget == null &&
                                      _emptyWindowSoftwareContextLease?.Context.IsValid == true;
            if (!needsSoftwareRetry &&
                _emptyWindowSoftwareContextLease is { } unusedLease &&
                !ReferenceEquals(RenderTarget?.OwnerContext, unusedLease.Context))
            {
                ReleaseEmptyRenderingLease();
            }
            _emptyRenderingState = needsSoftwareRetry
                ? EmptyRenderingState.Software
                : EmptyRenderingState.Full;
            RequestFullInvalidation();
            InvalidateWindow();
        }
        finally
        {
            Volatile.Write(ref _emptyRenderingPromotionInProgress, 0);

            if (renderThreadStopped &&
                RenderTarget is { IsValid: true } &&
                !_isClosing &&
                !_managedTeardownStarted)
            {
                StartFramePacerIfSupported();
                StartRenderThreadIfSupported();
                StartExternalPresentPacingIfSupported();
            }

            if (_emptyRenderingState == EmptyRenderingState.FullRequested &&
                !_isClosing &&
                !_managedTeardownStarted)
            {
                ScheduleEmptyRenderingPromotion();
            }
        }
    }

    /// <summary>
    /// Completes a successful transition to the ordinary rendering path. Call
    /// this after replacing/disposing the old target. If Auto resolved to the
    /// same normal Software Current because no GPU is available, releasing the
    /// lease is also safe: Current and the target retain their own ownership.
    /// </summary>
    internal void CompleteFullRenderingTransition()
    {
        var lease = _emptyWindowSoftwareContextLease;
        if (lease != null &&
            RenderTarget?.OwnerContext is { } owner &&
            ReferenceEquals(owner, lease.Context) &&
            (!ReferenceEquals(RenderContext.Current, lease.Context) ||
             RenderContext.IsEmptyWindowProvisionalCurrent(lease.Context)))
        {
            // The old auxiliary/provisional target is still active. Its render
            // resources must be released before the final pool lease can retire
            // the backend.
            return;
        }

        ReleaseEmptyRenderingLease();
        _emptyRenderingState = EmptyRenderingState.Full;
    }

    /// <summary>
    /// Releases the lease after Window render resources have definitely stopped.
    /// This is the teardown counterpart for both immediate and deferred render
    /// thread shutdown paths.
    /// </summary>
    internal void ReleaseEmptyRenderingAfterRenderResources()
    {
        ReleaseEmptyRenderingLease();
        ReleaseAutomaticRenderingContextLease();
        if (_emptyRenderingState != EmptyRenderingState.Undecided)
        {
            _emptyRenderingState = EmptyRenderingState.Full;
        }
    }

    /// <summary>
    /// Synchronizes this Window's automatic-context ownership with a newly-created
    /// target. Acquire the replacement lease first so a forced context rebuild
    /// cannot briefly drop the last consumer and destroy the old backend before
    /// its target/caches have finished tearing down.
    /// </summary>
    internal void TrackAutomaticRenderingContext(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var currentLease = Volatile.Read(ref _automaticWindowContextLease);
        if (ReferenceEquals(currentLease?.Context, context))
        {
            return;
        }

        var replacement = RenderContext.TryAcquireAutomaticWindowContext(context);
        var previous = Interlocked.Exchange(
            ref _automaticWindowContextLease,
            replacement);
        previous?.Dispose();
    }

    internal EmptyRenderingState CurrentEmptyRenderingState => _emptyRenderingState;

    internal EmptyRenderingDemand CurrentEmptyRenderingDemand => _emptyRenderingDemand;

    internal bool UsesAutomaticEmptySoftwareContext
        => _emptyWindowSoftwareContextLease != null &&
           _emptyRenderingState == EmptyRenderingState.Software;

    internal bool UsesAutomaticGpuContext
        => _automaticWindowContextLease != null;

    private void ScheduleEmptyRenderingPromotion()
    {
        if (_isClosing || _managedTeardownStarted ||
            Interlocked.Exchange(ref _emptyRenderingPromotionScheduled, 1) != 0)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            () =>
            {
                Volatile.Write(ref _emptyRenderingPromotionScheduled, 0);
                if (_emptyRenderingState == EmptyRenderingState.SoftwareRequested)
                {
                    RestoreAutomaticEmptyRenderingIfNeeded();
                }
                else if (_emptyRenderingState == EmptyRenderingState.FullRequested)
                {
                    PromoteAutomaticEmptyRenderingIfNeeded();
                }
            });
    }

    private void InvalidateAfterEmptyRenderingPromotion()
    {
        RequestFullInvalidation();
        InvalidateMeasure();
        InvalidateWindow();
    }

    private bool CanUseAutomaticEmptySoftwareNow()
    {
        var policy = CaptureEmptyRenderingPolicy(
            _renderBackendOverride,
            forceNewContext: false);
        return ShouldUseAutomaticEmptySoftware(policy) &&
               RenderContext.IsAutomaticEmptyWindowSoftwareAllowed();
    }

    private EmptyRenderingPolicyInput CaptureEmptyRenderingPolicy(
        RenderBackend requestedBackend,
        bool forceNewContext)
        => new(
            IsWindows: OperatingSystem.IsWindows(),
            RequestedBackend: requestedBackend,
            ForceNewContext: forceNewContext,
            HasContent: Content != null,
            AllowsTransparency: AllowsTransparency,
            HasSystemBackdrop: SystemBackdrop != WindowBackdropType.None,
            HasEffect: Effect != null,
            HasBackdropEffect: BackdropEffect != null,
            HasCustomDrawing: HasCustomWindowDrawing(),
            HasTitleBarCommands:
                LeftWindowCommands != null || RightWindowCommands != null,
            HasOverlayContent:
                OverlayLayer.Children.Count != 0 ||
                ActiveExternalPopups.Count != 0 ||
                ActiveContentDialog != null ||
                ActiveInPlaceDialogs.Count != 0,
            HasCustomTemplate:
                Template != null ||
                ContentTemplate != null ||
                ContentTemplateSelector != null ||
                ContentStringFormat != null ||
                ContentTransition != null ||
                TransitionMode != null ||
                CustomTitleBarStyle != null ||
                TitleBarStyleKey != null ||
                HasCustomWindowTemplateLifecycle(),
            HasUnexpectedVisualChildren:
                VisualChildrenCount > (TitleBar == null ? 2 : 3),
            RequiresEmbeddedSurface:
                (_emptyRenderingDemand & EmptyRenderingDemand.EmbeddedSurface) != 0,
            HasExplicitRenderingEngine:
                (_emptyRenderingDemand & EmptyRenderingDemand.RenderingEngine) != 0);

    private bool HasCustomWindowDrawing()
    {
        if (GetType() == typeof(Window))
        {
            return false;
        }

        Action<RenderTarget> renderer = OnRender;
        return renderer.Method.DeclaringType != typeof(Window);
    }

    private bool HasCustomWindowTemplateLifecycle()
    {
        if (GetType() == typeof(Window))
        {
            return false;
        }

        Action applyTemplate = OnApplyTemplate;
        return applyTemplate.Method.DeclaringType != typeof(Window);
    }

    private void ReleaseEmptyRenderingLease()
    {
        StopEmptyStorageCompaction();
        var lease = Interlocked.Exchange(
            ref _emptyWindowSoftwareContextLease,
            null);
        lease?.Dispose();
    }

    private void ReleaseAutomaticRenderingContextLease()
    {
        var lease = Interlocked.Exchange(
            ref _automaticWindowContextLease,
            null);
        lease?.Dispose();
    }
}
