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
/// which works under both JIT and NativeAOT. Each provider keeps one stable AddRef'd COM
/// pointer while its window is connected to UIA; outbound UIA calls reuse that pointer so
/// UIAutomationCore never observes a CCW that was released between events.
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
    private static readonly object s_windowProvidersGate = new();
    private static readonly Dictionary<nint, HashSet<AutomationPeerProvider>> s_windowProviders = new();
    private static readonly bool s_raiseNativeEvents = IsEnvironmentEnabled("JALIUM_UIA_RAISE_EVENTS", defaultValue: true);

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

            UiaTrace.Log($"UiaAccessibilityBridge init disabled={s_comInteropUnavailable} raiseNativeEvents={s_raiseNativeEvents}");
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
        var provider = s_providers.GetValue(peer, p => new AutomationPeerProvider(p, hwnd));
        provider.EnsureHwnd(hwnd);
        TrackProviderForWindow(provider);
        return provider;
    }

    internal static bool TryGetProvider(AutomationPeer peer, out AutomationPeerProvider? provider)
    {
        return s_providers.TryGetValue(peer, out provider);
    }

    internal static void NotifyWindowDestroyed(nint hwnd)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (hwnd == nint.Zero) return;

        var hasWindowProviders = HasProvidersForWindow(hwnd);
        UiaTrace.Log($"NotifyWindowDestroyed hwnd=0x{FormatPointer(hwnd)} hasProviders={hasWindowProviders} sink={AutomationPeer.EventSink != null}");
        if ((hasWindowProviders || AutomationPeer.EventSink != null) && !s_comInteropUnavailable)
        {
            try
            {
                _ = UiaNativeMethods.UiaReturnRawElementProvider(hwnd, nint.Zero, nint.Zero, nint.Zero);
                UiaTrace.Log($"NotifyWindowDestroyed cleared hwnd=0x{FormatPointer(hwnd)}");
            }
            catch (Exception ex)
            {
                if (IsMarshallingFailure(ex)) MarkComInteropUnavailable();
                UiaTrace.Log($"NotifyWindowDestroyed failed hwnd=0x{FormatPointer(hwnd)} ex={ex.GetType().Name}: {ex.Message}");
            }
        }

        ReleaseProvidersForWindow(hwnd);
    }

    private static void TrackProviderForWindow(AutomationPeerProvider provider)
    {
        var hwnd = provider.Hwnd;
        if (hwnd == nint.Zero)
            return;

        lock (s_windowProvidersGate)
        {
            if (!s_windowProviders.TryGetValue(hwnd, out var providers))
            {
                providers = [];
                s_windowProviders.Add(hwnd, providers);
            }

            providers.Add(provider);
        }
    }

    private static bool HasProvidersForWindow(nint hwnd)
    {
        lock (s_windowProvidersGate)
        {
            return s_windowProviders.TryGetValue(hwnd, out var providers) && providers.Count > 0;
        }
    }

    private static void ReleaseProvidersForWindow(nint hwnd)
    {
        HashSet<AutomationPeerProvider>? providers;
        lock (s_windowProvidersGate)
        {
            if (!s_windowProviders.Remove(hwnd, out providers))
                return;
        }

        foreach (var provider in providers)
            provider.ReleaseProviderPointerForWindow(hwnd);
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
        try
        {
            var pProvider = provider.GetOrCreateProviderPointer();
            if (pProvider == nint.Zero) return nint.Zero;

            var result = UiaNativeMethods.UiaReturnRawElementProvider(hwnd, wParam, lParam, pProvider);
            AutomationPeer.EventSink ??= new UiaAutomationEventSink();
            UiaTrace.Log($"TryGetRootProvider S_OK hwnd=0x{FormatPointer(hwnd)} provider=0x{FormatPointer(pProvider)} result=0x{FormatPointer(result)}");
            return result;
        }
        catch (Exception ex)
        {
            // Never let an exception escape the window procedure path: latch on a genuine
            // marshalling failure, swallow anything else and just decline the provider.
            if (IsMarshallingFailure(ex)) MarkComInteropUnavailable();
            UiaTrace.Log($"TryGetRootProvider failed hwnd=0x{FormatPointer(hwnd)} ex={ex.GetType().Name}: {ex.Message}");
            return nint.Zero;
        }
    }

    internal static void RaiseAutomationEvent(AutomationPeer peer, AutomationEvents eventId)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (s_comInteropUnavailable) return;
        if (!s_raiseNativeEvents)
        {
            if (UiaTrace.Enabled)
                UiaTrace.Log($"RaiseAutomationEvent skipped native event event={eventId} peer={peer.GetType().Name}");
            return;
        }

        if (!UiaNativeMethods.UiaClientsAreListening()) return;
        if (!TryGetProvider(peer, out var provider) || provider == null) return;
        if (provider.Hwnd == nint.Zero) return;

        int uiaEventId = UiaConstants.MapAutomationEvent(eventId);
        if (uiaEventId == 0) return;

        try
        {
            var pProvider = provider.GetOrCreateProviderPointer();
            if (pProvider == nint.Zero) return;

            var hr = UiaNativeMethods.UiaRaiseAutomationEvent(pProvider, uiaEventId);
            UiaTrace.Log($"RaiseAutomationEvent hr=0x{hr:X8} event={eventId} uia=0x{uiaEventId:X8} provider=0x{FormatPointer(pProvider)}");
        }
        catch (Exception ex)
        {
            // Never let an exception reach the UI/layout thread (the original crash class):
            // latch the kill-switch on a genuine marshalling failure, best-effort-swallow the
            // rest — a dropped UIA event is acceptable, crashing the app is not.
            if (IsMarshallingFailure(ex)) MarkComInteropUnavailable();
            UiaTrace.Log($"RaiseAutomationEvent failed event={eventId} ex={ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static void RaisePropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (s_comInteropUnavailable) return;
        if (!s_raiseNativeEvents)
        {
            if (UiaTrace.Enabled)
                UiaTrace.Log($"RaisePropertyChanged skipped native event property={property.Name} peer={peer.GetType().Name}");
            return;
        }

        if (!UiaNativeMethods.UiaClientsAreListening()) return;
        if (!TryGetProvider(peer, out var provider) || provider == null) return;
        if (provider.Hwnd == nint.Zero) return;

        int uiaPropertyId = UiaConstants.MapAutomationProperty(property);
        if (uiaPropertyId == 0) return;

        UiaVariant vOld = default;
        UiaVariant vNew = default;
        try
        {
            var pProvider = provider.GetOrCreateProviderPointer();
            if (pProvider == nint.Zero) return;

            vOld = UiaVariant.From(oldValue);
            vNew = UiaVariant.From(newValue);
            var hr = UiaNativeMethods.UiaRaiseAutomationPropertyChangedEvent(pProvider, uiaPropertyId, vOld, vNew);
            UiaTrace.Log($"RaisePropertyChanged hr=0x{hr:X8} property={property.Name} uia=0x{uiaPropertyId:X8} provider=0x{FormatPointer(pProvider)}");
        }
        catch (Exception ex)
        {
            // Never let an exception reach the UI/layout thread (the original crash class):
            // latch the kill-switch on a genuine marshalling failure, best-effort-swallow the
            // rest — a dropped UIA event is acceptable, crashing the app is not.
            if (IsMarshallingFailure(ex)) MarkComInteropUnavailable();
            UiaTrace.Log($"RaisePropertyChanged failed property={property.Name} ex={ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            vOld.Clear();
            vNew.Clear();
        }
    }

    internal static void RaiseAsyncContentLoaded(AutomationPeer peer, AsyncContentLoadedEventArgs args)
    {
        if (!CanRaiseNativeEvent(peer, out AutomationPeerProvider? provider) || provider is null)
            return;

        try
        {
            nint pProvider = provider.GetOrCreateProviderPointer();
            if (pProvider == nint.Zero) return;

            int hr = UiaNativeMethods.UiaRaiseAsyncContentLoadedEvent(
                pProvider,
                (int)args.AsyncContentLoadedState,
                args.PercentComplete);
            UiaTrace.Log($"RaiseAsyncContentLoaded hr=0x{hr:X8} state={args.AsyncContentLoadedState} percent={args.PercentComplete} provider=0x{FormatPointer(pProvider)}");
        }
        catch (Exception ex)
        {
            if (IsMarshallingFailure(ex)) MarkComInteropUnavailable();
            UiaTrace.Log($"RaiseAsyncContentLoaded failed state={args.AsyncContentLoadedState} ex={ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static void RaiseNotification(
        AutomationPeer peer,
        AutomationNotificationKind notificationKind,
        AutomationNotificationProcessing notificationProcessing,
        string displayString,
        string activityId)
    {
        if (!CanRaiseNativeEvent(peer, out AutomationPeerProvider? provider) || provider is null)
            return;

        try
        {
            nint pProvider = provider.GetOrCreateProviderPointer();
            if (pProvider == nint.Zero) return;

            int hr = UiaNativeMethods.UiaRaiseNotificationEvent(
                pProvider,
                (int)notificationKind,
                (int)notificationProcessing,
                displayString,
                activityId);
            UiaTrace.Log($"RaiseNotification hr=0x{hr:X8} kind={notificationKind} processing={notificationProcessing} provider=0x{FormatPointer(pProvider)}");
        }
        catch (Exception ex)
        {
            if (IsMarshallingFailure(ex)) MarkComInteropUnavailable();
            UiaTrace.Log($"RaiseNotification failed kind={notificationKind} ex={ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static void RaiseFocusChanged(AutomationPeer peer)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (s_comInteropUnavailable) return;
        if (!s_raiseNativeEvents)
        {
            if (UiaTrace.Enabled)
                UiaTrace.Log($"RaiseFocusChanged skipped native event peer={peer.GetType().Name}");
            return;
        }

        if (!UiaNativeMethods.UiaClientsAreListening()) return;
        if (!TryGetProvider(peer, out var provider) || provider == null) return;
        if (provider.Hwnd == nint.Zero) return;

        try
        {
            var pProvider = provider.GetOrCreateProviderPointer();
            if (pProvider == nint.Zero) return;

            var hr = UiaNativeMethods.UiaRaiseAutomationEvent(pProvider, UiaConstants.UIA_AutomationFocusChangedEventId);
            UiaTrace.Log($"RaiseFocusChanged hr=0x{hr:X8} provider=0x{FormatPointer(pProvider)}");
        }
        catch (Exception ex)
        {
            // Never let an exception reach the UI/layout thread (the original crash class):
            // latch the kill-switch on a genuine marshalling failure, best-effort-swallow the
            // rest — a dropped UIA event is acceptable, crashing the app is not.
            if (IsMarshallingFailure(ex)) MarkComInteropUnavailable();
            UiaTrace.Log($"RaiseFocusChanged failed ex={ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static void RaiseStructureChanged(AutomationPeer peer, StructureChangeType changeType)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (s_comInteropUnavailable) return;
        if (!s_raiseNativeEvents)
        {
            if (UiaTrace.Enabled)
                UiaTrace.Log($"RaiseStructureChanged skipped native event change={changeType} peer={peer.GetType().Name}");
            return;
        }

        if (!UiaNativeMethods.UiaClientsAreListening()) return;
        if (!TryGetProvider(peer, out var provider) || provider == null) return;
        if (provider.Hwnd == nint.Zero) return;

        try
        {
            var runtimeId = provider.GetRuntimeIdArray();
            var pProvider = provider.GetOrCreateProviderPointer();
            if (pProvider == nint.Zero) return;

            var hr = UiaNativeMethods.UiaRaiseStructureChangedEvent(pProvider, (int)changeType, runtimeId, runtimeId.Length);
            UiaTrace.Log($"RaiseStructureChanged hr=0x{hr:X8} change={changeType} provider=0x{FormatPointer(pProvider)}");
        }
        catch (Exception ex)
        {
            // Never let an exception reach the UI/layout thread (the original crash class):
            // latch the kill-switch on a genuine marshalling failure, best-effort-swallow the
            // rest — a dropped UIA event is acceptable, crashing the app is not.
            if (IsMarshallingFailure(ex)) MarkComInteropUnavailable();
            UiaTrace.Log($"RaiseStructureChanged failed change={changeType} ex={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool CanRaiseNativeEvent(AutomationPeer peer, out AutomationPeerProvider? provider)
    {
        provider = null;
        if (!OperatingSystem.IsWindows() || s_comInteropUnavailable || !s_raiseNativeEvents)
            return false;
        if (!UiaNativeMethods.UiaClientsAreListening())
            return false;
        if (!TryGetProvider(peer, out provider) || provider is null || provider.Hwnd == nint.Zero)
            return false;
        return true;
    }

    private static string FormatPointer(nint pointer)
        => ((nuint)pointer).ToString("X");

    private static bool IsEnvironmentEnabled(string name, bool defaultValue = false)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        if (value is "0" or "false" or "FALSE" or "no" or "NO")
            return false;

        return value is "1" or "true" or "TRUE" or "yes" or "YES";
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

    public void OnAsyncContentLoadedRaised(AutomationPeer peer, AsyncContentLoadedEventArgs args)
        => UiaAccessibilityBridge.RaiseAsyncContentLoaded(peer, args);

    public void OnNotificationRaised(
        AutomationPeer peer,
        AutomationNotificationKind notificationKind,
        AutomationNotificationProcessing notificationProcessing,
        string displayString,
        string activityId) =>
        UiaAccessibilityBridge.RaiseNotification(
            peer,
            notificationKind,
            notificationProcessing,
            displayString,
            activityId);
}
