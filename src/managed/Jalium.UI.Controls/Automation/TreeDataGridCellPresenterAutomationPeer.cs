using Jalium.UI.Automation.Provider;

namespace Jalium.UI.Automation.Peers;

/// <summary>Exposes the expansion state of an indented tree-grid cell.</summary>
public sealed class TreeDataGridCellPresenterAutomationPeer : FrameworkElementAutomationPeer, IExpandCollapseProvider
{
    public TreeDataGridCellPresenterAutomationPeer(Controls.TreeDataGridCellPresenter owner) : base(owner) { }
    private Controls.TreeDataGridCellPresenter Presenter => (Controls.TreeDataGridCellPresenter)Owner;
    protected override string GetClassNameCore() => nameof(Controls.TreeDataGridCellPresenter);
    protected override string GetNameCore() => Presenter.Content is Controls.TextBlock text ? text.Text ?? string.Empty : base.GetNameCore();
    protected override object? GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPatternCore(patternInterface);
    public ExpandCollapseState ExpandCollapseState => !Presenter.HasChildren ? ExpandCollapseState.LeafNode :
        Presenter.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
    public void Expand() => SetExpanded(true);
    public void Collapse() => SetExpanded(false);
    private void SetExpanded(bool expanded)
    {
        if (!IsEnabled()) throw new InvalidOperationException("The tree cell is disabled.");
        if (!Presenter.HasChildren) throw new InvalidOperationException("The tree cell has no children.");
        Presenter.SetCurrentValue(Controls.TreeDataGridCellPresenter.IsExpandedProperty, expanded);
    }
}
