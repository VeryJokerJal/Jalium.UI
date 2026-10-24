using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Automation.Peers;

/// <summary>Semantic item content, independent of generated child-row hosts.</summary>
internal static class ItemContentAutomation
{
    internal static List<AutomationPeer> Children(UIElement owner, object? content)
    {
        List<AutomationPeer> result = [];
        if (content is UIElement element && (!owner.IsArrangeValid || IsWithin(element, owner)))
        {
            Collect(element, result);
            return result;
        }

        // Content/header templates realize their controls below a presenter,
        // while the item's Content/Header remains the original data object.
        bool found = false;
        Stack<UIElement> pending = new();
        PushChildren(owner, pending);
        HashSet<UIElement> visited = [];
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current) || current is ItemsPresenter) continue;
            if (current is ContentPresenter presenter && ReferenceEquals(presenter.Content, content))
            {
                found = true;
                // Default string presentation already supplies the row's Name.
                if (presenter.ContentTemplate != null || presenter.ContentTemplateSelector != null
                    || presenter.GetType() != typeof(ContentPresenter))
                    CollectChildren(presenter, result);
                continue;
            }
            PushChildren(current, pending);
        }
        // A custom row template may put its own action directly in the visual
        // tree. It should not require a replacement automation peer.
        if (!found) CollectChildren(owner, result);
        return result;
    }

    private static bool IsWithin(UIElement element, UIElement owner)
    {
        HashSet<Visual> visited = [];
        for (Visual? current = element; current != null && visited.Add(current); current = current.VisualParent)
            if (ReferenceEquals(current, owner)) return true;
        return false;
    }

    private static void CollectChildren(UIElement owner, List<AutomationPeer> result)
    {
        Stack<UIElement> pending = new(); PushChildren(owner, pending);
        Collect(pending, result);
    }

    private static void Collect(UIElement element, List<AutomationPeer> result)
    { Stack<UIElement> pending = new(); pending.Push(element); Collect(pending, result); }

    private static void Collect(Stack<UIElement> pending, List<AutomationPeer> result)
    {
        HashSet<UIElement> visited = [];
        HashSet<AutomationPeer> peers = new(result);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current) || current is ItemsPresenter) continue;
            if (current.GetAutomationPeer() is { } peer && peer.IsControlElement())
            {
                if (peers.Add(peer)) result.Add(peer);
                continue;
            }
            PushChildren(current, pending);
        }
    }

    private static void PushChildren(UIElement owner, Stack<UIElement> pending)
    {
        for (int i = owner.VisualChildrenCount - 1; i >= 0; i--)
            if (owner.GetVisualChild(i) is UIElement child) pending.Push(child);
    }
}
