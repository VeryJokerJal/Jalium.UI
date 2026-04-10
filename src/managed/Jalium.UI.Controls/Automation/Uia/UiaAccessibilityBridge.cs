using System.Runtime.CompilerServices;
using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation.Uia;

/// <summary>
/// Manages the bridge between AutomationPeer and Windows UI Automation.
/// Follows WPF pattern: pass managed objects directly, let CLR handle COM marshaling.
/// </summary>
internal static class UiaAccessibilityBridge
{
    private static readonly ConditionalWeakTable<AutomationPeer, AutomationPeerProvider> s_providers = new();

    internal static AutomationPeerProvider GetOrCreateProvider(AutomationPeer peer, nint hwnd)
    {
        return s_providers.GetValue(peer, p => new AutomationPeerProvider(p, hwnd));
    }

    internal static bool TryGetProvider(AutomationPeer peer, out AutomationPeerProvider? provider)
    {
        return s_providers.TryGetValue(peer, out provider);
    }

    internal static void RaiseAutomationEvent(AutomationPeer peer, AutomationEvents eventId)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!UiaNativeMethods.UiaClientsAreListening()) return;
        if (!TryGetProvider(peer, out var provider) || provider == null) return;

        int uiaEventId = UiaConstants.MapAutomationEvent(eventId);
        if (uiaEventId != 0)
            UiaNativeMethods.UiaRaiseAutomationEvent(provider, uiaEventId);
    }

    internal static void RaisePropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!UiaNativeMethods.UiaClientsAreListening()) return;
        if (!TryGetProvider(peer, out var provider) || provider == null) return;

        int uiaPropertyId = UiaConstants.MapAutomationProperty(property);
        if (uiaPropertyId != 0)
            UiaNativeMethods.UiaRaiseAutomationPropertyChangedEvent(provider, uiaPropertyId, oldValue!, newValue!);
    }

    internal static void RaiseFocusChanged(AutomationPeer peer)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!UiaNativeMethods.UiaClientsAreListening()) return;
        if (!TryGetProvider(peer, out var provider) || provider == null) return;

        UiaNativeMethods.UiaRaiseAutomationEvent(provider, UiaConstants.UIA_AutomationFocusChangedEventId);
    }

    internal static void RaiseStructureChanged(AutomationPeer peer, StructureChangeType changeType)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!UiaNativeMethods.UiaClientsAreListening()) return;
        if (!TryGetProvider(peer, out var provider) || provider == null) return;

        var runtimeId = provider.GetRuntimeIdArray();
        UiaNativeMethods.UiaRaiseStructureChangedEvent(provider, (int)changeType, runtimeId, runtimeId.Length);
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
