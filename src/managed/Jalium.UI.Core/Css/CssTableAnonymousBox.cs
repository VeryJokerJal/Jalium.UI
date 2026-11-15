using Jalium.UI.Automation.Peers;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>A layout-only sibling helper; original controls keep their actual parents.</summary>
internal sealed class CssTableAnonymousBox : FrameworkElement, ICssAnonymousBox
{
    internal CssTableAnonymousBox(FrameworkElement owner, UIElement[] items, bool table)
    {
        Owner = owner; Items = items; CssDisplayMode = table ? CssDisplayMode.Table : CssDisplayMode.FlowRoot;
        IsHitTestVisible = false; Focusable = false;
        owner.AttachCssGeneratedChild(this);
    }
    public FrameworkElement Owner { get; }
    internal UIElement[] Items { get; }
    internal Point AllocationOrigin { get; set; }
    public double BaselineOffset(double height, bool last) => CssDisplayMode == CssDisplayMode.Table
        ? CssDisplayLayout.TableFor(this).Baseline : last ? CssDisplayLayout.FlowFor(this).LastBaseline : CssDisplayLayout.FlowFor(this).FirstBaseline;
    protected override bool SupportsCssContentLayout => true;
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
    internal void Detach() { CssTableLayout.ReleaseFlowTables(this); CssDisplayLayout.TableFor(this).Clear(); Owner.DetachCssGeneratedChild(this); }
}
