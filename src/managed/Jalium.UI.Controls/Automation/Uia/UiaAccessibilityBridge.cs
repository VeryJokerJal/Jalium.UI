using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation.Uia;

/// <summary>
/// Turns managed UIA providers into COM-callable pointers using source-generated
/// ComWrappers marshalling, so the same instance the generated interface stubs use
/// (<see cref="StrategyBasedComWrappers"/> default) builds every CCW — keeping COM
/// object identity consistent — without a process-wide <c>RegisterForMarshalling</c>.
/// </summary>
internal static unsafe class UiaComInterop
{
    /// <summary>Builds an AddRef'd IRawElementProviderSimple* CCW for a managed provider.</summary>
    internal static nint ProviderToPointer(IRawElementProviderSimple provider)
        => (nint)ComInterfaceMarshaller<IRawElementProviderSimple>.ConvertToUnmanaged(provider);

    /// <summary>Releases a pointer obtained from <see cref="ProviderToPointer"/>.</summary>
    internal static void FreeProviderPointer(nint p)
    {
        if (p != 0) ComInterfaceMarshaller<IRawElementProviderSimple>.Free((void*)p);
    }

    /// <summary>Wraps a native IRawElementProviderSimple* as an RCW (does not consume a ref).</summary>
    internal static IRawElementProviderSimple? ProviderFromPointer(nint p)
        => p == 0 ? null : ComInterfaceMarshaller<IRawElementProviderSimple>.ConvertToManaged((void*)p);
}

/// <summary>
/// Manages the bridge between AutomationPeer and Windows UI Automation.
/// </summary>
/// <remarks>
/// <para>
/// Providers are exposed as source-generated COM ([GeneratedComClass]/[GeneratedComInterface]),
/// which works under both JIT and NativeAOT. Every outbound call marshals the provider to a
/// COM pointer explicitly through <see cref="UiaComInterop"/> and releases it afterward.
/// </para>
/// <para>
/// The <see cref="s_comInteropUnavailable"/> latch is retained as belt-and-suspenders: with the
/// source-generated path in place the marshal now succeeds and the latch never trips, but if COM
/// marshalling is somehow unavailable (or the kill-switch is set) the first failure latches and
/// every subsequent UIA interop call becomes a no-op so a third-party UIA client (Narrator,
/// Inspect, a translation/OCR tool such as Qwen/Tongyi) can never crash the process.
/// </para>
/// </remarks>
internal static class UiaAccessibilityBridge
{
    private static readonly ConditionalWeakTable<AutomationPeer, AutomationPeerProvider> s_providers = new();

    /// <summary>
    /// Latched true when managed→COM marshalling is unavailable (or force-disabled via the
    /// kill-switch). Once set, all UIA interop is skipped.
    /// </summary>
    private static volatile bool s_comInteropUnavailable;

    static UiaAccessibilityBridge()
    {
        // Emergency kill-switch: JALIUM_UIA_AOT_DISABLE=1 forces the UIA COM path off (falls
        // back to "no accessibility, never crash") without a rebuild — an escape hatch if the
        // source-generated COM ABI ever misbehaves against a real UIA client in the field.
        if (OperatingSystem.IsWindows())
        {
            var disable = Environment.GetEnvironmentVariable("JALIUM_UIA_AOT_DISABLE");
            if (disable is "1" or "true" or "TRUE")
                s_comInteropUnavailable = true;
        }
    }

    /// <summary>True once managed→COM marshalling has been proven unavailable in this runtime.</summary>
    internal static bool IsComInteropUnavailable => s_comInteropUnavailable;

    /// <summary>Latch COM interop as permanently unavailable for the lifetime of the process.</summary>
    internal static void MarkComInteropUnavailable() => s_comInteropUnavailable = true;

    /// <summary>
    /// Classifies an exception from a UIA marshalling/P/Invoke boundary as "managed→COM
    /// marshalling is not available in this runtime" (missing ComWrappers / unsupported directive).
    /// </summary>
    internal static bool IsMarshallingFailure(Exception ex)
        => ex is NotSupportedException or MarshalDirectiveException;

    internal static AutomationPeerProvider GetOrCreateProvider(AutomationPeer peer, nint hwnd)
    {
        return s_providers.GetValue(peer, p => new AutomationPeerProvider(p, hwnd));
    }

    internal static bool TryGetProvider(AutomationPeer peer, out AutomationPeerProvider? provider)
    {
        return s_providers.TryGetValue(peer, out provider);
    }

    /// <summary>
    /// Handles a <c>WM_GETOBJECT</c> / <c>UiaRootObjectId</c> request by marshalling the window's
    /// root provider to native UIA. Returns the LRESULT to hand back from the window procedure,
    /// or <see cref="nint.Zero"/> if no provider could be returned. The event sink is armed only
    /// after a marshal actually succeeds.
    /// </summary>
    internal static nint TryGetRootProvider(AutomationPeer peer, nint hwnd, nint wParam, nint lParam)
    {
        if (!OperatingSystem.IsWindows()) return nint.Zero;
        if (s_comInteropUnavailable) return nint.Zero;

        var provider = GetOrCreateProvider(peer, hwnd);
        nint pProvider = nint.Zero;
        nint result;
        try
        {
            pProvider = UiaComInterop.ProviderToPointer(provider);
            result = UiaNativeMethods.UiaReturnRawElementProvider(hwnd, wParam, lParam, pProvider);
        }
        catch (Exception ex)
        {
            // Never let an exception escape the window procedure path: latch on a genuine
            // marshalling failure, swallow anything else and just decline the provider.
            if (IsMarshallingFailure(ex)) MarkComInteropUnavailable();
            return nint.Zero;
        }
        finally
        {
            UiaComInterop.FreeProviderPointer(pProvider);
        }

        AutomationPeer.EventSink ??= new UiaAutomationEventSink();
        return result;
    }

    internal static void RaiseAutomationEvent(AutomationPeer peer, AutomationEvents eventId)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (s_comInteropUnavailable) return;
        if (!UiaNativeMethods.UiaClientsAreListening()) return;
        if (!TryGetProvider(peer, out var provider) || provider == null) return;

        int uiaEventId = UiaConstants.MapAutomationEvent(eventId);
        if (uiaEventId == 0) return;

        nint pProvider = nint.Zero;
        try
        {
            pProvider = UiaComInterop.ProviderToPointer(provider);
            UiaNativeMethods.UiaRaiseAutomationEvent(pProvider, uiaEventId);
        }
        catch (Exception ex)
        {
            // Never let an exception reach the UI/layout thread (the original crash class):
            // latch the kill-switch on a genuine marshalling failure, best-effort-swallow the
            // rest — a dropped UIA event is acceptable, crashing the app is not.
            if (IsMarshallingFailure(ex)) MarkComInteropUnavailable();
        }
        finally
        {
            UiaComInterop.FreeProviderPointer(pProvider);
        }
    }

    internal static void RaisePropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (s_comInteropUnavailable) return;
        if (!UiaNativeMethods.UiaClientsAreListening()) return;
        if (!TryGetProvider(peer, out var provider) || provider == null) return;

        int uiaPropertyId = UiaConstants.MapAutomationProperty(property);
        if (uiaPropertyId == 0) return;

        nint pProvider = nint.Zero;
        UiaVariant vOld = default;
        UiaVariant vNew = default;
        try
        {
            pProvider = UiaComInterop.ProviderToPointer(provider);
            vOld = UiaVariant.From(oldValue);
            vNew = UiaVariant.From(newValue);
            UiaNativeMethods.UiaRaiseAutomationPropertyChangedEvent(pProvider, uiaPropertyId, vOld, vNew);
        }
        catch (Exception ex)
        {
            // Never let an exception reach the UI/layout thread (the original crash class):
            // latch the kill-switch on a genuine marshalling failure, best-effort-swallow the
            // rest — a dropped UIA event is acceptable, crashing the app is not.
            if (IsMarshallingFailure(ex)) MarkComInteropUnavailable();
        }
        finally
        {
            vOld.Clear();
            vNew.Clear();
            UiaComInterop.FreeProviderPointer(pProvider);
        }
    }

    internal static void RaiseFocusChanged(AutomationPeer peer)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (s_comInteropUnavailable) return;
        if (!UiaNativeMethods.UiaClientsAreListening()) return;
        if (!TryGetProvider(peer, out var provider) || provider == null) return;

        nint pProvider = nint.Zero;
        try
        {
            pProvider = UiaComInterop.ProviderToPointer(provider);
            UiaNativeMethods.UiaRaiseAutomationEvent(pProvider, UiaConstants.UIA_AutomationFocusChangedEventId);
        }
        catch (Exception ex)
        {
            // Never let an exception reach the UI/layout thread (the original crash class):
            // latch the kill-switch on a genuine marshalling failure, best-effort-swallow the
            // rest — a dropped UIA event is acceptable, crashing the app is not.
            if (IsMarshallingFailure(ex)) MarkComInteropUnavailable();
        }
        finally
        {
            UiaComInterop.FreeProviderPointer(pProvider);
        }
    }

    internal static void RaiseStructureChanged(AutomationPeer peer, StructureChangeType changeType)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (s_comInteropUnavailable) return;
        if (!UiaNativeMethods.UiaClientsAreListening()) return;
        if (!TryGetProvider(peer, out var provider) || provider == null) return;

        nint pProvider = nint.Zero;
        try
        {
            var runtimeId = provider.GetRuntimeIdArray();
            pProvider = UiaComInterop.ProviderToPointer(provider);
            UiaNativeMethods.UiaRaiseStructureChangedEvent(pProvider, (int)changeType, runtimeId, runtimeId.Length);
        }
        catch (Exception ex)
        {
            // Never let an exception reach the UI/layout thread (the original crash class):
            // latch the kill-switch on a genuine marshalling failure, best-effort-swallow the
            // rest — a dropped UIA event is acceptable, crashing the app is not.
            if (IsMarshallingFailure(ex)) MarkComInteropUnavailable();
        }
        finally
        {
            UiaComInterop.FreeProviderPointer(pProvider);
        }
    }
}

internal sealed class UiaAutomationEventSink : IAutomationEventSink
{
    public void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents eventId)
        => UiaAccessibilityBridge.RaiseAutomationEvent(peer, eventId);

    public void OnPropertyChangedRaised(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
        => UiaAccessibilityBridge.RaisePropertyChanged(peer, property, oldValue, newValue);

    public void OnFocusChanged(AutomationPeer peer)
        => UiaAccessibilityBridge.RaiseFocusChanged(peer);
}
