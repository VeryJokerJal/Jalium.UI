using System.Collections.Specialized;
using Jalium.UI.Controls;

namespace Jalium.UI.Automation.Peers;

/// <summary>Defines the automation contract supplied by a <see cref="ViewBase"/>.</summary>
public interface IViewAutomationPeer
{
    AutomationControlType GetAutomationControlType();
    object? GetPattern(PatternInterface patternInterface);
    List<AutomationPeer> GetChildren(List<AutomationPeer> children);
    AutomationPeer? CreateItemAutomationPeer(object item);
    void ItemsChanged(NotifyCollectionChangedEventArgs e);
    void ViewDetached();
}

/// <summary>Supplies Grid and Table automation semantics for a <see cref="GridView"/>.</summary>
public class GridViewAutomationPeer : IViewAutomationPeer
{
    private readonly GridView _owner;
    private readonly ListView _listView;
    private bool _isDetached;

    public GridViewAutomationPeer(GridView owner, ListView listView)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _listView = listView ?? throw new ArgumentNullException(nameof(listView));
        owner.Columns.CollectionChanged += OnColumnsChanged;
    }

    public int RowCount => _listView.Items.Count;

    public int ColumnCount => _owner.Columns.Count;

    AutomationControlType IViewAutomationPeer.GetAutomationControlType() =>
        AutomationControlType.DataGrid;

    object? IViewAutomationPeer.GetPattern(PatternInterface patternInterface) =>
        patternInterface is PatternInterface.Grid or PatternInterface.Table ? this : null;

    List<AutomationPeer> IViewAutomationPeer.GetChildren(List<AutomationPeer> children) =>
        children;

    AutomationPeer? IViewAutomationPeer.CreateItemAutomationPeer(object item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item is UIElement element)
        {
            return element.GetAutomationPeer();
        }

        int index = _listView.Items.IndexOf(item);
        return index >= 0 && _listView.ItemContainerGenerator.ContainerFromIndex(index) is UIElement container
            ? container.GetAutomationPeer()
            : null;
    }

    void IViewAutomationPeer.ItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _ = e.Action;
    }

    void IViewAutomationPeer.ViewDetached()
    {
        if (_isDetached)
        {
            return;
        }

        _owner.Columns.CollectionChanged -= OnColumnsChanged;
        _isDetached = true;
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = e.Action;
    }
}
